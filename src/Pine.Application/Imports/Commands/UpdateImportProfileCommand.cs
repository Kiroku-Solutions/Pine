using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Core.Importing;

namespace Pine.Application.Importing.Commands;

public record UpdateImportProfileCommand(
    Guid Id,
    string DateFormat,
    char Delimiter,
    bool HasHeaderRecord,
    bool InvertAmounts,
    Dictionary<string, string> ColumnMappings
) : IRequest<ImportProfile>;

public sealed class UpdateImportProfileCommandHandler : IRequestHandler<UpdateImportProfileCommand, ImportProfile>
{
    private readonly IImportProfileRepository _repository;
    private readonly TimeProvider _timeProvider;

    public UpdateImportProfileCommandHandler(IImportProfileRepository repository, TimeProvider timeProvider)
    {
        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async Task<ImportProfile> Handle(UpdateImportProfileCommand request, CancellationToken cancellationToken)
    {
        var profile = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (profile == null)
        {
            throw new ArgumentException("Import profile not found");
        }

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
