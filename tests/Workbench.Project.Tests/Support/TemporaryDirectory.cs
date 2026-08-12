namespace Workbench.Project.Tests.Support;

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory(string? name = null)
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AI.Game.Workbench.Tests", Guid.NewGuid().ToString("N"), name ?? "project");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                    }
                    Directory.Delete(Path, recursive: true);
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
