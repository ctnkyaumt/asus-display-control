using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AsusDisplayControl;

internal static class Program
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    [STAThread]
    private static void Main(string[] args)
    {
        bool startMinimized = args.Any(a =>
            a.Equals("--tray", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));

        // One instance only: two copies fight over the monitor's DDC bus and over each other's
        // preset switches (each would keep restoring its own idea of the current preset).
        using var single = new Mutex(initiallyOwned: true, @"Local\ASUSDisplayControl.Instance", out bool first);
        if (!first)
        {
            // Silent for the autostart path; a hand-launched second copy gets told why nothing opened.
            if (!startMinimized)
                MessageBox.Show("ASUS Display Control is already running — look for its icon in the system tray.",
                                AppConfig.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Distinct taskbar identity so the window/tray use our icon.
        try { SetCurrentProcessExplicitAppUserModelID(AppConfig.AppId); } catch { }

        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        ApplicationConfiguration.Initialize();   // EnableVisualStyles + text rendering defaults
        Application.Run(new MainForm(startMinimized));
    }
}
