using Workbench.Core.Tasks;
using Workbench.Runtime.Runtime;

namespace Workbench.Runtime.Registry;

public sealed record WorkerResource(
    string ProviderId,
    string ProviderAccountId,
    string AgentRuntimeId,
    string DisplayLabel,
    string ModelProfileId,
    string ModelDisplayName,
    bool IsReady)
{
    public ExecutionProfile CreateExecutionProfile()
    {
        if (!IsReady)
        {
            throw new InvalidOperationException("The Worker resource is not ready.");
        }

        return ExecutionProfile.Create(ProviderId, ProviderAccountId, ModelProfileId, AgentRuntimeId);
    }

    internal static string CreateRuntimeIdentity(IAgentRuntime runtime) =>
        $"{runtime.RuntimeKind}:{runtime.Provider.Id.Value}:{runtime.Account.Id.Value:D}";
}
