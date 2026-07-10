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
using Pine.Core.Configuration;
using Pine.Core.Settings;
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
    public partial int CurrentStep { get; set; } = 1;
    
    public bool IsStep1 => CurrentStep == 1;
    public bool IsStep2 => CurrentStep == 2;

    partial void OnCurrentStepChanged(int value)
    {
        OnPropertyChanged(nameof(IsStep1));
        OnPropertyChanged(nameof(IsStep2));
    }

    [ObservableProperty]
    public partial bool IsLocalOnly { get; set; } = true;

    [ObservableProperty]
    public partial bool IsCloudSync { get; set; }

    [ObservableProperty]
    public partial bool TelemetryOptIn { get; set; }

    [ObservableProperty]
    public partial string MasterPassword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "Enter a strong master password to encrypt your vault.";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public SetupViewModel(IServiceProvider serviceProvider, IVaultSession vaultSession, INavigationService navigationService)
    {
        _serviceProvider = serviceProvider;
        _vaultSession = vaultSession;
        _navigationService = navigationService;
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
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var vaultDir = Path.Combine(appData, "Pine", "Vault");
                if (!Directory.Exists(vaultDir)) Directory.CreateDirectory(vaultDir);
                
                var saltPath = Path.Combine(vaultDir, "pine.salt");
                if (File.Exists(saltPath))
                    throw new InvalidOperationException("A vault already exists. Please login instead.");

                var salt = Argon2KeyDerivation.GenerateSalt();
                File.WriteAllBytes(saltPath, salt);

                var key = Argon2KeyDerivation.DeriveKey(MasterPassword, salt);
                _vaultSession.Unlock(key);

                var dbContext = _serviceProvider.GetRequiredService<PineDbContext>();
                dbContext.Database.EnsureCreated();
            });

            StatusMessage = "Vault initialized! Saving settings...";

            var settingRepo = _serviceProvider.GetRequiredService<ISettingRepository>();
            await settingRepo.SaveAsync(Setting.Create("System.StorageMode", IsCloudSync ? "CloudSync" : "LocalOnly"));
            await settingRepo.SaveAsync(Setting.Create("System.Telemetry", TelemetryOptIn.ToString()));

            var mediator = _serviceProvider.GetRequiredService<IMediator>();
            await mediator.Send(new CreateAccountCommand("My First Bank", "USD", AccountType.Bank, 1000m));

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
