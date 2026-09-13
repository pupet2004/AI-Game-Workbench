using Xunit;

namespace Workbench.Runtime.Tests;

internal sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute(string environmentVariable)
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(environmentVariable),
                "1",
                StringComparison.Ordinal))
        {
            Skip = $"Set {environmentVariable}=1 to run this live test.";
        }
    }
}
