using System.Drawing;
using System.Windows.Forms;

namespace AsusDisplayControl;

/// <summary>
/// The two pages that mirror the rest of the monitor's OSD menu: "System Setup"
/// (input, OSD, power, locks, audio, info, resets) and "GamePlus &amp; OSD" (overlays plus
/// an on-screen replacement for the monitor's physical buttons).
///
/// These properties are not part of a Splendid preset, so they are read once per monitor
/// and never written into the preset memory.
/// </summary>
internal sealed partial class MainForm
{
    private static readonly (int code, string label)[] InputSourceOptions =
    {
        (1, "VGA"), (15, "DisplayPort 1"), (16, "DisplayPort 2"), (17, "HDMI-1"), (18, "HDMI-2"),
        (19, "HDMI-3"), (21, "Thunderbolt 1"), (22, "Thunderbolt 2"), (26, "USB-C 1"), (27, "USB-C 2"),
        (30, "SDI-1"), (31, "SDI-2"),
    };

    private static readonly (int code, string label)[] LanguageOptions =
    {
        (1, "Chinese (Traditional)"), (2, "English"), (3, "French"), (4, "German"), (5, "Italian"),
        (6, "Japanese"), (7, "Korean"), (8, "Portuguese"), (9, "Russian"), (10, "Spanish"),
        (12, "Turkish"), (13, "Chinese (Simplified)"), (17, "Croatian"), (18, "Czech"), (20, "Dutch"),
        (26, "Hungarian"), (30, "Polish"), (31, "Romanian"), (35, "Thai"), (36, "Ukrainian"),
        (37, "Vietnamese"), (38, "Persian"), (39, "Indonesian"),
    };

    private static readonly (int code, string label)[] PowerSavingOptions =
        { (0, "Standard / Performance"), (1, "Power Saving") };

    private static readonly (int code, string label)[] FpsOptions =
        { (0, "OFF"), (1, "Numerical"), (2, "Bar Graph") };

    private static readonly (int code, string label)[] TimerOptions =
        { (0, "OFF"), (1, "30s"), (2, "40s"), (3, "50s"), (4, "60s"), (5, "90s") };

    private static readonly (int code, string label)[] CrosshairOptions =
    {
        (0, "OFF"), (7, "Blue dot"), (8, "Green dot"), (9, "Blue target"),
        (10, "Green target"), (11, "Blue crosshair"), (12, "Green crosshair"),
    };

    /// <summary>EZOSD key codes (write-only) — the on-screen version of the monitor's joystick.</summary>
    private static readonly (string label, int code)[] OsdKeys =
    {
        ("Show Menu", 1), ("Back", 7), ("Close Menu", 0),
    };

    // Probed in two batches so the System page fills in without waiting on the GamePlus page.
    private static readonly string[] SystemPageProps =
    {
        "InputSource", "InputDetection", "OSDLanguage", "OSDTransparency", "OSDTimeout",
        "PowerSaving", "PowerIndicator", "PowerKeyLock", "KeyLock", "AudioVolume", "SoundMute",
        "UsageTime",
    };

    private static readonly string[] GamePlusPageProps = { "FPS", "Timer", "Crosshair", "DisplayAlignment" };

    // ==========================================================================
    // SYSTEM SETUP PAGE
    // ==========================================================================
    private static Label PageHeader(string text) => new()
    {
        Text = text,
        Font = Theme.Title,
        ForeColor = Theme.TextPrimary,
        BackColor = Theme.Main,
        AutoSize = true,
        Margin = new Padding(0, 0, 0, 10),
        UseMnemonic = false,   // keep the "&" in the title
    };

    private Control BuildSystemPage()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Main, ColumnCount = 1, RowCount = 4, Padding = new Padding(15) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // header
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // two columns
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // info
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // actions

        root.Controls.Add(PageHeader("System Setup"), 0, 0);

        var cols = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Main };
        cols.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        cols.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        var input = new SectionPanel("Input & OSD") { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0) };
        var it = NewRowsTable();
        int r = 0;
        AddOptionRow(it, r++, "Input Source", "InputSource", InputSourceOptions, system: true);
        AddToggleRow(it, r++, "Auto Input Detect", "InputDetection", system: true);
        AddOptionRow(it, r++, "OSD Language", "OSDLanguage", LanguageOptions, system: true);
        AddSliderRow(it, r++, "OSD Transparency", "OSDTransparency", max: 100, system: true);
        AddSliderRow(it, r++, "OSD Timeout (s)", "OSDTimeout", max: 120, system: true);
        input.Controls.Add(it);

        var power = new SectionPanel("Power, Audio & Locks") { Dock = DockStyle.Fill, Margin = new Padding(10, 0, 0, 0) };
        var pt = NewRowsTable();
        r = 0;
        AddOptionRow(pt, r++, "Power Saving", "PowerSaving", PowerSavingOptions, system: true);
        AddToggleRow(pt, r++, "Power Indicator", "PowerIndicator", system: true);
        AddToggleRow(pt, r++, "Power Key Lock", "PowerKeyLock", system: true);
        AddToggleRow(pt, r++, "Key Lock", "KeyLock", system: true);
        AddSliderRow(pt, r++, "Volume", "AudioVolume", max: 100, system: true);
        AddToggleRow(pt, r++, "Mute", "SoundMute", system: true);
        power.Controls.Add(pt);

        cols.Controls.Add(input, 0, 0);
        cols.Controls.Add(power, 1, 0);
        root.Controls.Add(cols, 0, 1);

        var infoSection = new SectionPanel("Monitor Information") { Dock = DockStyle.Fill, Height = 100, Margin = new Padding(0, 8, 0, 0) };
        _infoLabel = new Label { Text = "--", Font = Theme.Value, ForeColor = Theme.TextMuted, BackColor = Theme.Main, Dock = DockStyle.Fill, AutoSize = false };
        infoSection.Controls.Add(_infoLabel);
        root.Controls.Add(infoSection, 0, 2);

        root.Controls.Add(BuildSystemActions(), 0, 3);
        return root;
    }

    private Control BuildSystemActions()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 44, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, BackColor = Theme.Main, Margin = new Padding(0, 10, 0, 0) };

        var refresh = MakeButton("Refresh", Theme.Card, Theme.CardHover);
        refresh.Click += (_, _) =>
        {
            if (_selectedMonitorId != null) _systemLoaded.Remove(_selectedMonitorId);
            EnsureSystemLoaded();
        };

        var resetMode = MakeButton("Reset Mode", Theme.Card, Theme.CardHover);
        resetMode.Click += (_, _) => ResetThread("reset-mode", "Reset Mode",
            "Reset the current Splendid mode to its factory defaults?");

        var resetColor = MakeButton("Reset Color", Theme.Card, Theme.CardHover);
        resetColor.Click += (_, _) => ResetThread("reset-color", "Reset Color",
            "Reset all color settings (temperature, gains, offsets) to factory defaults?");

        var resetAll = MakeButton("Reset All", Theme.Accent, Theme.AccentDark);
        resetAll.Click += (_, _) => ResetThread("reset-all", "Reset All",
            "Reset every monitor setting to factory defaults?\n\nThis also clears the saved per-preset values in this app.");

        bar.Controls.Add(refresh);
        bar.Controls.Add(resetMode);
        bar.Controls.Add(resetColor);
        bar.Controls.Add(resetAll);
        return bar;
    }

    // ==========================================================================
    // GAMEPLUS + OSD REMOTE PAGE
    // ==========================================================================
    private Control BuildGamePlusPage()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Main, ColumnCount = 1, RowCount = 2, Padding = new Padding(15) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(PageHeader("GamePlus & OSD Remote"), 0, 0);

        var cols = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Main };
        cols.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        cols.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        var overlays = new SectionPanel("GamePlus Overlays") { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0) };
        var t = NewRowsTable();
        int r = 0;
        AddOptionRow(t, r++, "FPS Counter", "FPS", FpsOptions, system: true);
        AddOptionRow(t, r++, "Timer", "Timer", TimerOptions, system: true);
        AddOptionRow(t, r++, "Crosshair", "Crosshair", CrosshairOptions, system: true);
        AddToggleRow(t, r++, "Display Alignment", "DisplayAlignment", system: true);
        overlays.Controls.Add(t);

        cols.Controls.Add(overlays, 0, 0);
        cols.Controls.Add(BuildOsdRemote(), 1, 0);
        root.Controls.Add(cols, 0, 1);
        return root;
    }

    /// <summary>
    /// EZOSD d-pad. Anything the CLI has no property for (Rest Reminder, Color Augmentation,
    /// Aspect Control, Motion Sync, Adaptive-Sync, QuickFit, …) is still reachable by driving
    /// the monitor's own menu from here instead of reaching behind the panel.
    /// </summary>
    private Control BuildOsdRemote()
    {
        var section = new SectionPanel("OSD Remote") { Dock = DockStyle.Fill, Margin = new Padding(10, 0, 0, 0) };

        var wrap = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Theme.Main };
        wrap.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        wrap.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        wrap.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        wrap.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        wrap.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // keeps the pad at the top

        wrap.Controls.Add(new Label
        {
            Text = "Drive the monitor's own menu without reaching for its buttons — useful for " +
                   "settings the CLI cannot set directly (Rest Reminder, Color Augmentation, " +
                   "Aspect Control, Motion Sync, Adaptive-Sync, QuickFit). Not every model answers " +
                   "these commands.",
            Font = Theme.Muted,
            ForeColor = Theme.TextMuted,
            BackColor = Theme.Main,
            Dock = DockStyle.Fill,
            AutoSize = true,
            MaximumSize = new Size(380, 0),
            Margin = new Padding(0, 0, 0, 14),
        }, 0, 0);

        var pad = new TableLayoutPanel { ColumnCount = 3, RowCount = 3, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Theme.Main, Anchor = AnchorStyles.None };
        pad.Controls.Add(MakeOsdKey("▲", 2), 1, 0);
        pad.Controls.Add(MakeOsdKey("◀", 5), 0, 1);
        pad.Controls.Add(MakeOsdKey("OK", 6), 1, 1);
        pad.Controls.Add(MakeOsdKey("▶", 4), 2, 1);
        pad.Controls.Add(MakeOsdKey("▼", 3), 1, 2);
        wrap.Controls.Add(pad, 0, 1);

        var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Theme.Main, Margin = new Padding(0, 14, 0, 0), Anchor = AnchorStyles.None };
        foreach (var (label, code) in OsdKeys)
        {
            var b = MakeButton(label, Theme.Card, Theme.Accent);
            b.Margin = new Padding(4);
            b.Click += (_, _) => SendOsdKey(code);
            row.Controls.Add(b);
        }
        wrap.Controls.Add(row, 0, 2);

        section.Controls.Add(wrap);
        return section;
    }

    private Button MakeOsdKey(string text, int code)
    {
        var b = MakeButton(text, Theme.Card, Theme.Accent);
        b.AutoSize = false;
        b.Size = new Size(62, 36);
        b.Padding = new Padding(0);
        b.Margin = new Padding(4);
        b.Click += (_, _) => SendOsdKey(code);
        return b;
    }

    private void SendOsdKey(int code)
    {
        var mId = _selectedMonitorId;
        if (mId == null) { SetStatus("Error: No monitor selected."); return; }
        Task.Run(() =>
        {
            try { Dwc.Run("set", "EZOSD", code.ToString(), "--id", mId); SetStatus("OSD command sent."); }
            catch (Exception e) { SetStatus($"OSD command failed: {e.Message}"); }
        });
    }

    // ==========================================================================
    // SYSTEM PROPERTY SYNC
    // ==========================================================================
    /// <summary>
    /// Probe the system / GamePlus properties once per monitor. Unsupported codes cost ~1.3s each
    /// on the DDC bus, so this is slow enough to be worth doing lazily, in batches, and only once.
    /// </summary>
    private void EnsureSystemLoaded()
    {
        var mId = _selectedMonitorId;
        if (mId == null || _systemSyncing || _systemLoaded.Contains(mId)) return;
        _systemSyncing = true;
        SetStatus("Reading system settings...");
        Ui(MarkSystemRowsPending);
        Task.Run(QuerySystemSettings);
    }

    /// <summary>Show rows we have not read yet as pending instead of claiming they are unsupported.</summary>
    private void MarkSystemRowsPending()
    {
        foreach (var (prop, row) in _optionRows)
            if (_systemProps.Contains(prop) && !_system.ContainsKey(prop))
            {
                row.Shown = Array.Empty<int>();
                SetComboPlaceholder(row.Box, "Reading...");
            }
    }

    private void QuerySystemSettings()
    {
        var mId = _selectedMonitorId;
        if (mId == null) { _systemSyncing = false; return; }

        try
        {
            // Batch 1: the System page itself, plus the usage counter shown in the info box.
            var page = Dwc.ProbeMany(SystemPageProps, mId);
            string info;
            try { info = FormatInfo(Dwc.Run("info", "--id", mId), page.GetValueOrDefault("UsageTime")); }
            catch (Exception e) { info = $"Could not read monitor info: {e.Message}"; }

            MergeSystem(page);
            Ui(() =>
            {
                _infoLabel.Text = info;
                UpdateRows(system: true);
                MarkSystemRowsPending();
                SetStatus("System settings synchronized. Reading GamePlus...");
            });

            // Batch 2: the GamePlus overlays.
            MergeSystem(Dwc.ProbeMany(GamePlusPageProps, mId));
            _systemLoaded.Add(mId);
            Ui(() => { UpdateRows(system: true); SetStatus("System settings synchronized."); });
        }
        catch (Exception e) { SetStatus($"Error reading system settings: {e.Message}"); }
        finally { _systemSyncing = false; }
    }

    /// <summary>Condense `dwc info` (one "  Key: Value" per line) into two readable lines.</summary>
    private static string FormatInfo(string raw, int? usageHours)
    {
        var fields = new Dictionary<string, string>();
        foreach (var line in raw.Split('\n'))
        {
            int colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (key.Length > 0 && value.Length > 0) fields[key] = value;
        }
        if (fields.Count == 0) return raw.Trim();

        string Get(string k) => fields.GetValueOrDefault(k, "?");
        var first = $"{Get("Model Name")}     S/N {Get("Serial Number")}     {Get("Device ID")}";
        var second = $"Firmware {Get("Firmware Version")}     ASUS support: {Get("ASUS Monitor Support")}";
        if (usageHours.HasValue) second += $"     Usage: {usageHours.Value} h";
        return first + "\n" + second;
    }

    private void MergeSystem(Dictionary<string, int?> values)
    {
        var merged = new Dictionary<string, int?>(_system);
        foreach (var kv in values) merged[kv.Key] = kv.Value;
        _system = merged;
    }

    /// <summary>Write a system property. Unlike picture props these are not saved per preset.</summary>
    private void SetSystemThread(string prop, int val)
    {
        var mId = _selectedMonitorId;
        if (mId == null) { SetStatus("Error: No monitor selected."); return; }
        if (_systemSyncing) { SetStatus("Still reading system settings — try again in a moment."); UpdateRows(system: true); return; }

        // Switching the input cuts this PC's picture, and only the monitor's own buttons bring it back.
        if (prop == "InputSource" &&
            MessageBox.Show(this,
                "Switching the input source will disconnect this PC's picture.\n" +
                "You may need the monitor's physical buttons to switch back.\n\nContinue?",
                "Change Input Source", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            UpdateRows(system: true);   // put the combo back where it was
            return;
        }

        _system[prop] = val;
        SetStatus($"Updating {prop} to {val}...");
        Task.Run(() =>
        {
            try { Dwc.Run("set", prop, val.ToString(), "--id", mId); SetStatus($"{prop} updated successfully."); }
            catch (Exception e) { SetStatus($"Error updating {prop}: {e.Message}"); }
        });
    }
}
