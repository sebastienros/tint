using Tint.Grammars;
using Tint.Loading;
using Xunit;

namespace Tint.Tests.Generator;

public sealed class SampleTests
{
    private static string Repository => GeneratorHarness.RepositoryRoot;

    [Fact]
    public void TheSampleTokenizesLikeTheLoaderPath()
    {
        var csharp = GrammarLoader.Load(Path.Combine(Repository, "grammars", "csharp", "syntaxes", "csharp.tmLanguage.json"));
        var json = GrammarLoader.Load(Path.Combine(Repository, "grammars", "json", "syntaxes", "JSON.tmLanguage.json"));
        var theme = ThemeLoader.Load(Path.Combine(Repository, "themes", "theme-defaults", "dark_plus.json"));

        var expected = Tint.Generator.Sample.Program.Render(csharp, json, theme);
        Assert.Equal(expected, Tint.Generator.Sample.Program.RenderGenerated());
        Assert.Contains("'Greeter' #4EC9B0 source.cs entity.name.type.class.cs", expected);
    }

    [Fact]
    public void TheSampleBindsKernelsForCSharp()
    {
        var grammar = Tint.Generator.Sample.CSharpGrammar.Instance.GetGrammar();
        var compiled = grammar.Repository.Values
            .Select(r => r.Match)
            .OfType<RegexSource>()
            .Select(m => m.PatternFactory!(RegexAnchorVariant.A0G0))
            .Count(p => p.ExecutionBackend == GregsEx.RegexExecutionBackend.Compiled);
        Assert.True(compiled > 0);
    }
}
