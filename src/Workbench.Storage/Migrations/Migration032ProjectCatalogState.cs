using Microsoft.Data.Sqlite;

namespace Workbench.Storage.Migrations;

internal static class Migration032ProjectCatalogState
{
    public const long Version = 32;

    public static async Task ApplyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        foreach (var column in new[]
                 {
                     ("is_visible", "INTEGER NOT NULL DEFAULT 1"),
                     ("setup_required", "INTEGER NOT NULL DEFAULT 0")
                 })
        {
            var check = connection.CreateCommand();
            check.Transaction = transaction;
            check.CommandText = "SELECT 1 FROM pragma_table_info('projects') WHERE name = $name;";
            check.Parameters.AddWithValue("$name", column.Item1);
            if (await check.ExecuteScalarAsync(cancellationToken) is not null)
                continue;

            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"ALTER TABLE projects ADD COLUMN {column.Item1} {column.Item2};";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
