using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CsvHelper;
using CsvHelper.Configuration;
using Pine.Core.Importing;
using Pine.Core.Transactions;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Accounts;

namespace Pine.Infrastructure.Importing;

public sealed class CsvImporter : IImporter, IImportHeaderReader
{
    private readonly TimeProvider _timeProvider;

    public CsvImporter(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<Transaction>> ImportAsync(Pine.Core.Accounts.Account targetAccount, Stream fileStream, ImportProfile profile, CancellationToken cancellationToken = default)
    {
        var transactions = new List<Transaction>();

        // 1. Configure CsvHelper based on ImportProfile
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = profile.HasHeaderRecord,
            Delimiter = profile.Delimiter.ToString(),
            MissingFieldFound = null,
            BadDataFound = null,
        };

        using var reader = new StreamReader(fileStream);
        using var csv = new CsvReader(reader, config);

        var records = csv.GetRecordsAsync<dynamic>(cancellationToken);

        // Required mapping keys
        string? dateCol = profile.ColumnMappings.GetValueOrDefault("Date");
        string? payeeCol = profile.ColumnMappings.GetValueOrDefault("Payee");
        string? amountCol = profile.ColumnMappings.GetValueOrDefault("Amount");
        string? categoryCol = profile.ColumnMappings.GetValueOrDefault("Category");
        string? notesCol = profile.ColumnMappings.GetValueOrDefault("Notes");

        // Optional. A source that carries its own stable identifier (an OFX FITID, a bank's transaction
        // reference) can expose it through this mapping and get a key that survives a re-export in a
        // different row order, which a derived key cannot. It is a plain extra entry in the existing
        // free-form ColumnMappings dictionary, so profiles that do not set it are unaffected and no
        // schema change or migration is involved.
        string? referenceCol = profile.ColumnMappings.GetValueOrDefault("Reference");

        // TD-66: the source identifier used to be profile.Name, the only thing reachable inside
        // ImportAsync at the time — stable only by accident, because the wizard minted a throwaway
        // profile with a constant literal name on every run. Now that profiles are saved, reused and
        // user-named (TD-151), a DISPLAY name is not a safe key component: nothing stops two profiles
        // sharing one, and nothing should force a user's naming choice to double as a stability
        // guarantee. The profile's id is structural, immutable once created, and never shown to the
        // user to edit, so it is what the key is built from instead. Re-importing the same file under
        // the SAME saved profile reproduces the same keys; two different profiles — even sharing a
        // name — do not collide.
        var source = profile.Id.ToString("D", CultureInfo.InvariantCulture);

        // Counts the row's position in the FILE, incremented for every record CsvHelper yields, including
        // the ones rejected below. Deliberately not the index into `transactions`: an output index shifts
        // whenever a row ahead of it is rejected, so a parser change (or a blank row read differently)
        // would renumber every later row and their keys would stop matching the ones already stored.
        // The file position is a property of the file, so re-importing the same file reproduces it.
        var rowIndex = -1;

        await foreach (var record in records)
        {
            rowIndex++;

            var row = record as IDictionary<string, object>;
            if (row == null) continue;

            // Date
            DateTimeOffset date = _timeProvider.GetUtcNow();
            if (!string.IsNullOrEmpty(dateCol) && row.TryGetValue(dateCol, out var dateObj) && dateObj is string dateStr)
            {
                if (DateTimeOffset.TryParseExact(dateStr, profile.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                {
                    date = parsedDate;
                }
            }

            // Payee
            string payee = "Unknown";
            if (!string.IsNullOrEmpty(payeeCol) && row.TryGetValue(payeeCol, out var payeeObj) && payeeObj is string payeeStr)
            {
                payee = payeeStr.Trim();
            }

            // Amount
            decimal rawAmount = 0m;
            if (!string.IsNullOrEmpty(amountCol) && row.TryGetValue(amountCol, out var amountObj) && amountObj is string amountStr)
            {
                if (decimal.TryParse(amountStr.Replace("$", "").Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedAmount))
                {
                    rawAmount = parsedAmount;
                }
            }
            
            if (profile.InvertAmounts)
            {
                rawAmount = -rawAmount;
            }

            // Category
            string category = "";
            if (!string.IsNullOrEmpty(categoryCol) && row.TryGetValue(categoryCol, out var catObj) && catObj is string catStr)
            {
                category = catStr.Trim();
            }

            // Notes
            string notes = "";
            if (!string.IsNullOrEmpty(notesCol) && row.TryGetValue(notesCol, out var notesObj) && notesObj is string notesStr)
            {
                notes = notesStr.Trim();
            }

            // Reference (optional)
            string reference = "";
            if (!string.IsNullOrEmpty(referenceCol) && row.TryGetValue(referenceCol, out var referenceObj) && referenceObj is string referenceStr)
            {
                reference = referenceStr.Trim();
            }

            // Validate that we have the bare minimum
            if (rawAmount == 0 && string.IsNullOrEmpty(payee))
            {
                continue; // Skip invalid or empty row
            }

            var currency = targetAccount.Balance.Currency;
            var amount = new Money(rawAmount, currency);

            var type = amount.Amount >= 0 ? Pine.Core.Transactions.TransactionType.Income : Pine.Core.Transactions.TransactionType.Expense;
            var transaction = Transaction.Create(targetAccount.Id, date, payee, amount, type, _timeProvider, notes);

            // Optional: Set category. For now, since Transaction domain entity doesn't have a mutable category,
            // we'd either add a single split or handle it in the application layer.
            if (!string.IsNullOrEmpty(category))
            {
                transaction.AddSplit(amount, category, notes);
            }

            // Assigned before the transaction leaves the importer, because only the importer knows the
            // row's file position and whether the source supplied a reference of its own. The key
            // describes the transaction as CREATED — the inverted amount, the parsed date and payee —
            // so re-parsing the same row under the same profile reproduces it exactly.
            transaction.AssignImportKey(
                reference.Length > 0
                    ? ImportKeyFactory.FromReference(source, reference)
                    : ImportKeyFactory.Derive(date, amount.Amount, currency.Value, payee, source, rowIndex));

            transactions.Add(transaction);
        }

        return transactions;
    }

    /// <summary>
    /// Reads the first line of the file and splits it on a comma -- exactly what
    /// <c>ImportWizardViewModel.ReadCsvHeaders</c> did inline before TD-123 moved it here so the
    /// wizard's mapping step and this importer share one idea of "the header row".
    /// </summary>
    /// <remarks>
    /// Deliberately not <see cref="ImportProfile.Delimiter"/>-aware: the caller has no profile yet
    /// -- this runs before the user has mapped anything -- and a comma is what the wizard's own
    /// logic always assumed here. Widening it to the chosen delimiter is a separate, later change,
    /// not a silent behaviour shift smuggled into this one.
    /// </remarks>
    public IReadOnlyList<string> ReadHeaders(Stream fileStream)
    {
        ArgumentNullException.ThrowIfNull(fileStream);

        using var reader = new StreamReader(fileStream);
        var headerLine = reader.ReadLine();

        return headerLine is null
            ? Array.Empty<string>()
            : headerLine.Split(',').Select(h => h.Trim()).ToArray();
    }
}
