using Workbench.App.Tests.Support;
using Workbench.Core.Continuity;
using Workbench.Core.Projects;
using Workbench.Core.Tasks;
using Workbench.Runtime.Registry;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.Tests.Continuity;

public sealed class CanonicalWorkerLaunchServiceTests
{
    [Theory]
    [InlineData(AssignmentDisposition.Accepted)]
    [InlineData(AssignmentDisposition.Rejected)]
    [InlineData(AssignmentDisposition.RevisionRequired)]
    public async Task Confirmed_next_draft_delegates_once_without_accepting_its_proposed_change(AssignmentDisposition disposition)
    {
        await using var context = await AppTestContext.CreateAsync(runtimeRegistry: new AgentRuntimeRegistry());
        var now = context.Time.GetUtcNow();
        var project = new CoreProject(Guid.NewGuid(), "Next task", Path.GetTempPath(), ProjectType.Generic, null, now, now);
        var projectRef = new ProjectRef(project.Id);
        var principal = new UserPrincipalRef("U1");
        var authority = new DecidingAuthorityRef.UserPrincipal(principal);
        await context.Services.B1ProjectGovernance.CreateGovernedProjectAsync(project, principal);
        var established = await context.Services.B1AuthorityCommands.EstablishResponsibilityAsync(
            new EstablishResponsibilityCommand(projectRef, principal, authority,
                new ResponsibilityContract("Counter", "Working counter", AuthorityBoundary.Empty),
                new AssignmentDelegationInstruction(new ResponsibilityTarget.EstablishedByThisDecision(),
                    new AssignmentAssigneeTarget.EstablishedByThisDecision(), new AssignmentRevisionContract("+2"), null),
                RoleKind.Worker, [], []));
        var previous = established.AssignmentDelegationEffect!;
        await context.Services.B1AuthorityCommands.DecideAssignmentAsync(new DecideAssignmentCommand(
            projectRef, principal, authority,
            new AssignmentDispositionInstruction(previous.Assignment.AssignmentRef, previous.InitialRevision.RevisionRef, disposition),
            null, null, [], []));
        var revision = new TaskRevision(Guid.NewGuid(), 1, "Add Reset", "counter UI", "No other files", ["Reset clears the score"],
            TaskRiskLevel.Low, ExecutionProfile.Create("provider", "account", "model", "runtime"),
            "initial", TaskRevisionApprover.User, now, null);
        var before = await context.Services.B1AuthorityRepository.LoadProjectStateAsync(projectRef);

        var failure = await Assert.ThrowsAsync<B1CommandException>(() =>
            context.Services.CanonicalWorkerLaunch.PrepareAsync(project.Id, revision));
        Assert.Equal(B1FailureCode.AlreadyDispositioned, failure.Code);
        var unchanged = await context.Services.B1AuthorityRepository.LoadProjectStateAsync(projectRef);
        Assert.Equal(before.AuthorityDecisions.Count, unchanged.AuthorityDecisions.Count);
        Assert.Equal(before.Attempts.Count, unchanged.Attempts.Count);

        var first = await context.Services.ConfirmedWorkerDraftLaunch.PrepareAsync(project.Id, revision);
        var retry = await context.Services.ConfirmedWorkerDraftLaunch.PrepareAsync(project.Id, revision);

        Assert.NotNull(first);
        Assert.Equal(first, retry);
        Assert.NotEqual(previous.Assignment.AssignmentRef, first.AssignmentRef);
        var after = await context.Services.B1AuthorityRepository.LoadProjectStateAsync(projectRef);
        Assert.Equal(before.AuthorityDecisions.Count + 1, after.AuthorityDecisions.Count);
        Assert.Equal(before.Attempts.Count + 1, after.Attempts.Count);
        var decision = after.AuthorityDecisions.Last();
        Assert.Equal(authority, decision.DecidingAuthorityRef);
        Assert.Equal(previous.Assignment.AssignmentRef, decision.AssignmentDelegationEffect!.ReplacesAssignmentRef);
        Assert.Contains("Add Reset", decision.AssignmentDelegationEffect.InitialRevision.Contract.ToString());
        Assert.Empty(decision.AcceptedStateContributions);
        Assert.Equal(B1Projector.Build(before).AcceptedProjectState.CurrentContributions,
            B1Projector.Build(after).AcceptedProjectState.CurrentContributions);
        Assert.Equal(before.RevisionDispositions.Count, after.RevisionDispositions.Count);
    }

    [Fact]
    public async Task Unique_active_assignment_creates_and_then_reuses_attempt_without_authority_mutation()
    {
        await using var context = await AppTestContext.CreateAsync(runtimeRegistry: new AgentRuntimeRegistry());
        var now = context.Time.GetUtcNow();
        var project = new CoreProject(Guid.NewGuid(), "Alpha Candidate", Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), ProjectType.Generic, null, now, now);
        await context.Services.B1ProjectGovernance.CreateGovernedProjectAsync(project, new UserPrincipalRef("U1"));
        var authority = new DecidingAuthorityRef.UserPrincipal(new UserPrincipalRef("U1"));
        var establishment = await context.Services.B1AuthorityCommands.EstablishResponsibilityAsync(new EstablishResponsibilityCommand(
            new ProjectRef(project.Id), new UserPrincipalRef("U1"), authority,
            new ResponsibilityContract("Implement the candidate", "Candidate complete", AuthorityBoundary.Empty),
            new AssignmentDelegationInstruction(new ResponsibilityTarget.EstablishedByThisDecision(), new AssignmentAssigneeTarget.EstablishedByThisDecision(), new AssignmentRevisionContract("Implement the candidate"), null),
            RoleKind.Worker, [], []));

        var revision = new TaskRevision(Guid.NewGuid(), 1, "Leader task", "scope", "out", ["accept"], TaskRiskLevel.Low,
            ExecutionProfile.Create("provider", "account", "model", "runtime"), "initial", TaskRevisionApprover.User, now, null);
        var before = (await context.Services.B1AuthorityRepository.LoadProjectStateAsync(new ProjectRef(project.Id))).AuthorityDecisions.Count;
        var first = await context.Services.CanonicalWorkerLaunch.PrepareAsync(project.Id, revision);
        var afterFirst = (await context.Services.B1AuthorityRepository.LoadProjectStateAsync(new ProjectRef(project.Id))).AuthorityDecisions.Count;
        var second = await context.Services.CanonicalWorkerLaunch.PrepareAsync(project.Id, revision);

        Assert.NotNull(first);
        Assert.Equal(first, second);
        Assert.Equal(before, afterFirst);
        Assert.Equal(establishment.AssignmentDelegationEffect!.Assignment.AssignmentRef, first!.AssignmentRef);
        Assert.Equal(establishment.AssignmentDelegationEffect.InitialRevision.RevisionRef, first.AssignmentRevisionRef);
        Assert.Equal(first.AttemptRef, second!.AttemptRef);
    }

    [Fact]
    public async Task Multiple_active_assignments_remain_legacy_until_explicit_selection()
    {
        await using var context = await AppTestContext.CreateAsync(runtimeRegistry: new AgentRuntimeRegistry());
        var now = context.Time.GetUtcNow();
        var project = new CoreProject(Guid.NewGuid(), "Ambiguous Candidate", Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), ProjectType.Generic, null, now, now);
        var principal = new UserPrincipalRef("U1");
        await context.Services.B1ProjectGovernance.CreateGovernedProjectAsync(project, principal);
        var command = new EstablishResponsibilityCommand(new ProjectRef(project.Id), principal, new DecidingAuthorityRef.UserPrincipal(principal),
            new ResponsibilityContract("R1", "O1", AuthorityBoundary.Empty),
            new AssignmentDelegationInstruction(new ResponsibilityTarget.EstablishedByThisDecision(), new AssignmentAssigneeTarget.EstablishedByThisDecision(), new AssignmentRevisionContract("A"), null), RoleKind.Worker, [], []);
        await context.Services.B1AuthorityCommands.EstablishResponsibilityAsync(command);
        await context.Services.B1AuthorityCommands.EstablishResponsibilityAsync(new EstablishResponsibilityCommand(
            new ProjectRef(project.Id), principal, new DecidingAuthorityRef.UserPrincipal(principal),
            new ResponsibilityContract("R2", "O2", AuthorityBoundary.Empty),
            new AssignmentDelegationInstruction(new ResponsibilityTarget.EstablishedByThisDecision(), new AssignmentAssigneeTarget.EstablishedByThisDecision(), new AssignmentRevisionContract("B"), null),
            RoleKind.Worker, [], []));
        var revision = new TaskRevision(Guid.NewGuid(), 1, "Leader task", "scope", "out", ["accept"], TaskRiskLevel.Low,
            ExecutionProfile.Create("provider", "account", "model", "runtime"), "initial", TaskRevisionApprover.User, now, null);

        Assert.Null(await context.Services.CanonicalWorkerLaunch.PrepareAsync(project.Id, revision));
    }
}
