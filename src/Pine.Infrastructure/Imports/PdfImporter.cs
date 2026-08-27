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

    /// <remarks>
    /// <para>
    /// This importer parses NO rows today: it opens the document, reads each page's text, and returns an
    /// empty list. It therefore assigns no import keys.
    /// </para>
    /// <para>
    /// Whoever makes it produce transactions MUST call
    /// <see cref="Transaction.AssignImportKey"/> on each one before returning it. A PDF statement
    /// generally carries no stable per-row identifier, so this will in practice mean
    /// <c>ImportKeyFactory.Derive</c> with the row's position in the FILE, not its position in the
    /// returned list; use <c>ImportKeyFactory.FromReference</c> only if the statement really does print a
    /// reference the bank guarantees.
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
