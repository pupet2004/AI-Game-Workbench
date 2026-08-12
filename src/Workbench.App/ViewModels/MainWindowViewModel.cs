using CommunityToolkit.Mvvm.ComponentModel;
using Workbench.App.Services;
using Workbench.Project.Opening;
using Workbench.Storage.Database;

namespace Workbench.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly IFolderPickerService _folderPickerService;

    public MainWindowViewModel(AppServices services, IFolderPickerService folderPickerService)
    {
        _services = services;
        _folderPickerService = folderPickerService;
        CurrentPage = CreateHome();
    }

    [ObservableProperty]
    public partial ViewModelBase CurrentPage { get; set; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var home = (HomeViewModel)CurrentPage;
        try
        {
            await _services.InitializeAsync(cancellationToken);
            await home.LoadAsync(cancellationToken);
        }
        catch (DatabaseInitializationException)
        {
            home.SetStartupError("Could not initialize the local Workbench database. Your existing data was not changed.");
        }
        catch (Exception)
        {
            home.SetStartupError("Could not start AI Game Workbench.");
        }
    }

    public void BackToHome()
    {
        CurrentPage = CreateHome();
        _ = ((HomeViewModel)CurrentPage).LoadAsync();
    }

    private HomeViewModel CreateHome() =>
        new(
            _services.ProjectRepository,
            _services.ProjectOpenService,
            _folderPickerService,
            ShowWorkspace);

    private Task ShowWorkspace(ProjectOpenResult result)
    {
        CurrentPage = new WorkspacePlaceholderViewModel(result, BackToHome);
        return Task.CompletedTask;
    }
}
