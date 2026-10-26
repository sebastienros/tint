# Tint.Fuzz

Differential fuzzer: generates inputs for every bundled grammar, tokenizes them with Tint and with TextMateSharp 2.0.4
(Onigwrap) in the same process, and records every difference in tokens, styles, exceptions and time.

## How it works

- Engines and comparison are the ones of [Tint.CorpusCheck](../Tint.CorpusCheck/README.md) (`tools/Tint.Differential`):
  Tint's bundled grammars against TextMateSharp reading the same grammar files, Dark+ on both sides, `TokenizeLine`
  scopes and `TokenizeLine2` styles compared line by line with the state carried.
- Seeds, per grammar: its shiki sample (`test/Tint.Tests/Fixtures/samples`) and up to 40 corpus files with its
  extensions (`artifacts/corpus`, fetched by `tools/Tint.CorpusCheck/fetch.sh`); grammars with neither (injections,
  included-only grammars) draw from every sample.
- An input is a slice of 1 to 40 seed lines and one to five mutations: insertions of grammar-relevant fragments
  (quotes, braces, backslashes, `$`, `#`, comment and string delimiters, surrogate pairs, lone surrogates, NUL,
  U+FFFF, BOM, combining and bidi characters), character flips (control characters, astral characters, lone
  surrogates), deletions, duplicated, swapped, split, joined and spliced lines, long lines (up to 20,000 characters,
  VS Code's default tokenization limit) and deep nesting on one line or over many. Inputs are a function of the
  seed, the grammar and the iteration; grammars take turns.
- Kinds of findings, most severe for Tint first: `harness` (the fuzzer itself failed), `tint-hang` (a line past
  `--hang-seconds`: the thread is abandoned and a new worker starts), `tint-exception`, `tint-slow` (a line over
  `--slow-line-ms`), `stopped-tint` (Tint ran out of the per-line limit, TextMateSharp did not), `exception-type`,
  `tokens`, `styles`, then the reference's own: `reference-hang`, `reference-exception`, `reference-slow`,
  `stopped-reference`, and `invalid-utf16` (a difference on an input with a lone surrogate: Onigwrap works on UTF-8,
  so its offsets shift).
- A new finding is minimized (lines, then characters, while it keeps its kind) and saved once per signature (kind,
  grammar, and the innermost scopes or exception types involved); later hits only count.
- Referee: token, style and exception findings whose input survives UTF-8 are replayed by vscode-textmate
  (`tools/Tint.CorpusCheck/referee`), which says whether it agrees with Tint, with TextMateSharp, or with neither.

## Run

```bash
dotnet run -c Release --project tools/Tint.Fuzz -- --duration 00:20:00 --seed 20261025
```

Options: `--grammars source.cs,yaml` (scope names or language ids), `--workers N`, `--hang-seconds 60`,
`--slow-line-ms 1000`, `--line-time-limit-ms 10000` (both engines), `--minimize-budget 300`, `--referee false`,
`--corpus dir`, `--out dir` (default `artifacts/fuzz`). The output has `findings.md` (summary per kind and per
grammar, then each finding with its minimized input and both outputs), `findings.json` and the referee's input
files. The exit code is 1 when Tint threw, hung, was slow, ran out of time, or disagrees with vscode-textmate.

Replay findings (all, or one by signature) with both engines:

```bash
dotnet run -c Release --project tools/Tint.Fuzz -- repro artifacts/fuzz/findings.json --signature "tokens source.yaml …"
```

The latest campaign is [docs/research/fuzz-2026-10-25.md](../../docs/research/fuzz-2026-10-25.md).
