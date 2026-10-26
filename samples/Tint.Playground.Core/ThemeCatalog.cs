using System.Text;
using Tint.Themes;

namespace Tint.Playground;

/// <summary>Whether a theme is meant for a dark or a light background.</summary>
public enum ThemeKind
{
    /// <summary>A dark background (<c>dark</c> and <c>hcDark</c> themes).</summary>
    Dark,

    /// <summary>A light background (<c>light</c> and <c>hcLight</c> themes).</summary>
    Light,
}

/// <summary>A theme of the pickers.</summary>
/// <param name="Id">The slug used in permalinks (<c>dark-plus</c> for <c>Dark+</c>).</param>
/// <param name="Name">The theme's name.</param>
/// <param name="Kind">Dark or light.</param>
/// <param name="Source">The theme.</param>
public sealed record ThemeOption(string Id, string Name, ThemeKind Kind, IThemeSource Source);

/// <summary>Builds the theme pickers from themes.</summary>
public static class ThemeCatalog
{
    /// <summary>One option per theme, sorted by kind (dark first) then name.</summary>
    public static IReadOnlyList<ThemeOption> Build(IEnumerable<IThemeSource> themes)
    {
        ArgumentNullException.ThrowIfNull(themes);
        var options = new List<ThemeOption>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in themes)
        {
            var raw = source.GetTheme();
            var name = raw.Name ?? source.Name ?? NameFromClass(source.GetType().Name);
            var id = Slugify(name);
            for (var n = 2; !ids.Add(id); n++)
            {
                id = Slugify(name) + "-" + n;
            }

            options.Add(new ThemeOption(id, name, GetKind(raw.Type, name), source));
        }

        options.Sort(static (a, b) =>
        {
            var r = a.Kind.CompareTo(b.Kind);
            return r != 0 ? r : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        return options;
    }

    /// <summary>
    /// The kind a theme declares (<c>dark</c>, <c>light</c>, <c>hcDark</c>, <c>hcLight</c>), or, when it declares none,
    /// light when its name says so and dark otherwise.
    /// </summary>
    public static ThemeKind GetKind(string? type, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return type switch
        {
            "light" or "hcLight" => ThemeKind.Light,
            "dark" or "hcDark" => ThemeKind.Dark,
            _ => name.Contains("light", StringComparison.OrdinalIgnoreCase) ? ThemeKind.Light : ThemeKind.Dark,
        };
    }

    /// <summary>
    /// The name of a theme that declares none (VS Code labels Monokai, Monokai Dimmed and Tomorrow Night Blue from their
    /// extension's <c>package.json</c>): its generated class name without the <c>Theme</c> suffix, words split at capitals
    /// (<c>MonokaiDimmedTheme</c> is <c>Monokai Dimmed</c>).
    /// </summary>
    public static string NameFromClass(string className)
    {
        ArgumentNullException.ThrowIfNull(className);
        var name = className.EndsWith("Theme", StringComparison.Ordinal) && className.Length > "Theme".Length ? className[..^"Theme".Length] : className;
        var builder = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsAsciiLetterUpper(name[i]) && !char.IsAsciiLetterUpper(name[i - 1]))
            {
                builder.Append(' ');
            }

            builder.Append(name[i]);
        }

        return builder.ToString();
    }

    /// <summary>A lower-case slug: letters and digits kept, <c>+</c> spelled <c>plus</c>, runs of anything else one <c>-</c>.</summary>
    public static string Slugify(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var builder = new StringBuilder(name.Length + 8);
        foreach (var c in name)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                if (builder.Length > 0 && builder[^1] != '-')
                {
                    builder.Append('-');
                }

                if (c == '+')
                {
                    builder.Append("plus-");
                }
            }
        }

        while (builder.Length > 0 && builder[^1] == '-')
        {
            builder.Length--;
        }

        return builder.Length == 0 ? "theme" : builder.ToString();
    }
}
