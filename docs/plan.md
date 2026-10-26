# Tint implementation plan

Phases are sized for one implementation session each (Opus 5.5, medium effort), driven from the
design session. Each phase names its inputs, its deliverable and the check that proves it done.
Arrows mark dependencies; phases on the same row can run in parallel.

```
P0 scaffold ──► P1 core tokenizer + themes ──► P3 generator ──► P4 bundles ──► P6 playground
            └─► P2 kernel-size spike ────────┘               └─► P5 renderers, facade, benchmarks
                                                                             └─► P7 hardening, 0.1.0
```

## P0 — Scaffold and grammar sync

- Solution `Tint.slnx`; projects `src/Tint`, `src/Tint.Generator`, `test/Tint.Tests`,
  `benchmarks/Tint.Benchmarks`, `tools/Tint.Sync`; `Directory.Build.props`,
  `Directory.Packages.props` (CPM), `nuget.config` (nuget.org + Feedz `gregsex` feed mapped to
  `GregsEx*`), `.editorconfig`, `AGENTS.md`, workflows (`build.yml` ubuntu+windows, `aot.yml`,
  `publish.yml` with NuGet trusted publishing), repo settings.
- `tools/Tint.Sync`: fetch grammars + themes + `package.json` + `cgmanifest.json` from a pinned
  `microsoft/vscode` commit into `grammars/` and `themes/`, write `grammars/manifest.json`
  (scope name, language, path, embedded languages, injectTo, token types, bracket scopes, license,
  upstream) and `THIRD-PARTY-NOTICES.txt`.
- vscode-textmate test fixtures copied under `test/Tint.Tests/Fixtures/vscode-textmate/` with
  their license.
- Done when: `dotnet build` and `dotnet test` succeed with a placeholder test, CI is green,
  grammars and themes are committed with provenance.

## P1 — Core runtime (the big one)

- `Tint.Grammars` model (`RawGrammar`, `RawRule`, `RegexSource`, `IncludeReference`,
  `IGrammarSource`, `GrammarInfo`), `Tint.Loading` JSON + plist grammar loader (STJ source-generated
  context), theme model + loader (JSON with `include`, tmTheme).
- Rule compiler (`RuleFactory`, rule kinds, capture tables, pattern flattening, end-first ordering,
  `applyEndPatternLast`), `RegexSource` anchor variants and `\z` rewrite, dynamic end/while
  resolution with LRU, `RuleScanner` over GregsEx `OnigurumaScanner` with raised compile limits
  and the `\xHH` rewrite.
- Tokenizer: `StateStack`, `AttributedScopeStack` with child cache, `tokenizeString` loop with the
  four guards, captures with retokenization, `while` checks, injections and selector matcher,
  encoded token attributes, token merging, time budget.
- Theme engine: parse, resolve, trie, match, `ColorMap`, `StyleAttributes`.
- `GrammarRegistry`: register sources, resolve external includes and injections, create
  `Tokenizer`s.
- Tests: vscode-textmate suites (95 tokenization cases + themes suite) green through the loader;
  unit tests for the matcher, theme precedence, encoded attributes, state equality.
- Done when: all suites pass, `dotnet test` green on ubuntu and windows, allocations per line
  measured with a first micro-benchmark.

## P2 — Kernel-size spike (parallel with P1, own worktree)

- Throwaway project: emit `[GeneratedPattern]` methods for every distinct pattern of the C# and
  TypeScript grammars (and the four anchor variants where needed) with the GregsEx generator from
  the Feedz preview. Record: generated source size, assembly size, build time, generator diagnostics
  (how many patterns declined), first-call time, and match speed vs `OnigurumaPattern.Parse` on
  representative lines.
- Also test the fallback shape (lazy `Parse`) for size and first-use cost.
- Done when: a short report in `docs/research/kernel-spike.md` with the numbers and a
  recommendation for bundle partitioning and the kernel length threshold.
- Outcome (2026-10-25): one lazy-pattern bundle, no length threshold, kernels opt-in; see design
  5.5 and 8.4.

## P3 — Source generator

- `src/Tint.Generator` (netstandard2.0, `IsRoslynComponent`): `[TextMateGrammar(path)]` and
  `[TextMateTheme(path)]` attributes on partial classes (emitted as internal post-initialization
  sources) bound to `TintGrammar` / `TintTheme` AdditionalFiles via `build/Tint.props`, plus the
  item-only path with default class names; shared parsing library; emitter for grammar classes
  (static info, lazy `RawGrammar` builder, de-duplicated pattern table, anchor variants,
  `PatternIndex`), emitter for theme classes; diagnostics (invalid regex with position, missing
  dependency scope, unmatched attribute path, duplicate class name).
- Patterns parsed lazily with the TextMate compile limits by default (design 5.5 tier 1). Kernels
  opt-in (`Kernels = true` / `<TintKernels>`) through GregsEx's `KernelSource` façade (gregsex#27),
  reached by loading the consumer's `GregsEx.Generator.dll`, found from the compilation's
  `GregsEx.dll` reference (or the `TintGregsExPackagePath` property), through a reflection shim;
  informational diagnostic and interpreted fallback when the façade is absent. No command-line tool anywhere in the product; the bundles use the same
  generator.
- Generator tests in the GregsEx style (`CSharpGeneratorDriver`, assert diagnostics and output),
  plus the vscode-textmate suites run through generated grammars.
- Packed inside the `Tint` nupkg under `analyzers/dotnet/cs`.
- Done when: a sample project imports `csharp.tmLanguage.json` and `dark_plus.json`, builds with
  no warnings, and tokenizes identically to the loader path.

## P4 — Bundles

- `src/Tint.Grammars` (one assembly, all 84 grammars, lazily parsed patterns, per the P2 decision)
  and `src/Tint.Themes` generated from `grammars/` and `themes/`, `TintGrammars.All` /
  `TintThemes.All` registration helpers, generated `THIRD-PARTY-NOTICES.txt` packed under
  `licenses/`.
- Done when: every bundled grammar tokenizes its sample file, package sizes recorded, trim and AOT
  smoke apps that reference one grammar show only that grammar in the trimmed output.
- Delivered: `tools/Tint.Sync` writes `VSCodeGrammars.props`/`VSCodeThemes.props` with explicit
  class names; the generator's opt-in `TintGrammarsClass`/`TintThemesClass` aggregates (`All`,
  `Find`, one property per class); 84 grammars in `Tint.Grammars` (5.2 MB assembly, 1.9 MB package,
  10.5 s build per target framework) and 19 themes in `Tint.Themes` (180 KB, 94 KB package); every
  grammar tokenizes its shiki sample like the loader path; the smoke app trims `Tint.Grammars.dll`
  to 146 KB with only `CSharpGrammar` and `JsonGrammar` left, and runs as a 2.9 MB NativeAOT binary
  (osx-arm64), in `.github/workflows/aot.yml` on linux-x64 and osx-arm64.

## P5 — Renderers, facade, benchmarks

- `HtmlRenderer` (single and dual theme, line spans, style merging), `AnsiRenderer`,
  `Highlighter` facade, README usage section.
- `benchmarks/Tint.Benchmarks` with TextMateSharp 2.0.4 and TextMateLib 0.2.1 baselines, the
  `bigfile.cs` and docs-heavy workloads, `benchmarks/README.md` with results.
- Tier-2 runtime compilation (design 5.5): `Compile()` hot patterns after N searches on JIT
  runtimes; measure N and the gain on `bigfile.cs`; off under AOT.
- Done when: benchmarks run on macOS arm64 and linux x64 and the numbers are in the README table.
- Outcome (2026-10-25, macOS arm64 measured; linux x64 not yet run): `Highlighter`, `HtmlRenderer`
  (single, dual and named themes, CSS classes), `AnsiRenderer` shipped. On an M4 Pro, `bigfile.cs`
  tokenizes in 1.73 s (3.35 MB/s, 32.6 MB) against 7.65 s / 507 MB for TextMateSharp 2.0.4 and
  2.80 s for TextMateLib 0.2.1 (its native library loads on osx-arm64); docs-heavy is 2.8×–8.7×
  faster than TextMateSharp with 4.5×–52× fewer allocations; first use 2.5 ms vs 4.7 ms; HTML
  rendering adds under 2% to tokenization. Tier 2 is opt-in (`TokenizerOptions.CompileHotPatterns`,
  threshold 256): 3–5% faster on C# once warm, slower on short blocks, hundreds of milliseconds to
  compile. Numbers in [benchmarks/README.md](../benchmarks/README.md).

## P6 — Playground

- `samples/Tint.Playground` Blazor WebAssembly: language and theme pickers, editor, HTML preview,
  token inspector, permalinks; AOT + trimmed publish; lazy assembly loading; `pages.yml` workflow
  deploying to GitHub Pages.
- Done when: the site is live from `main`.

## P7 — Hardening and 0.1.0

- Differential corpus check and fuzzing tools under `tools/`, run on a prepared corpus; fix
  divergences or document them.
- API review, XML docs on every public member, package validation, `v0.1.0` tag through
  `publish.yml`.
- Outcome (2026-10-25): `tools/Tint.CorpusCheck` and `tools/Tint.Fuzz` compare Tint with
  TextMateSharp 2.0.4 reading the same grammar files and settle every difference with
  vscode-textmate 9.3.2 itself (`tools/Tint.CorpusCheck/referee`). Corpus: 14 pinned repositories,
  10,345 files, 3.3 million lines, no exception; 1,845 files differ from TextMateSharp and
  vscode-textmate agrees with Tint on all of them
  ([report](research/corpus-check-2026-10-25.md)). Fuzzing: 20 minutes, 12,484 inputs, 731,709
  lines over the 84 grammars; no Tint exception, hang or time-out and no tokenizer difference from
  vscode-textmate; one GregsEx work-limit difference and long lines where GregsEx is slower than Oniguruma kept as GregsEx
  inputs ([report](research/fuzz-2026-10-25.md)). Public API tracked by PublicApiAnalyzers
  (`PublicAPI.Shipped.txt`/`Unshipped.txt`) and, for the generated bundle classes, by
  `PublicAPI.Generated.txt` checked in a test; package validation on; `StateStack` reduced to
  `Initial`, `Depth` and equality. `CHANGELOG.md` and [release.md](release.md) written; the
  `v0.1.0` tag is the maintainer's call.

## Session protocol

Every implementation session: reads `AGENTS.md`, `docs/design.md` and this file; works on a branch
named `phase/<n>-<slug>`; keeps the build warning-free (`TreatWarningsAsErrors`); adds tests with
the code; runs `dotnet format --verify-no-changes`; opens a pull request against `main` with a
summary of measurements when relevant; never pushes to `main` directly.
