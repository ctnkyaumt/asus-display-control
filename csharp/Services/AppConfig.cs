using System.Drawing;
using System.Reflection;
using System.Text.Json;
using Microsoft.Win32;

namespace AsusDisplayControl;

internal sealed class AppSettings
{
    public bool MinimizeToTray { get; set; } = true;
    public bool StartMinimized { get; set; } = false;
    public bool LightTheme { get; set; } = false;
}

/// <summary>One per-app rule: when <see cref="Process"/> is in the foreground, use <see cref="Preset"/>.</summary>
internal sealed class AppTweakRule
{
    public string Process { get; set; } = "";
    public int Preset { get; set; }
}

internal sealed class AppTweakConfig
{
    public bool Enabled { get; set; }
    public List<AppTweakRule> Rules { get; set; } = new();
}

/// <summary>App-wide constants, paths, persistence and the Windows startup toggle.</summary>
internal static class AppConfig
{
    public const string AppName = "ASUS Display Control";
    public const string AppId = "ASUSDisplayControl"; // Run value name + %APPDATA% folder + taskbar id
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public static string DataDir { get; } = InitDataDir();
    public static string SettingsPath => Path.Combine(DataDir, "dwc_settings.json");
    public static string PresetsPath => Path.Combine(DataDir, "dwc_presets.json");
    public static string SchedulePath => Path.Combine(DataDir, "dwc_schedule.json");
    public static string TweaksPath => Path.Combine(DataDir, "dwc_apptweaks.json");

    private static string InitDataDir()
    {
        string baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppId);
        try { Directory.CreateDirectory(baseDir); }
        catch { baseDir = AppContext.BaseDirectory; }
        return baseDir;
    }

    // ---- settings -------------------------------------------------------------
    public static AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public static void SaveSettings(AppSettings s)
    {
        try { File.WriteAllText(SettingsPath, JsonSerializer.Serialize(s, JsonOpts)); }
        catch { }
    }

    // ---- preset memory (Splendid code -> {property -> value}) -----------------
    public static Dictionary<int, Dictionary<string, int>> LoadPresets()
    {
        try
        {
            if (File.Exists(PresetsPath))
                return JsonSerializer.Deserialize<Dictionary<int, Dictionary<string, int>>>(File.ReadAllText(PresetsPath))
                       ?? new();
        }
        catch { }
        return new();
    }

    public static void SavePresets(Dictionary<int, Dictionary<string, int>> presets)
    {
        try { File.WriteAllText(PresetsPath, JsonSerializer.Serialize(presets, JsonOpts)); }
        catch { }
    }

    // ---- schedule -------------------------------------------------------------
    public static ScheduleConfig LoadSchedule()
    {
        try
        {
            if (File.Exists(SchedulePath))
                return JsonSerializer.Deserialize<ScheduleConfig>(File.ReadAllText(SchedulePath), JsonOpts) ?? new ScheduleConfig();
        }
        catch { }
        return new ScheduleConfig();
    }

    public static void SaveSchedule(ScheduleConfig schedule)
    {
        try { File.WriteAllText(SchedulePath, JsonSerializer.Serialize(schedule, JsonOpts)); }
        catch { }
    }

    // ---- per-app tweaks -------------------------------------------------------
    public static AppTweakConfig LoadTweaks()
    {
        try
        {
            if (File.Exists(TweaksPath))
                return JsonSerializer.Deserialize<AppTweakConfig>(File.ReadAllText(TweaksPath), JsonOpts) ?? new AppTweakConfig();
        }
        catch { }
        return new AppTweakConfig();
    }

    public static void SaveTweaks(AppTweakConfig tweaks)
    {
        try { File.WriteAllText(TweaksPath, JsonSerializer.Serialize(tweaks, JsonOpts)); }
        catch { }
    }

    // ---- Windows startup (HKCU Run key) ---------------------------------------
    private static string StartupCommand()
    {
        // ProcessPath is the real host exe (valid for single-file/self-contained builds).
        string exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "ASUS-Display-Control.exe");
        return $"\"{exe}\" --tray";
    }

    public static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(AppId) != null;
        }
        catch { return false; }
    }

    public static void SetStartup(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (key == null) return;
            if (enabled) key.SetValue(AppId, StartupCommand());
            else if (key.GetValue(AppId) != null) key.DeleteValue(AppId, throwOnMissingValue: false);
        }
        catch { }
    }

    // ---- app icon (embedded) --------------------------------------------------
    public static Icon LoadIcon()
    {
        var asm = Assembly.GetExecutingAssembly();
        // Resource id is "<RootNamespace>.icon.ico"
        var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("icon.ico"));
        if (name != null)
        {
            using var s = asm.GetManifestResourceStream(name);
            if (s != null) return new Icon(s);
        }
        return SystemIcons.Application;
    }
}
