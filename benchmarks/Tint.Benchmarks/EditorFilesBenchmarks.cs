using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using TextMateSharp.Grammars;

namespace Tint.Benchmarks;

/// <summary>
/// An editor's workload: the ten files of Passenger's highlighting benchmark (<c>benchmarks/samples/passenger</c>, each
/// repeated to about 10,000 lines as Passenger does), tokenized line by line through <c>tokenizeLine2</c> with the
/// grammar files Passenger ships (VS Code's, through <c>GrammarLoader</c>), once warm. Tint against TextMateSharp 2.0.4
/// reading the same grammar files; <see cref="EditorFilesColdBenchmarks"/> measures the first passes of a fresh process.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
[Config(typeof(BenchmarkConfig))]
public class EditorFilesBenchmarks : IThroughputWorkload
{
    private string[] _lines = [];
    private TintRunner _tint = null!;
    private TintRunner _tintBundled = null!;
    private IGrammar _textMateSharpGrammar = null!;

    /// <summary>The fixture file.</summary>
    [Params("xml.props", "json.json", "yaml.yml", "markdown.md", "html.html", "css.css", "javascript.js", "shell.sh", "scss.scss", "less.less")]
    public string File { get; set; } = "xml.props";

    /// <summary>Loads the grammars and tokenizes the file once with each library.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _lines = Workloads.PassengerLines(File);
        var theme = Workloads.LoadTheme("dark_plus.json");
        _tint = TintRunner.Create("passenger:" + File, theme, TokenizerOptions.Default);
        _tintBundled = TintRunner.Create("passenger:" + File, theme, TokenizerOptions.Default, TintGrammarSet.Bundled);
        _tint.Run();
        _tintBundled.Run();
        _textMateSharpGrammar = Workloads.TextMateSharpFileRegistry().LoadGrammar(Workloads.PassengerScope(File))
            ?? throw new InvalidOperationException($"TextMateSharp could not load '{Workloads.PassengerScope(File)}'.");
        TextMateSharp();
    }

    /// <summary>Tint, the grammar files through <c>GrammarLoader</c> (what Passenger does).</summary>
    [Benchmark(Baseline = true)]
    public int Tint() => _tint.Run();

    /// <summary>Tint, the bundled grammars (<c>Tint.Grammars</c>).</summary>
    [Benchmark]
    public int TintBundled() => _tintBundled.Run();

    /// <summary>TextMateSharp 2.0.4 reading the same grammar files, <c>TokenizeLine2</c>.</summary>
    [Benchmark]
    public int TextMateSharp()
    {
        var count = 0;
        IStateStack? state = null;
        foreach (var line in _lines)
        {
            var result = _textMateSharpGrammar.TokenizeLine2(line, state, TimeSpan.MaxValue);
            state = result.RuleStack;
            count += result.Tokens.Length / 2;
        }

        return count;
    }

    /// <inheritdoc/>
    public long GetWorkloadBytes() => TintRunner.Bytes("passenger:" + File);
}

/// <summary>
/// The first ten passes over a Passenger fixture in a fresh process (<see cref="ColdStartConfig"/>, one pass per
/// iteration, the grammars loaded before the first): the time an editor sees while the runtime is still compiling
/// Tint and GregsEx at tier 0, which is what Passenger's harness measured. The mean is over the ten passes of every
/// launch; the first pass also pays for loading the grammars and parsing the patterns the file reaches.
/// </summary>
[MemoryDiagnoser]
[Config(typeof(ColdStartConfig))]
public class EditorFilesColdBenchmarks : IThroughputWorkload
{
    private string[] _lines = [];
    private TintRunner _tint = null!;
    private IGrammar _textMateSharpGrammar = null!;

    /// <summary>The fixture file.</summary>
    [Params("xml.props", "json.json", "yaml.yml", "markdown.md", "html.html", "css.css", "javascript.js", "shell.sh", "scss.scss", "less.less")]
    public string File { get; set; } = "xml.props";

    /// <summary>Loads the grammars (registered and compiled, patterns still unparsed) without tokenizing.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _lines = Workloads.PassengerLines(File);
        _tint = TintRunner.Create("passenger:" + File, Workloads.LoadTheme("dark_plus.json"), TokenizerOptions.Default);
        _textMateSharpGrammar = Workloads.TextMateSharpFileRegistry().LoadGrammar(Workloads.PassengerScope(File))
            ?? throw new InvalidOperationException($"TextMateSharp could not load '{Workloads.PassengerScope(File)}'.");
    }

    /// <summary>Tint, the grammar files through <c>GrammarLoader</c>.</summary>
    [Benchmark(Baseline = true)]
    public int Tint() => _tint.Run();

    /// <summary>TextMateSharp 2.0.4 reading the same grammar files.</summary>
    [Benchmark]
    public int TextMateSharp()
    {
        var count = 0;
        IStateStack? state = null;
        foreach (var line in _lines)
        {
            var result = _textMateSharpGrammar.TokenizeLine2(line, state, TimeSpan.MaxValue);
            state = result.RuleStack;
            count += result.Tokens.Length / 2;
        }

        return count;
    }

    /// <inheritdoc/>
    public long GetWorkloadBytes() => TintRunner.Bytes("passenger:" + File);
}
