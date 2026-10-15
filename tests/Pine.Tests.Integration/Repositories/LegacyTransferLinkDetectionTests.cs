using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pine.Application.Transactions.Queries;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Transactions;
using Pine.Infrastructure.Data;
using Pine.Tests.Integration.Data;
using Xunit;

namespace Pine.Tests.Integration.Repositories;

/// <summary>
/// The legacy-transfer detector against a real vault (TD-124): it must find a one-way link produced
/// the way old data was, and it must write nothing.
/// </summary>
public class LegacyTransferLinkDetectionTests
{
    [Fact]
    public async Task AOneWayLinkIsFound_AHealthyPairIsNot_AndNothingIsWritten()
    {
        using var vault = new TempVault();
        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        await context.Database.MigrateAsync(CancellationToken.None);

        var usd = new CurrencyCode("USD");
        var from = Account.Create("From", AccountType.Bank, new Money(0m, usd), TimeProvider.System);
        var to = Account.Create("To", AccountType.Bank, new Money(0m, usd), TimeProvider.System);
        context.Accounts.Add(from);
        context.Accounts.Add(to);

        var healthy = Transaction.CreateTransferPair(from.Id, to.Id, DateTimeOffset.UtcNow.AddDays(-1), "out", "in", new Money(5m, usd), TimeProvider.System);
        var legacy = Transaction.CreateTransferPair(from.Id, to.Id, DateTimeOffset.UtcNow.AddDays(-2), "out", "in", new Money(7m, usd), TimeProvider.System);
        context.Transactions.AddRange(healthy.Source, healthy.Destination, legacy.Source, legacy.Destination);
        await context.SaveChangesAsync(CancellationToken.None);

        // Reproduce data written before both directions were linked: the receiving leg has no link back.
        using (var connection = vault.OpenRaw(vault.DatabasePath, vault.Key))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"UPDATE ""Transactions"" SET ""LinkedTransactionId"" = NULL WHERE ""Id"" = $id;";
            command.Parameters.AddWithValue("$id", legacy.Destination.Id.ToString().ToUpperInvariant());
            command.ExecuteNonQuery().Should().Be(1, "the fixture must really have produced the one-way shape");
        }

        await using var readContext = new PineDbContext(vault.DatabasePath, vault.Key);
        var handler = new FindLegacyTransferLinksQueryHandler(new SqliteTransactionRepository(readContext));

        var report = await handler.Handle(new FindLegacyTransferLinksQuery(), CancellationToken.None);

        report.LinkedRowCount.Should().Be(3);
        var finding = report.Findings.Should().ContainSingle().Subject;
        finding.TransactionId.Should().Be(legacy.Source.Id);
        finding.LinkedTransactionId.Should().Be(legacy.Destination.Id);
        finding.Problem.Should().Be(TransferLinkProblem.PartnerNotLinkedBack);

        readContext.ChangeTracker.HasChanges().Should().BeFalse("the detector is read-only");
        (await readContext.Transactions.CountAsync(CancellationToken.None)).Should().Be(4);
    }
}
