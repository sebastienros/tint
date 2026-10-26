using BenchmarkDotNet.Attributes;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;

namespace Tint.Benchmarks;

/// <summary>
/// A documentation site's workload: the 40 fenced blocks of <c>benchmarks/samples/docs/&lt;language&gt;.md</c>, each
/// tokenized from the initial state with Dark+, once warm. Tint (bundled grammars interpreted, with tier-2 hot patterns,
/// and through the loader; plus the kernel-compiled grammar for C#) against TextMateSharp 2.0.4 with its bundled
/// grammars.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
[Config(typeof(BenchmarkConfig))]
public class DocsHeavyBenchmarks : IThroughputWorkload
{
    private static readonly Lazy<RegistryOptions> s_textMateSharpOptions = new(() => new RegistryOptions(ThemeName.DarkPlus));
    private static readonly Lazy<Registry> s_textMateSharp = new(() => new Registry(s_textMateSharpOptions.Value));

    private string[][] _blocks = [];
    private TintRunner _tint = null!;
    private TintRunner? _tintKernels;
    private TintRunner _tintHot = null!;
    private TintRunner _tintLoader = null!;
    private IGrammar _textMateSharpGrammar = null!;

    /// <summary>The language of the fences.</summary>
    [Params("csharp", "typescript", "javascript", "html", "json", "yaml", "bash", "markdown")]
    public string Language { get; set; } = "csharp";

    /// <summary>Loads the grammars and tokenizes the blocks once with each library.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _blocks = Workloads.DocsBlocks(Language);
        var theme = Workloads.LoadTheme("dark_plus.json");
        _tint = TintRunner.Create("docs:" + Language, theme, TokenizerOptions.Default, TintGrammarSet.Bundled);
        _tintHot = TintRunner.Create("docs:" + Language, theme, HotPatterns.Options(HotPatterns.Threshold), TintGrammarSet.Bundled);
        _tintLoader = TintRunner.Create("docs:" + Language, theme, TokenizerOptions.Default);
        _tint.Run();
        _tintHot.Run();
        _tintLoader.Run();
        if (Language == "csharp")
        {
            _tintKernels = TintRunner.Create("docs:" + Language, theme, TokenizerOptions.Default, TintGrammarSet.Kernels);
            _tintKernels.Run();
        }

        var scope = Workloads.DocsScopes[Language];
        _textMateSharpGrammar = s_textMateSharp.Value.LoadGrammar(scope)
            ?? throw new InvalidOperationException($"TextMateSharp has no grammar for '{scope}'.");
        TextMateSharp();
    }

    /// <summary>Tint, the bundled grammars with interpreted patterns (the default).</summary>
    [Benchmark(Baseline = true)]
    public int Tint() => _tint.Run();

    /// <summary>Tint, <c>CSharpKernelsGrammar</c> (ahead-of-time kernels); C# only.</summary>
    [Benchmark]
    [BenchmarkCategory(BenchmarkConfig.CSharpOnly)]
    public int TintKernels() => _tintKernels!.Run();

    /// <summary>Tint, bundled grammars with <see cref="TokenizerOptions.CompileHotPatterns"/>.</summary>
    [Benchmark]
    public int TintHotPatterns() => _tintHot.Run();

    /// <summary>Tint, the grammar files through <c>GrammarLoader</c>.</summary>
    [Benchmark]
    public int TintLoader() => _tintLoader.Run();

    /// <summary>TextMateSharp 2.0.4, <c>TokenizeLine2</c>.</summary>
    [Benchmark]
    public int TextMateSharp()
    {
        var count = 0;
        foreach (var block in _blocks)
        {
            IStateStack? state = null;
            foreach (var line in block)
            {
                var result = _textMateSharpGrammar.TokenizeLine2(line, state, TimeSpan.MaxValue);
                state = result.RuleStack;
                count += result.Tokens.Length / 2;
            }
        }

        return count;
    }

    /// <inheritdoc/>
    public long GetWorkloadBytes() => TintRunner.Bytes("docs:" + Language);
}
