using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Rules;
using Pine.Infrastructure.Data;
using Xunit;

namespace Pine.Tests.Integration.Data;

/// <summary>
/// Rules against a real SQLCipher vault: round-trip, cascade, and the ordering the engine relies on.
/// </summary>
/// <remarks>
/// The ordering assertion is the one that could not be a unit test. <c>RuleEvaluator</c> breaks ties
/// by creation time and then by id, and the repository claims to apply the same order in SQL — a
/// claim about what the database returns, not about a LINQ-to-objects sort. If the two disagreed,
/// the rules screen would list rules in an order they do not fire in.
/// </remarks>
public class RuleStorageTests
{
    private static Rule CoffeeRule(int priority = 100, string title = "Coffee shops")
        => Rule.Create(
            title,
            "Coffee",
            priority,
            [
                (RuleField.Payee, RuleOperator.Contains, "starbucks"),
                (RuleField.Amount, RuleOperator.LessThan, "-2"),
            ],
            TimeProvider.System);

    private static async Task<PineDbContext> MigratedAsync(TempVault vault)
    {
        var context = vault.CreateContext();
        await context.Database.MigrateAsync(CancellationToken.None);
        return context;
    }

    /// <summary>A rule and its conditions survive a round trip.</summary>
    [Fact]
    public async Task ARule_RoundTripsWithItsConditions()
    {
        using var vault = new TempVault();
        Guid id;

        await using (var context = await MigratedAsync(vault))
        {
            var rule = CoffeeRule();
            id = rule.Id;
            await new SqliteRuleRepository(context).AddAsync(rule, CancellationToken.None);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        await using var reopened = vault.CreateContext();
        var loaded = await new SqliteRuleRepository(reopened).GetAsync(id, CancellationToken.None);

        loaded.Should().NotBeNull();
        loaded!.Title.Should().Be("Coffee shops");
        loaded.Category.Should().Be("Coffee");
        loaded.IsEnabled.Should().BeTrue();
        loaded.Conditions.Should().HaveCount(2);
        loaded.Conditions.Should().Contain(c =>
            c.Field == RuleField.Payee && c.Operator == RuleOperator.Contains && c.Value == "starbucks");

        // The point of loading it: it still evaluates.
        loaded.Matches(new RuleInput("STARBUCKS #12", string.Empty, -4.50m)).Should().BeTrue();
        loaded.Matches(new RuleInput("STARBUCKS #12", string.Empty, -1.00m)).Should().BeFalse();
    }

    /// <summary>Enum values are stored as names, not as ordinals.</summary>
    /// <remarks>
    /// AN ORDINAL WOULD MAKE REORDERING THE ENUM A SILENT DATA CORRUPTION. Inserting a member in
    /// the middle of <c>RuleOperator</c> would quietly turn every stored "contains" into something
    /// else. Reading the raw column is the only way to assert the storage format rather than the
    /// round trip, which would pass either way.
    /// </remarks>
    [Fact]
    public async Task OperatorsAndFields_AreStoredAsNames()
    {
        using var vault = new TempVault();

        await using (var context = await MigratedAsync(vault))
        {
            await new SqliteRuleRepository(context).AddAsync(CoffeeRule(), CancellationToken.None);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        vault.CloseContexts();

        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Field, Operator FROM RuleConditions ORDER BY Field;";
        using var reader = command.ExecuteReader();

        var rows = new System.Collections.Generic.List<(string Field, string Operator)>();
        while (reader.Read())
        {
            rows.Add((reader.GetString(0), reader.GetString(1)));
        }

        rows.Should().Contain(("Payee", "Contains"));
        rows.Should().Contain(("Amount", "LessThan"));
    }

    /// <summary>Deleting a rule takes its conditions with it.</summary>
    /// <remarks>
    /// The conditions are an owned collection, so the cascade is EF's rather than something this
    /// code arranges — which is exactly why it is worth asserting once against a real database:
    /// orphaned conditions would accumulate invisibly.
    /// </remarks>
    [Fact]
    public async Task DeletingARule_DeletesItsConditions()
    {
        using var vault = new TempVault();
        Guid id;

        await using (var context = await MigratedAsync(vault))
        {
            var rule = CoffeeRule();
            id = rule.Id;
            await new SqliteRuleRepository(context).AddAsync(rule, CancellationToken.None);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        await using (var context = vault.CreateContext())
        {
            (await new SqliteRuleRepository(context).RemoveAsync(id, CancellationToken.None))
                .Should().BeTrue();
            await context.SaveChangesAsync(CancellationToken.None);
        }

        vault.CloseContexts();

        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM RuleConditions;";

        Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be(0, "a condition has no life without its rule");
    }

    /// <summary>
    /// The repository returns rules in the order the evaluator fires them.
    /// </summary>
    /// <remarks>
    /// Priority first, then creation time. Asserted through the database because that is where the
    /// ordering is applied; a screen that listed rules in a different order from the one they run
    /// in would be worse than an unordered list.
    /// </remarks>
    [Fact]
    public async Task RulesComeBackInEvaluationOrder()
    {
        using var vault = new TempVault();

        await using (var context = await MigratedAsync(vault))
        {
            var repository = new SqliteRuleRepository(context);
            await repository.AddAsync(CoffeeRule(priority: 10, title: "low"), CancellationToken.None);
            await repository.AddAsync(CoffeeRule(priority: 900, title: "high"), CancellationToken.None);
            await repository.AddAsync(CoffeeRule(priority: 500, title: "middle"), CancellationToken.None);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        await using var reopened = vault.CreateContext();
        var all = await new SqliteRuleRepository(reopened).GetAllAsync(CancellationToken.None);

        all.Select(rule => rule.Title).Should().Equal("high", "middle", "low");
    }

    /// <summary>Only enabled rules come back from the evaluation query.</summary>
    [Fact]
    public async Task OnlyEnabledRulesAreReturnedForEvaluation()
    {
        using var vault = new TempVault();

        await using (var context = await MigratedAsync(vault))
        {
            var repository = new SqliteRuleRepository(context);

            var off = CoffeeRule(title: "off");
            off.Disable();

            await repository.AddAsync(off, CancellationToken.None);
            await repository.AddAsync(CoffeeRule(title: "on"), CancellationToken.None);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        await using var reopened = vault.CreateContext();
        var repositoryAgain = new SqliteRuleRepository(reopened);

        (await repositoryAgain.GetEnabledAsync(CancellationToken.None))
            .Select(rule => rule.Title).Should().Equal("on");

        (await repositoryAgain.GetAllAsync(CancellationToken.None))
            .Should().HaveCount(2, "the rules screen still lists a disabled rule");
    }

    /// <summary>Removing a rule that is not there is not an error.</summary>
    [Fact]
    public async Task RemovingARuleThatIsNotThere_IsNotAnError()
    {
        using var vault = new TempVault();
        await using var context = await MigratedAsync(vault);

        (await new SqliteRuleRepository(context).RemoveAsync(Guid.NewGuid(), CancellationToken.None))
            .Should().BeFalse();
    }

    /// <summary>
    /// Updating a rule keeps its conditions.
    /// </summary>
    /// <remarks>
    /// THE TD-13 SHAPE, WHICH <c>EfModel_DeclaresOwnedCollectionsOnTransactionOnly</c> DEMANDS A
    /// PROOF OF. That defect was owned children being deleted and re-inserted — or dropped — when
    /// their parent was saved detached. <c>Rule.Conditions</c> is the second owned collection in
    /// this model, and switching a rule off is the one update path it has, so this is where the
    /// conditions would vanish if they were going to.
    /// </remarks>
    [Fact]
    public async Task TogglingARule_KeepsItsConditions()
    {
        using var vault = new TempVault();
        Guid id;

        await using (var context = await MigratedAsync(vault))
        {
            var rule = CoffeeRule();
            id = rule.Id;
            await new SqliteRuleRepository(context).AddAsync(rule, CancellationToken.None);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        await using (var context = vault.CreateContext())
        {
            var repository = new SqliteRuleRepository(context);
            var loaded = await repository.GetAsync(id, CancellationToken.None);
            loaded!.Disable();
            await context.SaveChangesAsync(CancellationToken.None);
        }

        await using var reopened = vault.CreateContext();
        var reloaded = await new SqliteRuleRepository(reopened).GetAsync(id, CancellationToken.None);

        reloaded!.IsEnabled.Should().BeFalse();
        reloaded.Conditions.Should().HaveCount(2, "an update must not take the owned children with it");
        reloaded.Conditions.Should().Contain(c => c.Value == "starbucks");
    }
}
