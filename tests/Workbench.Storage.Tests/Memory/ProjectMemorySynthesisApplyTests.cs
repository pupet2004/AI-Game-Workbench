using Microsoft.Data.Sqlite;
using Workbench.Storage.Leaders;
using Workbench.Storage.Memory;

namespace Workbench.Storage.Tests.Memory;

public sealed class ProjectMemorySynthesisApplyTests
{
    [Fact]
    public async Task Atomic_apply_creates_learned_and_pending_candidate_with_epoch_and_message_sources()
    {
        await using var context = await JobContext.CreateAsync();
        var epoch = await context.CreateEpochAsync(archived: true);
        var messages = new LeaderMessageRepository(context.Database);
        var source = await messages.AppendAsync(epoch.Id, "user", "Use Workbench ownership.", epoch.StartedAt);
        await context.Jobs.QueueSynthesisForEpochAsync(epoch.Id);
        await context.Jobs.ClaimNextPendingAsync(context.Project.Id);
        var memories = new ProjectMemoryRepository(context.Database);

        await memories.ApplySynthesisAsync(new ProjectMemorySynthesisApplication(
            epoch.Id,
            context.Project.Id,
            [new ProjectMemorySynthesisItem("Current Development Phase", "Building memory intelligence.", [source.Id])],
            [new ProjectMemorySynthesisItem("Memory Ownership", "Project memory belongs to the Workbench.", [source.Id])],
            DateTimeOffset.Parse("2026-08-13T00:10:00+00:00")));

        var learned = Assert.Single(await memories.GetAsync(context.Project.Id, "Learned", "Active"));
        var candidate = Assert.Single(await memories.GetAsync(context.Project.Id, "Candidate", "Active"));
        Assert.Null(learned.CertifiedAt);
        Assert.Null(candidate.CertifiedAt);
        Assert.Equal(ProjectMemorySynthesisJobStatus.Completed, (await context.Jobs.GetAsync(epoch.Id))!.Status);
        var sources = await memories.GetSourcesAsync(candidate.Id);
        Assert.Contains(new ProjectMemorySource("LeaderEpoch", epoch.Id.ToString()), sources);
        Assert.Contains(new ProjectMemorySource("LeaderMessage", source.Id.ToString()), sources);
    }

    [Fact]
    public async Task Same_topic_new_learned_supersedes_old_and_preserves_history()
    {
        await using var context = await JobContext.CreateAsync();
        var memories = new ProjectMemoryRepository(context.Database);
        var first = await PrepareRunningAsync(context);
        await memories.ApplySynthesisAsync(Application(first.Id, context.Project.Id,
            learned: [new("Phase", "Foundation", [])]));
        var second = await PrepareRunningAsync(context);

        await memories.ApplySynthesisAsync(Application(second.Id, context.Project.Id,
            learned: [new(" phase ", "Intelligence", [])]));

        Assert.Equal("Intelligence", Assert.Single(await memories.GetAsync(context.Project.Id, "Learned", "Active")).Content);
        Assert.Equal("Foundation", Assert.Single(await memories.GetAsync(context.Project.Id, "Learned", "Superseded")).Content);
        Assert.Empty(await memories.GetAsync(context.Project.Id, "Formal", "Active"));
    }

    [Fact]
    public async Task Retry_is_idempotent_and_formal_equivalent_blocks_candidate()
    {
        await using var context = await JobContext.CreateAsync();
        var epoch = await PrepareRunningAsync(context);
        var memories = new ProjectMemoryRepository(context.Database);
        var application = Application(epoch.Id, context.Project.Id,
            candidates: [new(" Memory Ownership ", "Workbench owns memory.", [])]);
        await memories.ApplySynthesisAsync(application);
        await SetJobPendingAsync(context, epoch.Id);
        await context.Jobs.ClaimNextPendingAsync(context.Project.Id);

        await memories.ApplySynthesisAsync(application);

        Assert.Single(await memories.GetAsync(context.Project.Id, "Candidate", "Active"));
        var service = new ProjectMemoryService(new ProjectActivityRepository(context.Database), memories);
        var candidate = Assert.Single(await service.GetPendingCandidatesAsync(context.Project.Id));
        await service.AcceptCandidateAsync(candidate.Id);
        var later = await PrepareRunningAsync(context);
        await memories.ApplySynthesisAsync(Application(later.Id, context.Project.Id,
            candidates: [new("memory ownership", " Workbench   owns memory. ", [])]));
        Assert.Empty(await service.GetPendingCandidatesAsync(context.Project.Id));
        Assert.Single(await service.GetFormalMemoriesAsync(context.Project.Id));
    }

    [Fact]
    public async Task Storage_apply_failure_rolls_back_all_memory_and_does_not_complete_job()
    {
        await using var context = await JobContext.CreateAsync();
        var epoch = await PrepareRunningAsync(context);
        await using (var connection = context.Database.CreateConnection())
        {
            await connection.OpenAsync();
            var trigger = connection.CreateCommand();
            trigger.CommandText = """
                CREATE TRIGGER fail_synthesized_candidate
                BEFORE INSERT ON project_memory_items
                WHEN NEW.layer = 'Candidate'
                BEGIN SELECT RAISE(ABORT, 'forced synthesis apply failure'); END;
                """;
            await trigger.ExecuteNonQueryAsync();
        }

        var memories = new ProjectMemoryRepository(context.Database);
        await Assert.ThrowsAnyAsync<SqliteException>(() => memories.ApplySynthesisAsync(
            Application(epoch.Id, context.Project.Id,
                learned: [new("Phase", "Should roll back", [])],
                candidates: [new("Rule", "Should fail", [])])));

        Assert.Empty(await memories.GetAsync(context.Project.Id, "Learned", "Active"));
        Assert.Empty(await memories.GetAsync(context.Project.Id, "Candidate", "Active"));
        Assert.Equal(ProjectMemorySynthesisJobStatus.Running, (await context.Jobs.GetAsync(epoch.Id))!.Status);
    }

    [Fact]
    public async Task Apply_rejects_cross_project_or_unknown_message_sources_without_partial_writes()
    {
        await using var context = await JobContext.CreateAsync();
        var epoch = await PrepareRunningAsync(context);
        var memories = new ProjectMemoryRepository(context.Database);

        await Assert.ThrowsAsync<InvalidOperationException>(() => memories.ApplySynthesisAsync(
            Application(epoch.Id, Guid.NewGuid(), learned: [new("Phase", "Wrong project", [])])));
        await Assert.ThrowsAsync<InvalidOperationException>(() => memories.ApplySynthesisAsync(
            Application(epoch.Id, context.Project.Id, candidates: [new("Rule", "Bad source", [long.MaxValue])])));

        Assert.Empty(await memories.GetAsync(context.Project.Id, "Learned", "Active"));
        Assert.Empty(await memories.GetAsync(context.Project.Id, "Candidate", "Active"));
    }

    private static ProjectMemorySynthesisApplication Application(
        Guid epochId,
        Guid projectId,
        IReadOnlyList<ProjectMemorySynthesisItem>? learned = null,
        IReadOnlyList<ProjectMemorySynthesisItem>? candidates = null) =>
        new(epochId, projectId, learned ?? [], candidates ?? [], DateTimeOffset.Parse("2026-08-13T00:10:00+00:00"));

    private static async Task<StoredLeaderSessionEpoch> PrepareRunningAsync(JobContext context)
    {
        var epoch = await context.CreateEpochAsync(archived: true);
        await context.Jobs.QueueSynthesisForEpochAsync(epoch.Id);
        Assert.NotNull(await context.Jobs.ClaimNextPendingAsync(context.Project.Id));
        return epoch;
    }

    private static async Task SetJobPendingAsync(JobContext context, Guid epochId)
    {
        await using var connection = context.Database.CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "UPDATE project_memory_synthesis_jobs SET status = 'Pending', completed_at = NULL WHERE epoch_id = $epochId;";
        command.Parameters.AddWithValue("$epochId", epochId.ToString());
        await command.ExecuteNonQueryAsync();
    }
}
