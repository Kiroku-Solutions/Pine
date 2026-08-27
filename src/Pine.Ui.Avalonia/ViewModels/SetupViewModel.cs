using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Pine.Application.Accounts.Commands;
using Pine.Application.Security;
using Pine.Application.Settings.Commands;
using Pine.Core.Accounts;
using Pine.Core.Security;
using Pine.Ui.Avalonia.Services;

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
    private readonly IKeyDerivationService _keyDerivationService;

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
    public partial string VaultName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MasterPassword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "Enter a strong master password to encrypt your vault.";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public SetupViewModel(
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

        if (string.IsNullOrWhiteSpace(VaultName))
        {
            StatusMessage = "Vault name cannot be empty.";
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

                // Writes pine.vault.json recording the KDF id and its cost parameters alongside the
                // salt, so this vault stays openable even after those parameters change (TD-05).
                var header = _vaultHeaderStore.Create(vaultDir);

                var key = _keyDerivationService.DeriveKey(MasterPassword, header);
                _vaultSession.Unlock(vaultDir, key);

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

            StatusMessage = "Vault initialized! Saving settings...";

            // These two settings go through the mediator, not through ISettingRepository (TD-01).
            // The repository no longer calls SaveChangesAsync, so a direct SaveAsync here would be
            // staged on a context nobody commits and the user's storage-mode and telemetry choices
            // would be silently lost on first run.
            var mediator = _serviceProvider.GetRequiredService<IMediator>();
            await mediator.Send(new SaveSettingCommand("System.StorageMode", IsCloudSync ? "CloudSync" : "LocalOnly"));
            await mediator.Send(new SaveSettingCommand("System.Telemetry", TelemetryOptIn.ToString()));

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
