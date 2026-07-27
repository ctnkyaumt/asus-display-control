using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace AsusDisplayControl;

internal static class Native
{
    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern int GetWindowThreadProcessId(IntPtr hwnd, out int pid);

    public const int ProcessQueryLimitedInformation = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(int access, bool inheritHandle, int pid);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll")]
    public static extern bool CloseHandle(IntPtr handle);

    /// <summary>Hand the working set back to Windows; it is paged in again on demand.</summary>
    [DllImport("psapi.dll")]
    public static extern bool EmptyWorkingSet(IntPtr process);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetCurrentProcess();
}

/// <summary>
/// Per-app tweak: watch the foreground window and switch presets to match a rule, the way
/// DisplayWidget Center's App Tweaker does. When nothing matches, the preset that was in use
/// before the first match is restored.
/// </summary>
internal sealed partial class MainForm
{
    private Control _tweakPage = null!;
    private ToggleSwitch _tweakEnable = null!;
    private TableLayoutPanel _tweakList = null!;
    private Label _tweakStatus = null!;
    private readonly List<(AppTweakRule rule, Control row)> _tweakRows = new();

    // ==========================================================================
    // PAGE
    // ==========================================================================
    private Control BuildTweakPage()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Main, ColumnCount = 1, RowCount = 5, Padding = new Padding(15) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // header
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // description + enable
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // rules
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // buttons
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // status

        root.Controls.Add(PageHeader("Per-App Tweak"), 0, 0);

        var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, AutoSize = true, BackColor = Theme.Main, Margin = new Padding(0, 0, 0, 10) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.Controls.Add(new Label
        {
            Text = "Switch the preset automatically for the app you are using. When the app in front " +
                   "matches no rule, the preset you had before is restored. Pick a running app from the " +
                   "list, type a process name, or Browse… to an .exe.",
            Font = Theme.Muted,
            ForeColor = Theme.TextMuted,
            BackColor = Theme.Main,
            AutoSize = true,
            MaximumSize = new Size(620, 0),
            Anchor = AnchorStyles.Left,
        }, 0, 0);

        _tweakEnable = new ToggleSwitch { BackColor = Theme.Main, Anchor = AnchorStyles.Right };
        _tweakEnable.SetValueSilent(_tweaks.Enabled);
        _tweakEnable.Toggled += on =>
        {
            _tweaks.Enabled = on;
            AppConfig.SaveTweaks(_tweaks);
            if (!on) RestoreFromTweak();
            SetTweakStatus();
        };
        top.Controls.Add(_tweakEnable, 1, 0);
        root.Controls.Add(top, 0, 1);

        var section = new SectionPanel("Rules") { Dock = DockStyle.Fill };
        _tweakList = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoScroll = true, BackColor = Theme.Main };
        _tweakList.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        section.Controls.Add(_tweakList);
        root.Controls.Add(section, 0, 2);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, BackColor = Theme.Main, Margin = new Padding(0, 10, 0, 0) };
        var add = MakeButton("Add rule", Theme.Accent, Theme.AccentDark);
        add.Click += (_, _) => AddTweakRule(new AppTweakRule { Process = "", Preset = PresetDefs[0].val });
        var refresh = MakeButton("Refresh app list", Theme.Card, Theme.CardHover);
        refresh.Click += (_, _) => BuildTweakRows();
        buttons.Controls.Add(add);
        buttons.Controls.Add(refresh);
        root.Controls.Add(buttons, 0, 3);

        _tweakStatus = new Label { Text = "", Font = Theme.Muted, ForeColor = Theme.TextMuted, BackColor = Theme.Main, AutoSize = true, Margin = new Padding(2, 10, 0, 0) };
        root.Controls.Add(_tweakStatus, 0, 4);

        BuildTweakRows();
        return root;
    }

    /// <summary>Rebuild every rule row (also refreshes the list of running apps).</summary>
    private void BuildTweakRows()
    {
        if (_tweakList == null) return;
        _tweakList.SuspendLayout();
        _tweakList.Controls.Clear();
        _tweakRows.Clear();
        foreach (var rule in _tweaks.Rules.ToList()) AddTweakRow(rule);
        _tweakList.ResumeLayout(true);
        SetTweakStatus();
    }

    private void AddTweakRule(AppTweakRule rule)
    {
        _tweaks.Rules.Add(rule);
        AppConfig.SaveTweaks(_tweaks);
        AddTweakRow(rule);
    }

    private void AddTweakRow(AppTweakRule rule)
    {
        var row = new TableLayoutPanel
        {
            ColumnCount = 4,
            RowCount = 1,
            Dock = DockStyle.Top,
            Height = 38,
            BackColor = Theme.Main,
            Margin = new Padding(0, 0, 0, 6),
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        // App: editable so an app that is not running right now can still be typed in.
        var app = new ThemedCombo(editable: true)
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 8, 4),
        };
        app.Items.AddRange(RunningApps());
        app.Text = rule.Process;
        app.TextChanged += (_, _) =>
        {
            rule.Process = app.Text.Trim();
            AppConfig.SaveTweaks(_tweaks);
        };

        // …for an app that isn't running (and won't be in the list): pick its .exe.
        var browse = MakeButton("Browse…", Theme.Card, Theme.CardHover);
        browse.Margin = new Padding(0, 3, 8, 3);
        browse.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog
            {
                Title = "Pick the application's .exe",
                Filter = "Programs (*.exe)|*.exe|All files (*.*)|*.*",
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            app.Text = Path.GetFileNameWithoutExtension(dlg.FileName);   // that is what the OS reports
        };

        var preset = MakeCombo();
        preset.Dock = DockStyle.Fill;
        preset.Margin = new Padding(0, 4, 8, 4);
        preset.Items.AddRange(PresetDefs.Select(p => p.name).ToArray());
        int idx = Array.FindIndex(PresetDefs, p => p.val == rule.Preset);
        preset.SelectedIndex = idx >= 0 ? idx : 0;
        preset.SelectedIndexChanged += (_, _) =>
        {
            if (preset.SelectedIndex < 0) return;
            rule.Preset = PresetDefs[preset.SelectedIndex].val;
            AppConfig.SaveTweaks(_tweaks);
        };

        var remove = MakeButton("Remove", Theme.Card, Theme.CardHover);
        remove.Margin = new Padding(0, 3, 0, 3);
        remove.Click += (_, _) =>
        {
            _tweaks.Rules.Remove(rule);
            AppConfig.SaveTweaks(_tweaks);
            _tweakList.Controls.Remove(row);
            _tweakRows.RemoveAll(r => r.rule == rule);
            row.Dispose();
            SetTweakStatus();
        };

        row.Controls.Add(app, 0, 0);
        row.Controls.Add(browse, 1, 0);
        row.Controls.Add(preset, 2, 0);
        row.Controls.Add(remove, 3, 0);
        _tweakList.Controls.Add(row);
        _tweakList.Controls.SetChildIndex(row, 0);   // newest on top of the docked stack
        _tweakRows.Add((rule, row));
    }

    /// <summary>Process names of everything with a visible window, e.g. "chrome", "code".</summary>
    private static string[] RunningApps()
    {
        var processes = Array.Empty<Process>();
        try
        {
            processes = Process.GetProcesses();
            return processes
                .Where(p => p.MainWindowHandle != IntPtr.Zero && p.ProcessName.Length > 0)
                .Select(p => p.ProcessName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch { return Array.Empty<string>(); }
        finally { foreach (var p in processes) p.Dispose(); }
    }

    private void SetTweakStatus()
    {
        if (_tweakStatus == null) return;
        string fg = _foregroundApp ?? "—";
        _tweakStatus.Text = _tweaks.Enabled
            ? $"Watching the foreground app.  In front now: {fg}   ·   {_tweaks.Rules.Count} rule(s)."
            : "Per-app switching is off.";
    }

    // ==========================================================================
    // WATCHER
    // ==========================================================================
    // Reused by the once-a-second lookup so the watcher allocates nothing per tick.
    private static readonly StringBuilder ImageNameBuffer = new(512);

    private static string? ForegroundProcessName()
    {
        var hwnd = Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;
        Native.GetWindowThreadProcessId(hwnd, out int pid);
        if (pid <= 0) return null;

        // Win32 rather than System.Diagnostics.Process: a Process object per tick is pure
        // garbage, and this path also works for processes we may not fully open.
        var handle = Native.OpenProcess(Native.ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            int size = ImageNameBuffer.Capacity;
            if (!Native.QueryFullProcessImageName(handle, 0, ImageNameBuffer, ref size)) return null;
            return Path.GetFileNameWithoutExtension(ImageNameBuffer.ToString(0, size));
        }
        finally { Native.CloseHandle(handle); }
    }

    /// <summary>Runs on the UI thread once a second.</summary>
    private void EvaluateAppTweak()
    {
        var app = ForegroundProcessName();
        bool changed = !string.Equals(app, _foregroundApp, StringComparison.OrdinalIgnoreCase);
        _foregroundApp = app;
        if (changed && _tweakPage.Visible) SetTweakStatus();

        if (!_tweaks.Enabled || _selectedMonitorId == null || _isSyncing || _isComparing) return;

        int? desired = MatchRule(app);
        if (desired.HasValue)
        {
            if (!_tweakApplied)
            {
                _tweakApplied = true;
                _tweakBasePreset = _userPresetActive ? UserPresetCode : _current.GetValueOrDefault("Splendid");
            }
            if (ActivePreset() != desired.Value)
            {
                SetStatus($"Per-app tweak → {PresetName(desired.Value)} for {app}");
                SetPresetThread(desired.Value, manual: false);
            }
        }
        else RestoreFromTweak();
    }

    private int? MatchRule(string? app)
    {
        if (string.IsNullOrEmpty(app)) return null;
        foreach (var rule in _tweaks.Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Process)) continue;
            var name = rule.Process.Trim();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
            if (string.Equals(name, app, StringComparison.OrdinalIgnoreCase)) return rule.Preset;
        }
        return null;
    }

    private int? ActivePreset() => _userPresetActive ? UserPresetCode : _current.GetValueOrDefault("Splendid");

    /// <summary>Go back to the preset that was in use before a rule took over.</summary>
    private void RestoreFromTweak()
    {
        if (!_tweakApplied) return;
        _tweakApplied = false;
        var back = _tweakBasePreset;
        _tweakBasePreset = null;
        if (back.HasValue && ActivePreset() != back.Value && !_isSyncing && !_isComparing)
        {
            SetStatus($"Per-app tweak ended → {PresetName(back.Value)}");
            SetPresetThread(back.Value, manual: false);
        }
    }
}
