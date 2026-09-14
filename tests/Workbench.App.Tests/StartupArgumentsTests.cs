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

    [Fact]
    public void Reads_database_path_from_separate_argument()
    {
        var path = StartupArguments.TryGetDatabasePath(["--database", "C:\\Temp\\Workbench\\isolated.db"]);

        Assert.Equal("C:\\Temp\\Workbench\\isolated.db", path);
    }

    [Fact]
    public void Reads_database_path_from_equals_argument()
    {
        var path = StartupArguments.TryGetDatabasePath(["--database=C:\\Temp\\Workbench\\isolated.db"]);

        Assert.Equal("C:\\Temp\\Workbench\\isolated.db", path);
    }

    [Fact]
    public void Ignores_database_switch_without_a_value()
    {
        var path = StartupArguments.TryGetDatabasePath(["--database"]);

        Assert.Null(path);
    }
}
