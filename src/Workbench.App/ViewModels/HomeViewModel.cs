using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.App.Services;
using Workbench.Project.Opening;
using Workbench.Storage.Projects;
using Workbench.App.ProjectWorld;

namespace Workbench.App.ViewModels;

public partial class HomeViewModel : ViewModelBase
{
    private readonly ProjectRepository _projectRepository;
    private readonly ProjectOpenService _projectOpenService;
    private readonly IFolderPickerService _folderPickerService;
    private readonly Func<ProjectOpenResult, Task> _onProjectOpened;
    private readonly Func<string, CancellationToken, Task<ProjectOpenResult>> _openProject;
    private readonly Func<Task>? _showSettings;
    private readonly Func<ProjectOpenResult, Task>? _createProject;
    private readonly ProjectWorldEntryStatusService? _entryStatusService;
    private readonly LocalizationService _localization;

    public HomeViewModel(
        ProjectRepository projectRepository,
        ProjectOpenService projectOpenService,
        IFolderPickerService folderPickerService,
        Func<ProjectOpenResult, Task> onProjectOpened,
        Func<string, CancellationToken, Task<ProjectOpenResult>>? openProject = null,
        Func<Task>? showSettings = null,
        ProjectWorldEntryStatusService? entryStatusService = null,
        Func<ProjectOpenResult, Task>? createProject = null,
        LocalizationService? localization = null)
    {
        _projectRepository = projectRepository;
        _projectOpenService = projectOpenService;
        _folderPickerService = folderPickerService;
        _onProjectOpened = onProjectOpened;
        _openProject = openProject ?? _projectOpenService.OpenAsync;
        _showSettings = showSettings;
        _entryStatusService = entryStatusService;
        _createProject = createProject;
        _localization = localization ?? new LocalizationService();
        _localization.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is "Item[]" or nameof(LocalizationService.Language))
            {
                OnPropertyChanged("Item[]");
            }
        };
    }

    public ObservableCollection<RecentProjectItemViewModel> RecentProjects { get; } = [];

    public string this[string key] => _localization[key];

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
            var item = new RecentProjectItemViewModel(project);
            if (_entryStatusService is not null)
            {
                item.EntryStatus = await _entryStatusService.GetStatusAsync(project, cancellationToken);
            }

            RecentProjects.Add(item);
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

    public async Task CreateProjectAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy || _createProject is null)
            return;

        var folderPath = await _folderPickerService.PickFolderAsync(cancellationToken);
        if (folderPath is not null)
        {
            await OpenAndCreateAsync(folderPath, cancellationToken);
        }
    }

    [RelayCommand]
    private Task CreateProject() => CreateProjectAsync();

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
            ErrorMessage = "Could not open this project. The folder is no longer available; nothing was changed.";
        }
        catch (Exception)
        {
            ErrorMessage = "Could not open this project; nothing was changed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void SetStartupError(string message) => ErrorMessage = message;

    private async Task OpenAndCreateAsync(string folderPath, CancellationToken cancellationToken)
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _openProject(folderPath, cancellationToken);
            await LoadAsync(cancellationToken);
            await _createProject!(result);
        }
        catch (DirectoryNotFoundException)
        {
            ErrorMessage = "Could not create this project. The folder is no longer available; nothing was changed.";
        }
        catch (Exception)
        {
            ErrorMessage = "Could not create this project; nothing was changed.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
