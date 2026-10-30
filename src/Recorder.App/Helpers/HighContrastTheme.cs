using System.Windows;

namespace Recorder.App.Helpers;

internal static class HighContrastTheme
{
    public static void ApplyIfNeeded(Application app)
    {
        if (!SystemParameters.HighContrast)
        {
            return;
        }

        app.Resources["SurfaceBrush"] = SystemColors.ControlBrush;
        app.Resources["SurfaceAltBrush"] = SystemColors.ControlLightBrush;
        app.Resources["BorderBrush"] = SystemColors.ControlDarkBrush;
        app.Resources["TextBrush"] = SystemColors.ControlTextBrush;
        app.Resources["MutedTextBrush"] = SystemColors.GrayTextBrush;
        app.Resources["AccentBrush"] = SystemColors.HighlightBrush;
        app.Resources["AccentTextBrush"] = SystemColors.HighlightTextBrush;
        app.Resources["DangerBrush"] = SystemColors.HighlightBrush;
        app.Resources["RecordingBrush"] = SystemColors.HighlightBrush;
    }
}
