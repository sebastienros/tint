using System.Globalization;
using System.Text;
#if !TINT_GENERATOR
using System.Text.Json;
#endif
using System.Xml;

namespace Tint.Loading;

// The common shape of JSON and plist documents: maps (keys in document order), lists, strings, booleans and numbers.
// Grammar and theme readers work on this tree so that both file formats share one interpretation.
internal sealed class TreeMap
{
    private readonly List<KeyValuePair<string, object?>> _entries = [];

    public IReadOnlyList<KeyValuePair<string, object?>> Entries => _entries;

    public void Set(string key, object? value)
    {
        // A repeated key keeps its first position and takes the last value, as JSON.parse does.
        for (var i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Key == key)
            {
                _entries[i] = new KeyValuePair<string, object?>(key, value);
                return;
            }
        }

        _entries.Add(new KeyValuePair<string, object?>(key, value));
    }

    public object? Get(string key)
    {
        foreach (var entry in _entries)
        {
            if (entry.Key == key)
            {
                return entry.Value;
            }
        }

        return null;
    }

    public bool Contains(string key)
    {
        foreach (var entry in _entries)
        {
            if (entry.Key == key)
            {
                return true;
            }
        }

        return false;
    }
}

// JSON goes through System.Text.Json in the runtime and through JsonTreeReader in Tint.Generator (TINT_GENERATOR); both
// produce the same tree.
internal static class DocumentTree
{
#if !TINT_GENERATOR
    private static readonly JsonDocumentOptions s_jsonOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        MaxDepth = 1024,
    };
#endif

    public static bool LooksLikeJson(ReadOnlySpan<byte> utf8)
    {
        utf8 = SkipBom(utf8);
        foreach (var b in utf8)
        {
            if (b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
            {
                continue;
            }

            return b is (byte)'{' or (byte)'[' or (byte)'/';
        }

        return false;
    }

#if TINT_GENERATOR
    public static object? ParseJson(ReadOnlyMemory<byte> utf8)
        => JsonTreeReader.Parse(Encoding.UTF8.GetString(SkipBom(utf8.Span).ToArray()));
#else
    public static object? ParseJson(ReadOnlyMemory<byte> utf8)
    {
        if (utf8.Span.StartsWith("﻿"u8))
        {
            utf8 = utf8.Slice(3);
        }

        using var document = JsonDocument.Parse(utf8, s_jsonOptions);
        return Convert(document.RootElement);
    }

    private static object? Convert(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var map = new TreeMap();
                foreach (var property in element.EnumerateObject())
                {
                    map.Set(property.Name, Convert(property.Value));
                }

                return map;
            case JsonValueKind.Array:
                var list = new List<object?>(element.GetArrayLength());
                foreach (var item in element.EnumerateArray())
                {
                    list.Add(Convert(item));
                }

                return list;
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Number:
                return element.GetDouble();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            default:
                return null;
        }
    }
#endif

    public static object? ParsePlist(ReadOnlyMemory<byte> utf8)
    {
        using var stream = new MemoryStream(utf8.ToArray(), writable: false);
        return ParsePlist(stream);
    }

    public static object? ParsePlist(Stream stream)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            CloseInput = false,
        };
        using var reader = XmlReader.Create(stream, settings);
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                if (reader.Name == "plist")
                {
                    if (reader.IsEmptyElement)
                    {
                        return null;
                    }

                    continue;
                }

                return ReadPlistValue(reader);
            }
        }

        return null;
    }

    // Positioned on a value element; returns with the reader on the value's last node.
    private static object? ReadPlistValue(XmlReader reader)
    {
        var name = reader.Name;
        switch (name)
        {
            case "dict":
                var map = new TreeMap();
                if (reader.IsEmptyElement)
                {
                    return map;
                }

                string? key = null;
                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.EndElement)
                    {
                        return map;
                    }

                    if (reader.NodeType != XmlNodeType.Element)
                    {
                        continue;
                    }

                    if (reader.Name == "key")
                    {
                        key = ReadText(reader);
                    }
                    else
                    {
                        var value = ReadPlistValue(reader);
                        if (key is not null)
                        {
                            map.Set(key, value);
                            key = null;
                        }
                    }
                }

                return map;
            case "array":
                var list = new List<object?>();
                if (reader.IsEmptyElement)
                {
                    return list;
                }

                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.EndElement)
                    {
                        return list;
                    }

                    if (reader.NodeType == XmlNodeType.Element)
                    {
                        list.Add(ReadPlistValue(reader));
                    }
                }

                return list;
            case "true":
                Skip(reader);
                return true;
            case "false":
                Skip(reader);
                return false;
            case "integer":
            case "real":
                var text = ReadText(reader);
                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : double.NaN;
            default:
                // string, data, date and anything unknown read as text.
                return ReadText(reader);
        }
    }

    // Reads the text content of the current element and leaves the reader on its end element.
    private static string ReadText(XmlReader reader)
    {
        if (reader.IsEmptyElement)
        {
            return string.Empty;
        }

        StringBuilder? builder = null;
        string? single = null;
        while (reader.Read())
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                case XmlNodeType.Whitespace:
                case XmlNodeType.SignificantWhitespace:
                    if (single is null && builder is null)
                    {
                        single = reader.Value;
                    }
                    else
                    {
                        builder ??= new StringBuilder(single);
                        builder.Append(reader.Value);
                    }

                    break;
                case XmlNodeType.EndElement:
                    return builder?.ToString() ?? single ?? string.Empty;
                case XmlNodeType.Element:
                    // Markup inside a text element: skip it.
                    Skip(reader);
                    break;
            }
        }

        return builder?.ToString() ?? single ?? string.Empty;
    }

    private static void Skip(XmlReader reader)
    {
        if (!reader.IsEmptyElement)
        {
            var depth = reader.Depth;
            while (reader.Read() && !(reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth))
            {
            }
        }
    }

    private static ReadOnlySpan<byte> SkipBom(ReadOnlySpan<byte> utf8) => utf8.StartsWith("﻿"u8) ? utf8.Slice(3) : utf8;
}
