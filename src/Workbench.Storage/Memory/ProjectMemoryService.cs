namespace Workbench.Storage.Memory;

/// <summary>Workbench-owned candidate and certification boundary; no agent runtime dependency.</summary>
public sealed class ProjectMemoryService(ProjectActivityRepository activities, ProjectMemoryRepository memories, TimeProvider? timeProvider = null)
{
    private readonly ProjectActivityRepository _activities = activities;
    private readonly ProjectMemoryRepository _memories = memories;
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    public async Task<ProjectActivityEvent> RecordManualActivityAsync(Guid projectId, string eventType, string summary, string? sourceRef, CancellationToken cancellationToken=default)
    { ValidateShort(summary,1000,"summary"); var now=_time.GetUtcNow(); var item=new ProjectActivityEvent(Guid.NewGuid(),projectId,eventType,summary,"Manual",sourceRef,now,now); await _activities.AddAsync(item,cancellationToken); return item; }
    public Task<ProjectMemoryItem> CreateCandidateAsync(Guid projectId,string topic,string content,IReadOnlyList<ProjectMemorySource> sources,CancellationToken cancellationToken=default) => CreateAsync(projectId,topic,content,"Candidate",sources,cancellationToken);
    public async Task<ProjectMemoryItem> CreateAgentMemoryAsync(Guid projectId,string topic,string content,string layer,IReadOnlyList<ProjectMemorySource> sources,CancellationToken cancellationToken=default)
    { if (layer!="Candidate") throw new InvalidOperationException("Agents may only create Candidate memory."); return await CreateCandidateAsync(projectId,topic,content,sources,cancellationToken); }
    public Task<IReadOnlyList<ProjectMemoryItem>> GetPendingCandidatesAsync(Guid projectId,CancellationToken cancellationToken=default)=>_memories.GetAsync(projectId,"Candidate","Active",cancellationToken);
    public Task<IReadOnlyList<ProjectMemoryItem>> GetFormalMemoriesAsync(Guid projectId,CancellationToken cancellationToken=default)=>_memories.GetAsync(projectId,"Formal","Active",cancellationToken);
    public Task<IReadOnlyList<ProjectMemoryItem>> GetLearnedMemoriesAsync(Guid projectId,CancellationToken cancellationToken=default)=>_memories.GetAsync(projectId,"Learned","Active",cancellationToken);
    public Task<ProjectMemoryItem?> GetMemoryItemAsync(Guid id,CancellationToken cancellationToken=default)=>_memories.GetAsync(id,cancellationToken);
    public Task<IReadOnlyList<ProjectMemorySource>> GetSourcesAsync(Guid id,CancellationToken cancellationToken=default)=>_memories.GetSourcesAsync(id,cancellationToken);
    public Task<ProjectMemoryItem> AcceptCandidateAsync(Guid id,CancellationToken cancellationToken=default)=>AcceptAsync(id,null,cancellationToken);
    public Task<ProjectMemoryItem> EditAndAcceptCandidateAsync(Guid id,string content,CancellationToken cancellationToken=default)=>AcceptAsync(id,content,cancellationToken);
    public async Task RejectCandidateAsync(Guid id,CancellationToken cancellationToken=default){var item=await RequireCertifiableAsync(id,cancellationToken);await _memories.RejectCandidateAsync(item.Id,item.ProjectId,_time.GetUtcNow(),cancellationToken);}
    private async Task<ProjectMemoryItem> AcceptAsync(Guid id,string? replacement,CancellationToken cancellationToken){if(replacement is not null)ValidateShort(replacement,8000,"content");var candidate=await RequireCertifiableAsync(id,cancellationToken);return await _memories.CertifyCandidateAsync(candidate.Id,candidate.ProjectId,replacement,_time.GetUtcNow(),cancellationToken);}
    private async Task<ProjectMemoryItem> CreateAsync(Guid projectId,string topic,string content,string layer,IReadOnlyList<ProjectMemorySource> sources,CancellationToken cancellationToken){ValidateShort(topic,200,"topic");ValidateShort(content,8000,"content");if(sources.Count==0)throw new ArgumentException("Memory requires provenance.",nameof(sources));var now=_time.GetUtcNow();var item=new ProjectMemoryItem(Guid.NewGuid(),projectId,layer,topic,content,"Active",now,now,null);await _memories.AddAsync(item,sources,cancellationToken);return item;}
    private async Task<ProjectMemoryItem> RequireCertifiableAsync(Guid id,CancellationToken cancellationToken){var item=await _memories.GetAsync(id,cancellationToken)??throw new KeyNotFoundException("Memory item was not found.");if(item.Layer!="Candidate")throw new InvalidOperationException("Only candidate memory can be certified.");if(item.Status!="Active")throw new MemoryCertificationConflictException("Candidate has already been processed.");return item;}
    private static void ValidateShort(string value,int max,string name){ArgumentException.ThrowIfNullOrWhiteSpace(value);if(System.Text.Encoding.UTF8.GetByteCount(value)>max)throw new ArgumentException($"{name} exceeds {max} UTF-8 bytes.",name);}
}
