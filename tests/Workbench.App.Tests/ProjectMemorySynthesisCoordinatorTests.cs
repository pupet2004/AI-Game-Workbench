using Workbench.App.Memory;
using Workbench.App.Services;
using Workbench.App.Tests.Support;
using Workbench.Core.Projects;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Providers;
using Workbench.Runtime.Registry;
using Workbench.Storage.Leaders;
using Workbench.Storage.Memory;
using Workbench.Storage.Projects;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.Tests;

public sealed class ProjectMemorySynthesisCoordinatorTests
{
    [Fact]
    public async Task Pending_job_resumes_archived_agent_session_without_creating_or_touching_current_session()
    {
        await using var context = await CoordinatorContext.CreateAsync();
        context.Runtime.QueueTurn(context.Completed(ValidPayload));
        var oldLastActive = context.ArchivedEpoch.LastActiveAt;
        var oldMessages = await context.Messages.GetAllAsync(context.ArchivedEpoch.Id);

        var result = await context.Coordinator.TryProcessNextAsync(context.Project.Id);

        Assert.Equal(ProjectMemorySynthesisRunResult.Completed, result);
        var resumed = Assert.Single(context.Runtime.ResumedSessions);
        Assert.Equal(context.ArchivedEpoch.AgentSessionId, resumed.Id.Value);
        Assert.Equal(context.ArchivedEpoch.ExternalSessionId, resumed.ExternalSessionId);
        Assert.Equal(context.ArchivedEpoch.ProviderAccountId, resumed.AccountId.Value);
        Assert.Empty(context.Runtime.CreatedSessions);
        Assert.Equal(context.ArchivedEpoch.AgentSessionId, Assert.Single(context.Runtime.SentSessions).Id.Value);
        Assert.DoesNotContain("DECISION_EVIDENCE_MARKER", Assert.Single(context.Runtime.SentRequests).Text, StringComparison.Ordinal);
        Assert.Contains("1:user", Assert.Single(context.Runtime.SentRequests).Text, StringComparison.Ordinal);
        Assert.Contains("2:assistant", Assert.Single(context.Runtime.SentRequests).Text, StringComparison.Ordinal);
        Assert.Equal(oldMessages, await context.Messages.GetAllAsync(context.ArchivedEpoch.Id));
        Assert.Empty(await context.Messages.GetAllAsync(context.CurrentEpoch.Id));
        Assert.Equal(oldLastActive, (await context.Epochs.GetAsync(context.ArchivedEpoch.Id))!.LastActiveAt);
        Assert.Equal(context.CurrentEpoch.Id, (await context.Epochs.GetCurrentForProjectAsync(context.Project.Id))!.Id);
        Assert.Single(await context.Memories.GetAsync(context.Project.Id, "Learned", "Active"));
        Assert.Single(await context.Memories.GetAsync(context.Project.Id, "Candidate", "Active"));
        Assert.Empty(await context.Memories.GetAsync(context.Project.Id, "Formal", "Active"));
    }

    [Fact]
    public async Task Invalid_json_leaves_job_pending_and_saves_no_partial_memory()
    {
        await using var context = await CoordinatorContext.CreateAsync();
        context.Runtime.QueueTurn(context.Completed("not-json"));

        var result = await context.Coordinator.TryProcessNextAsync(context.Project.Id);

        Assert.Equal(ProjectMemorySynthesisRunResult.Deferred, result);
        var job = await context.Jobs.GetAsync(context.ArchivedEpoch.Id);
        Assert.Equal(ProjectMemorySynthesisJobStatus.Pending, job!.Status);
        Assert.NotNull(job.LastError);
        Assert.Empty(await context.Memories.GetAsync(context.Project.Id, "Learned", "Active"));
        Assert.Empty(await context.Memories.GetAsync(context.Project.Id, "Candidate", "Active"));
    }

    [Fact]
    public async Task Runtime_unavailable_or_archived_resume_failure_leaves_job_pending_without_replacement()
    {
        await using var unavailable = await CoordinatorContext.CreateAsync(registerRuntime: false);
        Assert.Equal(ProjectMemorySynthesisRunResult.Deferred,
            await unavailable.Coordinator.TryProcessNextAsync(unavailable.Project.Id));
        Assert.Equal(ProjectMemorySynthesisJobStatus.Pending, (await unavailable.Jobs.GetAsync(unavailable.ArchivedEpoch.Id))!.Status);

        await using var resumeFailed = await CoordinatorContext.CreateAsync();
        resumeFailed.Runtime.ResumeException = new IOException("archived thread unavailable");
        Assert.Equal(ProjectMemorySynthesisRunResult.Deferred,
            await resumeFailed.Coordinator.TryProcessNextAsync(resumeFailed.Project.Id));
        Assert.Empty(resumeFailed.Runtime.CreatedSessions);
        Assert.Equal(ProjectMemorySynthesisJobStatus.Pending, (await resumeFailed.Jobs.GetAsync(resumeFailed.ArchivedEpoch.Id))!.Status);
    }

    [Fact]
    public async Task Approval_or_tool_activity_stops_internal_turn_and_leaves_job_pending()
    {
        await using var approval = await CoordinatorContext.CreateAsync();
        approval.Runtime.QueueTurn(new AgentApprovalRequested(
            AgentApprovalRequestId.New(), new AgentSessionId(approval.ArchivedEpoch.AgentSessionId), "Read file?",
            [new AgentApprovalOption("allow", "Allow")], approval.Time.GetUtcNow()));
        Assert.Equal(ProjectMemorySynthesisRunResult.Deferred,
            await approval.Coordinator.TryProcessNextAsync(approval.Project.Id));
        Assert.Empty(approval.Runtime.ApprovalDecisions);
        Assert.Single(approval.Runtime.StoppedSessions);
        Assert.Equal(ProjectMemorySynthesisJobStatus.Pending, (await approval.Jobs.GetAsync(approval.ArchivedEpoch.Id))!.Status);

        await using var tool = await CoordinatorContext.CreateAsync();
        tool.Runtime.QueueTurn(new AgentToolEvent("shell", "attempted command", tool.Time.GetUtcNow()));
        Assert.Equal(ProjectMemorySynthesisRunResult.Deferred,
            await tool.Coordinator.TryProcessNextAsync(tool.Project.Id));
        Assert.Single(tool.Runtime.StoppedSessions);
        Assert.Equal(ProjectMemorySynthesisJobStatus.Pending, (await tool.Jobs.GetAsync(tool.ArchivedEpoch.Id))!.Status);
    }

    [Fact]
    public async Task Failed_job_can_retry_later_without_duplicate_candidate()
    {
        await using var context = await CoordinatorContext.CreateAsync();
        context.Runtime.QueueTurn(context.Completed("{}"));
        context.Runtime.QueueTurn(context.Completed(ValidPayload));

        Assert.Equal(ProjectMemorySynthesisRunResult.Deferred,
            await context.Coordinator.TryProcessNextAsync(context.Project.Id));
        Assert.Equal(ProjectMemorySynthesisRunResult.Completed,
            await context.Coordinator.TryProcessNextAsync(context.Project.Id));

        var job = await context.Jobs.GetAsync(context.ArchivedEpoch.Id);
        Assert.Equal(2, job!.AttemptCount);
        Assert.Single(await context.Memories.GetAsync(context.Project.Id, "Candidate", "Active"));
    }

    [Fact]
    public async Task Same_project_is_single_flight_and_one_trigger_attempts_only_one_job()
    {
        await using var context = await CoordinatorContext.CreateAsync();
        context.Runtime.PauseBeforeEvents = true;
        context.Runtime.QueueTurn(context.Completed(ValidPayload));
        var first = context.Coordinator.TryProcessNextAsync(context.Project.Id);
        await context.Runtime.WaitForSendAsync();

        var second = await context.Coordinator.TryProcessNextAsync(context.Project.Id);

        Assert.Equal(ProjectMemorySynthesisRunResult.AlreadyRunning, second);
        Assert.Single(context.Runtime.SentRequests);
        context.Runtime.ReleaseSend();
        Assert.Equal(ProjectMemorySynthesisRunResult.Completed, await first);
    }

    [Fact]
    public async Task Pending_job_survives_service_recreation_and_next_safe_trigger_completes_it()
    {
        await using var context = await CoordinatorContext.CreateAsync();
        var reopenedJobs = new ProjectMemorySynthesisRepository(context.Services.Database, context.Time);
        var reopened = new ProjectMemorySynthesisCoordinator(
            context.Services.RuntimeRegistry, reopenedJobs, context.Epochs, context.Messages, context.Memories, context.Time);
        context.Runtime.QueueTurn(context.Completed(ValidPayload));

        var result = await reopened.TryProcessNextAsync(context.Project.Id);

        Assert.Equal(ProjectMemorySynthesisRunResult.Completed, result);
        Assert.Equal(ProjectMemorySynthesisJobStatus.Completed, (await reopenedJobs.GetAsync(context.ArchivedEpoch.Id))!.Status);
    }

    private const string ValidPayload = """
        {
          "learned": [{"topic":"Current Development Phase","content":"Building Project Memory.","source_message_sequences":[1]}],
          "candidates": [{"topic":"Memory Ownership","content":"Project memory belongs to the Workbench, not to an individual runtime session.","source_message_sequences":[1]}]
        }
        """;
}

internal sealed class CoordinatorContext : IAsyncDisposable
{
    private readonly TemporaryDirectory _directory;

    private CoordinatorContext(
        TemporaryDirectory directory,
        AppServices services,
        MutableTimeProvider time,
        FakeAgentRuntime runtime,
        CoreProject project,
        StoredLeaderSessionEpoch archivedEpoch,
        StoredLeaderSessionEpoch currentEpoch)
    {
        _directory = directory;
        Services = services;
        Time = time;
        Runtime = runtime;
        Project = project;
        ArchivedEpoch = archivedEpoch;
        CurrentEpoch = currentEpoch;
        Jobs = new ProjectMemorySynthesisRepository(services.Database, time);
        Epochs = services.LeaderSessionEpochRepository;
        Messages = services.LeaderMessageRepository;
        Memories = new ProjectMemoryRepository(services.Database);
        Coordinator = new ProjectMemorySynthesisCoordinator(
            services.RuntimeRegistry, Jobs, Epochs, Messages, Memories, time);
    }

    public AppServices Services { get; }
    public MutableTimeProvider Time { get; }
    public FakeAgentRuntime Runtime { get; }
    public CoreProject Project { get; }
    public StoredLeaderSessionEpoch ArchivedEpoch { get; }
    public StoredLeaderSessionEpoch CurrentEpoch { get; }
    public ProjectMemorySynthesisRepository Jobs { get; }
    public LeaderSessionEpochRepository Epochs { get; }
    public LeaderMessageRepository Messages { get; }
    public ProjectMemoryRepository Memories { get; }
    public ProjectMemorySynthesisCoordinator Coordinator { get; }

    public static async Task<CoordinatorContext> CreateAsync(bool registerRuntime = true)
    {
        var directory = new TemporaryDirectory("memory-coordinator");
        var time = new MutableTimeProvider(DateTimeOffset.Parse("2026-08-13T00:00:00+00:00"));
        var runtime = new FakeAgentRuntime(
            "Fake Provider", "Memory Account", ProviderAccountId.New(),
            new ModelProfile(new ProviderId("fake-provider"), "model-a", "Model A", AgentCapability.Resume));
        var registry = new AgentRuntimeRegistry();
        if (registerRuntime)
        {
            registry.Register(runtime);
        }
        var services = AppServices.CreateForDatabasePath(Path.Combine(directory.Path, "workbench.db"), time, registry);
        await services.InitializeAsync();
        var project = new CoreProject(Guid.NewGuid(), "Memory", "C:/Memory", ProjectType.Generic, null, time.GetUtcNow(), time.GetUtcNow());
        await new ProjectRepository(services.Database).UpsertAsync(project);
        var old = Epoch(project.Id, runtime, time.GetUtcNow(), "archived-thread");
        await services.ProjectLeaderRepository.CreateCurrentEpochAsync(
            new StoredProjectLeader(project.Id, null, time.GetUtcNow(), time.GetUtcNow()), old);
        var messages = services.LeaderMessageRepository;
        await messages.AppendAsync(old.Id, "user", "DECISION_EVIDENCE_MARKER", time.GetUtcNow());
        await messages.AppendAsync(old.Id, "assistant", "Acknowledged.", time.GetUtcNow());
        time.SetUtcNow(time.GetUtcNow().AddHours(1));
        var current = Epoch(project.Id, runtime, time.GetUtcNow(), "current-thread");
        await services.ProjectLeaderRepository.RolloverAsync(
            project.Id, old.Id, current, time.GetUtcNow(), "Manual", "handoff");
        var archived = (await services.LeaderSessionEpochRepository.GetAsync(old.Id))!;
        return new(directory, services, time, runtime, project, archived, current);
    }

    public AgentTurnCompleted Completed(string text) =>
        new(new AgentResult(new AgentSessionId(ArchivedEpoch.AgentSessionId), AgentSessionStatus.Completed, text, null), Time.GetUtcNow());

    private static StoredLeaderSessionEpoch Epoch(Guid projectId, FakeAgentRuntime runtime, DateTimeOffset now, string external) =>
        new(Guid.NewGuid(), projectId, runtime.Provider.Id.Value, runtime.Account.Id.Value, "model-a", Guid.NewGuid(), external, "C:/Memory", now, now, null, null, null);

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        _directory.Dispose();
    }
}
