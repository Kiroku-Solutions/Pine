using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Pine.Application.Accounts.Commands;
using Pine.Application.Security;
using Pine.Core.Accounts;
using Pine.Infrastructure.Data;
using Pine.Infrastructure.Security;
using Pine.Ui.Avalonia.Services;

namespace Pine.Ui.Avalonia.ViewModels;

public partial class SetupViewModel : ObservableObject
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IVaultSession _vaultSession;
    private readonly INavigationService _navigationService;

    [ObservableProperty]
    public partial string MasterPassword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "Welcome to Pine. Enter a strong master password to encrypt your vault.";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public SetupViewModel(IServiceProvider serviceProvider, IVaultSession vaultSession, INavigationService navigationService)
    {
        _serviceProvider = serviceProvider;
        _vaultSession = vaultSession;
        _navigationService = navigationService;
    }

    [RelayCommand]
    private async Task InitializeVaultAsync()
    {
        if (string.IsNullOrWhiteSpace(MasterPassword))
        {
            StatusMessage = "Password cannot be empty.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Encrypting vault... (this takes a moment)";

        try
        {
            await Task.Run(() => 
            {
                // Generate a salt and save it next to the database
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var vaultDir = Path.Combine(appData, "Pine", "Vault");
                if (!Directory.Exists(vaultDir)) Directory.CreateDirectory(vaultDir);
                
                var saltPath = Path.Combine(vaultDir, "pine.salt");
                
                if (File.Exists(saltPath))
                {
                    throw new InvalidOperationException("A vault already exists. Please login instead.");
                }

                var salt = Argon2KeyDerivation.GenerateSalt();
                File.WriteAllBytes(saltPath, salt);

                // Derive key
                var key = Argon2KeyDerivation.DeriveKey(MasterPassword, salt);
                
                // Unlock session
                _vaultSession.Unlock(key);

                // Now that it's unlocked, we can safely request PineDbContext and ensure created
                var dbContext = _serviceProvider.GetRequiredService<PineDbContext>();
                dbContext.Database.EnsureCreated();
            });

            StatusMessage = "Vault initialized! Creating default account...";

            // Send CQRS Command
            var mediator = _serviceProvider.GetRequiredService<IMediator>();
            await mediator.Send(new CreateAccountCommand("My First Bank", "USD", AccountType.Bank, 1000m));

            // Setup is complete! Transition to the Dashboard.
            _navigationService.NavigateTo<ShellViewModel>();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
