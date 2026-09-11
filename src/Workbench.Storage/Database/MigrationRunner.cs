using Microsoft.Data.Sqlite;
using Workbench.Storage.Migrations;

namespace Workbench.Storage.Database;

internal static class MigrationRunner
{
    public static async Task RunAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "PRAGMA user_version;";
        var currentVersion = Convert.ToInt64(await versionCommand.ExecuteScalarAsync(cancellationToken));

        if (currentVersion < Migration001Initial.Version)
        {
            await ApplyAsync(
                connection,
                Migration001Initial.ApplyAsync,
                cancellationToken);
        }

        if (currentVersion < Migration002PersistentLeaderSessions.Version)
        {
            await ApplyAsync(
                connection,
                Migration002PersistentLeaderSessions.ApplyAsync,
                cancellationToken);
        }

        if (currentVersion < Migration003LeaderRotationSettings.Version)
        {
            await ApplyAsync(
                connection,
                Migration003LeaderRotationSettings.ApplyAsync,
                cancellationToken);
        }
        if (currentVersion < Migration004ProjectMemoryFoundation.Version)
        {
            await ApplyAsync(connection, Migration004ProjectMemoryFoundation.ApplyAsync, cancellationToken);
        }
        if (currentVersion < Migration005MemorySynthesisJobs.Version)
        {
            await ApplyAsync(connection, Migration005MemorySynthesisJobs.ApplyAsync, cancellationToken);
        }
        if (currentVersion < Migration006LeaderBootDelivery.Version)
        {
            await ApplyAsync(connection, Migration006LeaderBootDelivery.ApplyAsync, cancellationToken);
        }
        if (currentVersion < Migration007LeaderWorkerDelegation.Version)
        {
            await ApplyAsync(connection, Migration007LeaderWorkerDelegation.ApplyAsync, cancellationToken);
        }
        if (currentVersion < Migration008ProjectLibrary.Version)
        {
            await ApplyAsync(connection, Migration008ProjectLibrary.ApplyAsync, cancellationToken);
        }
        if (currentVersion < Migration009MemoryContinuity.Version)
        {
            await ApplyAsync(connection, Migration009MemoryContinuity.ApplyAsync, cancellationToken);
        }
        if (currentVersion < Migration010LeaderContinuityPlans.Version)
        {
            await ApplyAsync(connection, Migration010LeaderContinuityPlans.ApplyAsync, cancellationToken);
        }
        if (currentVersion < Migration011ProjectLibraryEvolution.Version)
        {
            await ApplyAsync(connection, Migration011ProjectLibraryEvolution.ApplyAsync, cancellationToken);
        }
        if (currentVersion < Migration012LeaderReviewState.Version)
        {
            await ApplyWithForeignKeysTemporarilyDisabledAsync(
                connection,
                Migration012LeaderReviewState.ApplyAsync,
                cancellationToken);
        }
        if (currentVersion < Migration013LeaderAuthoritySettings.Version)
        {
            await ApplyAsync(connection, Migration013LeaderAuthoritySettings.ApplyAsync, cancellationToken);
        }
        if (currentVersion < Migration014TypedLeaderReviewState.Version)
        {
            await ApplyAsync(connection, Migration014TypedLeaderReviewState.ApplyAsync, cancellationToken);
        }
        if (currentVersion < Migration015ReviewGateReferenceDecoupling.Version)
        {
            await ApplyWithForeignKeysTemporarilyDisabledAsync(
                connection,
                Migration015ReviewGateReferenceDecoupling.ApplyAsync,
                cancellationToken);
        }
        if (currentVersion < Migration016HistoricalAuthorityRecording.Version)
        {
            await ApplyWithForeignKeysTemporarilyDisabledAsync(
                connection,
                Migration016HistoricalAuthorityRecording.ApplyAsync,
                cancellationToken);
        }
        if (currentVersion < Migration017TypedLeaderReviewBackfill.Version)
        {
            await ApplyAsync(connection, Migration017TypedLeaderReviewBackfill.ApplyAsync, cancellationToken);
        }
        if (currentVersion < Migration018ReviewDecisionSubject.Version)
        {
            await ApplyWithForeignKeysTemporarilyDisabledAsync(
                connection,
                Migration018ReviewDecisionSubject.ApplyAsync,
                cancellationToken);
        }
        if (currentVersion < Migration019ProjectSummary.Version)
        {
            await ApplyAsync(connection, Migration019ProjectSummary.ApplyAsync, cancellationToken);
        }
        if (currentVersion < Migration020ManualContinuitySpine.Version)
        {
            await ApplyAsync(connection, Migration020ManualContinuitySpine.ApplyAsync, cancellationToken);
        }
        if (currentVersion < Migration021LibraryProjectionProvenance.Version)
        {
            await ApplyWithForeignKeysTemporarilyDisabledAsync(
                connection,
                Migration021LibraryProjectionProvenance.ApplyAsync,
                cancellationToken);
        }
        if (currentVersion < Migration022EvidenceRecords.Version)
        {
            await ApplyAsync(connection, Migration022EvidenceRecords.ApplyAsync, cancellationToken);
        }
        if (currentVersion < Migration023ActiveWorkerSlot.Version)
        {
            await ApplyAsync(connection, Migration023ActiveWorkerSlot.ApplyAsync, cancellationToken);
        }
        if (currentVersion < Migration024ConcurrentWorkerSessions.Version)
        {
            await ApplyAsync(connection, Migration024ConcurrentWorkerSessions.ApplyAsync, cancellationToken);
        }
        if (currentVersion < Migration025LibraryTimelineOccurredAt.Version)
        {
            await ApplyAsync(connection, Migration025LibraryTimelineOccurredAt.ApplyAsync, cancellationToken);
        }
        if (currentVersion < Migration026WorkerWorkspaceBaseline.Version)
        {
            await ApplyAsync(connection, Migration026WorkerWorkspaceBaseline.ApplyAsync, cancellationToken);
        }
        if (currentVersion < Migration027B1WorkerBridge.Version)
        {
            await ApplyAsync(connection, Migration027B1WorkerBridge.ApplyAsync, cancellationToken);
        }
        if (currentVersion < Migration028ProjectEvolutionCandidates.Version)
        {
            await ApplyAsync(connection, Migration028ProjectEvolutionCandidates.ApplyAsync, cancellationToken);
        }
        if (currentVersion < Migration029EvolutionCandidateConsideredRefs.Version)
        {
            await ApplyAsync(connection, Migration029EvolutionCandidateConsideredRefs.ApplyAsync, cancellationToken);
        }
        if (currentVersion < Migration030CanonicalWorkerCompletions.Version)
        {
            await ApplyAsync(connection, Migration030CanonicalWorkerCompletions.ApplyAsync, cancellationToken);
        }
    }

    private static async Task ApplyAsync(
        SqliteConnection connection,
        Func<SqliteConnection, SqliteTransaction, CancellationToken, Task> migration,
        CancellationToken cancellationToken)
    {
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await migration(connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task ApplyWithForeignKeysTemporarilyDisabledAsync(
        SqliteConnection connection,
        Func<SqliteConnection, SqliteTransaction, CancellationToken, Task> migration,
        CancellationToken cancellationToken)
    {
        var disable = connection.CreateCommand();
        disable.CommandText = "PRAGMA foreign_keys = OFF;";
        await disable.ExecuteNonQueryAsync(cancellationToken);
        try
        {
            await ApplyAsync(connection, migration, cancellationToken);
        }
        finally
        {
            var enable = connection.CreateCommand();
            enable.CommandText = "PRAGMA foreign_keys = ON;";
            await enable.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
