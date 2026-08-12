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
