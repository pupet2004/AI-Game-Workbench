using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.App.Services;

namespace Workbench.App.ViewModels;

public sealed record RuntimeDiagnosticItem(
    string Provider,
    string Account,
    string RuntimeKind,
    string ConnectionStatus);

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

    public string RuntimeCountText =>
        string.Format(_localization["Diagnostics.RuntimeCount"], Runtimes.Count);

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
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            BackupStatus = string.Format(
                _localization["Diagnostics.BackupFailed"],
                exception.Message);
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        Loading = true;
        DatabaseError = null;
        Runtimes.Clear();
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

            OnPropertyChanged(nameof(RuntimeCountText));
            OnPropertyChanged(nameof(RuntimeUnavailableText));
            Loading = false;
        }
    }
}
