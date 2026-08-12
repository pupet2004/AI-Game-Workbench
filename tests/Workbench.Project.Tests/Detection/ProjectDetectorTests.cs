using Workbench.Core.Projects;
using Workbench.Project.Detection;
using Workbench.Project.Tests.Support;

namespace Workbench.Project.Tests.Detection;

public sealed class ProjectDetectorTests
{
    [Fact]
    public void Detects_godot_project()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "project.godot"), "");

        var result = new ProjectDetector().Detect(directory.Path);

        Assert.Equal(ProjectType.Godot, result.ProjectType);
    }

    [Fact]
    public void Detects_unreal_project()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "Demo.uproject"), "");

        var result = new ProjectDetector().Detect(directory.Path);

        Assert.Equal(ProjectType.Unreal, result.ProjectType);
    }

    [Fact]
    public void Detects_unity_project()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(directory.Path, "Assets"));
        Directory.CreateDirectory(Path.Combine(directory.Path, "ProjectSettings"));

        var result = new ProjectDetector().Detect(directory.Path);

        Assert.Equal(ProjectType.Unity, result.ProjectType);
    }

    [Fact]
    public void Unknown_folder_is_generic()
    {
        using var directory = new TemporaryDirectory();

        var result = new ProjectDetector().Detect(directory.Path);

        Assert.Equal(ProjectType.Generic, result.ProjectType);
    }

    [Fact]
    public void Godot_wins_if_multiple_markers_exist()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "project.godot"), "");
        File.WriteAllText(Path.Combine(directory.Path, "Demo.uproject"), "");
        Directory.CreateDirectory(Path.Combine(directory.Path, "Assets"));
        Directory.CreateDirectory(Path.Combine(directory.Path, "ProjectSettings"));

        var result = new ProjectDetector().Detect(directory.Path);

        Assert.Equal(ProjectType.Godot, result.ProjectType);
    }

    [Fact]
    public void Suggested_name_comes_from_directory_name()
    {
        using var directory = new TemporaryDirectory("NamedProject");

        var result = new ProjectDetector().Detect(directory.Path);

        Assert.Equal("NamedProject", result.SuggestedName);
    }
}
