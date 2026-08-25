using Workbench.Core.Continuity;
using Workbench.Storage.Continuity;
using Workbench.Storage.Memory;

namespace Workbench.App.Memory;

/// <summary>
/// Read-only mapping from B1 authority history/current projection to the
/// user-facing Library. Object association is accepted only when explicit
/// provenance material references identify both a Decision and contribution.
/// </summary>
public sealed class LibraryAcceptedStateReader(
    B1AuthorityRepository authorityRepository,
    ProjectLibraryEvolutionRepository libraryRepository)
{
    private readonly B1AuthorityRepository _authorityRepository =
        authorityRepository ?? throw new ArgumentNullException(nameof(authorityRepository));
    private readonly ProjectLibraryEvolutionRepository _libraryRepository =
        libraryRepository ?? throw new ArgumentNullException(nameof(libraryRepository));

    public async Task<LibraryAcceptedStateReadModel> ReadAsync(
        ProjectRef projectRef,
        CancellationToken cancellationToken = default)
    {
        var state = await _authorityRepository.LoadProjectStateAsync(projectRef, cancellationToken);
        var accepted = B1Projector.Build(state).AcceptedProjectState;
        var objects = (await _libraryRepository.ListObjectsAsync(projectRef.Value, cancellationToken))
            .ToDictionary(value => value.Id);
        var links = await LoadExplicitLinksAsync(projectRef, objects, cancellationToken);
        var decisions = new List<LibraryProjectDecisionProjection>();

        foreach (var decision in state.AuthorityDecisions)
        {
            var contributions = decision.AcceptedStateContributions;
            var decisionLinks = links
                .Where(value => value.AuthorityDecisionRef == decision.DecisionRef)
                .ToArray();
            decisions.Add(new(decision, contributions, decisionLinks));
        }

        var contributionProjections = accepted.CurrentContributions
            .Select(contribution =>
            {
                var decision = state.AuthorityDecisions.Single(value =>
                    value.DecisionRef == contribution.AuthorityDecisionRef);
                var contributionLinks = links
                    .Where(value => value.AuthorityDecisionRef == decision.DecisionRef &&
                                    value.ContributionRef == contribution.ContributionRef)
                    .ToArray();
                return new LibraryAcceptedContributionProjection(contribution, decision, contributionLinks);
            })
            .ToArray();

        var projectLevel = decisions
            .Where(value => value.LibraryProjections.Count == 0)
            .ToArray();

        return new(projectRef, accepted, contributionProjections, decisions, projectLevel);
    }

    private async Task<IReadOnlyList<LibraryObjectTimelineProjection>> LoadExplicitLinksAsync(
        ProjectRef projectRef,
        IReadOnlyDictionary<Guid, ProjectLibraryObject> objects,
        CancellationToken cancellationToken)
    {
        var result = new List<LibraryObjectTimelineProjection>();
        foreach (var metadata in await _libraryRepository.ListTimelineMetadataAsync(projectRef.Value, cancellationToken))
        {
            var materials = await _libraryRepository.GetMaterialReferencesAsync(
                projectRef.Value,
                metadata.NodeId,
                cancellationToken);
            var decisionRef = ParseMaterialReference(
                materials,
                LibraryProjectionMaterialKinds.AuthorityDecision,
                value => new AuthorityDecisionRef(value));
            var contributionRef = ParseMaterialReference(
                materials,
                LibraryProjectionMaterialKinds.AcceptedContribution,
                value => new AcceptedStateContributionRef(value));
            if (decisionRef is null || contributionRef is null || !objects.TryGetValue(metadata.ObjectId, out var libraryObject))
                continue;

            var node = await _libraryRepository.GetNodeAsync(projectRef.Value, metadata.NodeId, cancellationToken);
            if (node is null) continue;
            result.Add(new(libraryObject, node, materials, decisionRef.Value, contributionRef.Value));
        }

        return result;
    }

    private static TRef? ParseMaterialReference<TRef>(
        IReadOnlyList<LibraryMaterialReference> materials,
        string materialKind,
        Func<Guid, TRef> create)
        where TRef : struct
    {
        var reference = materials.FirstOrDefault(value =>
            string.Equals(value.MaterialKind, materialKind, StringComparison.Ordinal));
        return reference is not null && Guid.TryParse(reference.Reference, out var value)
            ? create(value)
            : null;
    }
}
