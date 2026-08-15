using Microsoft.Data.Sqlite;
using Workbench.Core.Projects;
using Workbench.Storage.Database;
using Workbench.Storage.Leaders;
using Workbench.Storage.Memory;
using Workbench.Storage.Projects;
using Workbench.Storage.Tests.Database;

namespace Workbench.Storage.Tests.Memory;

public sealed class ProjectMemorySynthesisJobTests
{
    [Fact]
    public async Task Queue_old_archived_epoch_is_supported_and_duplicate_queue_is_idempotent()
    {
        await using var context = await JobContext.CreateAsync();
        var archived = await context.CreateEpochAsync(archived: true);

        await context.Jobs.QueueSynthesisForEpochAsync(archived.Id);
        await context.Jobs.QueueSynthesisForEpochAsync(archived.Id);

        var job = await context.Jobs.GetAsync(archived.Id);
        Assert.NotNull(job);
        Assert.Equal(ProjectMemorySynthesisJobStatus.Pending, job.Status);
        Assert.Equal(0, job.AttemptCount);
        Assert.Equal(context.Project.Id, job.ProjectId);
        Assert.Equal(1, await context.CountJobsAsync());
    }

    [Fact]
    public async Task Current_epoch_does_not_have_synthesis_job()
    {
        await using var context = await JobContext.CreateAsync();
        var current = await context.CreateEpochAsync(archived: false);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.Jobs.QueueSynthesisForEpochAsync(current.Id));

        Assert.Null(await context.Jobs.GetAsync(current.Id));
    }

    [Fact]
    public async Task Pending_job_round_trips_through_running_failure_and_restart_freeze()
    {
        await using var context = await JobContext.CreateAsync();
        var archived = await context.CreateEpochAsync(archived: true);
        await context.Jobs.QueueSynthesisForEpochAsync(archived.Id);

        var running = await context.Jobs.ClaimNextPendingAsync(context.Project.Id);
        Assert.Equal(ProjectMemorySynthesisJobStatus.Running, running!.Status);
        Assert.Equal(1, running.AttemptCount);
        Assert.NotNull(running.LastAttemptedAt);

        await context.Jobs.ReturnToPendingAsync(archived.Id, "provider unavailable");
        var pending = await context.Jobs.GetAsync(archived.Id);
        Assert.Equal(ProjectMemorySynthesisJobStatus.Pending, pending!.Status);
        Assert.Equal("provider unavailable", pending.LastError);

        Assert.NotNull(await context.Jobs.ClaimNextPendingAsync(context.Project.Id));
        await context.Jobs.RecoverRunningAsync();
        var recovered = await context.Jobs.GetAsync(archived.Id);
        Assert.Equal(ProjectMemorySynthesisJobStatus.Running, recovered!.Status);
        Assert.Equal(2, recovered.AttemptCount);
    }

    [Fact]
    public async Task Claim_is_project_scoped_and_attempts_only_one_job()
    {
        await using var context = await JobContext.CreateAsync();
        var first = await context.CreateEpochAsync(archived: true);
        var second = await context.CreateEpochAsync(archived: true);
        var otherProject = await context.CreateOtherProjectEpochAsync();
        await context.Jobs.QueueSynthesisForEpochAsync(first.Id);
        await context.Jobs.QueueSynthesisForEpochAsync(second.Id);
        await context.Jobs.QueueSynthesisForEpochAsync(otherProject.Id);

        var claimed = await context.Jobs.ClaimNextPendingAsync(context.Project.Id);

        Assert.NotNull(claimed);
        Assert.Equal(context.Project.Id, claimed.ProjectId);
        Assert.Equal(1, (await context.Jobs.GetAsync(first.Id))!.AttemptCount + (await context.Jobs.GetAsync(second.Id))!.AttemptCount);
        Assert.Equal(ProjectMemorySynthesisJobStatus.Pending, (await context.Jobs.GetAsync(otherProject.Id))!.Status);
    }
}

internal sealed class JobContext : IAsyncDisposable
{
    private readonly TemporaryDatabase _temporary;

    private JobContext(TemporaryDatabase temporary, WorkbenchDatabase database, Project project)
    {
        _temporary = temporary;
        Database = database;
        Project = project;
        Jobs = new ProjectMemorySynthesisRepository(database, new FixedTimeProvider());
    }

    public WorkbenchDatabase Database { get; }
    public Project Project { get; }
    public ProjectMemorySynthesisRepository Jobs { get; }

    public static async Task<JobContext> CreateAsync()
    {
        var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();
        var project = CreateProject("A");
        await new ProjectRepository(database).UpsertAsync(project);
        return new JobContext(temporary, database, project);
    }

    public async Task<StoredLeaderSessionEpoch> CreateEpochAsync(bool archived)
    {
        var epoch = NewEpoch(Project.Id, archived);
        await InsertEpochAsync(epoch);
        return epoch;
    }

    public async Task<StoredLeaderSessionEpoch> CreateOtherProjectEpochAsync()
    {
        var project = CreateProject("B");
        await new ProjectRepository(Database).UpsertAsync(project);
        var epoch = NewEpoch(project.Id, archived: true);
        await InsertEpochAsync(epoch);
        return epoch;
    }

    public async Task<int> CountJobsAsync()
    {
        await using var connection = Database.CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM project_memory_synthesis_jobs;";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task InsertEpochAsync(StoredLeaderSessionEpoch epoch)
    {
        await using var connection = Database.CreateConnection();
        await connection.OpenAsync();
        var leader = connection.CreateCommand();
        leader.CommandText = "INSERT OR IGNORE INTO project_leaders(project_id,current_epoch_id,created_at,updated_at) VALUES($project,NULL,$now,$now);";
        leader.Parameters.AddWithValue("$project", epoch.ProjectId.ToString());
        leader.Parameters.AddWithValue("$now", epoch.StartedAt.ToString("O"));
        await leader.ExecuteNonQueryAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO leader_session_epochs(id,project_id,provider_id,provider_account_id,model_id,agent_session_id,external_session_id,working_directory,started_at,last_active_at,ended_at,rollover_reason,handoff_summary)
            VALUES($id,$project,'provider',$account,'model',$session,'external','C:/Project',$started,$started,$ended,$reason,$handoff);
            """;
        command.Parameters.AddWithValue("$id", epoch.Id.ToString());
        command.Parameters.AddWithValue("$project", epoch.ProjectId.ToString());
        command.Parameters.AddWithValue("$account", epoch.ProviderAccountId.ToString());
        command.Parameters.AddWithValue("$session", epoch.AgentSessionId.ToString());
        command.Parameters.AddWithValue("$started", epoch.StartedAt.ToString("O"));
        command.Parameters.AddWithValue("$ended", epoch.EndedAt is null ? DBNull.Value : epoch.EndedAt.Value.ToString("O"));
        command.Parameters.AddWithValue("$reason", epoch.EndedAt is null ? DBNull.Value : "Manual");
        command.Parameters.AddWithValue("$handoff", epoch.EndedAt is null ? DBNull.Value : "handoff");
        await command.ExecuteNonQueryAsync();
    }

    private static StoredLeaderSessionEpoch NewEpoch(Guid projectId, bool archived)
    {
        var now = DateTimeOffset.Parse("2026-08-12T12:00:00+00:00");
        return new(Guid.NewGuid(), projectId, "provider", Guid.NewGuid(), "model", Guid.NewGuid(), "external", "C:/Project", now, now, archived ? now.AddHours(1) : null, archived ? "Manual" : null, archived ? "handoff" : null);
    }

    private static Project CreateProject(string name)
    {
        var now = DateTimeOffset.Parse("2026-08-12T12:00:00+00:00");
        return new(Guid.NewGuid(), name, $"C:/{name}/{Guid.NewGuid():N}", ProjectType.Generic, null, now, now);
    }

    public ValueTask DisposeAsync() => _temporary.DisposeAsync();

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.Parse("2026-08-13T00:00:00+00:00");
    }
}
