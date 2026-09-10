using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ExcelDataReader;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Importing;
using Pine.Core.Transactions;

namespace Pine.Infrastructure.Importing;

public sealed class ExcelImporter : IImporter
{
    private readonly TimeProvider _timeProvider;

    public ExcelImporter(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        // ExcelDataReader requires this for encoding code pages like 1252 on modern .NET
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    }

    /// <remarks>
    /// <para>
    /// This is the spreadsheet counterpart of <see cref="CsvImporter"/> and deliberately mirrors it
    /// field for field: the same <see cref="ImportProfile.ColumnMappings"/> keys, the same defaults for
    /// a missing cell, the same "skip an empty row" rule, and the same import key derivation. A bank
    /// statement exported as .csv and as .xlsx should produce the same transactions, so the two
    /// importers must not disagree about what a row means.
    /// </para>
    /// <para>
    /// WHAT IS DIFFERENT FROM CSV, AND WHY. A CSV cell is always text; a spreadsheet cell is typed, so a
    /// date arrives as a <see cref="DateTime"/> and an amount as a <see cref="double"/> — the string
    /// parse that CSV relies on would fail on both and quietly hand back the defaults. Every field below
    /// therefore checks the typed case FIRST and falls back to the string parse only when the cell was
    /// stored as text, which is what a bank export that writes its dates as strings produces.
    /// </para>
    /// <para>
    /// COLUMN MAPPINGS RESOLVE TWO WAYS. <see cref="ImportProfile.ColumnMappings"/> is documented as
    /// holding "header names or indices". With <see cref="ImportProfile.HasHeaderRecord"/> set, a
    /// mapping is matched against the text of the first row (trimmed, case-insensitively); when that
    /// fails, or when the sheet has no header row at all, the mapping is read as a zero-based column
    /// index. A mapping that resolves to neither simply contributes nothing, exactly as an unmatched
    /// CSV header does.
    /// </para>
    /// <para>
    /// A BAD ROW IS NOT AN EXCEPTION. <see cref="CsvImporter"/> throws nothing on an unparseable cell:
    /// it falls back to the field's default (the current instant for a date, <c>0</c> for an amount)
    /// and drops the row only when it carries neither an amount nor a payee. This importer does the
    /// same, because a single malformed row in a thousand-row statement must not abort the import of
    /// the other nine hundred and ninety-nine.
    /// </para>
    /// <para>
    /// The key is assigned before the transaction leaves the importer, for the reason
    /// <see cref="ImportKeyFactory"/> sets out: <c>FromReference</c> when the workbook carries a stable
    /// identifier of its own (an OFX-style FITID column does appear in some bank Excel exports),
    /// otherwise <c>Derive</c> with the row's position in the FILE, not its position in the returned
    /// list. A row that escaped without a key would be saved and reported as
    /// <c>ImportedWithoutKeyCheck</c> — an unchecked row rather than an assumed-unique one.
    /// </para>
    /// </remarks>
    public Task<IReadOnlyList<Transaction>> ImportAsync(Pine.Core.Accounts.Account targetAccount, Stream fileStream, ImportProfile profile, CancellationToken cancellationToken = default)
    {
        var transactions = new List<Transaction>();

        using var reader = ExcelReaderFactory.CreateReader(fileStream);
        var result = reader.AsDataSet();

        if (result.Tables.Count == 0)
        {
            return Task.FromResult((IReadOnlyList<Transaction>)transactions);
        }

        var table = result.Tables[0];

        // The header row is data like any other to ExcelDataReader — AsDataSet names the columns
        // Column1, Column2, ... and yields row 0 as content — so the profile, not the reader, decides
        // whether the first row is a header.
        var firstDataRow = profile.HasHeaderRecord ? 1 : 0;
        var headers = profile.HasHeaderRecord && table.Rows.Count > 0
            ? BuildHeaderIndex(table.Rows[0])
            : null;

        // Required mapping keys
        int dateCol = ResolveColumn(profile, "Date", headers, table.Columns.Count);
        int payeeCol = ResolveColumn(profile, "Payee", headers, table.Columns.Count);
        int amountCol = ResolveColumn(profile, "Amount", headers, table.Columns.Count);
        int categoryCol = ResolveColumn(profile, "Category", headers, table.Columns.Count);
        int notesCol = ResolveColumn(profile, "Notes", headers, table.Columns.Count);

        // Optional. A source that carries its own stable identifier (an OFX FITID, a bank's transaction
        // reference) can expose it through this mapping and get a key that survives a re-export in a
        // different row order, which a derived key cannot. It is a plain extra entry in the existing
        // free-form ColumnMappings dictionary, so profiles that do not set it are unaffected and no
        // schema change or migration is involved.
        int referenceCol = ResolveColumn(profile, "Reference", headers, table.Columns.Count);

        // The only source identifier reachable from inside ImportAsync. The honest consequence: keys are
        // stable only while the profile NAME is stable — re-importing the same bank file under a renamed
        // or duplicated profile derives different keys, so every row will look new. Renaming a profile is
        // therefore a data event, not a cosmetic one.
        var source = profile.Name;

        for (int i = firstDataRow; i < table.Rows.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var row = table.Rows[i];

            // Counts the row's position in the FILE among the rows an importer is asked to read,
            // incremented for every row considered, including the ones rejected below. Deliberately not
            // the index into `transactions`: an output index shifts whenever a row ahead of it is
            // rejected, so a parser change (or a blank row read differently) would renumber every later
            // row and their keys would stop matching the ones already stored. It is measured from the
            // first DATA row so that the same statement exported as CSV and as XLSX — where CsvHelper
            // has already consumed the header — numbers its rows identically.
            var rowIndex = i - firstDataRow;

            // Set by every mapped cell that holds something. It exists because CSV and Excel disagree
            // about what a blank row looks like: CsvHelper hands back empty STRINGS, which fail the
            // "no amount and no payee" test below, whereas a blank spreadsheet cell is DBNull, which
            // never reaches the payee variable and leaves it at its "Unknown" default. Without this
            // flag the trailing blank rows every bank export carries would import as "Unknown"
            // transactions of zero — new junk that the CSV path does not produce.
            var rowHasValue = false;

            // Date
            DateTimeOffset date = _timeProvider.GetUtcNow();
            if (TryGetCell(row, dateCol, out var dateObj))
            {
                rowHasValue |= !IsBlank(dateObj);

                if (dateObj is DateTime dateValue)
                {
                    // The offset convention matches the CSV path: a DateTimeOffset built from a
                    // zone-less DateTime takes the local offset, exactly as TryParseExact with
                    // DateTimeStyles.None does for "2026-01-15". Only the calendar date reaches the
                    // import key, so the two paths agree on what the row identifies.
                    date = new DateTimeOffset(dateValue);
                }
                else if (dateObj is string dateStr)
                {
                    if (DateTimeOffset.TryParseExact(dateStr, profile.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                    {
                        date = parsedDate;
                    }
                }
            }

            // Payee
            string payee = "Unknown";
            if (TryGetCell(row, payeeCol, out var payeeObj))
            {
                rowHasValue |= !IsBlank(payeeObj);
                payee = CellToText(payeeObj).Trim();
            }

            // Amount
            decimal rawAmount = 0m;
            if (TryGetCell(row, amountCol, out var amountObj))
            {
                rowHasValue |= !IsBlank(amountObj);
                if (TryCellToDecimal(amountObj, out var parsedAmount))
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
            if (TryGetCell(row, categoryCol, out var catObj))
            {
                rowHasValue |= !IsBlank(catObj);
                category = CellToText(catObj).Trim();
            }

            // Notes
            string notes = "";
            if (TryGetCell(row, notesCol, out var notesObj))
            {
                rowHasValue |= !IsBlank(notesObj);
                notes = CellToText(notesObj).Trim();
            }

            // Reference (optional)
            string reference = "";
            if (TryGetCell(row, referenceCol, out var referenceObj))
            {
                rowHasValue |= !IsBlank(referenceObj);
                reference = CellToText(referenceObj).Trim();
            }

            // Validate that we have the bare minimum
            if (!rowHasValue || (rawAmount == 0 && string.IsNullOrEmpty(payee)))
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

        return Task.FromResult((IReadOnlyList<Transaction>)transactions);
    }

    /// <summary>
    /// Maps the text of each header cell to its column index.
    /// </summary>
    /// <remarks>
    /// Matching is ordinal case-insensitive on the trimmed text, because a header is a human label and
    /// an export that capitalises "AMOUNT" one quarter and "Amount" the next must not silently stop
    /// mapping. The FIRST occurrence of a duplicated header wins, which is the same column CsvHelper
    /// would hand back for that name.
    /// </remarks>
    private static Dictionary<string, int> BuildHeaderIndex(DataRow headerRow)
    {
        var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (int column = 0; column < headerRow.Table.Columns.Count; column++)
        {
            var text = CellToText(headerRow[column]).Trim();
            if (text.Length > 0)
            {
                headers.TryAdd(text, column);
            }
        }

        return headers;
    }

    /// <summary>
    /// Resolves one <see cref="ImportProfile.ColumnMappings"/> entry to a zero-based column index, or
    /// <c>-1</c> when the mapping is absent or names nothing in this sheet.
    /// </summary>
    /// <remarks>
    /// The header name is tried before the numeric index so that a sheet whose columns are labelled
    /// "1", "2", "3" resolves by its own labels rather than by position — the labels are what the user
    /// saw when they built the profile.
    /// </remarks>
    private static int ResolveColumn(ImportProfile profile, string field, Dictionary<string, int>? headers, int columnCount)
    {
        var mapping = profile.ColumnMappings.GetValueOrDefault(field);
        if (string.IsNullOrWhiteSpace(mapping))
        {
            return -1;
        }

        mapping = mapping.Trim();

        if (headers is not null && headers.TryGetValue(mapping, out var byName))
        {
            return byName;
        }

        if (int.TryParse(mapping, NumberStyles.Integer, CultureInfo.InvariantCulture, out var byIndex)
            && byIndex >= 0
            && byIndex < columnCount)
        {
            return byIndex;
        }

        return -1;
    }

    /// <summary>
    /// Reads one cell, treating an unmapped column, an out-of-range index and an empty cell alike as
    /// "not present" so the caller keeps the field's default.
    /// </summary>
    private static bool TryGetCell(DataRow row, int columnIndex, out object value)
    {
        value = null!;

        if (columnIndex < 0 || columnIndex >= row.Table.Columns.Count)
        {
            return false;
        }

        var cell = row[columnIndex];
        if (cell is null or DBNull)
        {
            return false;
        }

        value = cell;
        return true;
    }

    /// <summary>
    /// Whether a cell holds nothing a row could be built from — an empty or whitespace-only string.
    /// </summary>
    /// <remarks>
    /// A numeric or date cell is never blank: a spreadsheet that stores <c>0</c> stored it on purpose,
    /// and treating it as absent would drop a genuine zero-amount row.
    /// </remarks>
    private static bool IsBlank(object cell) => cell is string text && string.IsNullOrWhiteSpace(text);

    /// <summary>
    /// Renders a typed cell as the text a CSV export of the same sheet would have carried.
    /// </summary>
    /// <remarks>
    /// A payee, category or reference column can legitimately hold a number (an account or FITID column
    /// read as a <see cref="double"/> is the common case), and dropping it because it was not a string
    /// would lose the very identifier the reference mapping exists to use.
    /// <see cref="CultureInfo.InvariantCulture"/> is pinned so the rendered text — and therefore the
    /// derived import key — does not depend on the machine's locale.
    /// </remarks>
    private static string CellToText(object cell) => cell switch
    {
        string text => text,
        DateTime dateValue => dateValue.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => cell.ToString() ?? string.Empty,
    };

    /// <summary>
    /// Converts an amount cell to a decimal, handling the numeric cell a spreadsheet normally stores
    /// and the text cell an export that formats its own currency produces.
    /// </summary>
    /// <remarks>
    /// The double path checks <see cref="double.IsFinite"/> and the decimal range first: a NaN or an
    /// infinity reaching the cast would throw <see cref="OverflowException"/> and abort the whole
    /// import, whereas returning <c>false</c> leaves the row at its <c>0</c> default and lets the
    /// remaining rows through — the bad-row rule <see cref="CsvImporter"/> follows.
    /// The string path strips the same "$" and thousands separators CSV strips.
    /// </remarks>
    private static bool TryCellToDecimal(object cell, out decimal value)
    {
        switch (cell)
        {
            case decimal decimalValue:
                value = decimalValue;
                return true;

            case double doubleValue:
                if (double.IsFinite(doubleValue) && doubleValue >= (double)decimal.MinValue && doubleValue <= (double)decimal.MaxValue)
                {
                    value = (decimal)doubleValue;
                    return true;
                }

                value = 0m;
                return false;

            case float floatValue:
                if (float.IsFinite(floatValue))
                {
                    value = (decimal)floatValue;
                    return true;
                }

                value = 0m;
                return false;

            case int intValue:
                value = intValue;
                return true;

            case long longValue:
                value = longValue;
                return true;

            case string amountStr:
                return decimal.TryParse(amountStr.Replace("$", "").Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out value);

            default:
                value = 0m;
                return false;
        }
    }
}
