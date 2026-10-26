namespace Tint.Playground;

/// <summary>A token found by offset: its line, its index in the line and its offset in the code.</summary>
public readonly record struct TokenHit(int Line, int Index, int Offset, HighlightedToken Token);

/// <summary>
/// Finds the token at an offset of the code, for the inspector. Lines are split as the highlighter splits them: at
/// <c>\n</c> or <c>\r\n</c>, a newline at the very end starting no empty line.
/// </summary>
public sealed class TokenMap
{
    private readonly int[] _lineStarts;
    private readonly int[] _lineLengths;
    private readonly HighlightedToken[][] _lines;

    /// <summary>Indexes the tokens <see cref="Highlighter.Tokenize(string)"/> returned for <paramref name="code"/>.</summary>
    public TokenMap(string code, HighlightedToken[][] lines)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(lines);
        _lines = lines;
        _lineStarts = new int[lines.Length];
        _lineLengths = new int[lines.Length];
        var start = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var newline = start < code.Length ? code.IndexOf('\n', start) : -1;
            var end = newline < 0 ? code.Length : newline;
            _lineStarts[i] = start;
            _lineLengths[i] = end > start && code[end - 1] == '\r' && newline >= 0 ? end - start - 1 : end - start;
            start = newline < 0 ? code.Length : newline + 1;
        }

        TokenCount = lines.Sum(static line => line.Length);
    }

    /// <summary>The number of lines.</summary>
    public int LineCount => _lines.Length;

    /// <summary>The number of tokens over all lines.</summary>
    public int TokenCount { get; }

    /// <summary>The tokens of each line, offsets relative to their line.</summary>
    public IReadOnlyList<HighlightedToken[]> Lines => _lines;

    /// <summary>
    /// The token holding the character at <paramref name="offset"/>, as a mouse hover reports it. At a line end (the
    /// newline, or the end of the code) there is none, unless <paramref name="caret"/> is set: a caret sits between
    /// characters and takes the token before it when no token starts at it.
    /// </summary>
    public TokenHit? Find(int offset, bool caret = false)
    {
        if (_lines.Length == 0 || offset < 0)
        {
            return null;
        }

        var line = Array.BinarySearch(_lineStarts, offset);
        if (line < 0)
        {
            line = ~line - 1;
        }

        var column = offset - _lineStarts[line];
        var tokens = _lines[line];
        if (column >= _lineLengths[line])
        {
            if (!caret || column > _lineLengths[line] || tokens.Length == 0 || _lineLengths[line] == 0)
            {
                return null;
            }

            column = _lineLengths[line] - 1;
        }

        // Tokens are sorted and contiguous.
        int lo = 0, hi = tokens.Length - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >>> 1;
            var token = tokens[mid];
            if (column < token.Start)
            {
                hi = mid - 1;
            }
            else if (column >= token.End)
            {
                lo = mid + 1;
            }
            else
            {
                return new TokenHit(line, mid, _lineStarts[line] + token.Start, token);
            }
        }

        return null;
    }
}
