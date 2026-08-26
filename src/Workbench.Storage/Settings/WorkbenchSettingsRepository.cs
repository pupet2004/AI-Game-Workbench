using Workbench.Core.Leaders;
using Workbench.Storage.Database;

namespace Workbench.Storage.Settings;

public sealed class WorkbenchSettingsRepository(WorkbenchDatabase database)
{
    private const string RotationPolicyKey = "leader_session_rotation_policy";
    private const string LeaderAuthorityKey = "leader_authority_mode";
    private const string AgentRuntimePrefix = "agent_runtime.";
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

    public async Task<LeaderAuthorityMode> GetLeaderAuthorityModeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection(); await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand(); command.CommandText = "SELECT value FROM workbench_settings WHERE key = $key;"; command.Parameters.AddWithValue("$key", LeaderAuthorityKey);
        var value = await command.ExecuteScalarAsync(cancellationToken) as string;
        return value is null ? LeaderAuthorityMode.Balanced : ParseLeaderAuthorityMode(value);
    }

    public async Task SaveLeaderAuthorityModeAsync(LeaderAuthorityMode mode, CancellationToken cancellationToken = default)
    {
        ValidateLeaderAuthorityMode(mode);
        await using var connection = _database.CreateConnection(); await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand(); command.CommandText = "INSERT INTO workbench_settings (key, value) VALUES ($key, $value) ON CONFLICT(key) DO UPDATE SET value = excluded.value;";
        command.Parameters.AddWithValue("$key", LeaderAuthorityKey); command.Parameters.AddWithValue("$value", mode.ToString()); await command.ExecuteNonQueryAsync(cancellationToken);
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

    public async Task<AgentRuntimeSettings> GetAgentRuntimeSettingsAsync(
        string agentId,
        bool defaultEnabled = false,
        CancellationToken cancellationToken = default)
    {
        ValidateAgentId(agentId);
        var enabled = await GetValueAsync(GetAgentRuntimeKey(agentId, "enabled"), cancellationToken);
        var executablePath = await GetValueAsync(GetAgentRuntimeKey(agentId, "executable_path"), cancellationToken);
        return new AgentRuntimeSettings(
            agentId,
            enabled is null ? defaultEnabled : bool.TryParse(enabled, out var value) && value,
            string.IsNullOrWhiteSpace(executablePath) ? null : executablePath);
    }

    public async Task SaveAgentRuntimeSettingsAsync(
        AgentRuntimeSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ValidateAgentId(settings.AgentId);
        await SaveValueAsync(GetAgentRuntimeKey(settings.AgentId, "enabled"), settings.IsEnabled.ToString(), cancellationToken);
        await SaveValueAsync(GetAgentRuntimeKey(settings.AgentId, "executable_path"), settings.NormalizedExecutablePath ?? string.Empty, cancellationToken);
    }

    private async Task<string?> GetValueAsync(string key, CancellationToken cancellationToken)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM workbench_settings WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    private async Task SaveValueAsync(string key, string value, CancellationToken cancellationToken)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO workbench_settings (key, value) VALUES ($key, $value) ON CONFLICT(key) DO UPDATE SET value = excluded.value;";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string GetAgentRuntimeKey(string agentId, string setting) => $"{AgentRuntimePrefix}{agentId}.{setting}";

    private static void ValidateAgentId(string agentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        if (!agentId.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
        {
            throw new ArgumentException("Agent id can contain only ASCII letters, digits, hyphens, and underscores.", nameof(agentId));
        }
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

    internal static LeaderAuthorityMode ParseLeaderAuthorityMode(string value)
    {
        if (!Enum.TryParse<LeaderAuthorityMode>(value, false, out var mode) || !Enum.IsDefined(mode)) throw new InvalidDataException($"Unknown leader authority mode: {value}.");
        return mode;
    }

    internal static void ValidateLeaderAuthorityMode(LeaderAuthorityMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
    }
}
