using System.Text.Json;
using Workbench.App.Leader;
using Workbench.App.Tests.Support;
using Workbench.Core.Leaders;
using Workbench.Core.Projects;
using Workbench.Core.Tasks;
using Workbench.Runtime.Agents;
using Workbench.Storage.Leaders;
using Workbench.Storage.Tasks;
using Workbench.Storage.Reviews;
using Workbench.Storage.Workers;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.Tests;

public sealed class LeaderReviewAskUserGateTests
{
    [Theory]
    [InlineData(LeaderReviewOutcome.AskUser, LeaderReviewActionLevel.L3DecisionRequired)]
    [InlineData(LeaderReviewOutcome.Pass, LeaderReviewActionLevel.L2TaskRework)]
    [InlineData(LeaderReviewOutcome.Fix, LeaderReviewActionLevel.L2TaskRework)]
    [InlineData(LeaderReviewOutcome.Continue, LeaderReviewActionLevel.L2TaskRework)]
    public async Task Ask_user_resolution_opens_one_gate_and_uses_persisted_question_content(LeaderReviewOutcome outcome, LeaderReviewActionLevel action)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Context.Services.WorkbenchSettingsRepository.SaveLeaderAuthorityModeAsync(LeaderAuthorityMode.Cautious);
        var decision = await fixture.RecordDecisionAsync(outcome, action, issue: "scope conflict", note: "choose deliberately");

        Assert.Equal(LeaderReviewAskUserGateResultKind.Opened, (await fixture.Gate.TryOpenAsync(fixture.Project.Id, fixture.TaskId, fixture.Revision.Id, decision.FinalReportEventId)).Kind);
        Assert.Equal(TaskLifecycleStatus.NeedsUserDecision, (await fixture.Context.Services.TaskRepository.GetAsync(fixture.Project.Id, fixture.TaskId))!.Status);
        var message = Assert.Single(await fixture.MessagesAsync());
        Assert.Contains("summary", message.Text, StringComparison.Ordinal);
        Assert.Contains("scope conflict", message.Text, StringComparison.Ordinal);
        Assert.Contains("decide next", message.Text, StringComparison.Ordinal);
        Assert.Contains("choose deliberately", message.Text, StringComparison.Ordinal);
        Assert.Contains(AssignmentReviewStateRepository.UserDecisionQuestionMarker(fixture.Project.Id, fixture.TaskId, decision.EventId), message.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(LeaderAuthorityMode.Balanced, LeaderReviewActionLevel.L1LocalFix)]
    [InlineData(LeaderAuthorityMode.Balanced, LeaderReviewActionLevel.L2TaskRework)]
    [InlineData(LeaderAuthorityMode.Autonomous, LeaderReviewActionLevel.L2TaskRework)]
    public async Task Auto_and_notify_resolutions_do_not_open_a_gate(LeaderAuthorityMode authority, LeaderReviewActionLevel action)
    {
        await using var fixture = await Fixture.CreateAsync(); await fixture.Context.Services.WorkbenchSettingsRepository.SaveLeaderAuthorityModeAsync(authority); var decision = await fixture.RecordDecisionAsync(LeaderReviewOutcome.Pass, action);
        Assert.Equal(LeaderReviewAskUserGateResultKind.NoWork, (await fixture.Gate.TryOpenAsync(fixture.Project.Id, fixture.TaskId, fixture.Revision.Id, decision.FinalReportEventId)).Kind);
        Assert.Equal(TaskLifecycleStatus.Reviewing, (await fixture.Context.Services.TaskRepository.GetAsync(fixture.Project.Id, fixture.TaskId))!.Status); Assert.Empty(await fixture.MessagesAsync());
    }

    [Fact]
    public async Task Duplicate_and_recovery_are_idempotent()
    {
        await using var fixture = await Fixture.CreateAsync(); var decision = await fixture.RecordDecisionAsync(LeaderReviewOutcome.AskUser, LeaderReviewActionLevel.L3DecisionRequired);
        Assert.Equal(LeaderReviewAskUserGateResultKind.Opened, (await fixture.Gate.TryOpenAsync(fixture.Project.Id, fixture.TaskId, fixture.Revision.Id, decision.FinalReportEventId)).Kind);
        Assert.Equal(LeaderReviewAskUserGateResultKind.Existing, (await fixture.Gate.RecoverAsync(fixture.Project.Id)).Single().Kind);
        Assert.Equal(LeaderReviewAskUserGateResultKind.Existing, (await fixture.Gate.TryOpenAsync(fixture.Project.Id, fixture.TaskId, fixture.Revision.Id, decision.FinalReportEventId)).Kind);
        Assert.Single(await fixture.MessagesAsync()); Assert.Equal(1L, await fixture.CountEventsAsync("AssignmentNeedsUserDecision"));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(AppTestContext context, CoreProject project, Guid taskId, TaskRevision revision, Guid epochId, LeaderReviewAskUserGate gate) { Context=context; Project=project; TaskId=taskId; Revision=revision; EpochId=epochId; Gate=gate; }
        public AppTestContext Context { get; } public CoreProject Project { get; } public Guid TaskId { get; } public TaskRevision Revision { get; } public Guid EpochId { get; } public LeaderReviewAskUserGate Gate { get; }
        public static async Task<Fixture> CreateAsync()
        {
            var context=await AppTestContext.CreateAsync(); var now=context.Time.GetUtcNow(); var project=new CoreProject(Guid.NewGuid(),"E2A","C:/E2A",ProjectType.Generic,null,now,now); await context.Services.ProjectRepository.UpsertAsync(project); var taskId=Guid.NewGuid(); var profile=ExecutionProfile.Create("provider","account","model","runtime"); var revision=new TaskRevision(taskId,1,"goal","scope","out",["accept"],TaskRiskLevel.Low,profile,"initial",TaskRevisionApprover.User,now,null); await context.Services.TaskRepository.CreateAsync(project.Id,new TaskDraft(taskId,"Task","goal","scope","out",["accept"],TaskRiskLevel.Low,profile,now,revision,TaskLifecycleStatus.Reviewing));
            var epochId=Guid.NewGuid(); await context.Services.ProjectLeaderRepository.CreateCurrentEpochAsync(new StoredProjectLeader(project.Id,null,now,now),new StoredLeaderSessionEpoch(epochId,project.Id,"provider",Guid.NewGuid(),"model",Guid.NewGuid(),"leader",project.RootPath,now,now,null,null,null));
            return new Fixture(context,project,taskId,revision,epochId,new LeaderReviewAskUserGate(context.Services.TaskRepository,new AssignmentReviewStateRepository(context.Services.Database),new LeaderReviewStateRepository(context.Services.Database),context.Services.LeaderAuthoritySettings,context.Services.ProjectLeaderRepository,new LeaderMessageRepository(context.Services.Database),context.Time));
        }
        public async Task<StoredLeaderReviewDecision> RecordDecisionAsync(LeaderReviewOutcome outcome, LeaderReviewActionLevel action, string? issue=null, string? note=null)
        { var report=Guid.NewGuid(); await new TaskEventRepository(Context.Services.Database).AppendAsync(new StoredTaskEvent(report,Project.Id,TaskId,null,"WorkerFinalReportReceived",JsonSerializer.Serialize(new { WorkerSessionId=Guid.NewGuid(),Message="done" }),Context.Time.GetUtcNow())); var state=new AssignmentReviewStateRepository(Context.Services.Database); await state.TryRecordLeaderReviewDecisionAsync(new LeaderReviewDecisionPersistenceRequest(Guid.NewGuid(),Project.Id,TaskId,Revision.Id,report,outcome.ToString(),action.ToString(),"ReportOnly","summary",issue,"decide next",note,Context.Time.GetUtcNow())); var legacy=(await state.GetLeaderReviewDecisionAsync(Project.Id,TaskId,Revision.Id,report))!; var authority=await Context.Services.LeaderAuthoritySettings.GetEffectiveLeaderAuthorityModeAsync(Project.Id); await new LeaderReviewStateRepository(Context.Services.Database).InsertDecisionIfAbsentAsync(new(legacy.EventId,Project.Id,TaskId,Revision.Id,report,outcome.ToString(),action.ToString(),LeaderAuthorityResolver.Resolve(authority,action,outcome).ToString(),authority,legacy.CreatedAt)); return legacy; }
        public Task<IReadOnlyList<StoredLeaderMessage>> MessagesAsync()=>new LeaderMessageRepository(Context.Services.Database).GetAllAsync(EpochId);
        public async Task<long> CountEventsAsync(string type) { await using var c=Context.Services.Database.CreateConnection(); await c.OpenAsync(); var q=c.CreateCommand(); q.CommandText="SELECT COUNT(*) FROM task_events WHERE event_type=$t;"; q.Parameters.AddWithValue("$t",type); return Convert.ToInt64(await q.ExecuteScalarAsync()); }
        public ValueTask DisposeAsync()=>Context.DisposeAsync();
    }
}
