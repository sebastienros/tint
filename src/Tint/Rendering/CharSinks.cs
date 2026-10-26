using System.Buffers;
using System.Text;

namespace Tint.Rendering;

// Where renderers write: a TextWriter, an IBufferWriter<char>, a StringBuilder or a pooled buffer. Renderers are
// generic over the sink so every write is a direct call and no intermediate string is built.
internal interface ICharSink
{
    void Write(char value);

    void Write(ReadOnlySpan<char> value);
}

internal readonly struct TextWriterSink : ICharSink
{
    private readonly TextWriter _writer;

    public TextWriterSink(TextWriter writer)
    {
        _writer = writer;
    }

    public void Write(char value) => _writer.Write(value);

    public void Write(ReadOnlySpan<char> value) => _writer.Write(value);
}

internal readonly struct BufferWriterSink : ICharSink
{
    private readonly IBufferWriter<char> _writer;

    public BufferWriterSink(IBufferWriter<char> writer)
    {
        _writer = writer;
    }

    public void Write(char value)
    {
        _writer.GetSpan(1)[0] = value;
        _writer.Advance(1);
    }

    public void Write(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
        {
            return;
        }

        value.CopyTo(_writer.GetSpan(value.Length));
        _writer.Advance(value.Length);
    }
}

internal readonly struct StringBuilderSink : ICharSink
{
    private readonly StringBuilder _builder;

    public StringBuilderSink(StringBuilder builder)
    {
        _builder = builder;
    }

    public void Write(char value) => _builder.Append(value);

    public void Write(ReadOnlySpan<char> value) => _builder.Append(value);
}

// A growable buffer over ArrayPool<char>.Shared, for the overloads that return a string: the string is the only
// allocation once the pool is warm. Dispose returns the array.
internal struct PooledCharSink : ICharSink, IDisposable
{
    private char[] _chars;
    private int _length;

    public PooledCharSink(int capacity)
    {
        _chars = ArrayPool<char>.Shared.Rent(Math.Max(capacity, 256));
        _length = 0;
    }

    public readonly ReadOnlySpan<char> WrittenSpan => _chars.AsSpan(0, _length);

    public void Write(char value)
    {
        if (_length == _chars.Length)
        {
            Grow(1);
        }

        _chars[_length++] = value;
    }

    public void Write(ReadOnlySpan<char> value)
    {
        if (_length + value.Length > _chars.Length)
        {
            Grow(value.Length);
        }

        value.CopyTo(_chars.AsSpan(_length));
        _length += value.Length;
    }

    public override readonly string ToString() => new(_chars, 0, _length);

    public void Dispose()
    {
        var chars = _chars;
        _chars = [];
        _length = 0;
        if (chars.Length > 0)
        {
            ArrayPool<char>.Shared.Return(chars);
        }
    }

    private void Grow(int additional)
    {
        var next = ArrayPool<char>.Shared.Rent(Math.Max(_chars.Length * 2, _length + additional));
        _chars.AsSpan(0, _length).CopyTo(next);
        ArrayPool<char>.Shared.Return(_chars);
        _chars = next;
    }
}

internal static class SinkExtensions
{
    public static void WriteInt32<TSink>(ref this TSink sink, int value)
        where TSink : struct, ICharSink
    {
        Span<char> digits = stackalloc char[11];
        value.TryFormat(digits, out var written, provider: System.Globalization.CultureInfo.InvariantCulture);
        sink.Write(digits[..written]);
    }
}
