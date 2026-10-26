using BenchmarkDotNet.Attributes;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;

namespace Tint.Benchmarks;

/// <summary>
/// TextMateSharp's <c>bigfile.cs</c> (133k lines) tokenized line by line with C# and Dark+, once warm, through each
/// library's binary-token path (<c>tokenizeLine2</c>): Tint with the interpreter and with tier-2 compiled hot patterns,
/// TextMateSharp 2.0.4 (Onigwrap) and TextMateLib 0.2.1 (native Oniguruma) when its native library loads.
/// </summary>
/// <remarks>
/// Tint and TextMateLib read the VS Code C# grammar in <c>grammars/</c>; TextMateSharp uses the copy bundled in
/// TextMateSharp.Grammars, which is older.
/// </remarks>
[MemoryDiagnoser]
[ShortRunJob]
[Config(typeof(BenchmarkConfig))]
public class BigFileBenchmarks : IThroughputWorkload
{
    private string[] _lines = [];
    private TintRunner _tint = null!;
    private TintRunner _tintHot = null!;
    private IGrammar _textMateSharp = null!;
    private global::TextMateLib.Bindings.Registry? _textMateLibRegistry;
    private global::TextMateLib.Bindings.Grammar? _textMateLib;

    /// <summary>Downloads the file when needed, loads every library and tokenizes the file once with each.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _lines = Workloads.ReadLines("bigfile.cs");
        var theme = Workloads.LoadTheme("dark_plus.json");
        _tint = TintRunner.Create("bigfile.cs", theme, TokenizerOptions.Default);
        _tintHot = TintRunner.Create("bigfile.cs", theme, HotPatterns.Options(HotPatterns.Threshold));
        _tint.Run();
        _tintHot.Run();

        var options = new RegistryOptions(ThemeName.DarkPlus);
        _textMateSharp = new Registry(options).LoadGrammar("source.cs");
        TextMateSharp();

        if (TextMateLibAvailability.IsAvailable)
        {
            _textMateLibRegistry = new global::TextMateLib.Bindings.Registry();
            _textMateLibRegistry.SetThemeFromJson(Workloads.FlattenedThemeJson("dark_plus.json"));
            _textMateLibRegistry.AddGrammarFromFile(Workloads.CSharpGrammarPath);
            _textMateLib = _textMateLibRegistry.LoadGrammar("source.cs");
            TextMateLib();
        }
    }

    /// <summary>Releases TextMateLib's native objects.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _textMateLib?.Dispose();
        _textMateLibRegistry?.Dispose();
    }

    /// <summary>Tint, interpreted patterns (the default).</summary>
    [Benchmark(Baseline = true)]
    public int Tint() => _tint.Run();

    /// <summary>Tint with <see cref="TokenizerOptions.CompileHotPatterns"/>.</summary>
    [Benchmark]
    public int TintHotPatterns() => _tintHot.Run();

    /// <summary>TextMateSharp 2.0.4, <c>TokenizeLine2</c>.</summary>
    [Benchmark]
    public int TextMateSharp()
    {
        var count = 0;
        IStateStack? state = null;
        foreach (var line in _lines)
        {
            var result = _textMateSharp.TokenizeLine2(line, state, TimeSpan.MaxValue);
            state = result.RuleStack;
            count += result.Tokens.Length / 2;
        }

        return count;
    }

    /// <summary>TextMateLib 0.2.1, <c>TokenizeLine2</c>.</summary>
    [Benchmark]
    [BenchmarkCategory(BenchmarkConfig.TextMateLib)]
    public int TextMateLib()
    {
        var count = 0;
        var state = IntPtr.Zero;
        foreach (var line in _lines)
        {
            var result = _textMateLib!.TokenizeLine2(line, state);
            state = result.StateStack;
            count += result.TokenCount;
        }

        return count;
    }

    /// <inheritdoc/>
    public long GetWorkloadBytes() => TintRunner.Bytes("bigfile.cs");
}
