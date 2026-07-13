using System.Drawing;
using System.Windows.Forms;

namespace AsusDisplayControl;

/// <summary>A bordered panel with a centered title on the top edge (like a tk LabelFrame).</summary>
internal sealed class SectionPanel : Panel
{
    private readonly string _title;

    public SectionPanel(string title)
    {
        _title = title;
        DoubleBuffered = true;
        BackColor = Theme.Main;
        // Leave room at the top for the title, and pad the inner content.
        Padding = new Padding(16, 36, 16, 16);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        var rect = new Rectangle(0, 12, Width - 1, Height - 13);

        using (var pen = new Pen(Theme.CardHover))
            g.DrawRectangle(pen, rect);

        var size = TextRenderer.MeasureText(g, _title, Theme.Subtitle);
        int tx = (Width - size.Width) / 2;
        // Punch a gap in the border and draw the title over it.
        using (var bg = new SolidBrush(BackColor))
            g.FillRectangle(bg, tx - 6, 6, size.Width + 12, size.Height);
        TextRenderer.DrawText(g, _title, Theme.Subtitle, new Point(tx, 4), Theme.TextPrimary);
    }
}
