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
| `BigFileBenchmarks` | `'BigFileBenchmarks*'` | TextMateSharp's `bigfile.cs` (133,439 lines, 5.8 MB), once warm, `tokenizeLine2` paths: Tint with the bundled grammar, the kernel-compiled grammar, tier-2 hot patterns and the loader, TextMateSharp 2.0.4, TextMateLib 0.2.1. The file is downloaded into `samples/.cache/` (git-ignored) on first run. |
| `DocsHeavyBenchmarks` | `'*DocsHeavy*'` | 40 fenced blocks per language (`samples/docs/*.md`: csharp, typescript, javascript, html, json, yaml, bash, markdown), each from the initial state: Tint (bundled grammars, tier-2 hot patterns, loader; kernel-compiled grammar for csharp only) vs TextMateSharp. |
| `RenderBenchmarks` | `'*Render*'` | `Highlighter.ToHtml` (one theme, light + dark, to a `TextWriter`) and `ToAnsi` on `medium.cs`, against tokenization alone. |
| `FirstUseBenchmarks`, `FirstUseColdBenchmarks` | `'*FirstUse*'` | Registry creation, C# grammar and Dark+ loading, first line tokenized: Tint (loader, bundled, kernel-compiled, compile-all), TextMateSharp, TextMateLib, in a warm process; and the Tint rows in a fresh process per measurement (`RunStrategy.ColdStart`, 10 launches), which includes the JIT of the kernels the line reaches. |
| `EditorFilesBenchmarks` | `'*EditorFiles*'` | The ten files of Passenger's highlighting benchmark (`samples/passenger`, each repeated to about 10,000 lines as Passenger does), line by line with the grammar files Passenger ships (VS Code's, through `GrammarLoader`) and from the bundle, once warm, against TextMateSharp 2.0.4 reading the same grammar files. |
| `EditorFilesColdBenchmarks` | `'*EditorFilesCold*'` | The same files, ten passes in a fresh process (one pass per iteration, three launches): the time an editor sees while the runtime is still compiling Tint and GregsEx, which is what Passenger's harness measures. Runs out of process through `ColdStartConfig`, so without `--inProcess`. |
| `HotPatternBenchmarks`, `HotPatternColdBenchmarks` | `'*HotPattern*'` | Tier-2 runtime compilation (`TokenizerOptions.CompileHotPatterns`) for thresholds off/0/16/256, once warm and from a cold registry, on `bigfile.cs`, `medium.cs` and the whole docs mix. |

MB/s is 10^6 bytes of UTF-8 source per second. TextMateLib benchmarks are skipped with a message
when its native library does not load (it ships win-x64, linux-x64, osx-x64 and osx-arm64 only).

What each row tokenizes:

- **Tint, bundled**: `TintGrammars.CSharp` from `Tint.Grammars` (generated from the synced VS Code
  grammar, patterns parsed on first use and interpreted). This is what `Tint.Grammars` users get.
- **Tint, kernel-compiled**: `CSharpKernelsGrammar`, the same VS Code grammar generated in this
  project with `<TintGrammar ... Kernels="true" />`: 327 of the 338 pattern-table entries run
  ahead-of-time GregsEx kernels (293 with GregsEx 0.5.1, before kernels covered subexpression
  calls), the rest stay interpreted. It is what a project that imports a grammar with
  `Kernels="true"` gets.
- **Tint, tier 2**: the bundled grammar with `TokenizerOptions.CompileHotPatterns` (threshold 256).
- **Tint, loader**: `GrammarLoader` on `grammars/csharp/syntaxes/csharp.tmLanguage.json`.
- **TextMateLib 0.2.1** (native Oniguruma) reads the same synced VS Code grammar file.
- **TextMateSharp 2.0.4** (Onigwrap) uses the C# grammar bundled in TextMateSharp.Grammars, which is
  older, so token counts differ slightly.

Every library uses Dark+ (TextMateLib gets it with its `include` chain flattened, since it does not
resolve includes) and its encoded binary-token path, `tokenizeLine2` or the equivalent:
`Tokenizer.TokenizeLineEncoded` for Tint, `TokenizeLine2` for TextMateSharp and TextMateLib.

Kernels cost build time: with `Kernels="true"` the benchmark project's `Csc` step takes 3.8 s instead
of 0.9 s (warm compiler server, M4 Pro), and the generated C# grammar grows from 0.45 MB to 5.8 MB of
source (docs/generator.md).

## Results

Apple M4 Pro (14 cores), macOS 15.8.1 (24H32), .NET 10.0.10 (SDK 10.0.302), Arm64 RyuJIT,
BenchmarkDotNet 0.15.8 `ShortRun` (1 launch, 3 warmup, 3 iterations), GregsEx 0.5.1, 2026-10-25.
Each class ran twice back to back with no other benchmark, test or fuzz process running (load
average 2–3 from background services). The first full pass ran while another session was fuzzing
and was discarded; the `bigfile.cs` rows of the two full passes that followed still had single
iterations up to 2× slower, so `BigFileBenchmarks` ran twice more on its own and those two passes
are below. Tables show both passes; the headline table in the repository README takes, for each
row, the pass with the smaller error.

### `bigfile.cs` (133,439 lines, 5.8 MB)

| Library | Pass 1 | Pass 2 | MB/s (1 / 2) | Allocated |
|---|---:|---:|---:|---:|
| **Tint, bundled grammar** (interpreted) | 1.795 s ± 0.06 | **1.765 s** ± 0.01 | 3.24 / 3.29 | 32.6 MB |
| Tint, kernel-compiled grammar | 1.738 s ± 0.48 | **1.687 s** ± 0.003 | 3.34 / 3.44 | 32.6 MB |
| Tint, tier 2 (N = 256) | 1.740 s ± 0.09 | 1.948 s ± 5.3 | 3.34 / 2.98 | 32.6 MB |
| Tint, loader | 1.721 s ± 0.02 | 2.193 s ± 2.2 | 3.38 / 2.65 | 32.6 MB |
| TextMateLib 0.2.1 | **2.807 s** ± 0.49 | 3.621 s ± 4.5 | 2.07 / 1.60 | 31.0 MB |
| TextMateSharp 2.0.4 | 9.554 s ± 15.4 | **7.867 s** ± 0.71 | 0.61 / 0.74 | 507.3 MB |

± is BenchmarkDotNet's error (half the 99.9% confidence interval). With GregsEx 0.5.1, kernels did
not change the picture at the tokenizer level: the kernel-compiled grammar was 3–4% faster than the
bundled one (1.69–1.74 s against 1.77–1.80 s), about the spread between the bundled and loader
rows, which run the same interpreted patterns, because the type-name patterns built on subexpression
calls (about 83% of matching time) were declined and ran interpreted either way. Allocations are
identical: kernels change how a pattern searches, not what the tokenizer allocates.

#### GregsEx 0.5.2 (kernels for subexpression calls)

.NET 10.0.11, GregsEx 0.5.2, 2026-10-26, two runs back to back on an idle machine, in-process
(`--inProcess --warmupCount 2 --iterationCount 3`: in-process because the `.claude/worktrees`
copies of this project stop BenchmarkDotNet from picking it, and in-process drops the class's
`ShortRun` job). `TINT011`: 327 of 338 patterns compiled ahead of time.

| Library | Run 1 | Run 2 | MB/s (1 / 2) | Allocated |
|---|---:|---:|---:|---:|
| **Tint, bundled grammar** (interpreted) | 1.824 s ± 0.08 | 1.793 s ± 0.03 | 3.18 / 3.24 | 32.6 MB |
| Tint, kernel-compiled grammar | **1.371 s** ± 0.01 | **1.337 s** ± 0.19 | 4.24 / 4.34 | 32.6 MB |
| Tint, tier 2 (N = 256) | 1.412 s ± 0.44 | 1.380 s ± 0.36 | 4.11 / 4.21 | 32.6 MB |
| Tint, loader | 1.812 s ± 0.21 | 1.827 s ± 0.29 | 3.21 / 3.18 | 32.6 MB |
| TextMateLib 0.2.1 | 2.910 s ± 0.12 | 2.918 s ± 0.26 | 2.00 / 1.99 | 31.0 MB |
| TextMateSharp 2.0.4 | 8.117 s ± 0.85 | 7.991 s ± 0.27 | 0.72 / 0.73 | 507.3 MB |

**The kernel-compiled grammar now tokenizes the file in 0.75 of the interpreted time**, and tier 2
(runtime `Compile()` of hot patterns, the same kernels) gains the same. A `dotnet-trace` CPU profile
of one tokenization per grammar: with the interpreted grammar `OnigMatcher` is 79% of the samples;
with the kernel grammar it no longer appears among the top 400 functions, the kernels are 56% and
vectorized span helpers 12% of a smaller total. The property and field declaration patterns are the
two hottest kernels.

### Docs-heavy (40 fences per language)

Pass 2 (pass 1 agrees within 5% except the bash and html rows, whose first benchmark had a slow
iteration).

| Language | Tint, bundled | MB/s | Allocated | Tint, loader | Tier 2 (N = 256) | TextMateSharp | Allocated | Speedup |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| bash | 0.64 ms | 5.21 | 73.7 KB | 0.64 ms | 0.79 ms | 1.85 ms | 333.2 KB | 2.9× |
| csharp | 3.37 ms | 2.20 | 89.5 KB | 3.38 ms | 3.51 ms | 14.50 ms | 1,586.8 KB | 4.3× |
| html | 1.38 ms | 4.45 | 78.4 KB | 1.38 ms | 1.92 ms | 11.39 ms | 2,888.9 KB | 8.2× |
| javascript | 3.37 ms | 1.86 | 84.8 KB | 3.47 ms | 4.45 ms | 25.20 ms | 4,386.6 KB | 7.5× |
| json | 0.19 ms | 26.81 | 63.3 KB | 0.19 ms | 0.17 ms | 1.26 ms | 947.6 KB | 6.7× |
| markdown | 0.91 ms | 4.39 | 45.2 KB | 0.89 ms | 0.72 ms | 3.44 ms | 519.3 KB | 3.8× |
| typescript | 4.78 ms | 1.51 | 95.5 KB | 4.60 ms | 5.96 ms | 25.45 ms | 3,955.0 KB | 5.3× |
| yaml | 0.67 ms | 7.10 | 105.3 KB | 0.67 ms | 0.63 ms | 4.97 ms | 1,069.4 KB | 7.5× |

C# with the kernel-compiled grammar: 3.37 ms / 3.31 ms (passes 1 / 2, 2.24 MB/s, 89.5 KB), against
3.53 ms / 3.37 ms bundled: 2–5% faster, the same allocations.

### Editor files (Passenger's fixtures)

The workload of [Zaneris/passenger#572](https://github.com/Zaneris/passenger/pull/572), which replaced TextMateSharp
with Tint and measured XML at 0.85 and Markdown at 0.95 of TextMateSharp's tokenizer time while every other language
gained 2–14×. The cause was not matching but JIT tiering: Tint's per-line path and GregsEx's interpreter ran in tier-0
code (nothing inlined, three to five times slower) for the first seven to nine passes over a 10,000-line file, and
Passenger's harness, like an editor, keeps compiling new methods, which restarts the runtime's call-counting delay.
`MethodImplOptions.AggressiveOptimization` on the per-line path (design section 6.2; GregsEx 0.5.5 for the scanner
and the interpreter's search, attempt and helpers, its instruction loop left tiered for its profile) fixes it.

.NET 10.0.11, GregsEx 0.5.4 (before) and the 0.5.5 branch (after), 2026-10-29, before and after in the same session
(Microsoft Defender was scanning new worktrees during part of it: errors are larger than usual). Cold: mean of the
ten passes of three fresh processes each (`EditorFilesColdBenchmarks`, lower is better; the first pass also loads the
grammars). Warm: `EditorFilesBenchmarks`, in-process, 2 warmups, 3 iterations. TextMateSharp reads the same grammar
files.

| File | Lines | Tint cold, before | Tint cold, after | TextMateSharp cold | Tint warm, before | Tint warm, after | TextMateSharp warm |
|---|---:|---:|---:|---:|---:|---:|---:|
| `xml.props` | 9,992 | 46.5 ms | **26.4 ms** | 73.1 ms | 11.18 ms | 11.73 ms | 56.6 ms |
| `json.json` | 9,969 | 39.4 ms | **18.3 ms** | 63.1 ms | 8.34 ms | 8.55 ms | 47.6 ms |
| `yaml.yml` | 9,889 | 101.0 ms | **63.9 ms** | 115.2 ms | 28.26 ms | 30.55 ms | 102.6 ms |
| `markdown.md` | 9,960 | 126.9 ms | **84.3 ms** | 320.4 ms | 50.07 ms | 49.59 ms | 308.7 ms |
| `html.html` | 9,759 | 100.2 ms | **66.2 ms** | 229.7 ms | 48.42 ms | 49.94 ms | 216.5 ms |
| `css.css` | 9,961 | 375.4 ms | 351.9 ms | 4,403.7 ms | 337.0 ms | 335.4 ms | 4,354.5 ms |
| `javascript.js` | 9,919 | 287.9 ms | 258.4 ms | 1,802.2 ms | 238.4 ms | 241.4 ms | 1,828.0 ms |
| `shell.sh` | 9,673 | 134.3 ms | **99.6 ms** | 486.3 ms | 64.55 ms | 63.62 ms | 487.2 ms |
| `scss.scss` | 9,997 | 146.9 ms | **102.7 ms** | 1,243.2 ms | 77.98 ms | 78.82 ms | 1,284.1 ms |
| `less.less` | 9,997 | 87.1 ms | **50.9 ms** | 268.6 ms | 29.11 ms | 28.39 ms | 271.0 ms |

Allocations are unchanged (2.9 MB per pass of `xml.props`, 2.7 MB of `markdown.md`, almost all of it the per-line
strings). The bundled grammars (`TintBundled`) are within 2% of the loader rows. The other warm benchmarks moved
within noise: `bigfile.cs` 1.774 s → 1.772 s (kernels 1.413 s → 1.342 s), `medium.cs` 17.46 ms → 17.54 ms, the
docs-heavy languages −3% to +2%.

Marking the interpreter's instruction loop too was measured and rejected: the first passes fell a further 1.5–2×
(XML 21.5 ms, Markdown 78.9 ms cold), but every steady-state number lost 8–12% (`bigfile.cs` 1.99 s, `medium.cs`
19.2 ms, `sample.cs` 112 µs against 95 µs), the loop's dynamic profile being worth that much.

In Passenger's own harness (its `Passenger.HighlightingBenchmarks`, .NET 11 RC2, three processes each, shipped Tint
0.1.2 against this branch), the ten passes over `xml.props` take 841 ms → 630 ms in all, `markdown.md`
1,440 ms → 1,081 ms, `json.json` 722 ms → 404 ms, `yaml.yml` 1,183 ms → 854 ms, `shell.sh` 1,875 ms → 1,464 ms,
`html.html` 1,581 ms → 1,210 ms (medians of the three processes' sums), with identical colour hashes. Its per-pass
medians are not comparable pass for pass: promotion to tier 1 happens after a given wall time, so a faster build
fits more passes before it.

### First use (registry + C# grammar + Dark+ + first line)

Warm process (the JIT has compiled Tint and, after BenchmarkDotNet's first invocation, the kernels):

| Library | Pass 1 | Pass 2 | Allocated |
|---|---:|---:|---:|
| Tint, loader | 2.38 ms | 2.35 ms | 5,898 KB |
| **Tint, bundled grammar** | **0.19 ms** | **0.18 ms** | 558 KB |
| Tint, kernel-compiled grammar | 0.20 ms | 0.19 ms | 651 KB |
| Tint, loader, every reached pattern compiled (N = 0) | 183.8 ms | 179.6 ms | 20,921 KB |
| TextMateSharp 2.0.4 | 4.52 ms | 4.48 ms | 7,812 KB |
| TextMateLib 0.2.1 | 10.09 ms | 9.98 ms | 709 KB (managed only) |

Fresh process per measurement (`FirstUseColdBenchmarks`, 10 launches, one invocation each), so the
time includes the JIT of everything the first line reaches:

| Tint | Pass 1 | Pass 2 | Allocated |
|---|---:|---:|---:|
| Loader | 54.7 ms | 47.3 ms | 6,036 KB |
| Bundled grammar | 70.4 ms | 65.0 ms | 559 KB |
| Kernel-compiled grammar | 221.0 ms | 207.0 ms | 652 KB |

The kernels the first line reaches add about 140–150 ms of JIT in a fresh process; the spike's
1.2 ms per kernel predicts this order of magnitude. Once JIT-compiled, first use costs the same as
the bundled grammar.

The sections below are from the P5 run (same machine, .NET 10.0.11, loader path).

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
