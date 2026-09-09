using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediatR;
using Pine.Application.Security;
using Pine.Core.Security;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.Localization;

namespace Pine.Ui.Avalonia.ViewModels;

public partial class UnlockViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// Reads this vault's persisted accent theme once the vault is open.
    /// </summary>
    /// <remarks>
    /// INJECTED, NOT RESOLVED (TD-28). It was pulled out of the container at the point of use;
    /// taking it in the constructor is the same singleton, obtained where the type system can see
    /// it. The reason the read goes through the MEDIATOR at all is unchanged, and is written at the
    /// call site.
    /// </remarks>
    private readonly ISender _sender;
    private readonly LocalizedMessageSlot _message;

    private readonly IVaultSession _vaultSession;
    private readonly INavigationService _navigationService;

    /// <summary>
    /// Builds the pages this screen hands over to -- the shell on success, the picker on Back (TD-28).
    /// </summary>
    private readonly IViewModelFactory _viewModels;
    private readonly IVaultHeaderStore _vaultHeaderStore;

    /// <summary>
    /// The envelope (TD-06). This screen no longer derives a database key from the password: it
    /// asks this service to unwrap the vault's stored data key, and a wrong password is a failed
    /// AEAD tag inside that call rather than a SQLCipher error guessed at further down.
    /// </summary>
    private readonly IVaultKeyService _vaultKeyService;

    /// <summary>
    /// The database half of opening a vault (TD-28). This screen no longer creates a DI scope or
    /// resolves a DbContext of its own: it hands the work to this port, which owns the scope, the
    /// probe that proves the unwrapped key opens THIS database, and the schema migration -- in that
    /// order, and as two separate phases, so a failed schema upgrade can still be told apart from a
    /// wrong password by the catch blocks below.
    /// </summary>
    private readonly IVaultDatabaseOpener _vaultDatabaseOpener;

    private VaultProfile? _profile;

    [ObservableProperty]
    public partial string MasterPassword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string VaultName { get; set; } = "Unlock Vault";

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public UnlockViewModel(
        ISender sender,
        IVaultSession vaultSession,
        INavigationService navigationService,
        IViewModelFactory viewModels,
        IVaultHeaderStore vaultHeaderStore,
        IVaultKeyService vaultKeyService,
        IVaultDatabaseOpener vaultDatabaseOpener,
        ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        _message = new LocalizedMessageSlot(localizer, text => StatusMessage = text ?? string.Empty);
        _sender = sender;
        _vaultSession = vaultSession;
        _navigationService = navigationService;
        _viewModels = viewModels;
        _vaultHeaderStore = vaultHeaderStore;
        _vaultKeyService = vaultKeyService;
        _vaultDatabaseOpener = vaultDatabaseOpener;
    }

    public void Initialize(VaultProfile profile)
    {
        _profile = profile;
        VaultName = $"Unlock {profile.Name}";
    }

    [RelayCommand]
    private async Task UnlockVaultAsync()
    {
        if (string.IsNullOrWhiteSpace(MasterPassword))
        {
            _message.Set("Unlock.Status.PasswordEmpty");
            return;
        }

        IsBusy = true;
        _message.Set("Unlock.Status.Decrypting");

        if (_profile == null)
        {
            _message.Set("Unlock.Status.NoProfileSelected");
            IsBusy = false;
            return;
        }

        try
        {
            await Task.Run(async () =>
            {
                var vaultDir = _profile.Path;

                // The header states which KDF and cost parameters produced this vault's AUTH key,
                // and carries the wrapped data key itself. Read() never invents or upgrades
                // anything: a pre-envelope vault throws LegacyVaultFormatException and an
                // unreadable one throws rather than guessing parameters, because a guess would
                // silently derive the wrong key and present as unexplained data loss.
                var header = _vaultHeaderStore.Read(vaultDir);

                // THIS is the password check now (TD-06). Unwrapping runs the AEAD tag over the
                // stored wrap, so a wrong password fails HERE, before any file is opened, and it
                // fails as one specific exception type instead of as whatever error SQLCipher
                // happens to raise when handed a bad key.
                var key = _vaultKeyService.UnwrapDataKey(MasterPassword, header);

                // Unlock session. Unlock() COPIES the key into the session's own buffer, and it is
                // that copy which Lock() zeroes -- so this local is a second, independent copy of
                // the vault key that nothing else will ever clear. Left alone it would outlive
                // every lock, idle timeout and vault switch for the rest of the process, which is
                // precisely the exposure TD-11 exists to reduce. Cleared the moment it is handed
                // over. (A managed array still cannot be guaranteed un-copied by a compacting GC;
                // this narrows the window, it does not close it -- see TD-06.)
                _vaultSession.Unlock(vaultDir, key);
                Array.Clear(key, 0, key.Length);

                // The database phase (TD-28). Everything this used to do inline -- the explicit
                // DI scope, the probe that the unwrapped key really opens THIS database file, and
                // the schema migration afterwards -- now lives behind IVaultDatabaseOpener, in the
                // persistence adapter that owns those concepts. The auth phase above and the schema
                // phase in there stay distinct: a schema failure arrives as VaultMigrationException
                // and is caught below on its own terms, never as a wrong password.
                await _vaultDatabaseOpener.OpenExistingAsync(CancellationToken.None).ConfigureAwait(false);
            });

            // Vault is open: apply this vault's persisted accent theme before showing the shell.
            // Read through the mediator, not through the Scoped ISettingRepository: the mediator is
            // a singleton that opens its own scope per request, so nothing scoped escapes into this
            // long-lived view model and the read always targets the vault unlocked right now.
            await Pine.Ui.Avalonia.Utils.ThemeManager.ApplyFromSettingsAsync(_sender);

            // Transition to Dashboard
            _navigationService.NavigateTo(_viewModels.CreateShell());
        }
        catch (VaultMigrationException ex)
        {
            // The password was correct; the schema upgrade is what failed. Reporting this as an
            // auth failure would send the user to reset a password that is not the problem.
            // The path itself is deliberately not shown (TD-24), only where to look.
            _vaultSession.Lock();
            StatusMessage = ex.BackupPath is null
                ? "Your password was accepted, but Pine could not update the vault database. Your data was not modified. Please try again or reinstall the latest version of Pine."
                : "Your password was accepted, but Pine could not update the vault database. An encrypted backup of your data was saved in the 'backups' folder inside your vault before the update was attempted, so nothing is lost.";
        }
        catch (VaultBackupFailedException)
        {
            // The safety backup could not be written, so the migration was never attempted.
            _vaultSession.Lock();
            _message.Set("Unlock.Status.BackupFailed");
        }
        catch (UnsupportedVaultHeaderException)
        {
            _vaultSession.Lock();
            _message.Set("Unlock.Status.NewerVersion");
        }
        catch (VaultHeaderCorruptException)
        {
            _vaultSession.Lock();
            _message.Set("Unlock.Status.HeaderDamaged");
        }
        catch (VaultHeaderNotFoundException)
        {
            _vaultSession.Lock();
            _message.Set("Unlock.Status.NoVaultFound");
        }
        catch (VaultKeyUnwrapException)
        {
            // The data key could not be unwrapped: either the password is wrong or the header has
            // been altered since the wrap was made. THE WORDING IS IDENTICAL TO THE GENERIC CATCH
            // BELOW, ON PURPOSE. Distinguishing "your password is wrong" from "this vault is
            // damaged" would hand an attacker holding a copy of the vault exactly the oracle they
            // want -- confirmation that a candidate password was the failing part. VaultKeyUnwrap-
            // Exception itself carries no detail for the same reason; this block must not undo that
            // by saying more than the generic path does.
            _vaultSession.Lock();
            _message.Set("Unlock.Status.IncorrectPassword");
        }
        catch (LegacyVaultFormatException)
        {
            // A pre-envelope vault: its database key IS the raw Argon2id output, with no wrapped
            // data key to unwrap, so no password can open it in this build. Saying anything about
            // the password would send the user to retype a password that was very possibly right.
            // The vault directory is deliberately not named (TD-24).
            _vaultSession.Lock();
            _message.Set("Unlock.Status.PreReleaseVault");
        }
        catch (VaultKeyWrapUnavailableException)
        {
            // The header is valid and another build could open it; this platform simply does not
            // provide the AEAD the wrap needs. Unreachable on the four shipped heads since the
            // Phase 3 platform review deleted the Browser one -- kept for the same reason the
            // cipher's own guard is kept: it is a statement about AesGcm.IsSupported, which is a
            // runtime question on any target. Not an authentication failure, and must not be
            // reported as one.
            _vaultSession.Lock();
            _message.Set("Unlock.Status.PlatformUnsupported");
        }
        catch (Exception)
        {
            // Genuine authentication failure: the actual exception is deliberately collapsed so it
            // cannot be used as an oracle. Usually a CryptographicException or SqliteException.
            _message.Set("Unlock.Status.IncorrectPassword");
            
            // Re-lock the session just in case
            _vaultSession.Lock();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void GoBack()
    {
        _navigationService.NavigateTo(_viewModels.CreateVaultSelection());
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
        GC.SuppressFinalize(this);
    }

}
