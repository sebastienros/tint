using Xunit;

namespace Tint.Tests.Generator;

public sealed class SourcesSampleTests
{
    [Fact]
    public void TheSourcesSampleTokenizesLikeTheBundles()
    {
        var expected = Tint.Sources.Sample.Program.Render(Tint.Grammars.CSharpGrammar.Instance, Tint.Grammars.JsonGrammar.Instance, Tint.Themes.DarkPlusTheme.Instance);
        Assert.Equal(expected, Tint.Sources.Sample.Program.RenderGenerated());
        Assert.Contains("'Greeter' #4EC9B0 source.cs entity.name.type.class.cs", expected);
    }
}
