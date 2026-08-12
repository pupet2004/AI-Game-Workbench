using Workbench.App.Tests.Support;

namespace Workbench.App.Tests;

public sealed class TemporaryDirectoryTests
{
    [Fact]
    public async Task Dispose_retries_a_transient_windows_file_lock()
    {
        var temporary = new TemporaryDirectory("locked");
        var file = Path.Combine(temporary.Path, "workbench.db");
        await File.WriteAllTextAsync(file, "locked");
        var stream = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var release = Task.Run(async () =>
        {
            await Task.Delay(100);
            await stream.DisposeAsync();
        });

        temporary.Dispose();
        await release;

        Assert.False(Directory.Exists(temporary.Path));
    }
}
