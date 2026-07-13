using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace AsusDisplayControl;

/// <summary>Modal dialog to configure automatic preset switching (fixed times or daylight).</summary>
internal sealed class ScheduleForm : Form
{
    private readonly string[] _presetNames;
    private readonly int[] _presetCodes;

    private readonly CheckBox _enable;
    private readonly RadioButton _rbFixed, _rbSun;
    private readonly FlowLayoutPanel _fixedPanel, _rulesPanel, _sunPanel;
    private readonly TextBox _lat, _lon;
    private readonly Label _sunTimes;
    private readonly ComboBox _dayPreset, _nightPreset;

    public ScheduleConfig Result { get; private set; }

    public ScheduleForm(ScheduleConfig config, (string name, int code)[] presets)
    {
        _presetNames = presets.Select(p => p.name).ToArray();
        _presetCodes = presets.Select(p => p.code).ToArray();
        Result = config;

        Text = "Preset Schedule";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(474, 560);
        BackColor = Theme.Main;
        ForeColor = Theme.TextPrimary;
        Font = new Font("Segoe UI", 9f);

        // Bottom buttons
        var buttonBar = new Panel { Dock = DockStyle.Bottom, Height = 52, BackColor = Theme.Main };
        var ok = MakeButton("Save", Theme.Accent, Theme.AccentDark);
        var cancel = MakeButton("Cancel", Theme.Card, Theme.CardHover);
        ok.Click += OnSave;
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        ok.Location = new Point(ClientSize.Width - ok.Width - cancel.Width - 32, 10);
        cancel.Location = new Point(ClientSize.Width - cancel.Width - 16, 10);
        buttonBar.Controls.Add(ok);
        buttonBar.Controls.Add(cancel);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, BackColor = Theme.Main, Padding = new Padding(16), AutoScroll = true };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        root.Controls.Add(Lbl("Automatically switch Splendid presets while the app runs.", Theme.Muted, Theme.TextMuted, 440));

        _enable = new CheckBox { Text = "Enable scheduled preset switching", ForeColor = Theme.TextPrimary, BackColor = Theme.Main, Font = Theme.Label, AutoSize = true, Checked = config.Enabled, Margin = new Padding(0, 8, 0, 8) };
        root.Controls.Add(_enable);

        _rbFixed = new RadioButton { Text = "At fixed times of day", ForeColor = Theme.TextPrimary, BackColor = Theme.Main, AutoSize = true, Checked = config.Mode == ScheduleMode.Fixed };
        _rbSun = new RadioButton { Text = "By daylight  (sunrise / sunset)", ForeColor = Theme.TextPrimary, BackColor = Theme.Main, AutoSize = true, Checked = config.Mode == ScheduleMode.Sun };
        root.Controls.Add(_rbFixed);
        root.Controls.Add(_rbSun);

        // ---- Fixed-times panel ----
        _fixedPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = Theme.Main, Margin = new Padding(0, 6, 0, 0) };
        _fixedPanel.Controls.Add(Lbl("At each time, switch to a preset (wraps past midnight).", Theme.Muted, Theme.TextMuted, 440));
        _rulesPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = Theme.Main };
        _fixedPanel.Controls.Add(_rulesPanel);
        var addBtn = MakeButton("+ Add time", Theme.Card, Theme.CardHover);
        addBtn.Margin = new Padding(0, 6, 0, 0);
        addBtn.Click += (_, _) => _rulesPanel.Controls.Add(MakeRuleRow("12:00", _presetCodes[0]));
        _fixedPanel.Controls.Add(addBtn);

        var seed = (config.Rules != null && config.Rules.Count > 0)
            ? config.Rules
            : new List<TimeRule> { new() { Time = "09:00", Preset = 4 }, new() { Time = "19:00", Preset = 8 } };
        foreach (var r in seed) _rulesPanel.Controls.Add(MakeRuleRow(r.Time, r.Preset));
        root.Controls.Add(_fixedPanel);

        // ---- Sun panel ----
        _sunPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = Theme.Main, Margin = new Padding(0, 6, 0, 0) };
        _sunPanel.Controls.Add(Lbl("Your location in decimal degrees (right-click in Google Maps).", Theme.Muted, Theme.TextMuted, 440));

        var coords = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, BackColor = Theme.Main };
        coords.Controls.Add(Lbl("Latitude", Theme.Value, Theme.TextPrimary, 68, 6));
        _lat = MakeInput(config.Latitude != 0 ? config.Latitude.ToString(CultureInfo.InvariantCulture) : "");
        coords.Controls.Add(_lat);
        coords.Controls.Add(Lbl("Longitude", Theme.Value, Theme.TextPrimary, 78, 6));
        _lon = MakeInput(config.Longitude != 0 ? config.Longitude.ToString(CultureInfo.InvariantCulture) : "");
        coords.Controls.Add(_lon);
        _sunPanel.Controls.Add(coords);

        _sunTimes = Lbl("", Theme.Muted, Theme.Accent, 440);
        _sunTimes.Margin = new Padding(0, 4, 0, 8);
        _sunPanel.Controls.Add(_sunTimes);

        var dayRow = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, BackColor = Theme.Main };
        dayRow.Controls.Add(Lbl("Daytime preset", Theme.Value, Theme.TextPrimary, 100, 6));
        _dayPreset = MakePresetCombo(config.DayPreset);
        dayRow.Controls.Add(_dayPreset);
        _sunPanel.Controls.Add(dayRow);

        var nightRow = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, BackColor = Theme.Main };
        nightRow.Controls.Add(Lbl("Night preset", Theme.Value, Theme.TextPrimary, 100, 6));
        _nightPreset = MakePresetCombo(config.NightPreset);
        nightRow.Controls.Add(_nightPreset);
        _sunPanel.Controls.Add(nightRow);
        root.Controls.Add(_sunPanel);

        Controls.Add(root);
        Controls.Add(buttonBar);

        _rbFixed.CheckedChanged += (_, _) => ApplyMode();
        _lat.TextChanged += (_, _) => UpdateSunTimes();
        _lon.TextChanged += (_, _) => UpdateSunTimes();
        ApplyMode();
        UpdateSunTimes();
    }

    private void ApplyMode()
    {
        _fixedPanel.Visible = _rbFixed.Checked;
        _sunPanel.Visible = !_rbFixed.Checked;
    }

    private void UpdateSunTimes()
    {
        if (TryParseCoord(_lat.Text, out double la) && TryParseCoord(_lon.Text, out double lo))
        {
            var (sr, ss) = Solar.SunriseSunset(DateTime.UtcNow, la, lo);
            string F(DateTime? t) => t == null ? "--:--" : t.Value.ToLocalTime().ToString("HH:mm");
            _sunTimes.Text = $"Today: sunrise {F(sr)} · sunset {F(ss)}  (your local time)";
        }
        else
        {
            _sunTimes.Text = "Enter latitude and longitude to preview today's times.";
        }
    }

    private void OnSave(object? sender, EventArgs e)
    {
        var cfg = new ScheduleConfig
        {
            Enabled = _enable.Checked,
            Mode = _rbFixed.Checked ? ScheduleMode.Fixed : ScheduleMode.Sun,
            DayPreset = _presetCodes[Math.Max(0, _dayPreset.SelectedIndex)],
            NightPreset = _presetCodes[Math.Max(0, _nightPreset.SelectedIndex)],
        };

        foreach (Control row in _rulesPanel.Controls)
        {
            var tb = (MaskedTextBox)row.Controls[0];
            var cb = (ComboBox)row.Controls[1];
            if (TimeSpan.TryParse(tb.Text, out _))
                cfg.Rules.Add(new TimeRule { Time = tb.Text, Preset = _presetCodes[Math.Max(0, cb.SelectedIndex)] });
        }

        if (cfg is { Enabled: true, Mode: ScheduleMode.Sun } &&
            !(TryParseCoord(_lat.Text, out _) && TryParseCoord(_lon.Text, out _)))
        {
            MessageBox.Show(this, "Enter a valid latitude and longitude for the daylight schedule.",
                "Location required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        TryParseCoord(_lat.Text, out double la); cfg.Latitude = la;
        TryParseCoord(_lon.Text, out double lo); cfg.Longitude = lo;

        Result = cfg;
        DialogResult = DialogResult.OK;
        Close();
    }

    // ---- helpers --------------------------------------------------------------
    private static bool TryParseCoord(string s, out double v) =>
        double.TryParse((s ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v != 0;

    private Panel MakeRuleRow(string time, int presetCode)
    {
        var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, BackColor = Theme.Main, Margin = new Padding(0, 0, 0, 4) };
        var tb = new MaskedTextBox { Mask = "00:00", Text = NormalizeTime(time), Width = 52, BackColor = Theme.Trough, ForeColor = Theme.White, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Value, Margin = new Padding(0, 2, 6, 2) };
        var cb = MakePresetCombo(presetCode); cb.Width = 140;
        var rm = new Button { Text = "✕", Width = 28, Height = 24, FlatStyle = FlatStyle.Flat, BackColor = Theme.Card, ForeColor = Theme.White, Margin = new Padding(6, 2, 0, 2) };
        rm.FlatAppearance.BorderSize = 0;
        rm.Click += (_, _) => { _rulesPanel.Controls.Remove(row); row.Dispose(); };
        row.Controls.Add(tb);
        row.Controls.Add(cb);
        row.Controls.Add(rm);
        return row;
    }

    private static string NormalizeTime(string t) => TimeSpan.TryParse(t, out var ts) ? ts.ToString(@"hh\:mm") : "12:00";

    private ComboBox MakePresetCombo(int code)
    {
        var cb = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Trough,
            ForeColor = Theme.White,
            Font = Theme.Muted,
            DrawMode = DrawMode.OwnerDrawFixed,
            Width = 140,
            Margin = new Padding(0, 2, 0, 2),
        };
        cb.Items.AddRange(_presetNames);
        cb.DrawItem += (s, e) =>
        {
            if (e.Index < 0) return;
            var combo = (ComboBox)s!;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(sel ? Theme.Accent : Theme.Trough)) e.Graphics.FillRectangle(b, e.Bounds);
            TextRenderer.DrawText(e.Graphics, combo.Items[e.Index]?.ToString() ?? "", combo.Font, e.Bounds, Theme.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        };
        int idx = Array.IndexOf(_presetCodes, code);
        cb.SelectedIndex = idx >= 0 ? idx : 0;
        return cb;
    }

    private static TextBox MakeInput(string text) => new()
    {
        Text = text,
        Width = 90,
        BackColor = Theme.Trough,
        ForeColor = Theme.White,
        BorderStyle = BorderStyle.FixedSingle,
        Font = Theme.Value,
        Margin = new Padding(0, 2, 12, 2),
    };

    private static Label Lbl(string text, Font f, Color fore, int width, int topPad = 0) => new()
    {
        Text = text,
        Font = f,
        ForeColor = fore,
        BackColor = Theme.Main,
        AutoSize = false,
        Width = width,
        Height = text.Contains('\n') ? 34 : 22,
        Margin = new Padding(0, topPad, 0, 0),
        TextAlign = ContentAlignment.MiddleLeft,
    };

    private static Button MakeButton(string text, Color bg, Color hover)
    {
        var b = new Button
        {
            Text = text,
            Font = Theme.Label,
            ForeColor = Theme.White,
            BackColor = bg,
            FlatStyle = FlatStyle.Flat,
            AutoSize = false,
            Width = 90,
            Height = 32,
            Cursor = Cursors.Hand,
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = hover;
        return b;
    }
}
