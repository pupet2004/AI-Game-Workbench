using Microsoft.Data.Sqlite;

namespace Workbench.Storage.Database;

public sealed class WorkbenchDatabase
{
    private readonly string _connectionString;

    public WorkbenchDatabase(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        DatabasePath = databasePath;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true,
            Pooling = false
        }.ToString();
    }

    public string DatabasePath { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var directory = Path.GetDirectoryName(DatabasePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await MigrationRunner.RunAsync(connection, cancellationToken);
        }
        catch (DatabaseInitializationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new DatabaseInitializationException("Unable to initialize the Workbench database.", exception);
        }
    }

    public SqliteConnection CreateConnection() => new(_connectionString);
}
