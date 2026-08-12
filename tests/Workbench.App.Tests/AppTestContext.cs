using Workbench.App.Services;
using Workbench.App.ViewModels;
using Workbench.Project.Git;
using Workbench.Project.Opening;
using Workbench.App.Tests.Support;

namespace Workbench.App.Tests;

internal sealed class AppTestContext : IAsyncDisposable
{
    private readonly TemporaryDirectory _directory;
    private readonly TestFolderPickerService _folderPicker;

    private AppTestContext(TemporaryDirectory directory, AppServices services, MutableTimeProvider time, TestFolderPickerService folderPicker)
    {
        _directory = directory;
        Services = services;
        Time = time;
        _folderPicker = folderPicker;
    }

    public AppServices Services { get; }

    public MutableTimeProvider Time { get; }

    public ProjectOpenResult? LastOpened { get; private set; }

    public static async Task<AppTestContext> CreateAsync(string? folderPath = "unused")
    {
        var directory = new TemporaryDirectory("database");
        var time = new MutableTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00.0000000+00:00"));
        var services = AppServices.CreateForDatabasePath(Path.Combine(directory.Path, "workbench.db"), time);
        await services.InitializeAsync();
        return new AppTestContext(directory, services, time, new TestFolderPickerService(folderPath));
    }

    public HomeViewModel CreateHome(Func<string, CancellationToken, Task<ProjectOpenResult>>? opener = null) =>
        new(Services.ProjectRepository, Services.ProjectOpenService, _folderPicker, result =>
        {
            LastOpened = result;
            return Task.CompletedTask;
        }, opener);

    public MainWindowViewModel CreateMain() => new(Services, _folderPicker);

    public ValueTask DisposeAsync()
    {
        _directory.Dispose();
        return ValueTask.CompletedTask;
    }
}

internal sealed class TestFolderPickerService(string? folderPath) : IFolderPickerService
{
    public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) => Task.FromResult(folderPath);
}

internal sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private DateTimeOffset _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void SetUtcNow(DateTimeOffset utcNow) => _utcNow = utcNow;
}
