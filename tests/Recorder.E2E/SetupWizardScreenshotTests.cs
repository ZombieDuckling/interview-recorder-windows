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

    // Burn relaunches the bundle as a clean-room process; allow a generous budget on a cold CI desktop.
    private const int WindowDiscoveryTimeoutSeconds = 120;

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
            // Search the whole desktop: Burn relaunches itself as a separate (clean-room) process.
            var (window, titles) = FindWizardWindow(automation, TimeSpan.FromSeconds(WindowDiscoveryTimeoutSeconds));

            Assert.True(
                window is not null,
                $"Setup wizard window was not found within {WindowDiscoveryTimeoutSeconds}s. " +
                $"Top-level desktop windows seen: [{string.Join(" | ", titles)}]");
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
            // Close every setup process (the launcher and its relaunched child); nothing was installed.
            foreach (var p in System.Diagnostics.Process.GetProcessesByName(Path.GetFileNameWithoutExtension(SetupExe)))
            {
                try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
                p.Dispose();
            }
        }
    }

    // Polls the desktop for the wizard window, matching on window title or on the setup process id,
    // and returns the titles seen so an unsuccessful run is diagnosable from the CI log alone.
    private static (Window? window, string[] titles) FindWizardWindow(UIA3Automation automation, TimeSpan timeout)
    {
        var setupProcessIds = SetupProcessIds();
        var deadline = DateTime.UtcNow + timeout;
        string[] lastTitles = Array.Empty<string>();

        while (DateTime.UtcNow < deadline)
        {
            AutomationElement[] candidates = automation.GetDesktop().FindAllChildren().ToArray();
            lastTitles = candidates
                .Select(w => w.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .ToArray();

            var match = candidates.FirstOrDefault(w => MatchesWizard(w, setupProcessIds));
            if (match is not null)
            {
                return (match.AsWindow(), lastTitles);
            }

            setupProcessIds = SetupProcessIds();
            Thread.Sleep(500);
        }

        return (null, lastTitles);
    }

    private static bool MatchesWizard(AutomationElement element, HashSet<int> setupProcessIds)
    {
        if (TitleLooksLikeWizard(element.Name))
        {
            return true;
        }

        try
        {
            int pid = element.Properties.ProcessId.ValueOrDefault;
            return pid != 0 && setupProcessIds.Contains(pid);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool TitleLooksLikeWizard(string? name)
    {
        return !string.IsNullOrEmpty(name) && name.Contains("Recorder", StringComparison.OrdinalIgnoreCase);
    }

    private static HashSet<int> SetupProcessIds()
    {
        var ids = new HashSet<int>();
        foreach (var p in System.Diagnostics.Process.GetProcesses())
        {
            try
            {
                if (p.ProcessName.Contains("InterviewRecorder", StringComparison.OrdinalIgnoreCase))
                {
                    ids.Add(p.Id);
                }
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            finally { p.Dispose(); }
        }
        return ids;
    }
}
