using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Application.Common;
using Pine.Core.Importing;

namespace Pine.Application.Importing.Commands;

public record CreateImportProfileCommand(
    string Name,
    string DateFormat,
    char Delimiter,
    bool HasHeaderRecord,
    bool InvertAmounts,
    Dictionary<string, string> ColumnMappings
) : IRequest<ImportProfile>, ITransactionalRequest;

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

/// <summary>
/// Refuses an import profile that could never be applied to a real import, before it is saved
/// (TD-151). House convention (TD-152's own validator established it): every command that writes
/// gets one.
/// </summary>
/// <remarks>
/// <para>
/// WHY THE DATE FORMAT IS PARSED, NOT PATTERN-MATCHED. <c>DateTimeOffset.TryParseExact</c> is the
/// only thing that actually proves a format string works with .NET's own parser --  a hand-written
/// regex of "valid custom format characters" would accept strings that still throw at import time,
/// and reject legitimate ones it did not anticipate (two-letter vs four-letter year tokens, for
/// one). Formatting today's date with the string and parsing it back is exactly what
/// <see cref="Pine.Infrastructure.Importing.CsvImporter"/> will do to every row the profile is ever
/// used against.
/// </para>
/// <para>
/// WHY DATE AND AMOUNT ARE THE ONLY MAPPINGS REQUIRED. Payee, Category and Notes are optional on
/// both importers (TD-123's own description of <c>ImportProfile</c>) -- a transaction with no payee
/// falls back to "Unknown", and no category simply adds no split. A row with neither a parseable
/// date nor an amount is not a transaction at all, so a profile that cannot ever supply them is not
/// one the mapping step should ever let through.
/// </para>
/// </remarks>
public sealed class CreateImportProfileCommandValidator : IRequestValidator<CreateImportProfileCommand>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<ValidationError>> ValidateAsync(
        CreateImportProfileCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        IReadOnlyList<ValidationError> errors = ImportProfileValidation.Validate(
            request.Name,
            request.DateFormat,
            request.ColumnMappings,
            requireName: true);

        return Task.FromResult(errors);
    }
}

/// <summary>
/// The refusal rules <see cref="CreateImportProfileCommandValidator"/> and
/// <see cref="UpdateImportProfileCommandValidator"/> share. Pulled out rather than duplicated,
/// because the two commands validate the same settings -- only the name requirement differs, since
/// <see cref="UpdateImportProfileCommand"/> does not rename a profile.
/// </summary>
internal static class ImportProfileValidation
{
    public static IReadOnlyList<ValidationError> Validate(
        string? name,
        string dateFormat,
        Dictionary<string, string>? columnMappings,
        bool requireName)
    {
        var errors = new List<ValidationError>();

        if (requireName && string.IsNullOrWhiteSpace(name))
        {
            errors.Add(new ValidationError(
                nameof(CreateImportProfileCommand.Name),
                "An import profile must have a name, or it cannot be shown in the saved-profiles list.",
                ValidationText.ImportProfileNameRequired()));
        }

        if (string.IsNullOrWhiteSpace(dateFormat) || !IsParseableDateFormat(dateFormat))
        {
            errors.Add(new ValidationError(
                nameof(CreateImportProfileCommand.DateFormat),
                $"'{dateFormat}' is not a date format .NET can parse an exact date with.",
                ValidationText.ImportProfileDateFormatInvalid()));
        }

        var hasDate = columnMappings is not null && !string.IsNullOrWhiteSpace(columnMappings.GetValueOrDefault("Date"));
        var hasAmount = columnMappings is not null && !string.IsNullOrWhiteSpace(columnMappings.GetValueOrDefault("Amount"));

        if (!hasDate || !hasAmount)
        {
            errors.Add(new ValidationError(
                nameof(CreateImportProfileCommand.ColumnMappings),
                "An import profile must map both a Date and an Amount column; neither importer can build a transaction without them.",
                ValidationText.ImportProfileMappingIncomplete()));
        }

        return errors;
    }

    private static bool IsParseableDateFormat(string dateFormat)
    {
        try
        {
            // A ROUND TRIP, NOT MERELY A PARSE THAT SUCCEEDS. ".d" alone, or any format missing the
            // year, parses back to SOME date without throwing -- just not the one that was rendered
            // -- so success alone would accept a format that silently drops information every real
            // import needs. Comparing the parsed date against the sample is what actually proves the
            // format can carry a full calendar date, not merely that .NET can make sense of its own
            // output.
            var sample = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
            var rendered = sample.ToString(dateFormat, CultureInfo.InvariantCulture);
            return DateTimeOffset.TryParseExact(
                    rendered, dateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var roundTripped)
                && roundTripped.Date == sample.Date;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
