namespace Workbench.Core.Continuity;

public static class B1Projector
{
    public static B1ProjectProjection Build(B1ProjectState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var actors = state.LogicalActors.ToDictionary(x => x.LogicalActorRef);
        var responsibilities = state.Responsibilities.ToDictionary(x => x.ResponsibilityRef);
        var assignments = state.Assignments.ToDictionary(x => x.AssignmentRef);
        var revisions = state.Revisions.ToDictionary(x => x.RevisionRef);
        var dispositions = BuildDispositions(state.RevisionDispositions);
        var currentRevisions = BuildCurrentRevisions(state.Assignments, state.Revisions);
        var orderedDecisions = state.AuthorityDecisions.OrderBy(x => x.ProjectCommitSequence).ToArray();
        var replaced = orderedDecisions.Select(x => x.AssignmentDelegationEffect?.ReplacesAssignmentRef)
            .Where(x => x.HasValue).Select(x => x!.Value).ToHashSet();
        var currentDelegations = state.Assignments.Select(x => x.AssignmentRef).Where(x => !replaced.Contains(x)).ToHashSet();
        var effectiveFulfillment = currentDelegations.Where(assignment =>
            currentRevisions.TryGetValue(assignment, out var revision) && !dispositions.ContainsKey(revision)).ToHashSet();
        var currentContributions = BuildContributions(orderedDecisions);

        var accepted = new AcceptedProjectState(state.Governance.ProjectRef, actors, responsibilities, assignments,
            revisions, dispositions, currentRevisions, currentDelegations, currentContributions);

        var storedAttempts = state.AssignmentRoutingSelections.ToDictionary(x => x.AssignmentRef, x => x.SelectedAttemptRef);
        var attemptById = state.Attempts.ToDictionary(x => x.AttemptRef);
        var effectiveAttempts = storedAttempts.ToDictionary(x => x.Key, x =>
            IsEffectiveAttempt(x.Key, x.Value, effectiveFulfillment, currentRevisions, attemptById) ? x.Value : null);
        var routing = state.AttemptRoutingSelections.ToDictionary(x => x.AttemptRef);
        var storedHandoffs = routing.ToDictionary(x => x.Key, x => x.Value.SelectedHandoffRef);
        var storedBindings = routing.ToDictionary(x => x.Key, x => x.Value.SelectedSessionBindingRef);
        var effectiveAttemptIds = effectiveAttempts.Values.Where(x => x.HasValue).Select(x => x!.Value).ToHashSet();
        var handoffs = state.Handoffs.ToDictionary(x => x.HandoffRef);
        var bindings = state.SessionBindings.ToDictionary(x => x.SessionBindingRef);
        var effectiveHandoffs = storedHandoffs.ToDictionary(x => x.Key, x =>
            effectiveAttemptIds.Contains(x.Key) && x.Value is { } h && handoffs.TryGetValue(h, out var item) && item.AttemptRef == x.Key ? x.Value : null);
        var effectiveBindings = storedBindings.ToDictionary(x => x.Key, x =>
            effectiveAttemptIds.Contains(x.Key) && x.Value is { } b && bindings.TryGetValue(b, out var item) && item.AttemptRef == x.Key ? x.Value : null);

        return new(state.Governance.ProjectRef, accepted, effectiveFulfillment, storedAttempts, effectiveAttempts,
            storedHandoffs, effectiveHandoffs, storedBindings, effectiveBindings);
    }

    private static Dictionary<RevisionRef, RevisionDispositionRecord> BuildDispositions(IEnumerable<RevisionDispositionRecord> records)
    {
        var groups = records.GroupBy(x => x.RevisionRef).ToArray();
        if (groups.Any(x => x.Count() != 1)) throw new InvalidDataException("A Revision has multiple dispositions.");
        return groups.ToDictionary(x => x.Key, x => x.Single());
    }

    private static Dictionary<AssignmentRef, RevisionRef> BuildCurrentRevisions(
        IEnumerable<Assignment> assignments, IReadOnlyList<AssignmentRevision> revisions)
    {
        var result = new Dictionary<AssignmentRef, RevisionRef>();
        foreach (var assignment in assignments)
        {
            var owned = revisions.Where(x => x.AssignmentRef == assignment.AssignmentRef).ToArray();
            var parents = owned.Where(x => x.PriorRevisionRef.HasValue).Select(x => x.PriorRevisionRef!.Value).ToHashSet();
            var leaves = owned.Where(x => !parents.Contains(x.RevisionRef)).ToArray();
            if (leaves.Length != 1) throw new InvalidDataException("Assignment Revision chain has no unique current leaf.");
            result.Add(assignment.AssignmentRef, leaves[0].RevisionRef);
        }
        return result;
    }

    private static IReadOnlyList<AcceptedStateContribution> BuildContributions(IEnumerable<AuthorityDecision> decisions)
    {
        var current = new Dictionary<AcceptedStateContributionRef, AcceptedStateContribution>();
        foreach (var contribution in decisions.SelectMany(x => x.AcceptedStateContributions))
        {
            if (contribution.SupersedesContributionRef is { } old) current.Remove(old);
            current.Add(contribution.ContributionRef, contribution);
        }
        return Array.AsReadOnly(current.Values.ToArray());
    }

    private static bool IsEffectiveAttempt(AssignmentRef assignment, AttemptRef? selected,
        IReadOnlySet<AssignmentRef> effectiveAssignments, IReadOnlyDictionary<AssignmentRef, RevisionRef> revisions,
        IReadOnlyDictionary<AttemptRef, Attempt> attempts) =>
        effectiveAssignments.Contains(assignment) && selected is { } id && attempts.TryGetValue(id, out var attempt)
        && attempt.AssignmentRef == assignment && revisions[assignment] == attempt.EffectiveRevisionRef;
}
