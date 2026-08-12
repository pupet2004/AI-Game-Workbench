using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Project.Opening;

namespace Workbench.App.ViewModels.Panes;

public enum LibrarySection
{
    Overview,
    Browse,
    History,
    Project
}

public partial class LibraryPaneViewModel : ViewModelBase
{
    private readonly Func<Task> _focus;

    public LibraryPaneViewModel(ProjectOpenResult result, Func<Task> focus)
    {
        Result = result;
        _focus = focus;
    }

    public LibraryPaneViewModel(ProjectOpenResult result)
        : this(result, () => Task.CompletedTask)
    {
    }

    public ProjectOpenResult Result { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverview), nameof(IsBrowse), nameof(IsHistory), nameof(IsProject))]
    public partial LibrarySection SelectedSection { get; set; } = LibrarySection.Overview;

    public bool IsOverview => SelectedSection == LibrarySection.Overview;

    public bool IsBrowse => SelectedSection == LibrarySection.Browse;

    public bool IsHistory => SelectedSection == LibrarySection.History;

    public bool IsProject => SelectedSection == LibrarySection.Project;

    public string ProjectName => Result.Project.Name;

    public string ProjectType => Result.Project.Type.ToString();

    public string ProjectPath => Result.Project.RootPath;

    public string GitStatus => !Result.Git.GitInstalled
        ? "Unavailable"
        : Result.Git.IsRepository ? "Repository" : "No Repository";

    public string BranchText => Result.Git.IsDetachedHead ? "Detached" : Result.Git.BranchName ?? "—";

    public string ShortHead => string.IsNullOrWhiteSpace(Result.Git.HeadCommit)
        ? "—"
        : Result.Git.HeadCommit[..Math.Min(10, Result.Git.HeadCommit.Length)];

    public string WorkingTreeText => Result.Git.IsRepository
        ? Result.Git.IsDirty ? "Modified" : "Clean"
        : "—";

    public string? StatusMessage => Result.Git.Error is null ? null : "Git status unavailable";

    [RelayCommand]
    private void ShowOverview() => SelectedSection = LibrarySection.Overview;

    [RelayCommand]
    private void ShowBrowse() => SelectedSection = LibrarySection.Browse;

    [RelayCommand]
    private void ShowHistory() => SelectedSection = LibrarySection.History;

    [RelayCommand]
    private void ShowProject() => SelectedSection = LibrarySection.Project;

    [RelayCommand]
    private Task Focus() => _focus();
}
