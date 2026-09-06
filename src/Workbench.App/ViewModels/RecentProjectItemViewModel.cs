using CommunityToolkit.Mvvm.ComponentModel;
using Workbench.App.ProjectWorld;
using Workbench.App.Services;
using Workbench.Core.Projects;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.ViewModels;

public sealed partial class RecentProjectItemViewModel : ViewModelBase
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
    public string ProjectId => Project.Id.ToString();
    public string CreatedAtText => Project.CreatedAt.LocalDateTime.ToString("g");
    public string LastOpenedAtText => Project.LastOpenedAt.LocalDateTime.ToString("g");

    public ProjectType Type => Project.Type;
    public string TypeLabel => Project.Type switch
    {
        ProjectType.Generic => LocalizationService.Current["Project.TypeGeneric"],
        _ => Project.Type.ToString()
    };

    public ProjectAvailability Availability { get; }

    public bool IsAvailable => Availability == ProjectAvailability.Available;

    public string GitLabel => string.IsNullOrWhiteSpace(Project.GitRoot)
        ? LocalizationService.Current["Project.NoGit"]
        : LocalizationService.Current["Project.Git"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EntryStatusLabel), nameof(ProjectStateLabel), nameof(ProjectWorldActionLabel), nameof(HasProjectWorld), nameof(TypeLabel))]
    public partial ProjectWorldEntryStatus? EntryStatus { get; set; }

    public string EntryStatusLabel => EntryStatus?.DisplayLabel ?? LocalizationService.Current["Status.Unavailable"];

    public string ProjectStateLabel => EntryStatusLabel;
    public bool HasProjectWorld => EntryStatus?.Kind == ProjectWorldEntryKind.ProjectWorldReady;
    public string ProjectWorldActionLabel => HasProjectWorld
        ? LocalizationService.Current["Project.OpenWorld"]
        : LocalizationService.Current["Project.OpenSetup"];
}
