namespace Workbench.App.Services;

public static class GodotExecutableResolver
{
    private const string ConfiguredEnvironmentVariable = "WORKBENCH_GODOT_EXECUTABLE";
    private static readonly string[] CommonWindowsLocations =
    [
        @"E:\Godot\_v4.6.3-stable\_win64.exe",
        @"E:\Godot_v4.6.3-stable_win64.exe"
    ];

    public static string? Resolve(string? configuredPath = null)
    {
        var configured = configuredPath;
        if (string.IsNullOrWhiteSpace(configured))
            configured = Environment.GetEnvironmentVariable(ConfiguredEnvironmentVariable);
        var resolved = ResolveCandidate(configured);
        if (resolved is not null)
            return resolved;

        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (var name in new[] { "godot.exe", "godot_console.exe", "Godot_v4.6.3-stable_win64.exe" })
                {
                    var candidate = Path.Combine(directory.Trim(), name);
                    resolved = ResolveCandidate(candidate);
                    if (resolved is not null)
                        return resolved;
                }
            }
        }

        foreach (var location in CommonWindowsLocations)
        {
            resolved = ResolveCandidate(location);
            if (resolved is not null)
                return resolved;
        }

        return null;
    }

    private static bool IsExecutable(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        File.Exists(path) &&
        string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase);

    private static string? ResolveCandidate(string? path)
    {
        if (IsExecutable(path))
            return Path.GetFullPath(path!);

        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
        {
            var console = Directory.EnumerateFiles(path, "*console.exe", SearchOption.TopDirectoryOnly)
                .FirstOrDefault();
            if (IsExecutable(console))
                return Path.GetFullPath(console!);

            var executable = Directory.EnumerateFiles(path, "*.exe", SearchOption.TopDirectoryOnly)
                .FirstOrDefault();
            if (IsExecutable(executable))
                return Path.GetFullPath(executable!);
        }

        return null;
    }
}
