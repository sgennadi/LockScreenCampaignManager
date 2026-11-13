using System.Windows.Forms;
using System.Text;

namespace LockScreenCampaignManager;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // A PhotoScreenSaver.scr is the same portable, self-contained apphost
        // in a restricted screensaver entrypoint. Never expose the manager,
        // updater, repair or AD functions when loaded as a .scr.
        if (PhotoJpegSaver.IsScrProcess)
            return PhotoJpegSaver.Run(args);

        if (args.Length > 0 && args[0].Equals("--client-agent",
                StringComparison.OrdinalIgnoreCase))
            return NativeClientAgent.Run(args);

        if (args.Length > 0 && args[0] == PortableUpdateCoordinator.HelperSwitch)
            return PortableUpdateCoordinator.RunHelper(args);

        if (args.Contains("--self-test-exit-probe", StringComparer.OrdinalIgnoreCase))
            return 37; // CI verifies that WinExe exit codes propagate to PowerShell.

        if (args.Contains("--diagnostics-self-test", StringComparer.OrdinalIgnoreCase))
        {
            try { SelfTests.RunDiagnosticsOnly(); return 0; }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }

        if (args.Contains("--updater-self-test", StringComparer.OrdinalIgnoreCase))
        {
            try { SelfTests.RunUpdaterOnly(); return 0; }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }

        if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
        {
            try { SelfTests.Run(); return 0; }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }

        if (args.Contains("--endpoint-user-probe", StringComparer.OrdinalIgnoreCase))
        {
            // Explicit, local-only CURRENT-USER HKCU Screen Saver capture.
            // It neither requires nor opens an AD connection.
            ApplicationConfiguration.Initialize();
            Application.Run(new EndpointUserProbeDialog());
            return 0;
        }

        if (args.Contains("--ui-smoke-test", StringComparer.OrdinalIgnoreCase))
        {
            // CI-only and offline: do not Show/ShowDialog, start gMSA, access AD,
            // or save a user's UI preferences or geometry.
            try
            {
                ApplicationConfiguration.Initialize();
                UiLayoutSmokeTests.Run();
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }

        if (args.Contains("--gmsa-diagnose-once", StringComparer.OrdinalIgnoreCase))
        {
            // Dedicated Task Scheduler action running as the installed gMSA.
            // Read-only diagnostics NEVER invoke repair RPC or deployment code.
            try { return GmsaDiagnosticCheck.RunOnce(); }
            catch (Exception ex) { AppLog.Write(ex, "gMSA diagnostics startup"); return 1; }
        }

        if (args.Contains("--gmsa-repair-once", StringComparer.OrdinalIgnoreCase))
        {
            // Dedicated unattended identity; exact executable path and account
            // SID are validated from protected administrator-owned settings.
            try { return GmsaRepairWorker.RunOnce(); }
            catch (Exception ex) { AppLog.Write(ex, "gMSA worker startup"); return 1; }
        }

        if (args.Contains("--background-repair-once", StringComparer.OrdinalIgnoreCase))
        {
            // No form, no saved password, no automatic task creation. Called only
            // by an explicitly installed interactive-user Scheduled Task.
            try { return BackgroundRepairWorker.RunOnce(); }
            catch (Exception ex) { AppLog.Write(ex, "Background Repair startup"); return 1; }
        }

        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) =>
            UiErrorDialog.Show(Application.OpenForms.Cast<Form>().FirstOrDefault(), "Application UI", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            AppLog.Write(e.ExceptionObject as Exception ?? new Exception("Unhandled error"), "Unhandled application");
        Application.Run(new MainForm());
        return 0;
    }
}

internal static class AppLog
{
    internal static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LockScreenCampaignManager");

    internal static void Write(Exception ex, string operation = "Application") =>
        WriteToDirectory(DirectoryPath, ex, operation);

    // Explicit directory for synthetic diagnostics tests; production always
    // calls Write() with the user's local state directory.
    internal static void WriteToDirectory(string directory, Exception ex, string operation)
    {
        try
        {
            Directory.CreateDirectory(directory);
            string log = $"{DateTimeOffset.UtcNow:O} | " +
                UniversalDiagnosticsEmbedded.Clean(operation, 80) + " | " +
                UniversalDiagnosticsEmbedded.Clean(ex.GetType().Name, 90) + " | " +
                $"0x{ex.HResult:X8} | " + UniversalDiagnosticsEmbedded.Clean(ex.Message, 260);
            File.AppendAllText(Path.Combine(directory, "errors.log"), log + Environment.NewLine,
                new UTF8Encoding(false));
        }
        catch { /* Logging should never interrupt the GUI. */ }
    }
}
