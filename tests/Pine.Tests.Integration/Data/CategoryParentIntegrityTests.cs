using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Categories;
using Xunit;

namespace Pine.Tests.Integration.Data;

/// <summary>
/// <c>Category.ParentCategoryId</c> is a real foreign key, and these tests are what prove it is
/// enforced rather than merely declared (TD-32).
/// </summary>
/// <remarks>
/// <para>
/// The column existed from the first migration as a bare nullable <c>Guid</c>: the original
/// <c>CreateTable</c> emitted no <c>table.ForeignKey</c> for it and <c>PineDbContext</c> had no
/// <c>HasOne</c> either, while <c>Budget.CategoryId</c> two blocks away had a real constraint the
/// whole time. A category could therefore name a parent that had never existed.
/// </para>
/// <para>
/// WHY THIS NEEDS AN INTEGRATION TEST AND NOT A MAPPING ASSERTION. In SQLite a foreign key is
/// **not enforced unless <c>PRAGMA foreign_keys</c> is ON**, and that pragma is a per-connection
/// setting, not a property of the schema. A test that only inspected the EF model, or only read
/// the migration, would pass identically whether or not the constraint does anything at runtime.
/// The only honest check is to write a violating row through the real provider against a real
/// SQLCipher vault and require the database to refuse it.
/// </para>
/// </remarks>
public class CategoryParentIntegrityTests
{
    [Fact]
    public async Task ACategoryNamingAParentThatDoesNotExist_IsRefusedByTheDatabase()
    {
        using var vault = new TempVault();
        await using var context = vault.CreateContext();
        await context.Database.MigrateAsync(CancellationToken.None);

        var orphan = Category.Create("Orphan", "#FFFFFF", Guid.NewGuid());
        context.Categories.Add(orphan);

        var act = async () => await context.SaveChangesAsync(CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<DbUpdateException>(
            "a parent that does not exist is exactly what the foreign key is for. Before it was "
            + "configured this row saved happily and left a dangling reference");

        // The inner exception matters: it is the DATABASE refusing the row, not EF deciding to.
        thrown.And.InnerException.Should().BeOfType<SqliteException>()
            .Which.SqliteErrorCode.Should().Be(19, "SQLITE_CONSTRAINT");
    }

    [Fact]
    public async Task ACategoryNamingAParentThatExists_IsAccepted()
    {
        using var vault = new TempVault();
        await using var context = vault.CreateContext();
        await context.Database.MigrateAsync(CancellationToken.None);

        // Anti-vacuity. If the constraint refused everything -- or if the save path were broken for
        // some unrelated reason -- the test above would pass for the wrong reason and this one
        // catches it.
        var parent = Category.Create("Food", "#00FF00");
        context.Categories.Add(parent);
        await context.SaveChangesAsync(CancellationToken.None);

        var child = Category.Create("Groceries", "#00AA00", parent.Id);
        context.Categories.Add(child);

        var act = async () => await context.SaveChangesAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ACategoryWithNoParent_IsStillAccepted()
    {
        using var vault = new TempVault();
        await using var context = vault.CreateContext();
        await context.Database.MigrateAsync(CancellationToken.None);

        context.Categories.Add(Category.Create("Top level", "#123456"));

        var act = async () => await context.SaveChangesAsync(CancellationToken.None);
        await act.Should().NotThrowAsync("the parent is optional; a root category has none");
    }

    /// <summary>
    /// Deleting a parent that still has children: what the DATABASE guarantees, and what EF does
    /// instead.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These two assertions disagree on purpose, because the system does. At the database level the
    /// constraint is <c>Restrict</c> and SQLite refuses the delete — that is the durable guarantee,
    /// and it holds against anything that writes to the vault, including the raw SQLCipher
    /// connections this suite uses to simulate older builds.
    /// </para>
    /// <para>
    /// Through EF with the child tracked, the delete SUCCEEDS: EF issues
    /// <c>UPDATE ... SET ParentCategoryId = NULL</c> for the loaded dependent first and then deletes
    /// the parent, so the database constraint is satisfied and never fires. The child is not
    /// deleted; it is silently promoted to a root category. This was measured, not assumed — the
    /// first version of this test asserted a refusal and got none.
    /// </para>
    /// <para>
    /// It is left as observed rather than "fixed" because the relationship is OPTIONAL: a category
    /// with no parent is a perfectly valid root, so nulling the link produces a legal row rather
    /// than a corrupt one. What it does lose is the user's intent about where that category sat.
    /// Making EF refuse instead would mean a required parent or an explicit guard on a delete path
    /// that does not exist yet — <c>CreateCategoryCommand</c> has no caller and there is no delete
    /// command at all (TD-132).
    /// </para>
    /// </remarks>
    [Fact]
    public async Task DeletingAParentThatStillHasChildren_IsRefusedByTheDatabase()
    {
        using var vault = new TempVault();
        await using var context = vault.CreateContext();
        await context.Database.MigrateAsync(CancellationToken.None);

        var parent = Category.Create("Food", "#00FF00");
        context.Categories.Add(parent);
        await context.SaveChangesAsync(CancellationToken.None);

        context.Categories.Add(Category.Create("Groceries", "#00AA00", parent.Id));
        await context.SaveChangesAsync(CancellationToken.None);

        // Straight at the database, with nothing tracked to fix up the child first.
        var act = async () => await context.Database.ExecuteSqlRawAsync(
            """DELETE FROM "Categories" WHERE "Id" = {0}""", parent.Id);

        (await act.Should().ThrowAsync<SqliteException>(
            "Restrict means the database refuses to strand the children"))
            .Which.SqliteErrorCode.Should().Be(19, "SQLITE_CONSTRAINT");
    }

    [Fact]
    public async Task DeletingAParentThroughEfWithTheChildTracked_PromotesTheChildToARootInstead()
    {
        using var vault = new TempVault();
        await using var context = vault.CreateContext();
        await context.Database.MigrateAsync(CancellationToken.None);

        var parent = Category.Create("Food", "#00FF00");
        context.Categories.Add(parent);
        await context.SaveChangesAsync(CancellationToken.None);

        var child = Category.Create("Groceries", "#00AA00", parent.Id);
        context.Categories.Add(child);
        await context.SaveChangesAsync(CancellationToken.None);

        context.Categories.Remove(parent);
        await context.SaveChangesAsync(CancellationToken.None);

        var remaining = await context.Categories.AsNoTracking().ToListAsync(CancellationToken.None);

        remaining.Should().ContainSingle("the child survives; only the parent is deleted")
            .Which.ParentCategoryId.Should().BeNull(
                "EF nulls the tracked dependent's foreign key before deleting the principal, so the "
                + "database constraint never fires. The row stays legal and the filing is lost");
    }

    /// <summary>
    /// A cycle is NOT prevented, and that is recorded here rather than left to be discovered.
    /// </summary>
    /// <remarks>
    /// A foreign key constrains each row on its own, so A→B followed by B→A satisfies both. This
    /// test asserts the current, weaker guarantee so that the limit is visible in the suite instead
    /// of being an unwritten assumption. Preventing cycles needs a check on the write path, and the
    /// only writer is <c>CreateCategoryCommand</c>, which no production code calls (TD-132) — a
    /// guard there today would be enforcement written for a caller that does not exist. If this test
    /// ever fails because a cycle was refused, the guard has been added and this test should become
    /// its assertion rather than being deleted.
    /// </remarks>
    [Fact]
    public async Task ACycleBetweenTwoCategories_IsNotPreventedByTheForeignKeyAlone()
    {
        using var vault = new TempVault();
        await using var context = vault.CreateContext();
        await context.Database.MigrateAsync(CancellationToken.None);

        var first = Category.Create("First", "#111111");
        var second = Category.Create("Second", "#222222");
        context.Categories.AddRange(first, second);
        await context.SaveChangesAsync(CancellationToken.None);

        // Point each at the other. Both rows individually reference a row that exists.
        await context.Database.ExecuteSqlRawAsync(
            """UPDATE "Categories" SET "ParentCategoryId" = {0} WHERE "Id" = {1}""",
            second.Id, first.Id);
        await context.Database.ExecuteSqlRawAsync(
            """UPDATE "Categories" SET "ParentCategoryId" = {0} WHERE "Id" = {1}""",
            first.Id, second.Id);

        var reloaded = await context.Categories.AsNoTracking().ToListAsync(CancellationToken.None);

        reloaded.Should().HaveCount(2);
        reloaded.Should().OnlyContain(
            category => category.ParentCategoryId != null,
            "the database accepts the cycle: referential integrity is a per-row guarantee, and "
            + "nothing in the schema can see a loop");
    }

}
