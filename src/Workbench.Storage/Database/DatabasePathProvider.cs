namespace Workbench.Storage.Database;

public static class DatabasePathProvider
{
    public static string GetDefaultDatabasePath()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AI Game Workbench");

        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "workbench.db");
    }
}
