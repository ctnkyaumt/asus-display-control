using System.Drawing;
using System.Windows.Forms;

namespace AsusDisplayControl;

/// <summary>A Splendid preset tile: MDL2 glyph over a label, with hover/active states.</summary>
internal sealed class PresetCard : Panel
{
    public int PresetValue { get; }
    public event Action<int>? Clicked;

    private readonly Label _icon;
    private readonly Label _text;
    private bool _active;

    public PresetCard(string text, string glyph, int value)
    {
        PresetValue = value;
        DoubleBuffered = true;
        BackColor = Theme.Card;
        Margin = new Padding(2);
        Padding = new Padding(2, 4, 2, 4);

        _icon = new Label
        {
            Text = glyph,
            Font = Theme.CardIcon,
            ForeColor = Theme.ForeOn(Theme.Card),
            BackColor = Theme.Card,
            Dock = DockStyle.Top,
            Height = 26,
            TextAlign = ContentAlignment.MiddleCenter,
        };
        _text = new Label
        {
            Text = text,
            Font = Theme.CardText,
            ForeColor = Theme.ForeOn(Theme.Card),
            BackColor = Theme.Card,
            Dock = DockStyle.Top,
            Height = 18,
            TextAlign = ContentAlignment.MiddleCenter,
        };

        Controls.Add(_text);
        Controls.Add(_icon);

        foreach (Control c in new Control[] { this, _icon, _text })
        {
            c.Click += (_, _) => Clicked?.Invoke(PresetValue);
            c.MouseEnter += (_, _) => OnHover(true);
            c.MouseLeave += (_, _) => OnHover(false);
            c.Cursor = Cursors.Hand;
        }
    }

    public void SetActive(bool active)
    {
        _active = active;
        ApplyColor(active ? Theme.Accent : Theme.Card);
    }

    private void OnHover(bool entering)
    {
        if (_active) return;
        // Only repaint hover if the mouse is genuinely over the card (avoids flicker
        // when moving between the child labels).
        bool over = ClientRectangle.Contains(PointToClient(Cursor.Position));
        ApplyColor(entering && over ? Theme.CardHover : Theme.Card);
    }

    private void ApplyColor(Color c)
    {
        BackColor = c;
        _icon.BackColor = c;
        _text.BackColor = c;
        _icon.ForeColor = _text.ForeColor = Theme.ForeOn(c);
    }
}
