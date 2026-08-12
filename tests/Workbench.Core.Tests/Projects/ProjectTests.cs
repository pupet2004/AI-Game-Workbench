using Workbench.Core.Projects;

namespace Workbench.Core.Tests.Projects;

public sealed class ProjectTests
{
    [Fact]
    public void Project_preserves_expected_fields()
    {
        var id = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var lastOpenedAt = DateTimeOffset.UtcNow;

        var project = new Project(id, "Demo", "C:/Projects/Demo", ProjectType.Godot, "C:/Projects", createdAt, lastOpenedAt);

        Assert.Equal(id, project.Id);
        Assert.Equal("Demo", project.Name);
        Assert.Equal("C:/Projects/Demo", project.RootPath);
        Assert.Equal(ProjectType.Godot, project.Type);
        Assert.Equal("C:/Projects", project.GitRoot);
        Assert.Equal(createdAt, project.CreatedAt);
        Assert.Equal(lastOpenedAt, project.LastOpenedAt);
    }

    [Fact]
    public void Project_allows_null_git_root()
    {
        var project = new Project(Guid.NewGuid(), "Demo", "C:/Projects/Demo", ProjectType.Generic, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        Assert.Null(project.GitRoot);
    }

    [Fact]
    public void ProjectType_is_preserved()
    {
        var project = new Project(Guid.NewGuid(), "Demo", "C:/Projects/Demo", ProjectType.Unreal, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        Assert.Equal(ProjectType.Unreal, project.Type);
    }
}
