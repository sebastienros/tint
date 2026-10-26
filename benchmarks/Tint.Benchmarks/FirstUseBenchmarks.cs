using BenchmarkDotNet.Attributes;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;

namespace Tint.Benchmarks;

/// <summary>
/// Time to the first highlighted line of C#: create the registry, load the grammar and Dark+, tokenize the first line
/// of <c>sample.cs</c>. Every operation starts from nothing loaded (the process and the JIT are warm).
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
[Config(typeof(BenchmarkConfig))]
public class FirstUseBenchmarks
{
    private string _line = string.Empty;
    private string _themeJson = string.Empty;

    /// <summary>Reads the line, and flattens the theme for TextMateLib.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _line = Workloads.ReadLines("sample.cs").First(l => l.Length > 0);
        _themeJson = Workloads.FlattenedThemeJson("dark_plus.json");
    }

    /// <summary>Tint through the loader: JSON grammar and theme, patterns parsed as the line reaches them.</summary>
    [Benchmark(Baseline = true)]
    public int Tint() => FirstLine(TokenizerOptions.Default);

    /// <summary>Tint with every reached pattern runtime-compiled up front (threshold 0).</summary>
    [Benchmark]
    public int TintCompileAll() => FirstLine(HotPatterns.Options(0));

    /// <summary>TextMateSharp 2.0.4: <c>RegistryOptions</c>, <c>Registry</c>, <c>LoadGrammar</c>, first line.</summary>
    [Benchmark]
    public int TextMateSharp()
    {
        var registry = new Registry(new RegistryOptions(ThemeName.DarkPlus));
        var grammar = registry.LoadGrammar("source.cs");
        return grammar.TokenizeLine2(_line, null, TimeSpan.MaxValue).Tokens.Length;
    }

    /// <summary>TextMateLib 0.2.1: registry, theme, grammar file, first line.</summary>
    [Benchmark]
    [BenchmarkCategory(BenchmarkConfig.TextMateLib)]
    public int TextMateLib()
    {
        using var registry = new global::TextMateLib.Bindings.Registry();
        registry.SetThemeFromJson(_themeJson);
        registry.AddGrammarFromFile(Workloads.CSharpGrammarPath);
        using var grammar = registry.LoadGrammar("source.cs");
        return grammar.TokenizeLine2(_line, IntPtr.Zero).TokenCount;
    }

    private int FirstLine(TokenizerOptions options)
    {
        var registry = Workloads.CSharpRegistry();
        registry.TokenizerOptions = options;
        var tokenizer = registry.CreateTokenizer("source.cs", Workloads.LoadTheme("dark_plus.json"));
        return tokenizer.TokenizeLineEncoded(_line, StateStack.Initial, out _).Length;
    }
}
