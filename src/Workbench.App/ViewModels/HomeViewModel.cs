using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.App.Services;
using Workbench.Project.Opening;
using Workbench.Storage.Projects;

namespace Workbench.App.ViewModels;

public partial class HomeViewModel : ViewModelBase
{
    private readonly ProjectRepository _projectRepository;
    private readonly ProjectOpenService _projectOpenService;
    private readonly IFolderPickerService _folderPickerService;
    private readonly Func<ProjectOpenResult, Task> _onProjectOpened;
    private readonly Func<string, CancellationToken, Task<ProjectOpenResult>> _openProject;
    private readonly Func<Task>? _showSettings;

    public HomeViewModel(
        ProjectRepository projectRepository,
        ProjectOpenService projectOpenService,
        IFolderPickerService folderPickerService,
        Func<ProjectOpenResult, Task> onProjectOpened,
        Func<string, CancellationToken, Task<ProjectOpenResult>>? openProject = null,
        Func<Task>? showSettings = null)
    {
        _projectRepository = projectRepository;
        _projectOpenService = projectOpenService;
        _folderPickerService = folderPickerService;
        _onProjectOpened = onProjectOpened;
        _openProject = openProject ?? _projectOpenService.OpenAsync;
        _showSettings = showSettings;
    }

    public ObservableCollection<RecentProjectItemViewModel> RecentProjects { get; } = [];

    public bool HasNoRecentProjects => RecentProjects.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpenProjects))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    public bool CanOpenProjects => !IsBusy;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var projects = await _projectRepository.GetRecentAsync(20, cancellationToken);
        RecentProjects.Clear();
        foreach (var project in projects)
        {
            RecentProjects.Add(new RecentProjectItemViewModel(project));
        }

        OnPropertyChanged(nameof(HasNoRecentProjects));
    }

    public async Task OpenProjectFolderAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        var folderPath = await _folderPickerService.PickFolderAsync(cancellationToken);
        if (folderPath is not null)
        {
            await OpenPathAsync(folderPath, cancellationToken);
        }
    }

    public Task OpenRecentProjectAsync(RecentProjectItemViewModel item, CancellationToken cancellationToken = default) =>
        item.IsAvailable ? OpenPathAsync(item.RootPath, cancellationToken) : Task.CompletedTask;

    [RelayCommand]
    private Task OpenProjectFolder() => OpenProjectFolderAsync();

    [RelayCommand]
    private Task ShowSettings() => _showSettings?.Invoke() ?? Task.CompletedTask;

    [RelayCommand]
    private Task OpenRecentProject(RecentProjectItemViewModel item) => OpenRecentProjectAsync(item);

    public async Task OpenPathAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _openProject(folderPath, cancellationToken);
            await LoadAsync(cancellationToken);
            await _onProjectOpened(result);
        }
        catch (DirectoryNotFoundException)
        {
            ErrorMessage = "Could not open this project. The folder is no longer available.";
        }
        catch (Exception)
        {
            ErrorMessage = "Could not open this project.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void SetStartupError(string message) => ErrorMessage = message;
}
