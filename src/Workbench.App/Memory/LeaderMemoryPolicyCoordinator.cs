using Workbench.Runtime.Agents;
using Workbench.Runtime.Registry;
using Workbench.Storage.Leaders;
using Workbench.Storage.Memory;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.Memory;

public sealed class LeaderMemoryPolicyCoordinator(
    AgentRuntimeRegistry runtimeRegistry,
    IProjectMemoryApi memoryApi,
    TimeProvider timeProvider)
{
    public async Task<LeaderMemoryPolicyPreparation> PrepareForNewBrainAsync(
        CoreProject project,
        AgentSession sourceSession,
        StoredLeaderSessionEpoch sourceEpoch,
        bool resumeSourceSession,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (sourceEpoch.ProjectId != project.Id) return new(false, null, "The source epoch is not owned by the project.");
            var runtime = runtimeRegistry.GetByAccount(sourceSession.AccountId);
            var session = resumeSourceSession
                ? await runtime.ResumeSessionAsync(sourceSession, cancellationToken)
                : sourceSession;
            var catalog = await memoryApi.ListContinuityMaterialsAsync(project.Id, sourceEpoch.Id, cancellationToken);
            var preferences = await memoryApi.GetPreferencesAsync(project.Id, cancellationToken);
            string? payload = null;
            await foreach (var item in runtime.SendAsync(session, LeaderMemoryPolicyPromptBuilder.Build(project, catalog, preferences), cancellationToken))
            {
                if (item is AgentApprovalRequested or AgentToolEvent or AgentError)
                    return new(false, null, "The Leader memory policy turn was unavailable.");
                if (item is AgentTurnCompleted completed)
                {
                    if (completed.Result.FinalStatus != AgentSessionStatus.Completed)
                        return new(false, null, "The Leader memory policy turn did not complete.");
                    payload = completed.Result.FinalText;
                }
            }

            if (!LeaderMemoryPolicyPayloadParser.TryParse(project.Id, payload, out var decision) ||
                !SelectionsAreCatalogOwned(decision.ContinuitySelection, catalog))
                return new(false, null, "The Leader memory policy response was invalid.");

            if (decision.DailySummary is not null)
                await memoryApi.UpsertDailySummaryAsync(decision.DailySummary, timeProvider.GetUtcNow(), cancellationToken);
            return new(true, decision, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new(false, null, "The Leader memory policy turn was unavailable.");
        }
    }

    private static bool SelectionsAreCatalogOwned(
        IReadOnlyList<ContinuityMaterialSelection> selections,
        ContinuityMaterialCatalog catalog)
    {
        var known = catalog.Materials.ToDictionary(item => item.Reference, StringComparer.Ordinal);
        return selections.All(selection => known.TryGetValue(selection.Reference, out var descriptor) && descriptor.Kind == selection.Kind);
    }
}
