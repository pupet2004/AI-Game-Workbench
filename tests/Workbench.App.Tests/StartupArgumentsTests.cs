namespace Workbench.App.Tests;

public sealed class StartupArgumentsTests
{
    [Fact]
    public void Reads_project_path_from_separate_argument()
    {
        var path = StartupArguments.TryGetProjectPath(["--project", "C:\\Projects\\Counter"]);

        Assert.Equal("C:\\Projects\\Counter", path);
    }

    [Fact]
    public void Reads_project_path_from_equals_argument()
    {
        var path = StartupArguments.TryGetProjectPath(["--project=C:\\Projects\\Counter"]);

        Assert.Equal("C:\\Projects\\Counter", path);
    }

    [Fact]
    public void Ignores_unrelated_arguments()
    {
        var path = StartupArguments.TryGetProjectPath(["--reset-local-data"]);

        Assert.Null(path);
    }

    [Fact]
    public void Ignores_project_switch_without_a_value()
    {
        var path = StartupArguments.TryGetProjectPath(["--project"]);

        Assert.Null(path);
    }
}
