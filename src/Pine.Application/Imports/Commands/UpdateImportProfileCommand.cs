using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Application.Common;
using Pine.Core.Importing;

namespace Pine.Application.Importing.Commands;

public record UpdateImportProfileCommand(
    Guid Id,
    string DateFormat,
    char Delimiter,
    bool HasHeaderRecord,
    bool InvertAmounts,
    Dictionary<string, string> ColumnMappings
) : IRequest<ImportProfile>, ITransactionalRequest;

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

/// <summary>
/// Refuses an update that would leave a saved profile unusable, or that names a profile which is
/// not there to update (TD-151).
/// </summary>
/// <remarks>
/// Shares its setting rules with <see cref="CreateImportProfileCommandValidator"/> through
/// <see cref="ImportProfileValidation"/>; the one rule this validator adds is the existence check,
/// because <see cref="UpdateImportProfileCommand"/> -- unlike <c>Create</c> -- names an id that must
/// already be there.
/// </remarks>
public sealed class UpdateImportProfileCommandValidator : IRequestValidator<UpdateImportProfileCommand>
{
    private readonly IImportProfileRepository _repository;

    /// <summary>Creates the validator.</summary>
    /// <param name="repository">Read for the existence check.</param>
    public UpdateImportProfileCommandValidator(IImportProfileRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ValidationError>> ValidateAsync(
        UpdateImportProfileCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<ValidationError>(ImportProfileValidation.Validate(
            name: null,
            request.DateFormat,
            request.ColumnMappings,
            requireName: false));

        var existing = await _repository.GetByIdAsync(request.Id, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            errors.Add(new ValidationError(
                nameof(UpdateImportProfileCommand.Id),
                $"Import profile '{request.Id}' does not exist.",
                ValidationText.ImportProfileNotFound()));
        }

        return errors;
    }
}
