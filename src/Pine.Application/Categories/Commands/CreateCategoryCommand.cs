using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Application.Common;
using Pine.Core.Categories;

namespace Pine.Application.Categories.Commands;

/// <summary>
/// Creates a spending category and returns its identifier.
/// </summary>
/// <remarks>
/// Added by TD-01 to replace a direct <c>ICategoryRepository.SaveAsync</c> call in
/// <c>DiagnosticsViewModel</c>. Once repositories stopped flushing on their own, that call staged
/// a category nothing ever committed, and the <c>CreateBudgetCommand</c> sent immediately
/// afterwards referenced a category id that did not exist in the database.
/// </remarks>
/// <param name="Name">Display name of the category.</param>
/// <param name="ColorHex">Swatch colour, a hex code starting with '#'.</param>
/// <param name="ParentCategoryId">Optional parent category, for sub-categories.</param>
public record CreateCategoryCommand(
    string Name,
    string ColorHex,
    Guid? ParentCategoryId = null) : IRequest<Guid>, ITransactionalRequest;

public sealed class CreateCategoryCommandHandler : IRequestHandler<CreateCategoryCommand, Guid>
{
    private readonly ICategoryRepository _categoryRepository;

    public CreateCategoryCommandHandler(ICategoryRepository categoryRepository)
    {
        _categoryRepository = categoryRepository;
    }

    public async Task<Guid> Handle(CreateCategoryCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var category = Category.Create(request.Name, request.ColorHex, request.ParentCategoryId);
        await _categoryRepository.SaveAsync(category, cancellationToken).ConfigureAwait(false);

        return category.Id;
    }
}
