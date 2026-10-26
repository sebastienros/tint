using System.Buffers;

namespace Tint.Rendering;

// The encoded tokens of the current line for each theme of a render, copied out of the tokenizers' buffers so the
// themes can be walked together. Arrays come from ArrayPool<EncodedToken>.Shared; Dispose returns them.
internal struct LineTokens : IDisposable
{
    private EncodedToken[] _tokens;
    private int _count;
    private int _index;

    public LineTokens()
    {
        _tokens = ArrayPool<EncodedToken>.Shared.Rent(64);
    }

    public void Load(ReadOnlySpan<EncodedToken> tokens)
    {
        if (tokens.Length > _tokens.Length)
        {
            ArrayPool<EncodedToken>.Shared.Return(_tokens);
            _tokens = ArrayPool<EncodedToken>.Shared.Rent(tokens.Length);
        }

        tokens.CopyTo(_tokens);
        _count = tokens.Length;
        _index = -1;
    }

    // Moves to the token covering position (the last one starting at or before it) and returns where the next token
    // starts, or lineLength. Metadata is 0 before the first token.
    public int Seek(int position, int lineLength, out uint metadata)
    {
        while (_index + 1 < _count && _tokens[_index + 1].Start <= position)
        {
            _index++;
        }

        metadata = _index >= 0 ? _tokens[_index].Metadata : 0;
        return _index + 1 < _count ? Math.Min(_tokens[_index + 1].Start, lineLength) : lineLength;
    }

    public void Dispose()
    {
        if (_tokens is { Length: > 0 } tokens)
        {
            ArrayPool<EncodedToken>.Shared.Return(tokens);
        }

        _tokens = [];
    }
}
