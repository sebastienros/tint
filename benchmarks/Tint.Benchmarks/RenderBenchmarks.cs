using BenchmarkDotNet.Attributes;

namespace Tint.Benchmarks;

/// <summary>
/// <c>medium.cs</c> rendered by the <see cref="Highlighter"/>, once warm: tokens only, HTML with one theme and with
/// light and dark themes, terminal output, and HTML streamed to a writer.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
[Config(typeof(BenchmarkConfig))]
public class RenderBenchmarks : IThroughputWorkload
{
    private string _code = string.Empty;
    private TintRunner _tokens = null!;
    private Highlighter _single = null!;
    private Highlighter _dual = null!;

    /// <summary>Creates the highlighters and renders once with each.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _code = File.ReadAllText(Workloads.Sample("medium.cs"));
        var dark = Workloads.LoadTheme("dark_plus.json");
        var light = Workloads.LoadTheme("light_plus.json");
        _tokens = TintRunner.Create("medium.cs", dark, TokenizerOptions.Default);
        var registry = Workloads.CSharpRegistry();
        _single = registry.CreateHighlighter("source.cs", dark);
        _dual = registry.CreateHighlighter("source.cs", light, dark);
        _tokens.Run();
        HtmlSingleTheme();
        HtmlDualTheme();
        Ansi();
    }

    /// <summary>Tokenization alone, the encoded path the renderers use.</summary>
    [Benchmark(Baseline = true)]
    public int Tokens() => _tokens.Run();

    /// <summary><see cref="Highlighter.ToHtml(string)"/> with Dark+.</summary>
    [Benchmark]
    public string HtmlSingleTheme() => _single.ToHtml(_code);

    /// <summary><see cref="Highlighter.ToHtml(string)"/> with Light+ inline and Dark+ as CSS variables (two tokenizations).</summary>
    [Benchmark]
    public string HtmlDualTheme() => _dual.ToHtml(_code);

    /// <summary><see cref="Highlighter.ToHtml(ReadOnlySpan{char}, TextWriter)"/> to <see cref="TextWriter.Null"/>.</summary>
    [Benchmark]
    public void HtmlToWriter() => _single.ToHtml(_code, TextWriter.Null);

    /// <summary><see cref="Highlighter.ToAnsi(string)"/> with Dark+.</summary>
    [Benchmark]
    public string Ansi() => _single.ToAnsi(_code);

    /// <inheritdoc/>
    public long GetWorkloadBytes() => TintRunner.Bytes("medium.cs");
}
