using Avalonia;
using Avalonia.Controls;

namespace Workbench.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) => ApplySafeScreenPlacement();
    }

    private void ApplySafeScreenPlacement()
    {
        var screen = Screens.ScreenFromWindow(this);
        if (screen is null) return;

        var area = screen.WorkingArea;
        var frameSize = FrameSize ?? ClientSize;
        var frameWidth = Math.Max(0, frameSize.Width - ClientSize.Width);
        var frameHeight = Math.Max(0, frameSize.Height - ClientSize.Height);
        var placement = MainWindowPlacement.Calculate(
            area.X,
            area.Y,
            area.Width,
            area.Height,
            screen.Scaling,
            frameWidth,
            frameHeight);
        MinWidth = Math.Min(MinWidth, placement.Width);
        MinHeight = Math.Min(MinHeight, placement.Height);
        Width = placement.Width;
        Height = placement.Height;
        Position = new PixelPoint(placement.X, placement.Y);
    }
}

internal readonly record struct MainWindowPlacement(double Width, double Height, int X, int Y)
{
    private const double DefaultWidth = 1400;
    private const double DefaultHeight = 850;
    private const int SafeMargin = 48;

    public static MainWindowPlacement Calculate(
        int workingX,
        int workingY,
        int workingWidth,
        int workingHeight,
        double scaling,
        double frameWidth,
        double frameHeight)
    {
        var availableFrameWidth = (workingWidth - (SafeMargin * 2)) / scaling;
        var availableFrameHeight = (workingHeight - (SafeMargin * 2)) / scaling;
        var width = Math.Min(DefaultWidth, Math.Max(0, availableFrameWidth - frameWidth));
        var height = Math.Min(DefaultHeight, Math.Max(0, availableFrameHeight - frameHeight));
        var x = workingX + (int)Math.Round((workingWidth - ((width + frameWidth) * scaling)) / 2);
        var y = workingY + (int)Math.Round((workingHeight - ((height + frameHeight) * scaling)) / 2);
        return new MainWindowPlacement(width, height, x, y);
    }
}
