using System.Runtime.CompilerServices;
namespace Tint.Internal.Regex;

// A reusable character buffer for building scope names and resolved patterns without intermediate strings. Not
// thread-safe; owned by one tokenizer.
internal sealed class CharBuffer
{
    private char[] _chars;

    public CharBuffer(int capacity = 128)
    {
        _chars = new char[capacity];
    }

    public int Length { get; private set; }

    public void Clear() => Length = 0;

    [MethodImpl(Jit.FullOpts)]
    public void Append(char c)
    {
        if (Length == _chars.Length)
        {
            Grow(1);
        }

        _chars[Length++] = c;
    }

    [MethodImpl(Jit.FullOpts)]
    public void Append(ReadOnlySpan<char> value)
    {
        if (Length + value.Length > _chars.Length)
        {
            Grow(value.Length);
        }

        value.CopyTo(_chars.AsSpan(Length));
        Length += value.Length;
    }

    public ReadOnlySpan<char> AsSpan() => _chars.AsSpan(0, Length);

    public Span<char> AsWritableSpan(int start) => _chars.AsSpan(start, Length - start);

    public override string ToString() => new(_chars, 0, Length);

    private void Grow(int additional) => Array.Resize(ref _chars, Math.Max(_chars.Length * 2, Length + additional));
}
