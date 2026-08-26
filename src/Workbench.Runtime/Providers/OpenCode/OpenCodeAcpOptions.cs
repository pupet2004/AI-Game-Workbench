namespace Workbench.Runtime.Providers.OpenCode;

public sealed record OpenCodeAcpOptions(
    string ExecutablePath,
    string WorkingDirectory,
    TimeSpan RequestTimeout)
{
    public OpenCodeAcpOptions(string executablePath, string workingDirectory)
        : this(executablePath, workingDirectory, TimeSpan.FromMinutes(5))
    {
    }
}
