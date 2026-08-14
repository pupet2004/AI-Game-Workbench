using System.Globalization;
using Workbench.Core.Memory;
using Workbench.Storage.Database;

namespace Workbench.Storage.Settings;

public sealed class ProjectMemoryPreferencesRepository(WorkbenchDatabase database, TimeProvider? timeProvider = null)
{
    private readonly WorkbenchDatabase _database = database ?? throw new ArgumentNullException(nameof(database));
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<ProjectMemoryPreferences> GetAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT library_granularity,continuity_mode,time_zone_id,custom_instructions,updated_at FROM project_memory_preferences WHERE project_id=$projectId;";
        command.Parameters.AddWithValue("$projectId", projectId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return new ProjectMemoryPreferences(projectId, LibraryGranularityMode.Balanced, ContinuityMode.Balanced, TimeZoneInfo.Local.Id, null, _timeProvider.GetUtcNow());
        return new ProjectMemoryPreferences(projectId, ParseGranularity(reader.GetString(0)), ParseContinuity(reader.GetString(1)), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }

    public async Task SaveAsync(ProjectMemoryPreferences preferences, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (preferences.ProjectId == Guid.Empty) throw new ArgumentException("Project identity is required.", nameof(preferences));
        ArgumentException.ThrowIfNullOrWhiteSpace(preferences.TimeZoneId);
        if (preferences.CustomInstructions?.Length > 4000) throw new ArgumentException("Custom instructions are too long.", nameof(preferences));
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO project_memory_preferences (project_id,library_granularity,continuity_mode,time_zone_id,custom_instructions,updated_at) VALUES ($projectId,$granularity,$continuity,$timeZoneId,$customInstructions,$updatedAt) ON CONFLICT(project_id) DO UPDATE SET library_granularity=excluded.library_granularity,continuity_mode=excluded.continuity_mode,time_zone_id=excluded.time_zone_id,custom_instructions=excluded.custom_instructions,updated_at=excluded.updated_at;";
        command.Parameters.AddWithValue("$projectId", preferences.ProjectId.ToString());
        command.Parameters.AddWithValue("$granularity", preferences.LibraryGranularity.ToString());
        command.Parameters.AddWithValue("$continuity", preferences.Continuity.ToString());
        command.Parameters.AddWithValue("$timeZoneId", preferences.TimeZoneId);
        command.Parameters.AddWithValue("$customInstructions", (object?)preferences.CustomInstructions ?? DBNull.Value);
        command.Parameters.AddWithValue("$updatedAt", preferences.UpdatedAt.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static LibraryGranularityMode ParseGranularity(string value) => Enum.TryParse<LibraryGranularityMode>(value, out var parsed) && Enum.IsDefined(parsed) ? parsed : throw new InvalidDataException("Unknown library granularity preference.");
    private static ContinuityMode ParseContinuity(string value) => Enum.TryParse<ContinuityMode>(value, out var parsed) && Enum.IsDefined(parsed) ? parsed : throw new InvalidDataException("Unknown continuity preference.");
}
