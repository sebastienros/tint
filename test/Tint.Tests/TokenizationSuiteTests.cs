using System.Text;
using System.Text.Json;
using Tint.Grammars;
using Tint.Loading;
using Xunit;

namespace Tint.Tests;

// vscode-textmate's src/tests/tokenization.test.ts over first-mate/tests.json, suite1/tests.json and
// suite1/whileTests.json.
public sealed class TokenizationSuiteTests
{
    public static IEnumerable<TheoryDataRow<string, int>> Cases()
    {
        foreach (var suite in new[] { "first-mate/tests.json", "suite1/tests.json", "suite1/whileTests.json" })
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(SuitePath(suite)));
            var index = 0;
            foreach (var test in document.RootElement.EnumerateArray())
            {
                var desc = test.GetProperty("desc").GetString()!;
                var row = new TheoryDataRow<string, int>(suite, index++) { TestDisplayName = $"{suite}: {desc}" };
                if (KnownDifferences.TryGetValue((suite, desc), out var reason))
                {
                    row.Skip = reason;
                }

                yield return row;
            }
        }
    }

    // Cases that cannot pass because of a documented engine difference (docs/research/known-differences.md).
    private static readonly Dictionary<(string Suite, string Desc), string> KnownDifferences = new()
    {
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void TokenizesLikeVSCodeTextMate(string suite, int index) => Run(suite, index, path => GrammarLoader.Load(path));

    // Tier 2 (docs/design.md section 5.5): every pattern runtime-compiled when its rule is first reached.
    [Theory]
    [MemberData(nameof(Cases))]
    public void TokenizesLikeVSCodeTextMateWithCompiledPatterns(string suite, int index)
        => Run(suite, index, path => GrammarLoader.Load(path), new TokenizerOptions { CompileHotPatterns = true, HotPatternThreshold = 0 });

    // The grammar files every case of the suites uses, as full paths.
    internal static IEnumerable<string> GrammarFiles()
    {
        foreach (var suite in new[] { "first-mate/tests.json", "suite1/tests.json", "suite1/whileTests.json" })
        {
            var suitePath = SuitePath(suite);
            using var document = JsonDocument.Parse(File.ReadAllBytes(suitePath));
            foreach (var test in document.RootElement.EnumerateArray())
            {
                foreach (var grammarFile in test.GetProperty("grammars").EnumerateArray())
                {
                    yield return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(suitePath)!, grammarFile.GetString()!));
                }
            }
        }
    }

    internal static void Run(string suite, int index, Func<string, IGrammarSource> load, TokenizerOptions? options = null)
    {
        var suitePath = SuitePath(suite);
        using var document = JsonDocument.Parse(File.ReadAllBytes(suitePath));
        var test = document.RootElement[index];
        var directory = Path.GetDirectoryName(suitePath)!;

        var registry = new GrammarRegistry();
        var scopeName = test.TryGetProperty("grammarScopeName", out var scopeProperty) ? scopeProperty.GetString() : null;
        var grammarPath = test.TryGetProperty("grammarPath", out var pathProperty) ? pathProperty.GetString() : null;
        foreach (var grammarFile in test.GetProperty("grammars").EnumerateArray())
        {
            var relative = grammarFile.GetString()!;
            var grammar = load(Path.GetFullPath(Path.Combine(directory, relative)));
            registry.Add(grammar);
            if (scopeName is null && relative == grammarPath)
            {
                scopeName = grammar.ScopeName;
            }
        }

        Assert.NotNull(scopeName);
        if (test.TryGetProperty("grammarInjections", out var injections))
        {
            foreach (var injection in injections.EnumerateArray())
            {
                registry.AddInjection(scopeName, injection.GetString()!);
            }
        }

        var tokenizer = registry.CreateTokenizer(scopeName, null, options ?? TokenizerOptions.Default);
        var state = StateStack.Initial;
        foreach (var testLine in test.GetProperty("lines").EnumerateArray())
        {
            var line = testLine.GetProperty("line").GetString()!;
            var expected = new StringBuilder();
            foreach (var token in testLine.GetProperty("tokens").EnumerateArray())
            {
                var value = token.GetProperty("value").GetString()!;

                // The reference harness drops empty expected tokens on non-empty lines.
                if (line.Length > 0 && value.Length == 0)
                {
                    continue;
                }

                AppendToken(expected, value, token.GetProperty("scopes").EnumerateArray().Select(s => s.GetString()!));
            }

            var actual = new StringBuilder();
            foreach (var token in tokenizer.TokenizeLine(line, state, out state))
            {
                AppendToken(actual, line.Substring(token.Start, token.Length), token.Scopes.GetScopeNames());
            }

            Assert.Equal(expected.ToString(), actual.ToString());
        }
    }

    private static void AppendToken(StringBuilder builder, string value, IEnumerable<string> scopes)
        => builder.Append('|').Append(value).Append("| ").AppendJoin(' ', scopes).Append('\n');

    private static string SuitePath(string suite) => Path.Combine(Fixtures.VSCodeTextMate, suite);
}
