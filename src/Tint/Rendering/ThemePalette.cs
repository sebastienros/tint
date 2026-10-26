using System.Globalization;

namespace Tint.Rendering;

// What a renderer needs from a theme, computed once: colors as lower-case CSS strings and as RGB, and the default
// foreground and background ids that unstyled text has.
internal sealed class ThemePalette
{
    public ThemePalette(Theme theme)
    {
        Theme = theme;
        var colors = theme.ColorMap.Colors;
        CssColors = new string[colors.Count];
        Rgb = new int[colors.Count];
        for (var i = 0; i < colors.Count; i++)
        {
            CssColors[i] = colors[i].ToLowerInvariant();
            Rgb[i] = ParseRgb(colors[i]);
        }

        DefaultForeground = theme.Defaults.ForegroundId;
        DefaultBackground = theme.Defaults.BackgroundId;
    }

    public Theme Theme { get; }

    // By color id; index 0 is the empty string.
    public string[] CssColors { get; }

    // 0xRRGGBB by color id, -1 when the color is not a hex color.
    public int[] Rgb { get; }

    public int DefaultForeground { get; }

    public int DefaultBackground { get; }

    public string DefaultForegroundCss => GetCss(DefaultForeground);

    public string DefaultBackgroundCss => GetCss(DefaultBackground);

    public string GetCss(int id) => (uint)id < (uint)CssColors.Length ? CssColors[id] : string.Empty;

    public int GetRgb(int id) => (uint)id < (uint)Rgb.Length ? Rgb[id] : -1;

    // The style of a token relative to the defaults: foreground and background ids, 0 when they equal the default,
    // and the font style; packed so equal styles compare as equal integers.
    public uint GetStyleKey(uint metadata)
    {
        var foreground = EncodedTokenAttributes.GetForeground(metadata);
        var background = EncodedTokenAttributes.GetBackground(metadata);
        var fontStyle = EncodedTokenAttributes.GetFontStyle(metadata);
        if (foreground == DefaultForeground)
        {
            foreground = 0;
        }

        if (background == DefaultBackground)
        {
            background = 0;
        }

        var flags = fontStyle == FontStyle.NotSet ? 0 : (int)fontStyle & 0xF;
        return StyleKey.Pack(foreground, background, flags);
    }

    // #RGB, #RGBA, #RRGGBB and #RRGGBBAA; the alpha is ignored.
    private static int ParseRgb(string color)
    {
        var hex = color.AsSpan();
        if (hex.Length < 4 || hex[0] != '#')
        {
            return -1;
        }

        hex = hex[1..];
        if (hex.Length is 3 or 4)
        {
            Span<char> expanded = [hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]];
            return int.TryParse(expanded, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var shortValue) ? shortValue : -1;
        }

        if (hex.Length is 6 or 8)
        {
            return int.TryParse(hex[..6], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value) ? value : -1;
        }

        return -1;
    }
}

internal static class StyleKey
{
    public static uint Pack(int foreground, int background, int fontStyle)
        => (uint)foreground | ((uint)background << 9) | ((uint)fontStyle << 18);

    public static int Foreground(uint key) => (int)(key & 0x1FF);

    public static int Background(uint key) => (int)((key >> 9) & 0x1FF);

    public static FontStyle FontStyle(uint key) => (FontStyle)((key >> 18) & 0xF);
}
