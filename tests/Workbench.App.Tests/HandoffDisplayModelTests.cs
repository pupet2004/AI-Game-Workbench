using Workbench.App.ProjectWorld;
using Workbench.Core.Continuity;
using Workbench.Storage.Workers;

namespace Workbench.App.Tests;

public sealed class HandoffDisplayModelTests
{
    [Fact]
    public void B1_handoff_display_preserves_non_authority_and_provenance()
    {
        var project = new ProjectRef(Guid.NewGuid());
        var claimRef = new ClaimRef(Guid.NewGuid());
        var handoffRef = new HandoffRef(Guid.NewGuid());
        var attemptRef = new AttemptRef(Guid.NewGuid());
        var claim = new Claim(claimRef, project, new ClaimantRef.UserPrincipal(new UserPrincipalRef("operator")), null, new ClaimPayload.Result("Chapter completed"), [], DateTimeOffset.UtcNow);
        var handoff = new Handoff(handoffRef, attemptRef, claimRef, [], [], [], [], [], DateTimeOffset.UtcNow);
        var governance = new ProjectGovernance(project, new UserPrincipalRef("operator"), B1GovernanceOrigin.Created, null, 0);
        var state = new B1ProjectState(governance, [], [], [], [], [], [], [], [claim], [handoff], [], [], []);

        var model = HandoffDisplayModelFactory.FromB1(state, handoff, "Attempt / SessionBinding", ["chapter-01.md"], ["chapter-01.md"]);

        Assert.Equal(HandoffDisplaySourceKind.B1Handoff, model.SourceKind);
        Assert.Equal("Chapter completed", model.Result);
        Assert.Contains("not Accepted", model.AuthorityStatus);
        Assert.Equal("Attempt / SessionBinding", model.Provenance);
    }

    [Fact]
    public void Legacy_completion_display_keeps_legacy_identity()
    {
        var completion = new StoredCompletionPackage(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Chapter completed", "{\"schema_version\":1}", DateTimeOffset.UtcNow);
        var model = HandoffDisplayModelFactory.FromLegacyCompletion(completion, "Task / Execution / Session", ["chapter-01.md"]);

        Assert.Equal(HandoffDisplaySourceKind.LegacyWorkerCompletion, model.SourceKind);
        Assert.Contains("Legacy", model.AuthorityStatus);
        Assert.Equal(completion.PackageId.ToString(), model.SourceReference);
    }
}
