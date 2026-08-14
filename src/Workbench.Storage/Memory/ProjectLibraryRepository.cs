using System.Globalization;
using Microsoft.Data.Sqlite;
using Workbench.Storage.Database;
using Workbench.Storage.Migrations;

namespace Workbench.Storage.Memory;

public sealed class ProjectLibraryRepository(WorkbenchDatabase database)
{
    private readonly WorkbenchDatabase _database = database;
    public async Task SubmitAsync(LibrarySubmission submission, CancellationToken cancellationToken = default)
    {
        Validate(submission);
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT OR IGNORE INTO project_library_entries (id,project_id,source_session_id,task_id,category,topic,summary,source_reference,created_at) VALUES ($id,$project,$session,$task,$category,$topic,$summary,$source,$created);";
            command.Parameters.AddWithValue("$id", submission.SubmissionId.ToString());
            command.Parameters.AddWithValue("$project", submission.ProjectId.ToString());
            command.Parameters.AddWithValue("$session", submission.SourceSessionId.ToString());
            command.Parameters.AddWithValue("$task", (object?)submission.TaskId?.ToString() ?? DBNull.Value);
            command.Parameters.AddWithValue("$category", submission.Category);
            command.Parameters.AddWithValue("$topic", submission.Topic);
            command.Parameters.AddWithValue("$summary", submission.Summary);
            command.Parameters.AddWithValue("$source", (object?)submission.SourceReference ?? DBNull.Value);
            command.Parameters.AddWithValue("$created", submission.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
            await command.ExecuteNonQueryAsync(cancellationToken);
            await Migration011ProjectLibraryEvolution.ImportLegacyEntriesAsync(connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
    public async Task<IReadOnlyList<ProjectLibraryEntry>> BrowseAsync(Guid projectId, string? category = null, string? topic = null, string? text = null, CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection(); await connection.OpenAsync(cancellationToken); var command = connection.CreateCommand();
        command.CommandText = "SELECT id,project_id,source_session_id,task_id,category,topic,summary,source_reference,created_at FROM project_library_entries WHERE project_id=$project AND ($category IS NULL OR category=$category) AND ($topic IS NULL OR topic=$topic) AND ($text IS NULL OR instr(lower(category || ' ' || topic || ' ' || summary), lower($text)) > 0) ORDER BY created_at DESC, id DESC;";
        command.Parameters.AddWithValue("$project", projectId.ToString()); command.Parameters.AddWithValue("$category", (object?)category ?? DBNull.Value); command.Parameters.AddWithValue("$topic", (object?)topic ?? DBNull.Value); command.Parameters.AddWithValue("$text", (object?)text ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken); var entries = new List<ProjectLibraryEntry>();
        while (await reader.ReadAsync(cancellationToken)) entries.Add(new(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), Guid.Parse(reader.GetString(2)), reader.IsDBNull(3) ? null : Guid.Parse(reader.GetString(3)), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7), DateTimeOffset.Parse(reader.GetString(8), CultureInfo.InvariantCulture)));
        return entries;
    }
    private static void Validate(LibrarySubmission submission)
    {
        if (submission.SubmissionId == Guid.Empty || submission.ProjectId == Guid.Empty || submission.SourceSessionId == Guid.Empty) throw new ArgumentException("Library identity is required.");
        ValidateText(submission.Category, 100, nameof(submission.Category)); ValidateText(submission.Topic, 200, nameof(submission.Topic)); ValidateText(submission.Summary, 1000, nameof(submission.Summary));
        if (submission.SourceReference is not null && submission.SourceReference.Length > 500) throw new ArgumentException("Source reference is too long.", nameof(submission.SourceReference));
    }
    private static void ValidateText(string value, int max, string name) { if (string.IsNullOrWhiteSpace(value) || value.Length > max) throw new ArgumentException("Library text is invalid.", name); }
}
