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

## P3 — Source generator

- `src/Tint.Generator` (netstandard2.0, `IsRoslynComponent`): `TintGrammar` / `TintTheme`
  AdditionalFiles via `build/Tint.props`; shared parsing library; emitter for grammar classes
  (static info, lazy `RawGrammar` builder, de-duplicated pattern table, anchor variants,
  `PatternIndex`), emitter for theme classes; diagnostics (invalid regex with position, missing
  dependency scope, duplicate class name).
- Kernel emission through GregsEx's public API when present, lazy `Parse` otherwise; the
  `tools/Tint.Cli` two-step path for the bundles.
- Generator tests in the GregsEx style (`CSharpGeneratorDriver`, assert diagnostics and output),
  plus the vscode-textmate suites run through generated grammars.
- Packed inside the `Tint` nupkg under `analyzers/dotnet/cs`.
- Done when: a sample project imports `csharp.tmLanguage.json` and `dark_plus.json`, builds with
  no warnings, and tokenizes identically to the loader path.

## P4 — Bundles

- `src/Tint.Grammars` and `src/Tint.Themes` generated from `grammars/` and `themes/` per the P2
  decision (single assembly or per-family split), `TintGrammars.All` / `TintThemes.All`
  registration helpers, generated `THIRD-PARTY-NOTICES.txt` packed under `licenses/`.
- Done when: every bundled grammar tokenizes its sample file, package sizes recorded, trim and AOT
  smoke apps that reference one grammar show only that grammar in the trimmed output.

## P5 — Renderers, facade, benchmarks

- `HtmlRenderer` (single and dual theme, line spans, style merging), `AnsiRenderer`,
  `Highlighter` facade, README usage section.
- `benchmarks/Tint.Benchmarks` with TextMateSharp 2.0.4 and TextMateLib 0.2.1 baselines, the
  `bigfile.cs` and docs-heavy workloads, `benchmarks/README.md` with results.
- Done when: benchmarks run on macOS arm64 and linux x64 and the numbers are in the README table.

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

## Session protocol

Every implementation session: reads `AGENTS.md`, `docs/design.md` and this file; works on a branch
named `phase/<n>-<slug>`; keeps the build warning-free (`TreatWarningsAsErrors`); adds tests with
the code; runs `dotnet format --verify-no-changes`; opens a pull request against `main` with a
summary of measurements when relevant; never pushes to `main` directly.
