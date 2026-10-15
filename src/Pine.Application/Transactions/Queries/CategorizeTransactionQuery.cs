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
    private readonly Pine.Application.Settings.IUserPreferences? _preferences;

    /// <summary>Creates the handler.</summary>
    /// <param name="aiProvider">The categoriser.</param>
    /// <param name="aiOptions">
    /// Carries the confidence threshold that used to be the literal <c>0.3</c> here and, separately,
    /// in <c>ImportTransactionsCommandHandler</c> (TD-27).
    /// </param>
    /// <remarks>
    /// <c>ICategoryRepository</c> WAS REMOVED FROM THIS HANDLER (TD-32). It existed to turn a
    /// predicted <c>Guid</c> into a name, against a <c>Categories</c> table that nothing in this
    /// application wrote to (apart from one demo "Groceries" row the Diagnostics "Test Features"
    /// button created until <c>67af1f3</c>) — so the lookup returned <c>null</c> and this handler
    /// returned <c>null</c> for effectively every prediction the model produced, however confident.
    /// The <c>Categories</c> table itself was deleted by post-audit plan item 2.3 (TD-132).
    /// The categoriser now predicts the name directly, which is what the ledger stores.
    /// </remarks>
    public CategorizeTransactionQueryHandler(
        IAIProvider aiProvider,
        IOptions<PineAiOptions> aiOptions,
        Pine.Application.Settings.IUserPreferences? preferences = null)
    {
        ArgumentNullException.ThrowIfNull(aiOptions);

        // TD-119: the confidence gate is an in-vault Setting; aiOptions is the read-fallback.
        _preferences = preferences;

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
        var threshold = _preferences is null
            ? _aiOptions.CategorisationConfidenceThreshold
            : (await _preferences.GetAsync(cancellationToken)).CategorisationConfidenceThreshold;

        if (string.IsNullOrWhiteSpace(result.Category) || result.ConfidenceScore < threshold)
        {
            return null;
        }

        return result.Category;
    }
}
