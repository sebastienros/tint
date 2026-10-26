using System.Reflection;
using System.Text;
using Tint.Grammars;
using Tint.Themes;
using Xunit;

namespace Tint.Tests.Generator;

// The vscode-textmate suites again, through classes the generator emits from the suites' grammar and theme fixtures:
// generated with CSharpGeneratorDriver, compiled with Roslyn against Tint and GregsEx, loaded in an
// AssemblyLoadContext. They must produce the same tokens and colors as the loader path.
public sealed class GeneratedSuiteTests
{
    private static readonly Lazy<GeneratedFixtures> s_fixtures = new(() => new GeneratedFixtures());

    private static readonly Lazy<ThemeSuiteTests.Suite> s_themeSuite = new(() => ThemeSuiteTests.CreateSuite(
        (path, _) => s_fixtures.Value.Grammar(path),
        path => s_fixtures.Value.Theme(path)));

    public static IEnumerable<TheoryDataRow<string, int>> TokenizationCases() => TokenizationSuiteTests.Cases();

    public static IEnumerable<TheoryDataRow<string>> ThemeCases() => ThemeSuiteTests.Cases();

    [Fact]
    public void EveryFixtureGeneratesAndCompiles()
    {
        var fixtures = s_fixtures.Value;
        Assert.Empty(fixtures.Run.CompilationProblems);
        Assert.Empty(fixtures.Run.GeneratorDiagnostics.Where(d => d.Id is "TINT001" or "TINT002" or "TINT003" or "TINT008"));
    }

    [Theory]
    [MemberData(nameof(TokenizationCases))]
    public void GeneratedGrammarsTokenizeLikeVSCodeTextMate(string suite, int index)
        => TokenizationSuiteTests.Run(suite, index, s_fixtures.Value.Grammar);

    [Theory]
    [MemberData(nameof(ThemeCases))]
    public void GeneratedThemesColorLikeVSCodeTextMate(string file)
        => ThemeSuiteTests.Run(s_themeSuite.Value, file);

    private sealed class GeneratedFixtures
    {
        private readonly Assembly _assembly;
        private readonly Dictionary<string, string> _types = new(StringComparer.Ordinal);

        public GeneratedFixtures()
        {
            var files = new List<TestFile>();
            void Add(string path, string kind, params (string Key, string Value)[] metadata)
            {
                var name = ClassName(path);
                if (_types.TryAdd(path, "Fixtures." + name))
                {
                    files.Add(new TestFile(path, File.ReadAllText(path), metadata.Concat([("TintKind", kind), ("Name", name), ("Namespace", "Fixtures")]).ToDictionary(m => m.Item1, m => m.Item2)));
                }
            }

            foreach (var path in TokenizationSuiteTests.GrammarFiles())
            {
                Add(path, "Grammar");
            }

            foreach (var (path, language, embedded) in ThemeSuiteTests.GrammarFiles())
            {
                var metadata = new List<(string, string)>();
                if (language is not null)
                {
                    metadata.Add(("LanguageId", language));
                }

                if (embedded.Count > 0)
                {
                    metadata.Add(("EmbeddedLanguages", string.Join(",", embedded.Select(e => e.Key + "=" + e.Value))));
                }

                Add(path, "Grammar", [.. metadata]);
            }

            foreach (var (_, file) in ThemeSuiteTests.ThemeFiles)
            {
                Add(Path.GetFullPath(Path.Combine(ThemeSuiteTests.ThemesPath, file)), "Theme", ("ThemeMode", "Raw"));
            }

            Run = GeneratorHarness.Run(files);
            _assembly = Run.Load();
        }

        public GeneratorRun Run { get; }

        public IGrammarSource Grammar(string path) => (IGrammarSource)Instance(path);

        public IThemeSource Theme(string path) => (IThemeSource)Instance(path);

        private object Instance(string path)
            => _assembly.GetType(_types[path], throwOnError: true)!.GetProperty("Instance")!.GetValue(null)!;

        private static string ClassName(string path)
        {
            var relative = Path.GetRelativePath(Fixtures.VSCodeTextMate, path);
            var builder = new StringBuilder("F_");
            foreach (var c in relative)
            {
                builder.Append(char.IsAsciiLetterOrDigit(c) ? c : '_');
            }

            return builder.ToString();
        }
    }
}
