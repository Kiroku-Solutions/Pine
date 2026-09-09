using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Security;
using Pine.Infrastructure.Data;
using Xunit;

namespace Pine.Tests.Integration.Data;

/// <summary>
/// Pins the set of entities <see cref="PineDbContext"/> is allowed to map, so a table cannot re-enter
/// the schema without somebody saying why (TD-37).
/// </summary>
/// <remarks>
/// <para>
/// WHY AN ALLOW-LIST AND NOT "EVERY ENTITY MUST HAVE A CONSUMER". The stronger-sounding rule is the
/// one that cannot be honestly written. "Consumer" has no mechanical definition here: a
/// <c>DbSet</c> is not one — <c>Attachment</c>, <c>Rule</c> and <c>Tag</c> each had a <c>DbSet</c>
/// for the whole time they were dead — and any reflective stand-in (a repository interface naming
/// the type, a query handler mentioning it) would be a heuristic dressed as a proof, green for a
/// type that is referenced once and never called, and red for a legitimately new entity mid-build.
/// A test that claims to detect dead entities and actually detects naming conventions is worse than
/// no test, because it is trusted.
/// </para>
/// <para>
/// An allow-list claims exactly what it can support: <i>this</i> is the set of things Pine persists,
/// and it was reviewed. It is also strictly wider than a denylist of the three names dropped here —
/// a fourth shell entity added tomorrow fails this test too, and the person adding it has to put the
/// name in the list, in a commit, with a reason. That is the review step the three dead entities
/// never had.
/// </para>
/// <para>
/// Owned types are excluded because they are not separately persisted things: a split and a line item
/// are parts of a transaction, and the same exclusion is what <c>ConfigureConcurrencyTokens</c>
/// applies when handing out version tokens.
/// </para>
/// </remarks>
public class MappedEntitySurfaceTests
{
    /// <summary>
    /// Every non-owned entity type <see cref="PineDbContext"/> maps, by CLR name.
    /// </summary>
    /// <remarks>
    /// Widening this is the thing the test exists to make deliberate. <c>Tag</c> is deliberately
    /// absent: it was a shell with no behaviour and no consumer, and
    /// <c>DropAttachmentRuleAndTagTables</c> removed it and its table.
    /// </remarks>
    /// <remarks>
    /// <c>Attachment</c> IS ON THIS LIST AGAIN, AND IT IS NOT THE SAME TYPE. The shell dropped in
    /// iteration 27 had no hash, no content and an unconstrained <c>TransactionId</c>; it was
    /// deleted precisely because keeping it preserved a shape known to disagree with its own
    /// specification. The Phase 3 attachment carries the file, computes the SHA-256
    /// <c>10-Domain-Model</c> §7 requires, verifies it on read, and is bound to its transaction by
    /// a real foreign key. The allow-list grew by a deliberate edit, which is what it is for.
    /// </remarks>
    private static readonly string[] AllowedEntityNames =
    [
        nameof(Pine.Core.Accounts.Account),
        nameof(Pine.Core.Attachments.Attachment),
        nameof(Pine.Core.Budgets.Budget),
        nameof(Pine.Core.Categories.Category),
        nameof(Pine.Core.Importing.ImportProfile),
        nameof(Pine.Core.Rules.Rule),
        nameof(Pine.Core.Settings.Setting),
        nameof(Pine.Core.Transactions.Transaction),
    ];

    /// <summary>The one name that stayed removed, kept explicit so the regression reads clearly.</summary>
    /// <remarks>
    /// <c>Rule</c> left this list in the Phase 3 rule-engine work, and — like <c>Attachment</c>
    /// before it — the type that came back is not the one that was dropped. The shell had a
    /// <c>TargetCategoryId</c> the database knew nothing about, no conditions and no evaluator; the
    /// replacement has conditions, a pure evaluator, a rules screen and a place in the import
    /// pipeline. <c>Tag</c> is still nothing but a name in a document.
    /// </remarks>
    private static readonly string[] DroppedEntityNames = ["Tag"];

    [Fact]
    public void TheModelMapsOnlyTheEntitiesOnTheAllowList()
    {
        using var vault = new TempVault();
        var context = vault.CreateContext();

        var mapped = context.Model
            .GetEntityTypes()
            .Where(entityType => !entityType.IsOwned())
            .Select(entityType => entityType.ClrType.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        // Guard against a vacuous pass: an empty model would satisfy every assertion below.
        mapped.Should().NotBeEmpty("PineDbContext maps at least the money aggregates");

        mapped.Should().BeEquivalentTo(
            AllowedEntityNames,
            "a persisted entity is a promise that something reads and writes it. Adding one to the "
            + "model without adding it here is how Attachment, Rule and Tag came to sit in the "
            + "schema for months with no repository, no query, no command and no consumer (TD-37)");
    }

    /// <summary>
    /// The other half, and the one that is about the database rather than the model: a vault migrated
    /// from nothing to HEAD must not contain the three tables.
    /// </summary>
    /// <remarks>
    /// This runs the real migration pipeline against a real SQLCipher file, so it fails both if the
    /// drop migration is removed and if a later migration re-creates a table the model no longer
    /// knows about — which the model-level assertion above cannot see.
    /// </remarks>
    [Fact]
    public async Task AVaultMigratedToHeadHasNoAttachmentsRulesOrTagsTable()
    {
        using var vault = new TempVault();
        var context = vault.CreateContext();

        await new EfCoreVaultMigrator(context, new VaultBackupService(context), PineTestOptions.Security)
            .MigrateAsync(CancellationToken.None);

        var tables = ReadTableNames(vault);

        tables.Should().NotBeEmpty("the migration should have created the surviving tables");
        tables.Should().Contain("Accounts", "the surviving schema is still there, so the drop was surgical");

        foreach (var dropped in DroppedEntityNames)
        {
            tables.Should().NotContain(
                dropped + "s",
                $"DropAttachmentRuleAndTagTables removes the {dropped} table (TD-37)");
        }
    }

    private static List<string> ReadTableNames(TempVault vault)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT ""name"" FROM ""sqlite_master"" WHERE ""type"" = 'table';";

        var names = new List<string>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}
