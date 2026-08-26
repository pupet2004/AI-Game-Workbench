using Workbench.Runtime.Providers;
using Workbench.Runtime.Runtime;

namespace Workbench.Runtime.Registry;

public sealed class AgentRuntimeRegistry
{
    private readonly Dictionary<ProviderAccountId, IAgentRuntime> _runtimes = [];

    public IReadOnlyList<IAgentRuntime> Runtimes => _runtimes.Values.ToArray();

    public void Register(IAgentRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        if (!_runtimes.TryAdd(runtime.Account.Id, runtime))
        {
            throw new InvalidOperationException(
                $"A runtime is already registered for provider account '{runtime.Account.Id}'.");
        }
    }

    public IAgentRuntime GetByAccount(ProviderAccountId accountId)
    {
        if (_runtimes.TryGetValue(accountId, out var runtime))
        {
            return runtime;
        }

        throw new KeyNotFoundException(
            $"No runtime is registered for provider account '{accountId}'.");
    }

    public async Task<IReadOnlyList<AvailableModelProfile>> GetAvailableModelsAsync(
        CancellationToken cancellationToken = default)
    {
        var availableModels = new List<AvailableModelProfile>();

        foreach (var runtime in _runtimes.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var models = await runtime.GetModelsAsync(cancellationToken).ConfigureAwait(false);
            availableModels.AddRange(models.Select(model => new AvailableModelProfile(runtime.Account.Id, model)));
        }

        return availableModels;
    }

    public async Task<IReadOnlyList<WorkerResource>> GetWorkerResourcesAsync(
        CancellationToken cancellationToken = default)
    {
        var resources = new List<WorkerResource>();

        foreach (var runtime in _runtimes.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!runtime.Account.IsConnected)
            {
                continue;
            }

            IReadOnlyList<ModelProfile> models;
            try
            {
                models = await runtime.GetModelsAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                continue;
            }

            var runtimeIdentity = WorkerResource.CreateRuntimeIdentity(runtime);
            resources.AddRange(models
                .Select(model => new WorkerResource(
                    model.ProviderId.Value,
                    runtime.Account.Id.Value.ToString(),
                    runtimeIdentity,
                    $"{runtime.Provider.DisplayName} · {runtime.Account.DisplayName}",
                    model.ModelId,
                    model.DisplayName,
                    true)));
        }

        return resources;
    }
}
