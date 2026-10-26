using System.Collections.Frozen;
using Tint.Themes;

namespace Tint.Loading;

/// <summary>
/// Opens an included theme file. <paramref name="path"/> is the <c>include</c> or <c>tokenColors</c> value, combined
/// with the directory of the including file relative to the root theme. Returns null when the file does not exist.
/// </summary>
public delegate Stream? ThemeIncludeResolver(string path);

/// <summary>How <see cref="ThemeLoader"/> turns a JSON color theme into rules.</summary>
public enum ThemeLoadMode
{
    /// <summary>
    /// As VS Code does: the default rule comes from <c>editor.foreground</c> and <c>editor.background</c> (or the
    /// defaults of the theme's <c>type</c>), the global entries of legacy <c>settings</c> become those colors, and
    /// other scopeless rules are dropped.
    /// </summary>
    VSCode,

    /// <summary>The rules as written, includes flattened, as vscode-textmate receives them in its tests.</summary>
    Raw,
}

// The document-tree reading shared with Tint.Generator, which compiles this file into the generator (TINT_GENERATOR)
// so that generated and loaded themes come from one interpretation of the theme files.
public static partial class ThemeLoader
{
    private const int MaxIncludeDepth = 32;

    private static byte[] ReadAll(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    internal static RawTheme Read(byte[] utf8, DocumentFormat format, ThemeIncludeResolver? resolver, string baseDirectory, int depth, ThemeLoadMode mode)
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

        var ownColors = new HashSet<string>(StringComparer.Ordinal);
        if (root.Get("colors") is TreeMap colorMap)
        {
            foreach (var entry in colorMap.Entries)
            {
                if (entry.Value is string value)
                {
                    colors[entry.Key] = value;
                    ownColors.Add(entry.Key);
                }
            }
        }

        switch (root.Get("tokenColors"))
        {
            case List<object?> tokenColors:
                ReadRules(tokenColors, rules);
                break;
            case string { Length: > 0 } tokenColorsPath:
                var tmTheme = ReadInclude(tokenColorsPath, resolver, baseDirectory, depth).Rules;
                ConvertGlobalSettings(tmTheme, colors, ownColors);
                rules.AddRange(tmTheme);
                break;
        }

        if (root.Get("settings") is List<object?> settings)
        {
            var start = rules.Count;
            ReadRules(settings, rules);
            if (format == DocumentFormat.Json)
            {
                ConvertGlobalSettings(rules.GetRange(start, rules.Count - start), colors, ownColors);
            }
        }

        if (depth == 0 && format == DocumentFormat.Json && mode == ThemeLoadMode.VSCode)
        {
            ApplyVSCodeDefaults(rules, colors, type);
        }

        return new RawTheme(rules, name, type, colors.ToFrozenDictionary(StringComparer.Ordinal));
    }

    // VS Code's convertSettings: the global entries of a theme's tmTheme-style settings (legacy `settings`, or the
    // tmTheme `tokenColors` points at) become editor colors, unless the file's own `colors` sets them.
    private static void ConvertGlobalSettings(IEnumerable<RawThemeRule> rules, Dictionary<string, string> colors, HashSet<string> ownColors)
    {
        foreach (var rule in rules)
        {
            if (rule.Scopes is not null)
            {
                continue;
            }

            if (rule.Foreground is { } foreground && !ownColors.Contains("editor.foreground"))
            {
                colors["editor.foreground"] = foreground;
            }

            if (rule.Background is { } background && !ownColors.Contains("editor.background"))
            {
                colors["editor.background"] = background;
            }
        }
    }

    // VS Code's ColorThemeData.tokenColors: the default rule is always first, built from the editor colors or the
    // theme type's defaults, and no other rule without a scope reaches the tokenizer.
    private static void ApplyVSCodeDefaults(List<RawThemeRule> rules, Dictionary<string, string> colors, string? type)
    {
        var (defaultForeground, defaultBackground) = type switch
        {
            "light" or "vs" => ("#333333", "#FFFFFF"),
            "hc" or "hcDark" or "hc-black" => ("#FFFFFF", "#000000"),
            "hcLight" or "hc-light" => ("#292929", "#FFFFFF"),
            _ => ("#BBBBBB", "#1E1E1E"),
        };

        rules.RemoveAll(rule => rule.Scopes is null);
        rules.Insert(0, new RawThemeRule
        {
            Foreground = colors.GetValueOrDefault("editor.foreground", defaultForeground),
            Background = colors.GetValueOrDefault("editor.background", defaultBackground),
        });
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
        return Read(ReadAll(stream), format, resolver, slash < 0 ? string.Empty : path.Substring(0, slash + 1), depth + 1, ThemeLoadMode.Raw);
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
