using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ExcelDataReader;
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
    /// This importer parses NO rows today: it opens the workbook, walks it, and returns an empty list.
    /// It therefore assigns no import keys, and nothing here has been verified against a real export.
    /// </para>
    /// <para>
    /// Whoever makes it produce transactions MUST call
    /// <see cref="Transaction.AssignImportKey"/> on each one before returning it —
    /// <c>ImportKeyFactory.FromReference</c> if the workbook carries a stable identifier of its own (an
    /// OFX-style FITID column does appear in some bank Excel exports), otherwise
    /// <c>ImportKeyFactory.Derive</c> with the row's position in the FILE, not its position in the
    /// returned list.
    /// </para>
    /// <para>
    /// If a row ever escapes without a key, it is not silently trusted: the import handler saves it and
    /// reports it as <c>ImportedWithoutKeyCheck</c>, so the user sees an unchecked row rather than an
    /// assumed-unique one.
    /// </para>
    /// </remarks>
    public Task<IReadOnlyList<Transaction>> ImportAsync(Pine.Core.Accounts.Account targetAccount, Stream fileStream, ImportProfile profile, CancellationToken cancellationToken = default)
    {
        var transactions = new List<Transaction>();

        using var reader = ExcelReaderFactory.CreateReader(fileStream);
        var result = reader.AsDataSet();

        if (result.Tables.Count > 0)
        {
            var table = result.Tables[0];
            // Iterate rows starting from index 1 (skipping header)
            for (int i = 1; i < table.Rows.Count; i++)
            {
                var row = table.Rows[i];
                // Placeholder: Extract columns based on ImportProfile mapping
                // e.g. var date = row[0]; var amount = row[1];
                
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        return Task.FromResult((IReadOnlyList<Transaction>)transactions);
    }
}
