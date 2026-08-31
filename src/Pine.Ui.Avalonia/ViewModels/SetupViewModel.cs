using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Pine.Application.Security;
using Pine.Core.Security;
using Pine.Ui.Avalonia.Services;
using Microsoft.Extensions.Logging;
using Pine.Ui.Avalonia.Diagnostics;

namespace Pine.Ui.Avalonia.ViewModels;

public partial class SetupViewModel : ObservableObject
{
    /// <summary>
    /// Pre-TD-05 vaults recorded only a raw salt file with no KDF metadata. Its presence still
    /// means "a vault lives here", so setup must refuse rather than overwrite it.
    /// </summary>
    private const string LegacySaltFileName = "pine.salt";

    private readonly IServiceProvider _serviceProvider;
    private readonly IVaultSession _vaultSession;
    private readonly INavigationService _navigationService;
    private readonly IVaultHeaderStore _vaultHeaderStore;

    /// <summary>
    /// The only thing in this process allowed to turn a master password into a data key (TD-06).
    /// The view model never sees the KDF, the salt or the wrap; it sees a
    /// <see cref="VaultKeyMaterial"/> and hands both halves straight on.
    /// </summary>
    private readonly IVaultKeyService _vaultKeyService;
    private readonly ILogger<SetupViewModel>? _logger;

    [ObservableProperty]
    public partial int CurrentStep { get; set; } = 1;
    
    public bool IsStep1 => CurrentStep == 1;
    public bool IsStep2 => CurrentStep == 2;

    partial void OnCurrentStepChanged(int value)
    {
        OnPropertyChanged(nameof(IsStep1));
        OnPropertyChanged(nameof(IsStep2));
    }

    [ObservableProperty]
    public partial string VaultName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MasterPassword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ConfirmPassword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "Enter a strong master password to encrypt your vault.";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    // TD-11 live strength feedback. PasswordStrengthPolicy.Evaluate returns only enums and numbers
    // -- see PasswordWeakness's remarks on why -- so every string below is written here, in the UI,
    // and nowhere in Pine.Core or Pine.Application.

    /// <summary>Estimated entropy of <see cref="MasterPassword"/>, in bits, for the "X of Y bits" caption.</summary>
    [ObservableProperty]
    public partial double PasswordStrengthBits { get; set; }

    /// <summary>The floor <see cref="PasswordStrengthBits"/> is judged against; carried for the caption rather than a second reference to the policy constant.</summary>
    [ObservableProperty]
    public partial double PasswordStrengthRequiredBits { get; set; } = PasswordStrengthPolicy.MinimumEntropyBits;

    /// <summary>0..1 fill for the strength meter. Clamped so a very-strong password does not overflow the bar.</summary>
    [ObservableProperty]
    public partial double PasswordStrengthFraction { get; set; }

    /// <summary>The single acceptance test, mirrored from <see cref="PasswordStrengthResult.MeetsFloor"/> for binding.</summary>
    [ObservableProperty]
    public partial bool PasswordMeetsFloor { get; set; }

    /// <summary>
    /// "Strong -- meets Pine's minimum" or similar. The pass/fail state is carried in THIS TEXT, not
    /// only in the meter colour, so the meter still reads correctly for a colour-blind user or under
    /// a theme that changes the palette.
    /// </summary>
    [ObservableProperty]
    public partial string PasswordStrengthStatusText { get; set; } = string.Empty;

    /// <summary>Actionable advice built from <see cref="PasswordStrengthResult.Weaknesses"/>. Empty once the password is strong and unremarkable.</summary>
    [ObservableProperty]
    public partial string PasswordAdviceText { get; set; } = string.Empty;

    /// <summary>Whether <see cref="ConfirmPassword"/> currently agrees with <see cref="MasterPassword"/>. True while <see cref="ConfirmPassword"/> is still empty, so the mismatch warning only appears once the user has actually typed something to compare.</summary>
    [ObservableProperty]
    public partial bool PasswordsMatch { get; set; } = true;

    [ObservableProperty]
    public partial PasswordStrengthLevel PasswordStrengthLevelValue { get; set; } = PasswordStrengthLevel.Unusable;

    // Three coarse bands, not the six raw levels, because the meter has three colours available
    // (danger/warning/success) that already mean something elsewhere in this app (BudgetsView,
    // ScanReceiptView) -- reusing them keeps the vocabulary consistent instead of inventing a
    // fourth palette. MeetsFloor -- not the band -- is still what InitializeVaultAsync gates on.
    public bool IsPasswordStrengthDanger =>
        PasswordStrengthLevelValue is PasswordStrengthLevel.Unusable or PasswordStrengthLevel.VeryWeak or PasswordStrengthLevel.Weak;

    public bool IsPasswordStrengthWarning => PasswordStrengthLevelValue == PasswordStrengthLevel.Moderate;

    public bool IsPasswordStrengthSuccess =>
        PasswordStrengthLevelValue is PasswordStrengthLevel.Strong or PasswordStrengthLevel.VeryStrong;

    partial void OnPasswordStrengthLevelValueChanged(PasswordStrengthLevel value)
    {
        OnPropertyChanged(nameof(IsPasswordStrengthDanger));
        OnPropertyChanged(nameof(IsPasswordStrengthWarning));
        OnPropertyChanged(nameof(IsPasswordStrengthSuccess));
    }

    partial void OnMasterPasswordChanged(string value)
    {
        var result = PasswordStrengthPolicy.Evaluate(value);

        PasswordStrengthBits = result.EstimatedBits;
        PasswordStrengthRequiredBits = result.RequiredBits;
        PasswordStrengthFraction = result.RequiredBits <= 0d
            ? 0d
            : Math.Clamp(result.EstimatedBits / result.RequiredBits, 0d, 1d);
        PasswordMeetsFloor = result.MeetsFloor;
        PasswordStrengthLevelValue = result.Level;
        PasswordStrengthStatusText = BuildStatusText(result);
        PasswordAdviceText = BuildAdviceText(result);

        UpdatePasswordsMatch();
    }

    partial void OnConfirmPasswordChanged(string value) => UpdatePasswordsMatch();

    private void UpdatePasswordsMatch()
    {
        PasswordsMatch = string.IsNullOrEmpty(ConfirmPassword) || ConfirmPassword == MasterPassword;
    }

    private static string BuildStatusText(PasswordStrengthResult result)
    {
        if (result.Weaknesses.Contains(PasswordWeakness.Empty))
        {
            return string.Empty;
        }

        var band = result.Level switch
        {
            PasswordStrengthLevel.VeryWeak => "Very weak",
            PasswordStrengthLevel.Weak => "Weak",
            PasswordStrengthLevel.Moderate => "Moderate",
            PasswordStrengthLevel.Strong => "Strong",
            PasswordStrengthLevel.VeryStrong => "Very strong",
            _ => "Unusable",
        };

        return result.MeetsFloor
            ? $"{band} -- meets Pine's minimum for a vault password."
            : $"{band} -- does not yet meet Pine's minimum for a vault password.";
    }

    /// <summary>
    /// One actionable sentence per reason the estimator gave, skipping the two reasons that are
    /// already said by <see cref="BuildStatusText"/> (<see cref="PasswordWeakness.Empty"/> and
    /// <see cref="PasswordWeakness.BelowEntropyFloor"/>, which is only ever a restatement of "does
    /// not meet the minimum").
    /// </summary>
    private static string BuildAdviceText(PasswordStrengthResult result)
    {
        var sentences = result.Weaknesses
            .Select(AdviceFor)
            .Where(static s => s is not null);

        return string.Join(" ", sentences);
    }

    private static string? AdviceFor(PasswordWeakness weakness) => weakness switch
    {
        PasswordWeakness.Empty => null,
        PasswordWeakness.BelowEntropyFloor => null,
        PasswordWeakness.TooShort =>
            $"Use at least {PasswordStrengthPolicy.MinimumLength} characters.",
        PasswordWeakness.CommonPassword =>
            "Part of this password is a commonly used one (even disguised with capitals or numbers) -- pick something less guessable.",
        PasswordWeakness.RepeatedPattern =>
            "Avoid repeating characters or blocks, like \"aaaa\" or \"abcabcabc\".",
        PasswordWeakness.SequentialRun =>
            "Avoid sequential runs, like \"abcdef\" or \"123456\".",
        PasswordWeakness.KeyboardPattern =>
            "Avoid keyboard walks, like \"qwerty\" or \"asdfgh\".",
        PasswordWeakness.LimitedCharacterVariety =>
            "Add more length or mix in more variety -- length is the cheapest way to gain strength.",
        _ => null,
    };

    public SetupViewModel(
        IServiceProvider serviceProvider,
        IVaultSession vaultSession,
        INavigationService navigationService,
        IVaultHeaderStore vaultHeaderStore,
        IVaultKeyService vaultKeyService,
        ILogger<SetupViewModel>? logger = null)
    {
        _serviceProvider = serviceProvider;
        _vaultSession = vaultSession;
        _navigationService = navigationService;
        _vaultHeaderStore = vaultHeaderStore;
        _vaultKeyService = vaultKeyService;
        _logger = logger;
    }

    [RelayCommand]
    private void NextStep()
    {
        CurrentStep = 2;
    }

    [RelayCommand]
    private void BackStep()
    {
        CurrentStep = 1;
    }

    [RelayCommand]
    private async Task InitializeVaultAsync()
    {
        if (string.IsNullOrWhiteSpace(VaultName))
        {
            StatusMessage = "Vault name cannot be empty.";
            return;
        }

        // THE GATE (TD-11). A vault must never be created below the entropy floor -- see the
        // arithmetic on PasswordStrengthPolicy.MinimumEntropyBits. Re-evaluated here rather than
        // trusted from the live-feedback properties above: those exist for the meter, and a command
        // is not allowed to trust view state it did not itself just computed.
        var strength = PasswordStrengthPolicy.Evaluate(MasterPassword);
        if (!strength.MeetsFloor)
        {
            StatusMessage = string.IsNullOrEmpty(PasswordAdviceText)
                ? "This password does not meet Pine's minimum strength for a vault password."
                : $"This password does not meet Pine's minimum strength for a vault password. {PasswordAdviceText}";
            return;
        }

        // There is no recovery phrase (15-Security) -- a password mistyped here is permanent data
        // loss, not an inconvenience, so a mismatch must refuse rather than warn.
        if (MasterPassword != ConfirmPassword)
        {
            StatusMessage = "Passwords do not match. Re-enter both so a typo cannot lock you out permanently.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Encrypting vault... (this takes a moment)";

        try
        {
            var vaultManager = _serviceProvider.GetRequiredService<VaultManager>();
            var profile = await vaultManager.CreateVaultAsync(VaultName);
            
            await Task.Run(async () => 
            {
                var vaultDir = profile.Path;
                if (!Directory.Exists(vaultDir)) Directory.CreateDirectory(vaultDir);

                if (_vaultHeaderStore.Exists(vaultDir) ||
                    File.Exists(Path.Combine(vaultDir, LegacySaltFileName)))
                {
                    throw new InvalidOperationException("A vault already exists at this path.");
                }

                // Mints the envelope in one step (TD-06): a random data key, a random salt, and
                // that data key wrapped under an auth key derived from the master password with
                // the Argon2idV1 profile. The password is stamped nowhere -- what lands on disk is
                // the wrap, and only this password's auth key opens it.
                var material = _vaultKeyService.CreateVault(MasterPassword, KdfParameters.Argon2idV1);

                // ORDERING IS A CORRECTNESS REQUIREMENT, NOT A STYLE CHOICE. The header is written
                // BEFORE anything creates the database file, because the two failure modes are not
                // symmetric: a database whose header was never written is encrypted under a data
                // key that now exists nowhere and can NEVER be opened again -- silent, total data
                // loss. A header with no database is just an unused vault: harmless, and setup's
                // Exists() check above will refuse to overwrite it. If the process dies between
                // these two statements, the user loses nothing.
                _vaultHeaderStore.Write(vaultDir, material.Header);

                // Unlock() copies the key into the session's own buffer and Lock() zeroes THAT
                // copy, never this one. Clearing here keeps a second copy of the vault key from
                // living in the managed heap for the rest of the process (TD-11); see the same
                // comment in UnlockViewModel.
                _vaultSession.Unlock(vaultDir, material.DataKey);
                Array.Clear(material.DataKey, 0, material.DataKey.Length);

                // Resolved only after Unlock: the migrator depends on PineDbContext, which refuses
                // to be constructed while the vault session is locked.
                //
                // IVaultMigrator and PineDbContext are Scoped (TD-01), so they are resolved from an
                // EXPLICIT scope rather than from the root provider. Resolving a Scoped service
                // from the root creates a root-scoped instance that lives for the whole process --
                // here that would be a DbContext pinned to this vault's path and key, still alive
                // after the user locks it and opens a different vault. The scope is disposed only
                // after MigrateAsync has completed.
                using var migrationScope = _serviceProvider.CreateScope();
                var migrator = migrationScope.ServiceProvider.GetRequiredService<IVaultMigrator>();

                // Migrations (never EnsureCreated) are the single source of truth for the schema:
                // this creates the full schema AND records __EFMigrationsHistory. On a brand-new
                // vault there is no data yet, so no pre-migration backup is taken.
                await migrator.MigrateAsync(CancellationToken.None).ConfigureAwait(false);
            });

            StatusMessage = "Vault initialized!";

            // No settings are written here (TD-47/TD-48/TD-80): the storage-mode and telemetry
            // choices this step used to record described capabilities Pine does not have --
            // cloud sync was disabled in the UI and never wired to a reader, and telemetry has no
            // code path anywhere in the repository. Recording a setting nobody reads, or asking
            // consent for a capability that does not exist, is worse than the disabled affordance
            // it replaced. A fresh vault also seeds no account: see DashboardView's always-visible
            // "+ New Account" affordance for the empty-vault path.

            _navigationService.NavigateTo<ShellViewModel>();
        }
        catch (Exception ex)
        {
            // WAS: $"Error: {ex.Message}" (TD-24). Vault creation writes a directory, a header file
            // and a database, so its failures are filesystem and SQLCipher exceptions naming the
            // absolute path the user is about to store their finances in -- shown on a screen that
            // is, by definition, the first one a new user ever sees.
            StatusMessage = UserFacingError.Report(
                _logger,
                ex,
                "vault-setup",
                "Your vault could not be created. Choose a different folder or free up disk space, then try again.");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
