using Workbench.Core.Continuity;

namespace Workbench.App.ProjectWorld;

public enum ProjectWorldEntryKind
{
    ProjectWorldReady,
    ProjectWorldSetupIncomplete,
    ProjectWorldReconfirmationRequired,
    BootstrapRecoveryRequired,
    LegacySetupRequired,
    LegacyWorkspaceReady,
    UnmanagedProjectUnavailable,
    CorruptProjectUnavailable,
    PathUnavailable
}

public sealed record ProjectWorldEntryStatus(
    ProjectRef ProjectRef,
    ProjectWorldEntryKind Kind,
    bool HasLegacyContext,
    string DisplayLabel);
