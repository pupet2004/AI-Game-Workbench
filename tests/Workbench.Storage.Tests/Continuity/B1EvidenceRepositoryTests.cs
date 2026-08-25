using System.Text;
using Workbench.Core.Continuity;
using Workbench.Core.Projects;
using Workbench.Storage.Continuity;
using Workbench.Storage.Database;
using Workbench.Storage.Projects;
using Workbench.Storage.Tests.Database;

namespace Workbench.Storage.Tests.Continuity;

public sealed class B1EvidenceRepositoryTests
{
    [Fact]
    public async Task Evidence_roundtrips_and_verifies_sha256_content()
    {
        await using var fixture = await Fixture.CreateAsync();
        var content = Encoding.UTF8.GetBytes("test report");
        var evidenceRef = new EvidenceRef("test:run/42");
        var record = new EvidenceRecord(
            new ProjectRef(fixture.Project.Id),
            evidenceRef,
            EvidenceKind.TestRun,
            "artifacts:test-run/42",
            EvidenceDigest.Sha256Algorithm,
            EvidenceDigest.ComputeSha256(content),
            content.Length,
            fixture.Now);

        Assert.Equal(record, await fixture.Repository.RecordAsync(record));
        Assert.Equal(record, await fixture.Repository.GetAsync(new(fixture.Project.Id), evidenceRef));
        Assert.True(await fixture.Repository.VerifyAsync(new(fixture.Project.Id), evidenceRef, content));
        Assert.False(await fixture.Repository.VerifyAsync(new(fixture.Project.Id), evidenceRef, Encoding.UTF8.GetBytes("tampered")));
    }

    [Fact]
    public async Task Same_reference_cannot_be_rebound_to_different_provenance()
    {
        await using var fixture = await Fixture.CreateAsync();
        var reference = new EvidenceRef("ci:run/1");
        var first = new EvidenceRecord(new(fixture.Project.Id), reference, EvidenceKind.TestRun, "ci:run/1", null, null, null, fixture.Now);
        var second = new EvidenceRecord(new(fixture.Project.Id), reference, EvidenceKind.TestRun, "ci:run/2", null, null, null, fixture.Now);

        await fixture.Repository.RecordAsync(first);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Repository.RecordAsync(second));
    }

    [Fact]
    public void Evidence_digest_rejects_invalid_sha256_values()
    {
        Assert.Throws<ArgumentException>(() => new EvidenceRecord(
            new ProjectRef(Guid.NewGuid()),
            new EvidenceRef("file:test"),
            EvidenceKind.File,
            "file:test",
            EvidenceDigest.Sha256Algorithm,
            "not-a-digest",
            null,
            DateTimeOffset.UtcNow));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly TemporaryDatabase _temporary;
        private Fixture(TemporaryDatabase temporary, WorkbenchDatabase database, Project project, B1EvidenceRepository repository, DateTimeOffset now)
        {
            _temporary = temporary;
            Database = database;
            Project = project;
            Repository = repository;
            Now = now;
        }

        public WorkbenchDatabase Database { get; }
        public Project Project { get; }
        public B1EvidenceRepository Repository { get; }
        public DateTimeOffset Now { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var temporary = new TemporaryDatabase();
            var database = new WorkbenchDatabase(temporary.DatabasePath);
            await database.InitializeAsync();
            var now = DateTimeOffset.Parse("2026-08-25T00:00:00+00:00");
            var project = new Project(Guid.NewGuid(), "Evidence", $"C:/Evidence-{Guid.NewGuid():N}", ProjectType.Generic, null, now, now);
            await new ProjectRepository(database).UpsertAsync(project);
            return new Fixture(temporary, database, project, new B1EvidenceRepository(database), now);
        }

        public ValueTask DisposeAsync() => _temporary.DisposeAsync();
    }
}
