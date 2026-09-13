using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.App.Services;
using Workbench.App.ViewModels;
using Workbench.Project.Opening;

namespace Workbench.App.ProjectWorld;

public sealed record ProjectHistoryItemView(
    DateTimeOffset OccurredAt,
    string CategoryText,
    string Summary,
    string AuthorityStatus,
    IReadOnlyList<string> SourceRefs)
{
    public string TimestampText =>
        OccurredAt.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture);

    public string SourceText =>
        SourceRefs.Count == 0
            ? LocalizationService.Current["Dynamic.NoSources"]
            : string.Join(", ", SourceRefs);
}

public sealed partial class ProjectHistoryViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly ProjectOpenResult _result;
    private readonly Func<Task> _back;
    private readonly LocalizationService _localization;

    public ProjectHistoryViewModel(
        AppServices services,
        ProjectOpenResult result,
        Func<Task> back,
        LocalizationService? localization = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _result = result ?? throw new ArgumentNullException(nameof(result));
        _back = back ?? throw new ArgumentNullException(nameof(back));
        _localization = localization ?? new LocalizationService(services.WorkbenchSettingsRepository);
    }

    public string ProjectName => _result.Project.Name;
    public string ProjectPath => _result.Project.RootPath;
    public ObservableCollection<ProjectHistoryItemView> Entries { get; } = [];

    public string EntryCountText =>
        string.Format(_localization["History.EntryCount"], Entries.Count);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    public partial bool Loading { get; private set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    public bool IsBusy => Loading;

    public async Task InitializeAsync(CancellationToken cancellationToken = default) =>
        await LoadAsync(cancellationToken);

    [RelayCommand]
    private Task Reload() => LoadAsync(CancellationToken.None);

    [RelayCommand]
    private Task Back() => _back();

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Loading = true;
        ErrorMessage = null;
        Entries.Clear();
        OnPropertyChanged(nameof(EntryCountText));
        try
        {
            foreach (var entry in await _services.ProjectEvolutionIndex.ListAsync(_result.Project.Id, cancellationToken))
            {
                Entries.Add(new ProjectHistoryItemView(
                    entry.OccurredAt,
                    GetCategoryText(entry.Category),
                    entry.Summary,
                    entry.AuthorityStatus,
                    entry.SourceRefs));
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            OnPropertyChanged(nameof(EntryCountText));
            Loading = false;
        }
    }

    private string GetCategoryText(ProjectEvolutionCategory category) =>
        _localization[$"History.Category.{category}"];
}
