# Tint benchmarks

BenchmarkDotNet harness. `[MemoryDiagnoser]` is the allocation authority: the per-line tokenizer
path must report zero allocated bytes once warm.

```bash
dotnet run -c Release --project benchmarks/Tint.Benchmarks
dotnet run -c Release --project benchmarks/Tint.Benchmarks -- --filter '*TokenizeLine*'
```

`BenchmarkSwitcher` lists the benchmark classes when no filter is given. Results land in
`BenchmarkDotNet.Artifacts/` (ignored by git); paste the summary table into the pull request of
any change that touches a hot path.

`TokenizeLineBenchmarks` tokenizes the files in `samples/` line by line with the VS Code C# grammar and
Dark+, through the `Token` and `EncodedToken` paths. Comparison benchmarks (TextMateSharp,
TextMateLib) arrive with phase P5.
