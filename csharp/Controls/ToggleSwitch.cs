using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AsusDisplayControl;

/// <summary>Flat on/off toggle matching the Python ToggleSwitch (44x22 capsule).</summary>
internal sealed class ToggleSwitch : Control
{
    private bool _value;
    private bool _active = true;

    /// <summary>Fired only on a user click (not on programmatic set).</summary>
    public event Action<bool>? Toggled;

    public ToggleSwitch()
    {
        DoubleBuffered = true;
        Size = new Size(44, 22);
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Theme.Card;
    }

    public bool Value => _value;

    public bool Active
    {
        get => _active;
        set { _active = value; Cursor = value ? Cursors.Hand : Cursors.Default; Invalidate(); }
    }

    /// <summary>Set the state without firing Toggled (programmatic sync).</summary>
    public void SetValueSilent(bool v) { _value = v; Invalidate(); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (_active && e.Button == MouseButtons.Left)
        {
            _value = !_value;
            Invalidate();
            Toggled?.Invoke(_value);
        }
        base.OnMouseDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        const int w = 44, h = 22;
        Color track = !_active ? Theme.TroughDisabled : (_value ? Theme.Accent : Theme.Trough);
        Color knob = !_active ? Theme.FillDisabled : Theme.ForeOn(track);

        // Capsule track
        using (var tb = new SolidBrush(track))
        {
            g.FillEllipse(tb, 1, 1, h - 2, h - 2);
            g.FillEllipse(tb, w - (h - 1), 1, h - 2, h - 2);
            g.FillRectangle(tb, h / 2, 1, w - h, h - 2);
        }

        // Knob
        int kr = 7;
        int kx = _value ? (w - 11) : 11;
        int ky = 11;
        using var kb = new SolidBrush(knob);
        g.FillEllipse(kb, kx - kr, ky - kr, kr * 2, kr * 2);
    }
}
