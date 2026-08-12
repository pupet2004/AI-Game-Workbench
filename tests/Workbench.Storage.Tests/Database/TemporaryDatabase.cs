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

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        return ValueTask.CompletedTask;
    }
}
