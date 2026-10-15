using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using Pine.Application.Diagnostics.Queries;
using Pine.Core.Configuration;
using Pine.Core.Diagnostics;
using Pine.Core.Settings;
using Pine.Infrastructure.Diagnostics;
using Xunit;

namespace Pine.Tests.Unit.Application.Diagnostics;

/// <summary>
/// The diagnostic bundle (TD-84) carries redacted logs and non-sensitive environment facts and
/// nothing a user would regret attaching to a bug report.
/// </summary>
/// <remarks>
/// The log tail is seeded with RAW, unredacted records on purpose: the bundle must not rely on the
/// sink having redacted them, so the redactor wired in here is the real <see cref="LogRedactorAdapter"/>.
/// What this does not prove: that free prose naming a payee with no key is caught (the redactor
/// cannot recognise one, and an UNQUOTED multi-word value after a sensitive key is redacted only up to
/// its first space -- `payee=Starbucks Reserve` leaves `Reserve` -- which is why the seed quotes it), or anything about the Avalonia save dialog.
/// </remarks>
public class DiagnosticBundleTests
{
    private const string Payee = "Starbucks Reserve Roastery";
    private const string Amount = "1234.56";
    private const string Password = "hunter2-Correct-Horse";
    private const string Email = "jane.doe@example.com";
    private const string WinPath = @"C:\Users\camilo\Vaults\Personal\vault.db";
    private const string PosixPath = "/home/camilo/vaults/personal/vault.db";
    private const string Account = "4111111111111111";
    private const string SecretSettingValue = "super-secret-setting-value";

    private static readonly string[] Forbidden =
        [Payee, Amount, Password, Email, WinPath, PosixPath, "camilo", Account, SecretSettingValue];

    private static ILogTailReader SeededLogs()
    {
        var reader = Substitute.For<ILogTailReader>();
        var at = new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.Zero);
        IReadOnlyList<LogTailEntry> entries =
        [
            new(at, "Information", "Pine.Import", $"Imported row payee=\"{Payee}\" amount={Amount}"),
            new(at, "Warning", "Pine.Unlock", $"Unlock failed password={Password}"),
            new(at, "Error", "Pine.Vault", $"Open failed at {WinPath} and {PosixPath}"),
            new(at, "Information", "Pine.Mail", $"Notified {Email} about balance {Amount}"),
            new(at, "Information", "Pine.Acct", $"Account number {Account} seen"),
            new(at, "Information", "Pine.Ok", "Sync finished in 42 ms"),
        ];
        reader.ReadTailAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(entries);
        return reader;
    }

    private static CreateDiagnosticBundleQueryHandler NewHandler(IDiagnosticTextRedactor? redactor = null, BundleSummariesSnapshot? summarySnapshot = null)
    {
        var runtime = Substitute.For<IRuntimeInfoProvider>();
        runtime.AppVersion.Returns("1.4.0+abc");
        runtime.OsDescription.Returns("Windows 11 (X64, process X64)");

        var settings = Substitute.For<ISettingRepository>();
        IEnumerable<Setting> all =
        [
            Setting.Create("Appearance.Theme", "Dark"),
            Setting.Create("Vault.Passphrase", SecretSettingValue),
            Setting.Create("Custom.Unlisted", SecretSettingValue),
        ];
        settings.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(all));

        var environment = Substitute.For<IBundleEnvironmentSource>();
        environment.ReadAsync(Arg.Any<CancellationToken>())
            .Returns(new BundleEnvironmentSnapshot("20260101_Initial", 3, 120, 4));

        var summaries = Substitute.For<IBundleSummarySource>();
        summaries.ReadAsync(Arg.Any<CancellationToken>()).Returns(summarySnapshot ?? new BundleSummariesSnapshot(
            7,
            new BundleSyncSummary(new DateTime(2026, 10, 13, 9, 30, 0, DateTimeKind.Utc), "Failed", "IOException", 3, 5, 2, 0),
            4));

        return new CreateDiagnosticBundleQueryHandler(
            SeededLogs(), runtime, settings, environment, redactor ?? new LogRedactorAdapter(), summaries: summaries);
    }

    private static Dictionary<string, string> Unzip(DiagnosticBundle bundle)
    {
        using var zip = new ZipArchive(new MemoryStream(bundle.Content), ZipArchiveMode.Read);
        return zip.Entries.ToDictionary(
            e => e.FullName,
            e =>
            {
                using var reader = new StreamReader(e.Open(), Encoding.UTF8);
                return reader.ReadToEnd();
            });
    }

    [Fact]
    public async Task Bundle_ContainsNoSeededSensitiveValue_InAnyEntry()
    {
        var bundle = await NewHandler().Handle(new CreateDiagnosticBundleQuery(), CancellationToken.None);

        var files = Unzip(bundle);
        files.Keys.Should().BeEquivalentTo("environment.json", "settings.json", "summaries.json", "logs.txt");

        foreach (var (name, text) in files)
        {
            foreach (var secret in Forbidden)
            {
                text.Should().NotContain(secret, $"{name} must not leak '{secret}'");
            }
        }
    }

    [Fact]
    public async Task Bundle_KeepsTheNonSensitiveDiagnostics()
    {
        var files = Unzip(await NewHandler().Handle(new CreateDiagnosticBundleQuery(), CancellationToken.None));

        files["logs.txt"].Should().Contain("Sync finished in 42 ms");
        files["environment.json"].Should().Contain("1.4.0+abc")
            .And.Contain("Windows 11")
            .And.Contain("20260101_Initial")
            .And.Contain("\"transactions\": 120");
    }

    [Fact]
    public async Task Settings_ListEveryKey_ButOnlyAllowListedValues()
    {
        var settings = Unzip(await NewHandler().Handle(new CreateDiagnosticBundleQuery(), CancellationToken.None))["settings.json"];

        settings.Should().Contain("Appearance.Theme").And.Contain("Dark");
        settings.Should().Contain("Vault.Passphrase").And.Contain(CreateDiagnosticBundleQueryHandler.ValueSensitive);
        settings.Should().Contain("Custom.Unlisted").And.Contain(CreateDiagnosticBundleQueryHandler.ValueOmitted);
    }

    [Fact]
    public async Task FileName_CarriesOnlyAUtcTimestamp()
    {
        var bundle = await NewHandler().Handle(new CreateDiagnosticBundleQuery(), CancellationToken.None);

        bundle.SuggestedFileName.Should().MatchRegex(@"^pine-diagnostics-\d{8}T\d{6}Z\.zip$");
    }

    /// <summary>
    /// Guards the guard: a pass-through redactor must make the seeded values visible, otherwise the
    /// leak test above could pass for the wrong reason.
    /// </summary>
    [Fact]
    public async Task WithoutRedaction_TheSeededValuesDoAppear()
    {
        var passThrough = Substitute.For<IDiagnosticTextRedactor>();
        passThrough.Redact(Arg.Any<string?>()).Returns(call => call.Arg<string?>());
        passThrough.IsSensitiveKey(Arg.Any<string?>()).Returns(false);

        var logs = Unzip(await NewHandler(passThrough).Handle(new CreateDiagnosticBundleQuery(), CancellationToken.None))["logs.txt"];

        logs.Should().Contain(Payee).And.Contain(Password).And.Contain(Email).And.Contain(WinPath);
    }

    [Fact]
    public async Task Summaries_CarryCountsAndTimestamps_AndMarkUnrecordedSources()
    {
        var text = Unzip(await NewHandler().Handle(new CreateDiagnosticBundleQuery(), CancellationToken.None))["summaries.json"];

        using var doc = System.Text.Json.JsonDocument.Parse(text);
        var root = doc.RootElement;
        root.GetProperty("slo").GetString().Should().Be(CreateDiagnosticBundleQueryHandler.NotRecorded);
        root.GetProperty("metrics").GetString().Should().Be(CreateDiagnosticBundleQueryHandler.NotRecorded);
        var sync = root.GetProperty("sync");
        sync.GetProperty("attempts").GetInt32().Should().Be(7);
        var last = sync.GetProperty("lastRun");
        last.GetProperty("ranUtc").GetString().Should().Be("2026-10-13T09:30:00Z");
        last.GetProperty("outcome").GetString().Should().Be("Failed");
        last.GetProperty("envelopesUp").GetInt32().Should().Be(3);
        last.GetProperty("envelopesDown").GetInt32().Should().Be(5);
        last.GetProperty("conflictsDetected").GetInt32().Should().Be(2);
        var imports = root.GetProperty("imports");
        imports.GetProperty("zeroAmountImportedTransactions").GetInt32().Should().Be(4);
        imports.GetProperty("rowsImported").GetString().Should().Be(CreateDiagnosticBundleQueryHandler.NotRecorded);
        imports.GetProperty("rowsSkipped").GetString().Should().Be(CreateDiagnosticBundleQueryHandler.NotRecorded);
    }

    [Fact]
    public async Task Summaries_WhenVaultUnreadable_SayNotRecorded()
    {
        var text = Unzip(await NewHandler(summarySnapshot: BundleSummariesSnapshot.Unavailable)
            .Handle(new CreateDiagnosticBundleQuery(), CancellationToken.None))["summaries.json"];

        using var doc = System.Text.Json.JsonDocument.Parse(text);
        doc.RootElement.GetProperty("sync").GetProperty("lastRun").GetString().Should().Be(CreateDiagnosticBundleQueryHandler.NotRecorded);
        doc.RootElement.GetProperty("sync").GetProperty("attempts").GetString().Should().Be(CreateDiagnosticBundleQueryHandler.NotRecorded);
    }

    /// <summary>A hostile failure reason (e.g. a message with a path or e-mail) must still be redacted.</summary>
    [Fact]
    public async Task Summaries_RedactAFailureReasonThatCarriesSensitiveText()
    {
        var hostile = new BundleSummariesSnapshot(1,
            new BundleSyncSummary(DateTime.UtcNow, "Failed", $"open {WinPath} for {Email}", 0, 0, 0, 0), 0);

        var text = Unzip(await NewHandler(summarySnapshot: hostile)
            .Handle(new CreateDiagnosticBundleQuery(), CancellationToken.None))["summaries.json"];

        text.Should().NotContain(WinPath).And.NotContain(Email).And.NotContain("camilo");
    }
}
