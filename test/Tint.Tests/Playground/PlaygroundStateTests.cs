using System.Text;
using Tint.Playground;
using Xunit;

namespace Tint.Tests.Playground;

public sealed class PlaygroundStateTests
{
    [Fact]
    public void A_state_round_trips_through_the_hash()
    {
        var state = new PlaygroundState("csharp", "dark-plus", "light-plus", "var x = 1; // é ✓ 𝄞\r\nConsole.WriteLine(x);\n");

        var (language, theme, secondTheme, code) = PlaygroundState.Parse("#" + state.ToHash());

        Assert.Equal("csharp", language);
        Assert.Equal("dark-plus", theme);
        Assert.Equal("light-plus", secondTheme);
        Assert.Equal(state.Code, code);
    }

    [Fact]
    public void The_second_theme_is_left_out_when_there_is_none()
    {
        var hash = new PlaygroundState("json", "monokai", null, "{}").ToHash();

        Assert.DoesNotContain("theme2", hash);
        Assert.Null(PlaygroundState.Parse(hash).SecondTheme);
    }

    [Fact]
    public void Short_code_is_plain_base64url()
    {
        var encoded = PlaygroundState.EncodeCode("let x = 1;");

        Assert.Equal("bGV0IHggPSAxOw", encoded);
        Assert.Equal("let x = 1;", PlaygroundState.DecodeCode(encoded));
    }

    [Fact]
    public void Repetitive_code_is_deflated()
    {
        var code = string.Concat(Enumerable.Repeat("Console.WriteLine(\"Hello, World!\");\n", 50));

        var encoded = PlaygroundState.EncodeCode(code);

        Assert.StartsWith("~", encoded);
        Assert.True(encoded.Length < Convert.ToBase64String(Encoding.UTF8.GetBytes(code)).Length / 10);
        Assert.Equal(code, PlaygroundState.DecodeCode(encoded));
    }

    [Fact]
    public void The_hash_is_safe_in_a_url()
    {
        var hash = new PlaygroundState("c++", "solarized (dark)", null, string.Concat(Enumerable.Repeat("a+b/c=d?", 40))).ToHash();

        Assert.All(hash, c => Assert.True(char.IsAsciiLetterOrDigit(c) || "-_.~=&%".Contains(c), $"'{c}' in {hash}"));
        Assert.Equal("c++", PlaygroundState.Parse(hash).Language);
        Assert.Equal("solarized (dark)", PlaygroundState.Parse(hash).Theme);
    }

    [Theory]
    [InlineData("not base64!")]
    [InlineData("~____")]
    [InlineData("_w")]
    public void Unreadable_code_decodes_to_null(string value) => Assert.Null(PlaygroundState.DecodeCode(value));

    [Fact]
    public void Inflating_stops_at_a_megabyte()
    {
        var code = new string('a', PlaygroundState.MaxCodeBytes + 1);

        Assert.Null(PlaygroundState.DecodeCode(PlaygroundState.EncodeCode(code)));
    }

    [Fact]
    public void Missing_and_unknown_keys_are_ignored()
    {
        var (language, theme, secondTheme, code) = PlaygroundState.Parse("#foo=bar&lang=python&&=x&theme");

        Assert.Equal("python", language);
        Assert.Null(theme);
        Assert.Null(secondTheme);
        Assert.Null(code);
        Assert.Equal((null, null, null, null), PlaygroundState.Parse(""));
    }
}
