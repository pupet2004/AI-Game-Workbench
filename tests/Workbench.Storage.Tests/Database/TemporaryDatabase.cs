namespace Workbench.Storage.Tests.Database;

internal sealed class TemporaryDatabase : IAsyncDisposable
{
    private readonly string _directory;

    public TemporaryDatabase()
    {
        _directory = Path.Combine(Path.GetTempPath(), "AI.Game.Workbench.Tests", Guid.NewGuid().ToString("N"));
        DatabasePath = Path.Combine(_directory, "test.db");
    }

    public string DatabasePath { get; }

    public async ValueTask DisposeAsync()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                if (Directory.Exists(_directory))
                {
                    Directory.Delete(_directory, recursive: true);
                }
                return;
            }
            catch (IOException) when (attempt < 19)
            {
                await Task.Delay(25);
            }
            catch (UnauthorizedAccessException) when (attempt < 19)
            {
                await Task.Delay(25);
            }
        }
    }
}
