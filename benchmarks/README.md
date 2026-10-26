# Tint benchmarks

BenchmarkDotNet harness. `[MemoryDiagnoser]` is the allocation authority: the per-line tokenizer
path must report zero allocated bytes once warm (beyond the per-line string the scanner cache needs,
design section 5.6, and the `StateStack` nodes of `begin` rules).

```bash
dotnet run -c Release --project benchmarks/Tint.Benchmarks                      # pick from a list
dotnet run -c Release --project benchmarks/Tint.Benchmarks -- --filter '*BigFile*'
```

Results land in `BenchmarkDotNet.Artifacts/` (ignored by git); pass `--artifacts <dir>` to put them
elsewhere. Paste the summary table into the pull request of any change that touches a hot path. CI
does not run benchmarks.

| Class | Filter | What it measures |
|---|---|---|
| `TokenizeLineBenchmarks` | `'*TokenizeLine*'` | `sample.cs` and `medium.cs` line by line, C# + Dark+, `Token` and `EncodedToken` paths. The hot-path regression check. |
| `BigFileBenchmarks` | `'BigFileBenchmarks*'` | TextMateSharp's `bigfile.cs` (133,439 lines, 5.8 MB), Tint vs TextMateSharp 2.0.4 vs TextMateLib 0.2.1, once warm, `tokenizeLine2` paths. The file is downloaded into `samples/.cache/` (git-ignored) on first run. |
| `DocsHeavyBenchmarks` | `'*DocsHeavy*'` | 40 fenced blocks per language (`samples/docs/*.md`: csharp, typescript, javascript, html, json, yaml, bash, markdown), each from the initial state, Tint vs TextMateSharp. |
| `RenderBenchmarks` | `'*Render*'` | `Highlighter.ToHtml` (one theme, light + dark, to a `TextWriter`) and `ToAnsi` on `medium.cs`, against tokenization alone. |
| `FirstUseBenchmarks` | `'*FirstUse*'` | Registry creation, C# grammar and Dark+ loading, first line tokenized: Tint (loader path), TextMateSharp, TextMateLib. |
| `HotPatternBenchmarks`, `HotPatternColdBenchmarks` | `'*HotPattern*'` | Tier-2 runtime compilation (`TokenizerOptions.CompileHotPatterns`) for thresholds off/0/16/256, once warm and from a cold registry, on `bigfile.cs`, `medium.cs` and the whole docs mix. |

MB/s is 10^6 bytes of UTF-8 source per second. TextMateLib benchmarks are skipped with a message
when its native library does not load (it ships win-x64, linux-x64, osx-x64 and osx-arm64 only).
Tint and TextMateLib read the VS Code grammars in `grammars/`; TextMateSharp uses the copies bundled
in TextMateSharp.Grammars, which are older, so token counts differ slightly.

## Results

Apple M4 Pro (14 cores), macOS 15.8.1, .NET 10.0.11 (SDK 10.0.400), Arm64 RyuJIT, BenchmarkDotNet
0.15.8 `ShortRun`, GregsEx 0.5.1, 2026-10-25. Tint numbers are the default interpreter tier.

### `bigfile.cs` (133,439 lines)

| Library | Mean | MB/s | Allocated | vs Tint |
|---|---:|---:|---:|---:|
| **Tint** | **1.73 s** | **3.35** | **32.6 MB** | 1.00 |
| Tint, tier 2 (N = 256) | 1.70 s | 3.41 | 32.6 MB | 0.98 |
| TextMateLib 0.2.1 (native Oniguruma) | 2.80 s | 2.07 | 31.0 MB | 1.62 |
| TextMateSharp 2.0.4 (Onigwrap) | 7.65 s | 0.76 | 507.3 MB | 4.42 |

The TextMateSharp + GregsEx branch measured 1.74 s / 69.7 MB on the same machine class.

### Docs-heavy (40 fences per language)

| Language | Tint | MB/s | Allocated | TextMateSharp | Allocated | Speedup |
|---|---:|---:|---:|---:|---:|---:|
| bash | 0.64 ms | 5.22 | 73.7 KB | 1.81 ms | 333.2 KB | 2.8× |
| csharp | 3.47 ms | 2.14 | 89.5 KB | 14.59 ms | 1,586.8 KB | 4.2× |
| html | 1.34 ms | 4.60 | 78.4 KB | 11.71 ms | 2,888.9 KB | 8.7× |
| javascript | 3.29 ms | 1.90 | 84.8 KB | 26.16 ms | 4,386.6 KB | 7.9× |
| json | 0.19 ms | 26.36 | 63.3 KB | 1.28 ms | 947.6 KB | 6.7× |
| markdown | 0.63 ms | 6.35 | 45.2 KB | 3.54 ms | 519.3 KB | 5.6× |
| typescript | 4.49 ms | 1.60 | 95.5 KB | 26.20 ms | 3,955.0 KB | 5.8× |
| yaml | 0.68 ms | 6.99 | 105.3 KB | 5.17 ms | 1,069.4 KB | 7.7× |

### First use (registry + C# grammar + Dark+ + first line)

| Library | Mean | Allocated |
|---|---:|---:|
| **Tint** (loader path) | **2.50 ms** | 5,898 KB |
| Tint, every reached pattern compiled (N = 0) | 187.2 ms | 20,951 KB |
| TextMateSharp 2.0.4 | 4.70 ms | 7,812 KB |
| TextMateLib 0.2.1 | 10.27 ms | 709 KB (managed only) |

### Rendering `medium.cs` (956 lines, 36 KB)

| Operation | Mean | Allocated |
|---|---:|---:|
| Tokens only (`TokenizeLineEncoded`) | 17.22 ms | 280.2 KB |
| `ToHtml`, Dark+ | 17.32 ms | 596.4 KB |
| `ToHtml`, Light+ and Dark+ (two tokenizations) | 33.75 ms | 988.9 KB |
| `ToHtml` to a `TextWriter` | 17.30 ms | 280.2 KB |
| `ToAnsi`, Dark+ | 17.57 ms | 581.7 KB |

Rendering costs under 2% over tokenizing. Streaming to a writer allocates nothing beyond the
tokenizer's own per-line allocations; the string overloads add only the result string (the HTML of
`medium.cs` is about 160k characters).

### Tier 2: runtime-compiled hot patterns

`TokenizerOptions.CompileHotPatterns` (design section 5.5). Threshold -1 is the interpreter.

| Workload | Threshold | Warm | Cold (fresh registry) | Cold allocated |
|---|---:|---:|---:|---:|
| `bigfile.cs` | off | 1.79 s | 1.73 s | 42.7 MB |
| | 0 | 1.70 s | 2.13 s | 83.6 MB |
| | 16 | 1.72 s | 2.15 s | 87.3 MB |
| | 256 | 1.70 s | 2.12 s | 82.8 MB |
| `medium.cs` | off | 16.94 ms | 89.5 ms | 10.5 MB |
| | 0 | 16.39 ms | 553.4 ms | 49.4 MB |
| | 16 | 16.47 ms | 476.1 ms | 45.1 MB |
| | 256 | 16.43 ms | 337.4 ms | 26.5 MB |
| docs mix (all 8 languages) | off | 16.06 ms | 126.1 ms | 128.2 MB |
| | 0 | 20.26 ms | 1,787.6 ms | 273.7 MB |
| | 16 | 19.28 ms | 1,949.0 ms | 260.4 MB |
| | 256 | 19.61 ms (median) | 1,141.2 ms | 217.6 MB |

Once warm, compiling gains 3–5% on C# and loses 20–26% on short documentation blocks (per
language, from the docs-heavy run: json −10%, yaml −4%, csharp −1%, markdown +12%, bash +25%,
typescript +38%, javascript +35%, html +42%); from a cold registry it costs 0.25 s to 1.8 s. The
option therefore stays off by default, with N = 256 (the fewest compiled patterns for the same warm
C# gain) when enabled. The cold docs runs with N = 0 and 16 have a large spread (medians 247 ms and
575 ms, single iterations up to 4.9 s).

### `TokenizeLineBenchmarks`, before and after P5

Two alternating runs each of `main` (4fd7bfe) and the P5 branch. The tier-2 counter adds one
comparison per scanner search when the option is off; the difference is within noise and the
allocations are unchanged.

| Method | File | `main` run 1 / 2 | P5 run 1 / 2 | Allocated (both) |
|---|---|---:|---:|---:|
| Tokens | medium.cs | 17.04 / 16.53 ms | 17.38 / 16.76 ms | 280.17 KB |
| Encoded | medium.cs | 17.00 / 17.08 ms | 17.02 / 17.02 ms | 280.17 KB |
| Tokens | sample.cs | 93.91 / 93.47 µs | 92.84 / 93.95 µs | 2.42 KB |
| Encoded | sample.cs | 94.70 / 93.46 µs | 93.11 / 92.49 µs | 2.42 KB |
