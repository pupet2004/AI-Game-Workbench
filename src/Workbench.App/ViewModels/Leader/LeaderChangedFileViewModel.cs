namespace Workbench.App.ViewModels.Leader;

public sealed record LeaderChangedFileViewModel(string Path, int Added, int Removed, string Diff);
