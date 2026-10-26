using System.Globalization;
using System.Text;

namespace Tint.Loading;

/// <summary>A JSON syntax error, with the 1-based line and column where reading stopped.</summary>
internal sealed class JsonTreeException : FormatException
{
    public JsonTreeException(string message, int line, int column)
        : base($"{message} (line {line}, column {column})")
    {
        Line = line;
        Column = column;
    }

    public int Line { get; }

    public int Column { get; }
}

// A small JSON reader producing the DocumentTree shapes (TreeMap, List<object?>, string, double, bool, null), with the
// options the runtime gives System.Text.Json: comments skipped, trailing commas allowed, depth up to 1024. Tint.Generator
// compiles it instead of System.Text.Json, which analyzer hosts cannot load reliably.
internal ref struct JsonTreeReader
{
    private const int MaxDepth = 1024;

    private readonly string _text;
    private int _pos;
    private int _depth;
    private StringBuilder? _builder;

    private JsonTreeReader(string text)
    {
        _text = text;
        _pos = text.Length > 0 && text[0] == '﻿' ? 1 : 0;
        _depth = 0;
        _builder = null;
    }

    public static object? Parse(string text)
    {
        var reader = new JsonTreeReader(text);
        reader.SkipTrivia();
        var value = reader.ReadValue();
        reader.SkipTrivia();
        if (reader._pos < text.Length)
        {
            throw reader.Error("Unexpected content after the JSON value");
        }

        return value;
    }

    private object? ReadValue()
    {
        if (_pos >= _text.Length)
        {
            throw Error("Unexpected end of JSON");
        }

        switch (_text[_pos])
        {
            case '{':
                return ReadObject();
            case '[':
                return ReadArray();
            case '"':
                return ReadString();
            case 't':
                ReadLiteral("true");
                return true;
            case 'f':
                ReadLiteral("false");
                return false;
            case 'n':
                ReadLiteral("null");
                return null;
            default:
                return ReadNumber();
        }
    }

    private TreeMap ReadObject()
    {
        Enter();
        _pos++;
        var map = new TreeMap();
        SkipTrivia();
        while (true)
        {
            if (Peek() == '}')
            {
                _pos++;
                break;
            }

            if (Peek() != '"')
            {
                throw Error("Expected a property name");
            }

            var key = ReadString();
            SkipTrivia();
            if (Peek() != ':')
            {
                throw Error("Expected ':' after a property name");
            }

            _pos++;
            SkipTrivia();
            map.Set(key, ReadValue());
            SkipTrivia();
            var next = Peek();
            if (next == ',')
            {
                _pos++;
                SkipTrivia();
                continue;
            }

            if (next == '}')
            {
                _pos++;
                break;
            }

            throw Error("Expected ',' or '}'");
        }

        _depth--;
        return map;
    }

    private List<object?> ReadArray()
    {
        Enter();
        _pos++;
        var list = new List<object?>();
        SkipTrivia();
        while (true)
        {
            if (Peek() == ']')
            {
                _pos++;
                break;
            }

            list.Add(ReadValue());
            SkipTrivia();
            var next = Peek();
            if (next == ',')
            {
                _pos++;
                SkipTrivia();
                continue;
            }

            if (next == ']')
            {
                _pos++;
                break;
            }

            throw Error("Expected ',' or ']'");
        }

        _depth--;
        return list;
    }

    private string ReadString()
    {
        // Positioned on the opening quote.
        var start = ++_pos;
        while (_pos < _text.Length)
        {
            var c = _text[_pos];
            if (c == '"')
            {
                return _text.Substring(start, _pos++ - start);
            }

            if (c == '\\')
            {
                break;
            }

            if (c < ' ')
            {
                throw Error("Control character in a string");
            }

            _pos++;
        }

        var builder = _builder ??= new StringBuilder();
        builder.Clear();
        builder.Append(_text, start, _pos - start);
        while (_pos < _text.Length)
        {
            var c = _text[_pos];
            if (c == '"')
            {
                _pos++;
                return builder.ToString();
            }

            if (c < ' ')
            {
                throw Error("Control character in a string");
            }

            if (c != '\\')
            {
                builder.Append(c);
                _pos++;
                continue;
            }

            if (_pos + 1 >= _text.Length)
            {
                break;
            }

            var escape = _text[_pos + 1];
            _pos += 2;
            switch (escape)
            {
                case '"':
                case '\\':
                case '/':
                    builder.Append(escape);
                    break;
                case 'b':
                    builder.Append('\b');
                    break;
                case 'f':
                    builder.Append('\f');
                    break;
                case 'n':
                    builder.Append('\n');
                    break;
                case 'r':
                    builder.Append('\r');
                    break;
                case 't':
                    builder.Append('\t');
                    break;
                case 'u':
                    if (_pos + 4 > _text.Length
                        || !ushort.TryParse(_text.Substring(_pos, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code))
                    {
                        _pos -= 2;
                        throw Error("Invalid \\u escape");
                    }

                    builder.Append((char)code);
                    _pos += 4;
                    break;
                default:
                    _pos -= 2;
                    throw Error("Invalid escape sequence");
            }
        }

        throw Error("Unterminated string");
    }

    private double ReadNumber()
    {
        var start = _pos;
        if (Peek() == '-')
        {
            _pos++;
        }

        var digits = SkipDigits();
        if (digits == 0 || (digits > 1 && _text[_pos - digits] == '0'))
        {
            _pos = start;
            throw Error("Invalid JSON value");
        }

        if (Peek() == '.')
        {
            _pos++;
            if (SkipDigits() == 0)
            {
                throw Error("Invalid number");
            }
        }

        if (Peek() is 'e' or 'E')
        {
            _pos++;
            if (Peek() is '+' or '-')
            {
                _pos++;
            }

            if (SkipDigits() == 0)
            {
                throw Error("Invalid number");
            }
        }

        return double.Parse(_text.Substring(start, _pos - start), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private int SkipDigits()
    {
        var start = _pos;
        while (_pos < _text.Length && _text[_pos] is >= '0' and <= '9')
        {
            _pos++;
        }

        return _pos - start;
    }

    private void ReadLiteral(string literal)
    {
        if (string.CompareOrdinal(_text, _pos, literal, 0, literal.Length) != 0)
        {
            throw Error("Invalid JSON value");
        }

        _pos += literal.Length;
    }

    private void SkipTrivia()
    {
        while (_pos < _text.Length)
        {
            var c = _text[_pos];
            if (c is ' ' or '\t' or '\r' or '\n')
            {
                _pos++;
            }
            else if (c == '/' && _pos + 1 < _text.Length && _text[_pos + 1] == '/')
            {
                _pos += 2;
                while (_pos < _text.Length && _text[_pos] is not ('\n' or '\r'))
                {
                    _pos++;
                }
            }
            else if (c == '/' && _pos + 1 < _text.Length && _text[_pos + 1] == '*')
            {
                var end = _text.IndexOf("*/", _pos + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    throw Error("Unterminated comment");
                }

                _pos = end + 2;
            }
            else
            {
                return;
            }
        }
    }

    private readonly char Peek() => _pos < _text.Length ? _text[_pos] : '\0';

    private void Enter()
    {
        if (++_depth > MaxDepth)
        {
            throw Error($"JSON nests deeper than {MaxDepth} levels");
        }
    }

    private readonly JsonTreeException Error(string message)
    {
        var line = 1;
        var lineStart = 0;
        var end = Math.Min(_pos, _text.Length);
        for (var i = 0; i < end; i++)
        {
            if (_text[i] == '\n')
            {
                line++;
                lineStart = i + 1;
            }
        }

        return new JsonTreeException(message, line, end - lineStart + 1);
    }
}
