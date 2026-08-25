using System.Text;
using Workbench.App.ProjectWorld;
using Workbench.Core.Continuity;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Runtime;
using Workbench.Storage.Continuity;

namespace Workbench.App.Continuity;

public sealed record B1AgentExecutionRequest(
    ProjectRef ProjectRef,
    UserPrincipalRef AuthenticatedOperatorRef,
    AssignmentRef AssignmentRef,
    string ModelId,
    string? WorkingDirectory,
    string? Prompt,
    IReadOnlyList<EvidenceRef> EvidenceRefs,
    AttemptRef? ExistingAttemptRef = null);

public sealed record B1AgentExecutionResult(
    Attempt Attempt,
    SessionBinding SessionBinding,
    AgentSession Session,
    Handoff Handoff,
    string FinalText);

public sealed class B1AgentParticipationException(string message) : InvalidOperationException(message);

/// <summary>
/// Bridges a provider runtime into B1 participation. It can create Attempts,
/// bind an external Session, and record a non-authoritative Handoff, but it
/// has no path to commit an AuthorityDecision.
/// </summary>
public sealed class B1AgentParticipationAdapter(
    B1AuthorityRepository authorityRepository,
    B1NonAuthoritativeCommandService nonAuthoritativeCommands,
    GuidedHandoffComposerService guidedHandoffComposer,
    TimeProvider timeProvider)
{
    private readonly B1AuthorityRepository _authorityRepository =
        authorityRepository ?? throw new ArgumentNullException(nameof(authorityRepository));
    private readonly B1NonAuthoritativeCommandService _nonAuthoritativeCommands =
        nonAuthoritativeCommands ?? throw new ArgumentNullException(nameof(nonAuthoritativeCommands));
    private readonly GuidedHandoffComposerService _guidedHandoffComposer =
        guidedHandoffComposer ?? throw new ArgumentNullException(nameof(guidedHandoffComposer));
    private readonly TimeProvider _timeProvider =
        timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public async Task<B1AgentExecutionResult> ExecuteAsync(
        IAgentRuntime runtime,
        B1AgentExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ModelId);
        ArgumentNullException.ThrowIfNull(request.EvidenceRefs);

        var state = await _authorityRepository.LoadProjectStateAsync(request.ProjectRef, cancellationToken);
        var projection = B1Projector.Build(state);
        var assignment = projection.AcceptedProjectState.Assignments.GetValueOrDefault(request.AssignmentRef)
            ?? throw new B1AgentParticipationException("The Assignment is not part of the current Accepted Project State.");
        var revisionRef = projection.AcceptedProjectState.CurrentEffectiveRevisionRefs.GetValueOrDefault(request.AssignmentRef);
        if (revisionRef.Value == Guid.Empty)
            throw new B1AgentParticipationException("The Assignment has no effective Revision.");
        var revision = projection.AcceptedProjectState.Revisions[revisionRef];

        var attempt = await ResolveAttemptAsync(state, projection, request, revisionRef, cancellationToken);
        var session = await runtime.CreateSessionAsync(
            new CreateAgentSessionRequest(runtime.Account.Id, request.ModelId, request.WorkingDirectory),
            cancellationToken);
        var externalSession = new ExternalSessionRef(
            $"{runtime.Provider.Id.Value}:session/{session.ExternalSessionId ?? session.Id.Value.ToString("N")}");
        var binding = new SessionBinding(
            new SessionBindingRef(Guid.NewGuid()),
            attempt.AttemptRef,
            assignment.AssigneeActorRef,
            externalSession,
            _timeProvider.GetUtcNow());
        projection.StoredBindingSelections.TryGetValue(attempt.AttemptRef, out var expectedBinding);
        await _nonAuthoritativeCommands.CreateSessionBindingAndSelectAsync(
            new CreateSessionBindingCommand(
                request.ProjectRef,
                request.AuthenticatedOperatorRef,
                binding),
            expectedBinding,
            cancellationToken);

        var finalText = await ExecuteRuntimeAsync(runtime, session, revision.Contract.WorkContract, request.Prompt, cancellationToken);
        var handoff = await _guidedHandoffComposer.RecordAsync(
            attempt.AttemptRef,
            request.AssignmentRef,
            new GuidedHandoffRequest(
                request.ProjectRef,
                request.AuthenticatedOperatorRef,
                finalText,
                [],
                [],
                [],
                null,
                request.EvidenceRefs.Select(value => value.Value).ToArray()),
            cancellationToken);

        return new B1AgentExecutionResult(attempt, binding, session, handoff, finalText);
    }

    private async Task<Attempt> ResolveAttemptAsync(
        B1ProjectState state,
        B1ProjectProjection projection,
        B1AgentExecutionRequest request,
        RevisionRef revisionRef,
        CancellationToken cancellationToken)
    {
        if (request.ExistingAttemptRef is { } existingRef)
        {
            var existing = state.Attempts.SingleOrDefault(value => value.AttemptRef == existingRef)
                ?? throw new B1AgentParticipationException("The requested Attempt does not exist.");
            if (existing.AssignmentRef != request.AssignmentRef)
                throw new B1AgentParticipationException("The requested Attempt belongs to another Assignment.");
            projection.EffectiveCurrentAttemptRefs.TryGetValue(request.AssignmentRef, out var selected);
            if (selected != existingRef)
                throw new B1AgentParticipationException("The requested Attempt is not the selected continuation.");
            return existing;
        }

        projection.StoredAttemptSelections.TryGetValue(request.AssignmentRef, out var expectedAttempt);
        return await _nonAuthoritativeCommands.CreateAttemptAndSelectAsync(
            new CreateAttemptCommand(
                request.ProjectRef,
                request.AuthenticatedOperatorRef,
                new AttemptRef(Guid.NewGuid()),
                request.AssignmentRef,
                revisionRef,
                _timeProvider.GetUtcNow()),
            expectedAttempt,
            cancellationToken);
    }

    private static async Task<string> ExecuteRuntimeAsync(
        IAgentRuntime runtime,
        AgentSession session,
        string workContract,
        string? prompt,
        CancellationToken cancellationToken)
    {
        var requestText = BuildPrompt(workContract, prompt);
        string? finalText = null;
        var streamedText = new StringBuilder();
        await foreach (var agentEvent in runtime.SendAsync(
                           session,
                           new AgentRequest(requestText),
                           cancellationToken))
        {
            switch (agentEvent)
            {
                case AgentTextDelta delta:
                    streamedText.Append(delta.Text);
                    break;
                case AgentMessage { Role: AgentMessageRole.Assistant } message:
                    streamedText.Append(message.Text);
                    break;
                case AgentApprovalRequested:
                    throw new B1AgentParticipationException(
                        "The Agent requested an approval; B1 participation does not auto-authorize provider actions.");
                case AgentError error:
                    throw new B1AgentParticipationException($"The Agent runtime failed: {error.Message}");
                case AgentTurnCompleted completed:
                    if (completed.Result.FinalStatus != AgentSessionStatus.Completed)
                        throw new B1AgentParticipationException(
                            $"The Agent turn ended with status {completed.Result.FinalStatus}.");
                    finalText = completed.Result.FinalText;
                    break;
            }
        }

        finalText = string.IsNullOrWhiteSpace(finalText) ? streamedText.ToString() : finalText;
        if (string.IsNullOrWhiteSpace(finalText))
            throw new B1AgentParticipationException("The Agent completed without a result Claim.");
        return finalText.Trim();
    }

    private static string BuildPrompt(string workContract, string? prompt)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Execute the bounded Project Assignment below.");
        builder.AppendLine("Your output is a work result for review, not an accepted Project fact.");
        builder.AppendLine();
        builder.Append("Assignment contract: ").AppendLine(workContract);
        if (!string.IsNullOrWhiteSpace(prompt))
        {
            builder.AppendLine();
            builder.AppendLine("Additional task direction:");
            builder.AppendLine(prompt.Trim());
        }
        builder.AppendLine();
        builder.AppendLine("Return a concise result report and identify unresolved issues explicitly.");
        return builder.ToString();
    }
}
