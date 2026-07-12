using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Core.Importing;

namespace Pine.Application.Importing.Commands;

public record CreateImportProfileCommand(
    string Name,
    string DateFormat,
    char Delimiter,
    bool HasHeaderRecord,
    bool InvertAmounts,
    Dictionary<string, string> ColumnMappings
) : IRequest<ImportProfile>;

public sealed class CreateImportProfileCommandHandler : IRequestHandler<CreateImportProfileCommand, ImportProfile>
{
    private readonly IImportProfileRepository _repository;
    private readonly TimeProvider _timeProvider;

    public CreateImportProfileCommandHandler(IImportProfileRepository repository, TimeProvider timeProvider)
    {
        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async Task<ImportProfile> Handle(CreateImportProfileCommand request, CancellationToken cancellationToken)
    {
        var profile = ImportProfile.Create(request.Name, _timeProvider);
        
        profile.UpdateSettings(
            request.DateFormat, 
            request.Delimiter, 
            request.HasHeaderRecord, 
            request.InvertAmounts, 
            request.ColumnMappings, 
            _timeProvider);

        await _repository.SaveAsync(profile, cancellationToken);
        
        return profile;
    }
}
