using CommunityToolkit.Mvvm.ComponentModel;
using Workbench.App.ViewModels;

namespace Workbench.App.ProjectWorld;

public sealed record RecoveryPresentationModel(
    IReadOnlyList<string> AcceptedConstraints,
    IReadOnlyList<string> CompletedWork,
    IReadOnlyList<string> RemainingWork,
    IReadOnlyList<string> ContextSources);

public sealed partial class RecoveryViewModel : ViewModelBase
{
    public RecoveryViewModel(RecoveryPresentationModel model)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
    }

    public RecoveryPresentationModel Model { get; }
    public string Heading => "Recovered from Project World";
    public IReadOnlyList<string> AcceptedConstraints => Model.AcceptedConstraints;
    public IReadOnlyList<string> CompletedWork => Model.CompletedWork;
    public IReadOnlyList<string> RemainingWork => Model.RemainingWork;
    public IReadOnlyList<string> ContextSources => Model.ContextSources;
}
