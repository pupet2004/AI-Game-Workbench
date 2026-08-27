namespace Workbench.App.Skills;

public enum WorkbenchSkillRole
{
    Leader,
    Worker
}

public static class WorkbenchSkillCatalog
{
    public static string Load(WorkbenchSkillRole role)
    {
        var folder = role switch
        {
            WorkbenchSkillRole.Leader => "workbench-leader",
            WorkbenchSkillRole.Worker => "workbench-worker",
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
        };

        var relativePath = Path.Combine("skills", folder, "SKILL.md");
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, relativePath),
            Path.Combine(Directory.GetCurrentDirectory(), relativePath)
        };

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate).Trim();
            }
        }

        throw new FileNotFoundException(
            $"The Workbench {role} Skill is not installed with the application.",
            candidates[0]);
    }
}
