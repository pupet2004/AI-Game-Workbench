using Workbench.App.Leader;
using Workbench.App.Tests.Support;
using Workbench.Core.Projects;
using Workbench.Core.Tasks;
using Workbench.Runtime.Agents;
using Workbench.Storage.Tasks;
using Workbench.Storage.Workers;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.Tests;

public sealed class LeaderReviewRuntimeAdapterTests
{
    [Theory]
    [InlineData("PASS", "L1_LOCAL_FIX", "REPORT_ONLY", LeaderReviewOutcome.Pass, LeaderReviewActionLevel.L1LocalFix)]
    [InlineData("FIX", "L1_LOCAL_FIX", "EVIDENCE_CHECK", LeaderReviewOutcome.Fix, LeaderReviewActionLevel.L1LocalFix)]
    [InlineData("FIX", "L2_TASK_REWORK", "DEEP", LeaderReviewOutcome.Fix, LeaderReviewActionLevel.L2TaskRework)]
    [InlineData("CONTINUE", "L2_TASK_REWORK", "REPORT_ONLY", LeaderReviewOutcome.Continue, LeaderReviewActionLevel.L2TaskRework)]
    [InlineData("ASK_USER", "L3_DECISION_REQUIRED", "EVIDENCE_CHECK", LeaderReviewOutcome.AskUser, LeaderReviewActionLevel.L3DecisionRequired)]
    public async Task Valid_structured_response_from_the_existing_leader_session_returns_the_C1_decision(
        string outcome, string actionLevel, string reviewDepth, LeaderReviewOutcome expectedOutcome, LeaderReviewActionLevel expectedActionLevel)
    {
        var runtime = new FakeAgentRuntime();
        var session = LeaderSession(runtime);
        var input = ReviewInput();
        runtime.QueueTurn(Completed(session, DecisionJson(input, outcome: outcome, actionLevel: actionLevel, reviewDepth: reviewDepth)));
        var adapter = new LeaderReviewRuntimeAdapter();

        var result = await adapter.ReviewAsync(input, runtime, session);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Decision);
        Assert.Equal(expectedOutcome, result.Decision.Outcome);
        Assert.Equal(expectedActionLevel, result.Decision.ActionLevel);
        Assert.Equal(Enum.Parse<LeaderReviewDepth>(reviewDepth.Replace("_", string.Empty, StringComparison.OrdinalIgnoreCase), true), result.Decision.ReviewDepth);
        Assert.Equal(input.TaskId, result.Decision.TaskId);
        Assert.Equal(input.TaskRevisionId, result.Decision.TaskRevisionId);
        Assert.Equal(input.FinalReportEventId, result.Decision.FinalReportEventId);
        Assert.Equal(session.Id, runtime.SentSessions.Single().Id);
        Assert.Empty(runtime.CreateRequests);
        Assert.Empty(runtime.TranscriptRequests);
        var request = Assert.Single(runtime.SentRequests);
        Assert.NotNull(request.OutputSchema);
        Assert.All(["PASS", "FIX", "CONTINUE", "ASK_USER", "L1_LOCAL_FIX", "L2_TASK_REWORK", "L3_DECISION_REQUIRED", "REPORT_ONLY", "EVIDENCE_CHECK", "DEEP"],
            value => Assert.Contains(value, request.OutputSchema, StringComparison.Ordinal));
        Assert.Contains(input.FinalReport.Body, request.Text, StringComparison.Ordinal);
        Assert.Contains(input.Goal, request.Text, StringComparison.Ordinal);
        Assert.All(input.AcceptanceCriteria, item => Assert.Contains(item, request.Text, StringComparison.Ordinal));
        Assert.Contains(input.TaskId.ToString(), request.Text, StringComparison.Ordinal);
        Assert.Contains(input.TaskRevisionId.ToString(), request.Text, StringComparison.Ordinal);
        Assert.Contains(input.FinalReportEventId.ToString(), request.Text, StringComparison.Ordinal);
        Assert.Contains("REPORT FIRST", request.Text, StringComparison.Ordinal);
        Assert.Contains("does not prove all code has no bugs", request.Text, StringComparison.Ordinal);
        Assert.Contains("insufficient evidence", request.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("scan the repository", request.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("{", "Review decision JSON is malformed.")]
    [InlineData("{\"taskId\":\"00000000-0000-0000-0000-000000000002\",\"taskRevisionId\":\"00000000-0000-0000-0000-000000000003\",\"finalReportEventId\":\"00000000-0000-0000-0000-000000000005\",\"outcome\":\"UNKNOWN\",\"actionLevel\":\"L1_LOCAL_FIX\",\"reviewDepth\":\"REPORT_ONLY\",\"summary\":\"x\",\"nextAction\":\"x\"}", "Outcome is invalid.")]
    public async Task Malformed_or_unknown_enum_structured_output_is_rejected(string output, string expectedError)
    {
        var runtime = new FakeAgentRuntime();
        var session = LeaderSession(runtime);
        var input = ReviewInput();
        runtime.QueueTurn(Completed(session, output));

        var result = await new LeaderReviewRuntimeAdapter().ReviewAsync(input, runtime, session);

        Assert.False(result.Succeeded);
        Assert.Null(result.Decision);
        Assert.Equal(LeaderReviewFailureKind.StructuredOutput, result.FailureKind);
        Assert.Equal(expectedError, result.Error);
    }

    [Fact]
    public async Task Prose_prefixed_json_is_rejected_without_guessing_or_fallback()
    {
        var runtime = new FakeAgentRuntime();
        var session = LeaderSession(runtime);
        var input = ReviewInput();
        runtime.QueueTurn(Completed(session, "Looks good! " + DecisionJson(input)));

        var result = await new LeaderReviewRuntimeAdapter().ReviewAsync(input, runtime, session);

        Assert.False(result.Succeeded);
        Assert.Null(result.Decision);
        Assert.Equal(LeaderReviewFailureKind.StructuredOutput, result.FailureKind);
    }

    [Theory]
    [InlineData("Task")]
    [InlineData("Revision")]
    [InlineData("FinalReport")]
    public async Task Decision_with_mismatched_input_identity_is_rejected(string mismatch)
    {
        var runtime = new FakeAgentRuntime();
        var session = LeaderSession(runtime);
        var input = ReviewInput();
        runtime.QueueTurn(Completed(session, mismatch switch
        {
            "Task" => DecisionJson(input, taskId: Guid.NewGuid()),
            "Revision" => DecisionJson(input, taskRevisionId: Guid.NewGuid()),
            _ => DecisionJson(input, finalReportEventId: Guid.NewGuid())
        }));

        var result = await new LeaderReviewRuntimeAdapter().ReviewAsync(input, runtime, session);

        Assert.False(result.Succeeded);
        Assert.Null(result.Decision);
        Assert.Equal(LeaderReviewFailureKind.Validation, result.FailureKind);
    }

    [Fact]
    public async Task Runtime_error_throw_or_non_completed_turn_does_not_return_a_decision()
    {
        var input = ReviewInput();
        var errorRuntime = new FakeAgentRuntime();
        var errorSession = LeaderSession(errorRuntime);
        errorRuntime.QueueTurn(new AgentError("runtime failed", DateTimeOffset.UtcNow));

        var failed = await new LeaderReviewRuntimeAdapter().ReviewAsync(input, errorRuntime, errorSession);

        Assert.False(failed.Succeeded);
        Assert.Null(failed.Decision);
        Assert.Equal(LeaderReviewFailureKind.Runtime, failed.FailureKind);

        var throwingRuntime = new FakeAgentRuntime { SendException = new IOException("runtime threw") };
        var throwingSession = LeaderSession(throwingRuntime);
        var thrown = await new LeaderReviewRuntimeAdapter().ReviewAsync(input, throwingRuntime, throwingSession);
        Assert.False(thrown.Succeeded);
        Assert.Null(thrown.Decision);
        Assert.Equal(LeaderReviewFailureKind.Runtime, thrown.FailureKind);

        var incompleteRuntime = new FakeAgentRuntime();
        var incompleteSession = LeaderSession(incompleteRuntime);
        incompleteRuntime.QueueTurn(Completed(incompleteSession, DecisionJson(input), AgentSessionStatus.Failed));

        var incomplete = await new LeaderReviewRuntimeAdapter().ReviewAsync(input, incompleteRuntime, incompleteSession);

        Assert.False(incomplete.Succeeded);
        Assert.Null(incomplete.Decision);
        Assert.Equal(LeaderReviewFailureKind.Runtime, incomplete.FailureKind);
    }

    [Fact]
    public async Task One_review_invocation_has_no_persistence_or_worker_routing_side_effects()
    {
        await using var context = await AppTestContext.CreateAsync();
        var now = context.Time.GetUtcNow();
        var project = new CoreProject(Guid.NewGuid(), "Review Project", "C:/ReviewProject", ProjectType.Generic, null, now, now);
        await context.Services.ProjectRepository.UpsertAsync(project);
        var profile = ExecutionProfile.Create("provider", "account", "model", "runtime");
        var taskId = Guid.NewGuid();
        var revision = new TaskRevision(taskId, 1, "goal", "scope", "out", ["accept"], TaskRiskLevel.Low, profile, "initial", TaskRevisionApprover.User, now, null);
        await context.Services.TaskRepository.CreateAsync(project.Id, new TaskDraft(taskId, "Task", "goal", "scope", "out", ["accept"], TaskRiskLevel.Low, profile, now, revision, TaskLifecycleStatus.Reviewing));
        var events = new TaskEventRepository(context.Services.Database);
        await events.AppendAsync(new StoredTaskEvent(Guid.NewGuid(), project.Id, taskId, null, "WorkerFinalReportReceived", "{}", now));
        var beforeEvents = await events.ListAsync(project.Id, taskId, 100);
        var beforeStatus = (await context.Services.TaskRepository.GetAsync(project.Id, taskId))!.Status;
        var beforeMessages = await CountLeaderMessagesAsync(context);
        var runtime = new FakeAgentRuntime();
        var session = LeaderSession(runtime);
        var input = ReviewInput() with { ProjectId = project.Id, TaskId = taskId, TaskRevisionId = revision.Id };
        runtime.QueueTurn(Completed(session, DecisionJson(input)));

        var result = await new LeaderReviewRuntimeAdapter().ReviewAsync(input, runtime, session);

        Assert.True(result.Succeeded);
        Assert.Equal(beforeEvents, await events.ListAsync(project.Id, taskId, 100));
        Assert.Equal(beforeStatus, (await context.Services.TaskRepository.GetAsync(project.Id, taskId))!.Status);
        Assert.Equal(beforeMessages, await CountLeaderMessagesAsync(context));
        Assert.DoesNotContain(await events.ListAsync(project.Id, taskId, 100), item => item.Type == "LeaderReviewDecisionRecorded" || item.Type == "WorkerSessionStarted");
    }

    private static LeaderReviewInput ReviewInput() => new(
        Guid.Parse("00000000-0000-0000-0000-000000000001"),
        "Review Project",
        Guid.Parse("00000000-0000-0000-0000-000000000002"),
        Guid.Parse("00000000-0000-0000-0000-000000000003"),
        "Verify the assignment",
        ["Build succeeds", "Smoke passes"],
        "Only the adapter",
        "No persistence",
        Guid.Parse("00000000-0000-0000-0000-000000000004"),
        Guid.Parse("00000000-0000-0000-0000-000000000005"),
        new LeaderReviewFinalReport("Worker reports build and smoke pass.", "build green; smoke green"),
        TaskLifecycleStatus.Reviewing);

    private static AgentSession LeaderSession(FakeAgentRuntime runtime) => new(
        AgentSessionId.New(), runtime.Account.Id, runtime.Provider.Id, "model-a", "C:/ReviewProject", "leader-thread",
        AgentSessionStatus.Ready, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static AgentTurnCompleted Completed(AgentSession session, string text, AgentSessionStatus status = AgentSessionStatus.Completed) =>
        new(new AgentResult(session.Id, status, text, status == AgentSessionStatus.Completed ? null : "turn failed"), DateTimeOffset.UtcNow);

    private static string DecisionJson(LeaderReviewInput input, Guid? taskId = null, Guid? taskRevisionId = null, Guid? finalReportEventId = null, string outcome = "PASS", string actionLevel = "L1_LOCAL_FIX", string reviewDepth = "REPORT_ONLY") =>
        $$"""{"taskId":"{{taskId ?? input.TaskId}}","taskRevisionId":"{{taskRevisionId ?? input.TaskRevisionId}}","finalReportEventId":"{{finalReportEventId ?? input.FinalReportEventId}}","outcome":"{{outcome}}","actionLevel":"{{actionLevel}}","reviewDepth":"{{reviewDepth}}","summary":"Acceptance is met.","nextAction":"No action."}""";

    private static async Task<long> CountLeaderMessagesAsync(AppTestContext context)
    {
        await using var connection = context.Services.Database.CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM leader_messages;";
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
