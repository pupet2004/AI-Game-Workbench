using Workbench.Project.Git;
using Workbench.Project.Opening;
using Workbench.Storage.Database;
using Workbench.Storage.Projects;

namespace Workbench.App.Services;

public sealed class AppServices
{
    private AppServices(
        WorkbenchDatabase database,
        ProjectRepository projectRepository,
        ProjectLayoutRepository projectLayoutRepository,
        ProjectOpenService projectOpenService)
    {
        Database = database;
        ProjectRepository = projectRepository;
        ProjectLayoutRepository = projectLayoutRepository;
        ProjectOpenService = projectOpenService;
    }

    public WorkbenchDatabase Database { get; }

    public ProjectRepository ProjectRepository { get; }

    public ProjectLayoutRepository ProjectLayoutRepository { get; }

    public ProjectOpenService ProjectOpenService { get; }

    public static AppServices CreateDefault() =>
        CreateForDatabasePath(DatabasePathProvider.GetDefaultDatabasePath(), TimeProvider.System);

    public static AppServices CreateForDatabasePath(string databasePath, TimeProvider? timeProvider = null)
    {
        var database = new WorkbenchDatabase(databasePath);
        var projectRepository = new ProjectRepository(database);
        var layoutRepository = new ProjectLayoutRepository(database);
        var projectOpenService = new ProjectOpenService(
            projectRepository,
            layoutRepository,
            new GitCliInspector(),
            timeProvider ?? TimeProvider.System);

        return new AppServices(database, projectRepository, layoutRepository, projectOpenService);
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        Database.InitializeAsync(cancellationToken);
}
