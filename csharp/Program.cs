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

        // Distinct taskbar identity so the window/tray use our icon.
        try { SetCurrentProcessExplicitAppUserModelID(AppConfig.AppId); } catch { }

        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        ApplicationConfiguration.Initialize();   // EnableVisualStyles + text rendering defaults
        Application.Run(new MainForm(startMinimized));
    }
}
