// Opens the real setup .exe (not quiet), screenshots the wizard pages, ticks the terms box, then cancels (installs nothing).
// IR_SETUP_EXE = path of InterviewRecorder-Setup-x64.exe. Skipped when not set.
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace Recorder.E2E;

[Trait("Category", "E2E")]
public sealed class SetupWizardScreenshotTests
{
    private static readonly string? SetupExe = Environment.GetEnvironmentVariable("IR_SETUP_EXE");
    private static readonly string Screens = Environment.GetEnvironmentVariable("IR_SCREENSHOTS") ?? Path.Combine(Path.GetTempPath(), "ir-screens");

    [Fact]
    public void Wizard_shows_terms_and_enables_install_only_after_agreeing()
    {
        if (string.IsNullOrEmpty(SetupExe) || !OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(Screens);
        using var automation = new UIA3Automation();
        using Application app = Application.Launch(SetupExe);
        try
        {
            Window? window = null;
            for (int i = 0; i < 60 && window is null; i++)
            {
                window = app.GetAllTopLevelWindows(automation).FirstOrDefault(w => w.Title.Contains("Interview Recorder", StringComparison.Ordinal));
                if (window is null) Thread.Sleep(500);
            }

            Assert.NotNull(window);
            if (window is null) return;
            Thread.Sleep(1000);
            Capture.Element(window).ToFile(Path.Combine(Screens, "setup-01-welcome-terms.png"));

            AutomationElement? install = window.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Install").Or(cf.ByName("&Install"))));
            CheckBox? agree = window.FindFirstDescendant(cf => cf.ByControlType(ControlType.CheckBox))?.AsCheckBox();
            Assert.NotNull(agree);
            if (install is not null) Assert.False(install.IsEnabled, "Install must stay disabled until the terms are accepted.");
            agree!.IsChecked = true;
            Thread.Sleep(500);
            Capture.Element(window).ToFile(Path.Combine(Screens, "setup-02-terms-accepted.png"));
            if (install is not null) Assert.True(install.IsEnabled, "Install must be enabled after accepting the terms.");
        }
        finally
        {
            app.Close();
            Thread.Sleep(1000);
            if (!app.HasExited) app.Kill();
        }
    }
}
