using System.Text.Json;
using Workbench.Core.Tasks;
using Workbench.App.ProjectWorld;
using Workbench.App.Tests.Support;
using Workbench.App.Worker;
using Workbench.Storage.Workers;
using Workbench.Storage.Memory;
using Workbench.Project.Opening;

namespace Workbench.App.Tests;

public sealed class ProjectEvolutionIndexQueryTests
{
    [Fact]
    public async Task Lists_deterministic_library_artifact_and_legacy_worker_records_without_merging_identity()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var folder = new TemporaryDirectory("evolution-project");
        var opened = await context.Services.ProjectOpenService.OpenAsync(folder.Path);
        var now = context.Time.GetUtcNow();

        var libraryObject = await context.Services.ProjectLibraryEvolutionRepository.CreateObjectAsync(
            opened.Project.Id, "Chapter Delivery", "Chapter 1", now);
        var node = await context.Services.ProjectLibraryEvolutionRepository.AddNodeAsync(
            opened.Project.Id, libraryObject.Id, new DateOnly(2026, 9, 6),
            "时间礼仪课 remains a Library recommendation.",
            [new LibraryMaterialReferenceDraft("Artifact", "chapter-01.md", "Chapter 1 manuscript")], now);

        var taskId = Guid.NewGuid();
        var profile = Workbench.Core.Tasks.ExecutionProfile.Create("provider", "account", "model", "runtime");
        var revision = new Workbench.Core.Tasks.TaskRevision(taskId, 1, "Chapter 1", "scope", "out", ["accept"], Workbench.Core.Tasks.TaskRiskLevel.Low, profile, "initial", Workbench.Core.Tasks.TaskRevisionApprover.User, now, null);
        await context.Services.TaskRepository.CreateAsync(opened.Project.Id, new Workbench.Core.Tasks.TaskDraft(taskId, "Chapter 1", "Chapter 1", "scope", "out", ["accept"], Workbench.Core.Tasks.TaskRiskLevel.Low, profile, now, revision));
        var reportId = Guid.NewGuid();
        await new TaskEventRepository(context.Services.Database).AppendAsync(new StoredTaskEvent(
            reportId, opened.Project.Id, taskId, Guid.NewGuid(), "WorkerFinalReportReceived",
            JsonSerializer.Serialize(new { Message = "Chapter 1 completed" }), now));

        var entries = await context.Services.ProjectEvolutionIndex.ListAsync(opened.Project.Id);

        Assert.Contains(entries, item => item.Category == ProjectEvolutionCategory.Library &&
            item.ObjectRef == $"library-object:{libraryObject.Id}" &&
            item.Summary.Contains("时间礼仪课", StringComparison.Ordinal));
        Assert.Contains(entries, item => item.Category == ProjectEvolutionCategory.Artifact &&
            item.ObjectRef == "artifact:chapter-01.md" &&
            item.Summary.Contains("chapter-01.md", StringComparison.Ordinal));
        Assert.Contains(entries, item => item.Category == ProjectEvolutionCategory.Worker &&
            item.SourceRefs.Contains($"TaskEvent:{reportId}") &&
            item.Summary.Contains("Chapter 1 completed", StringComparison.Ordinal));
        Assert.DoesNotContain(entries, item => item.Category == ProjectEvolutionCategory.Authority);
        Assert.DoesNotContain(entries, item => item.AuthorityStatus == "AcceptedProjectState" &&
            item.Category == ProjectEvolutionCategory.Library);
        Assert.Contains(entries, item => item.SourceRefs.Contains($"LibraryTimelineNode:{node.Id}"));
    }

    [Fact]
    public async Task Keeps_results_bounded_and_query_read_only()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var folder = new TemporaryDirectory("evolution-bounded-project");
        var opened = await context.Services.ProjectOpenService.OpenAsync(folder.Path);
        var now = context.Time.GetUtcNow();
        var libraryObject = await context.Services.ProjectLibraryEvolutionRepository.CreateObjectAsync(opened.Project.Id, "Design", "Bounded", now);
        for (var index = 0; index < 205; index++)
            await context.Services.ProjectLibraryEvolutionRepository.AddNodeAsync(opened.Project.Id, libraryObject.Id, new DateOnly(2026, 9, 6), $"Node {index}", [], now.AddSeconds(index));

        var before = await context.Services.ProjectLibraryEvolutionRepository.GetTimelineAsync(opened.Project.Id, libraryObject.Id);
        var entries = await context.Services.ProjectEvolutionIndex.ListAsync(opened.Project.Id);
        var after = await context.Services.ProjectLibraryEvolutionRepository.GetTimelineAsync(opened.Project.Id, libraryObject.Id);

        Assert.True(entries.Count <= 200);
        Assert.Equal(before.Count, after.Count);
    }

    [Fact]
    public async Task Projects_authority_decisions_as_authority_records_only()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var folder = new TemporaryDirectory("evolution-authority-project");
        var opened = await context.Services.ProjectOpenService.OpenAsync(folder.Path);
        var principal = new Workbench.Core.Continuity.UserPrincipalRef("user:evolution-test");
        await context.Services.B1ProjectGovernance.CreateGovernedProjectForExistingProjectAsync(
            new Workbench.Core.Continuity.ProjectRef(opened.Project.Id), principal);
        await context.Services.ProjectWorldInitialization.CommitAsync(new Workbench.App.ProjectWorld.ProjectWorldInitializationRequest(
            new Workbench.Core.Continuity.ProjectRef(opened.Project.Id), principal,
            Workbench.Core.Continuity.RoleKind.Worker, "Own the work", "Keep the project coherent", "Deliver chapter"));

        var entries = await context.Services.ProjectEvolutionIndex.ListAsync(opened.Project.Id);

        Assert.Contains(entries, item => item.Category == ProjectEvolutionCategory.Authority &&
            item.AuthorityStatus == "AcceptedProjectState" &&
            item.ChangeType == "AuthorityDecisionRecorded");
        Assert.DoesNotContain(entries, item => item.Category == ProjectEvolutionCategory.Library &&
            item.AuthorityStatus == "AcceptedProjectState");
    }
}
