using CommunityToolkit.Mvvm.Input;
using Workbench.Project.Opening;

namespace Workbench.App.ViewModels;

public sealed partial class WorkspacePlaceholderViewModel : ViewModelBase
{
    private readonly Action _backToProjects;

    public WorkspacePlaceholderViewModel(ProjectOpenResult result, Action backToProjects)
    {
        Result = result;
        _backToProjects = backToProjects;
    }

    public ProjectOpenResult Result { get; }

    public string ProjectTypeText => Result.Project.Type.ToString();

    public string GitStatusText => Result.Git.IsRepository
        ? Result.Git.IsDetachedHead ? "Git repository · Detached HEAD" : $"Git repository · {Result.Git.BranchName}"
        : "Not a Git repository";

    [RelayCommand]
    private void BackToProjects() => _backToProjects();
}
