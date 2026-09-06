using CommunityToolkit.Mvvm.ComponentModel;
using Workbench.App.ViewModels;

namespace Workbench.App.ProjectWorld;

public sealed partial class HandoffDisplayViewModel : ViewModelBase
{
    public HandoffDisplayViewModel(HandoffDisplayModel model)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
    }

    public HandoffDisplayModel Model { get; }
    public string SourceLabel => Model.SourceKind == HandoffDisplaySourceKind.B1Handoff
        ? "B1 Handoff"
        : "Legacy Worker Completion";
    public string AuthorityStatus => Model.AuthorityStatus;
    public string Result => Model.Result;
    public IReadOnlyList<string> ArtifactPaths => Model.ArtifactPaths;
    public IReadOnlyList<string> ChangedPaths => Model.ChangedPaths;
    public IReadOnlyList<string> UnresolvedIssues => Model.UnresolvedIssues;
    public IReadOnlyList<string> Recommendations => Model.Recommendations;
    public string Provenance => Model.Provenance;
    public string? SourceReference => Model.SourceReference;
}
