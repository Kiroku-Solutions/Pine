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
    private readonly IKeyDerivationService _keyDerivationService;

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
        IKeyDerivationService keyDerivationService)
    {
        _serviceProvider = serviceProvider;
        _vaultSession = vaultSession;
        _navigationService = navigationService;
        _vaultHeaderStore = vaultHeaderStore;
        _keyDerivationService = keyDerivationService;
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

                // The header states which KDF and which cost parameters produced this vault's key.
                // A pre-TD-05 vault that only has pine.salt is adopted into a versioned header here
                // (the legacy file is kept as a fallback). If the header cannot be understood this
                // throws rather than guessing parameters, which would silently derive a wrong key.
                var header = _vaultHeaderStore.ReadOrAdoptLegacy(vaultDir);

                var key = _keyDerivationService.DeriveKey(MasterPassword, header);

                // Unlock session
                _vaultSession.Unlock(vaultDir, key);

                // PineDbContext and IVaultMigrator are both Scoped (TD-01). Resolving either from
                // the ROOT provider silently creates a root-scoped instance that never dies, so the
                // context built here - carrying THIS vault's file path and encryption key - would
                // still be handed out after the user locks the vault and unlocks a different one.
                // One explicit scope covers both the auth probe and the migration, and is disposed
                // only after MigrateAsync has completed.
                using var vaultScope = _serviceProvider.CreateScope();

                // Verify db access to ensure password was correct
                var dbContext = vaultScope.ServiceProvider.GetRequiredService<PineDbContext>();

                // Force a query to evaluate SQLCipher decryption. If the password is wrong, 
                // this will throw an exception (e.g., "file is not a database") which we catch below.
                bool canConnect = dbContext.Database.CanConnect();
                if (!canConnect) 
                {
                    throw new InvalidOperationException("Cannot connect to database.");
                }

                // Force actual read to trigger SQLCipher decryption failure if password is wrong
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
