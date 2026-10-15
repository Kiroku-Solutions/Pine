using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pine.Application.Security;
using Pine.Core.Common;
using Pine.Core.Security;
using Pine.Ui.Avalonia.Services;
using Microsoft.Extensions.Logging;
using Pine.Ui.Avalonia.Diagnostics;
using Pine.Ui.Avalonia.Localization;
using Microsoft.Extensions.Options;
using Pine.Core.Configuration;
using System.Collections.Generic;

namespace Pine.Ui.Avalonia.ViewModels;

public partial class SetupViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// Pre-TD-05 vaults recorded only a raw salt file with no KDF metadata. Its presence still
    /// means "a vault lives here", so setup must refuse rather than overwrite it.
    /// </summary>
    private const string LegacySaltFileName = "pine.salt";

    /// <summary>
    /// The vault registry this screen creates the new vault profile in.
    /// </summary>
    /// <remarks>
    /// INJECTED, NOT RESOLVED (TD-28). <c>InitializeVaultAsync</c> used to pull this out of the
    /// container mid-command, which is the last service-locator call left in this type once
    /// navigation stopped needing one. It is a singleton on every head, so this is the same object
    /// -- only the reach has gone.
    /// </remarks>
    private readonly IVaultManager _vaultManager;
    private readonly LocalizedMessageSlot _message;
    private readonly LocalizedMessageSlot _strengthStatus;
    private readonly LocalizedMessageSlot _advice;

    private static void SetOrClear(LocalizedMessageSlot slot, UserFacingMessage? message)
    {
        if (message is null) slot.Clear(); else slot.Set(message);
    }

    private static void SetOrClear(LocalizedMessageSlot slot, IReadOnlyList<UserFacingMessage> messages)
    {
        if (messages.Count == 0) slot.Clear(); else slot.Set(messages);
    }

    private readonly IVaultSession _vaultSession;
    private readonly INavigationService _navigationService;

    /// <summary>
    /// Builds the shell this screen hands over to once the vault exists (TD-28).
    /// </summary>
    private readonly IViewModelFactory _viewModels;
    private readonly IVaultHeaderStore _vaultHeaderStore;

    /// <summary>
    /// The only thing in this process allowed to turn a master password into a data key (TD-06).
    /// The view model never sees the KDF, the salt or the wrap; it sees a
    /// <see cref="VaultKeyMaterial"/> and hands both halves straight on.
    /// </summary>
    private readonly IVaultKeyService _vaultKeyService;

    /// <summary>
    /// Creates the new vault's database schema (TD-28). This screen no longer opens a DI scope or
    /// resolves the migrator itself: the scope, the DbContext and the migration all belong to the
    /// persistence adapter behind this port. The setup path is deliberately a different method from
    /// the unlock path -- a brand-new vault has nothing to probe and no data to back up.
    /// </summary>
    private readonly IVaultDatabaseOpener _vaultDatabaseOpener;

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
    public partial string StatusMessage { get; set; } = string.Empty;

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
        SetOrClear(_strengthStatus, PasswordFeedbackText.Status(result));
        SetOrClear(_advice, PasswordFeedbackText.Advice(result));

        UpdatePasswordsMatch();
    }

    partial void OnConfirmPasswordChanged(string value) => UpdatePasswordsMatch();

    private void UpdatePasswordsMatch()
    {
        PasswordsMatch = string.IsNullOrEmpty(ConfirmPassword) || ConfirmPassword == MasterPassword;
    }

    private readonly PineSecurityOptions _securityOptions;

    /// <param name="securityOptions">
    /// Carries the Argon2id profile NEW vaults are minted with (TD-27). Optional, defaulting to
    /// <see cref="KdfParameters.Argon2idV1"/> — see <see cref="NewVaultKdf"/> for why this can only
    /// ever affect new vaults.
    /// </param>
    public SetupViewModel(
        IVaultManager vaultManager,
        IVaultSession vaultSession,
        INavigationService navigationService,
        IViewModelFactory viewModels,
        IVaultHeaderStore vaultHeaderStore,
        IVaultKeyService vaultKeyService,
        IVaultDatabaseOpener vaultDatabaseOpener,
        ILocalizer localizer,
        IOptions<PineSecurityOptions>? securityOptions = null,
        ILogger<SetupViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        _message = new LocalizedMessageSlot(localizer, text => StatusMessage = text ?? string.Empty);
        _strengthStatus = new LocalizedMessageSlot(localizer, text => PasswordStrengthStatusText = text ?? string.Empty);
        _advice = new LocalizedMessageSlot(localizer, text => PasswordAdviceText = text ?? string.Empty);
        _message.Set("Setup.Status.Prompt");
        _securityOptions = securityOptions?.Value ?? new PineSecurityOptions();
        _vaultManager = vaultManager;
        _vaultSession = vaultSession;
        _navigationService = navigationService;
        _viewModels = viewModels;
        _vaultHeaderStore = vaultHeaderStore;
        _vaultKeyService = vaultKeyService;
        _vaultDatabaseOpener = vaultDatabaseOpener;
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
            _message.Set("Setup.Status.VaultNameEmpty");
            return;
        }

        // THE GATE (TD-11). A vault must never be created below the entropy floor -- see the
        // arithmetic on PasswordStrengthPolicy.MinimumEntropyBits. Re-evaluated here rather than
        // trusted from the live-feedback properties above: those exist for the meter, and a command
        // is not allowed to trust view state it did not itself just computed.
        var strength = PasswordStrengthPolicy.Evaluate(MasterPassword);
        if (!strength.MeetsFloor)
        {
            _message.Set([new UserFacingMessage("Password.BelowMinimum"), .. PasswordFeedbackText.Advice(strength)]);
            return;
        }

        // There is no recovery phrase (15-Security) -- a password mistyped here is permanent data
        // loss, not an inconvenience, so a mismatch must refuse rather than warn.
        if (MasterPassword != ConfirmPassword)
        {
            _message.Set("Setup.Status.PasswordMismatch");
            return;
        }

        IsBusy = true;
        _message.Set("Setup.Status.Encrypting");

        try
        {
            var profile = await _vaultManager.CreateVaultAsync(VaultName);
            
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
                var material = _vaultKeyService.CreateVault(MasterPassword, _securityOptions.ToKdfParameters());

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

                // The schema phase (TD-28). Creating it needs an explicit DI scope, a DbContext
                // and the migrator, and all three now live behind IVaultDatabaseOpener rather than
                // in this view model. Called only after Unlock: the migrator depends on
                // PineDbContext, which refuses to be constructed while the vault session is locked.
                // Migrations (never EnsureCreated) remain the single source of truth for the schema.
                await _vaultDatabaseOpener.CreateSchemaAsync(CancellationToken.None).ConfigureAwait(false);
            });

            _message.Set("Setup.Status.Initialized");

            // No settings are written here (TD-47/TD-48/TD-80): the storage-mode and telemetry
            // choices this step used to record described capabilities Pine does not have --
            // cloud sync was disabled in the UI and never wired to a reader, and telemetry has no
            // code path anywhere in the repository. Recording a setting nobody reads, or asking
            // consent for a capability that does not exist, is worse than the disabled affordance
            // it replaced. A fresh vault also seeds no account: see DashboardView's always-visible
            // "+ New Account" affordance for the empty-vault path.

            _navigationService.NavigateTo(_viewModels.CreateShell());
        }
        catch (Exception ex)
        {
            // WAS: $"Error: {ex.Message}" (TD-24). Vault creation writes a directory, a header file
            // and a database, so its failures are filesystem and SQLCipher exceptions naming the
            // absolute path the user is about to store their finances in -- shown on a screen that
            // is, by definition, the first one a new user ever sees.
            _message.Set(UserFacingError.Report(
                _logger,
                ex,
                "vault-setup",
                new UserFacingMessage("Setup.Failure.Create")));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Releases the localized message slot.
    /// </summary>
    /// <remarks>
    /// THE ANALYZER ASKED FOR THIS AND IT WAS RIGHT (CA1001). A
    /// <see cref="LocalizedMessageSlot"/> subscribes to <c>ILocalizer.LanguageChanged</c>, and the
    /// localizer is a singleton for the life of the process -- so a slot that is never released
    /// keeps this view model reachable forever. That is precisely the leak shape TD-34 removed
    /// from the messenger, and it would have come back through a different event.
    /// </remarks>
    public void Dispose()
    {
        _message.Dispose();
        _strengthStatus.Dispose();
        _advice.Dispose();
        GC.SuppressFinalize(this);
    }

}
