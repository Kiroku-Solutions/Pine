using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Core.Importing;

namespace Pine.Application.Importing.Queries;

public record GetImportProfilesQuery() : IRequest<IReadOnlyList<ImportProfile>>;

public sealed class GetImportProfilesQueryHandler : IRequestHandler<GetImportProfilesQuery, IReadOnlyList<ImportProfile>>
{
    private readonly IImportProfileRepository _repository;

    public GetImportProfilesQueryHandler(IImportProfileRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<ImportProfile>> Handle(GetImportProfilesQuery request, CancellationToken cancellationToken)
    {
        return await _repository.GetAllAsync(cancellationToken);
    }
}
