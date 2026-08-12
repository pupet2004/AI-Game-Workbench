using CommunityToolkit.Mvvm.ComponentModel;
using Workbench.Core.Projects;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.ViewModels;

public sealed class RecentProjectItemViewModel : ViewModelBase
{
    public RecentProjectItemViewModel(CoreProject project)
    {
        Project = project;
        Availability = Directory.Exists(project.RootPath)
            ? ProjectAvailability.Available
            : ProjectAvailability.PathMissing;
    }

    public CoreProject Project { get; }

    public string Name => Project.Name;

    public string RootPath => Project.RootPath;

    public ProjectType Type => Project.Type;

    public ProjectAvailability Availability { get; }

    public bool IsAvailable => Availability == ProjectAvailability.Available;

    public string GitLabel => string.IsNullOrWhiteSpace(Project.GitRoot) ? "No Git" : "Git";
}
