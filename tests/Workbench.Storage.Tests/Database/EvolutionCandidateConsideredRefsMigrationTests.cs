using Microsoft.Data.Sqlite;
using Workbench.Storage.Database;

namespace Workbench.Storage.Tests.Database;

public sealed class EvolutionCandidateConsideredRefsMigrationTests
{
    [Fact]
    public async Task V29_removes_pending_governance_language_from_accepted_contributions()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await HistoricalMigrationTestDatabase.InitializeThroughAsync(database, 28);

        await using (var connection = database.CreateConnection())
        {
            await connection.OpenAsync();
            await ExecuteAsync(connection, "INSERT INTO projects(id,name,root_path,project_type,created_at,last_opened_at) VALUES('p','P','C:/P',0,'2026-09-08T00:00:00Z','2026-09-08T00:00:00Z');");
            await ExecuteAsync(connection, "INSERT INTO b1_project_governance(project_id,bootstrap_user_principal,origin,adopted_at,last_commit_sequence) VALUES('p','user','Created',NULL,1);");
            await ExecuteAsync(connection, "INSERT INTO b1_authority_decisions(id,project_id,project_commit_sequence,command_kind,deciding_authority_kind,deciding_user_principal,deciding_actor_id,created_at) VALUES('d','p',1,'AuthorAcceptedState','UserPrincipal','user',NULL,'2026-09-08T00:01:00Z');");
            await ExecuteAsync(connection, "INSERT INTO b1_accepted_state_contributions(id,project_id,statement,scope_kind,scope_project_id,scope_responsibility_id,scope_assignment_id,supersedes_contribution_id,authority_decision_id,source_claim_id) VALUES('c','p','因果编号只用于追踪。该规则拟作为正式世界规则，待用户确认后才进入 Accepted Project State。','Project','p',NULL,NULL,NULL,'d',NULL);");
        }

        await database.InitializeAsync();

        await using var verified = database.CreateConnection();
        await verified.OpenAsync();
        Assert.Equal(30L, await ScalarAsync<long>(verified, "PRAGMA user_version;"));
        Assert.Equal("因果编号只用于追踪。", await ScalarAsync<string>(verified, "SELECT statement FROM b1_accepted_state_contributions WHERE id='c';"));
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> ScalarAsync<T>(SqliteConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)(await command.ExecuteScalarAsync())!;
    }
}
