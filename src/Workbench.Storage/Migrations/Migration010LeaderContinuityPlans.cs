using Microsoft.Data.Sqlite;
namespace Workbench.Storage.Migrations;
internal static class Migration010LeaderContinuityPlans
{
    public const long Version = 10;
    public static async Task ApplyAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    { var command=connection.CreateCommand(); command.Transaction=transaction; command.CommandText="""CREATE TABLE IF NOT EXISTS leader_epoch_continuity_plans (epoch_id TEXT PRIMARY KEY,total_max_utf8_bytes INTEGER NOT NULL CHECK(total_max_utf8_bytes > 0),created_at TEXT NOT NULL,FOREIGN KEY(epoch_id) REFERENCES leader_session_epochs(id) ON DELETE CASCADE); CREATE TABLE IF NOT EXISTS leader_epoch_continuity_selections (epoch_id TEXT NOT NULL,ordinal INTEGER NOT NULL,material_kind TEXT NOT NULL,material_ref TEXT NOT NULL,max_utf8_bytes INTEGER NOT NULL CHECK(max_utf8_bytes > 0),selector_json TEXT NULL,PRIMARY KEY(epoch_id,ordinal),FOREIGN KEY(epoch_id) REFERENCES leader_epoch_continuity_plans(epoch_id) ON DELETE CASCADE); PRAGMA user_version=10;"""; await command.ExecuteNonQueryAsync(cancellationToken); }
}
