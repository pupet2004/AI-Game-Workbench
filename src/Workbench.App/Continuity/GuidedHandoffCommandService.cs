using Workbench.Core.Continuity;
using Workbench.Storage.Continuity;

namespace Workbench.App.Continuity;

public sealed class GuidedHandoffCommandService(B1ClaimHandoffRepository repository)
{
    private readonly B1ClaimHandoffRepository _repository =
        repository ?? throw new ArgumentNullException(nameof(repository));

    public Task<Handoff> RecordAndSelectAsync(
        RecordGuidedHandoffCommand command,
        HandoffRef? expectedStored,
        CancellationToken cancellationToken = default) =>
        _repository.RecordGuidedHandoffAndSelectAsync(command, expectedStored, cancellationToken);
}
