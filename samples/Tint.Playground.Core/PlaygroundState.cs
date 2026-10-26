using System.Buffers.Text;
using System.IO.Compression;
using System.Text;

namespace Tint.Playground;

/// <summary>What a permalink holds: the language, the theme or themes and the code.</summary>
public sealed record PlaygroundState(string Language, string Theme, string? SecondTheme, string Code)
{
    // A deflated code value starts with this marker, which base64url never produces.
    internal const char DeflatedMarker = '~';

    // Links come from anywhere; inflating stops here.
    internal const int MaxCodeBytes = 1 << 20;

    /// <summary>
    /// Writes the URL fragment, without the leading <c>#</c>:
    /// <c>lang=csharp&amp;theme=dark-plus[&amp;theme2=light-plus]&amp;code=…</c>. The code is the base64url of its UTF-8
    /// bytes, or <c>~</c> and the base64url of the raw deflated bytes when that is shorter.
    /// </summary>
    public string ToHash()
    {
        var builder = new StringBuilder();
        builder.Append("lang=").Append(Uri.EscapeDataString(Language));
        builder.Append("&theme=").Append(Uri.EscapeDataString(Theme));
        if (SecondTheme is { Length: > 0 })
        {
            builder.Append("&theme2=").Append(Uri.EscapeDataString(SecondTheme));
        }

        builder.Append("&code=").Append(EncodeCode(Code));
        return builder.ToString();
    }

    /// <summary>
    /// Reads a URL fragment written by <see cref="ToHash"/>, with or without its <c>#</c>. Missing or unreadable values
    /// are null; unknown keys are ignored.
    /// </summary>
    public static (string? Language, string? Theme, string? SecondTheme, string? Code) Parse(string? hash)
    {
        string? language = null, theme = null, secondTheme = null, code = null;
        if (string.IsNullOrEmpty(hash))
        {
            return (language, theme, secondTheme, code);
        }

        var text = hash.AsSpan();
        if (text.StartsWith("#"))
        {
            text = text[1..];
        }

        foreach (var range in text.Split('&'))
        {
            var pair = text[range];
            var equals = pair.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            var key = pair[..equals];
            var value = pair[(equals + 1)..].ToString();
            switch (key)
            {
                case "lang":
                    language = Unescape(value);
                    break;
                case "theme":
                    theme = Unescape(value);
                    break;
                case "theme2":
                    secondTheme = Unescape(value);
                    break;
                case "code":
                    code = DecodeCode(value);
                    break;
            }
        }

        return (language, theme, secondTheme, code);
    }

    /// <summary>Encodes code for the <c>code</c> value: plain base64url, or deflated when that is shorter.</summary>
    public static string EncodeCode(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        var bytes = Encoding.UTF8.GetBytes(code);
        var plain = Base64Url.EncodeToString(bytes);

        using var buffer = new MemoryStream();
        using (var deflate = new DeflateStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            deflate.Write(bytes);
        }

        var deflated = Base64Url.EncodeToString(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
        return deflated.Length + 1 < plain.Length ? DeflatedMarker + deflated : plain;
    }

    /// <summary>Decodes a <c>code</c> value; null when it is not valid base64url, deflate or UTF-8, or too large.</summary>
    public static string? DecodeCode(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var deflated = value.StartsWith(DeflatedMarker);
        var text = deflated ? value.AsSpan(1) : value.AsSpan();
        if (!Base64Url.IsValid(text))
        {
            return null;
        }

        var bytes = Base64Url.DecodeFromChars(text);
        if (deflated)
        {
            using var input = new MemoryStream(bytes);
            using var inflate = new DeflateStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            var chunk = new byte[16 * 1024];
            try
            {
                int read;
                while ((read = inflate.Read(chunk)) > 0)
                {
                    if (output.Length + read > MaxCodeBytes)
                    {
                        return null;
                    }

                    output.Write(chunk, 0, read);
                }
            }
            catch (InvalidDataException)
            {
                return null;
            }

            bytes = output.ToArray();
        }

        if (bytes.Length > MaxCodeBytes)
        {
            return null;
        }

        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static string? Unescape(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch (UriFormatException)
        {
            return null;
        }
    }
}
