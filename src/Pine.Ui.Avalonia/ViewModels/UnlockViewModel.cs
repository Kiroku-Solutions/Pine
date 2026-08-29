using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Pine.Application.Security;
using Pine.Core.Security;
using Pine.Infrastructure.Data;
using Pine.Ui.Avalonia.Services;

namespace Pine.Ui.Avalonia.ViewModels;

public partial class UnlockViewModel : ObservableObject
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IVaultSession _vaultSession;
    private readonly INavigationService _navigationService;
    private readonly IVaultHeaderStore _vaultHeaderStore;

    /// <summary>
    /// The envelope (TD-06). This screen no longer derives a database key from the password: it
    /// asks this service to unwrap the vault's stored data key, and a wrong password is a failed
    /// AEAD tag inside that call rather than a SQLCipher error guessed at further down.
    /// </summary>
    private readonly IVaultKeyService _vaultKeyService;

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
        IServiceProvider serviceProvider,
        IVaultSession vaultSession,
        INavigationService navigationService,
        IVaultHeaderStore vaultHeaderStore,
        IVaultKeyService vaultKeyService)
    {
        _serviceProvider = serviceProvider;
        _vaultSession = vaultSession;
        _navigationService = navigationService;
        _vaultHeaderStore = vaultHeaderStore;
        _vaultKeyService = vaultKeyService;
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
            StatusMessage = "Password cannot be empty.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Decrypting vault...";

        if (_profile == null)
        {
            StatusMessage = "No vault profile selected.";
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

                // PineDbContext and IVaultMigrator are both Scoped (TD-01). Resolving either from
                // the ROOT provider silently creates a root-scoped instance that never dies, so the
                // context built here - carrying THIS vault's file path and encryption key - would
                // still be handed out after the user locks the vault and unlocks a different one.
                // One explicit scope covers both the auth probe and the migration, and is disposed
                // only after MigrateAsync has completed.
                using var vaultScope = _serviceProvider.CreateScope();

                // THE PROBE IS NO LONGER THE PASSWORD CHECK, AND THE DIFFERENCE MATTERS (TD-06).
                // Before the envelope, this query WAS the authentication: the password derived the
                // database key directly, so "SQLCipher refused to read a page" was the only signal
                // that the password had been wrong. Now UnwrapDataKey has already answered that
                // question above, with an authentication tag rather than an inference.
                //
                // What the probe still proves is a different proposition: that the data key we just
                // unwrapped actually opens THIS database file. Those can diverge -- a header paired
                // with a database from another vault, a restored backup, a half-finished copy -- and
                // that mismatch is not a wrong password and would otherwise only surface later, on
                // some arbitrary first query deep inside the app. It is checked here, once, up front.
                var dbContext = vaultScope.ServiceProvider.GetRequiredService<PineDbContext>();

                bool canConnect = dbContext.Database.CanConnect();
                if (!canConnect)
                {
                    throw new InvalidOperationException("Cannot connect to database.");
                }

                // CanConnect alone is too weak: it can succeed without ever decrypting a page. One
                // real read forces SQLCipher to actually use the key.
                _ = System.Linq.Queryable.Any(dbContext.Accounts);

                // Schema phase, kept distinct from the auth phase above so its failures are not
                // reported as a wrong password. Resolved only after Unlock, because the migrator
                // depends on PineDbContext, which refuses to be built while the session is locked.
                // No-op (and no backup) when the schema is already current.
                var migrator = vaultScope.ServiceProvider.GetRequiredService<IVaultMigrator>();
                await migrator.MigrateAsync(CancellationToken.None).ConfigureAwait(false);
            });

            // Vault is open: apply this vault's persisted accent theme before showing the shell.
            // Read through the mediator, not through the Scoped ISettingRepository: the mediator is
            // a singleton that opens its own scope per request, so nothing scoped escapes into this
            // long-lived view model and the read always targets the vault unlocked right now.
            var sender = _serviceProvider.GetService<ISender>();
            if (sender != null)
                await Pine.Ui.Avalonia.Utils.ThemeManager.ApplyFromSettingsAsync(sender);

            // Transition to Dashboard
            _navigationService.NavigateTo<ShellViewModel>();
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
            StatusMessage = "Pine could not create a safety backup before updating the vault database, so the update was cancelled and your data is unchanged. Free up disk space and try again.";
        }
        catch (UnsupportedVaultHeaderException)
        {
            _vaultSession.Lock();
            StatusMessage = "This vault was created by a newer version of Pine and cannot be opened by this one. Please update Pine.";
        }
        catch (VaultHeaderCorruptException)
        {
            _vaultSession.Lock();
            StatusMessage = "This vault's header file is damaged, so Pine cannot tell how to unlock it. Restore the vault folder from a backup.";
        }
        catch (VaultHeaderNotFoundException)
        {
            _vaultSession.Lock();
            StatusMessage = "No Pine vault was found in this folder.";
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
            StatusMessage = "Incorrect password or corrupted vault.";
        }
        catch (LegacyVaultFormatException)
        {
            // A pre-envelope vault: its database key IS the raw Argon2id output, with no wrapped
            // data key to unwrap, so no password can open it in this build. Saying anything about
            // the password would send the user to retype a password that was very possibly right.
            // The vault directory is deliberately not named (TD-24).
            _vaultSession.Lock();
            StatusMessage = "This vault was created by a pre-release build of Pine, before encrypted key storage, and cannot be opened by this version. Please create a new vault.";
        }
        catch (VaultKeyWrapUnavailableException)
        {
            // The header is valid and another build could open it; this platform simply does not
            // provide the AEAD the wrap needs (AesGcm.IsSupported is false on the browser runtime).
            // Not an authentication failure, and must not be reported as one.
            _vaultSession.Lock();
            StatusMessage = "This build of Pine cannot open vaults on this platform, because the encryption it requires is not available here. Your vault is undamaged -- open it with the desktop version of Pine.";
        }
        catch (Exception)
        {
            // Genuine authentication failure: the actual exception is deliberately collapsed so it
            // cannot be used as an oracle. Usually a CryptographicException or SqliteException.
            StatusMessage = "Incorrect password or corrupted vault.";
            
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
        _navigationService.NavigateTo<VaultSelectionViewModel>();
    }
}
