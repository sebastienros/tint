using System.Text;

namespace Tint.Fuzz;

/// <summary>The seed material of one grammar: lines of its sample and of corpus files of its language.</summary>
public sealed class SeedLines(string scopeName, IReadOnlyList<string[]> documents)
{
    /// <summary>The grammar scope.</summary>
    public string ScopeName { get; } = scopeName;

    /// <summary>Documents, each split into lines.</summary>
    public IReadOnlyList<string[]> Documents { get; } = documents;
}

/// <summary>
/// Generates the inputs of a campaign: a function of the seed, the grammar and the iteration, so a campaign is
/// reproducible. An input is a slice of a seed document, then one to five mutations.
/// </summary>
public static class Generator
{
    // Characters and sequences that open, close or escape constructs in most grammars.
    private static readonly string[] s_fragments =
    [
        "\"", "'", "`", "\"\"\"", "'''", "```", "{", "}", "[", "]", "(", ")", "<", ">", "</", "/>", "\\", "\\\\", "\\\"",
        "$", "${", "$(", "#", "#{", "@", "/", "//", "/*", "*/", "<!--", "-->", "*", "**", "-", "--", "%", "%>", "<%", "?>",
        "<?php", "=", "=>", ":", "::", ";", ",", ".", "|", "&", "^", "~", "!", "?", " ", "  ", "\t", "\\u", "\\x", "\\0",
        "\\x{", "{{", "}}", "{%", "%}", "@\"", "$\"", "r\"", "r#\"", "u8\"", "#!", "<<", "<<EOF", "EOF", "---", "...", "- ",
        "\U0001F600", "\U0001F600\U0001F600", "\uD83D", "\uDE00", "\uDE00\uD83D", "\u0000", "\uFFFF", "\uFFFE", "\uFEFF", "\u2028",
        "\u0301", "\u00E9", "\u200B", "\u202E", "\U0001D4B3", "\uFB01", "\u0130", "\u00DF", "\u00A0",
    ];

    private static readonly string[] s_openers = ["(", "[", "{", "<div>", "\"", "/*", "${", "#{", "<", "`", "((", "[[", "{{", "(?:"];

    /// <summary>The input of an iteration.</summary>
    public static string[] Generate(SeedLines seeds, int seed, int iteration, out string description)
    {
        // HashCode.Combine is randomized per process; inputs must not be.
        var random = new Random(unchecked((seed * 1_000_003) ^ (StableHash(seeds.ScopeName) * 7_919) ^ (iteration * 104_729)));
        var document = seeds.Documents[random.Next(seeds.Documents.Count)];
        var length = Math.Min(document.Length, random.Next(1, 40));
        var start = document.Length == length ? 0 : random.Next(document.Length - length);
        var lines = document.Skip(start).Take(length).ToList();
        if (lines.Count == 0)
        {
            lines.Add("");
        }

        var mutations = new List<string>();
        var count = random.Next(1, 6);
        for (var m = 0; m < count; m++)
        {
            mutations.Add(Mutate(lines, seeds, random));
        }

        description = $"{start + 1}+{length} of a {document.Length}-line document; " + string.Join(", ", mutations);
        return [.. lines];
    }

    private static string Mutate(List<string> lines, SeedLines seeds, Random random)
    {
        var index = random.Next(lines.Count);
        var line = lines[index];
        var at = line.Length == 0 ? 0 : random.Next(line.Length + 1);
        switch (random.Next(14))
        {
            case 0:
            case 1:
            case 2:
                // Insert a grammar-relevant fragment.
                var fragment = s_fragments[random.Next(s_fragments.Length)];
                lines[index] = line.Insert(at, fragment);
                return "insert";
            case 3:
                // Flip a character to a random one, sometimes outside the BMP or a lone surrogate.
                if (line.Length == 0)
                {
                    goto case 0;
                }

                var position = random.Next(line.Length);
                var replacement = random.Next(8) switch
                {
                    0 => ((char)random.Next(0xD800, 0xE000)).ToString(),
                    1 => char.ConvertFromUtf32(random.Next(0x10000, 0x10FFFF)),
                    // Control characters, but not line terminators: an editor would split the line there.
                    2 => ((char)random.Next(0, 0x20)) is var control && control is '\n' or '\r' ? "\t" : control.ToString(),
                    _ => ((char)random.Next(0x20, 0x7F)).ToString(),
                };
                lines[index] = line[..position] + replacement + line[(position + 1)..];
                return "flip";
            case 4:
                // Delete a range.
                if (line.Length == 0)
                {
                    goto case 0;
                }

                var from = random.Next(line.Length);
                lines[index] = line.Remove(from, random.Next(1, Math.Min(20, line.Length - from) + 1));
                return "delete";
            case 5:
                lines.Insert(index, line);
                return "duplicate line";
            case 6:
                if (lines.Count > 1)
                {
                    var other = random.Next(lines.Count);
                    (lines[index], lines[other]) = (lines[other], lines[index]);
                    return "swap lines";
                }

                goto case 0;
            case 7:
                lines[index] = line[..at];
                lines.Insert(index + 1, line[at..]);
                return "split line";
            case 8:
                if (index + 1 < lines.Count)
                {
                    lines[index] = line + lines[index + 1];
                    lines.RemoveAt(index + 1);
                    return "join lines";
                }

                goto case 0;
            case 9:
                // A very long line, up to VS Code's default tokenization limit (editor.maxTokenizationLineLength,
                // 20,000): the line repeated, or one fragment repeated.
                var repeat = random.Next(2) == 0 && line.Length > 0 ? line : s_fragments[random.Next(s_fragments.Length)];
                var times = Math.Max(1, random.Next(1_000, 20_000) / Math.Max(1, repeat.Length));
                lines[index] = line.Insert(at, string.Concat(Enumerable.Repeat(repeat, times)));
                return $"long line ({lines[index].Length} chars)";
            case 10:
                // Deep nesting, on one line or over many.
                var opener = s_openers[random.Next(s_openers.Length)];
                var depth = random.Next(50, 2_000);
                if (random.Next(2) == 0)
                {
                    lines[index] = line.Insert(at, string.Concat(Enumerable.Repeat(opener, depth)));
                    return $"nesting {depth}x '{opener}'";
                }

                lines.InsertRange(index, Enumerable.Repeat(opener, Math.Min(depth, 500)));
                return $"nesting {Math.Min(depth, 500)} lines of '{opener}'";
            case 11:
                // Splice a line from another seed document.
                var document = seeds.Documents[random.Next(seeds.Documents.Count)];
                if (document.Length > 0)
                {
                    lines.Insert(index, document[random.Next(document.Length)]);
                }

                return "splice";
            case 12:
                // Random characters, mostly ASCII punctuation.
                var builder = new StringBuilder();
                for (var i = random.Next(1, 30); i > 0; i--)
                {
                    builder.Append(random.Next(4) == 0 ? s_fragments[random.Next(s_fragments.Length)] : ((char)random.Next(0x21, 0x7F)).ToString());
                }

                lines[index] = line.Insert(at, builder.ToString());
                return "random";
            default:
                lines.Insert(index, "");
                return "empty line";
        }
    }

    /// <summary>A hash stable across processes (string.GetHashCode is randomized).</summary>
    public static int StableHash(string text)
    {
        var hash = 17;
        foreach (var c in text)
        {
            hash = hash * 31 + c;
        }

        return hash;
    }
}
