using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Core.Importing;
using Pine.Core.Transactions;

namespace Pine.Application.Transactions.Commands;

public record ImportTransactionsCommand(
    Stream FileStream,
    IImporter Importer,
    ImportProfile Profile
) : IRequest<IReadOnlyList<Transaction>>;

public sealed class ImportTransactionsCommandHandler : IRequestHandler<ImportTransactionsCommand, IReadOnlyList<Transaction>>
{
    // Normally this would also take an ITransactionRepository to save the transactions.
    public ImportTransactionsCommandHandler()
    {
    }

    public async Task<IReadOnlyList<Transaction>> Handle(ImportTransactionsCommand request, CancellationToken cancellationToken)
    {
        // Use the selected importer to parse the file
        var transactions = await request.Importer.ImportAsync(request.FileStream, request.Profile, cancellationToken);
        
        // TODO: Validate against rules, deduplicate against existing transactions, and save to repository.
        // For Phase 2 baseline, we just return the imported transactions.
        
        return transactions;
    }
}
