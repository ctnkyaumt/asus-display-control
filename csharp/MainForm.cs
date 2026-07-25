using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;

namespace AsusDisplayControl;

internal sealed partial class MainForm : Form
{
    // ---- constants -----------------------------------------------------------
    private const int PresetTransitionMs = 50; // settle after Splendid switch before corrective writes
    private const int PresetGainMs = 50;       // settle after ColorTemp before RGB gains

    /// <summary>Picture properties: re-read on every sync and remembered per Splendid preset.</summary>
    private static readonly string[] PictureProps =
        { "Splendid", "Brightness", "Contrast", "Overdrive", "Sharpness", "ShadowBoost", "ASCR",
          "BlueLightFilter", "Saturation", "Hue", "ColorTemp", "RedGain", "GreenGain", "BlueGain",
          "RedOffset", "GreenOffset", "BlueOffset" };

    private static readonly (int code, string label)[] ShadowOptions =
        { (0, "OFF"), (1, "Level 1"), (2, "Level 2"), (3, "Level 3") };

    private static readonly (int code, string label)[] BlueLightOptions =
        { (0, "OFF"), (1, "Level 1"), (2, "Level 2"), (3, "Level 3"), (4, "Level 4") };

    private static readonly string[] GainProps = { "RedGain", "GreenGain", "BlueGain" };

    private static readonly (int code, string label)[] TempMaster =
    {
        (3, "4000K"), (4, "5000K"), (5, "6500K (Warm)"), (6, "7500K"),
        (7, "8200K"), (8, "9300K (Cool)"), (9, "10000K"), (11, "User"),
    };

    /// <summary>
    /// Not a monitor mode: Splendid has no User slot, so this is the app's own preset. It
    /// rides on top of a base Splendid mode and keeps its own remembered values.
    /// </summary>
    public const int UserPresetCode = 100;
    private const string BaseModeKey = "BaseSplendid";

    private static readonly (string name, int glyph, int val)[] PresetDefs =
    {
        ("Standard", 0xE7F4, 4), ("Reading", 0xE82F, 7), ("Theater", 0xE8B2, 1),
        ("Scenery", 0xEB9F, 2), ("Game", 0xE7FC, 5), ("sRGB", 0xE790, 3),
        ("Darkroom", 0xEA80, 8), ("Night View", 0xEC46, 6), ("User", 0xE713, UserPresetCode),
    };

    // ---- state ---------------------------------------------------------------
    private string? _selectedMonitorId;
    private Dictionary<string, int?> _current = new();
    private Dictionary<int, Dictionary<string, int>> _presetMemory;
    private int? _currentPreset, _previousPreset;
    private readonly Dictionary<string, List<string>> _supportedProps = new();
    private readonly Dictionary<string, List<int>?> _supportedTemps = new();
    private volatile bool _isSyncing, _isComparing;
    private bool _updatingUi;
    private int[] _tempCodes;
    private string[] _tempValues;
    private readonly AppSettings _settings;

    // ---- system / GamePlus pages --------------------------------------------
    private Dictionary<string, int?> _system = new();
    private readonly HashSet<string> _systemProps = new();   // props that live on the System / GamePlus pages
    private readonly HashSet<string> _systemLoaded = new();  // monitor IDs whose system props were probed
    private Control _displayPage = null!, _systemPage = null!, _gamePlusPage = null!;
    private readonly List<(Label nav, Control page)> _navItems = new();
    private Label _infoLabel = null!;
    private volatile bool _systemSyncing;

    // ---- scheduling ----------------------------------------------------------
    private ScheduleConfig _schedule;
    private System.Windows.Forms.Timer? _scheduleTimer;
    private int? _lastScheduledPreset;
    private bool _firstSyncDone;

    // ---- per-app tweak -------------------------------------------------------
    private AppTweakConfig _tweaks;
    private System.Windows.Forms.Timer? _tweakTimer;
    private string? _foregroundApp;
    private int? _tweakBasePreset;   // preset to fall back to when no rule matches
    private bool _tweakApplied;

    /// <summary>True while the app's own User preset is the active one (the monitor has no such mode).</summary>
    private bool _userPresetActive;
    private int? _compareCard;   // tile to highlight while press-and-hold compare is running

    // ---- tray / window -------------------------------------------------------
    private readonly Icon _appIcon;
    private NotifyIcon? _tray;
    private ToolStripMenuItem? _trayStartupItem;
    private readonly bool _startMinimized;
    private bool _shownOnce, _reallyExit;

    // ---- controls ------------------------------------------------------------
    private ComboBox _monitorCombo = null!;
    private Label _status = null!;
    private ToggleSwitch _startupSwitch = null!, _traySwitch = null!, _themeSwitch = null!;
    private List<Dwc.Monitor> _monitors = new();
    private bool _rebuilding;
    private Button _compareBtn = null!;
    private readonly Dictionary<int, PresetCard> _cards = new();

    // Property rows, keyed by CLI property name. Everything on a page is registered here,
    // so syncing the UI is just "walk the rows and push the values in".
    private sealed class OptionRow
    {
        public ComboBox Box = null!;
        public int[] Codes = Array.Empty<int>();      // base value list
        public string[] Labels = Array.Empty<string>();
        public int[] Shown = Array.Empty<int>();      // what is currently in the combo
    }
    private readonly Dictionary<string, (ModernSlider slider, Label value)> _sliderRows = new();
    private readonly Dictionary<string, OptionRow> _optionRows = new();
    private readonly Dictionary<string, ToggleSwitch> _toggleRows = new();

    public MainForm(bool startMinimized)
    {
        _startMinimized = startMinimized;
        _settings = AppConfig.LoadSettings();
        _schedule = AppConfig.LoadSchedule();
        _presetMemory = AppConfig.LoadPresets();
        _tweaks = AppConfig.LoadTweaks();
        _appIcon = AppConfig.LoadIcon();
        _tempCodes = TempMaster.Select(t => t.code).ToArray();
        _tempValues = TempMaster.Select(t => t.label).ToArray();

        Theme.Apply(!_settings.LightTheme);
        BuildUi();
        Icon = _appIcon;
        SetupTray();
        _ = Handle;                 // force handle creation so BeginInvoke works while hidden
        ApplyTitleBarTheme();
        DetectMonitors();

        // Re-check the schedule every minute (switches are applied on the UI thread).
        _scheduleTimer = new System.Windows.Forms.Timer { Interval = 60_000 };
        _scheduleTimer.Tick += (_, _) => EvaluateSchedule();
        _scheduleTimer.Start();

        // Watch the foreground window for per-app preset rules.
        _tweakTimer = new System.Windows.Forms.Timer { Interval = 1_000 };
        _tweakTimer.Tick += (_, _) => EvaluateAppTweak();
        _tweakTimer.Start();
    }

    // ==========================================================================
    // UI CONSTRUCTION
    // ==========================================================================
    private void BuildUi()
    {
        Text = "ASUS Display Control Panel";
        if (!_rebuilding)
        {
            ClientSize = new Size(1100, 680);
            StartPosition = FormStartPosition.CenterScreen;
        }
        MinimumSize = new Size(1016, 679);
        BackColor = Theme.Main;
        Font = new Font("Segoe UI", 9f);
        DoubleBuffered = true;

        var main = BuildMainPanel();
        var sidebar = BuildSidebar();
        Controls.Add(main);
        Controls.Add(sidebar);      // added last -> docks first (reserves the left column)
    }

    /// <summary>
    /// Switch palettes. Controls capture their colours when built, so the cheapest correct
    /// answer is to throw the tree away and build it again, then push the cached values back.
    /// </summary>
    private void ApplyTheme(bool light)
    {
        _settings.LightTheme = light;
        AppConfig.SaveSettings(_settings);

        int page = _navItems.FindIndex(n => n.page.Visible);
        _rebuilding = true;
        SuspendLayout();
        var old = Controls.Cast<Control>().ToArray();
        Controls.Clear();
        foreach (var c in old) c.Dispose();

        _sliderRows.Clear();
        _optionRows.Clear();
        _toggleRows.Clear();
        _systemProps.Clear();
        _cards.Clear();
        _navItems.Clear();
        _tweakRows.Clear();

        Theme.Apply(dark: !light);
        BuildUi();
        ResumeLayout(true);
        _rebuilding = false;
        ApplyTitleBarTheme();

        // Repopulate from the values we already hold — no monitor round-trip needed.
        RestoreMonitorList();
        RebuildTempOptions();
        UpdateRows(system: false);
        UpdateRows(system: true);
        MarkSystemRowsPending();
        UpdateActiveCard();
        BuildTweakRows();
        if (page > 0 && page < _navItems.Count) ShowPage(_navItems[page].page);
        SetStatus(light ? "Light theme applied." : "Dark theme applied.");
    }

    /// <summary>Match the window frame to the palette (Windows 10 2004+ / 11).</summary>
    private void ApplyTitleBarTheme()
    {
        try
        {
            int dark = Theme.IsDark ? 1 : 0;
            _ = Native.DwmSetWindowAttribute(Handle, 20 /* USE_IMMERSIVE_DARK_MODE */, ref dark, sizeof(int));
        }
        catch { }
    }

    private Control BuildSidebar()
    {
        var side = new TableLayoutPanel
        {
            Dock = DockStyle.Left,
            Width = 220,
            BackColor = Theme.Sidebar,
            ColumnCount = 1,
            Padding = new Padding(15, 12, 15, 10),
        };
        side.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // Logo row
        var logo = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Theme.Sidebar, Margin = new Padding(0, 6, 0, 12) };
        logo.Controls.Add(new Label { Text = "🖥️", Font = Theme.LogoIcon, ForeColor = Theme.Accent, BackColor = Theme.Sidebar, AutoSize = true, Margin = new Padding(0, 0, 8, 0) });
        logo.Controls.Add(new Label { Text = "ASUS\nDisplayWidget", Font = Theme.LogoText, ForeColor = Theme.TextPrimary, BackColor = Theme.Sidebar, AutoSize = true });

        var selLbl = L("SELECT MONITOR", Theme.Muted, Theme.TextMuted, Theme.Sidebar);
        selLbl.Margin = new Padding(0, 8, 0, 2);

        _monitorCombo = MakeCombo();
        _monitorCombo.Dock = DockStyle.Fill;
        _monitorCombo.Margin = new Padding(0, 0, 0, 12);
        _monitorCombo.SelectedIndexChanged += OnMonitorSelected;

        var nav = new TableLayoutPanel { ColumnCount = 1, Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Theme.Sidebar, Margin = new Padding(0, 0, 0, 5) };
        nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        nav.Controls.Add(MakeNavItem("Splendid", _displayPage));
        nav.Controls.Add(MakeNavItem("System Setup", _systemPage));
        nav.Controls.Add(MakeNavItem("GamePlus & OSD", _gamePlusPage));
        nav.Controls.Add(MakeNavItem("Per-App Tweak", _tweakPage));

        // Settings block
        _startupSwitch = new ToggleSwitch { BackColor = Theme.Sidebar };
        _startupSwitch.SetValueSilent(AppConfig.IsStartupEnabled());
        _startupSwitch.Toggled += on => { AppConfig.SetStartup(on); };

        _traySwitch = new ToggleSwitch { BackColor = Theme.Sidebar };
        _traySwitch.SetValueSilent(_settings.MinimizeToTray);
        _traySwitch.Toggled += on => { _settings.MinimizeToTray = on; AppConfig.SaveSettings(_settings); };

        _themeSwitch = new ToggleSwitch { BackColor = Theme.Sidebar };
        _themeSwitch.SetValueSilent(_settings.LightTheme);
        // Rebuilding disposes this switch, so hand the work to the message loop first.
        _themeSwitch.Toggled += on => BeginInvoke(() => ApplyTheme(on));

        var settings = new TableLayoutPanel { ColumnCount = 1, Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Theme.Sidebar, Margin = new Padding(0, 8, 0, 4) };
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        settings.Controls.Add(L("SETTINGS", Theme.Muted, Theme.TextMuted, Theme.Sidebar));
        settings.Controls.Add(MakeToggleRow("Start with Windows", _startupSwitch));
        settings.Controls.Add(MakeToggleRow("Close to tray", _traySwitch));
        settings.Controls.Add(MakeToggleRow("Light theme", _themeSwitch));

        _status = new Label { Text = "Initializing...", Font = Theme.Muted, ForeColor = Theme.TextMuted, BackColor = Theme.Sidebar, Dock = DockStyle.Fill, AutoSize = false, Height = 60, TextAlign = ContentAlignment.TopLeft };

        side.Controls.Add(logo, 0, 0);
        side.Controls.Add(selLbl, 0, 1);
        side.Controls.Add(_monitorCombo, 0, 2);
        side.Controls.Add(nav, 0, 3);
        side.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = Theme.Sidebar }, 0, 4); // spacer
        side.Controls.Add(settings, 0, 5);
        side.Controls.Add(_status, 0, 6);
        side.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        side.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        side.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        side.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        side.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        side.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        side.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        return side;
    }

    /// <summary>Hosts the pages; the sidebar nav swaps which one is visible.</summary>
    private Control BuildMainPanel()
    {
        var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Main };
        _displayPage = BuildDisplayPage();
        _systemPage = BuildSystemPage();
        _gamePlusPage = BuildGamePlusPage();
        _tweakPage = BuildTweakPage();
        foreach (var page in new[] { _systemPage, _gamePlusPage, _tweakPage, _displayPage })
        {
            page.Dock = DockStyle.Fill;
            page.Visible = page == _displayPage;
            host.Controls.Add(page);
        }
        return host;
    }

    private Label MakeNavItem(string text, Control page)
    {
        var bg = page == _displayPage ? Theme.Accent : Theme.Sidebar;
        var l = new Label
        {
            Text = "  " + text,
            Font = Theme.Label,
            ForeColor = Theme.ForeOn(bg),
            BackColor = bg,
            Dock = DockStyle.Fill,
            Height = 30,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 2, 0, 2),
            Cursor = Cursors.Hand,
            UseMnemonic = false,   // keep the "&" in "GamePlus & OSD"
        };
        l.Click += (_, _) => ShowPage(page);
        _navItems.Add((l, page));
        return l;
    }

    private void ShowPage(Control page)
    {
        foreach (var (nav, p) in _navItems)
        {
            nav.BackColor = p == page ? Theme.Accent : Theme.Sidebar;
            nav.ForeColor = Theme.ForeOn(nav.BackColor);
            p.Visible = p == page;
        }
        if (page == _systemPage || page == _gamePlusPage) EnsureSystemLoaded();
    }

    private Control BuildDisplayPage()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Main, ColumnCount = 1, RowCount = 4, Padding = new Padding(15) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // Header
        root.Controls.Add(new Label { Text = "Splendid Presets", Font = Theme.Title, ForeColor = Theme.TextPrimary, BackColor = Theme.Main, AutoSize = true, Margin = new Padding(0, 0, 0, 10) }, 0, 0);

        // Preset cards row
        var presetRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = PresetDefs.Length, RowCount = 1, Height = 66, BackColor = Theme.Main, Margin = new Padding(0, 0, 0, 10) };
        for (int i = 0; i < PresetDefs.Length; i++)
        {
            presetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / PresetDefs.Length));
            var (name, glyph, val) = PresetDefs[i];
            var card = new PresetCard(name, char.ConvertFromUtf32(glyph), val) { Dock = DockStyle.Fill };
            card.Clicked += v => SetPresetThread(v);
            _cards[val] = card;
            presetRow.Controls.Add(card, i, 0);
        }
        root.Controls.Add(presetRow, 0, 1);

        // Two settings columns
        var cols = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Main };
        cols.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        cols.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        cols.Controls.Add(BuildImageSection(), 0, 0);
        cols.Controls.Add(BuildColorSection(), 1, 0);
        root.Controls.Add(cols, 0, 2);

        // Actions row
        root.Controls.Add(BuildActions(), 0, 3);
        return root;
    }

    private Control BuildImageSection()
    {
        var section = new SectionPanel("Image Settings") { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0) };
        var t = NewRowsTable();
        int r = 0;
        AddSliderRow(t, r++, "Brightness", "Brightness");
        AddSliderRow(t, r++, "Contrast", "Contrast");
        AddSliderRow(t, r++, "Trace Free", "Overdrive");
        AddSliderRow(t, r++, "Sharpness", "Sharpness");
        AddOptionRow(t, r++, "Shadow Boost", "ShadowBoost", ShadowOptions);
        AddOptionRow(t, r++, "Blue Light Filter", "BlueLightFilter", BlueLightOptions);
        AddToggleRow(t, r++, "ASCR", "ASCR");
        section.Controls.Add(t);
        return section;
    }

    private Control BuildColorSection()
    {
        var section = new SectionPanel("Color Settings") { Dock = DockStyle.Fill, Margin = new Padding(10, 0, 0, 0) };
        var t = NewRowsTable();
        int r = 0;
        AddSliderRow(t, r++, "Saturation", "Saturation");
        AddSliderRow(t, r++, "Hue", "Hue");
        AddOptionRow(t, r++, "Color Temp.", "ColorTemp", TempMaster);
        AddSliderRow(t, r++, "Red Gain", "RedGain");
        AddSliderRow(t, r++, "Green Gain", "GreenGain");
        AddSliderRow(t, r++, "Blue Gain", "BlueGain");
        AddSliderRow(t, r++, "Red Offset", "RedOffset");
        AddSliderRow(t, r++, "Green Offset", "GreenOffset");
        AddSliderRow(t, r++, "Blue Offset", "BlueOffset");
        section.Controls.Add(t);
        return section;
    }

    private Control BuildActions()
    {
        var bar = new Panel { Dock = DockStyle.Fill, Height = 44, BackColor = Theme.Main, Margin = new Padding(0, 12, 0, 0) };

        var left = new FlowLayoutPanel { Dock = DockStyle.Left, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, BackColor = Theme.Main };
        var reset = MakeButton("Reset Mode", Theme.Card, Theme.CardHover);
        reset.Click += (_, _) => ResetThread("reset-mode", "Reset Mode",
            "Reset the current Splendid mode to its factory defaults?");
        _compareBtn = MakeButton("Compare Settings", Theme.Card, Theme.Accent);
        _compareBtn.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) StartCompare(); };
        _compareBtn.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left) StopCompare(); };
        var scheduleBtn = MakeButton("Schedule…", Theme.Card, Theme.CardHover);
        scheduleBtn.Click += (_, _) => OpenSchedule();
        left.Controls.Add(reset);
        left.Controls.Add(_compareBtn);
        left.Controls.Add(scheduleBtn);

        var right = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, BackColor = Theme.Main };
        var import = MakeButton("Import Profile", Theme.Card, Theme.CardHover);
        import.Click += (_, _) => ImportProfile();
        var export = MakeButton("Export Profile", Theme.Accent, Theme.AccentDark);
        export.Click += (_, _) => ExportProfile();
        right.Controls.Add(import);
        right.Controls.Add(export);

        bar.Controls.Add(left);
        bar.Controls.Add(right);
        return bar;
    }

    // ---- small UI builders ---------------------------------------------------
    private static Label L(string text, Font f, Color fore, Color bg, bool autoSize = true) =>
        new() { Text = text, Font = f, ForeColor = fore, BackColor = bg, AutoSize = autoSize };

    private static TableLayoutPanel NewRowsTable()
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 3, BackColor = Theme.Main };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        return t;
    }

    private void AddSliderRow(TableLayoutPanel t, int row, string label, string prop, int max = 100, bool system = false)
    {
        var l = L(label, Theme.Label, Theme.TextPrimary, Theme.Main);
        l.Anchor = AnchorStyles.Left;
        l.Margin = new Padding(0, 8, 6, 8);
        var slider = new ModernSlider { Dock = DockStyle.Fill, Margin = new Padding(4, 6, 4, 6), Maximum = max };
        var v = L("--", Theme.Value, Theme.TextMuted, Theme.Main);
        v.Anchor = AnchorStyles.Right;
        v.Margin = new Padding(6, 8, 0, 8);
        slider.ValueChanging += val => v.Text = val.ToString();
        slider.ValueCommitted += val => SetProp(prop, val);
        t.Controls.Add(l, 0, row);
        t.Controls.Add(slider, 1, row);
        t.Controls.Add(v, 2, row);
        Register(prop, system);
        _sliderRows[prop] = (slider, v);
    }

    private void AddOptionRow(TableLayoutPanel t, int row, string label, string prop,
                              (int code, string label)[] options, bool system = false)
    {
        var combo = MakeCombo();
        var entry = new OptionRow
        {
            Box = combo,
            Codes = options.Select(o => o.code).ToArray(),
            Labels = options.Select(o => o.label).ToArray(),
        };
        combo.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingUi || !combo.Enabled) return;
            int idx = combo.SelectedIndex;
            if (idx >= 0 && idx < entry.Shown.Length) SetProp(prop, entry.Shown[idx]);
        };
        AddControlRow(t, row, label, combo, fill: true);
        Register(prop, system);
        _optionRows[prop] = entry;
    }

    private void AddToggleRow(TableLayoutPanel t, int row, string label, string prop, bool system = false)
    {
        var sw = new ToggleSwitch { BackColor = Theme.Main };
        sw.Toggled += on => SetProp(prop, on ? 1 : 0);
        AddControlRow(t, row, label, sw);
        Register(prop, system);
        _toggleRows[prop] = sw;
    }

    private void Register(string prop, bool system)
    {
        if (system) _systemProps.Add(prop);
    }

    private void AddControlRow(TableLayoutPanel t, int row, string label, Control ctrl, bool fill = false)
    {
        var l = L(label, Theme.Label, Theme.TextPrimary, Theme.Main);
        l.Anchor = AnchorStyles.Left;
        l.Margin = new Padding(0, 10, 6, 10);
        if (fill) { ctrl.Dock = DockStyle.Fill; ctrl.Margin = new Padding(4, 8, 0, 8); }
        else { ctrl.Anchor = AnchorStyles.Left; ctrl.Margin = new Padding(4, 8, 0, 8); }
        t.Controls.Add(l, 0, row);
        t.Controls.Add(ctrl, 1, row);
        if (fill) t.SetColumnSpan(ctrl, 2);
    }

    private Control MakeToggleRow(string text, ToggleSwitch sw)
    {
        var row = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Dock = DockStyle.Top, AutoSize = true, BackColor = Theme.Sidebar, Margin = new Padding(0, 3, 0, 3) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var l = L(text, Theme.Muted, Theme.TextPrimary, Theme.Sidebar);
        l.Anchor = AnchorStyles.Left;
        sw.Anchor = AnchorStyles.Right;
        row.Controls.Add(l, 0, 0);
        row.Controls.Add(sw, 1, 0);
        return row;
    }

    private ComboBox MakeCombo() => new ThemedCombo();

    private static Button MakeButton(string text, Color bg, Color hover)
    {
        var b = new Button
        {
            Text = text,
            Font = Theme.Label,
            ForeColor = Theme.ForeOn(bg),
            BackColor = bg,
            FlatStyle = FlatStyle.Flat,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 6, 12, 6),
            Margin = new Padding(0, 0, 10, 0),
            Cursor = Cursors.Hand,
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = hover;
        return b;
    }

    // ==========================================================================
    // TRAY + WINDOW LIFECYCLE
    // ==========================================================================
    private void SetupTray()
    {
        var menu = new ContextMenuStrip();
        var open = new ToolStripMenuItem("Open ASUS Display Control", null, (_, _) => ShowWindow()) { Font = new Font(menu.Font, FontStyle.Bold) };
        _trayStartupItem = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleStartupFromTray());
        var exit = new ToolStripMenuItem("Exit", null, (_, _) => RealExit());
        menu.Items.Add(open);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_trayStartupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exit);
        menu.Opening += (_, _) => _trayStartupItem.Checked = AppConfig.IsStartupEnabled();

        _tray = new NotifyIcon { Icon = _appIcon, Text = AppConfig.AppName, Visible = true, ContextMenuStrip = menu };
        _tray.DoubleClick += (_, _) => ShowWindow();
    }

    private void ToggleStartupFromTray()
    {
        bool now = !AppConfig.IsStartupEnabled();
        AppConfig.SetStartup(now);
        Ui(() => _startupSwitch.SetValueSilent(now));
    }

    private void ShowWindow()
    {
        // Note: don't toggle ShowInTaskbar at runtime — WinForms recreates the window
        // handle when it changes, which is unstable mid-close. A hidden window already
        // has no taskbar button, so Show()/Hide() is enough.
        _shownOnce = true;
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    private void HideToTray()
    {
        Hide();
        SetStatus("Minimized to tray. Right-click the tray icon to exit.");
    }

    private void RealExit()
    {
        _reallyExit = true;
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
        Close();
    }

    protected override void SetVisibleCore(bool value)
    {
        // Start hidden in the tray when launched with --tray.
        if (!_shownOnce && _startMinimized && _tray != null)
        {
            _shownOnce = true;
            base.SetVisibleCore(false);
            return;
        }
        _shownOnce = true;
        base.SetVisibleCore(value);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_reallyExit && _tray != null && _settings.MinimizeToTray && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        base.OnFormClosing(e);
    }

    // ==========================================================================
    // THREAD MARSHALLING
    // ==========================================================================
    private void Ui(Action a)
    {
        if (IsDisposed) return;
        try { if (InvokeRequired) BeginInvoke(a); else a(); }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    private void SetStatus(string msg) => Ui(() => _status.Text = msg);

    // ==========================================================================
    // MONITOR DETECTION + QUERY
    // ==========================================================================
    private void DetectMonitors()
    {
        SetStatus("Searching for ASUS monitors...");
        Task.Run(() =>
        {
            try
            {
                var list = Dwc.ParseList(Dwc.Run("list"));
                if (list.Count == 0) { SetStatus("No monitors found. Check connection."); return; }
                Ui(() =>
                {
                    _monitors = list;
                    _updatingUi = true;
                    _monitorCombo.Items.Clear();
                    _monitorCombo.Items.AddRange(list.Select(m => $"{m.Id} - {m.Model}").ToArray());
                    _updatingUi = false;
                    _monitorCombo.SelectedIndex = 0; // fires OnMonitorSelected -> query
                });
            }
            catch (Exception e) { SetStatus($"Error: {e.Message}"); }
        });
    }

    /// <summary>Repopulate the monitor combo after a rebuild, without re-querying the monitor.</summary>
    private void RestoreMonitorList()
    {
        if (_monitors.Count == 0) return;
        _updatingUi = true;
        _monitorCombo.Items.Clear();
        _monitorCombo.Items.AddRange(_monitors.Select(m => $"{m.Id} - {m.Model}").ToArray());
        int idx = _monitors.FindIndex(m => m.Id == _selectedMonitorId);
        _monitorCombo.SelectedIndex = idx >= 0 ? idx : 0;
        _updatingUi = false;
    }

    private void OnMonitorSelected(object? sender, EventArgs e)
    {
        if (_updatingUi) return;
        if (_monitorCombo.SelectedItem is not string txt || txt.Length == 0) return;
        _selectedMonitorId = txt.Split(" - ")[0];

        // The system pages belong to the previous monitor — blank them until re-probed.
        _system = new();
        _infoLabel.Text = "--";
        UpdateRows(system: true);

        QuerySettingsThread();
        if (!_displayPage.Visible) EnsureSystemLoaded();
    }

    private void QuerySettingsThread()
    {
        if (_isSyncing) return;
        _isSyncing = true;
        SetStatus("Syncing with monitor settings...");
        Task.Run(QuerySettings);
    }

    private void QuerySettings()
    {
        var mId = _selectedMonitorId;
        if (mId == null) { _isSyncing = false; return; }

        var settings = new Dictionary<string, int?>();

        if (!_supportedProps.ContainsKey(mId))
        {
            // First sync for this monitor: probe every property.
            var results = Dwc.ProbeMany(PictureProps, mId);

            var supported = new List<string>();
            foreach (var p in PictureProps)
            {
                var v = results.GetValueOrDefault(p);
                settings[p] = v;
                if (v.HasValue) supported.Add(p);
            }
            _supportedProps[mId] = supported;
            DetectColorTempSupport(mId);
        }
        else
        {
            foreach (var p in _supportedProps[mId])
            {
                if (p is "RedGain" or "GreenGain" or "BlueGain")
                {
                    int? ct = settings.GetValueOrDefault("ColorTemp") ?? _current.GetValueOrDefault("ColorTemp");
                    if (ct != 11) { settings[p] = null; continue; }
                }
                settings[p] = Dwc.GetInt(p, mId);
            }
            foreach (var p in PictureProps)
                if (!settings.ContainsKey(p)) settings[p] = null;
        }

        var oldPreset = _current.GetValueOrDefault("Splendid");
        var newPreset = settings.GetValueOrDefault("Splendid");

        if (newPreset.HasValue)
        {
            if (!_currentPreset.HasValue) _currentPreset = newPreset;
            else if (_currentPreset != newPreset) { _previousPreset = _currentPreset; _currentPreset = newPreset; }
        }

        _current = settings;

        // Capture a baseline for presets we haven't seen yet. While the app's own User preset
        // is active the monitor still reports its base mode, so keep writing into User's slot.
        int? memoryKey = _userPresetActive ? UserPresetCode : newPreset;
        if (memoryKey.HasValue && !_presetMemory.ContainsKey(memoryKey.Value))
        {
            var d = new Dictionary<string, int>();
            foreach (var kv in settings)
                if (kv.Value.HasValue && kv.Key != "Splendid") d[kv.Key] = kv.Value.Value;
            if (_userPresetActive && newPreset.HasValue) d[BaseModeKey] = newPreset.Value;
            _presetMemory[memoryKey.Value] = d;
            AppConfig.SavePresets(_presetMemory);
        }

        Ui(UpdateUiState);
    }

    private void DetectColorTempSupport(string mId)
    {
        List<int>? codes = null;
        try { codes = Dwc.ParseSupportedColorTemps(Dwc.Run("getcaps", "--id", mId)); }
        catch { codes = null; }
        _supportedTemps[mId] = codes;
        SanitizePresetsForCaps(codes);
    }

    private void SanitizePresetsForCaps(List<int>? supported)
    {
        if (supported == null || supported.Count == 0) return;
        bool changed = false;
        foreach (var preset in _presetMemory.Keys.ToList())
        {
            var props = _presetMemory[preset];
            if (props.TryGetValue("ColorTemp", out int ct) && !supported.Contains(ct))
            {
                props.Remove("ColorTemp");
                changed = true;
            }
        }
        if (changed) AppConfig.SavePresets(_presetMemory);
    }

    // ==========================================================================
    // UI STATE SYNC
    // ==========================================================================
    private void UpdateUiState()
    {
        _isSyncing = false;
        SetStatus("Settings synchronized.");

        UpdateActiveCard();
        RebuildTempOptions();
        UpdateRows(system: false);

        // Apply the schedule once the first real sync has populated the current preset.
        if (!_firstSyncDone) { _firstSyncDone = true; EvaluateSchedule(); }
    }

    /// <summary>Highlight the active tile — the User tile wins, since the monitor reports its base mode.</summary>
    private void UpdateActiveCard()
    {
        int? active = _compareCard ?? (_userPresetActive ? UserPresetCode : _current.GetValueOrDefault("Splendid"));
        foreach (var kv in _cards) kv.Value.SetActive(kv.Key == active);
    }

    /// <summary>Push the current values into every registered row of one page group.</summary>
    private void UpdateRows(bool system)
    {
        var src = system ? _system : _current;
        foreach (var (prop, row) in _sliderRows)
            if (_systemProps.Contains(prop) == system) UpdateSlider(row.slider, row.value, src.GetValueOrDefault(prop));
        foreach (var (prop, row) in _optionRows)
            if (_systemProps.Contains(prop) == system) UpdateOption(row, src.GetValueOrDefault(prop));
        foreach (var (prop, sw) in _toggleRows)
            if (_systemProps.Contains(prop) == system) UpdateToggle(sw, src.GetValueOrDefault(prop));
    }

    private void UpdateOption(OptionRow row, int? value)
    {
        if (!value.HasValue) { row.Shown = Array.Empty<int>(); SetComboUnsupported(row.Box); return; }

        var codes = row.Codes.ToList();
        var labels = row.Labels.ToList();
        int idx = codes.IndexOf(value.Value);
        if (idx < 0)
        {
            // Monitor reports a level outside the documented list — show it rather than hide it.
            codes.Add(value.Value);
            labels.Add($"Value {value.Value}");
            idx = codes.Count - 1;
        }
        row.Shown = codes.ToArray();
        SetComboItems(row.Box, labels.ToArray(), idx);
    }

    private static void UpdateToggle(ToggleSwitch sw, int? value)
    {
        if (value.HasValue) { sw.Active = true; sw.SetValueSilent(value.Value == 1); }
        else { sw.SetValueSilent(false); sw.Active = false; }
    }

    private void UpdateSlider(ModernSlider slider, Label label, int? v)
    {
        if (v.HasValue)
        {
            slider.Active = true;
            slider.SetValueSilent(v.Value);
            label.Text = v.Value.ToString();
            label.ForeColor = Theme.TextPrimary;
        }
        else
        {
            slider.SetValueSilent(0);
            slider.Active = false;
            label.Text = "--";
            label.ForeColor = Theme.Disabled;
        }
    }

    private void RebuildTempOptions()
    {
        var map = TempMaster.ToDictionary(t => t.code, t => t.label);
        List<int> codes = (_selectedMonitorId != null &&
                           _supportedTemps.TryGetValue(_selectedMonitorId, out var c) && c is { Count: > 0 })
            ? c : TempMaster.Select(t => t.code).ToList();
        _tempCodes = codes.Where(map.ContainsKey).ToArray();
        _tempValues = _tempCodes.Select(k => map[k]).ToArray();
        if (_optionRows.TryGetValue("ColorTemp", out var row)) { row.Codes = _tempCodes; row.Labels = _tempValues; }
    }

    private void SetComboItems(ComboBox cb, string[] items, int sel)
    {
        _updatingUi = true;
        cb.BeginUpdate();
        cb.Items.Clear();
        cb.Items.AddRange(items);
        cb.Enabled = true;
        cb.SelectedIndex = (sel >= 0 && sel < items.Length) ? sel : -1;
        cb.EndUpdate();
        _updatingUi = false;
    }

    private void SetComboUnsupported(ComboBox cb) => SetComboPlaceholder(cb, "Unsupported");

    private void SetComboPlaceholder(ComboBox cb, string text)
    {
        _updatingUi = true;
        cb.Items.Clear();
        cb.Items.Add(text);
        cb.SelectedIndex = 0;
        cb.Enabled = false;
        _updatingUi = false;
    }

    // ==========================================================================
    // SET OPERATIONS
    // ==========================================================================
    /// <summary>Route a row edit: picture props are remembered per preset, system props are not.</summary>
    private void SetProp(string prop, int val)
    {
        if (_systemProps.Contains(prop)) SetSystemThread(prop, val);
        else SetVcpThread(prop, val);
    }

    private void SetVcpThread(string prop, int val)
    {
        if (_selectedMonitorId == null) { SetStatus("Error: No monitor selected."); return; }
        if (_isSyncing || _isComparing) return;

        _current[prop] = val;
        int? preset = _userPresetActive ? UserPresetCode : _current.GetValueOrDefault("Splendid");
        if (preset.HasValue)
        {
            if (!_presetMemory.TryGetValue(preset.Value, out var d)) { d = new(); _presetMemory[preset.Value] = d; }
            d[prop] = val;
            AppConfig.SavePresets(_presetMemory);
        }

        SetStatus($"Updating {prop} to {val}...");
        Task.Run(() =>
        {
            try { Dwc.Run("set", prop, val.ToString(), "--id", _selectedMonitorId!); SetStatus($"{prop} updated successfully."); }
            catch (Exception e) { SetStatus($"Error updating {prop}: {e.Message}"); }
        });
    }

    /// <summary>
    /// <paramref name="manual"/> is false for switches the app makes itself (per-app rules);
    /// only a real pick may redefine what a per-app rule falls back to.
    /// </summary>
    private void SetPresetThread(int val, bool manual = true)
    {
        if (_selectedMonitorId == null) { SetStatus("Error: No monitor selected."); return; }
        if (_isSyncing || _isComparing) return;

        if (_currentPreset.HasValue && _currentPreset != val) { _previousPreset = _currentPreset; _currentPreset = val; }
        _userPresetActive = val == UserPresetCode;
        if (manual && _tweakApplied) _tweakBasePreset = val;

        SetStatus(val == UserPresetCode ? "Applying User preset..." : $"Changing Splendid mode to {val}...");
        _isSyncing = true;
        foreach (var c in _cards.Values) c.SetActive(false);
        if (_cards.TryGetValue(val, out var card)) card.SetActive(true);

        Task.Run(() => SetPreset(val));
    }

    /// <summary>The Splendid mode the User preset sits on top of (its own memory, else the current one).</summary>
    private int UserBaseMode()
    {
        if (_presetMemory.TryGetValue(UserPresetCode, out var mem) && mem.TryGetValue(BaseModeKey, out int b)) return b;
        return _current.GetValueOrDefault("Splendid") ?? 4;   // 4 = Standard
    }

    private void WritePropsParallel(Dictionary<string, int> props)
    {
        if (props.Count == 0) return;
        Dwc.WriteMany(props.Select(kv => (kv.Key, kv.Value)).ToArray(), _selectedMonitorId!);
    }

    private void ApplyPresetSettingsParallel(int val)
    {
        if (!_presetMemory.TryGetValue(val, out var saved) || saved.Count == 0) return;

        // BaseSplendid is bookkeeping for the User preset, not a monitor property.
        var first = saved.Where(kv => kv.Key != BaseModeKey && !GainProps.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
        WritePropsParallel(first);

        var gains = saved.Where(kv => GainProps.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
        if (gains.Count > 0)
        {
            if (first.ContainsKey("ColorTemp")) Thread.Sleep(PresetGainMs);
            WritePropsParallel(gains);
        }
    }

    private void SetPreset(int val)
    {
        try
        {
            // The User preset is ours, not the monitor's: put the panel in its base mode first.
            int mode = val == UserPresetCode ? UserBaseMode() : val;
            Dwc.Run("set", "Splendid", mode.ToString(), "--id", _selectedMonitorId!);
            Thread.Sleep(PresetTransitionMs);
            ApplyPresetSettingsParallel(val);
            QuerySettings();
        }
        catch (Exception e)
        {
            SetStatus($"Error: {e.Message}");
            _isSyncing = false;
        }
    }

    // ==========================================================================
    // COMPARE (press-and-hold)
    // ==========================================================================
    private void StartCompare()
    {
        if (_selectedMonitorId == null || _isSyncing || !_previousPreset.HasValue) return;
        _isComparing = true;
        _compareCard = _previousPreset;
        SetStatus("Comparing: Showing previous preset...");
        _compareBtn.BackColor = Theme.Accent;
        _compareBtn.Text = "Comparing...";
        foreach (var c in _cards.Values) c.SetActive(false);
        if (_cards.TryGetValue(_previousPreset.Value, out var card)) card.SetActive(true);
        Task.Run(() => SetPresetCompare(_previousPreset.Value));
    }

    private void StopCompare()
    {
        if (_selectedMonitorId == null || !_isComparing) return;
        _isComparing = false;
        _compareCard = null;
        SetStatus("Restoring current preset...");
        _compareBtn.BackColor = Theme.Card;
        _compareBtn.Text = "Compare Settings";
        foreach (var c in _cards.Values) c.SetActive(false);
        if (_currentPreset.HasValue && _cards.TryGetValue(_currentPreset.Value, out var card)) card.SetActive(true);
        _isSyncing = true;
        Task.Run(() => SetPreset(_currentPreset!.Value));
    }

    private void SetPresetCompare(int val)
    {
        try
        {
            int mode = val == UserPresetCode ? UserBaseMode() : val;
            Dwc.Run("set", "Splendid", mode.ToString(), "--id", _selectedMonitorId!);
            Thread.Sleep(PresetTransitionMs);
            ApplyPresetSettingsParallel(val);

            var presetVals = new Dictionary<string, int?>(_current) { ["Splendid"] = val };
            if (_presetMemory.TryGetValue(val, out var mem))
                foreach (var kv in mem) presetVals[kv.Key] = kv.Value;
            else
                foreach (var k in presetVals.Keys.ToList())
                    if (k != "Splendid") presetVals[k] = null;

            var actual = new Dictionary<string, int?>(_current);
            Ui(() =>
            {
                _current = presetVals;
                UpdateUiState();
                _current = actual;
                SetStatus("Comparing: Showing previous preset (Release to restore)...");
            });
        }
        catch (Exception e) { SetStatus($"Error comparing: {e.Message}"); }
    }

    // ==========================================================================
    // SCHEDULING
    // ==========================================================================
    private void OpenSchedule()
    {
        var presets = PresetDefs.Select(p => (p.name, p.val)).ToArray();
        using var dlg = new ScheduleForm(_schedule, presets);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _schedule = dlg.Result;
            AppConfig.SaveSchedule(_schedule);
            _lastScheduledPreset = null;   // force a fresh evaluation against the new config
            EvaluateSchedule();
        }
    }

    /// <summary>
    /// Runs on the UI thread (startup, timer tick, config change). Switches presets only
    /// when the scheduled target changes, so it never fights a manual choice mid-window.
    /// </summary>
    private void EvaluateSchedule()
    {
        if (_schedule is not { Enabled: true } || _selectedMonitorId == null) return;

        int? desired = _schedule.DesiredPreset(DateTime.Now);
        if (desired == null || desired == _lastScheduledPreset) return;
        if (_isSyncing || _isComparing) return;    // busy — retry next tick without recording

        _lastScheduledPreset = desired;
        if (ActivePreset() == desired) return;                     // already in that preset
        if (!_cards.ContainsKey(desired.Value)) return;

        SetStatus($"Scheduled switch → {PresetName(desired.Value)}");
        SetPresetThread(desired.Value);
    }

    private static string PresetName(int code)
    {
        foreach (var p in PresetDefs) if (p.val == code) return p.name;
        return code.ToString();
    }

    // ==========================================================================
    // RESET / IMPORT / EXPORT
    // ==========================================================================
    /// <summary>kind is one of the CLI reset commands: reset-all, reset-color, reset-mode.</summary>
    private void ResetThread(string kind, string title, string question)
    {
        if (_selectedMonitorId == null) { SetStatus("Error: No monitor selected."); return; }
        if (_isSyncing || _isComparing) return;
        if (MessageBox.Show(this, question, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _isSyncing = true;
        SetStatus("Resetting monitor settings...");
        Task.Run(() =>
        {
            try
            {
                // The monitor forgets the tweaks, so drop the matching preset memory too.
                if (kind == "reset-all") { _presetMemory.Clear(); AppConfig.SavePresets(_presetMemory); _userPresetActive = false; }
                else
                {
                    var preset = _current.GetValueOrDefault("Splendid");
                    if (preset.HasValue && _presetMemory.Remove(preset.Value)) AppConfig.SavePresets(_presetMemory);
                }
                Dwc.Run(kind, "--id", _selectedMonitorId!);
                Thread.Sleep(2000);
                QuerySettings();
                if (kind == "reset-all") { _systemLoaded.Remove(_selectedMonitorId!); QuerySystemSettings(); }
            }
            catch (Exception e) { SetStatus($"Error: {e.Message}"); _isSyncing = false; }
        });
    }

    private void ExportProfile()
    {
        if (_selectedMonitorId == null) { SetStatus("Error: No monitor selected."); return; }
        if (_current.Count == 0) return;
        using var dlg = new SaveFileDialog { DefaultExt = "json", Filter = "JSON Files|*.json", Title = "Export Settings Profile" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(_current, new JsonSerializerOptions { WriteIndented = true }));
            SetStatus("Profile exported successfully.");
        }
        catch (Exception e) { MessageBox.Show(this, $"Could not export profile: {e.Message}", "Export Failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void ImportProfile()
    {
        if (_selectedMonitorId == null) { SetStatus("Error: No monitor selected."); return; }
        if (_isSyncing) return;
        using var dlg = new OpenFileDialog { Filter = "JSON Files|*.json", Title = "Import Settings Profile" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var imported = JsonSerializer.Deserialize<Dictionary<string, int?>>(File.ReadAllText(dlg.FileName)) ?? new();
            _isSyncing = true;
            SetStatus("Applying imported profile...");
            Task.Run(() => ApplyImportedProfile(imported));
        }
        catch (Exception e)
        {
            MessageBox.Show(this, $"Could not import profile: {e.Message}", "Import Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _isSyncing = false;
        }
    }

    private void ApplyImportedProfile(Dictionary<string, int?> dict)
    {
        try
        {
            var mId = _selectedMonitorId!;
            var importedPreset = dict.GetValueOrDefault("Splendid");
            if (importedPreset.HasValue)
            {
                try { Dwc.Run("set", "Splendid", importedPreset.Value.ToString(), "--id", mId); Thread.Sleep(PresetTransitionMs); }
                catch { }
            }

            var currentPreset = importedPreset ?? _current.GetValueOrDefault("Splendid");
            if (currentPreset.HasValue)
            {
                if (!_presetMemory.TryGetValue(currentPreset.Value, out var d)) { d = new(); _presetMemory[currentPreset.Value] = d; }
                foreach (var kv in dict)
                    if (kv.Value.HasValue && kv.Key != "Splendid") d[kv.Key] = kv.Value.Value;
                AppConfig.SavePresets(_presetMemory);
            }

            // Dependency order: ColorTemp first, RGB gains last.
            var ordered = new List<(string, int)>();
            if (dict.TryGetValue("ColorTemp", out var ctv) && ctv.HasValue) ordered.Add(("ColorTemp", ctv.Value));
            foreach (var kv in dict)
                if (kv.Value.HasValue && kv.Key is not ("ColorTemp" or "RedGain" or "GreenGain" or "BlueGain" or "Splendid"))
                    ordered.Add((kv.Key, kv.Value.Value));
            foreach (var g in GainProps)
                if (dict.TryGetValue(g, out var gv) && gv.HasValue) ordered.Add((g, gv.Value));

            foreach (var (p, v) in ordered)
            {
                try { Dwc.Run("set", p, v.ToString(), "--id", mId); Thread.Sleep(50); } catch { }
            }
            QuerySettings();
        }
        catch (Exception e) { SetStatus($"Error applying profile: {e.Message}"); _isSyncing = false; }
    }
}
