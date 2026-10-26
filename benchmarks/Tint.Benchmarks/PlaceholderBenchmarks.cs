using BenchmarkDotNet.Attributes;

namespace Tint.Benchmarks;

/// <summary>
/// Keeps the benchmark harness wired up until the tokenizer benchmarks land in phase P1.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class PlaceholderBenchmarks
{
    [Benchmark]
    public string Commit() => TintInfo.VSCodeCommit;
}
