using System.Text.Json;
using Xunit;

namespace Tint.Tests;

public sealed class FixtureTests
{
    [Fact]
    public void FirstMateSuiteLoadsWithAllSixtyFourCases()
    {
        var path = Path.Combine(Fixtures.VSCodeTextMate, "first-mate", "tests.json");

        using var document = JsonDocument.Parse(File.ReadAllBytes(path));

        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.Equal(64, document.RootElement.GetArrayLength());
    }
}
