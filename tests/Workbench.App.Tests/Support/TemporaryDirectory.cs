namespace Workbench.App.Tests.Support;

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory(string name = "project")
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AI.Game.Workbench.App.Tests", Guid.NewGuid().ToString("N"), name);
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(Path, recursive: true);
        }
    }
}
