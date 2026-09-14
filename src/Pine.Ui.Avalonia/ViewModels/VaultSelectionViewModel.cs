using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pine.Application.Security;
using Pine.Core.Common;
using Pine.Ui.Avalonia.Diagnostics;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.Services;

namespace Pine.Ui.Avalonia.ViewModels;

public partial class VaultSelectionViewModel : ObservableObject, IAsyncInitializable, IDisposable
{
    private readonly LocalizedMessageSlot _error;
    private readonly LocalizedMessageSlot _lockNotice;

    private readonly VaultManager _vaultManager;
    private readonly INavigationService _navigationService;
    private readonly IViewModelFactory _viewModels;
    private readonly ILogger<VaultSelectionViewModel>? _logger;

    [ObservableProperty]
    public partial ObservableCollection<VaultProfile> Vaults { get; set; } = new();

    /// <summary>
    /// TD-11: set by <c>MainViewModel</c> when it navigates here because the vault just locked
    /// (idle timeout, manual lock, or the app being suspended), so this screen can say why the
    /// user is looking at it instead of the shell they were just using -- otherwise a sudden idle
    /// lock reads as a crash. Empty on every other route into this screen (first run, "create new
    /// vault", the normal vault picker).
    /// </summary>
    [ObservableProperty]
    public partial string? LockNotice { get; private set; }

    /// <summary>Shows why the vault locked, in the active language (TD-139).</summary>
    /// <param name="notice">Named by <c>MainViewModel</c>, which knows the reason.</param>
    public void ShowLockNotice(UserFacingMessage notice) => _lockNotice.Set(notice);

    /// <summary>
    /// What the user reads when the vault list cannot be read.
    /// </summary>
    /// <remarks>
    /// THIS SCREEN'S SILENT FAILURE WAS THE WORST ONE IN THE APPLICATION (TD-35). The constructor
    /// discarded <c>LoadVaultsAsync()</c> and that method has no <c>catch</c>, so any failure to
    /// enumerate the vaults produced an empty list and nothing else -- no message, no log record.
    /// A person whose vaults had failed to enumerate saw a screen saying, in effect, that they had
    /// no vaults. For a local-first application whose entire contents live in those files, that is
    /// the single most alarming lie the UI could tell, and it told it silently.
    /// </remarks>
    private static readonly UserFacingMessage VaultsUnavailable = new("VaultSelection.Failure.Load");

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    public VaultSelectionViewModel(
        VaultManager vaultManager,
        INavigationService navigationService,
        IViewModelFactory viewModels,
        ILogger<VaultSelectionViewModel>? logger = null,
        ILocalizer? localizer = null)
    {
        _vaultManager = vaultManager;
        _navigationService = navigationService;
        _viewModels = viewModels;
        _logger = logger;
        localizer ??= new Localizer();
        _error = new LocalizedMessageSlot(localizer, text => ErrorMessage = text);
        _lockNotice = new LocalizedMessageSlot(localizer, text => LockNotice = text);
    }

    /// <summary>Releases the localized message slot.</summary>
    /// <remarks>
    /// CA1001, for the reason every page that owns a slot gives: it subscribes to the process-lifetime
    /// localizer. <c>PageActivation.Deactivate</c> disposes a page that implements this.
    /// </remarks>
    public void Dispose()
    {
        _error.Dispose();
        _lockNotice.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public Task InitializeAsync(CancellationToken cancellationToken) => LoadVaultsAsync();

    /// <inheritdoc />
    public InitializationFailurePolicy InitializationFailurePolicy => new(
        _logger,
        "vault-list-load",
        VaultsUnavailable,
        _error.Set);

    private async Task LoadVaultsAsync()
    {
        var vaults = await _vaultManager.GetVaultsAsync();

        // Cleared only after the await has produced a list. Clearing first would empty the screen
        // and then leave it empty if the read threw -- the same "your vaults are gone" lie, just
        // arrived at from the other direction.
        _error.Clear();
        Vaults.Clear();
        foreach (var vault in vaults)
        {
            Vaults.Add(vault);
        }
    }

    [RelayCommand]
    private void CreateNewVault()
    {
        _navigationService.NavigateTo(_viewModels.CreateSetup());
    }

    [RelayCommand]
    private void SelectVault(VaultProfile vault)
    {
        if (vault != null)
        {
            var unlock = _viewModels.CreateUnlock();
            unlock.Initialize(vault);
            _navigationService.NavigateTo(unlock);
        }
    }
}
