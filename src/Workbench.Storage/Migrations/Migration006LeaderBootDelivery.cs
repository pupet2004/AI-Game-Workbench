using Microsoft.Data.Sqlite;

namespace Workbench.Storage.Migrations;

internal static class Migration006LeaderBootDelivery
{
    public const long Version = 6;

    public static async Task ApplyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            ALTER TABLE leader_session_epochs
            ADD COLUMN boot_context_delivered_at TEXT NULL;

            UPDATE leader_session_epochs AS epoch
            SET boot_context_delivered_at = (
                SELECT MIN(message.created_at)
                FROM leader_messages AS message
                WHERE message.epoch_id = epoch.id AND message.role = 'user')
            WHERE EXISTS (
                SELECT 1
                FROM leader_messages AS message
                WHERE message.epoch_id = epoch.id AND message.role = 'user');

            PRAGMA user_version = 6;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
