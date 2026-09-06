using Microsoft.Data.Sqlite;
using Workbench.Core.Projects;
using Workbench.Storage.Database;
using Workbench.Storage.Memory;
using Workbench.Storage.Projects;
using Workbench.Storage.Tests.Database;

namespace Workbench.Storage.Tests.Memory;

public sealed class ProjectLibraryProposalTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-08-14T08:00:00+00:00");
    private static readonly DateOnly Day = new(2026, 8, 14);

    [Fact]
    public async Task Creating_and_previewing_proposal_writes_no_library_archive()
    {
        await using var fixture = await Fixture.CreateAsync();

        var proposal = await fixture.Proposals.CreateProposalAsync(fixture.CreateNodeDraft());

        Assert.Equal(LibraryProposalStatus.Pending, proposal.Status);
        Assert.Equal(proposal.Id, Assert.Single(await fixture.Proposals.GetPendingAsync(fixture.Project.Id)).Id);
        Assert.Equal(0L, await fixture.CountAsync("project_library_objects"));
        Assert.Equal(0L, await fixture.CountAsync("project_library_timeline_nodes"));
        Assert.Equal(0L, await fixture.CountAsync("project_library_material_refs"));
    }

    [Fact]
    public async Task Create_node_with_non_library_target_is_rejected_before_pending_proposal_is_persisted()
    {
        await using var fixture = await Fixture.CreateAsync();
        var assignmentId = Guid.NewGuid();
        var draft = fixture.CreateNodeDraft() with { TargetObjectId = assignmentId };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Proposals.CreateProposalAsync(draft));

        Assert.Contains("Library Object", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await fixture.Proposals.GetPendingAsync(fixture.Project.Id));
        Assert.Equal(0L, await fixture.CountAsync("project_library_proposals"));
    }

    [Fact]
    public async Task Create_node_with_existing_library_object_target_can_be_accepted_as_append()
    {
        await using var fixture = await Fixture.CreateAsync();
        var libraryObject = await fixture.Library.CreateObjectAsync(fixture.Project.Id, "Design", "Relics", T0);
        var draft = fixture.CreateNodeDraft() with
        {
            TargetObjectId = libraryObject.Id,
            CurrentOverview = null,
            ExpectedOverviewRevision = null
        };

        var proposal = await fixture.Proposals.CreateProposalAsync(draft);
        await fixture.Proposals.AcceptAsync(fixture.Project.Id, proposal.Id);

        Assert.Equal(LibraryProposalStatus.Accepted,
            (await fixture.Proposals.GetAsync(fixture.Project.Id, proposal.Id))!.Status);
        var node = Assert.Single(await fixture.Library.GetTimelineAsync(fixture.Project.Id, libraryObject.Id));
        Assert.Equal(draft.NodeContent, node.Content);
    }

    [Fact]
    public async Task Reject_is_idempotent_and_writes_no_library_or_other_memory_state()
    {
        await using var fixture = await Fixture.CreateAsync();
        var proposal = await fixture.Proposals.CreateProposalAsync(fixture.CreateNodeDraft());

        await fixture.Proposals.RejectAsync(fixture.Project.Id, proposal.Id);
        await fixture.Proposals.RejectAsync(fixture.Project.Id, proposal.Id);

        Assert.Equal(LibraryProposalStatus.Rejected, (await fixture.Proposals.GetAsync(fixture.Project.Id, proposal.Id))!.Status);
        Assert.Empty(await fixture.Proposals.GetPendingAsync(fixture.Project.Id));
        Assert.Equal(0L, await fixture.CountAsync("project_library_objects"));
        Assert.Equal(0L, await fixture.CountAsync("project_daily_summaries"));
        Assert.Equal(0L, await fixture.CountAsync("project_memory_synthesis_jobs"));
        Assert.Equal(0L, await fixture.CountAsync("leader_messages"));
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM leader_session_epochs WHERE handoff_summary IS NOT NULL;"));
    }

    [Fact]
    public async Task Accept_create_node_applies_object_overview_timeline_and_reference_atomically()
    {
        await using var fixture = await Fixture.CreateAsync();
        var proposal = await fixture.Proposals.CreateProposalAsync(fixture.CreateNodeDraft());

        await fixture.Proposals.AcceptAsync(fixture.Project.Id, proposal.Id);

        Assert.Equal(LibraryProposalStatus.Accepted, (await fixture.Proposals.GetAsync(fixture.Project.Id, proposal.Id))!.Status);
        var obj = Assert.Single(await fixture.Library.ListObjectsByCategoryAsync(fixture.Project.Id, "Design"));
        Assert.Equal("Relics", obj.Topic);
        Assert.Equal("Current relic state", obj.CurrentOverview);
        Assert.Equal(1, obj.OverviewRevision);
        var node = Assert.Single(await fixture.Library.GetTimelineAsync(fixture.Project.Id, obj.Id));
        Assert.Equal("Relic mechanism is implemented.", node.Content);
        Assert.Equal(Day, node.LocalDate);
        var material = Assert.Single(await fixture.Library.GetMaterialReferencesAsync(fixture.Project.Id, node.Id));
        Assert.Equal("Document", material.MaterialKind);
        Assert.Equal("docs/relics.md", material.Reference);
        Assert.Equal("Design notes", material.Label);
    }

    [Fact]
    public async Task Accept_update_node_applies_node_overview_and_references_together()
    {
        await using var fixture = await Fixture.CreateAsync();
        var obj = await fixture.Library.CreateObjectAsync(fixture.Project.Id, "Design", "Relics", T0);
        var node = await fixture.Library.AddNodeAsync(
            fixture.Project.Id,
            obj.Id,
            Day,
            "Original",
            [new("Document", "docs/original.md", null)],
            T0);
        var proposal = await fixture.Proposals.CreateProposalAsync(fixture.UpdateNodeDraft(obj.Id, node.Id, 1, 0));

        await fixture.Proposals.AcceptAsync(fixture.Project.Id, proposal.Id);

        Assert.Equal("Revised", (await fixture.Library.GetNodeAsync(fixture.Project.Id, node.Id))!.Content);
        Assert.Equal(2, (await fixture.Library.GetNodeAsync(fixture.Project.Id, node.Id))!.Revision);
        Assert.Equal("Current", (await fixture.Library.GetObjectAsync(fixture.Project.Id, obj.Id))!.CurrentOverview);
        Assert.Equal("docs/revised.md", Assert.Single(await fixture.Library.GetMaterialReferencesAsync(fixture.Project.Id, node.Id)).Reference);
    }

    [Fact]
    public async Task Stale_node_revision_leaves_proposal_pending_and_rolls_back_every_operation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var obj = await fixture.Library.CreateObjectAsync(fixture.Project.Id, "Design", "Relics", T0);
        var node = await fixture.Library.AddNodeAsync(
            fixture.Project.Id,
            obj.Id,
            Day,
            "Original",
            [new("Document", "docs/original.md", null)],
            T0);
        var proposal = await fixture.Proposals.CreateProposalAsync(fixture.UpdateNodeDraft(obj.Id, node.Id, 1, 0));
        await fixture.Library.UpdateNodeAsync(
            fixture.Project.Id,
            node.Id,
            "Concurrent",
            1,
            [new("Document", "docs/concurrent.md", null)],
            T0.AddMinutes(1));

        await Assert.ThrowsAsync<LibraryRevisionConflictException>(() =>
            fixture.Proposals.AcceptAsync(fixture.Project.Id, proposal.Id));

        Assert.Equal(LibraryProposalStatus.Pending, (await fixture.Proposals.GetAsync(fixture.Project.Id, proposal.Id))!.Status);
        Assert.Null((await fixture.Library.GetObjectAsync(fixture.Project.Id, obj.Id))!.CurrentOverview);
        Assert.Equal("Concurrent", (await fixture.Library.GetNodeAsync(fixture.Project.Id, node.Id))!.Content);
        Assert.Equal("docs/concurrent.md", Assert.Single(await fixture.Library.GetMaterialReferencesAsync(fixture.Project.Id, node.Id)).Reference);
    }

    [Fact]
    public async Task Mid_transaction_material_failure_rolls_back_new_object_overview_and_node()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.RejectMaterialInsertsAsync();
        var proposal = await fixture.Proposals.CreateProposalAsync(fixture.CreateNodeDraft());

        await Assert.ThrowsAsync<SqliteException>(() => fixture.Proposals.AcceptAsync(fixture.Project.Id, proposal.Id));

        Assert.Equal(LibraryProposalStatus.Pending, (await fixture.Proposals.GetAsync(fixture.Project.Id, proposal.Id))!.Status);
        Assert.Equal(0L, await fixture.CountAsync("project_library_objects"));
        Assert.Equal(0L, await fixture.CountAsync("project_library_timeline_nodes"));
        Assert.Equal(0L, await fixture.CountAsync("project_library_material_refs"));
    }

    [Fact]
    public async Task Confirmation_is_project_scoped_and_cannot_apply_foreign_proposal()
    {
        await using var fixture = await Fixture.CreateAsync();
        var other = Project("Other");
        await fixture.Projects.UpsertAsync(other);
        var proposal = await fixture.Proposals.CreateProposalAsync(fixture.CreateNodeDraft());

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Proposals.AcceptAsync(other.Id, proposal.Id));

        Assert.Equal(LibraryProposalStatus.Pending, (await fixture.Proposals.GetAsync(fixture.Project.Id, proposal.Id))!.Status);
        Assert.Empty(await fixture.Library.ListObjectsAsync(fixture.Project.Id));
        Assert.Empty(await fixture.Library.ListObjectsAsync(other.Id));
    }

    [Fact]
    public async Task Duplicate_accept_is_idempotent_and_does_not_replay_archive_writes()
    {
        await using var fixture = await Fixture.CreateAsync();
        var proposal = await fixture.Proposals.CreateProposalAsync(fixture.CreateNodeDraft());

        await fixture.Proposals.AcceptAsync(fixture.Project.Id, proposal.Id);
        await fixture.Proposals.AcceptAsync(fixture.Project.Id, proposal.Id);

        Assert.Equal(1L, await fixture.CountAsync("project_library_objects"));
        Assert.Equal(1L, await fixture.CountAsync("project_library_timeline_nodes"));
        Assert.Equal(1L, await fixture.CountAsync("project_library_material_refs"));
    }

    [Fact]
    public async Task Edit_and_accept_applies_user_content_without_adding_reason_fields()
    {
        await using var fixture = await Fixture.CreateAsync();
        var proposal = await fixture.Proposals.CreateProposalAsync(fixture.CreateNodeDraft());
        var edit = new LibraryProposalEdit(
            "User-edited factual state",
            "User-edited overview",
            [new("GitCommit", "abc123", "Accepted implementation")]);

        await fixture.Proposals.EditAndAcceptAsync(fixture.Project.Id, proposal.Id, edit);

        var obj = Assert.Single(await fixture.Library.ListObjectsAsync(fixture.Project.Id));
        Assert.Equal("User-edited overview", obj.CurrentOverview);
        var node = Assert.Single(await fixture.Library.GetTimelineAsync(fixture.Project.Id, obj.Id));
        Assert.Equal("User-edited factual state", node.Content);
        Assert.Equal("abc123", Assert.Single(await fixture.Library.GetMaterialReferencesAsync(fixture.Project.Id, node.Id)).Reference);
        Assert.DoesNotContain(typeof(ProjectLibraryProposalDraft).GetProperties(), property =>
            property.Name.Contains("Reason", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("Rationale", StringComparison.OrdinalIgnoreCase));
    }

    private static Project Project(string name) =>
        new(Guid.NewGuid(), name, $"C:/{name}/{Guid.NewGuid():N}", ProjectType.Generic, null, T0, T0);

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            TemporaryDatabase temporary,
            WorkbenchDatabase database,
            Project project,
            ProjectRepository projects,
            ProjectLibraryEvolutionRepository library,
            ProjectLibraryProposalService proposals)
        {
            Temporary = temporary;
            Database = database;
            Project = project;
            Projects = projects;
            Library = library;
            Proposals = proposals;
        }

        public TemporaryDatabase Temporary { get; }
        public WorkbenchDatabase Database { get; }
        public Project Project { get; }
        public ProjectRepository Projects { get; }
        public ProjectLibraryEvolutionRepository Library { get; }
        public ProjectLibraryProposalService Proposals { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var temporary = new TemporaryDatabase();
            var database = new WorkbenchDatabase(temporary.DatabasePath);
            await database.InitializeAsync();
            var project = ProjectLibraryProposalTests.Project("Proposal");
            var projects = new ProjectRepository(database);
            await projects.UpsertAsync(project);
            return new(
                temporary,
                database,
                project,
                projects,
                new ProjectLibraryEvolutionRepository(database),
                new ProjectLibraryProposalService(database, new FixedTimeProvider(T0.AddHours(1))));
        }

        public ProjectLibraryProposalDraft CreateNodeDraft() => new(
            Guid.NewGuid(),
            Project.Id,
            Guid.NewGuid(),
            LibraryProposalAction.CreateNode,
            null,
            null,
            null,
            0,
            "Design",
            "Relics",
            Day,
            "Relic mechanism is implemented.",
            "Current relic state",
            [new("Document", "docs/relics.md", "Design notes")],
            T0);

        public ProjectLibraryProposalDraft UpdateNodeDraft(
            Guid objectId,
            Guid nodeId,
            int expectedNodeRevision,
            int expectedOverviewRevision) => new(
            Guid.NewGuid(),
            Project.Id,
            Guid.NewGuid(),
            LibraryProposalAction.UpdateNode,
            objectId,
            nodeId,
            expectedNodeRevision,
            expectedOverviewRevision,
            "Design",
            "Relics",
            Day,
            "Revised",
            "Current",
            [new("Document", "docs/revised.md", null)],
            T0);

        public async Task<long> CountAsync(string table) =>
            await ScalarAsync($"SELECT COUNT(*) FROM {table};");

        public async Task<long> ScalarAsync(string sql)
        {
            await using var connection = Database.CreateConnection();
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }

        public async Task RejectMaterialInsertsAsync()
        {
            await using var connection = Database.CreateConnection();
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = "CREATE TRIGGER reject_proposal_material BEFORE INSERT ON project_library_material_refs BEGIN SELECT RAISE(ABORT, 'material rejected'); END;";
            await command.ExecuteNonQueryAsync();
        }

        public ValueTask DisposeAsync() => Temporary.DisposeAsync();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
