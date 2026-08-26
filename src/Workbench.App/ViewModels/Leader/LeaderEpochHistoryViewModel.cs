using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Storage.Leaders;
using Workbench.App.Services;

namespace Workbench.App.ViewModels.Leader;

public sealed partial class LeaderEpochHistoryViewModel : ViewModelBase
{
    private const int PageSize = 10;
    private readonly Guid _projectId;
    private readonly LeaderSessionEpochRepository _epochs;
    private readonly LeaderMessageRepository _messages;
    private LeaderEpochHistoryCursor? _cursor;

    public LeaderEpochHistoryViewModel(Guid projectId, LeaderSessionEpochRepository epochs, LeaderMessageRepository messages)
    {
        _projectId = projectId;
        _epochs = epochs;
        _messages = messages;
    }

    public ObservableCollection<ArchivedLeaderEpochViewModel> ArchivedEpochs { get; } = [];
    public bool HasMore => _cursor is not null;
    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial string? ErrorMessage { get; set; }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (ArchivedEpochs.Count != 0 || IsLoading) return;
        await LoadAsync(null, false, cancellationToken);
    }

    [RelayCommand]
    public Task LoadEarlierAsync() => LoadAsync(_cursor, true, CancellationToken.None);

    [RelayCommand]
    public Task RetryAsync() => LoadAsync(_cursor, ArchivedEpochs.Count != 0, CancellationToken.None);

    public async Task RefreshAfterRolloverAsync(CancellationToken cancellationToken = default)
    {
        ArchivedEpochs.Clear();
        _cursor = null;
        await LoadAsync(null, false, cancellationToken);
    }

    private async Task LoadAsync(LeaderEpochHistoryCursor? before, bool prepend, CancellationToken cancellationToken)
    {
        if (IsLoading) return;
        IsLoading = true; ErrorMessage = null;
        try
        {
            var page = await _epochs.GetArchivedPageAsync(_projectId, PageSize, before, cancellationToken);
            var items = page.Epochs.Reverse().Select(epoch => new ArchivedLeaderEpochViewModel(epoch, _messages));
            if (prepend)
            {
                foreach (var item in items.Reverse()) ArchivedEpochs.Insert(0, item);
            }
            else foreach (var item in items) ArchivedEpochs.Add(item);
            _cursor = page.NextCursor;
            OnPropertyChanged(nameof(HasMore));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { ErrorMessage = LocalizationService.Current["Dynamic.UnableLoadPrevious"]; }
        finally { IsLoading = false; OnPropertyChanged(nameof(HasError)); }
    }
}

public sealed partial class ArchivedLeaderEpochViewModel : ViewModelBase
{
    private readonly LeaderMessageRepository _messages;
    public ArchivedLeaderEpochViewModel(StoredArchivedLeaderSessionEpoch epoch, LeaderMessageRepository messages)
    { Epoch = epoch; _messages = messages; }
    public StoredArchivedLeaderSessionEpoch Epoch { get; }
    public string Header => $"{FormatDate(Epoch.EndedAt)} · {Epoch.MessageCount} {LocalizationService.Current[(Epoch.MessageCount == 1 ? "Dynamic.MessageSingular" : "Dynamic.MessagePlural")]}";
    public string Preview => CreateHandoffPreview(Epoch.HandoffSummary) ?? ReasonLabel(Epoch.RolloverReason);
    public string? FullHandoff => Epoch.HandoffSummary;
    public bool HasFullHandoff => !string.IsNullOrWhiteSpace(FullHandoff);
    public ObservableCollection<LeaderMessageViewModel> Messages { get; } = [];
    [ObservableProperty] public partial bool IsExpanded { get; set; }
    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial string? ErrorMessage { get; set; }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    [RelayCommand]
    public async Task ToggleAsync()
    {
        IsExpanded = !IsExpanded;
        if (IsExpanded && Messages.Count == 0) await LoadMessagesAsync();
    }
    [RelayCommand] public Task RetryAsync() => LoadMessagesAsync();
    private async Task LoadMessagesAsync()
    {
        if (IsLoading) return;
        IsLoading = true; ErrorMessage = null;
        try { foreach (var message in await _messages.GetAllAsync(Epoch.Id)) Messages.Add(new LeaderMessageViewModel(message.Role == "user" ? LeaderMessageRole.User : LeaderMessageRole.Assistant, message.Text)); }
        catch { ErrorMessage = LocalizationService.Current["Dynamic.UnableLoadSession"]; }
        finally { IsLoading = false; OnPropertyChanged(nameof(HasError)); }
    }
    private static string FormatDate(DateTimeOffset endedAt)
    {
        var date = endedAt.ToLocalTime().Date;
        if (date == DateTime.Today) return LocalizationService.Current["Dynamic.Today"];
        if (date == DateTime.Today.AddDays(-1)) return LocalizationService.Current["Dynamic.Yesterday"];
        return endedAt.ToLocalTime().ToString("MMM d");
    }

    public static string? CreateHandoffPreview(string? handoff)
    {
        if (string.IsNullOrWhiteSpace(handoff)) return null;
        var firstSection = handoff.Split(["\r\n\r\n", "\n\n"], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? handoff;
        var normalized = string.Join(" ", firstSection.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        const int maximumRunes = 180;
        if (normalized.EnumerateRunes().Count() <= maximumRunes) return normalized;
        return string.Concat(normalized.EnumerateRunes().Take(maximumRunes).Select(rune => rune.ToString())) + "…";
    }

    private static string ReasonLabel(string? reason) => reason switch { "Manual" => LocalizationService.Current["Dynamic.StartedManually"], "WorkdayBoundary" => LocalizationService.Current["Dynamic.NewWorkday"], _ => LocalizationService.Current["Dynamic.PreviousSession"] };
}
