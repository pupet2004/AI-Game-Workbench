namespace Workbench.Runtime.Providers.Codex;

public sealed record CodexAppServerOptions
{
    public CodexAppServerOptions(
        string executablePath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan? operationTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        ExecutablePath = executablePath;
        Arguments = arguments.ToArray();
        WorkingDirectory = workingDirectory;
        OperationTimeout = operationTimeout ?? TimeSpan.FromSeconds(30);

        if (OperationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(operationTimeout));
        }
    }

    public string ExecutablePath { get; }

    public IReadOnlyList<string> Arguments { get; }

    public string WorkingDirectory { get; }

    public TimeSpan OperationTimeout { get; }
}
