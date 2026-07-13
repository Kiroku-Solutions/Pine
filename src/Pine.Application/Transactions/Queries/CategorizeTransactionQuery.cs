using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Core.Ai;
using Pine.Core.Categories;

namespace Pine.Application.Transactions.Queries;

public record CategorizeTransactionQuery(string TransactionText, decimal Amount) : IRequest<string?>;

public sealed class CategorizeTransactionQueryHandler : IRequestHandler<CategorizeTransactionQuery, string?>
{
    private readonly IAIProvider _aiProvider;
    private readonly ICategoryRepository _categoryRepository;

    public CategorizeTransactionQueryHandler(IAIProvider aiProvider, ICategoryRepository categoryRepository)
    {
        _aiProvider = aiProvider;
        _categoryRepository = categoryRepository;
    }

    public async Task<string?> Handle(CategorizeTransactionQuery request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TransactionText))
            return null;

        var result = await _aiProvider.CategorizeTransactionAsync(request.TransactionText, request.Amount, cancellationToken);
        
        // If confidence is too low or no category predicted, return null
        if (result.CategoryId == Guid.Empty || result.ConfidenceScore < 0.3)
        {
            return null;
        }

        var category = await _categoryRepository.GetByIdAsync(result.CategoryId, cancellationToken);
        return category?.Name;
    }
}
