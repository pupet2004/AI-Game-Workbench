using Workbench.Core.Continuity;
using Workbench.Storage.Continuity;

namespace Workbench.App.Continuity;

public sealed class B1ProjectionService(
    B1AuthorityRepository authorityRepository)
{
    private readonly B1AuthorityRepository _authorityRepository =
        authorityRepository ?? throw new ArgumentNullException(nameof(authorityRepository));

    public async Task<B1ProjectProjection> GetProjectProjectionAsync(
        ProjectRef projectRef,
        CancellationToken ct = default)
    {
        var state = await GetProjectStateAsync(projectRef, ct);
        return B1Projector.Build(state);
    }

    public Task<B1ProjectState> GetProjectStateAsync(
        ProjectRef projectRef,
        CancellationToken ct = default) =>
        _authorityRepository.LoadProjectStateAsync(projectRef, ct);

    public async Task<AcceptedProjectState> GetAcceptedProjectStateAsync(
        ProjectRef projectRef,
        CancellationToken ct = default) =>
        (await GetProjectProjectionAsync(projectRef, ct)).AcceptedProjectState;
}
