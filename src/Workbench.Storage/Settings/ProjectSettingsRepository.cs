using Workbench.Core.Leaders;
using Workbench.Storage.Database;

namespace Workbench.Storage.Settings;

public sealed class ProjectSettingsRepository(WorkbenchDatabase database)
{
    private readonly WorkbenchDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    public async Task<LeaderSessionRotationPolicy?> GetLeaderSessionRotationPolicyOverrideAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT leader_session_rotation_policy FROM project_settings WHERE project_id = $projectId;";
        command.Parameters.AddWithValue("$projectId", projectId.ToString());
        var value = await command.ExecuteScalarAsync(cancellationToken) as string;
        return value is null ? null : WorkbenchSettingsRepository.ParsePolicy(value);
    }

    public async Task SaveLeaderSessionRotationPolicyOverrideAsync(
        Guid projectId,
        LeaderSessionRotationPolicy? policy,
        CancellationToken cancellationToken = default)
    {
        if (policy is null)
        {
            await ClearLeaderSessionRotationPolicyOverrideAsync(projectId, cancellationToken);
            return;
        }

        WorkbenchSettingsRepository.ValidatePolicy(policy.Value);
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO project_settings (project_id, leader_session_rotation_policy)
            VALUES ($projectId, $policy)
            ON CONFLICT(project_id) DO UPDATE SET leader_session_rotation_policy = excluded.leader_session_rotation_policy;
            """;
        command.Parameters.AddWithValue("$projectId", projectId.ToString());
        command.Parameters.AddWithValue("$policy", policy.Value.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<LeaderAuthorityMode?> GetLeaderAuthorityModeOverrideAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection(); await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand(); command.CommandText = "SELECT leader_authority_mode FROM project_settings WHERE project_id = $projectId;"; command.Parameters.AddWithValue("$projectId", projectId.ToString());
        var value = await command.ExecuteScalarAsync(cancellationToken) as string;
        return value is null ? null : WorkbenchSettingsRepository.ParseLeaderAuthorityMode(value);
    }

    public async Task SaveLeaderAuthorityModeOverrideAsync(Guid projectId, LeaderAuthorityMode? mode, CancellationToken cancellationToken = default)
    {
        if (mode is null) { await ClearLeaderAuthorityModeOverrideAsync(projectId, cancellationToken); return; }
        WorkbenchSettingsRepository.ValidateLeaderAuthorityMode(mode.Value);
        await using var connection = _database.CreateConnection(); await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand(); command.CommandText = "INSERT INTO project_settings (project_id, leader_authority_mode) VALUES ($projectId, $mode) ON CONFLICT(project_id) DO UPDATE SET leader_authority_mode = excluded.leader_authority_mode;";
        command.Parameters.AddWithValue("$projectId", projectId.ToString()); command.Parameters.AddWithValue("$mode", mode.Value.ToString()); await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task ClearLeaderSessionRotationPolicyOverrideAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "UPDATE project_settings SET leader_session_rotation_policy = NULL WHERE project_id = $projectId;";
        command.Parameters.AddWithValue("$projectId", projectId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task ClearLeaderAuthorityModeOverrideAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await using var connection = _database.CreateConnection(); await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand(); command.CommandText = "UPDATE project_settings SET leader_authority_mode = NULL WHERE project_id = $projectId;"; command.Parameters.AddWithValue("$projectId", projectId.ToString()); await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
