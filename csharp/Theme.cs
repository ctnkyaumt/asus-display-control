using System.Drawing;

namespace AsusDisplayControl;

/// <summary>
/// Central palette + fonts. The colours are mutable: <see cref="Apply"/> swaps the whole
/// palette and the form rebuilds its controls, since every control reads these at build /
/// paint time.
/// </summary>
internal static class Theme
{
    public static bool IsDark { get; private set; } = true;

    public static Color Sidebar, Main, Card, CardHover, Accent, AccentDark;
    public static Color TextPrimary, TextMuted, Disabled;
    public static Color Trough, TroughDisabled, FillDisabled, KnobDisabled;

    public static readonly Color White = Color.White;

    public static readonly Font Title    = new("Segoe UI", 16f, FontStyle.Bold);
    public static readonly Font Subtitle = new("Segoe UI", 12f, FontStyle.Bold);
    public static readonly Font Label    = new("Segoe UI", 9.75f, FontStyle.Bold);
    public static readonly Font Value    = new("Segoe UI", 9.75f, FontStyle.Regular);
    public static readonly Font Muted     = new("Segoe UI", 9f, FontStyle.Regular);
    public static readonly Font LogoText = new("Segoe UI", 12f, FontStyle.Bold);
    public static readonly Font LogoIcon = new("Segoe UI Emoji", 20f, FontStyle.Regular);
    public static readonly Font CardIcon = new("Segoe MDL2 Assets", 15f, FontStyle.Regular);
    public static readonly Font CardText = new("Segoe UI", 8f, FontStyle.Bold);

    static Theme() => Apply(dark: true);

    public static void Apply(bool dark)
    {
        IsDark = dark;
        if (dark)
        {
            Sidebar     = FromHex("#0f172a"); // slate-900
            Main        = FromHex("#1e293b"); // slate-800
            Card        = FromHex("#334155"); // slate-700
            CardHover   = FromHex("#475569"); // slate-600
            TextPrimary = FromHex("#f8fafc"); // slate-50
            TextMuted   = FromHex("#94a3b8"); // slate-400
            Disabled    = FromHex("#475569");
            Trough        = FromHex("#1e293b");
            TroughDisabled= FromHex("#2d3748");
            FillDisabled  = FromHex("#4a5568");
            KnobDisabled  = FromHex("#718096");
        }
        else
        {
            Sidebar     = FromHex("#e2e8f0"); // slate-200
            Main        = FromHex("#f8fafc"); // slate-50
            Card        = FromHex("#e2e8f0");
            CardHover   = FromHex("#cbd5e1"); // slate-300
            TextPrimary = FromHex("#0f172a");
            TextMuted   = FromHex("#475569");
            Disabled    = FromHex("#94a3b8");
            Trough        = FromHex("#dde3ea");
            TroughDisabled= FromHex("#e6ebf1");
            FillDisabled  = FromHex("#c3ccd8");
            KnobDisabled  = FromHex("#94a3b8");
        }
        Accent     = FromHex("#2563eb"); // blue-600 (ASUS blue)
        AccentDark = FromHex("#1d4ed8");
    }

    /// <summary>Readable foreground for text/knobs drawn on top of <paramref name="background"/>.</summary>
    public static Color ForeOn(Color background)
    {
        // Rec. 601 luma — good enough to pick between the light and dark ink.
        double luma = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255.0;
        return luma < 0.55 ? Color.White : FromHex("#0f172a");
    }

    public static Color FromHex(string hex)
    {
        hex = hex.TrimStart('#');
        return Color.FromArgb(
            Convert.ToInt32(hex.Substring(0, 2), 16),
            Convert.ToInt32(hex.Substring(2, 2), 16),
            Convert.ToInt32(hex.Substring(4, 2), 16));
    }
}
