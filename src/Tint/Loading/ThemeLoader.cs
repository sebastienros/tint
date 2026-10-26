using System.Collections.Frozen;
using Tint.Themes;

namespace Tint.Loading;

/// <summary>
/// Opens an included theme file. <paramref name="path"/> is the <c>include</c> or <c>tokenColors</c> value, combined
/// with the directory of the including file relative to the root theme. Returns null when the file does not exist.
/// </summary>
public delegate Stream? ThemeIncludeResolver(string path);

/// <summary>Reads VS Code JSON color themes (with <c>include</c>) and TextMate <c>.tmTheme</c> plist themes.</summary>
public static class ThemeLoader
{
    private const int MaxIncludeDepth = 32;

    /// <summary>
    /// Loads a theme file: JSON when the name ends with <c>.json</c>, plist otherwise. Includes are resolved relative
    /// to the file.
    /// </summary>
    public static LoadedTheme Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        var format = path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? DocumentFormat.Json : DocumentFormat.Plist;
        ThemeIncludeResolver resolver = include =>
        {
            var full = Path.GetFullPath(Path.Combine(directory, include));
            return File.Exists(full) ? File.OpenRead(full) : null;
        };
        return new LoadedTheme(Read(File.ReadAllBytes(path), format, resolver, string.Empty, 0));
    }

    /// <summary>Loads a theme from a stream.</summary>
    /// <param name="stream">The theme content.</param>
    /// <param name="format">The format of the content.</param>
    /// <param name="includeResolver">Opens included files, or null to fail on includes.</param>
    public static LoadedTheme Load(Stream stream, DocumentFormat format = DocumentFormat.Auto, ThemeIncludeResolver? includeResolver = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return new LoadedTheme(Read(ReadAll(stream), format, includeResolver, string.Empty, 0));
    }

    /// <summary>Loads a theme from UTF-8 content.</summary>
    /// <param name="utf8">The theme content.</param>
    /// <param name="format">The format of the content.</param>
    /// <param name="includeResolver">Opens included files, or null to fail on includes.</param>
    public static LoadedTheme Load(ReadOnlySpan<byte> utf8, DocumentFormat format = DocumentFormat.Auto, ThemeIncludeResolver? includeResolver = null)
        => new(Read(utf8.ToArray(), format, includeResolver, string.Empty, 0));

    private static byte[] ReadAll(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static RawTheme Read(byte[] utf8, DocumentFormat format, ThemeIncludeResolver? resolver, string baseDirectory, int depth)
    {
        if (format == DocumentFormat.Auto)
        {
            format = DocumentTree.LooksLikeJson(utf8) ? DocumentFormat.Json : DocumentFormat.Plist;
        }

        var tree = format == DocumentFormat.Json ? DocumentTree.ParseJson(utf8) : DocumentTree.ParsePlist(utf8);
        if (tree is not TreeMap root)
        {
            throw new FormatException("A theme must be an object.");
        }

        var rules = new List<RawThemeRule>();
        var colors = new Dictionary<string, string>(StringComparer.Ordinal);
        var name = root.Get("name") as string;
        var type = root.Get("type") as string;

        // VS Code loads the included theme first; the including theme's rules follow and its colors override.
        if (format == DocumentFormat.Json && root.Get("include") is string { Length: > 0 } include)
        {
            var parent = ReadInclude(include, resolver, baseDirectory, depth);
            rules.AddRange(parent.Rules);
            foreach (var color in parent.UiColors)
            {
                colors[color.Key] = color.Value;
            }

            name ??= parent.Name;
            type ??= parent.Type;
        }

        if (root.Get("colors") is TreeMap colorMap)
        {
            foreach (var entry in colorMap.Entries)
            {
                if (entry.Value is string value)
                {
                    colors[entry.Key] = value;
                }
            }
        }

        switch (root.Get("tokenColors"))
        {
            case List<object?> tokenColors:
                ReadRules(tokenColors, rules);
                break;
            case string { Length: > 0 } tokenColorsPath:
                rules.AddRange(ReadInclude(tokenColorsPath, resolver, baseDirectory, depth).Rules);
                break;
        }

        if (root.Get("settings") is List<object?> settings)
        {
            ReadRules(settings, rules);
        }

        if (depth == 0 && format == DocumentFormat.Json
            && (colors.TryGetValue("editor.foreground", out var foreground) | colors.TryGetValue("editor.background", out var background)))
        {
            // VS Code color themes: the default rule comes from the editor colors and replaces any scopeless rule.
            rules.RemoveAll(rule => rule.Scopes is null);
            rules.Insert(0, new RawThemeRule { Foreground = foreground, Background = background });
        }

        return new RawTheme(rules, name, type, colors.ToFrozenDictionary(StringComparer.Ordinal));
    }

    private static RawTheme ReadInclude(string include, ThemeIncludeResolver? resolver, string baseDirectory, int depth)
    {
        if (depth >= MaxIncludeDepth)
        {
            throw new FormatException($"Theme includes nest deeper than {MaxIncludeDepth} levels.");
        }

        if (resolver is null)
        {
            throw new FormatException($"The theme includes '{include}' but no include resolver was given.");
        }

        var path = CombineRelative(baseDirectory, include);
        using var stream = resolver(path) ?? throw new FileNotFoundException($"The included theme '{path}' was not found.", path);
        var format = path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? DocumentFormat.Json : DocumentFormat.Plist;
        var slash = path.Replace('\\', '/').LastIndexOf('/');
        return Read(ReadAll(stream), format, resolver, slash < 0 ? string.Empty : path.Substring(0, slash + 1), depth + 1);
    }

    private static string CombineRelative(string baseDirectory, string include)
    {
        if (include.StartsWith("./", StringComparison.Ordinal))
        {
            include = include.Substring(2);
        }

        return baseDirectory.Length == 0 || Path.IsPathRooted(include) ? include : baseDirectory + include;
    }

    private static void ReadRules(List<object?> entries, List<RawThemeRule> rules)
    {
        foreach (var item in entries)
        {
            // parseTheme skips entries without settings.
            if (item is not TreeMap entry || entry.Get("settings") is not TreeMap settings)
            {
                continue;
            }

            IReadOnlyList<string>? scopes = null;
            switch (entry.Get("scope"))
            {
                case string scope:
                    scopes = scope.Trim(',').Split(',');
                    break;
                case List<object?> list:
                    var items = new List<string>(list.Count);
                    foreach (var value in list)
                    {
                        if (value is string s)
                        {
                            items.Add(s);
                        }
                    }

                    scopes = items;
                    break;
            }

            rules.Add(new RawThemeRule
            {
                Name = entry.Get("name") as string,
                Scopes = scopes,
                FontStyle = settings.Get("fontStyle") as string,
                Foreground = settings.Get("foreground") as string,
                Background = settings.Get("background") as string,
            });
        }
    }
}
