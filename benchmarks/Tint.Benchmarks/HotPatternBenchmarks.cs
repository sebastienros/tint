using BenchmarkDotNet.Attributes;

namespace Tint.Benchmarks;

/// <summary>
/// Tier 2 (docs/design.md section 5.5) once warm: every rule reached was compiled during setup, so this measures the
/// steady-state speed of the patterns each threshold ends up compiling. Threshold -1 is the interpreter.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
[Config(typeof(BenchmarkConfig))]
public class HotPatternBenchmarks : IThroughputWorkload
{
    private TintRunner _runner = null!;

    /// <summary>The workload.</summary>
    [Params("bigfile.cs", "medium.cs", "docs")]
    public string Workload { get; set; } = "medium.cs";

    /// <summary>Searches of a rule before its patterns are compiled; -1 keeps the interpreter.</summary>
    [Params(-1, 0, 16, 256)]
    public int Threshold { get; set; }

    /// <summary>Loads the grammars and runs the workload twice so the scanners reach their final tier.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _runner = TintRunner.Create(Workload, Workloads.LoadTheme("dark_plus.json"), HotPatterns.Options(Threshold));
        _runner.Run();
        _runner.Run();
        Console.WriteLine($"// {Workload}, threshold {Threshold}: {_runner.CompiledPatternCount} compiled patterns in the scanners");
    }

    /// <summary>Tokenizes the workload once warm.</summary>
    [Benchmark]
    public int Warm() => _runner.Run();

    /// <inheritdoc/>
    public long GetWorkloadBytes() => TintRunner.Bytes(Workload);
}

/// <summary>
/// Tier 2 from a cold registry: each operation loads the grammars (outside the measurement) and tokenizes the workload
/// once, so it pays for parsing and compiling the patterns it reaches.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 1, iterationCount: 5, invocationCount: 1)]
[Config(typeof(BenchmarkConfig))]
public class HotPatternColdBenchmarks : IThroughputWorkload
{
    private TintRunner _runner = null!;
    private Theme _theme = null!;

    /// <summary>The workload.</summary>
    [Params("bigfile.cs", "medium.cs", "docs")]
    public string Workload { get; set; } = "medium.cs";

    /// <summary>Searches of a rule before its patterns are compiled; -1 keeps the interpreter.</summary>
    [Params(-1, 0, 16, 256)]
    public int Threshold { get; set; }

    /// <summary>Loads the theme and the workload files.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _theme = Workloads.LoadTheme("dark_plus.json");
        TintRunner.Bytes(Workload);
    }

    /// <summary>Loads fresh grammars, so no pattern is parsed or compiled yet.</summary>
    [IterationSetup]
    public void LoadGrammars() => _runner = TintRunner.Create(Workload, _theme, HotPatterns.Options(Threshold));

    /// <summary>Tokenizes the workload from a cold registry.</summary>
    [Benchmark]
    public int Cold() => _runner.Run();

    /// <inheritdoc/>
    public long GetWorkloadBytes() => TintRunner.Bytes(Workload);
}

/// <summary>The tier-2 settings the comparison benchmarks use.</summary>
public static class HotPatterns
{
    /// <summary>The threshold of the <c>TintHotPatterns</c> benchmarks.</summary>
    public const int Threshold = 256;

    /// <summary>Options for a threshold; -1 for the interpreter.</summary>
    public static TokenizerOptions Options(int threshold)
        => threshold < 0 ? TokenizerOptions.Default : new TokenizerOptions { CompileHotPatterns = true, HotPatternThreshold = threshold };
}
