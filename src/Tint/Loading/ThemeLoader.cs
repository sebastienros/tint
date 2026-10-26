using System.Collections.Frozen;
using Tint.Themes;

namespace Tint.Loading;

/// <summary>Reads VS Code JSON color themes (with <c>include</c>) and TextMate <c>.tmTheme</c> plist themes.</summary>
public static partial class ThemeLoader
{
    /// <summary>
    /// Loads a theme file: JSON when the name ends with <c>.json</c>, plist otherwise. Includes are resolved relative
    /// to the file.
    /// </summary>
    /// <param name="path">The theme file.</param>
    /// <param name="mode">How JSON color themes become rules; plist themes are always read as written.</param>
    public static LoadedTheme Load(string path, ThemeLoadMode mode = ThemeLoadMode.VSCode)
    {
        ArgumentNullException.ThrowIfNull(path);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        var format = path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? DocumentFormat.Json : DocumentFormat.Plist;
        ThemeIncludeResolver resolver = include =>
        {
            var full = Path.GetFullPath(Path.Combine(directory, include));
            return File.Exists(full) ? File.OpenRead(full) : null;
        };
        return new LoadedTheme(Read(File.ReadAllBytes(path), format, resolver, string.Empty, 0, mode));
    }

    /// <summary>Loads a theme from a stream.</summary>
    /// <param name="stream">The theme content.</param>
    /// <param name="format">The format of the content.</param>
    /// <param name="includeResolver">Opens included files, or null to fail on includes.</param>
    /// <param name="mode">How JSON color themes become rules; plist themes are always read as written.</param>
    public static LoadedTheme Load(Stream stream, DocumentFormat format = DocumentFormat.Auto, ThemeIncludeResolver? includeResolver = null, ThemeLoadMode mode = ThemeLoadMode.VSCode)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return new LoadedTheme(Read(ReadAll(stream), format, includeResolver, string.Empty, 0, mode));
    }

    /// <summary>Loads a theme from UTF-8 content.</summary>
    /// <param name="utf8">The theme content.</param>
    /// <param name="format">The format of the content.</param>
    /// <param name="includeResolver">Opens included files, or null to fail on includes.</param>
    /// <param name="mode">How JSON color themes become rules; plist themes are always read as written.</param>
    public static LoadedTheme Load(ReadOnlySpan<byte> utf8, DocumentFormat format = DocumentFormat.Auto, ThemeIncludeResolver? includeResolver = null, ThemeLoadMode mode = ThemeLoadMode.VSCode)
        => new(Read(utf8.ToArray(), format, includeResolver, string.Empty, 0, mode));
}
