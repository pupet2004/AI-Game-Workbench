using System.Text.Json;
using Workbench.Core.Continuity;
using Workbench.Storage.Continuity;

namespace Workbench.App.Continuity;

public sealed class B1WorkerExecutionBridgeService(B1WorkerBridgeRepository repository, B1AuthorityRepository authorityRepository, B1EvidenceRepository evidenceRepository)
{
    private readonly B1WorkerBridgeRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly B1AuthorityRepository _authorityRepository = authorityRepository ?? throw new ArgumentNullException(nameof(authorityRepository));
    private readonly B1EvidenceRepository _evidenceRepository = evidenceRepository ?? throw new ArgumentNullException(nameof(evidenceRepository));

    public async Task<B1WorkerTaskLink> LinkWorkerTaskAsync(
        ProjectRef project,
        AssignmentRef assignment,
        RevisionRef assignmentRevision,
        Guid workerTaskId,
        Guid workerTaskRevisionId,
        CancellationToken cancellationToken = default)
    {
        var state = await _authorityRepository.LoadProjectStateAsync(project, cancellationToken);
        var projection = B1Projector.Build(state);
        if (!projection.AcceptedProjectState.Assignments.ContainsKey(assignment))
            throw new InvalidOperationException("The Assignment is not part of the accepted Project State.");
        if (!projection.AcceptedProjectState.Revisions.ContainsKey(assignmentRevision))
            throw new InvalidOperationException("The Assignment Revision is not part of the accepted Project State.");
        return await _repository.LinkTaskAsync(new B1WorkerTaskLink(project, assignment, assignmentRevision, workerTaskId, workerTaskRevisionId, DateTimeOffset.UtcNow), cancellationToken);
    }

    public Task<B1WorkerExecutionLink> LinkWorkerExecutionAsync(ProjectRef project, AttemptRef attempt, Guid workerExecutionId, B1WorkerExecutionRelationKind relationKind, CancellationToken cancellationToken = default) =>
        _repository.LinkExecutionAsync(new B1WorkerExecutionLink(project, attempt, workerExecutionId, relationKind, DateTimeOffset.UtcNow), cancellationToken);

    public Task<B1WorkerSessionLink> LinkAgentSessionAsync(B1WorkerSessionLink link, CancellationToken cancellationToken = default) =>
        _repository.LinkSessionAsync(link, cancellationToken);

    public Task<B1WorkerExecutionEvidence> RecordVerificationEvidenceAsync(
        ProjectRef project,
        EvidenceRef evidenceRef,
        Guid workerExecutionId,
        Guid workerTaskId,
        Guid workerTaskRevisionId,
        AttemptRef attempt,
        string verificationResult,
        object verification,
        CancellationToken cancellationToken = default) =>
        RecordEvidenceCoreAsync(project, evidenceRef, workerExecutionId, workerTaskId, workerTaskRevisionId, attempt, verificationResult, verification, cancellationToken);

    private async Task<B1WorkerExecutionEvidence> RecordEvidenceCoreAsync(
        ProjectRef project, EvidenceRef evidenceRef, Guid workerExecutionId, Guid workerTaskId, Guid workerTaskRevisionId,
        AttemptRef attempt, string verificationResult, object verification, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(verification);
        await _evidenceRepository.RecordAsync(new EvidenceRecord(project, evidenceRef, EvidenceKind.TestRun,
            $"workbench:worker-execution/{workerExecutionId:N}/verification", null, null, json.Length, DateTimeOffset.UtcNow), cancellationToken);
        return await _repository.RecordEvidenceAsync(new B1WorkerExecutionEvidence(
            project, evidenceRef, workerExecutionId, workerTaskId, workerTaskRevisionId, attempt,
            verificationResult, json, DateTimeOffset.UtcNow), cancellationToken);
    }

    public Task<B1WorkerTaskLink?> GetWorkerTaskLinkAsync(ProjectRef project, Guid workerTaskId, CancellationToken cancellationToken = default) =>
        _repository.GetTaskLinkAsync(project, workerTaskId, cancellationToken);

    public Task<IReadOnlyList<B1WorkerExecutionLink>> ListWorkerExecutionsAsync(ProjectRef project, AttemptRef attempt, CancellationToken cancellationToken = default) =>
        _repository.ListExecutionLinksAsync(project, attempt, cancellationToken);

    public Task<B1WorkerExecutionEvidence?> GetVerificationEvidenceAsync(ProjectRef project, Guid workerExecutionId, CancellationToken cancellationToken = default) =>
        _repository.GetEvidenceAsync(project, workerExecutionId, cancellationToken);

    public Task<B1WorkerExecutionLink?> GetWorkerExecutionLinkAsync(ProjectRef project, Guid workerExecutionId, CancellationToken cancellationToken = default) =>
        _repository.GetExecutionLinkAsync(project, workerExecutionId, cancellationToken);

    public Task<B1WorkerSessionLink?> GetWorkerSessionLinkAsync(ProjectRef project, Guid workerExecutionId, CancellationToken cancellationToken = default) =>
        _repository.GetSessionLinkAsync(project, workerExecutionId, cancellationToken);
}
