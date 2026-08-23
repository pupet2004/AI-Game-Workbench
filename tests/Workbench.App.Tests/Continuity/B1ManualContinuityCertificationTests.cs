using Workbench.App.Services;
using Workbench.App.Tests.Support;
using Workbench.Core.Continuity;
using Workbench.Core.Projects;
using Workbench.Runtime.Registry;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.Tests.Continuity;

public sealed class B1ManualContinuityCertificationTests
{
    private static readonly DateTimeOffset At =
        DateTimeOffset.Parse("2026-08-22T12:00:00.0000000+00:00");

    [Fact]
    public async Task Manual_zero_session_continuity_survives_restart_without_legacy_or_runtime_side_effects()
    {
        using var directory = new TemporaryDirectory("b1-manual-continuity");
        var databasePath = Path.Combine(directory.Path, "workbench.db");
        var registry = new AgentRuntimeRegistry();
        Assert.Empty(registry.Runtimes);
        var projectRef = new ProjectRef(Guid.NewGuid());
        var operatorRef = new UserPrincipalRef("U1");
        var decidingAuthority = new DecidingAuthorityRef.UserPrincipal(operatorRef);
        var attemptRef = new AttemptRef(Guid.NewGuid());
        var handoffRef = new HandoffRef(Guid.NewGuid());

        LogicalActorRef workerRef;
        ResponsibilityRef responsibilityRef;
        AssignmentRef assignmentRef;
        RevisionRef revisionRef;
        Claim resultClaim;
        Claim validationClaim;
        Claim contributionClaim;
        AuthorityDecision acceptanceDecision;

        await using (var services = AppServices.CreateForDatabasePath(databasePath, TimeProvider.System, registry))
        {
            await services.InitializeAsync();
            await services.B1ProjectGovernance.CreateGovernedProjectAsync(
                new CoreProject(
                    projectRef.Value,
                    "P1",
                    Path.Combine(directory.Path, "P1"),
                    ProjectType.Godot,
                    null,
                    At,
                    At),
                operatorRef);

            var establishment = await services.B1AuthorityCommands.EstablishResponsibilityAsync(
                new EstablishResponsibilityCommand(
                    projectRef,
                    operatorRef,
                    decidingAuthority,
                    new ResponsibilityContract("R1", "P1 completion", AuthorityBoundary.Empty),
                    new AssignmentDelegationInstruction(
                        new ResponsibilityTarget.EstablishedByThisDecision(),
                        new AssignmentAssigneeTarget.EstablishedByThisDecision(),
                        new AssignmentRevisionContract("R1.1"),
                        null),
                    RoleKind.Worker,
                    [],
                    []));
            workerRef = establishment.LogicalActorEstablishmentEffect!.LogicalActor.LogicalActorRef;
            responsibilityRef = establishment.ResponsibilityEstablishmentEffect!.Responsibility.ResponsibilityRef;
            assignmentRef = establishment.AssignmentDelegationEffect!.Assignment.AssignmentRef;
            revisionRef = establishment.AssignmentDelegationEffect.InitialRevision.RevisionRef;

            await services.B1NonAuthoritativeCommands.CreateAttemptAsync(
                new CreateAttemptCommand(projectRef, operatorRef, attemptRef, assignmentRef, revisionRef, At));
            await services.B1NonAuthoritativeCommands.SelectCurrentAttemptAsync(
                new SelectCurrentAttemptCommand(projectRef, operatorRef, assignmentRef, null, attemptRef));

            resultClaim = await services.B1NonAuthoritativeCommands.RecordClaimAsync(
                new RecordClaimCommand(
                    projectRef,
                    operatorRef,
                    new ClaimRef(Guid.NewGuid()),
                    new ClaimantRef.LogicalActor(workerRef),
                    null,
                    new ClaimPayload.Result("C1"),
                    [new EvidenceRef("manual:C1")],
                    At));
            validationClaim = await services.B1NonAuthoritativeCommands.RecordClaimAsync(
                new RecordClaimCommand(
                    projectRef,
                    operatorRef,
                    new ClaimRef(Guid.NewGuid()),
                    new ClaimantRef.LogicalActor(workerRef),
                    null,
                    new ClaimPayload.Validation("C2"),
                    [new EvidenceRef("manual:C2")],
                    At));
            contributionClaim = await services.B1NonAuthoritativeCommands.RecordClaimAsync(
                new RecordClaimCommand(
                    projectRef,
                    operatorRef,
                    new ClaimRef(Guid.NewGuid()),
                    new ClaimantRef.LogicalActor(workerRef),
                    null,
                    new ClaimPayload.ProposedStateContribution(
                        "C3",
                        new ContributionScopeRef.Project(projectRef),
                        null),
                    [new EvidenceRef("manual:C3")],
                    At));

            var acceptedBeforeHandoff =
                (await services.B1Projections.GetProjectProjectionAsync(projectRef)).AcceptedProjectState;
            var handoff = await services.B1NonAuthoritativeCommands.CreateHandoffAsync(
                new CreateHandoffCommand(
                    projectRef,
                    operatorRef,
                    new Handoff(
                        handoffRef,
                        attemptRef,
                        resultClaim.ClaimRef,
                        [validationClaim.ClaimRef],
                        [],
                        [contributionClaim.ClaimRef],
                        [],
                        [new EvidenceRef("manual:H1")],
                        At)));
            await services.B1NonAuthoritativeCommands.SelectContinuationHandoffAsync(
                new SelectContinuationHandoffCommand(projectRef, operatorRef, attemptRef, null, handoff.HandoffRef));

            var beforeDecision = await services.B1Projections.GetProjectProjectionAsync(projectRef);
            AssertAcceptedProjectStateEqual(acceptedBeforeHandoff, beforeDecision.AcceptedProjectState);
            Assert.Empty(beforeDecision.AcceptedProjectState.CurrentContributions);
            Assert.Empty(registry.Runtimes);
            Assert.Equal(0L, await CountRowsAsync(services, "b1_session_bindings"));

            acceptanceDecision = await services.B1AuthorityCommands.DecideAssignmentAsync(
                new DecideAssignmentCommand(
                    projectRef,
                    operatorRef,
                    decidingAuthority,
                    new AssignmentDispositionInstruction(assignmentRef, revisionRef, AssignmentDisposition.Accepted),
                    null,
                    null,
                    [new ConsideredRef.Handoff(handoffRef), new ConsideredRef.Claim(resultClaim.ClaimRef)],
                    [new AcceptedContributionInstruction(
                        "C3",
                        new ContributionScopeTarget.Project(projectRef),
                        null,
                        contributionClaim.ClaimRef)]));

            Assert.Equal(new ClaimantRef.LogicalActor(workerRef), resultClaim.ClaimantRef);
            Assert.Null(resultClaim.SourceSessionBindingRef);
            Assert.Equal(decidingAuthority, acceptanceDecision.DecidingAuthorityRef);
            Assert.Equal(operatorRef, ((DecidingAuthorityRef.UserPrincipal)acceptanceDecision.DecidingAuthorityRef).UserPrincipalRef);
            var accepted = Assert.Single(acceptanceDecision.AcceptedStateContributions);
            Assert.Equal(acceptanceDecision.DecisionRef, accepted.AuthorityDecisionRef);
            Assert.Equal(contributionClaim.ClaimRef, accepted.SourceClaimRef);
            var afterDecision = await services.B1Projections.GetProjectProjectionAsync(projectRef);
            Assert.Equal(attemptRef, afterDecision.StoredAttemptSelections[assignmentRef]);
            Assert.Equal(handoffRef, afterDecision.StoredHandoffSelections[attemptRef]);
            Assert.Null(afterDecision.EffectiveCurrentAttemptRefs[assignmentRef]);
            Assert.Null(afterDecision.EffectiveCurrentHandoffRefs[attemptRef]);
            Assert.Null(afterDecision.StoredBindingSelections[attemptRef]);
            Assert.Null(afterDecision.EffectiveCurrentBindingRefs[attemptRef]);
            Assert.Equal(0L, await CountRowsAsync(services, "b1_session_bindings"));
            Assert.Empty(registry.Runtimes);
        }

        await using (var restarted = AppServices.CreateForDatabasePath(databasePath, TimeProvider.System, registry))
        {
            await restarted.InitializeAsync();
            var recovered = await restarted.B1Projections.GetProjectProjectionAsync(projectRef);
            var accepted = await restarted.B1Projections.GetAcceptedProjectStateAsync(projectRef);

            Assert.Equal(attemptRef, recovered.StoredAttemptSelections[assignmentRef]);
            Assert.Equal(handoffRef, recovered.StoredHandoffSelections[attemptRef]);
            Assert.Null(recovered.EffectiveCurrentAttemptRefs[assignmentRef]);
            Assert.Null(recovered.EffectiveCurrentHandoffRefs[attemptRef]);
            Assert.Null(recovered.StoredBindingSelections[attemptRef]);
            Assert.Null(recovered.EffectiveCurrentBindingRefs[attemptRef]);
            var recoveredContribution = Assert.Single(accepted.CurrentContributions);
            Assert.Equal(acceptanceDecision.DecisionRef, recoveredContribution.AuthorityDecisionRef);
            Assert.Equal(contributionClaim.ClaimRef, recoveredContribution.SourceClaimRef);

            var laterDelegation = await restarted.B1AuthorityCommands.DelegateAssignmentAsync(
                new DelegateAssignmentCommand(
                    projectRef,
                    operatorRef,
                    decidingAuthority,
                    new AssignmentDelegationInstruction(
                        new ResponsibilityTarget.Existing(responsibilityRef),
                        new AssignmentAssigneeTarget.Existing(workerRef),
                        new AssignmentRevisionContract("Continue R1"),
                        null),
                    null,
                    [],
                    []));

            Assert.NotEqual(acceptanceDecision.DecisionRef, laterDelegation.DecisionRef);
            Assert.Equal(acceptanceDecision.ProjectCommitSequence + 1, laterDelegation.ProjectCommitSequence);
            Assert.NotNull(laterDelegation.AssignmentDelegationEffect);
            Assert.Equal(responsibilityRef, laterDelegation.AssignmentDelegationEffect!.Assignment.ResponsibilityRef);
            Assert.Equal(workerRef, laterDelegation.AssignmentDelegationEffect.Assignment.AssigneeActorRef);
            Assert.Equal(0L, await CountRowsAsync(restarted, "b1_session_bindings"));
            Assert.Empty(registry.Runtimes);

            foreach (var table in new[]
                     {
                         "leader_session_epochs",
                         "leader_messages",
                         "project_summary_entries",
                         "project_summary_source_refs",
                         "worker_executions",
                         "task_events",
                         "task_review_decisions",
                         "worker_completion_packages"
                     })
            {
                Assert.Equal(0L, await CountRowsAsync(restarted, table));
            }
        }

        Assert.Empty(registry.Runtimes);
    }

    private static void AssertAcceptedProjectStateEqual(
        AcceptedProjectState expected,
        AcceptedProjectState actual)
    {
        Assert.Equal(expected.ProjectRef, actual.ProjectRef);
        Assert.Equal(expected.LogicalActors.OrderBy(item => item.Key.Value), actual.LogicalActors.OrderBy(item => item.Key.Value));
        Assert.Equal(expected.Responsibilities.OrderBy(item => item.Key.Value), actual.Responsibilities.OrderBy(item => item.Key.Value));
        Assert.Equal(expected.Assignments.OrderBy(item => item.Key.Value), actual.Assignments.OrderBy(item => item.Key.Value));
        Assert.Equal(expected.Revisions.OrderBy(item => item.Key.Value), actual.Revisions.OrderBy(item => item.Key.Value));
        Assert.Equal(expected.RevisionDispositions.OrderBy(item => item.Key.Value), actual.RevisionDispositions.OrderBy(item => item.Key.Value));
        Assert.Equal(expected.CurrentEffectiveRevisionRefs.OrderBy(item => item.Key.Value), actual.CurrentEffectiveRevisionRefs.OrderBy(item => item.Key.Value));
        Assert.Equal(expected.CurrentDelegationAssignments.OrderBy(item => item.Value), actual.CurrentDelegationAssignments.OrderBy(item => item.Value));
        Assert.Equal(expected.CurrentContributions, actual.CurrentContributions);
    }

    private static async Task<long> CountRowsAsync(AppServices services, string table)
    {
        await using var connection = services.Database.CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
