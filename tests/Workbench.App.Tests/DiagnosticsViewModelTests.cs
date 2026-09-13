using Workbench.App.Tests.Support;
using Workbench.App.Services;
using Workbench.App.ViewModels;

namespace Workbench.App.Tests;

public sealed class DiagnosticsViewModelTests
{
    [Fact]
    public async Task Diagnostics_reports_healthy_database_and_empty_runtime_state()
    {
        await using var context = await AppTestContext.CreateAsync();
        var diagnostics = new DiagnosticsViewModel(context.Services, () => Task.CompletedTask);

        await diagnostics.InitializeAsync();

        Assert.Equal(context.Services.Database.DatabasePath, diagnostics.DatabasePath);
        Assert.Equal("Database connection is healthy", diagnostics.DatabaseStatus);
        Assert.Empty(diagnostics.Runtimes);
        Assert.Equal("Registered runtimes: 0", diagnostics.RuntimeCountText);
        Assert.False(diagnostics.HasDatabaseError);
    }

    [Fact]
    public async Task Diagnostics_lists_registered_runtime_identity()
    {
        var runtime = new FakeAgentRuntime();
        await using var context = await AppTestContext.CreateAsync(runtimeRegistry: RegistryWith(runtime));
        var diagnostics = new DiagnosticsViewModel(context.Services, () => Task.CompletedTask);

        await diagnostics.InitializeAsync();

        var item = Assert.Single(diagnostics.Runtimes);
        Assert.Equal("Fake Provider", item.Provider);
        Assert.Equal("Fake Account", item.Account);
        Assert.Equal("fake-runtime", item.RuntimeKind);
        Assert.Equal("Connected", item.ConnectionStatus);
    }

    [Fact]
    public async Task Diagnostics_creates_a_consistent_database_backup()
    {
        await using var context = await AppTestContext.CreateAsync();
        var diagnostics = new DiagnosticsViewModel(context.Services, () => Task.CompletedTask);
        await diagnostics.InitializeAsync();

        await diagnostics.CreateBackupCommand.ExecuteAsync(null);

        var backupPath = diagnostics.BackupStatus!["Backup created: ".Length..];
        Assert.True(File.Exists(backupPath));
        Assert.NotEqual(0, new FileInfo(backupPath).Length);
        Assert.Single(diagnostics.Backups);
        await using (var backupConnection = new Workbench.Storage.Database.WorkbenchDatabase(backupPath).CreateConnection())
        {
            await backupConnection.OpenAsync();
            await using var integrityCommand = backupConnection.CreateCommand();
            integrityCommand.CommandText = "PRAGMA integrity_check;";
            Assert.Equal("ok", await integrityCommand.ExecuteScalarAsync());
        }
        File.Delete(backupPath);
    }

    [Fact]
    public async Task Diagnostics_uses_a_unique_path_for_repeated_backups()
    {
        await using var context = await AppTestContext.CreateAsync();
        var diagnostics = new DiagnosticsViewModel(context.Services, () => Task.CompletedTask);
        await diagnostics.InitializeAsync();

        await diagnostics.CreateBackupCommand.ExecuteAsync(null);
        var firstPath = diagnostics.BackupStatus!["Backup created: ".Length..];
        await diagnostics.CreateBackupCommand.ExecuteAsync(null);
        var secondPath = diagnostics.BackupStatus!["Backup created: ".Length..];

        Assert.NotEqual(firstPath, secondPath);
        Assert.True(File.Exists(firstPath));
        Assert.True(File.Exists(secondPath));
        File.Delete(firstPath);
        File.Delete(secondPath);
    }

    [Fact]
    public async Task Diagnostics_lists_and_validates_existing_backups()
    {
        await using var context = await AppTestContext.CreateAsync();
        var backupService = new WorkbenchBackupService(context.Services.Database, context.Time);
        var firstPath = await backupService.CreateDatabaseBackupAsync();
        var secondPath = await backupService.CreateDatabaseBackupAsync();
        var diagnostics = new DiagnosticsViewModel(context.Services, () => Task.CompletedTask);

        await diagnostics.InitializeAsync();
        await diagnostics.ValidateBackupsCommand.ExecuteAsync(null);

        Assert.Equal(2, diagnostics.Backups.Count);
        Assert.Equal("Validated 2 backup(s).", diagnostics.BackupValidationStatus);
        File.Delete(firstPath);
        File.Delete(secondPath);
    }

    [Fact]
    public async Task Backup_validation_does_not_create_a_missing_file()
    {
        await using var context = await AppTestContext.CreateAsync();
        var service = new WorkbenchBackupService(context.Services.Database, context.Time);
        var missingPath = Path.Combine(
            Path.GetDirectoryName(context.Services.Database.DatabasePath)!,
            "workbench.backup-missing.db");

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => service.ValidateDatabaseBackupAsync(missingPath));

        Assert.False(File.Exists(missingPath));
    }

    private static Workbench.Runtime.Registry.AgentRuntimeRegistry RegistryWith(FakeAgentRuntime runtime)
    {
        var registry = new Workbench.Runtime.Registry.AgentRuntimeRegistry();
        registry.Register(runtime);
        return registry;
    }
}
