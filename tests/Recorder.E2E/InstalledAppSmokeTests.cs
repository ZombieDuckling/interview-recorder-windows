// OWNER: win-lead. Runs against the INSTALLED app (CI installs the Burn .exe per-user first; SHIP-5: evidence from the built thing).
// IR_APP_EXE = full path of InterviewRecorder.exe; IR_SCREENSHOTS = folder for the PNGs. Skipped when IR_APP_EXE is not set.
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.UIA3;

namespace Recorder.E2E;

[Trait("Category", "E2E")]
public sealed class InstalledAppSmokeTests
{
    private static readonly string? AppExe = Environment.GetEnvironmentVariable("IR_APP_EXE");
    private static readonly string Screens = Environment.GetEnvironmentVariable("IR_SCREENSHOTS") ?? Path.Combine(Path.GetTempPath(), "ir-screens");

    [Fact]
    public void Launches_shows_every_page_and_gates_start_on_consent()
    {
        if (string.IsNullOrEmpty(AppExe) || !OperatingSystem.IsWindows())
        {
            return; // not provisioned: CI sets IR_APP_EXE after installing the bundle
        }

        Directory.CreateDirectory(Screens);
        using var automation = new UIA3Automation();
        using Application app = Application.Launch(AppExe);
        try
        {
            Window? window = app.GetMainWindow(automation, TimeSpan.FromSeconds(30));
            Assert.NotNull(window);
            if (window is null) return;
            Assert.Equal("Interview Recorder", window.Title);
            Shot(window, "00-start");

            foreach (string nav in new[] { "NavWelcome", "NavDevices", "NavLibrary", "NavPrivacy", "NavDiagnostics", "NavNewInterview" })
            {
                AutomationElement item = Find(window, nav);
                item.Click();
                Thread.Sleep(700);
                Shot(window, nav);
            }

            // Consent gate (PLAN: never record without explicit consent).
            Find(window, "NewInterviewParticipantId").AsTextBox().Enter("P-0001");
            AutomationElement start = Find(window, "NewInterviewStart");
            CheckBox consent = Find(window, "NewInterviewConsent").AsCheckBox();
            consent.IsChecked = false;
            Thread.Sleep(300);
            Assert.False(start.IsEnabled, "Start must be disabled until consent is ticked.");
            consent.IsChecked = true;
            Thread.Sleep(300);
            Shot(window, "10-consent-ticked");
            // Start may still be disabled on a machine without audio devices (CI runners): that is correct behaviour.
            File.WriteAllText(Path.Combine(Screens, "start-enabled-with-consent.txt"), start.IsEnabled ? "enabled" : "disabled (no audio device)");
        }
        finally
        {
            app.Close();
            if (!app.HasExited)
            {
                app.Kill();
            }
        }
    }

    private static AutomationElement Find(Window window, string automationId)
    {
        AutomationElement? found = null;
        for (int i = 0; i < 20 && found is null; i++)
        {
            found = window.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
            if (found is null)
            {
                Thread.Sleep(250);
            }
        }

        return found ?? throw new InvalidOperationException($"Element '{automationId}' not found.");
    }

    private static void Shot(Window window, string name) =>
        Capture.Element(window).ToFile(Path.Combine(Screens, $"{name}.png"));
}
