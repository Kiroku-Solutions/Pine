using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Pine.Ui.Avalonia.Services;

namespace Pine.Ui.Avalonia.ViewModels;

public partial class MainViewModel : ObservableObject, INavigationService
{
    private readonly IServiceProvider _serviceProvider;

    [ObservableProperty]
    public partial ObservableObject CurrentPage { get; set; } = null!;

    public MainViewModel(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public void Initialize()
    {
        NavigateTo<SetupViewModel>();
    }

    public void NavigateTo<TViewModel>() where TViewModel : ObservableObject
    {
        CurrentPage = _serviceProvider.GetRequiredService<TViewModel>();
    }
}
