using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Pine.Infrastructure.Data;
using Xunit;

namespace Pine.Tests.Integration.Data;

/// <summary>
/// Pins the exact text EF Core writes into a SQLite <c>TEXT</c> column for a <see cref="Guid"/>.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS TEST EXISTS. Several tests in this project seed "legacy" rows over a raw SQLCipher
/// connection so a migration can be exercised against data EF did not write. Those rows are only a
/// faithful stand-in for real data if their column encodings match EF's own byte for byte. For every
/// other column the encoding is visible in the test (a date format string, a decimal format string).
/// For a <see cref="Guid"/> it is not: <c>guid.ToString()</c> looks obviously right and is silently
/// wrong, because EF writes the upper-case form and SQLite compares TEXT case-sensitively.
/// </para>
/// <para>
/// WHAT GETTING IT WRONG COSTS, AND IT IS NOT HYPOTHETICAL. A row seeded in lower case still
/// MATERIALISES correctly — <see cref="Guid.Parse(string)"/> ignores case — so every assertion made
/// through the change tracker keeps passing. What breaks is the half nobody looks at: a predicate
/// pushed down into SQL (<c>WHERE "AccountId" = @p</c>) can never match it. A test that asserts "this
/// query returns nothing" then passes for the wrong reason, and would keep passing if the behaviour
/// it names were broken. That is exactly what happened to
/// <c>ImportKeyMigrationTests.MigratedVault_NullImportKeys_MatchNothing...</c>, which is why its seed
/// and this test landed in the same change.
/// </para>
/// <para>
/// This test is deliberately about the STORE, not about Pine. If a future EF or
/// Microsoft.Data.Sqlite release changes the encoding, every raw-seeding test in this project becomes
/// wrong at once, and this is the one place that says so out loud.
/// </para>
/// </remarks>
public class RawGuidEncodingTests
{
    /// <summary>
    /// EF writes a <see cref="Guid"/> key as upper-case, hyphenated text, and a server-side equality
    /// predicate over that column is case-sensitive.
    /// </summary>
    [Fact]
    public async Task EfCore_WritesGuidKeys_AsUpperCaseText_AndMatchesThemCaseSensitively()
    {
        // Arrange — a real vault, written by EF itself.
        using var vault = new TempVault();
        var accountId = Guid.NewGuid();

        await using (var context = new PineDbContext(vault.DatabasePath, vault.Key))
        {
            await context.Database.MigrateAsync(CancellationToken.None);

            var account = Account.Create(
                "Encoding probe",
                AccountType.Bank,
                new Money(0m, new CurrencyCode("USD")),
                TimeProvider.System);

            // The id is assigned by Account.Create, so it is read back rather than dictated.
            accountId = account.Id;
            context.Accounts.Add(account);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        // Act — read the raw cell, bypassing EF entirely.
        string storedId;
        using (var connection = vault.OpenRaw(vault.DatabasePath, vault.Key))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"SELECT ""Id"" FROM ""Accounts"" LIMIT 1;";
            storedId = (string)command.ExecuteScalar()!;
        }

        // Assert — the exact text, and the case-sensitivity that makes it matter.
        storedId.Should().Be(
            accountId.ToString().ToUpperInvariant(),
            "EF writes Guid keys upper-cased, so a raw-seeded row must be encoded the same way or it "
            + "will be invisible to every predicate pushed down into SQL");

        storedId.Should().NotBe(
            accountId.ToString(),
            "if these two were the same string this whole class would be pointless -- the trap only "
            + "exists because ToString() is the obvious call and produces the wrong case");

        using (var connection = vault.OpenRaw(vault.DatabasePath, vault.Key))
        {
            using var lowerCase = connection.CreateCommand();
            lowerCase.CommandText = @"SELECT COUNT(*) FROM ""Accounts"" WHERE ""Id"" = $id;";
            lowerCase.Parameters.AddWithValue("$id", accountId.ToString());
            Convert.ToInt32(lowerCase.ExecuteScalar(), CultureInfo.InvariantCulture).Should().Be(
                0,
                "SQLite compares TEXT case-sensitively, so a lower-case id matches nothing -- this is "
                + "the failure mode that makes a lower-case seed silently unreachable from SQL");

            using var upperCase = connection.CreateCommand();
            upperCase.CommandText = @"SELECT COUNT(*) FROM ""Accounts"" WHERE ""Id"" = $id;";
            upperCase.Parameters.AddWithValue("$id", accountId.ToString().ToUpperInvariant());
            Convert.ToInt32(upperCase.ExecuteScalar(), CultureInfo.InvariantCulture).Should().Be(
                1,
                "and the upper-case form is the one that reaches the row");
        }
    }
}
