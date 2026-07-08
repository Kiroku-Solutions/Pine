using CommunityToolkit.Mvvm.ComponentModel;

namespace Pine.Ui.Avalonia.Services;

public interface INavigationService
{
    void NavigateTo<TViewModel>() where TViewModel : ObservableObject;
}
