using Tint.Loading;
using Xunit;

namespace Tint.Tests;

// The generator reads JSON with JsonTreeReader instead of System.Text.Json; both must build the same document tree.
public sealed class JsonTreeReaderTests
{
    public static IEnumerable<TheoryDataRow<string>> Files()
    {
        var root = Generator.GeneratorHarness.RepositoryRoot;
        foreach (var directory in new[] { Path.Combine(root, "grammars"), Path.Combine(root, "themes"), Fixtures.VSCodeTextMate })
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                yield return new TheoryDataRow<string>(Path.GetRelativePath(root, file));
            }
        }
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void ReadsLikeSystemTextJson(string file)
    {
        var bytes = File.ReadAllBytes(Path.Combine(Generator.GeneratorHarness.RepositoryRoot, file));
        object? expected;
        try
        {
            expected = DocumentTree.ParseJson(bytes);
        }
        catch (System.Text.Json.JsonException)
        {
            Assert.Throws<JsonTreeException>(() => JsonTreeReader.Parse(System.Text.Encoding.UTF8.GetString(bytes)));
            return;
        }

        Assert.Equal(Describe(expected), Describe(JsonTreeReader.Parse(System.Text.Encoding.UTF8.GetString(bytes))));
    }

    [Theory]
    [InlineData("{ \"a\": 1, }", "{a:1}")]
    [InlineData("[1, 2,] // done", "[1,2]")]
    [InlineData("/* lead */ { \"a\": [true, false, null, -1.5e2, \"\\u00e9\\n\"] }", "{a:[True,False,null,-150,\"é\n\"]}")]
    [InlineData("{ \"a\": 1, \"a\": 2 }", "{a:2}")]
    public void AcceptsCommentsTrailingCommasAndDuplicateKeys(string json, string expected)
        => Assert.Equal(expected, Describe(JsonTreeReader.Parse(json)));

    [Theory]
    [InlineData("{\n  \"a\": [ }", 2, 10)]
    [InlineData("{ \"a\": 01 }", 1, 8)]
    [InlineData("{ \"a\": \"x", 1, 10)]
    [InlineData("{} x", 1, 4)]
    public void ReportsTheLineAndColumnOfSyntaxErrors(string json, int line, int column)
    {
        var error = Assert.Throws<JsonTreeException>(() => JsonTreeReader.Parse(json));
        Assert.Equal((line, column), (error.Line, error.Column));
    }

    private static string Describe(object? value) => value switch
    {
        TreeMap map => "{" + string.Join(",", map.Entries.Select(e => e.Key + ":" + Describe(e.Value))) + "}",
        List<object?> list => "[" + string.Join(",", list.Select(Describe)) + "]",
        string s => "\"" + s + "\"",
        double d => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        null => "null",
        _ => value.ToString()!,
    };
}
