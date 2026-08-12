using Workbench.Core.Leaders;
using Workbench.Storage.Database;

namespace Workbench.Storage.Settings;

public sealed class WorkbenchSettingsRepository(WorkbenchDatabase database)
{
    private const string RotationPolicyKey = "leader_session_rotation_policy";
    private readonly WorkbenchDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    public async Task<LeaderSessionRotationPolicy> GetLeaderSessionRotationPolicyAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM workbench_settings WHERE key = $key;";
        command.Parameters.AddWithValue("$key", RotationPolicyKey);
        var value = await command.ExecuteScalarAsync(cancellationToken) as string;
        return value is null ? LeaderSessionRotationPolicy.Auto : ParsePolicy(value);
    }

    public async Task SaveLeaderSessionRotationPolicyAsync(
        LeaderSessionRotationPolicy policy,
        CancellationToken cancellationToken = default)
    {
        ValidatePolicy(policy);
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO workbench_settings (key, value) VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$key", RotationPolicyKey);
        command.Parameters.AddWithValue("$value", policy.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static LeaderSessionRotationPolicy ParsePolicy(string value)
    {
        if (!Enum.TryParse<LeaderSessionRotationPolicy>(value, false, out var policy) || !Enum.IsDefined(policy))
        {
            throw new InvalidDataException($"Unknown leader session rotation policy: {value}.");
        }

        return policy;
    }

    internal static void ValidatePolicy(LeaderSessionRotationPolicy policy)
    {
        if (!Enum.IsDefined(policy))
        {
            throw new ArgumentOutOfRangeException(nameof(policy));
        }
    }
}
