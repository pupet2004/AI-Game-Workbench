namespace Workbench.Core.Layout;

public sealed record ProjectLayout
{
    private const double Epsilon = 0.000001;

    public ProjectLayout(
        Guid projectId,
        double leaderWidth,
        double workWidth,
        double libraryWidth,
        WorkspacePane focusedPane,
        DateTimeOffset updatedAt)
    {
        if (leaderWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(leaderWidth), "Width must be greater than zero.");
        }

        if (workWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workWidth), "Width must be greater than zero.");
        }

        if (libraryWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(libraryWidth), "Width must be greater than zero.");
        }

        if (Math.Abs(leaderWidth + workWidth + libraryWidth - 1.0) > Epsilon)
        {
            throw new ArgumentException("Pane widths must total approximately 1.0.");
        }

        ProjectId = projectId;
        LeaderWidth = leaderWidth;
        WorkWidth = workWidth;
        LibraryWidth = libraryWidth;
        FocusedPane = focusedPane;
        UpdatedAt = updatedAt;
    }

    public Guid ProjectId { get; }

    public double LeaderWidth { get; }

    public double WorkWidth { get; }

    public double LibraryWidth { get; }

    public WorkspacePane FocusedPane { get; }

    public DateTimeOffset UpdatedAt { get; }

    public static ProjectLayout CreateDefault(Guid projectId) =>
        Create(projectId, 0.30, 0.45, 0.25, WorkspacePane.Work);

    public static ProjectLayout FocusLeader(Guid projectId) =>
        Create(projectId, 0.60, 0.25, 0.15, WorkspacePane.Leader);

    public static ProjectLayout FocusWork(Guid projectId) =>
        Create(projectId, 0.15, 0.70, 0.15, WorkspacePane.Work);

    public static ProjectLayout FocusLibrary(Guid projectId) =>
        Create(projectId, 0.15, 0.20, 0.65, WorkspacePane.Library);

    private static ProjectLayout Create(
        Guid projectId,
        double leaderWidth,
        double workWidth,
        double libraryWidth,
        WorkspacePane focusedPane) =>
        new(projectId, leaderWidth, workWidth, libraryWidth, focusedPane, DateTimeOffset.UtcNow);
}
