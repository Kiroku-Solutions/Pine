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

    public Task<IReadOnlyList<Transaction>> ImportAsync(Guid targetAccountId, Stream fileStream, ImportProfile profile, CancellationToken cancellationToken = default)
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
