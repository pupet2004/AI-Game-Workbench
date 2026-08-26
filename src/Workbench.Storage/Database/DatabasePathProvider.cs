namespace Workbench.Storage.Database;

public static class DatabasePathProvider
{
    public static string GetDefaultDatabasePath()
    {
        var directory = GetDefaultDataDirectory();

        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "workbench.db");
    }

    public static int ResetDefaultData()
    {
        var directory = GetDefaultDataDirectory();
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        var deleted = 0;
        foreach (var path in Directory.EnumerateFiles(directory, "workbench.db*", SearchOption.TopDirectoryOnly))
        {
            File.Delete(path);
            deleted++;
        }

        return deleted;
    }

    private static string GetDefaultDataDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AI Game Workbench");
}
