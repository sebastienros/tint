using System.Reflection;
using Microsoft.CodeAnalysis;
using Tint.Grammars;
using Tint.Themes;
using Xunit;

namespace Tint.Tests.Generator;

public sealed class AggregateTests
{
    private static readonly Dictionary<string, string> s_grammarsClass = new() { ["TintGrammarsClass"] = "TintGrammars" };
    private static readonly Dictionary<string, string> s_themesClass = new() { ["TintThemesClass"] = "TintThemes" };

    private static TestFile[] Grammars(string accessibility = "public") =>
    [
        TestFile.Grammar("b.json", """{ "scopeName": "source.zed", "patterns": [] }""", ("Accessibility", accessibility)),
        TestFile.Grammar("a.json", """{ "scopeName": "source.cs", "patterns": [] }""", ("Accessibility", accessibility)),
        TestFile.Grammar("c.json", """{ "scopeName": "text.html.basic", "patterns": [] }""", ("Accessibility", accessibility)),
    ];

    [Fact]
    public void NoAggregateClassIsGeneratedWithoutTheProperty()
    {
        var run = GeneratorHarness.Run(Grammars());

        Assert.DoesNotContain(run.Sources.Keys, k => k.Contains("TintGrammars", StringComparison.Ordinal));
    }

    [Fact]
    public void TheGrammarsClassListsEveryGrammarByScopeNameWithOnePropertyEach()
    {
        var run = GeneratorHarness.Run(Grammars(), properties: s_grammarsClass);

        Assert.Empty(run.CompilationProblems);
        Assert.Contains("public static partial class TintGrammars", run.Source("Test.Root.TintGrammars"));
        var type = run.Load().GetType("Test.Root.TintGrammars", throwOnError: true)!;

        var all = (IReadOnlyList<IGrammarSource>)type.GetProperty("All")!.GetValue(null)!;
        Assert.Equal(["source.cs", "source.zed", "text.html.basic"], all.Select(g => g.ScopeName));
        Assert.Throws<NotSupportedException>(() => ((IList<IGrammarSource>)all).Add(all[0]));

        var csharp = type.GetProperty("CSharp")!;
        Assert.Equal("CSharpGrammar", csharp.PropertyType.Name);
        Assert.Same(all[0], csharp.GetValue(null));
        Assert.Same(all[1], type.GetProperty("Zed")!.GetValue(null));
        Assert.Same(all[2], type.GetProperty("HtmlBasic")!.GetValue(null));

        var find = type.GetMethod("Find")!;
        Assert.Same(all[2], find.Invoke(null, ["text.html.basic"]));
        Assert.Null(find.Invoke(null, ["TEXT.HTML.BASIC"]));
        Assert.Null(find.Invoke(null, ["source.missing"]));
        var thrown = Assert.Throws<TargetInvocationException>(() => find.Invoke(null, [null]));
        Assert.IsType<ArgumentNullException>(thrown.InnerException);
    }

    [Fact]
    public void TheGrammarsClassIsInternalUnlessEveryGrammarIsPublic()
    {
        var run = GeneratorHarness.Run(Grammars(accessibility: "internal"), properties: s_grammarsClass);

        Assert.Empty(run.CompilationProblems);
        Assert.Contains("internal static partial class TintGrammars", run.Source("Test.Root.TintGrammars"));
    }

    [Fact]
    public void AttributeBoundClassesAreListedWithTheirOwnAccessibility()
    {
        var run = GeneratorHarness.Run(
            [TestFile.Grammar("simple.json", """{ "scopeName": "source.simple", "patterns": [] }""")],
            """
            namespace My.Grammars;

            public static partial class Outer
            {
                [Tint.TextMateGrammar("simple.json")]
                public partial class Simple;
            }
            """,
            s_grammarsClass);

        Assert.Empty(run.CompilationProblems);
        var source = run.Source("Test.Root.TintGrammars");
        Assert.Contains("public static partial class TintGrammars", source);
        Assert.Contains("public static global::My.Grammars.Outer.Simple Simple => global::My.Grammars.Outer.Simple.Instance;", source);
    }

    [Fact]
    public void TheClassNameMayCarryANamespace()
    {
        var run = GeneratorHarness.Run(Grammars(), properties: new Dictionary<string, string> { ["TintGrammarsClass"] = "My.Bundles.Languages" });

        Assert.Empty(run.CompilationProblems);
        Assert.Contains("namespace My.Bundles", run.Source("My.Bundles.Languages"));
        Assert.NotNull(run.Load().GetType("My.Bundles.Languages"));
    }

    [Fact]
    public void PropertyNamesThatClashKeepTheClassName()
    {
        var run = GeneratorHarness.Run(
        [
            TestFile.Grammar("all.json", """{ "scopeName": "source.all", "patterns": [] }""", ("Accessibility", "public")),
            TestFile.Grammar("find.json", """{ "scopeName": "source.find", "patterns": [] }""", ("Accessibility", "public")),
            TestFile.Grammar("one.json", """{ "scopeName": "source.one", "patterns": [] }""", ("Accessibility", "public"), ("Name", "Grammar")),
            TestFile.Grammar("two.json", """{ "scopeName": "source.two", "patterns": [] }""", ("Accessibility", "public"), ("Name", "Grammar"), ("Namespace", "Other")),
        ],
        properties: s_grammarsClass);

        Assert.Empty(run.CompilationProblems);
        var type = run.Load().GetType("Test.Root.TintGrammars", throwOnError: true)!;
        Assert.Equal(
            ["AllGrammar", "FindGrammar", "Grammar", "Grammar2"],
            type.GetProperties(BindingFlags.Public | BindingFlags.Static).Select(p => p.Name).Where(n => n != "All").Order(StringComparer.Ordinal));
    }

    [Fact]
    public void GrammarsThatFailToGenerateAreLeftOut()
    {
        var run = GeneratorHarness.Run(
        [
            TestFile.Grammar("good.json", """{ "scopeName": "source.good", "patterns": [] }""", ("Accessibility", "public")),
            TestFile.Grammar("broken.json", """{ "scopeName": "source.broken", "patterns": [ }""", ("Accessibility", "public")),
        ],
        properties: s_grammarsClass);

        Assert.Single(run.Tint("TINT003"));
        var source = run.Source("Test.Root.TintGrammars");
        Assert.Contains("GoodGrammar", source);
        Assert.DoesNotContain("BrokenGrammar", source);
    }

    [Fact]
    public void TheThemesClassFindsThemesByNameOrPropertyIgnoringCase()
    {
        var run = GeneratorHarness.Run(
        [
            TestFile.Theme("dark_plus.json", """{ "name": "Dark+", "tokenColors": [] }""", ("Accessibility", "public"), ("Name", "DarkPlusTheme")),
            TestFile.Theme("monokai.json", """{ "tokenColors": [] }""", ("Accessibility", "public")),
            TestFile.Theme("abyss.json", """{ "name": "Abyss", "tokenColors": [] }""", ("Accessibility", "public")),
        ],
        properties: s_themesClass);

        Assert.Empty(run.CompilationProblems);
        var type = run.Load().GetType("Test.Root.TintThemes", throwOnError: true)!;
        var all = (IReadOnlyList<IThemeSource>)type.GetProperty("All")!.GetValue(null)!;
        Assert.Equal(["Abyss", "Dark+", null], all.Select(t => t.Name));

        var find = type.GetMethod("Find")!;
        Assert.Same(all[1], find.Invoke(null, ["dark+"]));
        Assert.Same(all[1], find.Invoke(null, ["DarkPlus"]));
        Assert.Same(all[2], find.Invoke(null, ["monokai"]));
        Assert.Same(all[0], type.GetProperty("Abyss")!.GetValue(null));
        Assert.Null(find.Invoke(null, ["Dark"]));
    }

    [Fact]
    public void AnEmptyCompilationStillGetsTheClass()
    {
        var run = GeneratorHarness.Run([], properties: s_grammarsClass);

        Assert.Empty(run.CompilationProblems);
        var type = run.Load().GetType("Test.Root.TintGrammars", throwOnError: true)!;
        Assert.Empty((IReadOnlyList<IGrammarSource>)type.GetProperty("All")!.GetValue(null)!);
        Assert.DoesNotContain(run.GeneratorDiagnostics, d => d.Severity > DiagnosticSeverity.Info);
    }
}
