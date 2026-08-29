using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Importing;
using Pine.Core.Transactions;
using Pine.Infrastructure.Importing;
using Xunit;

namespace Pine.Tests.Unit.Infrastructure.Importing;

/// <summary>
/// Covers the import key <see cref="CsvImporter"/> stamps onto every transaction it produces (TD-03).
/// </summary>
/// <remarks>
/// These tests exist because the failure they guard against was invisible: de-duplicating on
/// <c>(date, amount)</c> destroyed the second of two genuinely identical purchases and reported nothing.
/// A test that only checked "the import succeeded" passed happily while the data was being lost, so each
/// test here asserts on the KEYS rather than on the count of surviving rows.
/// </remarks>
public class CsvImporterImportKeyTests
{
    private const string HeaderRow = "Date,Payee,Amount";

    private static ImportProfile CreateProfile(string name, bool withReferenceColumn = false)
    {
        var timeProvider = TimeProvider.System;
        var profile = ImportProfile.Create(name, timeProvider);

        var mappings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Date"] = "Date",
            ["Payee"] = "Payee",
            ["Amount"] = "Amount",
        };

        if (withReferenceColumn)
        {
            mappings["Reference"] = "Reference";
        }

        profile.UpdateSettings("yyyy-MM-dd", ',', hasHeader: true, invertAmounts: false, mappings, timeProvider);
        return profile;
    }

    private static Account CreateAccount() =>
        Account.Create(
            "Test",
            AccountType.Bank,
            new Money(0m, new CurrencyCode("USD")),
            TimeProvider.System);

    private static async Task<IReadOnlyList<Transaction>> ImportAsync(string csv, ImportProfile profile)
    {
        var importer = new CsvImporter(TimeProvider.System);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        return await importer.ImportAsync(CreateAccount(), stream, profile, CancellationToken.None);
    }

    [Fact]
    public async Task ImportAsync_WithTwoIdenticalRows_ProducesDistinctKeys()
    {
        // The exact shape of TD-03: two coffees, same day, same price, same shop. Matching on
        // (date, amount) collapsed these into one and threw the second away.
        var csv = HeaderRow + "\n"
            + "2026-01-15,Acme Coffee,-4.50\n"
            + "2026-01-15,Acme Coffee,-4.50\n";

        var result = await ImportAsync(csv, CreateProfile("Bank CSV"));

        Assert.Equal(2, result.Count);
        Assert.NotEqual(result[0].ImportKey, result[1].ImportKey);
    }

    [Fact]
    public async Task ImportAsync_WithSameContentTwice_ReproducesTheSameKeysInOrder()
    {
        // Re-import recognition depends entirely on this: if the keys drifted between two reads of the
        // same bytes, every re-imported row would look new and the vault would fill with duplicates.
        var csv = HeaderRow + "\n"
            + "2026-01-15,Acme Coffee,-4.50\n"
            + "2026-01-16,Grocer,-31.20\n"
            + "2026-01-16,Grocer,-31.20\n";

        var profileName = "Bank CSV";
        var first = await ImportAsync(csv, CreateProfile(profileName));
        var second = await ImportAsync(csv, CreateProfile(profileName));

        Assert.Equal(3, first.Count);
        Assert.Equal(
            first.Select(transaction => transaction.ImportKey),
            second.Select(transaction => transaction.ImportKey));
    }

    [Fact]
    public async Task ImportAsync_WithMappedReferenceColumn_PrefersTheReferenceScheme()
    {
        // A populated reference is the source's own identifier and survives a re-export in a different
        // row order; a blank cell has nothing to offer, so that row must fall back rather than fail.
        var csv = "Date,Payee,Amount,Reference\n"
            + "2026-01-15,Acme Coffee,-4.50,FITID-001\n"
            + "2026-01-15,Acme Coffee,-4.50,\n";

        var result = await ImportAsync(csv, CreateProfile("Bank CSV", withReferenceColumn: true));

        Assert.Equal(2, result.Count);
        Assert.StartsWith(ImportKeyFactory.ReferenceSchemePrefix, result[0].ImportKey, StringComparison.Ordinal);
        Assert.StartsWith(ImportKeyFactory.DerivedSchemePrefix, result[1].ImportKey, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportAsync_AssignsAKeyToEveryRow_WithinThePersistedColumnWidth()
    {
        // A key longer than the column would be truncated or rejected at save time — after the import
        // reported success — so the width is asserted here, where it is still cheap to notice.
        var longPayee = new string('X', 400);
        var csv = "Date,Payee,Amount,Reference\n"
            + "2026-01-15," + longPayee + ",-4.50," + new string('R', 400) + "\n"
            + "2026-01-16," + longPayee + ",-4.50,\n";

        var result = await ImportAsync(csv, CreateProfile("Bank CSV", withReferenceColumn: true));

        Assert.Equal(2, result.Count);
        Assert.All(result, transaction =>
        {
            Assert.False(string.IsNullOrWhiteSpace(transaction.ImportKey));
            Assert.True(transaction.ImportKey!.Length <= Transaction.MaxImportKeyLength);
        });
    }
}
