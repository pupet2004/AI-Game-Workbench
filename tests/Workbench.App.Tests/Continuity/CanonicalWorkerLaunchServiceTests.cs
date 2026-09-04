using Workbench.App.Tests.Support;
using Workbench.Core.Continuity;
using Workbench.Core.Projects;
using Workbench.Core.Tasks;
using Workbench.Runtime.Registry;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.Tests.Continuity;

public sealed class CanonicalWorkerLaunchServiceTests
{
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
