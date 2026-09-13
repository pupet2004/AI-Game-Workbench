using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.App.Services;

namespace Workbench.App.ViewModels;

public sealed record RuntimeDiagnosticItem(
    string Provider,
    string Account,
    string RuntimeKind,
    string ConnectionStatus);

public sealed record DatabaseBackupDiagnosticItem(
    string FileName,
    string Path,
    string SizeText,
    string LastWriteText);

public sealed partial class DiagnosticsViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly Func<Task> _back;
    private readonly LocalizationService _localization;
    private readonly WorkbenchBackupService _backupService;

    public DiagnosticsViewModel(
        AppServices services,
        Func<Task> back,
        LocalizationService? localization = null,
        WorkbenchBackupService? backupService = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _back = back ?? throw new ArgumentNullException(nameof(back));
        _localization = localization ?? new LocalizationService(services.WorkbenchSettingsRepository);
        _backupService = backupService ?? new WorkbenchBackupService(services.Database, services.TimeProvider);
    }

    public string DatabasePath => _services.Database.DatabasePath;

    public ObservableCollection<RuntimeDiagnosticItem> Runtimes { get; } = [];

    public ObservableCollection<DatabaseBackupDiagnosticItem> Backups { get; } = [];

    public string RuntimeCountText =>
        string.Format(_localization["Diagnostics.RuntimeCount"], Runtimes.Count);

    public string BackupCountText =>
        string.Format(_localization["Diagnostics.BackupCount"], Backups.Count);

    public string RuntimeUnavailableText =>
        _services.RuntimeUnavailableDetail ?? _localization["Diagnostics.NoRuntimeError"];

    [ObservableProperty]
    public partial bool Loading { get; private set; }

    [ObservableProperty]
    public partial string DatabaseStatus { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDatabaseError))]
    public partial string? DatabaseError { get; private set; }

    [ObservableProperty]
    public partial string? BackupStatus { get; private set; }

    [ObservableProperty]
    public partial string? BackupValidationStatus { get; private set; }

    public bool HasDatabaseError => !string.IsNullOrWhiteSpace(DatabaseError);

    public async Task InitializeAsync(CancellationToken cancellationToken = default) =>
        await RefreshAsync(cancellationToken);

    [RelayCommand]
    private Task Refresh() => RefreshAsync(CancellationToken.None);

    [RelayCommand]
    private Task Back() => _back();

    [RelayCommand]
    private async Task CreateBackup()
    {
        try
        {
            BackupStatus = string.Format(
                _localization["Diagnostics.BackupCreated"],
                await _backupService.CreateDatabaseBackupAsync());
            LoadBackups();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            BackupStatus = string.Format(
                _localization["Diagnostics.BackupFailed"],
                exception.Message);
        }
    }

    [RelayCommand]
    private async Task ValidateBackups()
    {
        var validCount = 0;
        var failures = new List<string>();
        foreach (var backup in Backups)
        {
            try
            {
                await _backupService.ValidateDatabaseBackupAsync(backup.Path);
                validCount++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failures.Add(string.Format(
                    _localization["Diagnostics.BackupValidationFailed"],
                    backup.FileName,
                    exception.Message));
            }
        }

        BackupValidationStatus = failures.Count == 0
            ? string.Format(_localization["Diagnostics.BackupsValidated"], validCount)
            : string.Format(
                _localization["Diagnostics.BackupValidationSummary"],
                validCount,
                failures.Count) + " " + string.Join(" ", failures);
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        Loading = true;
        DatabaseError = null;
        Runtimes.Clear();
        Backups.Clear();
        OnPropertyChanged(nameof(RuntimeCountText));
        OnPropertyChanged(nameof(RuntimeUnavailableText));

        try
        {
            await using var connection = _services.Database.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1;";
            await command.ExecuteScalarAsync(cancellationToken);
            DatabaseStatus = _localization["Diagnostics.Healthy"];
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            DatabaseStatus = _localization["Diagnostics.Failed"];
            DatabaseError = exception.Message;
        }
        finally
        {
            foreach (var runtime in _services.RuntimeRegistry.Runtimes)
            {
                Runtimes.Add(new RuntimeDiagnosticItem(
                    runtime.Provider.DisplayName,
                    runtime.Account.DisplayName,
                    runtime.RuntimeKind,
                    runtime.Account.IsConnected
                        ? _localization["Diagnostics.Connected"]
                        : _localization["Diagnostics.Disconnected"]));
            }

            LoadBackups();
            OnPropertyChanged(nameof(RuntimeCountText));
            OnPropertyChanged(nameof(RuntimeUnavailableText));
            Loading = false;
        }
    }

    private void LoadBackups()
    {
        Backups.Clear();
        foreach (var backup in _backupService.ListDatabaseBackups())
        {
            Backups.Add(new DatabaseBackupDiagnosticItem(
                System.IO.Path.GetFileName(backup.Path),
                backup.Path,
                FormatSize(backup.SizeBytes),
                backup.LastWriteAt.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture)));
        }

        OnPropertyChanged(nameof(BackupCountText));
    }

    private static string FormatSize(long bytes) =>
        bytes < 1024 * 1024
            ? $"{Math.Max(1, bytes / 1024)} KB"
            : $"{bytes / (1024d * 1024d):0.0} MB";
}
