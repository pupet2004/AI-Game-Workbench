namespace Workbench.App.ViewModels;

public static class WorkspaceLayoutCalculator
{
    public static (double Leader, double Work, double Library) Normalize(double leaderPixels, double workPixels, double libraryPixels)
    {
        if (leaderPixels <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(leaderPixels));
        }

        if (workPixels <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workPixels));
        }

        if (libraryPixels <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(libraryPixels));
        }

        var total = leaderPixels + workPixels + libraryPixels;
        return (leaderPixels / total, workPixels / total, libraryPixels / total);
    }
}
