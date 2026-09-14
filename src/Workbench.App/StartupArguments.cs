namespace Workbench.App;

internal static class StartupArguments
{
    public static string? TryGetProjectPath(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            if (string.Equals(argument, "--project", StringComparison.Ordinal))
            {
                return index + 1 < args.Count && !string.IsNullOrWhiteSpace(args[index + 1])
                    ? args[index + 1]
                    : null;
            }

            const string prefix = "--project=";
            if (argument.StartsWith(prefix, StringComparison.Ordinal))
            {
                var value = argument[prefix.Length..].Trim();
                return value.Length == 0 ? null : value;
            }
        }

        return null;
    }

    public static string? TryGetDatabasePath(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            if (string.Equals(argument, "--database", StringComparison.Ordinal))
            {
                return index + 1 < args.Count && !string.IsNullOrWhiteSpace(args[index + 1])
                    ? args[index + 1]
                    : null;
            }

            const string prefix = "--database=";
            if (argument.StartsWith(prefix, StringComparison.Ordinal))
            {
                var value = argument[prefix.Length..].Trim();
                return value.Length == 0 ? null : value;
            }
        }

        return null;
    }
}
