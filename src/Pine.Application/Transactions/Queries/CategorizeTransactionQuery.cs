using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Options;
using Pine.Core.Ai;
using Pine.Core.Configuration;

namespace Pine.Application.Transactions.Queries;

public record CategorizeTransactionQuery(string TransactionText, decimal Amount) : IRequest<string?>;

public sealed class CategorizeTransactionQueryHandler : IRequestHandler<CategorizeTransactionQuery, string?>
{
    private readonly IAIProvider _aiProvider;
    private readonly PineAiOptions _aiOptions;

    /// <summary>Creates the handler.</summary>
    /// <param name="aiProvider">The categoriser.</param>
    /// <param name="aiOptions">
    /// Carries the confidence threshold that used to be the literal <c>0.3</c> here and, separately,
    /// in <c>ImportTransactionsCommandHandler</c> (TD-27).
    /// </param>
    /// <remarks>
    /// <c>ICategoryRepository</c> WAS REMOVED FROM THIS HANDLER (TD-32). It existed to turn a
    /// predicted <c>Guid</c> into a name, against a <c>Categories</c> table that nothing in this
    /// application has ever inserted a row into — so the lookup returned <c>null</c> and this
    /// handler returned <c>null</c> for every prediction the model produced, however confident.
    /// The categoriser now predicts the name directly, which is what the ledger stores.
    /// </remarks>
    public CategorizeTransactionQueryHandler(
        IAIProvider aiProvider,
        IOptions<PineAiOptions> aiOptions)
    {
        ArgumentNullException.ThrowIfNull(aiOptions);

        _aiProvider = aiProvider;
        _aiOptions = aiOptions.Value;
    }

    public async Task<string?> Handle(CategorizeTransactionQuery request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TransactionText))
            return null;

        var result = await _aiProvider.CategorizeTransactionAsync(request.TransactionText, request.Amount, cancellationToken);
        
        // If confidence is too low or no category predicted, return null.
        //
        // THE THRESHOLD IS SHARED WITH THE IMPORT PATH ON PURPOSE. It was written as a literal here
        // and again in ImportTransactionsCommandHandler, and nothing tied the two numbers together
        // -- so a change to one would have left import auto-applying categories this path considered
        // too weak to even show the user.
        if (string.IsNullOrWhiteSpace(result.Category) || result.ConfidenceScore < _aiOptions.CategorisationConfidenceThreshold)
        {
            return null;
        }

        return result.Category;
    }
}
