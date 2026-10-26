namespace Tint.Themes;

/// <summary>
/// The colors of a theme, interned: ids start at 1 (0 means "not set"), colors are upper-cased as vscode-textmate
/// stores them.
/// </summary>
public sealed class ColorMap
{
    private readonly List<string> _colors = [string.Empty];
    private readonly Dictionary<string, int> _ids = new(StringComparer.Ordinal);

    internal ColorMap()
    {
    }

    /// <summary>The number of ids, including the unused id 0.</summary>
    public int Count => _colors.Count;

    /// <summary>The colors by id; index 0 is the empty string.</summary>
    public IReadOnlyList<string> Colors => _colors;

    /// <summary>The color of an id, or the empty string for 0 and unknown ids.</summary>
    public string GetColor(int id) => (uint)id < (uint)_colors.Count ? _colors[id] : string.Empty;

    /// <summary>The id of a color, or 0 when the theme does not use it.</summary>
    public int GetId(string? color)
    {
        if (color is null)
        {
            return 0;
        }

        return _ids.TryGetValue(color.ToUpperInvariant(), out var id) ? id : 0;
    }

    internal int GetOrAddId(string? color)
    {
        if (color is null)
        {
            return 0;
        }

        color = color.ToUpperInvariant();
        if (_ids.TryGetValue(color, out var id))
        {
            return id;
        }

        id = _colors.Count;
        _colors.Add(color);
        _ids[color] = id;
        return id;
    }
}
