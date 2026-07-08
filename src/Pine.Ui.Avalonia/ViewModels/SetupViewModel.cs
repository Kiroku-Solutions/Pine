using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediatR;
using Pine.Application.Accounts.Commands;
using Pine.Core.Accounts;
using Pine.Infrastructure.Data;
using Pine.Ui.Avalonia.Services;

namespace Pine.Ui.Avalonia.ViewModels;

public partial class SetupViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    private readonly PineDbContext _dbContext;
    private readonly INavigationService _navigationService;

    [ObservableProperty]
    public partial string MasterPassword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "Welcome to Pine. Enter your new master password to initialize the vault.";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public SetupViewModel(IMediator mediator, PineDbContext dbContext, INavigationService navigationService)
    {
        _mediator = mediator;
        _dbContext = dbContext;
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
        StatusMessage = "Deriving key with Argon2id... (this takes a moment)";

        try
        {
            await Task.Run(() => 
            {
                _dbContext.Database.EnsureCreated();
            });

            StatusMessage = "Vault initialized! Creating default account...";

            // Send CQRS Command
            await _mediator.Send(new CreateAccountCommand("My First Bank", "USD", AccountType.Bank, 1000m));

            // Setup is complete! Transition to the Dashboard.
            _navigationService.NavigateTo<DashboardViewModel>();
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
