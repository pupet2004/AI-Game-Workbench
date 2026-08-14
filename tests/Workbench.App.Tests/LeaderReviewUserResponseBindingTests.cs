using System.Text.Json;
using Workbench.App.Tests.Support;
using Workbench.Core.Projects;
using Workbench.Core.Tasks;
using Workbench.Storage.Leaders;
using Workbench.Storage.Tasks;
using Workbench.Storage.Workers;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.Tests;

public sealed class LeaderReviewUserResponseBindingTests
{
    [Fact]
    public async Task Singleton_open_gate_binds_first_user_message_without_copying_body()
    {
        await using var fixture = await Fixture.CreateAsync();
        var decision = await fixture.CreateOpenGateAsync();
        var user = await fixture.Messages.AppendAsync(fixture.EpochId, "user", "a long user answer", fixture.Now);
        var binding = await fixture.Binding.TryBindAsync(fixture.Project.Id, user.Id);

        Assert.NotNull(binding);
        Assert.Equal(user.Id, binding.UserMessageId);
        Assert.Equal(decision.EventId, binding.ReviewDecisionEventId);
        Assert.Equal(TaskLifecycleStatus.NeedsUserDecision, (await fixture.Context.Services.TaskRepository.GetAsync(fixture.Project.Id, fixture.TaskId))!.Status);
        var events = await new TaskEventRepository(fixture.Context.Services.Database).ListAsync(fixture.Project.Id, fixture.TaskId, 100);
        Assert.DoesNotContain(events, item => item.Type == "LeaderReviewUserResponseReceived" && item.Payload.Contains("a long user answer", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Replay_and_second_message_do_not_create_another_binding()
    {
        await using var fixture = await Fixture.CreateAsync(); await fixture.CreateOpenGateAsync(); var first = await fixture.Messages.AppendAsync(fixture.EpochId, "user", "first", fixture.Now); var second = await fixture.Messages.AppendAsync(fixture.EpochId, "user", "second", fixture.Now.AddSeconds(1));
        var one = await fixture.Binding.TryBindAsync(fixture.Project.Id, first.Id); var replay = await fixture.Binding.TryBindAsync(fixture.Project.Id, first.Id); var later = await fixture.Binding.TryBindAsync(fixture.Project.Id, second.Id);
        Assert.Equal(one!.EventId, replay!.EventId); Assert.Null(later); Assert.Equal(first.Id, await fixture.Binding.GetBoundUserMessageIdAsync(fixture.Project.Id, fixture.TaskId, one.ReviewDecisionEventId));
    }

    [Fact]
    public async Task Wrong_project_message_does_not_bind()
    {
        await using var fixture = await Fixture.CreateAsync(); await fixture.CreateOpenGateAsync(); var other = await Fixture.CreateAsync(); await other.CreateOpenGateAsync(); var user = await other.Messages.AppendAsync(other.EpochId, "user", "ambiguous", other.Now);
        Assert.Null(await fixture.Binding.TryBindAsync(fixture.Project.Id, user.Id)); await other.DisposeAsync();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(AppTestContext context, CoreProject project, Guid taskId, TaskRevision revision, Guid epochId, LeaderMessageRepository messages, LeaderReviewUserResponseBindingRepository binding) { Context=context; Project=project; TaskId=taskId; Revision=revision; EpochId=epochId; Messages=messages; Binding=binding; Now=context.Time.GetUtcNow(); }
        public AppTestContext Context { get; } public CoreProject Project { get; } public Guid TaskId { get; } public TaskRevision Revision { get; } public Guid EpochId { get; } public LeaderMessageRepository Messages { get; } public LeaderReviewUserResponseBindingRepository Binding { get; } public DateTimeOffset Now { get; }
        public static async Task<Fixture> CreateAsync()
        { var c=await AppTestContext.CreateAsync(); var now=c.Time.GetUtcNow(); var p=new CoreProject(Guid.NewGuid(),"E2B","C:/E2B",ProjectType.Generic,null,now,now); await c.Services.ProjectRepository.UpsertAsync(p); var id=Guid.NewGuid(); var profile=ExecutionProfile.Create("provider","account","model","runtime"); var r=new TaskRevision(id,1,"goal","scope","out",["accept"],TaskRiskLevel.Low,profile,"initial",TaskRevisionApprover.User,now,null); await c.Services.TaskRepository.CreateAsync(p.Id,new TaskDraft(id,"Task","goal","scope","out",["accept"],TaskRiskLevel.Low,profile,now,r,TaskLifecycleStatus.Reviewing)); var epoch=Guid.NewGuid(); await c.Services.ProjectLeaderRepository.CreateCurrentEpochAsync(new StoredProjectLeader(p.Id,null,now,now),new StoredLeaderSessionEpoch(epoch,p.Id,"provider",Guid.NewGuid(),"model",Guid.NewGuid(),"leader",p.RootPath,now,now,null,null,null)); return new Fixture(c,p,id,r,epoch,new LeaderMessageRepository(c.Services.Database),new LeaderReviewUserResponseBindingRepository(c.Services.Database)); }
        public async Task<StoredLeaderReviewDecision> CreateOpenGateAsync()
        { var report=Guid.NewGuid(); var events=new TaskEventRepository(Context.Services.Database); await events.AppendAsync(new StoredTaskEvent(report,Project.Id,TaskId,null,"WorkerFinalReportReceived",JsonSerializer.Serialize(new { WorkerSessionId=Guid.NewGuid(),Message="done" }),Now)); var state=new AssignmentReviewStateRepository(Context.Services.Database); await state.TryRecordLeaderReviewDecisionAsync(new LeaderReviewDecisionPersistenceRequest(Guid.NewGuid(),Project.Id,TaskId,Revision.Id,report,"AskUser","L3DecisionRequired","ReportOnly","summary",null,"choose",null,Now)); var decision=(await state.GetLeaderReviewDecisionAsync(Project.Id,TaskId,Revision.Id,report))!; var marker=AssignmentReviewStateRepository.UserDecisionQuestionMarker(Project.Id,TaskId,decision.EventId); await state.TryTransitionAsync(Project.Id,TaskId,TaskLifecycleStatus.Reviewing,TaskLifecycleStatus.NeedsUserDecision,Guid.NewGuid(),"AssignmentNeedsUserDecision","{}",Now); await Messages.AppendAsync(EpochId,"user",$"Question\n[{marker}]",Now); return decision; }
        public ValueTask DisposeAsync()=>Context.DisposeAsync();
    }
}
