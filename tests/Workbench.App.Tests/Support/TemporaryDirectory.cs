namespace Workbench.App.Tests.Support;

internal sealed class TemporaryDirectory : IDisposable
{
    private readonly string _ownedDirectory;

    public TemporaryDirectory(string name = "project")
    {
        _ownedDirectory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "AI.Game.Workbench.App.Tests",
            Guid.NewGuid().ToString("N"));
        Path = System.IO.Path.Combine(_ownedDirectory, name);
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                if (Directory.Exists(_ownedDirectory))
                {
                    foreach (var file in Directory.EnumerateFiles(_ownedDirectory, "*", SearchOption.AllDirectories))
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                    }
                    Directory.Delete(_ownedDirectory, recursive: true);
                }
                return;
            }
            catch (IOException) when (attempt < 19)
            {
                Thread.Sleep(25);
            }
            catch (UnauthorizedAccessException) when (attempt < 19)
            {
                Thread.Sleep(25);
            }
        }
    }
}
