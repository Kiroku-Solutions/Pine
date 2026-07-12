using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Pine.Core.Importing;
using Pine.Core.Transactions;
using UglyToad.PdfPig;

namespace Pine.Infrastructure.Importing;

public sealed class PdfImporter : IImporter
{
    private readonly TimeProvider _timeProvider;

    public PdfImporter(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public Task<IReadOnlyList<Transaction>> ImportAsync(Pine.Core.Accounts.Account targetAccount, Stream fileStream, ImportProfile profile, CancellationToken cancellationToken = default)
    {
        var transactions = new List<Transaction>();

        using var document = PdfDocument.Open(fileStream);
        
        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = page.Text;
            
            // Placeholder logic: We extract the raw text blocks and would 
            // typically pipe this through the AI / Rule engine to extract transactions.
        }

        return Task.FromResult((IReadOnlyList<Transaction>)transactions);
    }
}
