using Microsoft.CodeAnalysis;
using Tint.Grammars;
using Tint.Loading;
using Tint.Themes;
using Xunit;

namespace Tint.Tests.Generator;

public sealed class GeneratorTests
{
    private const string SimpleGrammar = """
        {
          "scopeName": "source.simple",
          "name": "Simple",
          "patterns": [ { "include": "#word" }, { "match": "\\A#!.*", "name": "comment.shebang" } ],
          "repository": {
            "word": { "match": "\\w+", "name": "word.simple" },
            "block": { "begin": "(<<)(\\w+)", "end": "\\2", "name": "block.simple" }
          }
        }
        """;

    private const string SimpleTheme = """
        {
          "name": "Simple Dark",
          "type": "dark",
          "colors": { "editor.foreground": "#ffffff", "editor.background": "#000000" },
          "tokenColors": [ { "scope": "word.simple", "settings": { "foreground": "#ff0000", "fontStyle": "bold" } } ]
        }
        """;

    private static string CSharpGrammarPath => Path.Combine(GeneratorHarness.RepositoryRoot, "grammars", "csharp", "syntaxes", "csharp.tmLanguage.json");

    [Fact]
    public void AnAttributeBindsAPartialClassToTheItemWithItsPath()
    {
        var run = GeneratorHarness.Run(
            [TestFile.Grammar("grammars/simple.tmLanguage.json", SimpleGrammar)],
            """
            namespace My.Grammars;

            [Tint.TextMateGrammar("grammars/simple.tmLanguage.json", LanguageId = "simple", Aliases = ["Simple"])]
            public partial class Simple;
            """);

        Assert.Empty(run.GeneratorDiagnostics.Where(d => d.Severity > DiagnosticSeverity.Info));
        Assert.Empty(run.CompilationProblems);
        var source = run.Source("My.Grammars.Simple");
        Assert.Contains("partial class Simple : global::Tint.Grammars.IGrammarSource", source);
        Assert.Contains("LanguageId = \"simple\"", source);
        Assert.DoesNotContain(run.Sources.Keys, k => k.Contains("SimpleGrammar", StringComparison.Ordinal));
    }

    [Fact]
    public void AttributePathsMatchWithBackslashesAndDotSegments()
    {
        var run = GeneratorHarness.Run(
            [TestFile.Grammar("grammars/simple.tmLanguage.json", SimpleGrammar)],
            """
            [Tint.TextMateGrammar(@".\other\..\grammars\simple.tmLanguage.json")]
            partial class Simple;
            """);

        Assert.Empty(run.Tint("TINT001"));
        Assert.Contains("Simple.g.cs", run.Sources.Keys);
    }

    [Fact]
    public void ItemOnlyGrammarsAreNamedFromTheScopeInTheRootNamespace()
    {
        var run = GeneratorHarness.Run(
        [
            TestFile.Grammar("simple.tmLanguage.json", SimpleGrammar),
            TestFile.Grammar("csharp.tmLanguage.json", """{ "scopeName": "source.cs", "patterns": [] }"""),
            TestFile.Grammar("html.json", """{ "scopeName": "text.html.basic", "patterns": [] }"""),
        ]);

        Assert.Empty(run.CompilationProblems);
        Assert.Contains("internal partial class SimpleGrammar", run.Source("Test.Root.SimpleGrammar"));
        Assert.Contains("Test.Root.CSharpGrammar.g.cs", run.Sources.Keys);
        Assert.Contains("Test.Root.HtmlBasicGrammar.g.cs", run.Sources.Keys);
    }

    [Fact]
    public void ItemOnlyThemesAreNamedFromTheFile()
    {
        var run = GeneratorHarness.Run([TestFile.Theme("themes/dark_plus.json", SimpleTheme)]);

        Assert.Empty(run.CompilationProblems);
        Assert.Contains("partial class DarkPlusTheme : global::Tint.Themes.IThemeSource", run.Source("Test.Root.DarkPlusTheme"));
    }

    [Fact]
    public void NameNamespaceAndAccessibilityMetadataOverrideTheDefaults()
    {
        var run = GeneratorHarness.Run(
        [
            TestFile.Grammar("simple.tmLanguage.json", SimpleGrammar, ("Name", "Custom"), ("Namespace", "Other.Place"), ("Accessibility", "public")),
            TestFile.Theme("dark.json", SimpleTheme, ("Name", "Night")),
        ]);

        Assert.Empty(run.CompilationProblems);
        Assert.Contains("public partial class Custom", run.Source("Other.Place.Custom"));
        Assert.Contains("Test.Root.Night.g.cs", run.Sources.Keys);
    }

    [Fact]
    public void ItemMetadataBecomesTheGrammarInfo()
    {
        var run = GeneratorHarness.Run(
        [
            TestFile.Grammar(
                "simple.tmLanguage.json",
                SimpleGrammar,
                ("LanguageId", "simple"),
                ("Aliases", "Simple,simple"),
                ("FileExtensions", ".simple"),
                ("EmbeddedLanguages", "meta.embedded.css=css"),
                ("InjectTo", "source.cs"),
                ("InjectionSelector", "L:comment"),
                ("TokenTypes", "string.quoted=string,meta.other=other"),
                ("BalancedBracketScopes", "*"),
                ("UnbalancedBracketScopes", "keyword.operator")),
            TestFile.Grammar("includer.json", """{ "scopeName": "source.includer", "patterns": [ { "include": "source.simple" }, { "include": "source.css#rule" } ] }"""),
        ]);

        var assembly = run.Load();
        var simple = Grammar(assembly, "Test.Root.SimpleGrammar");
        Assert.Equal("simple", simple.Info.LanguageId);
        Assert.Equal(["Simple", "simple"], simple.Info.Aliases);
        Assert.Equal([".simple"], simple.Info.FileExtensions);
        Assert.Equal("css", simple.Info.EmbeddedLanguages["meta.embedded.css"]);
        Assert.Equal(["source.cs"], simple.Info.InjectTo);
        Assert.Equal("L:comment", simple.Info.InjectionSelector);
        Assert.Equal([new("string.quoted", StandardTokenType.String), new("meta.other", StandardTokenType.Other)], simple.Info.TokenTypes);
        Assert.Equal(["*"], simple.Info.BalancedBracketScopes!);
        Assert.Equal(["keyword.operator"], simple.Info.UnbalancedBracketScopes);
        Assert.Empty(simple.Info.Dependencies);

        var includer = Grammar(assembly, "Test.Root.IncluderGrammar");
        Assert.Equal(["source.simple", "source.css"], includer.Info.Dependencies);
        Assert.Null(includer.Info.BalancedBracketScopes);
    }

    [Fact]
    public void TheGeneratedGrammarMirrorsTheLoadedOne()
    {
        var run = GeneratorHarness.Run([TestFile.Grammar("simple.tmLanguage.json", SimpleGrammar)]);
        var generated = Grammar(run.Load(), "Test.Root.SimpleGrammar").GetGrammar();
        var loaded = GrammarLoader.Load(System.Text.Encoding.UTF8.GetBytes(SimpleGrammar), DocumentFormat.Json).GetGrammar();

        Assert.Equal(loaded.ScopeName, generated.ScopeName);
        Assert.Equal(loaded.Name, generated.Name);
        Assert.Equal(loaded.Patterns.Count, generated.Patterns.Count);
        Assert.Equal(loaded.Repository.Keys.Order(), generated.Repository.Keys.Order());
        Assert.Equal(loaded.Repository["block"].End!.Text, generated.Repository["block"].End!.Text);
        Assert.True(generated.Repository["block"].End!.HasBackReferences);

        // Back-referencing end patterns are resolved per match at run time; everything else is precompiled.
        Assert.Null(generated.Repository["block"].End!.PatternFactory);
        Assert.NotNull(generated.Repository["word"].Match!.PatternFactory);
        var shebang = generated.Patterns[1].Match!;
        Assert.True(shebang.HasAnchorA);
        Assert.Equal("\\A#!.*", shebang.GetText(RegexAnchorVariant.A1G0));
        Assert.NotSame(shebang.PatternFactory!(RegexAnchorVariant.A0G0), shebang.PatternFactory(RegexAnchorVariant.A1G0));
        Assert.Same(shebang.PatternFactory(RegexAnchorVariant.A1G0), shebang.PatternFactory(RegexAnchorVariant.A1G1));
    }

    [Fact]
    public void ThePatternTableIsDeduplicatedAndHoldsTheAnchorVariants()
    {
        var grammar = """
            {
              "scopeName": "source.dedup",
              "patterns": [ { "match": "\\w+" }, { "match": "\\w+" }, { "match": "\\Gx" }, { "match": "\\xff\\z" } ]
            }
            """;
        var run = GeneratorHarness.Run([TestFile.Grammar("dedup.json", grammar)]);
        var type = run.Load().GetType("Test.Root.DedupGrammar")!;
        var index = (IReadOnlyDictionary<string, int>)type.GetProperty("PatternIndex")!.GetValue(null)!;

        Assert.Equal(["\\w+", "\\￿x", "\\Gx", "\\x{ff}$(?!\\n)(?<!\\n)"], index.OrderBy(e => e.Value).Select(e => e.Key));
        var rules = ((IGrammarSource)type.GetProperty("Instance")!.GetValue(null)!).GetGrammar().Patterns;
        Assert.Same(rules[0].Match, rules[1].Match);
    }

    [Fact]
    public void TheGeneratedThemeAppliesVSCodeDefaultsUnlessRaw()
    {
        var run = GeneratorHarness.Run(
        [
            TestFile.Theme("vscode.json", SimpleTheme),
            TestFile.Theme("raw.json", SimpleTheme, ("ThemeMode", "Raw")),
        ]);
        var assembly = run.Load();

        var vscode = Theme(assembly, "Test.Root.VscodeTheme").GetTheme();
        var loaded = ThemeLoader.Load(System.Text.Encoding.UTF8.GetBytes(SimpleTheme), DocumentFormat.Json).GetTheme();
        Assert.Equal("Simple Dark", vscode.Name);
        Assert.Equal("dark", vscode.Type);
        Assert.Equal(Describe(loaded), Describe(vscode));
        Assert.Equal("#000000", vscode.UiColors["editor.background"]);

        var raw = Theme(assembly, "Test.Root.RawTheme").GetTheme();
        Assert.Single(raw.Rules);
    }

    [Fact]
    public void ThemeIncludesResolveAmongAdditionalFiles()
    {
        var run = GeneratorHarness.Run(
        [
            TestFile.Theme("themes/plus.json", """{ "name": "Plus", "include": "./base/base.json", "tokenColors": [ { "scope": "b", "settings": { "foreground": "#222222" } } ] }"""),
            new TestFile("themes/base/base.json", """{ "name": "Base", "tokenColors": [ { "scope": "a", "settings": { "foreground": "#111111" } } ] }"""),
        ]);

        var theme = Theme(run.Load(), "Test.Root.PlusTheme").GetTheme();
        Assert.Equal(["a", "b"], theme.Rules.Where(r => r.Scopes is not null).Select(r => r.Scopes![0]));
        Assert.DoesNotContain("Test.Root.BaseTheme.g.cs", run.Sources.Keys);
    }

    [Fact]
    public void GeneratedCodeCompilesWarningFreeUnderNullableAndWarningsAsErrors()
    {
        var run = GeneratorHarness.Run(
        [
            TestFile.Grammar("csharp.tmLanguage.json", File.ReadAllText(CSharpGrammarPath)),
            TestFile.Theme("dark_plus.json", File.ReadAllText(Path.Combine(GeneratorHarness.RepositoryRoot, "themes", "theme-defaults", "dark_plus.json"))),
            new TestFile("dark_vs.json", File.ReadAllText(Path.Combine(GeneratorHarness.RepositoryRoot, "themes", "theme-defaults", "dark_vs.json"))),
        ]);

        Assert.Empty(run.GeneratorDiagnostics.Where(d => d.Severity > DiagnosticSeverity.Info));
        Assert.Empty(run.CompilationProblems);
        Assert.DoesNotContain("WithGeneratedKernels", run.Source("Test.Root.CSharpGrammar"));
    }

    [Fact]
    public void KernelsOptedInEmitGregsExKernelsForTheCSharpGrammar()
    {
        var run = GeneratorHarness.Run(
            [TestFile.Grammar("csharp.tmLanguage.json", File.ReadAllText(CSharpGrammarPath))],
            """
            [Tint.TextMateGrammar("csharp.tmLanguage.json", Kernels = true)]
            internal partial class CSharp;
            """,
            // Kernels bind GregsEx's Run methods as method groups, which Roslyn 5.0's nullable analysis crashes on under
            // C# 12 (fixed in 5.3); GregsEx's own generator emits the same code.
            languageVersion: Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp14);

        Assert.Empty(run.CompilationProblems);
        var kernels = run.Sources["CSharp.Kernels.g.cs"];
        Assert.StartsWith("// <auto-generated/>", kernels, StringComparison.Ordinal);
        Assert.Contains("using System;", kernels);
        Assert.Contains(".WithGeneratedKernels(0x", kernels);
        var summary = Assert.Single(run.Tint("TINT011"));
        Assert.Equal(DiagnosticSeverity.Info, summary.Severity);

        // Every kernel the generated grammar binds matches the pattern the runtime parses (WithGeneratedKernels checks
        // the fingerprint).
        var grammar = Grammar(run.Load(), "CSharp");
        var patterns = grammar.GetGrammar().Repository.Values.Select(r => r.Match).OfType<RegexSource>().Select(m => m.PatternFactory!(RegexAnchorVariant.A0G0)).ToList();
        Assert.Contains(patterns, p => p.ExecutionBackend == GregsEx.RegexExecutionBackend.Compiled);
        var registry = new GrammarRegistry();
        registry.Add(grammar);
        Assert.NotEmpty(registry.CreateTokenizer("source.cs").TokenizeLine("public static class C { int x = 1; }", StateStack.Initial, out _).ToArray());
    }

    [Fact]
    public void KernelsThroughTheProjectPropertyAndItemMetadata()
    {
        var grammar = """{ "scopeName": "source.k", "patterns": [ { "match": "abc" } ] }""";
        var byProperty = GeneratorHarness.Run([TestFile.Grammar("k.json", grammar)], properties: new Dictionary<string, string> { ["TintKernels"] = "true" });
        Assert.Contains("KGrammar.Kernels.g.cs", string.Join(",", byProperty.Sources.Keys));

        var byItem = GeneratorHarness.Run([TestFile.Grammar("k.json", grammar, ("Kernels", "true"))]);
        Assert.Contains("Test.Root.KGrammar.Kernels.g.cs", byItem.Sources.Keys);

        var off = GeneratorHarness.Run([TestFile.Grammar("k.json", grammar, ("Kernels", "false"))], properties: new Dictionary<string, string> { ["TintKernels"] = "true" });
        Assert.DoesNotContain("Test.Root.KGrammar.Kernels.g.cs", off.Sources.Keys);
    }

    [Fact]
    public void AnUnmatchedAttributePathIsTint001()
    {
        var run = GeneratorHarness.Run([], """
            [Tint.TextMateGrammar("missing.json")]
            partial class Missing;
            """);

        var diagnostic = Assert.Single(run.Tint("TINT001"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("missing.json", diagnostic.GetMessage());
        Assert.EndsWith("Source.cs", diagnostic.Location.GetLineSpan().Path, StringComparison.Ordinal);
        Assert.Equal(0, diagnostic.Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public void ADuplicateClassNameIsTint002()
    {
        var run = GeneratorHarness.Run(
        [
            TestFile.Grammar("a/simple.json", SimpleGrammar),
            TestFile.Grammar("b/simple.json", SimpleGrammar),
        ]);

        var diagnostic = Assert.Single(run.Tint("TINT002"));
        Assert.Contains("Test.Root.SimpleGrammar", diagnostic.GetMessage());
        Assert.Empty(run.CompilationProblems);
    }

    [Fact]
    public void AnInvalidFileIsTint003WithItsPosition()
    {
        var run = GeneratorHarness.Run([TestFile.Grammar("broken.json", "{\n  \"scopeName\": \"source.broken\",\n  \"patterns\": [ }\n}")]);

        var diagnostic = Assert.Single(run.Tint("TINT003"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(2, diagnostic.Location.GetLineSpan().StartLinePosition.Line);
        Assert.Contains("line 3", diagnostic.GetMessage());

        var plist = GeneratorHarness.Run([TestFile.Theme("broken.tmTheme", "<plist><dict><key>name</key>")]);
        Assert.Single(plist.Tint("TINT003"));
    }

    [Fact]
    public void AnInvalidRegexIsTint004WithTheRulePathAndOffset()
    {
        var run = GeneratorHarness.Run([TestFile.Grammar("bad.json", """
            { "scopeName": "source.bad", "repository": { "comment": { "begin": "a(b", "end": "c" } }, "patterns": [] }
            """)]);

        var diagnostic = Assert.Single(run.Tint("TINT004"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("repository.comment.begin", diagnostic.GetMessage());
        Assert.Contains("offset", diagnostic.GetMessage());

        // The invalid pattern gets no factory, so the runtime treats it as the loader does (it never matches).
        var grammar = Grammar(run.Load(), "Test.Root.BadGrammar").GetGrammar();
        Assert.Null(grammar.Repository["comment"].Begin!.PatternFactory);
    }

    [Fact]
    public void ExternalIncludesAreTint005()
    {
        var run = GeneratorHarness.Run([TestFile.Grammar("html.json", """
            { "scopeName": "text.html.x", "patterns": [ { "include": "source.js" }, { "include": "source.css#rules" }, { "include": "source.js" } ] }
            """)]);

        var diagnostic = Assert.Single(run.Tint("TINT005"));
        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
        Assert.Contains("source.js, source.css", diagnostic.GetMessage());
    }

    [Fact]
    public void KernelsWithoutGregsExAreTint006AndValidationIsSkippedWithTint007()
    {
        var run = GeneratorHarness.Run(
            [TestFile.Grammar("bad.json", """{ "scopeName": "source.bad", "patterns": [ { "match": "(" } ] }""", ("Kernels", "true"))],
            referenceGregsEx: false);

        Assert.Equal(DiagnosticSeverity.Info, Assert.Single(run.Tint("TINT006")).Severity);
        Assert.Equal(DiagnosticSeverity.Info, Assert.Single(run.Tint("TINT007")).Severity);
        Assert.Empty(run.Tint("TINT004"));
        Assert.Contains("Test.Root.BadGrammar.g.cs", run.Sources.Keys);
    }

    [Fact]
    public void TheGregsExPackagePathPropertyOverridesDiscovery()
    {
        var grammar = """{ "scopeName": "source.bad", "patterns": [ { "match": "(" } ] }""";
        var overridden = GeneratorHarness.Run(
            [TestFile.Grammar("bad.json", grammar)],
            properties: new Dictionary<string, string> { ["TintGregsExPackagePath"] = GeneratorHarness.GregsExPackagePath },
            referenceGregsEx: false);
        Assert.Single(overridden.Tint("TINT004"));

        var wrong = GeneratorHarness.Run(
            [TestFile.Grammar("bad.json", grammar)],
            properties: new Dictionary<string, string> { ["TintGregsExPackagePath"] = Path.Combine(GeneratorHarness.ProjectDirectory, "no-such-package") });
        Assert.Contains("no-such-package", Assert.Single(wrong.Tint("TINT007")).GetMessage());
    }

    [Fact]
    public void AMissingThemeIncludeIsTint008()
    {
        var run = GeneratorHarness.Run([TestFile.Theme("plus.json", """{ "include": "./missing.json", "tokenColors": [] }""")]);

        var diagnostic = Assert.Single(run.Tint("TINT008"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("Theme 'plus.json' includes 'missing.json', which is not an AdditionalFiles item; add it as a TintTheme or AdditionalFiles item", diagnostic.GetMessage());
    }

    [Fact]
    public void ANonPartialOrGenericTargetIsTint009()
    {
        var run = GeneratorHarness.Run([TestFile.Grammar("simple.json", SimpleGrammar)], """
            [Tint.TextMateGrammar("simple.json")]
            class NotPartial;

            [Tint.TextMateGrammar("simple.json")]
            partial class Generic<T>;
            """);

        Assert.Equal(2, run.Tint("TINT009").Count());
    }

    [Fact]
    public void InvalidMetadataIsTint010()
    {
        var run = GeneratorHarness.Run([TestFile.Grammar("simple.json", SimpleGrammar, ("TokenTypes", "string=text"), ("Accessibility", "private"))]);

        Assert.Equal(2, run.Tint("TINT010").Count());
        Assert.All(run.Tint("TINT010"), d => Assert.Equal(DiagnosticSeverity.Warning, d.DefaultSeverity));
        Assert.Empty(run.CompilationProblems);
    }

    [Fact]
    public void NestedTargetsGetTheirContainingTypes()
    {
        var run = GeneratorHarness.Run([TestFile.Theme("simple.json", SimpleTheme)], """
            namespace N;

            public static partial class Themes
            {
                [Tint.TextMateTheme("simple.json")]
                public partial class Simple;
            }
            """);

        Assert.Empty(run.CompilationProblems);
        Assert.Equal("Simple Dark", Theme(run.Load(), "N.Themes+Simple").Name);
    }

    private static IGrammarSource Grammar(System.Reflection.Assembly assembly, string type)
        => (IGrammarSource)assembly.GetType(type, throwOnError: true)!.GetProperty("Instance")!.GetValue(null)!;

    private static IThemeSource Theme(System.Reflection.Assembly assembly, string type)
        => (IThemeSource)assembly.GetType(type, throwOnError: true)!.GetProperty("Instance")!.GetValue(null)!;

    private static string Describe(RawTheme theme)
        => string.Join("\n", theme.Rules.Select(r => $"{r.Name}|{(r.Scopes is null ? "-" : string.Join(",", r.Scopes))}|{r.FontStyle}|{r.Foreground}|{r.Background}"));
}
