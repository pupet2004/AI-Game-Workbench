namespace Workbench.Storage.Tests.Database;

public sealed class TemporaryDatabaseTests
{
    [Fact]
    public async Task Dispose_retries_a_transient_windows_file_lock()
    {
        var temporary = new TemporaryDatabase();
        Directory.CreateDirectory(Path.GetDirectoryName(temporary.DatabasePath)!);
        await File.WriteAllTextAsync(temporary.DatabasePath, "locked");
        var stream = new FileStream(temporary.DatabasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var release = Task.Run(async () =>
        {
            await Task.Delay(100);
            await stream.DisposeAsync();
        });

        await temporary.DisposeAsync();
        await release;

        Assert.False(Directory.Exists(Path.GetDirectoryName(temporary.DatabasePath)));
    }
}
