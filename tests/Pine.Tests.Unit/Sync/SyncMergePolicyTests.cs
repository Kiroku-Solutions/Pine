using System;
using System.Collections.Generic;
using FluentAssertions;
using Pine.Core.Budgets;
using Pine.Core.Sync;
using Pine.Infrastructure.Data;
using Pine.Infrastructure.Sync;
using Xunit;

namespace Pine.Tests.Unit.Sync;

/// <summary>
/// `14-Synchronization` §7 as <see cref="SyncMergePolicy"/> encodes it (post-audit plan item 3.1b): which rows both
/// vaults changed are settled by recency, and which are left for a person.
/// </summary>
public class SyncMergePolicyTests
{
    private static readonly Guid DeviceA = Guid.Parse("AAAAAAAA-0000-4000-8000-000000000000");
    private static readonly Guid DeviceB = Guid.Parse("BBBBBBBB-0000-4000-8000-000000000000");
    private static readonly Guid VaultA = Guid.Parse("0AAAAAAA-0000-4000-8000-000000000000");
    private static readonly Guid VaultB = Guid.Parse("0BBBBBBB-0000-4000-8000-000000000000");

    private static SyncRow Row(params (string Column, object? Value)[] columns)
    {
        var pairs = new List<KeyValuePair<string, object?>>();
        foreach (var (column, value) in columns)
        {
            pairs.Add(new(column, value));
        }

        return new SyncRow(pairs);
    }

    [Fact]
    public void Stamps_AreOrderedByClockFirst_ThenByDevice()
    {
        SyncMergePolicy.CompareStamps(2, DeviceA, 1, DeviceB).Should().BePositive("a later clock wins whatever the device");
        SyncMergePolicy.CompareStamps(1, DeviceB, 2, DeviceA).Should().BeNegative();
        SyncMergePolicy.CompareStamps(5, DeviceB, 5, DeviceA).Should().BePositive("a tie on the clock is broken by the device");
        SyncMergePolicy.CompareStamps(5, DeviceA, 5, DeviceA).Should().Be(0);
    }

    [Fact]
    public void ATieOnClockAndDevice_IsBrokenByVault_TheSameWayRoundOnBothSides()
    {
        var a = (5L, DeviceA, VaultA);
        var b = (5L, DeviceA, VaultB);

        SyncMergePolicy.RemoteIsLater(a, b).Should().BeTrue();
        SyncMergePolicy.RemoteIsLater(b, a).Should().BeFalse("both vaults must reach the same answer, or they never converge");
        SyncMergePolicy.RemoteIsLater((6L, DeviceA, VaultB), (5L, DeviceB, VaultA)).Should().BeFalse();
    }

    [Fact]
    public void BookkeepingAndDerivedColumns_AreNeverADifference()
    {
        var local = Row(("Id", "X"), ("Name", "Current"), ("Balance_Amount", 100L), (PineDbContext.ClockColumnName, 3L),
            (PineDbContext.DeviceColumnName, "A"), (PineDbContext.LocalClockColumnName, 3L), (PineDbContext.ConcurrencyTokenName, "v1"),
            (PineDbContext.CreatedAtColumnName, "t1"), (PineDbContext.UpdatedAtColumnName, "t1"));
        var remote = Row(("Id", "Y"), ("Name", "Current"), ("Balance_Amount", 250L), (PineDbContext.ClockColumnName, 9L),
            (PineDbContext.DeviceColumnName, "B"), (PineDbContext.LocalClockColumnName, 1L), (PineDbContext.ConcurrencyTokenName, "v2"),
            (PineDbContext.CreatedAtColumnName, "t2"), (PineDbContext.UpdatedAtColumnName, "t2"));

        SyncMergePolicy.DifferingColumns("Accounts", local, remote).Should().BeEmpty(
            "the id is matched separately, the balance is re-derived, and the rest describe the row in one vault");
        SyncMergePolicy.IsDerived("Accounts", "Balance_Amount").Should().BeTrue();
        SyncMergePolicy.IsDerived("Transactions", "Balance_Amount").Should().BeFalse();
    }

    [Fact]
    public void AColumnPresentOnOneSideOnly_IsADifference()
    {
        SyncMergePolicy.DifferingColumns("Settings", Row(("Value", "a")), Row(("Value", "a"), ("New", null)))
            .Should().Equal("New");
        SyncMergePolicy.DifferingColumns("Settings", Row(("Value", new byte[] { 1 })), Row(("Value", new byte[] { 1 })))
            .Should().BeEmpty("bytes are compared by content");
        SyncMergePolicy.DifferingColumns("Settings", Row(("Value", 1L)), Row(("Value", "1")))
            .Should().Equal("Value");
    }

    [Theory]
    [InlineData("Settings", "Value")]
    [InlineData("Accounts", "Name")]
    [InlineData("Accounts", "OpeningBalance_Amount")]
    public void ALastWriterWinsColumn_GoesToTheLaterStamp(string table, string column)
    {
        SyncMergePolicy.DecideBothChanged(table, [column], childrenDiffer: false, (3, DeviceA, VaultA), (4, DeviceB, VaultB))
            .Should().Be(SyncBothChangedOutcome.TakeRemote);
        SyncMergePolicy.DecideBothChanged(table, [column], childrenDiffer: false, (4, DeviceA, VaultA), (3, DeviceB, VaultB))
            .Should().Be(SyncBothChangedOutcome.KeepLocal);
    }

    [Theory]
    [InlineData("Transactions", "TotalAmount_Amount")]
    [InlineData("Transactions", "Payee")]
    [InlineData("Transactions", "Date")]
    [InlineData("Budgets", "LimitAmount")]
    [InlineData("Rules", "Title")]
    [InlineData("Accounts", "AColumnNobodyClassified")]
    [InlineData("Accounts", "Balance_Currency")]
    [InlineData("Accounts", "OpeningBalance_Currency")]
    public void AnyOtherColumn_IsAConflict_WhicheverStampIsLater(string table, string column)
    {
        SyncMergePolicy.DecideBothChanged(table, [column], childrenDiffer: false, (3, DeviceA, VaultA), (4, DeviceB, VaultB))
            .Should().Be(SyncBothChangedOutcome.Conflict, "§7 rejects 'everything is LWW'");
        SyncMergePolicy.DecideBothChanged(table, ["Name", column], childrenDiffer: false, (4, DeviceA, VaultA), (3, DeviceB, VaultB))
            .Should().Be(SyncBothChangedOutcome.Conflict, "one non-LWW difference makes the whole row a question");
    }

    [Fact]
    public void DifferentChildren_AreAConflict_AndNoDifferenceKeepsTheLocalRow()
    {
        SyncMergePolicy.DecideBothChanged("Settings", ["Value"], childrenDiffer: true, (3, DeviceA, VaultA), (4, DeviceB, VaultB))
            .Should().Be(SyncBothChangedOutcome.Conflict, "splits are not set-unioned: two valid split sets need not union to one");
        SyncMergePolicy.DecideBothChanged("Transactions", [], childrenDiffer: false, (3, DeviceA, VaultA), (4, DeviceB, VaultB))
            .Should().Be(SyncBothChangedOutcome.KeepLocal);
    }

    [Fact]
    public void Children_AreComparedByContent_NotByTheirIdsOrTheirOwner()
    {
        var owners = new Dictionary<string, string> { ["TransactionSplits"] = "TransactionId" };
        IReadOnlyDictionary<string, IReadOnlyList<SyncRow>> Splits(string owner, params (string Id, string Category)[] rows)
        {
            var list = new List<SyncRow>();
            foreach (var (id, category) in rows)
            {
                list.Add(Row(("Id", id), ("TransactionId", owner), ("Category", category), ("Amount_Amount", 10L)));
            }

            return new Dictionary<string, IReadOnlyList<SyncRow>> { ["TransactionSplits"] = list };
        }

        SyncMergePolicy.ChildrenEqual(Splits("T1", ("a", "Food"), ("b", "Fuel")), Splits("T2", ("y", "Fuel"), ("x", "Food")), owners)
            .Should().BeTrue();
        SyncMergePolicy.ChildrenEqual(Splits("T1", ("a", "Food")), Splits("T1", ("a", "Fuel")), owners).Should().BeFalse();
        SyncMergePolicy.ChildrenEqual(Splits("T1", ("a", "Food")), new Dictionary<string, IReadOnlyList<SyncRow>>(), owners)
            .Should().BeFalse("a removed split is a difference");
        SyncMergePolicy.ChildrenEqual(
                new Dictionary<string, IReadOnlyList<SyncRow>> { ["Other"] = [Row(("Id", "1"), ("V", null))] },
                new Dictionary<string, IReadOnlyList<SyncRow>> { ["Other"] = [Row(("Id", "2"), ("V", null))] },
                owners)
            .Should().BeTrue("a child table with no known owner column still ignores the ids");
        SyncMergePolicy.ChildrenEqual(
                new Dictionary<string, IReadOnlyList<SyncRow>> { ["Other"] = [Row(("V", new byte[] { 1 }))] },
                new Dictionary<string, IReadOnlyList<SyncRow>> { ["Other"] = [Row(("V", new byte[] { 2 }))] },
                owners)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData("TelemetryConsent", null, "true", true)]
    [InlineData("TelemetryConsent", "false", "yes", true)]
    [InlineData("crash.consent", null, "granted", true)]
    [InlineData("TelemetryConsent", null, "false", false)]
    [InlineData("TelemetryConsent", "true", "off", false)]
    [InlineData("TelemetryConsent", "true", "true", false)]
    [InlineData("TelemetryConsent", null, " Declined ", false)]
    [InlineData("Theme", null, "true", false)]
    public void AConsent_IsNeverGrantedByAPeer(string key, string? local, string? remote, bool grants)
    {
        SyncMergePolicy.WouldGrantConsent(key, local, remote).Should().Be(grants);
    }

    [Fact]
    public void TheLastWriterWinsAllowList_IsExactlyWhatSection7Allows()
    {
        SyncMergePolicy.LastWriterWinsColumns.Keys.Should().BeEquivalentTo(["Accounts", "Settings"],
            "widening this list is a decision about which edits may silently overwrite others, and deserves a failing test");
        SyncMergePolicy.LastWriterWinsColumns["Settings"].Should().BeEquivalentTo(["Value"]);
        SyncMergePolicy.LastWriterWinsColumns["Accounts"].Should().BeEquivalentTo(["Name", "OpeningBalance_Amount", "UpdatedAt"]);
        SyncMergePolicy.LastWriterWinsColumns["Accounts"].Should().NotContain(["Balance_Currency", "OpeningBalance_Currency"],
            "an account's currency is immutable, never last-writer-wins");
    }

    [Theory]
    [InlineData("USD", "EUR", "USD", "USD", true)]
    [InlineData("USD", "USD", "USD", "EUR", true)]
    [InlineData("USD", "USD", "USD", "USD", false)]
    [InlineData("USD", "USD", null, "USD", false)]
    [InlineData("USD", "USD", "USD", null, false)]
    public void ADifferentNonNullAccountCurrency_IsDetected(string localBalance, string remoteBalance, string? localOpening, string? remoteOpening, bool differs)
    {
        var local = Row(("Balance_Currency", localBalance), ("OpeningBalance_Currency", localOpening));
        var remote = Row(("Balance_Currency", remoteBalance), ("OpeningBalance_Currency", remoteOpening));

        SyncMergePolicy.ImmutableColumnsDiffer("Accounts", local, remote).Should().Be(differs);
        SyncMergePolicy.ImmutableColumnsDiffer("Settings", local, remote).Should().BeFalse("only an account's currency is immutable");
    }

    [Fact]
    public void TheRowOnlyNaturalKeyFunctions_AgreeWithTheAggregateOnes()
    {
        var account = Guid.NewGuid();
        var transaction = Guid.NewGuid();

        NaturalKeys.SettingKey("Theme").Should().Be("setting|Theme");
        NaturalKeys.TransactionKey(account, "R-1").Should().Be($"transaction|{account:D}|R-1");
        NaturalKeys.TransactionKey(account, "  ").Should().BeNull();
        NaturalKeys.TransactionKey(account, null).Should().BeNull();
        NaturalKeys.AttachmentKey(transaction, "abcdef").Should().Be(NaturalKeys.AttachmentKey(transaction, "ABCDEF"));

        var nullKey = () => NaturalKeys.SettingKey(null!);
        nullKey.Should().Throw<ArgumentNullException>();
        var nullHash = () => NaturalKeys.AttachmentKey(transaction, null!);
        nullHash.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AStoredRow_HasTheSameNaturalKeyAsTheAggregateItWasWrittenFrom()
    {
        var start = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(-5));
        var budget = Budget.Create("Holiday", new Pine.Core.Common.ValueObjects.Money(100m, new("USD")), BudgetPeriod.Custom, start, start.AddMonths(1));

        var stored = Row(("Category", "Holiday"), ("LimitCurrency", "USD"), ("Period", "Custom"),
            ("StartDate", "2026-07-01 00:00:00-05:00"), ("EndDate", "2026-08-01 00:00:00-05:00"));

        SyncSchema.NaturalKeyOf("Budgets", stored).Should().Be(NaturalKeys.For(budget));
        SyncSchema.NaturalKeyOf("Budgets", stored.With([new("StartDate", null), new("EndDate", null)]))
            .Should().Be(NaturalKeys.BudgetKey("Holiday", "USD", BudgetPeriod.Custom, null, null));
        SyncSchema.NaturalKeyOf("Settings", Row(("Key", "Theme"))).Should().Be("setting|Theme");
        SyncSchema.NaturalKeyOf("Settings", Row(("Key", null))).Should().BeNull();
        SyncSchema.NaturalKeyOf("Accounts", Row(("Name", "Current"))).Should().BeNull("an account has no natural key (TD-146)");
        SyncSchema.NaturalKeyOf("Transactions", Row(("AccountId", SyncRow.GuidText(Guid.Empty)), ("ImportKey", null))).Should().BeNull();
        SyncSchema.NaturalKeyOf("Attachments", Row(("TransactionId", SyncRow.GuidText(Guid.Empty)), ("Sha256", "ab")))
            .Should().Be(NaturalKeys.AttachmentKey(Guid.Empty, "AB"));
        SyncSchema.NaturalKeyOf("Attachments", Row(("TransactionId", SyncRow.GuidText(Guid.Empty)))).Should().BeNull();

        SyncSchema.NaturalKeyNarrowingColumn("Budgets").Should().Be("LimitCurrency");
        SyncSchema.NaturalKeyNarrowingColumn("Transactions").Should().Be("ImportKey");
        SyncSchema.NaturalKeyNarrowingColumn("Attachments").Should().Be("TransactionId");
        SyncSchema.NaturalKeyNarrowingColumn("Settings").Should().Be("Key");
        SyncSchema.NaturalKeyNarrowingColumn("Rules").Should().BeNull();
    }
}
