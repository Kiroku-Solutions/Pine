using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Pine.Application.Security;
using Pine.Infrastructure.Data;
using Pine.Infrastructure.Security;
using Pine.Ui.Avalonia.Services;

namespace Pine.Ui.Avalonia.ViewModels;

public partial class UnlockViewModel : ObservableObject
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IVaultSession _vaultSession;
    private readonly INavigationService _navigationService;

    private VaultProfile? _profile;

    [ObservableProperty]
    public partial string MasterPassword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string VaultName { get; set; } = "Unlock Vault";

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public UnlockViewModel(IServiceProvider serviceProvider, IVaultSession vaultSession, INavigationService navigationService)
    {
        _serviceProvider = serviceProvider;
        _vaultSession = vaultSession;
        _navigationService = navigationService;
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
            return;
        }

        try
        {
            await Task.Run(() =>
            {
                var vaultDir = _profile.Path;
                var saltPath = Path.Combine(vaultDir, "pine.salt");

                if (!File.Exists(saltPath))
                {
                    throw new FileNotFoundException("Vault salt not found. The vault might be corrupted or missing.");
                }

                var salt = File.ReadAllBytes(saltPath);
                
                // Derive key
                var key = Argon2KeyDerivation.DeriveKey(MasterPassword, salt);

                // Unlock session
                _vaultSession.Unlock(vaultDir, key);

                // Verify db access to ensure password was correct
                var dbContext = _serviceProvider.GetRequiredService<PineDbContext>();
                
                // Force a query to evaluate SQLCipher decryption. If the password is wrong, 
                // this will throw an exception (e.g., "file is not a database") which we catch below.
                bool canConnect = dbContext.Database.CanConnect();
                if (!canConnect) 
                {
                    throw new InvalidOperationException("Cannot connect to database.");
                }

                // Force actual read to trigger SQLCipher decryption failure if password is wrong
                _ = System.Linq.Queryable.Any(dbContext.Accounts);
            });

            // Transition to Dashboard
            _navigationService.NavigateTo<ShellViewModel>();
        }
        catch (Exception)
        {
            // We obscure the actual exception for security, but usually it's a CryptographicException or SqliteException
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
