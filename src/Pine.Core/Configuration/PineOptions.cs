using Pine.Core.Ai;
using System;
using System.Collections.Generic;
using System.IO;

namespace Pine.Core.Configuration;

/// <summary>
/// Every value Pine reads from configuration rather than from a literal (TD-27), as one object.
/// </summary>
/// <remarks>
/// <para>
/// WHY THE WHOLE TREE LIVES IN <c>Pine.Core</c>. These are plain BCL classes with no attributes and
/// no package reference, so they do not breach the BCL-only rule this project keeps. They are here
/// rather than beside each subsystem because a configuration file is ONE DOCUMENT: a reader opening
/// <c>appsettings.json</c> should be able to find the schema of every key it may contain in one
/// place, and `Pine.Core` is the only assembly that `Pine.Application`, `Pine.Infrastructure` and
/// the UI can all see. Scattering the sections across three assemblies would make the sections
/// discoverable only by whoever already knew where to look.
/// </para>
/// <para>
/// VALIDATION IS HAND-WRITTEN, NOT <c>DataAnnotations</c>. `ValidateDataAnnotations()` would pull
/// `Microsoft.Extensions.Options.DataAnnotations` into the graph to express bounds that
/// <see cref="Pine.Core.Security.KdfParameters"/> already expresses with explicit throws, and it
/// reports a violated `[Range]` without saying what the value is for. <see cref="Validate"/>
/// returns sentences instead, and they are the sentences a person editing a config file needs.
/// </para>
/// <para>
/// WHAT IS DELIBERATELY NOT HERE. Anything a user changes from inside the app. Theme, accent and
/// language are already user settings, written through <c>SaveSettingCommand</c> into the encrypted
/// vault, and that is the right home for a per-user preference: it is private, it travels with the
/// vault, and it does not require the user to find a JSON file. Configuration is for what an
/// OPERATOR or a DEVELOPER sets for an installation. The boundary is recorded as TD-119, because
/// several values below sit close to it.
/// </para>
/// </remarks>
public sealed class PineOptions
{
    /// <summary>The configuration section this tree binds from.</summary>
    public const string SectionName = "Pine";

    /// <summary>Where Pine keeps its data on this machine.</summary>
    public PineStorageOptions Storage { get; set; } = new();

    /// <summary>Vault creation parameters and backup retention.</summary>
    public PineSecurityOptions Security { get; set; } = new();

    /// <summary>Thresholds for the on-device AI subsystem.</summary>
    public PineAiOptions Ai { get; set; } = new();

    /// <summary>Values the shell reads to render lists.</summary>
    public PineUiOptions Ui { get; set; } = new();

    /// <summary>
    /// Checks every section and returns one sentence per problem.
    /// </summary>
    /// <returns>An empty list when the configuration is usable.</returns>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        problems.AddRange(Storage.Validate());
        problems.AddRange(Security.Validate());
        problems.AddRange(Ai.Validate());
        problems.AddRange(Ui.Validate());

        return problems;
    }
}

/// <summary>
/// Where Pine keeps its data on this machine.
/// </summary>
/// <remarks>
/// <para>
/// THIS SECTION EXISTS BECAUSE THE ANSWER WAS WRITTEN DOWN TWICE AND AGREED BY COINCIDENCE.
/// <c>VaultManager</c> computed <c>%LOCALAPPDATA%/Pine</c> in its constructor and
/// <c>PineLoggingOptions.DefaultDirectory()</c> computed <c>%LOCALAPPDATA%/Pine/logs</c>
/// independently, with no shared constant between them. Two definitions of "the Pine data
/// directory" agree until one of them is edited.
/// </para>
/// <para>
/// IT ALSO CLOSES HALF OF TD-81. That row's fix reads "extract `IVaultManager`, **or inject the
/// data directory**"; this is the second half, and it is what lets a test point `VaultManager` at a
/// temporary directory instead of mutating the developer's real one.
/// </para>
/// </remarks>
public sealed class PineStorageOptions
{
    /// <summary>The root directory for vaults, logs and the vault registry.</summary>
    /// <remarks>
    /// Defaults to <c>%LOCALAPPDATA%/Pine</c>, falling back to a <c>data</c> directory beside the
    /// executable where <c>Environment.GetFolderPath</c> returns an empty string. The Browser head
    /// was the known instance of that and was deleted in the Phase 3 platform review; the fallback
    /// stays because it is a statement about the API's contract, not about one head — the same
    /// call returns empty on a stripped container image and on some Linux configurations.
    /// </remarks>
    public string DataDirectory { get; set; } = DefaultDataDirectory();

    /// <summary>The sub-directory of <see cref="DataDirectory"/> that holds vault directories.</summary>
    public string VaultsDirectoryName { get; set; } = "Vaults";

    /// <summary>The sub-directory of <see cref="DataDirectory"/> that holds log files.</summary>
    public string LogsDirectoryName { get; set; } = "logs";

    /// <summary>The resolved vaults directory.</summary>
    public string VaultsDirectory => Path.Combine(DataDirectory, VaultsDirectoryName);

    /// <summary>The resolved logs directory.</summary>
    public string LogsDirectory => Path.Combine(DataDirectory, LogsDirectoryName);

    /// <summary>The one definition of Pine's per-user data directory.</summary>
    public static string DefaultDataDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return string.IsNullOrEmpty(localAppData)
            ? Path.Combine(AppContext.BaseDirectory, "data")
            : Path.Combine(localAppData, "Pine");
    }

    /// <summary>Checks this section.</summary>
    /// <returns>One sentence per problem; empty when usable.</returns>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(DataDirectory))
        {
            problems.Add("Pine:Storage:DataDirectory must not be empty. Remove the key to use the default.");
        }

        // A rooted path is required, not merely preferred: a relative one resolves against the
        // process working directory, which differs between a shortcut, a debugger and a shell, and
        // the symptom is a second empty vault list rather than an error.
        else if (!Path.IsPathRooted(DataDirectory))
        {
            problems.Add($"Pine:Storage:DataDirectory must be an absolute path. '{DataDirectory}' is relative, so it would resolve differently depending on how Pine was launched.");
        }

        if (string.IsNullOrWhiteSpace(VaultsDirectoryName))
        {
            problems.Add("Pine:Storage:VaultsDirectoryName must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(LogsDirectoryName))
        {
            problems.Add("Pine:Storage:LogsDirectoryName must not be empty.");
        }

        return problems;
    }
}

/// <summary>
/// Vault creation parameters and backup retention.
/// </summary>
/// <remarks>
/// <para>
/// READ <see cref="NewVaultKdfMemoryKib"/>'s REMARKS BEFORE CHANGING ANYTHING HERE. The KDF section
/// is the one place in this file where a configuration value touches cryptography, and it is far
/// narrower than it looks.
/// </para>
/// </remarks>
public sealed class PineSecurityOptions
{
    /// <summary>Argon2id memory cost, in KiB, for vaults created from now on.</summary>
    /// <remarks>
    /// <para>
    /// THIS AFFECTS NEW VAULTS ONLY, AND THAT IS NOT A LIMITATION -- IT IS THE WHOLE CONTRACT. A
    /// vault's KDF parameters are written into its own <c>pine.vault.json</c> header when it is
    /// created and are read back from that header on every unlock
    /// (<c>FileVaultHeaderStore.Read</c>, <c>Argon2idKeyDerivationService.DeriveKey(password,
    /// header)</c>). Deriving a key with any other cost produces a different key, so if this value
    /// were consulted at unlock, editing a config file would lock every existing vault out
    /// permanently and the symptom would be indistinguishable from a wrong password.
    /// <c>PineKdfConfigurationTests</c> exists to make that impossible to introduce by accident.
    /// </para>
    /// <para>
    /// THE DEFAULT IS <c>KdfParameters.Argon2idV1</c>'s 65 536 KiB (64 MiB), unchanged. Lowering it
    /// weakens every vault created afterwards; the floor below is
    /// <c>KdfParameters.MinMemoryKib</c>, which is a structural bound rather than a safe one, so a
    /// value that passes validation is not thereby a value worth using.
    /// </para>
    /// </remarks>
    public int NewVaultKdfMemoryKib { get; set; } = 65_536;

    /// <summary>Argon2id iteration count for vaults created from now on.</summary>
    public int NewVaultKdfIterations { get; set; } = 3;

    /// <summary>Argon2id parallelism (lanes) for vaults created from now on.</summary>
    public int NewVaultKdfParallelism { get; set; } = 4;

    /// <summary>How many automatic backups of a vault are retained.</summary>
    /// <remarks>
    /// Was <c>VaultBackupService.DefaultBackupsToKeep</c>, whose doc comment named TD-27 as the
    /// reason it was a constant.
    /// </remarks>
    public int BackupsToKeep { get; set; } = 5;

    /// <summary>How long a vault may sit untouched before it locks itself.</summary>
    /// <remarks>
    /// Seeds <c>VaultAutoLockOptions.IdleTimeout</c>. It seeds rather than replaces because that
    /// type stays a plain mutable singleton on purpose -- a settings screen changes the timeout at
    /// runtime, and an <c>IOptions</c> snapshot would not reach the already-running timer.
    /// <para>
    /// TD-119: THIS IS A READ-FALLBACK, NOT THE HOME OF THE VALUE. Configuration is plaintext and outside
    /// the vault, so it holds only what the pre-unlock screen needs (theme, language). This is an
    /// operative preference: the encrypted in-vault Setting <c>Security.AutoLockIdleMinutes</c> wins when present and valid,
    /// and this value applies only when no Setting exists (so a pre-TD-119 user keeps their value).
    /// </para>
    /// </remarks>
    public TimeSpan AutoLockIdleTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The Argon2id profile a vault created right now is minted with.
    /// </summary>
    /// <returns>The parameters to hand to <c>IVaultKeyService.CreateVault</c>.</returns>
    /// <remarks>
    /// <para>
    /// THIS METHOD IS THE ONLY BRIDGE FROM CONFIGURATION TO CRYPTOGRAPHY, and it deliberately has
    /// exactly one caller: <c>SetupViewModel</c>, once per vault, ever. It lives here rather than in
    /// that view model so that a test can exercise the REAL conversion instead of restating it —
    /// a test that reimplements the mapping it is checking proves only that the author can write
    /// the same three lines twice.
    /// </para>
    /// <para>
    /// SALT AND KEY LENGTHS COME FROM <c>Argon2idV1</c> AND ARE NOT CONFIGURABLE. The key length is
    /// fixed at 32 bytes by <c>AesGcmVaultKeyService</c>, which refuses anything else because the
    /// data key it wraps is AES-256; the salt length has no reason to vary and every reason not to.
    /// </para>
    /// <para>
    /// NOTHING ON THE UNLOCK PATH MAY CALL THIS. A vault's parameters are read from its own header;
    /// deriving with a configured cost instead would lock every existing vault out, indistinguishably
    /// from a wrong password. <c>PineOptionsCompositionTests</c> pins that separation.
    /// </para>
    /// </remarks>
    public Security.KdfParameters ToKdfParameters() => new(
        kdfId: Security.KdfParameters.Argon2id,
        memoryKib: NewVaultKdfMemoryKib,
        iterations: NewVaultKdfIterations,
        parallelism: NewVaultKdfParallelism,
        keyLengthBytes: Security.KdfParameters.Argon2idV1.KeyLengthBytes,
        saltLengthBytes: Security.KdfParameters.Argon2idV1.SaltLengthBytes);

    /// <summary>Checks this section.</summary>
    /// <returns>One sentence per problem; empty when usable.</returns>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        // The bounds are Pine.Core.Security.KdfParameters' own, quoted here rather than referenced
        // so that a bad config file fails with a sentence about a config key instead of an
        // ArgumentOutOfRangeException from a constructor the reader has never heard of.
        if (NewVaultKdfMemoryKib is < 8 or > 16 * 1024 * 1024)
        {
            problems.Add($"Pine:Security:NewVaultKdfMemoryKib must be between 8 and {16 * 1024 * 1024} KiB. It was {NewVaultKdfMemoryKib}.");
        }

        if (NewVaultKdfIterations is < 1 or > 1_000)
        {
            problems.Add($"Pine:Security:NewVaultKdfIterations must be between 1 and 1000. It was {NewVaultKdfIterations}.");
        }

        if (NewVaultKdfParallelism is < 1 or > 256)
        {
            problems.Add($"Pine:Security:NewVaultKdfParallelism must be between 1 and 256. It was {NewVaultKdfParallelism}.");
        }

        if (BackupsToKeep < 1)
        {
            problems.Add($"Pine:Security:BackupsToKeep must be at least 1; a vault with no retained backup cannot be recovered. It was {BackupsToKeep}.");
        }

        if (AutoLockIdleTimeout < TimeSpan.FromMinutes(1) || AutoLockIdleTimeout > TimeSpan.FromHours(8))
        {
            problems.Add($"Pine:Security:AutoLockIdleTimeout must be between 1 minute and 8 hours. It was {AutoLockIdleTimeout}.");
        }

        return problems;
    }
}

/// <summary>
/// Thresholds for the on-device AI subsystem.
/// </summary>
public sealed class PineAiOptions
{
    /// <summary>
    /// The confidence below which a predicted category is discarded rather than offered.
    /// </summary>
    /// <remarks>
    /// WAS THE LITERAL <c>0.3</c>, WRITTEN TWICE. `CategorizeTransactionQueryHandler` refused a
    /// prediction below it and `ImportTransactionsCommandHandler` auto-applied one at or above it,
    /// in two files, with nothing tying the two numbers together. They could drift, and if they
    /// did, the import path would silently apply categories the suggestion path considered too
    /// weak to show.
    /// <para>
    /// TD-119: THIS IS A READ-FALLBACK, NOT THE HOME OF THE VALUE. Configuration is plaintext and outside
    /// the vault, so it holds only what the pre-unlock screen needs (theme, language). This is an
    /// operative preference: the encrypted in-vault Setting <c>Ai.CategorisationConfidenceThreshold</c> wins when present and valid,
    /// and this value applies only when no Setting exists (so a pre-TD-119 user keeps their value).
    /// </para>
    /// </remarks>
    public double CategorisationConfidenceThreshold { get; set; } = 0.3;

    /// <summary>Rows below which training is refused.</summary>
    /// <remarks>
    /// Fewer than this and the model memorises rather than generalises, and a confidently wrong
    /// category is worse than no category. Was <c>MlNetCategorizer.MinimumTrainingRows</c>.
    /// </remarks>
    public int MinimumTrainingRows { get; set; } = 10;

    /// <summary>Newly categorised transactions required before a retraining pass is run.</summary>
    public int TrainingBatchThreshold { get; set; } = 10;

    /// <summary>How long the trainer waits after startup before its first pass.</summary>
    /// <remarks>The vault is locked when the process starts, so an immediate pass would find nothing.</remarks>
    public TimeSpan TrainingStartupDelay { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>How often the trainer checks whether a retraining pass is due.</summary>
    public TimeSpan TrainingInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Absolute path to an optional LayoutLMv3 ONNX model that reads Merchant, Date, Total and Tax from a receipt.
    /// <see langword="null"/> (the default) leaves the model off and receipts use the heuristic extraction.
    /// </summary>
    /// <remarks>
    /// The model is not shipped with Pine (its weights are CC BY-NC-SA 4.0, research use) and is never bundled;
    /// this is the only way to turn it on. A path to a file that does not exist is not an error: scanning logs
    /// a warning and falls back to the heuristics. A path only, no tuned constants (docs/13 section 7.1).
    /// </remarks>
    public string? LayoutLmModelPath { get; set; }

    /// <summary>
    /// Absolute path to the model's <c>tokenizer.json</c>. <see langword="null"/> means <c>tokenizer.json</c>
    /// in the same directory as <see cref="LayoutLmModelPath"/>.
    /// </summary>
    public string? LayoutLmTokenizerPath { get; set; }

    /// <summary>
    /// Which engine reads receipt text: <see cref="OcrBackend.Paddle"/> (default, Windows) or
    /// <see cref="OcrBackend.Onnx"/> (managed, portable). Bound from <c>Pine:Ai:OcrBackend</c>.
    /// </summary>
    /// <remarks>
    /// Only consulted by a head that has receipt vision. The default stays Paddle until the ONNX engine's
    /// parity run has been accepted (docs/65 section 9, item B3). A path or a mode, no tuned constants
    /// (docs/13 section 7.1).
    /// </remarks>
    public OcrBackend OcrBackend { get; set; } = OcrBackend.Paddle;

    /// <summary>
    /// Directory holding <c>det.onnx</c>, <c>rec.onnx</c> and <c>dict_latin_v5.txt</c> for the ONNX backend.
    /// <see langword="null"/> means <c>Assets/Ai/ocr</c> beside the application.
    /// </summary>
    public string? OcrModelDirectory { get; set; }

    /// <summary>Checks this section.</summary>
    /// <returns>One sentence per problem; empty when usable.</returns>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        if (CategorisationConfidenceThreshold is < 0 or > 1)
        {
            problems.Add($"Pine:Ai:CategorisationConfidenceThreshold is a probability and must be between 0 and 1. It was {CategorisationConfidenceThreshold}.");
        }

        if (MinimumTrainingRows < 1)
        {
            problems.Add($"Pine:Ai:MinimumTrainingRows must be at least 1. It was {MinimumTrainingRows}.");
        }

        if (TrainingBatchThreshold < 1)
        {
            problems.Add($"Pine:Ai:TrainingBatchThreshold must be at least 1. It was {TrainingBatchThreshold}.");
        }

        if (TrainingStartupDelay < TimeSpan.Zero)
        {
            problems.Add($"Pine:Ai:TrainingStartupDelay must not be negative. It was {TrainingStartupDelay}.");
        }

        // PeriodicTimer throws on a non-positive period, so this refusal replaces one that would
        // otherwise happen inside a background service where nobody would see it.
        if (TrainingInterval <= TimeSpan.Zero)
        {
            problems.Add($"Pine:Ai:TrainingInterval must be greater than zero. It was {TrainingInterval}.");
        }

        return problems;
    }
}

/// <summary>
/// Values the shell reads to render lists.
/// </summary>
public sealed class PineUiOptions
{
    /// <summary>How many transactions one page of the transaction list holds.</summary>
    /// <remarks>
    /// THERE WERE TWO OF THESE AND THEY DISAGREED. <c>GetTransactionsQuery</c> declared
    /// <c>PageSize = 50</c> as a default parameter and <c>TransactionsViewModel</c> declared
    /// <c>const int PageSize = 30</c> and passed it explicitly, so the 50 was never reached by the
    /// shipped UI. The audit's §6.2 cites "page size 50" as the hard-coded value; the value the
    /// user actually experienced was 30. The default parameter is gone and this is the only page
    /// size that exists.
    /// <para>
    /// TD-119: THIS IS A READ-FALLBACK, NOT THE HOME OF THE VALUE. Configuration is plaintext and outside
    /// the vault, so it holds only what the pre-unlock screen needs (theme, language). This is an
    /// operative preference: the encrypted in-vault Setting <c>Ui.TransactionsPageSize</c> wins when present and valid,
    /// and this value applies only when no Setting exists (so a pre-TD-119 user keeps their value).
    /// </para>
    /// </remarks>
    public int TransactionsPageSize { get; set; } = 30;

    /// <summary>How many log records the diagnostics panel keeps in memory.</summary>
    public int DiagnosticsLogEntryLimit { get; set; } = 200;

    /// <summary>Checks this section.</summary>
    /// <returns>One sentence per problem; empty when usable.</returns>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        if (TransactionsPageSize < 1)
        {
            problems.Add($"Pine:Ui:TransactionsPageSize must be at least 1. It was {TransactionsPageSize}.");
        }

        if (DiagnosticsLogEntryLimit < 1)
        {
            problems.Add($"Pine:Ui:DiagnosticsLogEntryLimit must be at least 1. It was {DiagnosticsLogEntryLimit}.");
        }

        return problems;
    }
}
