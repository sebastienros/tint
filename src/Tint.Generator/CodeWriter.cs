using System.Globalization;
using System.Text;

namespace Tint.Generator;

internal sealed class CodeWriter
{
    private readonly StringBuilder _builder = new();
    private int _indent;
    private bool _lineStart = true;

    public CodeWriter Write(string text)
    {
        if (_lineStart && text.Length > 0)
        {
            _builder.Append(' ', _indent * 4);
            _lineStart = false;
        }

        _builder.Append(text);
        return this;
    }

    public CodeWriter Line(string text = "")
    {
        Write(text);
        _builder.Append('\n');
        _lineStart = true;
        return this;
    }

    public CodeWriter Open(string header)
    {
        Line(header);
        Line("{");
        _indent++;
        return this;
    }

    public CodeWriter Close(string suffix = "")
    {
        _indent--;
        Line("}" + suffix);
        return this;
    }

    public void Indent() => _indent++;

    public void Unindent() => _indent--;

    public override string ToString() => _builder.ToString();

    // Plain ASCII C# literals: everything outside printable ASCII is a \u escape, so the source file encoding never matters.
    public static string Literal(string? value)
    {
        if (value is null)
        {
            return "null";
        }

        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                default:
                    if (c is >= ' ' and <= '~')
                    {
                        builder.Append(c);
                    }
                    else
                    {
                        builder.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                    }

                    break;
            }
        }

        return builder.Append('"').ToString();
    }

    public static string Bool(bool value) => value ? "true" : "false";

    public static string StringArray(IEnumerable<string> values)
    {
        var items = values.Select(Literal).ToList();
        return items.Count == 0 ? "global::System.Array.Empty<string>()" : "new string[] { " + string.Join(", ", items) + " }";
    }
}
