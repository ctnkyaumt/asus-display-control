using System.Drawing;

namespace AsusDisplayControl;

/// <summary>Central palette + fonts, mirroring the original Python style constants.</summary>
internal static class Theme
{
    public static Color Sidebar    = FromHex("#0f172a"); // slate-900
    public static Color Main       = FromHex("#1e293b"); // slate-800
    public static Color Card       = FromHex("#334155"); // slate-700
    public static Color CardHover  = FromHex("#475569"); // slate-600
    public static Color Accent     = FromHex("#2563eb"); // blue-600 (ASUS blue)
    public static Color AccentDark = FromHex("#1d4ed8");
    public static Color TextPrimary= FromHex("#f8fafc"); // slate-50
    public static Color TextMuted  = FromHex("#94a3b8"); // slate-400
    public static Color Disabled   = FromHex("#475569");

    // Slider / toggle secondary tones
    public static Color Trough        = FromHex("#1e293b");
    public static Color TroughDisabled= FromHex("#2d3748");
    public static Color FillDisabled  = FromHex("#4a5568");
    public static Color KnobDisabled  = FromHex("#718096");
    public static Color White         = Color.White;

    public static readonly Font Title    = new("Segoe UI", 16f, FontStyle.Bold);
    public static readonly Font Subtitle = new("Segoe UI", 12f, FontStyle.Bold);
    public static readonly Font Label    = new("Segoe UI", 9.75f, FontStyle.Bold);
    public static readonly Font Value    = new("Segoe UI", 9.75f, FontStyle.Regular);
    public static readonly Font Muted     = new("Segoe UI", 9f, FontStyle.Regular);
    public static readonly Font LogoText = new("Segoe UI", 12f, FontStyle.Bold);
    public static readonly Font LogoIcon = new("Segoe UI Emoji", 20f, FontStyle.Regular);
    public static readonly Font CardIcon = new("Segoe MDL2 Assets", 15f, FontStyle.Regular);
    public static readonly Font CardText = new("Segoe UI", 8f, FontStyle.Bold);

    public static Color FromHex(string hex)
    {
        hex = hex.TrimStart('#');
        return Color.FromArgb(
            Convert.ToInt32(hex.Substring(0, 2), 16),
            Convert.ToInt32(hex.Substring(2, 2), 16),
            Convert.ToInt32(hex.Substring(4, 2), 16));
    }
}
