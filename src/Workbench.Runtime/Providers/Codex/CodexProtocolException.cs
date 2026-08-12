namespace Workbench.Runtime.Providers.Codex;

internal sealed class CodexProtocolException : Exception
{
    public CodexProtocolException(string message)
        : base(message)
    {
    }

    public CodexProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
