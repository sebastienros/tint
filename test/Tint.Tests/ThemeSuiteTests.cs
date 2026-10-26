using System.Text;
using System.Text.Json;
using Tint.Grammars;
using Tint.Loading;
using Tint.Themes;
using Xunit;

namespace Tint.Tests;

// vscode-textmate's src/tests/themes.test.ts: every file of themes/tests tokenized with tokenizeLine2 under 13
// themes, foreground colors compared with the .result files.
public sealed class ThemeSuiteTests
{
    internal static readonly (string Name, string File)[] ThemeFiles =
    [
        ("abyss", "Abyss.tmTheme"),
        ("dark_vs", "dark_vs.json"),
        ("light_vs", "light_vs.json"),
        ("hc_black", "hc_black.json"),
        ("dark_plus", "dark_plus.json"),
        ("light_plus", "light_plus.json"),
        ("kimbie_dark", "Kimbie_dark.tmTheme"),
        ("monokai", "Monokai.tmTheme"),
        ("monokai_dimmed", "dimmed-monokai.tmTheme"),
        ("quietlight", "QuietLight.tmTheme"),
        ("red", "red.tmTheme"),
        ("solarized_dark", "Solarized-dark.tmTheme"),
        ("solarized_light", "Solarized-light.tmTheme"),
        ("tomorrow_night_blue", "Tomorrow-Night-Blue.tmTheme"),
    ];

    private static readonly Lazy<Suite> s_suite = new(() => new Suite(
        (path, info) => GrammarLoader.Load(path, info),
        path => ThemeLoader.Load(path, ThemeLoadMode.Raw)));

    internal static string ThemesPath => Path.Combine(Fixtures.VSCodeTextMate, "themes");

    public static IEnumerable<TheoryDataRow<string>> Cases()
    {
        foreach (var file in Directory.GetFiles(Path.Combine(ThemesPath, "tests")).Select(Path.GetFileName).Order(StringComparer.Ordinal))
        {
            if (file!.EndsWith(".result", StringComparison.Ordinal) || file.EndsWith(".result.patch", StringComparison.Ordinal)
                || file.EndsWith(".actual", StringComparison.Ordinal) || file.EndsWith(".diff.html", StringComparison.Ordinal))
            {
                continue;
            }

            yield return new TheoryDataRow<string>(file) { TestDisplayName = $"themes: {file}" };
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ColorsLikeVSCodeTextMate(string file) => Run(s_suite.Value, file);

    // The suite's grammars (full path, language and embedded languages) and themes (full path).
    internal static IEnumerable<(string Path, string? Language, IReadOnlyDictionary<string, string> EmbeddedLanguages)> GrammarFiles()
    {
        using var grammars = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ThemesPath, "grammars.json")));
        foreach (var grammar in grammars.RootElement.EnumerateArray())
        {
            yield return (Path.GetFullPath(Path.Combine(ThemesPath, grammar.GetProperty("path").GetString()!)), Language(grammar), EmbeddedLanguages(grammar));
        }
    }

    internal static Suite CreateSuite(Func<string, GrammarInfo, IGrammarSource> loadGrammar, Func<string, IThemeSource> loadTheme) => new(loadGrammar, loadTheme);

    internal static void Run(Suite suite, string file)
    {
        var contents = File.ReadAllText(Path.Combine(ThemesPath, "tests", file));
        using var expected = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ThemesPath, "tests", file + ".result")));

        var language = suite.FindLanguageByExtension(Path.GetExtension(file)) ?? suite.FindLanguageByFilename(file)
            ?? throw new InvalidOperationException("Could not determine language for " + file);
        var scopeName = suite.FindScopeByLanguage(language);

        foreach (var (name, theme) in suite.Themes)
        {
            var expectedText = new StringBuilder();
            foreach (var token in expected.RootElement.GetProperty(name).EnumerateArray())
            {
                expectedText.Append(token.GetProperty("content").GetString()).Append('\t').Append(token.GetProperty("color").GetString()).Append('\n');
            }

            var actualText = new StringBuilder();
            var tokenizer = suite.Registry.CreateTokenizer(scopeName, theme);
            var state = StateStack.Initial;
            foreach (var line in contents.ReplaceLineEndings("\n").Split('\n'))
            {
                var tokens = tokenizer.TokenizeLineEncoded(line, state, out state);
                for (var i = 0; i < tokens.Length; i++)
                {
                    var start = tokens[i].Start;
                    var end = i + 1 < tokens.Length ? tokens[i + 1].Start : line.Length;
                    if (end <= start)
                    {
                        continue;
                    }

                    actualText.Append(line, start, end - start).Append('\t').Append(theme.ColorMap.GetColor(tokens[i].Foreground)).Append('\n');
                }
            }

            Assert.Equal(expectedText.ToString(), actualText.ToString());
        }
    }

    private static string? Language(JsonElement grammar) => grammar.TryGetProperty("language", out var l) ? l.GetString() : null;

    private static Dictionary<string, string> EmbeddedLanguages(JsonElement grammar)
    {
        var embedded = new Dictionary<string, string>();
        if (grammar.TryGetProperty("embeddedLanguages", out var e))
        {
            foreach (var entry in e.EnumerateObject())
            {
                embedded[entry.Name] = entry.Value.GetString()!;
            }
        }

        return embedded;
    }

    internal sealed class Suite
    {
        private readonly List<(string Id, string[] Extensions, string[] Filenames)> _languages = [];
        private readonly List<(string? Language, string ScopeName)> _grammars = [];

        public Suite(Func<string, GrammarInfo, IGrammarSource> loadGrammar, Func<string, IThemeSource> loadTheme)
        {
            using (var languages = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ThemesPath, "languages.json"))))
            {
                foreach (var language in languages.RootElement.EnumerateArray())
                {
                    var id = language.GetProperty("id").GetString()!;
                    _languages.Add((id, Strings(language, "extensions"), Strings(language, "filenames")));

                    // Language ids are assigned 1..N in languages.json order.
                    Registry.GetLanguageId(id);
                }
            }

            foreach (var (path, language, embedded) in GrammarFiles())
            {
                var loaded = loadGrammar(path, new GrammarInfo { LanguageId = language, EmbeddedLanguages = embedded });
                Registry.Add(loaded);
                _grammars.Add((language, loaded.ScopeName));
            }

            // vscode-textmate's tests hand the theme files to the tokenizer as written, without VS Code's defaults.
            foreach (var (name, file) in ThemeFiles)
            {
                Themes.Add((name, Theme.FromSource(loadTheme(Path.GetFullPath(Path.Combine(ThemesPath, file))))));
            }
        }

        public GrammarRegistry Registry { get; } = new();

        public List<(string Name, Theme Theme)> Themes { get; } = [];

        public string? FindLanguageByExtension(string extension)
            => _languages.FirstOrDefault(l => l.Extensions.Contains(extension)).Id;

        public string? FindLanguageByFilename(string filename)
            => _languages.FirstOrDefault(l => l.Filenames.Contains(filename)).Id;

        public string FindScopeByLanguage(string language)
            => _grammars.First(g => g.Language == language).ScopeName;

        private static string[] Strings(JsonElement element, string property)
            => element.TryGetProperty(property, out var value) ? [.. value.EnumerateArray().Select(v => v.GetString()!)] : [];
    }
}
