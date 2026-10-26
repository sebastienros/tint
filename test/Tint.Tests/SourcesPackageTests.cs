using System.Xml.Linq;
using Tint.Tests.Generator;
using Xunit;

namespace Tint.Tests;

// The props of Tint.Grammars.Sources and Tint.Themes.Sources come from the emitter that writes the bundles' item lists;
// their items are the bundle items, path for path and metadata for metadata (but Accessibility, left to the consumer).
public sealed class SourcesPackageTests
{
    [Theory]
    [InlineData("src/Tint.Grammars/VSCodeGrammars.props", "TintGrammar", "$(MSBuildThisFileDirectory)../../grammars/", "src/Tint.Grammars.Sources/build/Tint.Grammars.Sources.props", "TintGrammarsSourcesAll", "$(TintGrammarsSourcesPath)")]
    [InlineData("src/Tint.Themes/VSCodeThemes.props", "TintTheme", "$(MSBuildThisFileDirectory)../../themes/", "src/Tint.Themes.Sources/build/Tint.Themes.Sources.props", "TintThemesSourcesAll", "$(TintThemesSourcesPath)")]
    public void TheSourcesItemsAreTheBundleItems(string bundle, string bundleItem, string bundlePrefix, string sources, string sourcesItem, string sourcesPrefix)
    {
        var expected = Items(bundle, bundleItem, bundlePrefix);
        var actual = Items(sources, sourcesItem, sourcesPrefix);

        Assert.NotEmpty(expected);
        Assert.Equal(expected, actual);
        Assert.All(actual, item => Assert.True(File.Exists(Path.Combine(GeneratorHarness.RepositoryRoot, bundlePrefix.Split('/')[^2], item.Split('|')[0]))));
    }

    [Theory]
    [InlineData("src/Tint.Grammars.Sources/build/Tint.Grammars.Sources.props", "TintGrammarsSourcesPath", "grammars")]
    [InlineData("src/Tint.Themes.Sources/build/Tint.Themes.Sources.props", "TintThemesSourcesPath", "themes")]
    public void ThePathPropertyIsTheContentFolderAndCompilerVisible(string props, string property, string folder)
    {
        var document = XDocument.Load(Path.Combine(GeneratorHarness.RepositoryRoot, props));
        Assert.Equal(
            $"$([MSBuild]::NormalizeDirectory('$(MSBuildThisFileDirectory)', '..', 'content', '{folder}'))",
            document.Descendants(property).Single().Value);
        Assert.Contains(document.Descendants("CompilerVisibleProperty"), e => (string?)e.Attribute("Include") == property);
    }

    // "path|Name=...|LanguageId=..." per item, sorted.
    private static List<string> Items(string props, string itemName, string prefix)
        => [.. XDocument.Load(Path.Combine(GeneratorHarness.RepositoryRoot, props)).Descendants(itemName)
            .Select(e =>
            {
                var include = (string)e.Attribute("Include")!;
                Assert.StartsWith(prefix, include, StringComparison.Ordinal);
                return string.Join('|', e.Attributes()
                    .Where(a => a.Name.LocalName is not ("Include" or "Accessibility"))
                    .Select(a => a.Name.LocalName + "=" + a.Value)
                    .Prepend(include.Substring(prefix.Length)));
            })
            .Order(StringComparer.Ordinal)];
}
