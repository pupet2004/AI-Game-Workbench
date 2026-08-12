using Workbench.Core.Projects;
using Workbench.Storage.Database;
using Workbench.Storage.Leaders;
using Workbench.Storage.Projects;
using Workbench.Storage.Tests.Database;

namespace Workbench.Storage.Tests.Leaders;

public sealed class LeaderPersistenceRepositoryTests
{
    [Fact]
    public async Task Project_leader_and_current_epoch_round_trip()
    {
        await using var context = await LeaderStorageContext.CreateAsync();
        var leader = new StoredProjectLeader(context.ProjectA.Id, null, context.T0, context.T0);
        var epoch = context.CreateEpoch(context.ProjectA.Id);
        await context.Leaders.CreateCurrentEpochAsync(leader with { UpdatedAt = context.T1 }, epoch);

        var restored = await context.Leaders.GetAsync(context.ProjectA.Id);

        Assert.Equal(context.ProjectA.Id, restored!.ProjectId);
        Assert.Equal(epoch.Id, restored.CurrentEpochId);
        Assert.Equal(context.T0, restored.CreatedAt);
        Assert.Equal(context.T1, restored.UpdatedAt);
    }

    [Fact]
    public async Task Project_has_at_most_one_current_epoch_and_rejects_foreign_epoch()
    {
        await using var context = await LeaderStorageContext.CreateAsync();
        var epochA = context.CreateEpoch(context.ProjectA.Id);
        var epochB = context.CreateEpoch(context.ProjectB.Id);
        await context.CreateCurrentEpochAsync(epochA);
        await context.CreateCurrentEpochAsync(epochB);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.Leaders.SetCurrentEpochAsync(context.ProjectA.Id, epochB.Id, context.T1));
        Assert.Equal(epochA.Id, (await context.Leaders.GetAsync(context.ProjectA.Id))!.CurrentEpochId);
    }

    [Fact]
    public async Task Leader_creation_rejects_unvalidated_current_epoch_pointer()
    {
        await using var context = await LeaderStorageContext.CreateAsync();
        var leader = new StoredProjectLeader(context.ProjectA.Id, Guid.NewGuid(), context.T0, context.T0);

        await Assert.ThrowsAsync<ArgumentException>(() => context.Leaders.CreateIfMissingAsync(leader));
        Assert.Null(await context.Leaders.GetAsync(context.ProjectA.Id));
    }

    [Fact]
    public async Task Current_epoch_foreign_key_rejects_missing_epoch()
    {
        await using var context = await LeaderStorageContext.CreateAsync();
        await context.EnsureLeaderAsync(context.ProjectA.Id);
        await using var connection = context.Database.CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "UPDATE project_leaders SET current_epoch_id = $epochId WHERE project_id = $projectId;";
        command.Parameters.AddWithValue("$epochId", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$projectId", context.ProjectA.Id.ToString());

        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => command.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task Ordinary_epoch_save_rejects_a_new_active_epoch_without_current_pointer()
    {
        await using var context = await LeaderStorageContext.CreateAsync();
        await context.EnsureLeaderAsync(context.ProjectA.Id);
        var epoch = context.CreateEpoch(context.ProjectA.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.Epochs.SaveAsync(epoch));
        Assert.Null(await context.Epochs.GetAsync(epoch.Id));
        Assert.Null((await context.Leaders.GetAsync(context.ProjectA.Id))!.CurrentEpochId);
    }

    [Fact]
    public async Task Session_epoch_preserves_all_runtime_metadata_with_independent_id()
    {
        await using var context = await LeaderStorageContext.CreateAsync();
        var epoch = context.CreateEpoch(context.ProjectA.Id);

        await context.CreateCurrentEpochAsync(epoch);
        var restored = await context.Epochs.GetAsync(epoch.Id);

        Assert.Equal(epoch, restored);
        Assert.NotEqual(epoch.Id, epoch.AgentSessionId);
        Assert.NotEqual(epoch.Id.ToString(), epoch.ExternalSessionId);
    }

    [Fact]
    public async Task Archived_and_multiple_epochs_round_trip_but_only_pointer_is_current()
    {
        await using var context = await LeaderStorageContext.CreateAsync();
        await context.EnsureLeaderAsync(context.ProjectA.Id);
        var archived = context.CreateEpoch(context.ProjectA.Id) with
        {
            EndedAt = context.T1,
            RolloverReason = "manual",
            HandoffSummary = "handoff"
        };
        var current = context.CreateEpoch(context.ProjectA.Id);
        await context.Epochs.SaveAsync(archived);
        await context.CreateCurrentEpochAsync(current);

        Assert.Equal(archived, await context.Epochs.GetAsync(archived.Id));
        Assert.Equal(current, await context.Epochs.GetCurrentForProjectAsync(context.ProjectA.Id));
        Assert.Equal(2, (await context.Epochs.GetAllForProjectAsync(context.ProjectA.Id)).Count);
    }

    [Fact]
    public async Task Ordinary_epoch_save_cannot_move_or_archive_an_existing_epoch()
    {
        await using var context = await LeaderStorageContext.CreateAsync();
        await context.EnsureLeaderAsync(context.ProjectB.Id);
        var epoch = context.CreateEpoch(context.ProjectA.Id);
        await context.CreateCurrentEpochAsync(epoch);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.Epochs.SaveAsync(epoch with { ProjectId = context.ProjectB.Id }));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.Epochs.SaveAsync(epoch with { EndedAt = context.T1 }));
        Assert.Equal(epoch, await context.Epochs.GetCurrentForProjectAsync(context.ProjectA.Id));
    }

    [Fact]
    public async Task Atomic_rollover_archives_old_epoch_and_switches_to_fresh_current_epoch()
    {
        await using var context = await LeaderStorageContext.CreateAsync();
        var oldEpoch = context.CreateEpoch(context.ProjectA.Id);
        await context.CreateCurrentEpochAsync(oldEpoch);
        var newEpoch = context.CreateEpoch(context.ProjectA.Id) with
        {
            ProviderId = oldEpoch.ProviderId,
            ProviderAccountId = oldEpoch.ProviderAccountId,
            ModelId = oldEpoch.ModelId,
            WorkingDirectory = oldEpoch.WorkingDirectory,
            StartedAt = context.T1,
            LastActiveAt = context.T1
        };

        await context.Leaders.RolloverAsync(
            context.ProjectA.Id,
            oldEpoch.Id,
            newEpoch,
            context.T1,
            "WorkdayBoundary",
            "CURRENT FOCUS\nContinue M1-05B");

        var archived = await context.Epochs.GetAsync(oldEpoch.Id);
        var current = await context.Epochs.GetCurrentForProjectAsync(context.ProjectA.Id);
        Assert.Equal(context.T1, archived!.EndedAt);
        Assert.Equal("WorkdayBoundary", archived.RolloverReason);
        Assert.Equal("CURRENT FOCUS\nContinue M1-05B", archived.HandoffSummary);
        Assert.Equal(newEpoch, current);
        Assert.NotEqual(oldEpoch.Id, current!.Id);
        Assert.NotEqual(oldEpoch.AgentSessionId, current.AgentSessionId);
        Assert.NotEqual(oldEpoch.ExternalSessionId, current.ExternalSessionId);
        Assert.Equal(oldEpoch.ProviderAccountId, current.ProviderAccountId);
        Assert.Equal(oldEpoch.ModelId, current.ModelId);
        Assert.Equal(oldEpoch.WorkingDirectory, current.WorkingDirectory);
        Assert.Null(current.EndedAt);
    }

    [Fact]
    public async Task Failed_rollover_transaction_leaves_old_current_and_no_new_epoch()
    {
        await using var context = await LeaderStorageContext.CreateAsync();
        var oldEpoch = context.CreateEpoch(context.ProjectA.Id);
        await context.CreateCurrentEpochAsync(oldEpoch);
        var newEpoch = context.CreateEpoch(context.ProjectA.Id) with { StartedAt = context.T1, LastActiveAt = context.T1 };
        await using (var connection = context.Database.CreateConnection())
        {
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TRIGGER fail_rollover_pointer
                BEFORE UPDATE OF current_epoch_id ON project_leaders
                BEGIN SELECT RAISE(ABORT, 'forced rollover failure'); END;
                """;
            await command.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAnyAsync<Exception>(() => context.Leaders.RolloverAsync(
            context.ProjectA.Id, oldEpoch.Id, newEpoch, context.T1, "Manual", "handoff"));

        Assert.Equal(oldEpoch, await context.Epochs.GetAsync(oldEpoch.Id));
        Assert.Equal(oldEpoch.Id, (await context.Leaders.GetAsync(context.ProjectA.Id))!.CurrentEpochId);
        Assert.Null(await context.Epochs.GetAsync(newEpoch.Id));
    }

    [Fact]
    public async Task Most_recent_archived_epoch_is_deterministic_and_project_isolated()
    {
        await using var context = await LeaderStorageContext.CreateAsync();
        await context.EnsureLeaderAsync(context.ProjectA.Id);
        await context.EnsureLeaderAsync(context.ProjectB.Id);
        var lowerId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var higherId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var first = context.CreateEpoch(context.ProjectA.Id) with
        {
            Id = lowerId, EndedAt = context.T1, RolloverReason = "Manual", HandoffSummary = "older tie"
        };
        var deterministicWinner = context.CreateEpoch(context.ProjectA.Id) with
        {
            Id = higherId, EndedAt = context.T1, RolloverReason = "Manual", HandoffSummary = "winner"
        };
        var otherProject = context.CreateEpoch(context.ProjectB.Id) with
        {
            EndedAt = context.T1.AddDays(1), RolloverReason = "Manual", HandoffSummary = "B"
        };
        await context.Epochs.SaveAsync(first);
        await context.Epochs.SaveAsync(deterministicWinner);
        await context.Epochs.SaveAsync(otherProject);

        var restored = await context.Epochs.GetMostRecentArchivedForProjectAsync(context.ProjectA.Id);

        Assert.Equal(deterministicWinner.Id, restored!.Id);
        Assert.Equal("winner", restored.HandoffSummary);
    }

    [Fact]
    public async Task Project_A_rollover_does_not_change_Project_B_epoch()
    {
        await using var context = await LeaderStorageContext.CreateAsync();
        var oldA = context.CreateEpoch(context.ProjectA.Id);
        var currentB = context.CreateEpoch(context.ProjectB.Id);
        await context.CreateCurrentEpochAsync(oldA);
        await context.CreateCurrentEpochAsync(currentB);
        var newA = context.CreateEpoch(context.ProjectA.Id) with
        {
            ProviderId = oldA.ProviderId,
            ProviderAccountId = oldA.ProviderAccountId,
            ModelId = oldA.ModelId,
            WorkingDirectory = oldA.WorkingDirectory,
            StartedAt = context.T1,
            LastActiveAt = context.T1
        };

        await context.Leaders.RolloverAsync(
            context.ProjectA.Id, oldA.Id, newA, context.T1, "Manual", "A handoff");

        Assert.Equal(newA.Id, (await context.Leaders.GetAsync(context.ProjectA.Id))!.CurrentEpochId);
        Assert.Equal(currentB.Id, (await context.Leaders.GetAsync(context.ProjectB.Id))!.CurrentEpochId);
        Assert.Equal(currentB, await context.Epochs.GetAsync(currentB.Id));
    }

    [Fact]
    public async Task Atomic_rollover_rejects_successor_that_changes_runtime_slot_or_model()
    {
        await using var context = await LeaderStorageContext.CreateAsync();
        var oldEpoch = context.CreateEpoch(context.ProjectA.Id);
        await context.CreateCurrentEpochAsync(oldEpoch);
        var changed = context.CreateEpoch(context.ProjectA.Id) with
        {
            ProviderId = "other-provider",
            ProviderAccountId = Guid.NewGuid(),
            ModelId = "other-model",
            WorkingDirectory = "C:/Other",
            StartedAt = context.T1,
            LastActiveAt = context.T1
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.Leaders.RolloverAsync(
            context.ProjectA.Id, oldEpoch.Id, changed, context.T1, "Manual", "handoff"));

        Assert.Equal(oldEpoch, await context.Epochs.GetAsync(oldEpoch.Id));
        Assert.Equal(oldEpoch.Id, (await context.Leaders.GetAsync(context.ProjectA.Id))!.CurrentEpochId);
        Assert.Null(await context.Epochs.GetAsync(changed.Id));
    }

    [Fact]
    public async Task Messages_round_trip_ordered_with_independent_epoch_sequences_and_unicode()
    {
        await using var context = await LeaderStorageContext.CreateAsync();
        await context.EnsureLeaderAsync(context.ProjectA.Id);
        var first = context.CreateEpoch(context.ProjectA.Id);
        var second = context.CreateEpoch(context.ProjectA.Id) with { EndedAt = context.T1 };
        await context.CreateCurrentEpochAsync(first);
        await context.Epochs.SaveAsync(second);

        var a1 = await context.Messages.AppendAsync(first.Id, "user", "你好，世界", context.T0);
        var a2 = await context.Messages.AppendAsync(first.Id, "assistant", "完成", context.T1);
        var b1 = await context.Messages.AppendAsync(second.Id, "user", "new epoch", context.T1);

        Assert.Equal([1L, 2L], (await context.Messages.GetAllAsync(first.Id)).Select(message => message.Sequence));
        Assert.Equal(["你好，世界", "完成"], (await context.Messages.GetAllAsync(first.Id)).Select(message => message.Text));
        Assert.Equal(1L, b1.Sequence);
        Assert.Equal(1L, a1.Sequence);
        Assert.Equal(2L, a2.Sequence);
    }

    [Fact]
    public async Task Different_projects_do_not_share_messages_after_database_reopen()
    {
        await using var context = await LeaderStorageContext.CreateAsync();
        var epochA = context.CreateEpoch(context.ProjectA.Id);
        var epochB = context.CreateEpoch(context.ProjectB.Id);
        await context.CreateCurrentEpochAsync(epochA);
        await context.CreateCurrentEpochAsync(epochB);
        await context.Messages.AppendAsync(epochA.Id, "user", "A only", context.T0);
        await context.Messages.AppendAsync(epochB.Id, "assistant", "B only", context.T0);

        var reopened = new LeaderMessageRepository(new WorkbenchDatabase(context.DatabasePath));

        Assert.Equal("A only", Assert.Single(await reopened.GetAllAsync(epochA.Id)).Text);
        Assert.Equal("B only", Assert.Single(await reopened.GetAllAsync(epochB.Id)).Text);
    }

    [Fact]
    public async Task Unknown_role_is_rejected()
    {
        await using var context = await LeaderStorageContext.CreateAsync();
        var epoch = context.CreateEpoch(context.ProjectA.Id);
        await context.CreateCurrentEpochAsync(epoch);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            context.Messages.AppendAsync(epoch.Id, "reasoning", "hidden", context.T0));
    }

    [Fact]
    public async Task Project_delete_cascades_all_leader_data()
    {
        await using var context = await LeaderStorageContext.CreateAsync();
        var epoch = context.CreateEpoch(context.ProjectA.Id);
        await context.CreateCurrentEpochAsync(epoch);
        await context.Messages.AppendAsync(epoch.Id, "user", "gone", context.T0);

        await context.Projects.RemoveAsync(context.ProjectA.Id);

        Assert.Null(await context.Leaders.GetAsync(context.ProjectA.Id));
        Assert.Null(await context.Epochs.GetAsync(epoch.Id));
        Assert.Empty(await context.Messages.GetAllAsync(epoch.Id));
    }

    [Fact]
    public async Task Archived_history_uses_stable_keyset_cursor_and_returns_metadata_counts()
    {
        await using var context = await LeaderStorageContext.CreateAsync();
        await context.EnsureLeaderAsync(context.ProjectA.Id);
        var oldest = context.CreateEpoch(context.ProjectA.Id) with { Id = Guid.Parse("00000000-0000-0000-0000-000000000001"), EndedAt = context.T0, HandoffSummary = "old" };
        var tie = context.CreateEpoch(context.ProjectA.Id) with { Id = Guid.Parse("00000000-0000-0000-0000-000000000002"), EndedAt = context.T1, HandoffSummary = "tie" };
        var newest = context.CreateEpoch(context.ProjectA.Id) with { Id = Guid.Parse("00000000-0000-0000-0000-000000000003"), EndedAt = context.T1, HandoffSummary = "new" };
        await context.Epochs.SaveAsync(oldest);
        await context.Epochs.SaveAsync(tie);
        await context.Epochs.SaveAsync(newest);
        await context.Messages.AppendAsync(newest.Id, "user", "only count this", context.T1);

        var first = await context.Epochs.GetArchivedPageAsync(context.ProjectA.Id, 2);
        var second = await context.Epochs.GetArchivedPageAsync(context.ProjectA.Id, 2, first.NextCursor);

        Assert.Equal([newest.Id, tie.Id], first.Epochs.Select(epoch => epoch.Id));
        Assert.Equal(1, first.Epochs[0].MessageCount);
        Assert.Equal([oldest.Id], second.Epochs.Select(epoch => epoch.Id));
        Assert.Null(second.NextCursor);
        Assert.Equal(3, first.Epochs.Concat(second.Epochs).Select(epoch => epoch.Id).Distinct().Count());
    }
}

internal sealed class LeaderStorageContext : IAsyncDisposable
{
    private readonly TemporaryDatabase _temporary;

    private LeaderStorageContext(TemporaryDatabase temporary, WorkbenchDatabase database, Project projectA, Project projectB)
    {
        _temporary = temporary;
        DatabasePath = temporary.DatabasePath;
        Database = database;
        Projects = new ProjectRepository(database);
        Leaders = new ProjectLeaderRepository(database);
        Epochs = new LeaderSessionEpochRepository(database);
        Messages = new LeaderMessageRepository(database);
        ProjectA = projectA;
        ProjectB = projectB;
    }

    public DateTimeOffset T0 { get; } = DateTimeOffset.Parse("2026-01-01T00:00:00+00:00");
    public DateTimeOffset T1 { get; } = DateTimeOffset.Parse("2026-01-02T00:00:00+00:00");
    public string DatabasePath { get; }
    public WorkbenchDatabase Database { get; }
    public ProjectRepository Projects { get; }
    public ProjectLeaderRepository Leaders { get; }
    public LeaderSessionEpochRepository Epochs { get; }
    public LeaderMessageRepository Messages { get; }
    public Project ProjectA { get; }
    public Project ProjectB { get; }

    public static async Task<LeaderStorageContext> CreateAsync()
    {
        var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();
        var now = DateTimeOffset.Parse("2026-01-01T00:00:00+00:00");
        var a = new Project(Guid.NewGuid(), "A", "C:/A", ProjectType.Generic, null, now, now);
        var b = new Project(Guid.NewGuid(), "B", "C:/B", ProjectType.Generic, null, now, now);
        var context = new LeaderStorageContext(temporary, database, a, b);
        await context.Projects.UpsertAsync(a);
        await context.Projects.UpsertAsync(b);
        return context;
    }

    public Task EnsureLeaderAsync(Guid projectId) =>
        Leaders.CreateIfMissingAsync(new StoredProjectLeader(projectId, null, T0, T0));

    public Task CreateCurrentEpochAsync(StoredLeaderSessionEpoch epoch) =>
        Leaders.CreateCurrentEpochAsync(
            new StoredProjectLeader(epoch.ProjectId, null, T0, T1),
            epoch);

    public StoredLeaderSessionEpoch CreateEpoch(Guid projectId) =>
        new(
            Guid.NewGuid(), projectId, "codex", Guid.NewGuid(), "gpt-test",
            Guid.NewGuid(), $"thread-{Guid.NewGuid():N}", "C:/Project",
            T0, T1, null, null, null);

    public ValueTask DisposeAsync() => _temporary.DisposeAsync();
}
