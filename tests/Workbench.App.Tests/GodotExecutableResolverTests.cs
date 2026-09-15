using Workbench.App.Services;

namespace Workbench.App.Tests;

public sealed class GodotExecutableResolverTests
{
    [Fact]
    public void User_provided_godot_path_is_detected()
    {
        using var folder = new Support.TemporaryDirectory("godot-resolver");
        var path = Path.Combine(folder.Path, "Godot_v4.6.3-stable.exe");
        File.WriteAllText(Path.Combine(folder.Path, "Godot_v4.6.3-stable_console.exe"), "test console");

        Assert.Equal(
            Path.GetFullPath(Path.Combine(folder.Path, "Godot_v4.6.3-stable_console.exe")),
            GodotExecutableResolver.Resolve(folder.Path));
    }
}
