# Kernel-size spike (P2), 2026-10-25

What does it cost to compile every regex of a TextMate grammar to GregsEx source-generated kernels,
and what does it buy over the lazy interpreted `OnigurumaPattern.Parse` fallback? The projects,
drivers and raw results are in [`tools/spikes/KernelSize/`](../../tools/spikes/KernelSize/README.md)
(`results/*.json`).

## Short answer

- Kernels are big and slow to build, and most of the cost is in places a length threshold does not
  reach. The C# grammar's 338 pattern methods become 5.8 MB of generated C# and a 1.0 MB assembly
  (10.7× the lazy-`Parse` assembly). The full corpus would be about 60–100 MB of IL and 5–20 minutes
  of clean build on this machine.
- Nearly all of the build time is the ILLink trim analyzer (switched on by `IsAotCompatible`)
  reading the generated code. The GregsEx generator itself takes about 0.1 s. Without the trim
  analyzers the same builds take 1.3–3.6 s.
- The speedup is modest. With every search cross-checked equal (27,040 searches, 0 mismatches),
  kernels are 2.5× faster on the 30 patterns that match most often, but only 1.33× (geometric mean)
  over all 293 kernel-backed C# patterns, and slower on 28 of them. The first search costs 1.2 ms
  per kernel for the JIT, against 18 µs interpreted.
- **Recommendation:** ship all 84 grammars in one `Tint.Grammars` assembly as lazy `Parse`
  patterns. Do not emit kernels for the bundles until GregsEx fixes the issues listed under "Asks
  for GregsEx" and a tokenizer-level benchmark shows a real gain. Then add kernels as an opt-in,
  curated per-language-family package. A Tint-side length threshold is not needed, because GregsEx
  already declines patterns over about 940 characters. Lazy `Parse` is an acceptable fallback for
  the generator.

## Environment

- Apple M4 Pro (14 cores), 48 GB, macOS 15.8.1 (24H32).
- .NET SDK 10.0.400 (`global.json` 10.0.100 with `latestFeature` roll-forward), runtime 10.0.11,
  Roslyn 5.0.0 as shipped in that SDK.
- GregsEx **0.5.0** from nuget.org (the same commit, 0510cf29, as Feedz `0.1.0-preview.56`; the
  first pass ran on preview.56 and gave the same numbers within noise).
- The spike projects inherit the repository's `Directory.Build.props` (`TreatWarningsAsErrors`,
  `EnforceCodeStyleInBuild`) and set `IsAotCompatible` + `IsTrimmable`, as the shipped bundles will.
- Grammars from microsoft/vscode @ `3ac0f056`: `csharp.tmLanguage.json`,
  `TypeScript.tmLanguage.json`, and, beyond the brief, `cpp.tmLanguage.json` as a stress case
  (long patterns, many `\A` variants).

## Pattern extraction (`Emit`)

Rules are walked recursively (`patterns`, `repository`, `injections`, `captures`,
`beginCaptures`, `endCaptures`, `whileCaptures`), and every `match`/`begin`/`end`/`while` string
is collected. Pre-processing follows design §5: `\xHH` → `\x{HH}` (with the `\x{7fffffff}` clamp)
and `\z` → `$(?!\n)(?<!\n)`. Texts are de-duplicated, then the vscode-textmate anchor variants are
expanded (`\A`/`\G` letter → U+FFFF when disallowed) and de-duplicated again. `end`/`while`
patterns with `\1`..`\9` are skipped and counted, using vscode's `/\\(\d+)/` test. Patterns that
contain U+FFFF or control characters are emitted as regular escaped literals; all others as `@"…"`.

| Grammar | Patterns (match/begin/end/while) | Dynamic end/while skipped | Distinct | with `\A` / `\G` / both | Methods incl. variants | Pattern chars emitted | Longest |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C# | 547 (149/199/196/3) | 0 | 331 | 0 / 7 / 0 | 338 | 29,292 | 1,044 |
| TypeScript | 515 (169/173/173/0) | 1 | 365 | 1 / 4 / 0 | 370 | 93,251 | 8,385 |
| C# + TypeScript | 1,062 | 1 | 663 (33 shared) | 1 / 11 / 0 | 675 | 122,387 | 8,385 |
| C++ (extra) | 1,083 (749/167/167/0) | 1 | 271 | 97 / 16 / 1 | 385 | 279,143 | 8,667 |

No `\xHH` rewrite was needed in these grammars, and C++ needed one `\z` rewrite.

## Build results

`dotnet build -c Release`. "Clean" means `bin/` and `obj/Release/` were deleted and restore was
already done; "cold" is the first clean build after `dotnet build-server shutdown`; the warm figures
are the median of 3. "No trim analyzers" is the same clean build with `EnableTrimAnalyzer`,
`EnableAotAnalyzer` and `EnableSingleFileAnalyzer` set to false. Generated C# is everything under
`EmitCompilerGeneratedFiles` output. Diagnostics come from the SARIF error log.

| Project | Methods | Kernels | GREGEX003 declines | Emitted source | Generated C# | DLL | Clean cold / warm | Clean, no trim analyzers | No-op incremental |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| (a) `Tint.Spike.CSharp` | 338 | 293 | 45 | 77,747 B | 5,845,130 B | **1,013,248 B** | 12.5 s / 10.1 s | 1.28 s | 0.47 s |
| (b) `Tint.Spike.TypeScript` | 370 | 344 | 26 | 151,729 B | 13,806,707 B | **2,877,440 B** | 37.4 s / 30.4 s | 2.51 s | 0.43 s |
| (c) `Tint.Spike.Both` | 675 | 604 | 71 | 225,006 B | 19,429,582 B | **3,913,216 B** | 48.0 s / 42.5 s | 3.17 s | 0.48 s |
| (d) `Tint.Spike.CSharpLazy` | 338 | 0 | – | 99,531 B | 1,795 B | **94,720 B** | 1.4 s / 0.73 s | 0.59 s | 0.47 s |
| (extra) `Tint.Spike.Cpp` | 385 | 286 | 99 | 340,013 B | 17,771,895 B | **4,125,696 B** | 52.1 s / 45.4 s | 3.59 s | 0.49 s |

The build with `EmitCompilerGeneratedFiles=true` took the same time as the normal build (10.0 s,
29.6 s, 42.4 s, 0.6 s, 44.1 s).

No GREGEX001 (invalid pattern) errors and no other GregsEx diagnostics were reported, so no
grammar needed attention. All kernel and lazy `All()` calls construct without exceptions, including
the C++ patterns of up to 8,667 characters that the generator parses with GregsEx's default compile
limits.

### Declined patterns (GREGEX003, "runs on the interpreter")

| Reason | C# | TypeScript | C++ |
| --- | --- | --- | --- |
| A loop whose body can match the empty string is not lowered | 24 (10–812 chars) | 1 (849) | 75 (47–7,087) |
| Subexpression calls are not lowered (`\g<…>`) | 18 (137–1,044) | – | – |
| The absent operator is not lowered (`(?~\*/)`, one pattern × its `\G` variants) | 3 | – | – |
| The program exceeds the lowering node bound | – | 20 (942–8,385) | 19 (919–8,667) |
| A character class exceeds the class bound (`[[:upper:]]` and friends) | – | 5 (53–169) | – |
| Backreferences are not lowered | – | – | 5 |

The longest kernel-backed pattern is 354 characters in C#, 978 in TypeScript and 714 in C++.
GregsEx already acts as a length threshold: every pattern over 978 characters in these grammars was
declined. Note that the corpus notes count `(?~` only in yaml, but C# has one `(?~\*/)` pattern (in
the doc-comment rules).

### Where the build time goes

`-p:ReportAnalyzer=true` on the clean builds:

| Project | Analyzer CPU time | of which ILLink `DynamicallyAccessedMembersAnalyzer` | GregsEx generator |
| --- | --- | --- | --- |
| C# | 120.2 s | 119.8 s | 0.04 s |
| TypeScript | 390.5 s | 390.0 s | 0.09 s |
| Both | 551.4 s | 550.6 s | 0.13 s |
| C++ | 573.0 s | 572.3 s | 0.14 s |
| C# lazy | 0.15 s | 0.11 s | 0.002 s |

These are CPU seconds across 14 cores. Comparing with the no-trim-analyzer builds, the analyzer
accounts for 87–93% of the clean-build wall time. The trim analyzer walks the very large kernel `Run` methods (hundreds of labels and
`goto`s each). The analyzer cost is roughly linear in generated code: (a) + (b) = 40.5 s against
42.5 s for (c).

### Where the size goes

The generated size per kernel correlates with pattern length (Pearson r 0.77–0.91), but it has a
large fixed part. 219 of the 293 C# kernels come from patterns of 30 characters or fewer, and they
still generate a median of 8.3 KB each. Every kernel emits two classes (`Plain` and `Capture`), and
each carries its own `SearchValues` and Unicode `Bitmap` range strings. `[[:alpha:]]` alone is a
~1,500-character literal, repeated per class. Per method in the DLL: 3.0 KB (C#), 7.8 KB
(TypeScript), 5.8 KB (both), 10.7 KB (C++), against 280 B for lazy `Parse`. The largest kernels are
85 KB (C#, a 260-char pattern), 260 KB (TypeScript, 772 chars) and 253 KB (C++, 418 chars). The
top 10% of kernels make up 28–36% of the generated source.

## First use and memory (C# grammar, JIT)

Median of 7 fresh processes (`Measure first kernel|lazy`). `All()` constructs every pattern. Then
one `FindCaptures` per pattern on one line creates its workspace and runs it once.
`GregsExEnableAutoCompilation=false`, so interpreted means interpreted.

| | Kernels (`[GeneratedPattern]`) | Lazy `Parse` |
| --- | --- | --- |
| `All()` (338 patterns) | 89.8 ms (266 µs/pattern) | 46.8 ms (138 µs/pattern) |
| Allocated by `All()` | 26.0 MB | 11.8 MB |
| Retained after `All()` | 1.43 MB | 1.27 MB |
| First search of every pattern | **416.6 ms (1,233 µs/pattern)** | 6.1 ms (18 µs/pattern) |
| Allocated by the first searches | 15.9 MB | 0.64 MB |
| Retained after first searches | 3.26 MB | 1.27 MB |
| Kernel-backed patterns | 293 of 338 | 0 |

The first-search cost is the JIT. The kernel `Run` methods are `[MethodImpl(AggressiveOptimization)]`,
so they skip tiering and are not precompiled by ReadyToRun either. Their static tables (`Bitmap`
arrays of 1,024 `ulong`s, `SearchValues`) are built on first use. `All()` on kernels also costs more
than plain `Parse`, because the generated `Create()` parses the pattern and then calls
`WithGeneratedKernels`.

NativeAOT (`AotProbe`, osx-arm64, 3 runs after a clean publish):

| Variant | Binary | Δ vs GregsEx alone | `All()` | First search of every pattern |
| --- | --- | --- | --- | --- |
| GregsEx only, one pattern | 2,030,792 B | – | 0.2 ms | 0.01 ms |
| C# lazy `Parse` | 2,180,104 B | +149 KB | 5.5 ms | 0.6 ms |
| C# kernels | 3,356,296 B | **+1,326 KB** | 27 ms | 8.7 ms |

Under AOT the kernels add 1.3× their IL size in native code. Even without the JIT, kernel first use
costs ~5× the lazy path, because of the static table initialization.

## Matching throughput (C# grammar)

20 lines of `mediumfile.cs` from TextMateSharp (`samples/lines.txt`, each with `\n` appended).
`OnigurumaWorkspace.FindCaptures` is called at position 0 and at ¼, ½ and ¾ of the line, with a
capture span of `GroupCount`. Each timing does 1,000 warm-up rounds and then takes the best of 5
trials of 10,000 rounds (line = round % 20). Kernel and interpreted results were cross-checked on
every pattern × line × position: **27,040 searches, 0 mismatches** (status, range and every capture).

The 30 patterns with the highest match rate. 26 of them are kernel-backed; the other 4 run on the
interpreter on both sides and show ~1.0×:

| | Kernel ns/search | Interpreted ns/search |
| --- | --- | --- |
| Position 0, mean over the 30 | 223.8 | 430.5 |
| Inner positions, mean over the 30 | 88.4 | 195.3 |
| Geometric-mean speedup, all 30 | **2.22×** | |
| Geometric-mean speedup, 26 kernel-backed | **2.50×** | |

Per pattern the speedup ranges from 0.33× to 8.9×: identifiers `@?[_[:alpha:]][_[:alnum:]]*` 4.2×,
the 229–260-char type-name patterns 3.2–3.5×, `(?<=$)` 8.9×. Trivial patterns are slower as kernels:
`$` 0.87× and `(?=$)` 0.33× (130 ns against 28 ns). The kernel scans the whole input for
surrogates (`IndexOfAnyInRange('\uD800', '\uDFFF')`) and sets up its guard on every call, which
outweighs the match itself.

A top-30-by-match-rate list favors exactly those trivial patterns that match everywhere, while a
tokenizer mostly runs patterns that fail. So the same timing was run over **every** pattern:

| All 338 C# patterns | Geometric-mean speedup |
| --- | --- |
| All patterns | 1.28× |
| 293 kernel-backed | **1.33×** (p10 1.01×, median 1.17×, p90 2.74×, max 7.88×) |
| 166 kernel-backed patterns that match at least once on these lines | 1.47× |
| Kernel slower than interpreter | 28 patterns |

Most failing searches end in the prefilters (literal / FIRST-set / required-unit), which both paths
share, so kernels only help once a candidate start is found. A run with `GregsExEnableAutoCompilation=true`
gave the same numbers (`results/measure-auto-compilation.jsonl`): runtime Auto compilation does not
compile `OnigurumaPattern`s, so JIT platforms also stay on the interpreter unless `Compile()` is
called explicitly.

## Extrapolation to the full corpus

The corpus has 17,314 patterns, 2.5 MB of pattern text, 330 dynamic `end`/`while`, 1,292 `\G` and
644 `\A` patterns. De-duplication varies a lot: C# keeps 61% of its patterns, TypeScript 71%, C++
25%, and the C family and the JS/TS family share most of their patterns across grammars. Expected
size: about 8,000–12,000 distinct texts plus 1,000–1,500 anchor variants (C++ alone adds 114 for its
97 `\A` and 16 `\G` patterns), so about **10,000 methods**. GregsEx declines 7–26% of them (C#: 13%), mostly the
long ones.

| Assumption | Measured per method | ≈ 10,000 methods |
| --- | --- | --- |
| Kernel DLL size | 3.0 KB (C#) … 5.8 KB (C#+TS) … 10.7 KB (C++) | **30–110 MB**. With the C family (cpp, cuda-cpp, cpp.embedded.macro, c, objc, objcpp ≈ 30% of the corpus) near the top, expect ~60–100 MB |
| Kernel NativeAOT code | ×1.3 of the IL | 40–140 MB native before trimming |
| Kernel clean build, trim analyzers on (14-core M4 Pro) | 30 ms (C#) … 63 ms (C#+TS) … 118 ms (C++) wall | **5–20 min** (~10 min central); 0.36–1.49 analyzer CPU-s/method = 1–4 CPU-hours, so ≥1 h on a 4-vCPU CI runner |
| Kernel clean build, trim analyzers off | 3.8 ms … 4.7 ms … 9.3 ms | 40–95 s |
| Generated C# on disk | 17 KB … 29 KB … 46 KB | 0.2–0.5 GB |
| Lazy `Parse` DLL | 280 B (C#, dominated by the UTF-16 pattern text) | **~4–7 MB** (≈1.5–2.5 M distinct chars × 2 bytes + per-method IL) |
| Lazy `Parse` clean build | 2.2 ms | a few seconds |
| No-op incremental build | 0.4–0.5 s regardless | 0.5 s, but any edit to a kernel bundle project pays the full clean cost |

A single all-grammars kernel assembly is therefore out: it would be a 60–100 MB package and a
multi-minute, CPU-bound build in CI on every change to that project. The generator is not the
bottleneck.

## Recommendations

### 1. Bundle partitioning: one `Tint.Grammars` assembly, lazy patterns

Ship all 84 grammars in one `Tint.Grammars` assembly with lazy `Parse` patterns. That is ~4–7 MB,
builds in seconds, and its first use is 18 µs per pattern actually reached. Each grammar is its own
class with lazily built rules and lazily parsed patterns, so trimmed and AOT apps keep only what they
reference (P4's smoke test should confirm this). Splitting by family is not worth the extra packages
at this size.

Revisit kernels once GregsEx shares tables between kernels and drops the per-call overhead, and once
a tokenizer-level benchmark (P5 `bigfile.cs`) shows a gain worth tens of megabytes. Then ship them as
**opt-in per-family kernel packages** (for example `Tint.Grammars.Kernels.Web` for JS/TS/HTML/CSS/JSON,
`.DotNet` for C#/F#/VB/Razor, `.CFamily`, `.Scripting`, `.Markup`). They should hold only the
families whose hot patterns benefit, not a curated subset of languages in the main bundle. Blazor
can lazy-load such a package per language.

### 2. Kernel length threshold: none on the Tint side

GregsEx's lowering bounds already decline every pattern over ~940–980 characters, together with the
nullable-loop and subexpression-call patterns. In the three measured grammars every pattern over 978 characters
was declined, so the corpus's long tail (338 patterns of 1,000–5,000 characters, 54 over 5,000) will
mostly land on the interpreter anyway. Below that, size is driven by
per-kernel fixed overhead and Unicode classes, not length: a 71-character C# pattern generates 60 KB.
A length cut cannot fix that. If a size budget is wanted for opt-in kernel packages, cap on the
generated size per kernel (for example skip kernels whose emitted source exceeds 100 KB: 22 of 344
TypeScript kernels, 28% of its generated code) or on measured benefit, not on pattern length.

### 3. Lazy `Parse` as the generator fallback: yes

Lazy `Parse` is an acceptable fallback, and the right default until GregsEx has a kernel-emission
API and the fixes below:

- It is 10.7× smaller (DLL), builds ~14× faster, and is 70× cheaper on first search under JIT and
  14× under AOT.
- It gives identical results.
- It costs only 1.33× in steady-state per-search speed over all patterns (2.5× on the hottest).

The TextMateSharp + GregsEx branch already reached 1.4–22× over Onigwrap on the interpreter.

Tint's lazy path must pass the raised `RegexCompileLimits`, as the spike's lazy class does. The
GregsEx-generated `Create()` calls `Parse` with default limits, and objective-c's 282k-unit platform
list will need the raised ones.

## Asks for GregsEx (from this spike)

1. Share `SearchValues`/`Bitmap` tables between the `Plain` and `Capture` kernels of a pattern and
   across the kernels of an assembly. Avoid the per-kernel copy of the Unicode `[:alpha:]` tables.
2. Remove the per-call fixed cost that makes `$`, `(?=$)` and `(?=[^;\)])` slower than the
   interpreter. The `IndexOfAnyInRange(D800–DFFF)` scan of the whole input on every call could be
   cached per input, or skipped when the program cannot meet a surrogate. Trivial programs could
   also stay on the interpreter.
3. Reconsider `AggressiveOptimization` on `Run`: it blocks ReadyToRun and costs ~1.2 ms of JIT per
   kernel on first use.
4. Generated code that the ILLink `DynamicallyAccessedMembersAnalyzer` can skip, or guidance to
   disable the trim analyzers for kernel-only assemblies. The analyzer accounts for 87–93% of build
   time.
5. A compile-limits knob on `[GeneratedPattern]`, or in the kernel-emission API, so generated
   `Create()` can use raised limits.
6. Lower nullable-body loops (the largest decline reason: 24 C#, 75 C++) and subexpression calls
   (all of C#'s type-name patterns).
