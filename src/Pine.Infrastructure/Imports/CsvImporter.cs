using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CsvHelper;
using CsvHelper.Configuration;
using Pine.Core.Importing;
using Pine.Core.Transactions;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Accounts;

namespace Pine.Infrastructure.Importing;

public sealed class CsvImporter : IImporter
{
    private readonly TimeProvider _timeProvider;

    public CsvImporter(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<Transaction>> ImportAsync(Guid targetAccountId, Stream fileStream, ImportProfile profile, CancellationToken cancellationToken = default)
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

        await foreach (var record in records)
        {
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

            // Validate that we have the bare minimum
            if (rawAmount == 0 && string.IsNullOrEmpty(payee))
            {
                continue; // Skip invalid or empty row
            }

            // Currently assuming USD for all imports. 
            // In a fully featured version, we'd retrieve the Account's currency from the DB.
            var currency = new CurrencyCode("USD");
            var amount = new Money(rawAmount, currency);

            var transaction = Transaction.Create(targetAccountId, date, payee, amount, _timeProvider, notes);

            // Optional: Set category. For now, since Transaction domain entity doesn't have a mutable category,
            // we'd either add a single split or handle it in the application layer.
            if (!string.IsNullOrEmpty(category))
            {
                transaction.AddSplit(amount, category, notes);
            }

            transactions.Add(transaction);
        }

        return transactions;
    }
}
