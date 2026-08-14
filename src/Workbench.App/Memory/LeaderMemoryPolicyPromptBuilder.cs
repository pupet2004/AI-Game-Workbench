using System.Text;
using System.Text.Json;
using Workbench.Core.Memory;
using Workbench.Runtime.Agents;
using Workbench.Storage.Memory;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.Memory;

public static class LeaderMemoryPolicyPromptBuilder
{
    public static AgentRequest Build(CoreProject project, ContinuityMaterialCatalog catalog, ProjectMemoryPreferences preferences)
    {
        var builder = new StringBuilder();
        builder.AppendLine("LEADER MEMORY CONTINUITY POLICY");
        builder.AppendLine("New Brain is not Save Memory, End Day, Daily Summary, Handoff, or Library archival.");
        builder.AppendLine("Every write field is optional. Return null when no write is useful.");
        builder.AppendLine("Select continuity material by descriptor reference only. Do not invent references.");
        builder.AppendLine("Choose by current task, material type, age, size, and user continuity preference. Recent short work may favor raw conversation; older work may favor summaries or handoff.");
        builder.AppendLine("Daily Summary records what happened, why, decisions/tradeoffs, and user feedback; it is not Library. Handoff is only short-term adjacent-session operational context.");
        builder.AppendLine("Library Current Overview describes an object's current factual state; a Library Timeline Node describes its historical evolution.");
        builder.AppendLine("For older projects, explicitly selecting Library + Daily may be more useful than relying on old Raw Conversation. Selection remains descriptor-driven; Library is never included automatically.");
        builder.AppendLine($"Project: {project.Name}");
        builder.AppendLine($"Continuity preference: {preferences.Continuity}");
        builder.AppendLine("CATALOG:");
        foreach (var item in catalog.Materials)
            builder.AppendLine($"{item.Kind}|{item.Reference}|{item.Label}|{item.OccurredAt:O}|{item.Utf8Bytes}");
        builder.AppendLine("Return only the policy JSON object with brain_handoff, daily_summary, total_continuity_budget_utf8_bytes, and continuity_selection.");
        return new AgentRequest(builder.ToString());
    }
}
