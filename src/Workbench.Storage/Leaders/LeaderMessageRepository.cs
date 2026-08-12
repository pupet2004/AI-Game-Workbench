using System.Globalization;
using Microsoft.Data.Sqlite;
using Workbench.Storage.Database;

namespace Workbench.Storage.Leaders;

public sealed class LeaderMessageRepository(WorkbenchDatabase database)
{
    private static readonly HashSet<string> Roles = new(StringComparer.Ordinal) { "user", "assistant" };
    private readonly WorkbenchDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    public async Task<IReadOnlyList<StoredLeaderMessage>> GetAllAsync(
        Guid epochId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, epoch_id, sequence, role, text, created_at
            FROM leader_messages
            WHERE epoch_id = $epochId
            ORDER BY sequence;
            """;
        command.Parameters.AddWithValue("$epochId", epochId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var messages = new List<StoredLeaderMessage>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var role = reader.GetString(3);
            ValidateRole(role);
            messages.Add(new StoredLeaderMessage(
                reader.GetInt64(0), Guid.Parse(reader.GetString(1)), reader.GetInt64(2),
                role, reader.GetString(4), Parse(reader.GetString(5))));
        }

        return messages;
    }

    public async Task<long> GetNextSequenceAsync(Guid epochId, CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await GetNextSequenceAsync(connection, null, epochId, cancellationToken);
    }

    public async Task<StoredLeaderMessage> AppendAsync(
        Guid epochId,
        string role,
        string text,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        ValidateRole(role);
        ArgumentNullException.ThrowIfNull(text);
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var sequence = await GetNextSequenceAsync(connection, transaction, epochId, cancellationToken);
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO leader_messages (epoch_id, sequence, role, text, created_at)
            VALUES ($epochId, $sequence, $role, $text, $createdAt);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$epochId", epochId.ToString());
        command.Parameters.AddWithValue("$sequence", sequence);
        command.Parameters.AddWithValue("$role", role);
        command.Parameters.AddWithValue("$text", text);
        command.Parameters.AddWithValue("$createdAt", Format(createdAt));
        var id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return new StoredLeaderMessage(id, epochId, sequence, role, text, createdAt);
    }

    private static async Task<long> GetNextSequenceAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid epochId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(MAX(sequence), 0) + 1 FROM leader_messages WHERE epoch_id = $epochId;";
        command.Parameters.AddWithValue("$epochId", epochId.ToString());
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static void ValidateRole(string role)
    {
        if (!Roles.Contains(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role), role, "Leader message role must be 'user' or 'assistant'.");
        }
    }

    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
