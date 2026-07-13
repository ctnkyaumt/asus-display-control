using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AsusDisplayControl;

/// <summary>Flat, canvas-drawn slider matching the Python ModernSlider (0-100, rounded track).</summary>
internal sealed class ModernSlider : Control
{
    private const int Pad = 12;      // horizontal inset of the track
    private const int TrackW = 6;    // track thickness
    private const int Knob = 6;      // knob radius

    public int Minimum { get; set; } = 0;
    public int Maximum { get; set; } = 100;
    private int _value;
    private bool _active = true;
    private bool _dragging;

    /// <summary>Fired continuously while the value changes (drag / click).</summary>
    public event Action<int>? ValueChanging;
    /// <summary>Fired once when the user releases (commit the write).</summary>
    public event Action<int>? ValueCommitted;

    public ModernSlider()
    {
        DoubleBuffered = true;
        Height = 26;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Theme.Card;
    }

    public int Value => _value;

    public bool Active
    {
        get => _active;
        set { _active = value; Invalidate(); }
    }

    /// <summary>Set the value without firing any events (programmatic sync).</summary>
    public void SetValueSilent(int v)
    {
        _value = Math.Max(Minimum, Math.Min(Maximum, v));
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        int w = Width, h = Height;
        float y = h / 2f;

        Color trough = _active ? Theme.Trough : Theme.TroughDisabled;
        Color fill = _active ? Theme.Accent : Theme.FillDisabled;
        Color knob = _active ? Theme.White : Theme.KnobDisabled;

        // Background track
        using (var pen = new Pen(trough, TrackW) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawLine(pen, Pad, y, w - Pad, y);

        // Filled portion
        int span = Maximum - Minimum; if (span == 0) span = 1;
        float pct = (float)(_value - Minimum) / span;
        float x = Pad + pct * (w - 2 * Pad);
        if (x > Pad)
            using (var pen = new Pen(fill, TrackW) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(pen, Pad, y, x, y);

        // Knob
        using (var kb = new SolidBrush(knob))
        using (var kp = new Pen(fill, 2))
        {
            var r = new RectangleF(x - Knob, y - Knob, Knob * 2, Knob * 2);
            g.FillEllipse(kb, r);
            g.DrawEllipse(kp, r);
        }
    }

    private void UpdateFromX(int mouseX)
    {
        int span = Maximum - Minimum;
        float pct = (mouseX - Pad) / (float)(Width - 2 * Pad);
        pct = Math.Max(0f, Math.Min(1f, pct));
        int v = (int)(Minimum + pct * span);
        if (v != _value)
        {
            _value = v;
            Invalidate();
            ValueChanging?.Invoke(_value);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (_active && e.Button == MouseButtons.Left) { _dragging = true; UpdateFromX(e.X); }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging && _active) UpdateFromX(e.X);
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_dragging)
        {
            _dragging = false;
            if (_active) ValueCommitted?.Invoke(_value);
        }
        base.OnMouseUp(e);
    }
}
