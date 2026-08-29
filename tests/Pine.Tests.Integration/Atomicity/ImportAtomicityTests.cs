using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pine.Application.Transactions.Commands;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Importing;
using Pine.Core.Transactions;
using Xunit;

namespace Pine.Tests.Integration.Atomicity;

/// <summary>
/// A batch import is the write path with the most rows and therefore the most ways to end up half
/// applied. Failing on row 3 of 4 must import nothing at all.
/// </summary>
public class ImportAtomicityTests
{
    private const decimal OpeningBalance = 2000m;

    private static readonly decimal[] RowAmounts = [-10m, -20m, -30m, -40m];

    [Fact]
    public async Task Import_WhenEveryRowSucceeds_CommitsEveryRowAndTheFinalBalance()
    {
        await using var host = await AtomicityHost.CreateAsync();
        var account = await host.SeedAccountAsync("Imported", OpeningBalance);

        using var file = new MemoryStream();
        var imported = await host.Mediator.Send(new ImportTransactionsCommand(
            account, file, new StubImporter(RowAmounts), NewProfile()));

        // The command now answers with a per-row report rather than a bare list; this file is about
        // atomicity, so it asserts only that every row survived. Which disposition each row received
        // is the subject of Pine.Tests.Integration.Importing.ImportDeduplicationTests.
        imported.Imported.Should().HaveCount(RowAmounts.Length);
        (await host.CountTransactionsAsync(account)).Should().Be(RowAmounts.Length);
        (await host.ReadBalanceAsync(account)).Should().Be(OpeningBalance - 100m);

        host.UnitOfWorkLog.Calls.Should().Equal(
            UnitOfWorkCall.Begin, UnitOfWorkCall.SaveChanges, UnitOfWorkCall.Commit);
    }

    /// <summary>
    /// Rows 1 and 2 are flushed into the database inside the boundary, then row 3 fails. Because
    /// the whole batch shares one transaction, the two rows already written vanish with it.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Import_WhenRowThreeFails_ImportsNothingAtAll()
    {
        await using var host = await AtomicityHost.CreateAsync(FaultInjection.Install);
        var account = await host.SeedAccountAsync("Imported", OpeningBalance);

        var rowsInsideTransaction = -1;

        host.ArmFaults(async context =>
        {
            if (context.Repository != FaultInjection.TransactionRepository)
            {
                return;
            }

            if (context.RepositorySaveNumber <= 2)
            {
                await context.UnitOfWork.SaveChangesAsync(context.CancellationToken);
                return;
            }

            rowsInsideTransaction = await context.DbContext.Transactions
                .AsNoTracking()
                .CountAsync(context.CancellationToken);
            throw new InvalidOperationException("Simulated failure importing row 3.");
        });

        using var file = new MemoryStream();
        var act = async () => await host.Mediator.Send(new ImportTransactionsCommand(
            account, file, new StubImporter(RowAmounts), NewProfile()));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Simulated failure importing row 3.");

        rowsInsideTransaction.Should().Be(2, "the first two rows really were written before row 3 failed");

        (await host.CountTransactionsAsync(account)).Should().Be(0, "a partial import is not an import");
        (await host.QueryAsync(c => c.Transactions.AsNoTracking().CountAsync())).Should().Be(0);
        (await host.ReadBalanceAsync(account)).Should().Be(OpeningBalance);

        host.UnitOfWorkLog.Calls.Should().Equal(
            UnitOfWorkCall.Begin,
            UnitOfWorkCall.SaveChanges,
            UnitOfWorkCall.SaveChanges,
            UnitOfWorkCall.Rollback);
        host.UnitOfWorkLog.Calls.Should().NotContain(UnitOfWorkCall.Commit);
    }

    private static ImportProfile NewProfile() => ImportProfile.Create("integration", TimeProvider.System);

    /// <summary>
    /// Parses nothing and simply yields the rows the test asked for.
    /// </summary>
    /// <remarks>
    /// <c>ImportTransactionsCommand</c> takes its <see cref="IImporter"/> as a request parameter, so
    /// supplying one here is the command's own extension point rather than a testing back door. It
    /// assigns no import key, so every row is imported unchecked (TD-03) and de-duplication cannot
    /// remove a row and quietly weaken the atomicity assertions below.
    /// </remarks>
    private sealed class StubImporter : IImporter
    {
        private readonly decimal[] _amounts;

        public StubImporter(decimal[] amounts) => _amounts = amounts;

        public Task<IReadOnlyList<Transaction>> ImportAsync(
            Account targetAccount, Stream fileStream, ImportProfile profile, CancellationToken cancellationToken = default)
        {
            var currency = new CurrencyCode("USD");
            var date = DateTimeOffset.UtcNow.AddDays(-2);
            var rows = new List<Transaction>(_amounts.Length);

            foreach (var amount in _amounts)
            {
                rows.Add(Transaction.Create(
                    targetAccount.Id,
                    date,
                    $"Row {amount}",
                    new Money(amount, currency),
                    TransactionType.Expense,
                    TimeProvider.System));
            }

            return Task.FromResult<IReadOnlyList<Transaction>>(rows);
        }
    }
}
