namespace Tint.Rendering;

// Splits code into lines at "\n" and "\r\n". A newline at the very end terminates the last line rather than
// starting an empty one, so "a" and "a\n" have the same single line; empty code has no lines.
internal ref struct LineReader
{
    private ReadOnlySpan<char> _remaining;
    private bool _done;

    public LineReader(ReadOnlySpan<char> code)
    {
        _remaining = code;
        _done = code.IsEmpty;
        EndsWithNewline = code.Length > 0 && code[^1] == '\n';
    }

    // Whether the code ends with a line terminator.
    public bool EndsWithNewline { get; }

    public bool TryRead(out ReadOnlySpan<char> line)
    {
        if (_done)
        {
            line = default;
            return false;
        }

        var index = _remaining.IndexOf('\n');
        if (index < 0)
        {
            line = _remaining;
            _done = true;
            return true;
        }

        line = index > 0 && _remaining[index - 1] == '\r' ? _remaining[..(index - 1)] : _remaining[..index];
        _remaining = _remaining[(index + 1)..];
        _done = _remaining.IsEmpty;
        return true;
    }
}
