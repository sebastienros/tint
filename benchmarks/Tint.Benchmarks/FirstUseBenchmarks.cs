using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;

namespace Tint.Benchmarks;

/// <summary>
/// Time to the first highlighted line of C#: create the registry, load the grammar and Dark+, tokenize the first line
/// of <c>sample.cs</c>. Every operation starts from nothing loaded (the process and the JIT are warm), except the
/// kernel-compiled grammar: its kernels are JIT-compiled once per process, so only BenchmarkDotNet's first (warmup)
/// invocation pays for them; <see cref="FirstUseColdBenchmarks"/> measures that.
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

    /// <summary>Tint, <c>TintGrammars.CSharp</c> (generated, interpreted patterns).</summary>
    [Benchmark]
    public int TintBundled() => FirstLine(TokenizerOptions.Default, TintGrammarSet.Bundled);

    /// <summary>Tint, <c>CSharpKernelsGrammar</c> (ahead-of-time kernels, already JIT-compiled after the first call).</summary>
    [Benchmark]
    public int TintKernels() => FirstLine(TokenizerOptions.Default, TintGrammarSet.Kernels);

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

    private int FirstLine(TokenizerOptions options, TintGrammarSet grammars = TintGrammarSet.Loader)
    {
        var registry = Workloads.CSharpRegistry(grammars);
        registry.TokenizerOptions = options;
        var tokenizer = registry.CreateTokenizer("source.cs", Workloads.LoadTheme("dark_plus.json"));
        return tokenizer.TokenizeLineEncoded(_line, StateStack.Initial, out _).Length;
    }
}

/// <summary>
/// <see cref="FirstUseBenchmarks"/> in a fresh process per measurement (<see cref="RunStrategy.ColdStart"/>, one
/// invocation per launch): the first highlighted line including the JIT of Tint's code and of the kernels the line
/// reaches.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.ColdStart, launchCount: 10, warmupCount: 0, iterationCount: 1, invocationCount: 1)]
[Config(typeof(BenchmarkConfig))]
public class FirstUseColdBenchmarks
{
    private string _line = string.Empty;

    /// <summary>Reads the line.</summary>
    [GlobalSetup]
    public void Setup() => _line = Workloads.ReadLines("sample.cs").First(l => l.Length > 0);

    /// <summary>Tint through the loader.</summary>
    [Benchmark(Baseline = true)]
    public int Tint() => FirstLine(TintGrammarSet.Loader);

    /// <summary>Tint, <c>TintGrammars.CSharp</c>.</summary>
    [Benchmark]
    public int TintBundled() => FirstLine(TintGrammarSet.Bundled);

    /// <summary>Tint, <c>CSharpKernelsGrammar</c>.</summary>
    [Benchmark]
    public int TintKernels() => FirstLine(TintGrammarSet.Kernels);

    private int FirstLine(TintGrammarSet grammars)
    {
        var tokenizer = Workloads.CSharpRegistry(grammars).CreateTokenizer("source.cs", Workloads.LoadTheme("dark_plus.json"));
        return tokenizer.TokenizeLineEncoded(_line, StateStack.Initial, out _).Length;
    }
}
