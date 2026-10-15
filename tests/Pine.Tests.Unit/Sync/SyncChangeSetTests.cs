using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Pine.Infrastructure.Data;
using Pine.Infrastructure.Sync;
using Xunit;

namespace Pine.Tests.Unit.Sync;

/// <summary>
/// The <c>pine.sync.v1</c> document and the schema a merge reads from the model (post-audit plan item 3.1b).
/// </summary>
public class SyncChangeSetTests
{
    private static readonly Guid Source = Guid.Parse("5A5A5A5A-0000-4000-8000-000000000001");
    private static readonly Guid Target = Guid.Parse("7B7B7B7B-0000-4000-8000-000000000002");
    private static readonly Guid Device = Guid.Parse("D1D1D1D1-0000-4000-8000-000000000003");

    private static SyncRow Row(params (string Column, object? Value)[] columns) =>
        new(columns.Select(c => new KeyValuePair<string, object?>(c.Column, c.Value)));

    private static SyncChangeSet Sample()
    {
        var id = Guid.Parse("11111111-0000-4000-8000-000000000000");
        return new SyncChangeSet(
            SyncChangeSet.FormatName,
            "20260918145142_AddSyncMergeState",
            Source,
            Target,
            SyncChangeSet.Never,
            42,
            7,
            [
                new SyncChange("Transactions", id, 3, Device, IsTombstone: false, "transaction|x|y",
                    Row(("Id", SyncRow.GuidText(id)), ("Payee", "Grocer \"quoted\""), ("TotalAmount_Amount", -200_000L), ("Notes", null), ("Blob", new byte[] { 0, 1, 255 })),
                    new Dictionary<string, IReadOnlyList<SyncRow>>
                    {
                        ["TransactionSplits"] = [Row(("Id", "S1"), ("Category", "Food"))],
                        ["TransactionLineItems"] = [],
                    }),
                new SyncChange("Accounts", Guid.Parse("22222222-0000-4000-8000-000000000000"), 9, Device, IsTombstone: true, null, null, null),
            ]);
    }

    [Fact]
    public void ADocument_SurvivesARoundTrip_ByteForByte()
    {
        var json = Sample().ToJson();
        var back = SyncChangeSet.FromJson(json);

        back.ToJson().Should().Be(json, "two exports of the same state are the same bytes");
        back.SourceVaultId.Should().Be(Source);
        back.TargetVaultId.Should().Be(Target);
        back.SinceClock.Should().Be(SyncChangeSet.Never);
        back.SourceClock.Should().Be(42);
        back.SourceKnowsTargetUpTo.Should().Be(7);
        back.Changes.Should().HaveCount(2);
        back.Changes[0].Row!["Blob"].Should().BeEquivalentTo(new byte[] { 0, 1, 255 });
        back.Changes[0].Row!["TotalAmount_Amount"].Should().Be(-200_000L);
        back.Changes[0].Row!["Notes"].Should().BeNull();
        back.Changes[0].Children!["TransactionSplits"].Should().ContainSingle();
        back.Changes[1].IsTombstone.Should().BeTrue();
        back.Changes[1].Row.Should().BeNull();
        back.Changes[1].Children.Should().BeNull();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"format\":\"pine.sync.v2\"}")]
    [InlineData("{\"format\":\"pine.sync.v1\",\"schemaVersion\":\"x\"}")]
    public void AnythingElse_IsRefused_NotHalfRead(string json)
    {
        var act = () => SyncChangeSet.FromJson(json);
        act.Should().Throw<SyncMergeRefusedException>();
    }

    [Fact]
    public void AColumnHoldingAJsonTypeNoRowHolds_IsRefused()
    {
        var json = Sample().ToJson().Replace("\"Notes\":null", "\"Notes\":true", StringComparison.Ordinal);
        var act = () => SyncChangeSet.FromJson(json);
        act.Should().Throw<SyncMergeRefusedException>();
    }

    [Fact]
    public void Changes_AreOrderedByClock_ThenDevice_ThenTableAndId()
    {
        var a = new SyncChange("B", Guid.Parse("00000000-0000-0000-0000-000000000002"), 1, Device, false, null, null, null);
        var b = new SyncChange("A", Guid.Parse("00000000-0000-0000-0000-000000000009"), 2, Device, false, null, null, null);
        var c = new SyncChange("A", Guid.Parse("00000000-0000-0000-0000-000000000001"), 2, Device, false, null, null, null);
        var d = new SyncChange("B", Guid.Parse("00000000-0000-0000-0000-000000000001"), 2, Device, false, null, null, null);

        var sorted = new List<SyncChange> { d, b, a, c };
        sorted.Sort(SyncChange.Compare);

        sorted.Should().Equal(a, c, b, d);
    }

    [Fact]
    public void ARow_CarriesOnlyWhatSqliteStores()
    {
        var row = Row(("i", 5), ("l", 6L), ("s", "t"), ("n", DBNull.Value), ("g", SyncRow.GuidText(Device)));

        row["i"].Should().Be(5L, "an int is widened to the INTEGER SQLite stores");
        row["n"].Should().BeNull();
        row.Number("l").Should().Be(6);
        row.Number("s").Should().BeNull();
        row.Text("i").Should().BeNull();
        row.GuidAt("g").Should().Be(Device);
        row.GuidAt("s").Should().BeNull();
        row.GuidAt("missing").Should().BeNull();
        row.Keys.Should().Equal("g", "i", "l", "n", "s");
        row.Count.Should().Be(5);
        row.ContainsKey("s").Should().BeTrue();

        var real = () => Row(("r", 1.5));
        real.Should().Throw<NotSupportedException>("Pine's schema has no REAL column, and one is refused rather than rounded");
    }

    [Fact]
    public void TheSchema_IsReadFromTheModel_InDependencyOrder()
    {
        using var context = new PineDbContext("sync-schema-model.db", new byte[32]);
        var schema = SyncSchema.From(context.Model);
        var order = schema.RootsInDependencyOrder.Select(t => t.Name).ToList();

        order.Should().BeEquivalentTo(["Accounts", "Attachments", "Budgets", "ExchangeRates", "ImportProfiles", "Rules", "Settings", "Transactions"],
            "every aggregate root is merged, and no bookkeeping table is");
        order.IndexOf("Accounts").Should().BeLessThan(order.IndexOf("Transactions"));
        order.IndexOf("Transactions").Should().BeLessThan(order.IndexOf("Attachments"));

        var transactions = schema.ByName("Transactions")!;
        transactions.EntityTypeName.Should().Be("Transaction");
        transactions.ChildOwnerColumns.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["TransactionSplits"] = "TransactionId",
            ["TransactionLineItems"] = "TransactionId",
        });
        transactions.References.Should().ContainEquivalentOf(new SyncReference("AccountId", "Accounts", IsConstraint: true));
        transactions.References.Should().ContainEquivalentOf(new SyncReference("LinkedTransactionId", "Transactions", IsConstraint: false),
            "a transfer's link is not a foreign key, and the model cannot say it is a reference");
        schema.ByName("Rules")!.ChildOwnerColumns.Should().ContainKey("RuleConditions");

        schema.DependentsOf("Transactions").Select(d => d.Dependent.Name).Should().Equal("Attachments");
        schema.DependentsOf("Accounts").Select(d => d.Dependent.Name).Should().Equal("Transactions");
        schema.ByEntityType("Budget")!.Name.Should().Be("Budgets");
        schema.ByName("SyncPeers").Should().BeNull();
    }

    [Theory]
    [InlineData("Accounts")]
    [InlineData("RowLocalClock")]
    [InlineData("_x9")]
    public void APlainIdentifier_IsQuoted(string name)
    {
        VaultMerger.Ident(name).Should().Be("\"" + name + "\"");
    }

    [Theory]
    [InlineData("Name\" = 1; --")]
    [InlineData("Accounts; DROP TABLE Accounts")]
    [InlineData("9lives")]
    [InlineData("")]
    [InlineData("a b")]
    [InlineData("a\nb")]
    [InlineData("Ñame")]
    [InlineData(null)]
    public void AnythingElse_IsRefusedBeforeItReachesSql(string? name)
    {
        var act = () => VaultMerger.Ident(name!);
        act.Should().Throw<SyncMergeRefusedException>();
    }
}
