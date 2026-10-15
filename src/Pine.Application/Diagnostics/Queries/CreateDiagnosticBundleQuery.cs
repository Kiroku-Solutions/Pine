using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Core.Configuration;
using Pine.Core.Diagnostics;

namespace Pine.Application.Diagnostics.Queries;

/// <summary>The finished bundle: the zip bytes and the file name to offer the user (TD-84).</summary>
/// <param name="Content">The zip archive.</param>
/// <param name="SuggestedFileName">A file name carrying only a UTC timestamp.</param>
public sealed record DiagnosticBundle(byte[] Content, string SuggestedFileName);

/// <summary>
/// Builds the user-triggered diagnostic bundle (TD-84): redacted logs plus non-sensitive environment
/// facts, in memory. Nothing is written to disk here; the user picks the destination in the UI.
/// </summary>
/// <remarks>
/// A query, not a command: it reads and writes nothing in the vault, so it opens no transaction.
/// </remarks>
/// <param name="MaxLogEntries">Ceiling on log records packed.</param>
public sealed record CreateDiagnosticBundleQuery(int MaxLogEntries = 1000) : IRequest<DiagnosticBundle>;

/// <summary>Handles <see cref="CreateDiagnosticBundleQuery"/>.</summary>
/// <remarks>
/// WHAT IS IN THE ZIP, AND WHY EACH PART IS SAFE.
/// <list type="bullet">
/// <item><description><c>environment.json</c>: app version, OS description, migration head, and row
/// counts for accounts, transactions and budgets. Counts, never rows.</description></item>
/// <item><description><c>settings.json</c>: every setting KEY. A value is included only for the
/// allow-listed appearance/currency keys; every other key's value is replaced, and a key the
/// redactor calls sensitive is marked as such. New settings are therefore private by default.</description></item>
/// <item><description><c>logs.txt</c>: the log tail, with every field re-run through the redactor
/// (paths, e-mail, amounts, long digit runs, values of sensitive keys).</description></item>
/// </list>
/// NOT IN THE ZIP: any ledger content, the vault file, key material, the machine or user name, or any
/// filesystem path. The redactor is a net, not a proof (see <c>LogRedactor</c>): a payee mentioned in
/// free prose with no key is not recognisable, which is why the sink also refuses to log such values.
/// </remarks>
public sealed class CreateDiagnosticBundleQueryHandler : IRequestHandler<CreateDiagnosticBundleQuery, DiagnosticBundle>
{
    /// <summary>Setting value shown for a key that is not allow-listed.</summary>
    public const string ValueOmitted = "<omitted>";

    /// <summary>Setting value shown for a key the redactor names as sensitive.</summary>
    public const string ValueSensitive = "<redacted>";

    private static readonly HashSet<string> ValueAllowList = new(StringComparer.Ordinal)
    {
        "Appearance.Theme",
        "Appearance.Palette",
        "Appearance.Language",
        "Vault.BaseCurrency",
        "Ai.Categorization.TrainingConsent",
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly ILogTailReader _logTailReader;
    private readonly IRuntimeInfoProvider _runtimeInfo;
    private readonly ISettingRepository _settings;
    private readonly IBundleEnvironmentSource _environment;
    private readonly IDiagnosticTextRedactor _redactor;
    private readonly IBundleSummarySource? _summaries;
    private readonly TimeProvider _time;

    /// <summary>Marker written for any summary the app does not record (TD-84).</summary>
    public const string NotRecorded = "not recorded";

    /// <summary>Creates the handler.</summary>
    public CreateDiagnosticBundleQueryHandler(
        ILogTailReader logTailReader,
        IRuntimeInfoProvider runtimeInfo,
        ISettingRepository settings,
        IBundleEnvironmentSource environment,
        IDiagnosticTextRedactor redactor,
        TimeProvider? time = null,
        IBundleSummarySource? summaries = null)
    {
        ArgumentNullException.ThrowIfNull(logTailReader);
        ArgumentNullException.ThrowIfNull(runtimeInfo);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(redactor);
        _logTailReader = logTailReader;
        _runtimeInfo = runtimeInfo;
        _settings = settings;
        _environment = environment;
        _redactor = redactor;
        _time = time ?? TimeProvider.System;
        _summaries = summaries;
    }

    /// <inheritdoc />
    public async Task<DiagnosticBundle> Handle(CreateDiagnosticBundleQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = _time.GetUtcNow();
        var snapshot = await _environment.ReadAsync(cancellationToken).ConfigureAwait(false);
        var settings = (await _settings.GetAllAsync(cancellationToken).ConfigureAwait(false)).ToList();
        var summarySnapshot = _summaries is null
            ? BundleSummariesSnapshot.Unavailable
            : await _summaries.ReadAsync(cancellationToken).ConfigureAwait(false);
        var tail = await _logTailReader.ReadTailAsync(Math.Max(1, request.MaxLogEntries), cancellationToken).ConfigureAwait(false);

        var environment = new
        {
            generatedUtc = now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            appVersion = Clean(_runtimeInfo.AppVersion),
            os = Clean(_runtimeInfo.OsDescription),
            migrationHead = MigrationHead(snapshot.MigrationHead),
            counts = new
            {
                accounts = snapshot.AccountCount,
                transactions = snapshot.TransactionCount,
                budgets = snapshot.BudgetCount,
            },
        };

        var settingsEntries = settings
            .OrderBy(s => s.Key, StringComparer.Ordinal)
            .Select(s => new { key = Clean(s.Key), value = DescribeValue(s.Key, s.Value) })
            .ToList();

        var logs = new StringBuilder();
        foreach (var entry in tail)
        {
            logs.Append(entry.Timestamp.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture))
                .Append(" [").Append(Clean(entry.Level)).Append("] ")
                .Append(Clean(entry.Category)).Append(": ")
                .AppendLine(Clean(entry.Message));
        }

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "environment.json", JsonSerializer.Serialize(environment, JsonOptions));
            Add(zip, "settings.json", JsonSerializer.Serialize(settingsEntries, JsonOptions));
            Add(zip, "summaries.json", JsonSerializer.Serialize(BuildSummaries(summarySnapshot), JsonOptions));
            Add(zip, "logs.txt", logs.ToString());
        }

        var name = string.Create(CultureInfo.InvariantCulture, $"pine-diagnostics-{now.UtcDateTime:yyyyMMdd'T'HHmmss'Z'}.zip");

        return new DiagnosticBundle(buffer.ToArray(), name);
    }

    // Counts and timestamps only. The app has no metrics or SLO instrumentation and no import-run
    // record, so those fields say "not recorded" rather than inventing numbers. Every string still
    // goes through the redactor (the failure reason is a type or refusal name, never a message).
    private object BuildSummaries(BundleSummariesSnapshot snapshot)
    {
        object? lastSync = snapshot.LastSync is null
            ? NotRecorded
            : new
            {
                ranUtc = snapshot.LastSync.RanAtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                outcome = Clean(snapshot.LastSync.Outcome),
                failureReason = snapshot.LastSync.FailureReason is null ? null : Clean(snapshot.LastSync.FailureReason),
                envelopesUp = snapshot.LastSync.EnvelopesUp,
                envelopesDown = snapshot.LastSync.EnvelopesDown,
                conflictsDetected = snapshot.LastSync.ConflictsDetected,
                conflictsResolved = snapshot.LastSync.ConflictsResolved,
            };

        return new
        {
            slo = NotRecorded,
            metrics = NotRecorded,
            sync = new
            {
                attempts = snapshot.SyncAttemptCount is { } n ? (object)n : NotRecorded,
                lastRun = lastSync,
            },
            imports = new
            {
                runs = NotRecorded,
                rowsImported = NotRecorded,
                rowsSkipped = NotRecorded,
                zeroAmountImportedTransactions = snapshot.ZeroAmountImportedTransactionCount is { } z ? (object)z : NotRecorded,
            },
        };
    }

    private string DescribeValue(string key, string value)
    {
        if (_redactor.IsSensitiveKey(key))
        {
            return ValueSensitive;
        }

        return ValueAllowList.Contains(key) ? Clean(value) : ValueOmitted;
    }

    // An EF migration id is "yyyyMMddHHmmss_Name", compiled into the app and not user data; the
    // digit-run rule would otherwise mangle it into "<digits>_Name" and defeat its purpose. Any
    // other shape still goes through the redactor.
    private string MigrationHead(string? value) =>
        value is not null && System.Text.RegularExpressions.Regex.IsMatch(value, @"^\d{8,14}_[A-Za-z0-9_]+$", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(50))
            ? value
            : Clean(value);

    private string Clean(string? value) => _redactor.Redact(value) ?? string.Empty;

    private static void Add(ZipArchive zip, string name, string text)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(text);
    }
}
