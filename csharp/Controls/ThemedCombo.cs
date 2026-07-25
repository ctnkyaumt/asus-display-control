using System.Drawing;
using System.Windows.Forms;

namespace AsusDisplayControl;

/// <summary>
/// Flat, owner-drawn combo in the app palette.
///
/// The resize handling matters: a FlatStyle.Flat combo paints its frame and button itself but
/// only invalidates the area it had *before* a resize, so growing or shrinking the window left
/// the old frame on screen next to the new one (the phantom second dropdown). Repainting the
/// whole control — and the strip of parent behind it — on every size change fixes that.
/// </summary>
internal class ThemedCombo : ComboBox
{
    public ThemedCombo(bool editable = false)
    {
        SetStyle(ControlStyles.ResizeRedraw, true);
        DropDownStyle = editable ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
        BackColor = Theme.Trough;
        ForeColor = Theme.ForeOn(Theme.Trough);
        Font = Theme.Muted;
        if (!editable) DrawMode = DrawMode.OwnerDrawFixed;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) { base.OnDrawItem(e); return; }
        bool selected = (e.State & DrawItemState.Selected) != 0;
        var bg = selected ? Theme.Accent : Theme.Trough;
        using (var b = new SolidBrush(bg)) e.Graphics.FillRectangle(b, e.Bounds);
        TextRenderer.DrawText(e.Graphics, Items[e.Index]?.ToString() ?? "", Font, e.Bounds,
                              Theme.ForeOn(bg), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Parent?.Invalidate(Bounds, false);
        Invalidate();
    }
}
