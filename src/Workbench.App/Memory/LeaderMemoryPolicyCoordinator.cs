using Workbench.Runtime.Agents;
using Workbench.Runtime.Registry;
using Workbench.Storage.Leaders;
using Workbench.Storage.Memory;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.Memory;

public sealed class LeaderMemoryPolicyCoordinator
{
    public const string LegacyMemoryMode = "FROZEN_READ_ONLY";

    public LeaderMemoryPolicyCoordinator(
        AgentRuntimeRegistry runtimeRegistry,
        IProjectMemoryApi memoryApi,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(runtimeRegistry);
        ArgumentNullException.ThrowIfNull(memoryApi);
        ArgumentNullException.ThrowIfNull(timeProvider);
    }

    public Task<LeaderMemoryPolicyPreparation> PrepareForNewBrainAsync(
        CoreProject project,
        AgentSession sourceSession,
        StoredLeaderSessionEpoch sourceEpoch,
        bool resumeSourceSession,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new LeaderMemoryPolicyPreparation(
            false,
            null,
            "Legacy Memory synthesis is frozen read-only."));
    }
}
