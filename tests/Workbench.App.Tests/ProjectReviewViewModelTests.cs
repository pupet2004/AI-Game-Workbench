using Workbench.App.ProjectWorld;
using Workbench.App.Tests.Support;
using Workbench.Core.Continuity;
using Workbench.Project.Opening;

namespace Workbench.App.Tests;

public sealed class ProjectReviewViewModelTests
{
    [Fact]
    public async Task Review_queue_loads_pending_handoffs_and_opens_the_selected_decision()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var folder = new TemporaryDirectory("review-queue");
        var result = await context.Services.ProjectOpenService.OpenAsync(folder.Path);
        var principal = context.Services.UserPrincipalProvider.GetCurrent();
        var projectRef = new ProjectRef(result.Project.Id);

        await context.Services.B1ProjectGovernance.CreateGovernedProjectForExistingProjectAsync(projectRef, principal);
        var initialization = await context.Services.ProjectWorldInitialization.CommitAsync(
            new ProjectWorldInitializationRequest(
                projectRef,
                principal,
                RoleKind.Worker,
                "Own the first playable slice",
                "A clear first result",
                "Implement the first bounded change"));
        var assignment = initialization.AssignmentDelegationEffect!.Assignment.AssignmentRef;
        var revision = initialization.AssignmentDelegationEffect.InitialRevision.RevisionRef;
        var attempt = await context.Services.B1NonAuthoritativeCommands.CreateAttemptAndSelectAsync(
            new CreateAttemptCommand(projectRef, principal, new AttemptRef(Guid.NewGuid()), assignment, revision,
                context.Time.GetUtcNow()),
            null);
        var handoff = await context.Services.GuidedHandoffComposer.RecordAsync(
            attempt.AttemptRef,
            assignment,
            new GuidedHandoffRequest(
                projectRef,
                principal,
                "First slice completed",
                [],
                [],
                ["Keep the first slice playable"],
                null,
                []));

        HandoffRef? opened = null;
        var review = new ProjectReviewViewModel(
            context.Services,
            result,
            () => Task.CompletedTask,
            handoffRef =>
            {
                opened = handoffRef;
                return Task.CompletedTask;
            });

        await review.InitializeAsync();

        var pending = Assert.Single(review.PendingHandoffs);
        Assert.Equal(handoff.HandoffRef, pending.HandoffRef);
        Assert.Equal(1, review.PendingHandoffCount);
        Assert.Contains("1", review.PendingHandoffSummary, StringComparison.Ordinal);
        Assert.Contains("change", review.PendingHandoffSummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("handoff", review.PendingHandoffSummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Authority", pending.Status, StringComparison.Ordinal);

        await review.ReviewHandoffCommand.ExecuteAsync(pending.HandoffRef);

        Assert.Equal(handoff.HandoffRef, opened);
    }
}
