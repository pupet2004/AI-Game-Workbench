using Workbench.Core.Projects;

namespace Workbench.Project.Detection;

public sealed class ProjectDetector
{
    public ProjectDetectionResult Detect(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        var directory = new DirectoryInfo(rootPath);
        if (!directory.Exists)
        {
            throw new DirectoryNotFoundException($"Project directory does not exist: {rootPath}");
        }

        var projectType = DetectProjectType(directory);
        return new ProjectDetectionResult(directory.FullName, directory.Name, projectType);
    }

    private static ProjectType DetectProjectType(DirectoryInfo directory)
    {
        if (File.Exists(Path.Combine(directory.FullName, "project.godot")))
        {
            return ProjectType.Godot;
        }

        if (directory.EnumerateFiles("*.uproject", SearchOption.TopDirectoryOnly).Any())
        {
            return ProjectType.Unreal;
        }

        if (Directory.Exists(Path.Combine(directory.FullName, "Assets")) &&
            Directory.Exists(Path.Combine(directory.FullName, "ProjectSettings")))
        {
            return ProjectType.Unity;
        }

        return ProjectType.Generic;
    }
}
