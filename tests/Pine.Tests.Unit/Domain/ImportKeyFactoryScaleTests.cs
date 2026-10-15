using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using Pine.Application.Transactions.Commands;
using Pine.Core.Accounts;
using Pine.Core.Ai;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Importing;
using Pine.Core.Transactions;
using Xunit;

namespace Pine.Tests.Unit.Domain;

/// <summary>TD-102: the derived import key must not depend on the decimal's scale.</summary>
public class ImportKeyFactoryScaleTests
{
    private static readonly DateTimeOffset Date = new(2026, 3, 5, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("12.5", "12.50")]
    [InlineData("12.5", "12.5000")]
    [InlineData("-24.5", "-24.50")]
    [InlineData("100", "100.00")]
    [InlineData("0", "0.00")]
    [InlineData("0", "-0.00")]
    public void Equal_amounts_with_different_scales_derive_the_same_key(string a, string b)
    {
        var first = ImportKeyFactory.Derive(Date, decimal.Parse(a, CultureInfo.InvariantCulture), "USD", "ACME", "s", 0);
        var second = ImportKeyFactory.Derive(Date, decimal.Parse(b, CultureInfo.InvariantCulture), "USD", "ACME", "s", 0);

        second.Should().Be(first);
    }

    [Fact]
    public void Different_amounts_still_derive_different_keys()
    {
        ImportKeyFactory.Derive(Date, 12.5m, "USD", "ACME", "s", 0)
            .Should().NotBe(ImportKeyFactory.Derive(Date, 12.05m, "USD", "ACME", "s", 0));
        ImportKeyFactory.Derive(Date, 12.5m, "USD", "ACME", "s", 0)
            .Should().NotBe(ImportKeyFactory.Derive(Date, -12.5m, "USD", "ACME", "s", 0));
    }

    [Fact]
    public void The_key_uses_the_h2_scheme()
    {
        var key = ImportKeyFactory.Derive(Date, 12.50m, "USD", "ACME", "s", 0);

        key.Should().StartWith("h2:");
        key.Should().Be(ImportKeyFactory.Derive(Date, 12.5m, "USD", "ACME", "s", 0));
        key.Length.Should().Be(3 + 64);
    }

    [Fact]
    public void The_legacy_key_keeps_the_h1_prefix_and_its_scale_dependence()
    {
        var a = ImportKeyFactory.DeriveLegacyV1(Date, 12.5m, "USD", "ACME", "s", 0);
        var b = ImportKeyFactory.DeriveLegacyV1(Date, 12.50m, "USD", "ACME", "s", 0);

        a.Should().StartWith("h1:");
        a.Should().NotBe(b, "this documents the defect the h2 scheme removed; the legacy form exists only to probe old keys");
        a.Should().NotBe(ImportKeyFactory.Derive(Date, 12.5m, "USD", "ACME", "s", 0));
    }

    [Fact]
    public async Task A_row_whose_legacy_key_is_already_in_the_account_is_skipped()
    {
        var tx = Transaction.Create(Guid.NewGuid(), Date, "ACME", new Money(-12.50m, new CurrencyCode("USD")),
            TransactionType.Expense, TimeProvider.System);
        var legacy = ImportKeyFactory.DeriveLegacyV1(Date, -12.50m, "USD", "ACME", "s", 0);
        tx.AssignImportKey(ImportKeyFactory.Derive(Date, -12.50m, "USD", "ACME", "s", 0), legacy);

        var account = Account.Create("Main", AccountType.Bank, new Money(100m, new CurrencyCode("USD")), TimeProvider.System);
        var importer = Substitute.For<IImporter>();
        importer.ImportAsync(Arg.Any<Account>(), Arg.Any<Stream>(), Arg.Any<ImportProfile>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Transaction>>(new[] { tx }));
        var accounts = Substitute.For<IAccountRepository>();
        accounts.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(account);
        var transactions = Substitute.For<ITransactionRepository>();
        transactions.GetExistingImportKeysAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyCollection<string>>(
                ((IReadOnlyCollection<string>)call[1]).Where(k => k == legacy).ToArray()));
        var ai = Substitute.For<IAIProvider>();
        var rules = Substitute.For<Pine.Core.Rules.IRuleRepository>();
        rules.GetEnabledAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Pine.Core.Rules.Rule>>(Array.Empty<Pine.Core.Rules.Rule>()));
        var handler = new ImportTransactionsCommandHandler(transactions, accounts, TimeProvider.System, ai, rules,
            Microsoft.Extensions.Options.Options.Create(new Pine.Core.Configuration.PineAiOptions()));

        var result = await handler.Handle(
            new ImportTransactionsCommand(account.Id, new MemoryStream(), importer, ImportProfile.Create("s", TimeProvider.System)),
            CancellationToken.None);

        result.Rows.Should().ContainSingle().Which.Disposition.Should().Be(ImportRowDisposition.SkippedAlreadyInAccount);
        await transactions.DidNotReceive().SaveAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>());
    }
}
