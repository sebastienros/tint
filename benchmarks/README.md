# Tint benchmarks

BenchmarkDotNet harness. `[MemoryDiagnoser]` is the allocation authority: the per-line tokenizer
path must report zero allocated bytes once warm (beyond the `StateStack` nodes of `begin` rules;
the line text of interpreted patterns goes through a reused buffer since GregsEx 0.6.0, design
section 5.6).

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

## GregsEx 0.6.0 and the line buffer

GregsEx 0.6.0 walks a scanner's starts once for all its patterns instead of searching each pattern in turn, so no
interpreted pattern runs past the winning start ([gregsex#42](https://github.com/sebastienros/gregsex/pull/42), design
section 5.4); alternations also jump to the first branch that can start at a position
([gregsex#38](https://github.com/sebastienros/gregsex/pull/38)) and the failure memo covers more patterns
([gregsex#39](https://github.com/sebastienros/gregsex/pull/39)). Searching a span then costs what searching a
string-backed line with the scanner's per-string cache does, so the tokenizer copies each line into a reused `char[]`
instead of allocating `line + "\n"`, except for scanners holding a compiled pattern (design section 5.6). Four
builds, once warm:

- **0.5.8 (`main`)**: before the change.
- **0.6.0, string lines**: `main` with GregsEx 0.6.0 and nothing else: the GregsEx change alone.
- **0.6.0, span lines only**: every scanner searching a span over the line buffer.
- **0.6.0, this PR**: interpreted scanners search the span, scanners holding a compiled pattern a string of the line.

Intel Xeon 2.1 GHz cloud VM (4 cores), Ubuntu 24.04.5, .NET 10.0.14 (SDK 10.0.403), X64 RyuJIT x86-64-v4,
BenchmarkDotNet 0.15.8, 1 launch, 5 warmups, 15 iterations, 2026-11-10, one build at a time with nothing else
running, in the order string, span, `main`, span, string, this PR, this PR; the 0.6.0 columns are the mean of their
two runs, which differ by up to 10% (the VM's run-to-run noise). `bigfile.cs` and `TokenizeLineBenchmarks` use the C#
grammar (bundled, loader, kernel-compiled, tier 2 with N = 256), the editor files the loader
(`EditorFilesBenchmarks.Tint`), the docs mix the bundled grammars (`DocsHeavyBenchmarks.Tint`). Allocations are exact.

| Workload | 0.5.8 (`main`) | 0.6.0, string lines | 0.6.0, span lines only | **0.6.0, this PR** | Speedup | Allocated, `main` | Allocated, this PR |
|---|---:|---:|---:|---:|---:|---:|---:|
| `bigfile.cs`, bundled | 1.57 s | 560.8 ms | 561.8 ms | **546.7 ms** | 2.87× | 32.6 MB | 18.3 MB (-44%) |
| `bigfile.cs`, loader | 1.55 s | 539.6 ms | 551.8 ms | **550.4 ms** | 2.82× | 32.6 MB | 18.3 MB (-44%) |
| `bigfile.cs`, kernel-compiled | 2.20 s | 1.44 s | 2.02 s | **1.44 s** | 1.52× | 32.6 MB | 32.6 MB (+0%) |
| `bigfile.cs`, tier 2 | 2.17 s | 1.45 s | 2.07 s | **1.47 s** | 1.48× | 32.6 MB | 32.6 MB (+0%) |
| `medium.cs`, `Token` | 14.8 ms | 4.69 ms | 4.84 ms | **4.78 ms** | 3.09× | 280.2 KB | 186.7 KB (-33%) |
| `medium.cs`, encoded | 14.1 ms | 4.66 ms | 4.81 ms | **4.63 ms** | 3.04× | 280.2 KB | 186.7 KB (-33%) |
| `sample.cs`, `Token` | 84 µs | 32 µs | 31 µs | **31 µs** | 2.69× | 2.4 KB | 1.3 KB (-45%) |
| `sample.cs`, encoded | 85 µs | 32 µs | 30 µs | **31 µs** | 2.73× | 2.4 KB | 1.3 KB (-45%) |
| Editor `xml.props` | 19.6 ms | 15.6 ms | 14.1 ms | **14.8 ms** | 1.33× | 2.9 MB | 1.3 MB (-57%) |
| Editor `json.json` | 14.1 ms | 10.2 ms | 9.21 ms | **9.92 ms** | 1.42× | 2.4 MB | 1.3 MB (-45%) |
| Editor `yaml.yml` | 50.5 ms | 38.9 ms | 37.5 ms | **37.3 ms** | 1.35× | 2.8 MB | 1.5 MB (-47%) |
| Editor `markdown.md` | 82.0 ms | 32.1 ms | 36.1 ms | **32.9 ms** | 2.49× | 2.7 MB | 1.3 MB (-54%) |
| Editor `html.html` | 74.6 ms | 64.3 ms | 61.3 ms | **63.5 ms** | 1.18× | 3.4 MB | 2.0 MB (-42%) |
| Editor `css.css` | 226.2 ms | 49.3 ms | 48.5 ms | **48.9 ms** | 4.62× | 3.6 MB | 2.5 MB (-29%) |
| Editor `javascript.js` | 273.8 ms | 101.2 ms | 97.5 ms | **100.9 ms** | 2.71× | 3.3 MB | 2.3 MB (-31%) |
| Editor `shell.sh` | 111.8 ms | 44.3 ms | 43.6 ms | **39.4 ms** | 2.84× | 4.2 MB | 3.0 MB (-28%) |
| Editor `scss.scss` | 58.3 ms | 23.5 ms | 24.7 ms | **25.1 ms** | 2.33× | 1.5 MB | 992.3 KB (-37%) |
| Editor `less.less` | 46.5 ms | 21.1 ms | 20.7 ms | **22.0 ms** | 2.12× | 1.9 MB | 1.3 MB (-29%) |
| Docs, bash | 1.47 ms | 582 µs | 590 µs | **592 µs** | 2.49× | 73.7 KB | 63.6 KB (-14%) |
| Docs, csharp | 4.44 ms | 1.22 ms | 1.25 ms | **1.19 ms** | 3.72× | 89.5 KB | 68.6 KB (-23%) |
| Docs, html | 1.76 ms | 1.04 ms | 1.07 ms | **1.10 ms** | 1.60× | 78.4 KB | 61.6 KB (-21%) |
| Docs, javascript | 5.10 ms | 1.81 ms | 1.88 ms | **1.84 ms** | 2.77× | 84.8 KB | 67.2 KB (-21%) |
| Docs, json | 423 µs | 275 µs | 269 µs | **286 µs** | 1.48× | 63.3 KB | 48.7 KB (-23%) |
| Docs, markdown | 1.24 ms | 516 µs | 539 µs | **567 µs** | 2.19× | 45.2 KB | 32.3 KB (-28%) |
| Docs, typescript | 6.44 ms | 2.27 ms | 2.17 ms | **2.25 ms** | 2.86× | 95.5 KB | 75.7 KB (-21%) |
| Docs, yaml | 1.37 ms | 961 µs | 943 µs | **970 µs** | 1.41× | 105.3 KB | 88.9 KB (-16%) |

- **GregsEx 0.6.0 is the speedup**: with interpreted patterns, C# 2.7× to 3.7× faster (`bigfile.cs` 1.57 s → 0.55 s,
  3.7 → 10.6–10.9 MB/s), the other editor files and fences 1.2× (HTML) to 4.6× (CSS).
- **The line buffer is the allocation drop** and costs no measurable time: `bigfile.cs` allocates 18.3 MB instead of
  32.6 MB (the 14.3 MB were the line strings), the editor files 28% to 57% less. What is left is `StateStack` nodes
  and the first growth of the tokenizer's buffers.
- **Compiled patterns lose to the interpreter.** GregsEx 0.6.0 searches a kernel or a tier-2 pattern whole, to its
  own match or the end of the line, so the kernel-compiled grammar and tier 2 take 2.6× the interpreted time on
  `bigfile.cs`. With span lines they also lose the per-string cache that spares them most of those searches (2.0 s
  instead of 1.45 s), which is why their scanners keep a string of the line. On this machine kernels were already
  slower than the interpreter with GregsEx 0.5.8 (2.20 s against 1.57 s; the M4 Pro rows below say otherwise).
- With GregsEx 0.5.8 the span path was 1.2× to 1.5× slower than the string one (a `ShortRun` pass): the per-string
  cache was what made later searches of a line cheap, which is why the string was there.
- Keeping the per-string cache for spans does not pay either: a prototype GregsEx overload taking a caller-supplied
  line version (owner and counter, so the scanner keeps what it learned of a line across the searches of that line
  over the reused buffer) measured within 10% of the plain span path in both directions over two runs on the
  gregsex#42 head. Tint does not ask for it.

### `bigfile.cs` against the other libraries (this PR)

The whole `BigFileBenchmarks` class on the final build, same machine and job, right after the runs above:

| Library | Time | MB/s | Allocated |
|---|---:|---:|---:|
| **Tint, bundled grammar** (interpreted) | **533.9 ms** ± 53.2 | 10.88 | 18.3 MB |
| Tint, loader | 570.4 ms ± 40.4 | 10.18 | 18.3 MB |
| Tint, tier 2 (N = 256) | 1,462.9 ms ± 99.0 | 3.97 | 32.6 MB |
| Tint, kernel-compiled grammar | 1,484.4 ms ± 84.8 | 3.91 | 32.6 MB |
| TextMateLib 0.2.1 (native Oniguruma) | 5,033.0 ms ± 93.2 | 1.15 | 31.0 MB |
| TextMateSharp 2.0.4 | 14,469.8 ms ± 463.0 | 0.40 | 507.3 MB |

The interpreted bundle is 9.4× faster than TextMateLib and 27× faster than TextMateSharp on this machine.

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
