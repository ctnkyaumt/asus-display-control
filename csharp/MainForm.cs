using System.Collections.Concurrent;
using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;

namespace AsusDisplayControl;

internal sealed class MainForm : Form
{
    // ---- constants -----------------------------------------------------------
    private const int PresetTransitionMs = 50; // settle after Splendid switch before corrective writes
    private const int PresetGainMs = 50;       // settle after ColorTemp before RGB gains

    private static readonly string[] AllProps =
        { "Splendid", "Brightness", "Contrast", "Overdrive", "ShadowBoost", "ASCR",
          "Saturation", "Hue", "ColorTemp", "RedGain", "GreenGain", "BlueGain" };

    private static readonly string[] ShadowLevels = { "OFF", "Level 1", "Level 2", "Level 3" };
    private static readonly string[] GainProps = { "RedGain", "GreenGain", "BlueGain" };

    private static readonly (int code, string label)[] TempMaster =
    {
        (3, "4000K"), (4, "5000K"), (5, "6500K (Warm)"), (6, "7500K"),
        (7, "8200K"), (8, "9300K (Cool)"), (9, "10000K"), (11, "User"),
    };

    private static readonly (string name, int glyph, int val)[] PresetDefs =
    {
        ("Standard", 0xE7F4, 4), ("Reading", 0xE82F, 7), ("Theater", 0xE8B2, 1),
        ("Scenery", 0xEB9F, 2), ("Game", 0xE7FC, 5), ("sRGB", 0xE790, 3),
        ("Darkroom", 0xEA80, 8), ("Night View", 0xEC46, 6),
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

    // ---- scheduling ----------------------------------------------------------
    private ScheduleConfig _schedule;
    private System.Windows.Forms.Timer? _scheduleTimer;
    private int? _lastScheduledPreset;
    private bool _firstSyncDone;

    // ---- tray / window -------------------------------------------------------
    private readonly Icon _appIcon;
    private NotifyIcon? _tray;
    private ToolStripMenuItem? _trayStartupItem;
    private readonly bool _startMinimized;
    private bool _shownOnce, _reallyExit;

    // ---- controls ------------------------------------------------------------
    private ComboBox _monitorCombo = null!, _shadowCombo = null!, _tempCombo = null!;
    private Label _status = null!;
    private ToggleSwitch _ascr = null!, _startupSwitch = null!, _traySwitch = null!;
    private ModernSlider _brightness = null!, _contrast = null!, _overdrive = null!,
                         _saturation = null!, _hue = null!, _rGain = null!, _gGain = null!, _bGain = null!;
    private Label _vBrightness = null!, _vContrast = null!, _vOverdrive = null!,
                  _vSaturation = null!, _vHue = null!, _vR = null!, _vG = null!, _vB = null!;
    private Button _compareBtn = null!;
    private readonly Dictionary<int, PresetCard> _cards = new();

    public MainForm(bool startMinimized)
    {
        _startMinimized = startMinimized;
        _settings = AppConfig.LoadSettings();
        _schedule = AppConfig.LoadSchedule();
        _presetMemory = AppConfig.LoadPresets();
        _appIcon = AppConfig.LoadIcon();
        _tempCodes = TempMaster.Select(t => t.code).ToArray();
        _tempValues = TempMaster.Select(t => t.label).ToArray();

        BuildUi();
        Icon = _appIcon;
        SetupTray();
        _ = Handle;                 // force handle creation so BeginInvoke works while hidden
        DetectMonitors();

        // Re-check the schedule every minute (switches are applied on the UI thread).
        _scheduleTimer = new System.Windows.Forms.Timer { Interval = 60_000 };
        _scheduleTimer.Tick += (_, _) => EvaluateSchedule();
        _scheduleTimer.Start();
    }

    // ==========================================================================
    // UI CONSTRUCTION
    // ==========================================================================
    private void BuildUi()
    {
        Text = "ASUS Display Control Panel";
        ClientSize = new Size(1100, 680);
        MinimumSize = new Size(1016, 679);
        BackColor = Theme.Main;
        Font = new Font("Segoe UI", 9f);
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;

        var main = BuildMainPanel();
        var sidebar = BuildSidebar();
        Controls.Add(main);
        Controls.Add(sidebar);      // added last -> docks first (reserves the left column)
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
        logo.Controls.Add(new Label { Text = "ASUS\nDisplayWidget", Font = Theme.LogoText, ForeColor = Theme.White, BackColor = Theme.Sidebar, AutoSize = true });

        var selLbl = L("SELECT MONITOR", Theme.Muted, Theme.TextMuted, Theme.Sidebar);
        selLbl.Margin = new Padding(0, 8, 0, 2);

        _monitorCombo = MakeCombo();
        _monitorCombo.Dock = DockStyle.Fill;
        _monitorCombo.Margin = new Padding(0, 0, 0, 12);
        _monitorCombo.SelectedIndexChanged += OnMonitorSelected;

        var splendid = new Label { Text = "  Splendid", Font = Theme.Label, ForeColor = Theme.White, BackColor = Theme.Accent, Dock = DockStyle.Fill, Height = 30, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 2, 0, 5) };

        // Settings block
        _startupSwitch = new ToggleSwitch { BackColor = Theme.Sidebar };
        _startupSwitch.SetValueSilent(AppConfig.IsStartupEnabled());
        _startupSwitch.Toggled += on => { AppConfig.SetStartup(on); };

        _traySwitch = new ToggleSwitch { BackColor = Theme.Sidebar };
        _traySwitch.SetValueSilent(_settings.MinimizeToTray);
        _traySwitch.Toggled += on => { _settings.MinimizeToTray = on; AppConfig.SaveSettings(_settings); };

        var settings = new TableLayoutPanel { ColumnCount = 1, Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Theme.Sidebar, Margin = new Padding(0, 8, 0, 4) };
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        settings.Controls.Add(L("SETTINGS", Theme.Muted, Theme.TextMuted, Theme.Sidebar));
        settings.Controls.Add(MakeToggleRow("Start with Windows", _startupSwitch));
        settings.Controls.Add(MakeToggleRow("Close to tray", _traySwitch));

        _status = new Label { Text = "Initializing...", Font = Theme.Muted, ForeColor = Theme.TextMuted, BackColor = Theme.Sidebar, Dock = DockStyle.Fill, AutoSize = false, Height = 60, TextAlign = ContentAlignment.TopLeft };

        side.Controls.Add(logo, 0, 0);
        side.Controls.Add(selLbl, 0, 1);
        side.Controls.Add(_monitorCombo, 0, 2);
        side.Controls.Add(splendid, 0, 3);
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

    private Control BuildMainPanel()
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
            card.Clicked += SetPresetThread;
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
        (_brightness, _vBrightness) = AddSliderRow(t, r++, "Brightness", "Brightness");
        (_contrast, _vContrast) = AddSliderRow(t, r++, "Contrast", "Contrast");
        (_overdrive, _vOverdrive) = AddSliderRow(t, r++, "Trace Free", "Overdrive");

        _shadowCombo = MakeCombo();
        _shadowCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingUi || !_shadowCombo.Enabled) return;
            int idx = _shadowCombo.SelectedIndex;
            if (idx >= 0 && idx < 4) SetVcpThread("ShadowBoost", idx);
        };
        AddComboRow(t, r++, "Shadow Boost", _shadowCombo);

        _ascr = new ToggleSwitch { BackColor = Theme.Main };
        _ascr.Toggled += on => SetVcpThread("ASCR", on ? 1 : 0);
        AddControlRow(t, r++, "ASCR", _ascr);

        section.Controls.Add(t);
        return section;
    }

    private Control BuildColorSection()
    {
        var section = new SectionPanel("Color Settings") { Dock = DockStyle.Fill, Margin = new Padding(10, 0, 0, 0) };
        var t = NewRowsTable();
        int r = 0;
        (_saturation, _vSaturation) = AddSliderRow(t, r++, "Saturation", "Saturation");
        (_hue, _vHue) = AddSliderRow(t, r++, "Hue", "Hue");

        _tempCombo = MakeCombo();
        _tempCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingUi) return;
            if (_tempCombo.SelectedItem is not string txt) return;
            int idx = Array.IndexOf(_tempValues, txt);
            if (idx >= 0) SetVcpThread("ColorTemp", _tempCodes[idx]);
        };
        AddComboRow(t, r++, "Color Temp.", _tempCombo);

        (_rGain, _vR) = AddSliderRow(t, r++, "Red Gain", "RedGain");
        (_gGain, _vG) = AddSliderRow(t, r++, "Green Gain", "GreenGain");
        (_bGain, _vB) = AddSliderRow(t, r++, "Blue Gain", "BlueGain");

        section.Controls.Add(t);
        return section;
    }

    private Control BuildActions()
    {
        var bar = new Panel { Dock = DockStyle.Fill, Height = 44, BackColor = Theme.Main, Margin = new Padding(0, 12, 0, 0) };

        var left = new FlowLayoutPanel { Dock = DockStyle.Left, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, BackColor = Theme.Main };
        var reset = MakeButton("Reset Mode", Theme.Card, Theme.CardHover);
        reset.Click += (_, _) => ResetThread();
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

    private (ModernSlider, Label) AddSliderRow(TableLayoutPanel t, int row, string label, string prop)
    {
        var l = L(label, Theme.Label, Theme.TextPrimary, Theme.Main);
        l.Anchor = AnchorStyles.Left;
        l.Margin = new Padding(0, 8, 6, 8);
        var slider = new ModernSlider { Dock = DockStyle.Fill, Margin = new Padding(4, 6, 4, 6) };
        var v = L("--", Theme.Value, Theme.TextMuted, Theme.Main);
        v.Anchor = AnchorStyles.Right;
        v.Margin = new Padding(6, 8, 0, 8);
        slider.ValueChanging += val => v.Text = val.ToString();
        slider.ValueCommitted += val => SetVcpThread(prop, val);
        t.Controls.Add(l, 0, row);
        t.Controls.Add(slider, 1, row);
        t.Controls.Add(v, 2, row);
        return (slider, v);
    }

    private void AddComboRow(TableLayoutPanel t, int row, string label, ComboBox combo)
    {
        var l = L(label, Theme.Label, Theme.TextPrimary, Theme.Main);
        l.Anchor = AnchorStyles.Left;
        l.Margin = new Padding(0, 10, 6, 10);
        combo.Dock = DockStyle.Fill;
        combo.Margin = new Padding(4, 8, 0, 8);
        t.Controls.Add(l, 0, row);
        t.Controls.Add(combo, 1, row);
        t.SetColumnSpan(combo, 2);
    }

    private void AddControlRow(TableLayoutPanel t, int row, string label, Control ctrl)
    {
        var l = L(label, Theme.Label, Theme.TextPrimary, Theme.Main);
        l.Anchor = AnchorStyles.Left;
        l.Margin = new Padding(0, 10, 6, 10);
        ctrl.Anchor = AnchorStyles.Left;
        ctrl.Margin = new Padding(4, 8, 0, 8);
        t.Controls.Add(l, 0, row);
        t.Controls.Add(ctrl, 1, row);
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

    private ComboBox MakeCombo()
    {
        var cb = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Trough,
            ForeColor = Theme.White,
            Font = Theme.Muted,
            DrawMode = DrawMode.OwnerDrawFixed,
        };
        cb.DrawItem += (s, e) =>
        {
            if (e.Index < 0) return;
            var combo = (ComboBox)s!;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(sel ? Theme.Accent : Theme.Trough)) e.Graphics.FillRectangle(b, e.Bounds);
            TextRenderer.DrawText(e.Graphics, combo.Items[e.Index]?.ToString() ?? "", combo.Font, e.Bounds, Theme.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        };
        return cb;
    }

    private static Button MakeButton(string text, Color bg, Color hover)
    {
        var b = new Button
        {
            Text = text,
            Font = Theme.Label,
            ForeColor = Theme.White,
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
                var vals = list.Select(m => $"{m.Id} - {m.Model}").ToArray();
                Ui(() =>
                {
                    _updatingUi = true;
                    _monitorCombo.Items.Clear();
                    _monitorCombo.Items.AddRange(vals);
                    _updatingUi = false;
                    _monitorCombo.SelectedIndex = 0; // fires OnMonitorSelected -> query
                });
            }
            catch (Exception e) { SetStatus($"Error: {e.Message}"); }
        });
    }

    private void OnMonitorSelected(object? sender, EventArgs e)
    {
        if (_updatingUi) return;
        if (_monitorCombo.SelectedItem is not string txt || txt.Length == 0) return;
        _selectedMonitorId = txt.Split(" - ")[0];
        QuerySettingsThread();
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
            // First sync for this monitor: probe every property in parallel.
            var results = new ConcurrentDictionary<string, int?>();
            Task.WaitAll(AllProps.Select(p => Task.Run(() => results[p] = Dwc.GetInt(p, mId))).ToArray());

            var supported = new List<string>();
            foreach (var p in AllProps)
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
            foreach (var p in AllProps)
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

        // Capture a baseline for presets we haven't seen yet.
        if (newPreset.HasValue && !_presetMemory.ContainsKey(newPreset.Value))
        {
            var d = new Dictionary<string, int>();
            foreach (var kv in settings)
                if (kv.Value.HasValue && kv.Key != "Splendid") d[kv.Key] = kv.Value.Value;
            _presetMemory[newPreset.Value] = d;
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

        var active = _current.GetValueOrDefault("Splendid");
        foreach (var kv in _cards) kv.Value.SetActive(kv.Key == active);

        UpdateSlider(_brightness, _vBrightness, "Brightness");
        UpdateSlider(_contrast, _vContrast, "Contrast");
        UpdateSlider(_overdrive, _vOverdrive, "Overdrive");

        var sb = _current.GetValueOrDefault("ShadowBoost");
        if (sb.HasValue && sb.Value >= 0 && sb.Value < 4) SetComboItems(_shadowCombo, ShadowLevels, sb.Value);
        else SetComboUnsupported(_shadowCombo);

        var ascr = _current.GetValueOrDefault("ASCR");
        if (ascr.HasValue) { _ascr.Active = true; _ascr.SetValueSilent(ascr.Value == 1); }
        else { _ascr.SetValueSilent(false); _ascr.Active = false; }

        UpdateSlider(_saturation, _vSaturation, "Saturation");
        UpdateSlider(_hue, _vHue, "Hue");

        RebuildTempOptions();
        var ct = _current.GetValueOrDefault("ColorTemp");
        int idx = ct.HasValue ? Array.IndexOf(_tempCodes, ct.Value) : -1;
        if (idx >= 0) SetComboItems(_tempCombo, _tempValues, idx);
        else SetComboUnsupported(_tempCombo);

        UpdateSlider(_rGain, _vR, "RedGain");
        UpdateSlider(_gGain, _vG, "GreenGain");
        UpdateSlider(_bGain, _vB, "BlueGain");

        // Apply the schedule once the first real sync has populated the current preset.
        if (!_firstSyncDone) { _firstSyncDone = true; EvaluateSchedule(); }
    }

    private void UpdateSlider(ModernSlider slider, Label label, string prop)
    {
        var v = _current.GetValueOrDefault(prop);
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

    private void SetComboUnsupported(ComboBox cb)
    {
        _updatingUi = true;
        cb.Items.Clear();
        cb.Items.Add("Unsupported");
        cb.SelectedIndex = 0;
        cb.Enabled = false;
        _updatingUi = false;
    }

    // ==========================================================================
    // SET OPERATIONS
    // ==========================================================================
    private void SetVcpThread(string prop, int val)
    {
        if (_selectedMonitorId == null) { SetStatus("Error: No monitor selected."); return; }
        if (_isSyncing || _isComparing) return;

        _current[prop] = val;
        var preset = _current.GetValueOrDefault("Splendid");
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

    private void SetPresetThread(int val)
    {
        if (_selectedMonitorId == null) { SetStatus("Error: No monitor selected."); return; }
        if (_isSyncing || _isComparing) return;

        if (_currentPreset.HasValue && _currentPreset != val) { _previousPreset = _currentPreset; _currentPreset = val; }

        SetStatus($"Changing Splendid mode to {val}...");
        _isSyncing = true;
        foreach (var c in _cards.Values) c.SetActive(false);
        if (_cards.TryGetValue(val, out var card)) card.SetActive(true);

        Task.Run(() => SetPreset(val));
    }

    private void WritePropsParallel(Dictionary<string, int> props)
    {
        Task.WaitAll(props.Select(kv => Task.Run(() =>
        {
            try { Dwc.Run("set", kv.Key, kv.Value.ToString(), "--id", _selectedMonitorId!); } catch { }
        })).ToArray());
    }

    private void ApplyPresetSettingsParallel(int val)
    {
        if (!_presetMemory.TryGetValue(val, out var saved) || saved.Count == 0) return;

        var first = saved.Where(kv => !GainProps.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
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
            Dwc.Run("set", "Splendid", val.ToString(), "--id", _selectedMonitorId!);
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
            Dwc.Run("set", "Splendid", val.ToString(), "--id", _selectedMonitorId!);
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
        if (_current.GetValueOrDefault("Splendid") == desired) return; // already in that preset
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
    private void ResetThread()
    {
        if (_selectedMonitorId == null) { SetStatus("Error: No monitor selected."); return; }
        if (_isSyncing || _isComparing) return;
        if (MessageBox.Show(this, "Are you sure you want to reset the current display settings to factory default?",
                "Reset Mode", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _isSyncing = true;
        SetStatus("Resetting monitor settings...");
        Task.Run(() =>
        {
            try
            {
                var preset = _current.GetValueOrDefault("Splendid");
                if (preset.HasValue && _presetMemory.Remove(preset.Value)) AppConfig.SavePresets(_presetMemory);
                Dwc.Run("reset-all", "--id", _selectedMonitorId!);
                Thread.Sleep(2000);
                QuerySettings();
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
