using Workbench.Runtime.Agents;
using Workbench.Runtime.Registry;

namespace Workbench.App.Worker;

public sealed record WorkerRemovalResult(bool Succeeded, string? Error)
{
    public static WorkerRemovalResult Success() => new(true, null);
    public static WorkerRemovalResult Failure(string error) => new(false, error);
}

public sealed class WorkerRemovalService(AgentRuntimeRegistry runtimes, IWorkerRoutingStore store, TimeProvider time)
{
    public async Task<WorkerRemovalResult> RemoveAsync(WorkerSessionRecord worker, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(worker);
        var runtime = runtimes.Runtimes.SingleOrDefault(item => item.Account.Id == worker.Session.AccountId);
        if (runtime is not null)
        {
            try
            {
                if (await runtime.GetStatusAsync(worker.Session, cancellationToken) == AgentSessionStatus.Running)
                {
                    await runtime.StopAsync(worker.Session, cancellationToken);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                return WorkerRemovalResult.Failure(exception.Message);
            }
        }

        try
        {
            await store.AppendRemovalAsync(new WorkerRemoval(worker.ProjectId, worker.TaskId, worker.Session.Id, time.GetUtcNow()), cancellationToken);
            return WorkerRemovalResult.Success();
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return WorkerRemovalResult.Failure(exception.Message);
        }
    }
}
