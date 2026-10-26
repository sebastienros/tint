using System.Text;
using System.Xml.Linq;
using Tint.Grammars;
using Tint.Loading;
using Tint.Themes;
using Xunit;

namespace Tint.Tests;

// Tint.Grammars and Tint.Themes against the files they were generated from: every item of the generated props is in
// the aggregate, and the generated classes tokenize and color exactly like the loader path.
public sealed class BundleTests
{
    private static readonly Lazy<IReadOnlyList<(string ClassName, string Path)>> s_grammarItems = new(() => ReadItems("src/Tint.Grammars/VSCodeGrammars.props", "TintGrammar"));
    private static readonly Lazy<IReadOnlyList<(string ClassName, string Path)>> s_themeItems = new(() => ReadItems("src/Tint.Themes/VSCodeThemes.props", "TintTheme"));

    // Both registries register in TintGrammars.All order so that language ids, and so encoded metadata, agree.
    private static readonly Lazy<GrammarRegistry> s_generated = new(() =>
    {
        var registry = new GrammarRegistry();
        registry.Add([.. TintGrammars.All]);
        return registry;
    });

    private static readonly Lazy<GrammarRegistry> s_loaded = new(() =>
    {
        var registry = new GrammarRegistry();
        registry.Add([.. TintGrammars.All.Select(g => (IGrammarSource)GrammarLoader.Load(GrammarPath(g), g.Info))]);
        return registry;
    });

    public static TheoryData<string> ScopeNames => [.. TintGrammars.All.Select(g => g.ScopeName)];

    public static TheoryData<string> ThemeNames => [.. s_themeItems.Value.Select(t => t.ClassName)];

    [Fact]
    public void EveryBundledGrammarIsInAllOnceWithAUniqueScopeAndClass()
    {
        var all = TintGrammars.All;

        Assert.Equal(84, s_grammarItems.Value.Count);
        Assert.Equal(s_grammarItems.Value.Select(i => i.ClassName).Order(StringComparer.Ordinal), all.Select(g => g.GetType().Name).Order(StringComparer.Ordinal));
        Assert.Equal(all.Count, all.Select(g => g.ScopeName).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(all.Select(g => g.ScopeName).Order(StringComparer.Ordinal), all.Select(g => g.ScopeName));
        Assert.All(all, g => Assert.Equal("Tint.Grammars", g.GetType().Namespace));
    }

    [Fact]
    public void TheBundledClassNamesArePinned()
    {
        // Public API: tools/Tint.Sync names the classes (BundleProps.GrammarClassName); a sync that renames one fails here.
        Assert.Equal(s_grammarClassNames, TintGrammars.All.Select(g => g.GetType().Name).Order(StringComparer.Ordinal));
        Assert.Equal(s_themeClassNames, TintThemes.All.Select(t => t.GetType().Name).Order(StringComparer.Ordinal));
        Assert.Same(TintGrammars.TypeScriptReact, TintGrammars.Find("source.tsx"));
        Assert.Same(TintGrammars.Php, TintGrammars.Find("text.html.php"));
        Assert.Same(TintGrammars.PhpSource, TintGrammars.Find("source.php"));
        Assert.Same(TintGrammars.Html, TintGrammars.Find("text.html.derivative"));
        Assert.Same(TintGrammars.Raku, TintGrammars.Find("source.perl.6"));
    }

    [Fact]
    public void TheGrammarsClassExposesEachGrammarByPropertyAndScope()
    {
        Assert.Same(CSharpGrammar.Instance, TintGrammars.CSharp);
        Assert.Same(TintGrammars.CSharp, TintGrammars.Find("source.cs"));
        Assert.Same(TintGrammars.Json, TintGrammars.Find("source.json"));
        Assert.Same(TintGrammars.Markdown, TintGrammars.Find("text.html.markdown"));
        Assert.Null(TintGrammars.Find("source.missing"));
        Assert.All(TintGrammars.All, g => Assert.Same(g, TintGrammars.Find(g.ScopeName)));
    }

    [Fact]
    public void GrammarInfoComesFromTheManifest()
    {
        var csharp = TintGrammars.CSharp.Info;
        Assert.Equal("csharp", csharp.LanguageId);
        Assert.Equal(["C#", "csharp"], csharp.Aliases);
        Assert.Contains(".cs", csharp.FileExtensions);
        Assert.Contains("keyword.operator.arrow.cs", csharp.UnbalancedBracketScopes);
        Assert.Equal(StandardTokenType.Other, Assert.Single(csharp.TokenTypes, t => t.Key == "meta.interpolation").Value);

        // One file, several registrations: the language named like the scope wins, the others add aliases and extensions.
        Assert.Equal("yaml", TintGrammars.Yaml.Info.LanguageId);
        Assert.Equal("ini", TintGrammars.Ini.Info.LanguageId);
        Assert.Contains(".properties", TintGrammars.Ini.Info.FileExtensions);
        Assert.Equal("prompt", TintGrammars.Prompt.Info.LanguageId);

        Assert.Equal(["text.html.markdown"], TintGrammars.MarkdownMathBlock.Info.InjectTo);
        Assert.Equal("latex", TintGrammars.MarkdownMathInline.Info.EmbeddedLanguages["punctuation.definition.math.end.markdown"]);
        Assert.Equal(["*"], TintGrammars.ShellScript.Info.BalancedBracketScopes);
        Assert.Null(TintGrammars.CSharp.Info.BalancedBracketScopes);
    }

    [Theory]
    [MemberData(nameof(ScopeNames))]
    public void EveryGrammarTokenizesItsSampleLikeTheLoaderPath(string scopeName)
    {
        var grammar = TintGrammars.Find(scopeName)!;
        var lines = Sample(grammar).Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        var generated = Tokenize(s_generated.Value.CreateTokenizer(scopeName), lines);
        var loaded = Tokenize(s_loaded.Value.CreateTokenizer(scopeName), lines);

        Assert.NotEmpty(generated);
        Assert.Equal(loaded.Count, generated.Count);
        for (var i = 0; i < loaded.Count; i++)
        {
            Assert.Equal(loaded[i], generated[i]);
        }
    }

    [Fact]
    public void RegisteringAllLeavesOnlyGrammarsVSCodeDoesNotShip()
    {
        var missing = TintGrammars.All
            .SelectMany(g => s_generated.Value.GetMissingGrammars(g.ScopeName).Select(m => (Grammar: g.ScopeName, Missing: m)))
            .GroupBy(e => e.Missing, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Grammar).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList(), StringComparer.Ordinal);

        Assert.Equal(s_knownMissing.Keys.Order(StringComparer.Ordinal), missing.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void EveryBundledThemeIsInAllOnce()
    {
        Assert.Equal(19, s_themeItems.Value.Count);
        Assert.Equal(s_themeItems.Value.Select(i => i.ClassName).Order(StringComparer.Ordinal), TintThemes.All.Select(t => t.GetType().Name));
        Assert.Same(DarkPlusTheme.Instance, TintThemes.DarkPlus);
        Assert.Same(TintThemes.DarkPlus, TintThemes.Find("Dark+"));
        Assert.Same(TintThemes.DarkPlus, TintThemes.Find("darkplus"));
        Assert.Same(TintThemes.Monokai, TintThemes.Find("Monokai"));
        Assert.Same(TintThemes.LightVisualStudio, TintThemes.Find("Light (Visual Studio)"));
        Assert.Null(TintThemes.Find("Missing"));
    }

    [Theory]
    [MemberData(nameof(ThemeNames))]
    public void EveryThemeColorsTheCSharpSampleLikeTheLoaderPath(string className)
    {
        var generatedSource = TintThemes.All.Single(t => t.GetType().Name == className);
        var path = s_themeItems.Value.Single(t => t.ClassName == className).Path;
        var loadedSource = ThemeLoader.Load(path);
        Assert.Equal(loadedSource.Name, generatedSource.Name);

        var lines = File.ReadAllLines(Path.Combine(Fixtures.Samples, "csharp.sample"));
        Assert.Equal(Colors(Theme.FromSource(loadedSource), lines), Colors(Theme.FromSource(generatedSource), lines));
    }

    private static readonly string[] s_grammarClassNames =
    [
        "BatchGrammar",
        "BibtexGrammar",
        "CGrammar",
        "CPlatformGrammar",
        "CSharpGrammar",
        "ClojureGrammar",
        "CoffeeScriptGrammar",
        "CppEmbeddedLatexGrammar",
        "CppGrammar",
        "CppMacroGrammar",
        "CssGrammar",
        "CudaCppGrammar",
        "DartGrammar",
        "DiffGrammar",
        "DockerfileGrammar",
        "DotenvGrammar",
        "FSharpGrammar",
        "GitCommitGrammar",
        "GitRebaseGrammar",
        "GoGrammar",
        "GroovyGrammar",
        "HandlebarsGrammar",
        "HlslGrammar",
        "HtmlBasicGrammar",
        "HtmlGrammar",
        "IgnoreGrammar",
        "IniGrammar",
        "JavaGrammar",
        "JavaScriptGrammar",
        "JavaScriptReactGrammar",
        "JavaScriptRegexpGrammar",
        "JsDocJavaScriptInjectionGrammar",
        "JsDocTypeScriptInjectionGrammar",
        "JsonGrammar",
        "JsonLinesGrammar",
        "JsoncGrammar",
        "JuliaGrammar",
        "LatexGrammar",
        "LessGrammar",
        "LogGrammar",
        "LuaGrammar",
        "MakefileGrammar",
        "MarkdownGrammar",
        "MarkdownLatexCombinedGrammar",
        "MarkdownMathBlockGrammar",
        "MarkdownMathCodeBlockGrammar",
        "MarkdownMathGrammar",
        "MarkdownMathInlineGrammar",
        "ObjectiveCGrammar",
        "ObjectiveCppGrammar",
        "PerlGrammar",
        "PhpGrammar",
        "PhpSourceGrammar",
        "PowerShellGrammar",
        "PromptGrammar",
        "PugGrammar",
        "PythonGrammar",
        "PythonRegexpGrammar",
        "RGrammar",
        "RakuGrammar",
        "RazorGrammar",
        "RestructuredTextGrammar",
        "RubyGrammar",
        "RustGrammar",
        "SassdocGrammar",
        "ScssGrammar",
        "SearchResultGrammar",
        "ShaderLabGrammar",
        "ShellScriptGrammar",
        "SnippetsGrammar",
        "SqlGrammar",
        "SwiftGrammar",
        "TexGrammar",
        "TypeScriptGrammar",
        "TypeScriptReactGrammar",
        "VisualBasicGrammar",
        "XmlGrammar",
        "XslGrammar",
        "Yaml10Grammar",
        "Yaml11Grammar",
        "Yaml12Grammar",
        "Yaml13Grammar",
        "YamlEmbeddedGrammar",
        "YamlGrammar",
    ];

    private static readonly string[] s_themeClassNames =
    [
        "AbyssTheme",
        "Dark2026Theme",
        "DarkHighContrastTheme",
        "DarkModernTheme",
        "DarkPlusTheme",
        "DarkVisualStudioTheme",
        "KimbieDarkTheme",
        "Light2026Theme",
        "LightHighContrastTheme",
        "LightModernTheme",
        "LightPlusTheme",
        "LightVisualStudioTheme",
        "MonokaiDimmedTheme",
        "MonokaiTheme",
        "QuietLightTheme",
        "RedTheme",
        "SolarizedDarkTheme",
        "SolarizedLightTheme",
        "TomorrowNightBlueTheme",
    ];

    // Scope names some bundled grammar includes that no VS Code extension contributes a grammar for. They are skipped,
    // as in VS Code without the extension that would provide them.
    private static readonly Dictionary<string, string> s_knownMissing = new(StringComparer.Ordinal)
    {
        ["source.abap"] = "markdown fenced code blocks for languages VS Code has no grammar for",
        ["source.arm"] = "C/C++ inline assembly; VS Code ships no assembly grammars",
        ["source.asm"] = "C/C++ inline assembly; VS Code ships no assembly grammars",
        ["source.asy"] = "LaTeX embedded Asymptote; no VS Code grammar",
        ["source.asymptote"] = "LaTeX embedded Asymptote; no VS Code grammar",
        ["source.cmake"] = "reStructuredText code blocks; no VS Code grammar",
        ["source.dot"] = "LaTeX embedded Graphviz; no VS Code grammar",
        ["source.dts"] = "reStructuredText code blocks; no VS Code grammar",
        ["source.elixir"] = "markdown fenced code blocks; no VS Code grammar",
        ["source.erlang"] = "markdown fenced code blocks; no VS Code grammar",
        ["source.glsl"] = "C/C++ embedded GLSL; no VS Code grammar",
        ["source.gnuplot"] = "LaTeX embedded gnuplot; no VS Code grammar",
        ["source.graphql"] = "Ruby heredocs; no VS Code grammar",
        ["source.haskell"] = "LaTeX embedded Haskell; no VS Code grammar",
        ["source.html"] = "a stale scope name in sassdoc (HTML is text.html.basic)",
        ["source.js.jquery"] = "Ruby heredocs; no VS Code grammar",
        ["source.kconfig"] = "reStructuredText code blocks; no VS Code grammar",
        ["source.less"] = "a stale scope name in pug (Less is source.css.less)",
        ["source.postscript"] = "Perl heredocs; no VS Code grammar",
        ["source.sass"] = "pug filters; VS Code ships SCSS only",
        ["source.scala"] = "markdown, LaTeX and search-result code blocks; no VS Code grammar",
        ["source.stylus"] = "pug filters; no VS Code grammar",
        ["source.syntax.regexp.tmLanguage"] = "snippet regular expressions; no VS Code grammar",
        ["source.twig"] = "markdown fenced code blocks; no VS Code grammar",
        ["source.x86"] = "C/C++ inline assembly; VS Code ships no assembly grammars",
        ["source.x86_64"] = "C/C++ inline assembly; VS Code ships no assembly grammars",
        ["source.yang"] = "markdown fenced code blocks; no VS Code grammar",
        ["text.haml"] = "Ruby heredocs; no VS Code grammar",
        ["text.html.javadoc"] = "Groovy doc comments; no VS Code grammar",
        ["text.html.php.blade"] = "PHP's Blade injection; no VS Code grammar",
        ["text.slim"] = "Ruby heredocs; no VS Code grammar",
    };

    private static List<string> Tokenize(Tokenizer tokenizer, List<string> lines)
    {
        var tokens = new List<string>();
        var state = StateStack.Initial;
        for (var i = 0; i < lines.Count; i++)
        {
            foreach (var token in tokenizer.TokenizeLine(lines[i], state, out state))
            {
                tokens.Add($"{i}:{token.Start}+{token.Length} {token.Scopes.TokenAttributes:X8} {token.Scopes}");
            }
        }

        return tokens;
    }

    private static string Colors(Theme theme, string[] lines)
    {
        var tokenizer = s_generated.Value.CreateTokenizer("source.cs", theme);
        var output = new StringBuilder();
        var state = StateStack.Initial;
        foreach (var line in lines)
        {
            foreach (var token in tokenizer.TokenizeLine(line, state, out state))
            {
                output.Append(line, token.Start, token.Length).Append(' ')
                    .Append(theme.ColorMap.GetColor(token.Foreground)).Append(' ')
                    .Append(theme.ColorMap.GetColor(token.Background)).Append(' ')
                    .Append(token.FontStyle).Append('\n');
            }
        }

        return output.ToString();
    }

    // The shiki sample for the grammar's language when there is one, else the grammar's own file.
    private static string Sample(IGrammarSource grammar)
    {
        if (grammar.Info.LanguageId is { } language && File.Exists(Path.Combine(Fixtures.Samples, language + ".sample")))
        {
            return File.ReadAllText(Path.Combine(Fixtures.Samples, language + ".sample"));
        }

        return File.ReadAllText(GrammarPath(grammar));
    }

    private static string GrammarPath(IGrammarSource grammar)
        => s_grammarItems.Value.Single(i => i.ClassName == grammar.GetType().Name).Path;

    private static IReadOnlyList<(string ClassName, string Path)> ReadItems(string props, string itemName)
    {
        var path = Path.Combine(Fixtures.RepositoryRoot, props);
        var directory = Path.GetDirectoryName(path)!;
        return
        [
            .. XDocument.Load(path).Descendants(itemName).Select(item => (
                (string)item.Attribute("Name")!,
                Path.GetFullPath(Path.Combine(directory, Uri.UnescapeDataString(((string)item.Attribute("Include")!).Replace("$(MSBuildThisFileDirectory)", "", StringComparison.Ordinal))))))
        ];
    }
}
