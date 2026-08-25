using Workbench.App.ProjectWorld;
using Workbench.App.Continuity;
using Workbench.App.Memory;
using Workbench.App.Services;
using Workbench.App.Tests.Support;
using Workbench.Core.Continuity;
using Workbench.Project.Opening;
using Workbench.Storage.Memory;

namespace Workbench.App.Tests;

public sealed class ManualProjectSliceCertificationTests
{
    [Fact]
    public async Task New_project_manual_journey_survives_close_reopen_with_explicit_library_projection()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var folder = new TemporaryDirectory("manual-slice-certification");
        var opened = await context.Services.ProjectOpenService.OpenAsync(folder.Path);
        var projectRef = new ProjectRef(opened.Project.Id);
        var principal = context.Services.UserPrincipalProvider.GetCurrent();

        Assert.Equal(ProjectWorldEntryKind.UnmanagedProjectUnavailable,
            (await context.Services.ProjectWorldEntryStatus.GetStatusAsync(opened.Project)).Kind);

        await context.Services.B1ProjectGovernance.CreateGovernedProjectForExistingProjectAsync(projectRef, principal);
        var initialization = new ProjectWorldInitializationRequest(
            projectRef,
            principal,
            RoleKind.Worker,
            "Own the first playable combat slice",
            "A clear, testable combat direction",
            "Define the first combat prototype");
        var preview = await context.Services.ProjectWorldInitialization.PreviewAsync(initialization);
        Assert.Contains("one LogicalActor", preview.EffectsSummary);
        Assert.Equal(0L, await CountAsync(context.Services, "b1_authority_decisions", opened.Project.Id));

        await context.Services.ProjectWorldInitialization.CommitAsync(initialization);
        var explorer = new ProjectWorldExplorerViewModel(context.Services, opened, () => Task.CompletedTask);
        await explorer.InitializeAsync();
        var active = Assert.Single(explorer.ActiveWork);
        Assert.False(explorer.HasAcceptedState);

        var manual = new ManualWorkViewModel(context.Services, opened, active.AssignmentRef, () => Task.CompletedTask);
        await manual.InitializeAsync();
        await manual.BeginOrContinueCommand.ExecuteAsync(null);
        Assert.True(manual.HasAttempt);
        Assert.Equal(0L, await CountAsync(context.Services, "b1_session_bindings", opened.Project.Id));
        Assert.Empty(context.Services.RuntimeRegistry.Runtimes);

        manual.PrimaryResult = "Combat prototype implemented";
        manual.ProposedChangesText = "Use card-based combat for the first prototype";
        manual.ShowHandoffComposerCommand.Execute(null);
        await manual.RecordHandoffCommand.ExecuteAsync(null);

        var afterHandoff = await context.Services.B1AuthorityRepository.LoadProjectStateAsync(projectRef);
        Assert.Single(afterHandoff.Handoffs);
        Assert.Empty(B1Projector.Build(afterHandoff).AcceptedProjectState.CurrentContributions);
        // Primary Result and Proposed Project Changes are two distinct typed Claims.
        Assert.Equal(2L, await CountAsync(context.Services, "b1_claims", opened.Project.Id));
        Assert.Equal(1L, await CountAsync(context.Services, "b1_handoffs", opened.Project.Id));

        var handoff = Assert.Single(afterHandoff.Handoffs);
        var decision = await context.Services.GuidedDecision.CommitAsync(new GuidedDecisionRequest(
            projectRef,
            principal,
            handoff.HandoffRef,
            AssignmentDisposition.Accepted,
            ContributionDecisionMode.AdoptVerbatim,
            null,
            null));
        var acceptedBeforeRestart = await context.Services.B1Projections.GetAcceptedProjectStateAsync(projectRef);
        var acceptedContribution = Assert.Single(acceptedBeforeRestart.CurrentContributions);
        Assert.Equal(AssignmentDisposition.Accepted, decision.AssignmentDispositionEffect!.Disposition);
        Assert.Equal("Use card-based combat for the first prototype", acceptedContribution.Statement);

        var explorerAfterDecision = new ProjectWorldExplorerViewModel(
            context.Services,
            opened,
            () => Task.CompletedTask);
        await explorerAfterDecision.InitializeAsync();
        explorerAfterDecision.LibraryCategory = "Design";
        explorerAfterDecision.LibraryTopic = "Combat";
        explorerAfterDecision.LibraryNodeContent = "Combat prototype direction established.";
        explorerAfterDecision.ShowLibraryComposerCommand.Execute(null);
        await explorerAfterDecision.ProjectToLibraryCommand.ExecuteAsync(null);
        Assert.Contains("Library updated", explorerAfterDecision.LibraryStatusMessage);

        var restarted = AppServices.CreateForDatabasePath(context.Services.Database.DatabasePath, context.Time);
        await restarted.InitializeAsync();
        try
        {
            var acceptedAfterRestart = await restarted.B1Projections.GetAcceptedProjectStateAsync(projectRef);
            Assert.Equal(acceptedBeforeRestart.CurrentContributions, acceptedAfterRestart.CurrentContributions);
            var projection = await restarted.LibraryAcceptedStateReader.ReadAsync(projectRef);
            var contributionProjection = Assert.Single(projection.CurrentContributions);
            var libraryProjection = Assert.Single(contributionProjection.LibraryProjections);
            Assert.Equal("Design", libraryProjection.Object.Category);
            Assert.Equal("Combat", libraryProjection.Object.Topic);
            Assert.Equal("Combat prototype direction established.", libraryProjection.Node.Content);
            Assert.Equal(decision.DecisionRef, libraryProjection.AuthorityDecisionRef);
            Assert.Equal(acceptedContribution.ContributionRef, libraryProjection.ContributionRef);

            var reopened = await restarted.ProjectOpenService.OpenAsync(folder.Path);
            var reopenedManual = new ManualWorkViewModel(
                restarted,
                reopened,
                active.AssignmentRef,
                () => Task.CompletedTask);
            await reopenedManual.InitializeAsync();
            // Accepted disposition makes the prior revision no longer fulfillable, so
            // the stored routing history remains but no longer presents as effective work.
            Assert.False(reopenedManual.HasAttempt);
            Assert.False(reopenedManual.HasHandoff);
            Assert.Equal(1L, await CountAsync(restarted, "b1_attempts", opened.Project.Id));
            Assert.Equal(0L, await CountAsync(restarted, "b1_session_bindings", opened.Project.Id));
            Assert.Empty(restarted.RuntimeRegistry.Runtimes);
        }
        finally
        {
            await restarted.DisposeAsync();
        }
    }

    private static async Task<long> CountAsync(AppServices services, string table, Guid projectId)
    {
        await using var connection = services.Database.CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table} WHERE project_id=$project;";
        command.Parameters.AddWithValue("$project", projectId.ToString());
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
