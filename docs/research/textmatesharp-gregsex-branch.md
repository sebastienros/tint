# Lessons from the TextMateSharp + GregsEx branch

Repository `/Users/sebastienros/github/TextMateSharp-gregsex`, branch `gregsex-pr` (fork
`sebastienros/TextMateSharp`, upstream PR danipen/TextMateSharp#134), 25 commits on top of
upstream e0db45b. Code-only diff: 30 files, +969/-126; the new piece is
`src/TextMateSharp/Internal/Oniguruma/OnigScanner.cs` (231 lines) wrapping GregsEx.

## Wiring

- No syntax translation layer. GregsEx parses Oniguruma syntax natively with
  `RegexProfiles.OnigurumaTextMate`. The one rewrite, `OnigScanner.Preprocess` (port of Onigwrap's
  `UnicodeCharEscape`): `\x` followed by 2–8 hex digits becomes `\x{…}`, and `\x{7fffffff}` is
  clamped to `\x{7ffff}` (PHP grammar).
- Compile limits raised: `new RegexCompileLimits(maxPatternUnits: 1 << 22, maxNestingDepth: 4096,
  maxInstructions: 1 << 24, maxClassRanges: 1 << 20, maxCaptureGroups: 32767, maxCompileWork: 1L <<
  32, maxPlanBytes: 1L << 30, maxCompileMemoryBytes: 1L << 30)`. "Grammars hold patterns far longer
  than GregsEx's default ceiling (objective-c's platform list is 282k units)."
- Invalid pattern → replaced by `(?!)` and remembered; `FindNextMatch` throws
  `ArgumentException("Invalid regular expression: …")` only when the search reaches it
  (Onigwrap's in-order-stop-at-offset semantics).
- Anchors and `\z`: unchanged vscode-textmate logic (`RegExpSource.HandleAnchors`,
  `BuildAnchorCache` four variants with `￿`, `RegExpSourceList.Compile(allowA, allowG)`).
- Dynamic ends: unchanged `ResolveBackReferences` + `SetSource` recompiling a new scanner; the
  fuzzing worktree adds a lone-surrogate guard (a lone surrogate in the capture becomes
  `(?:\p{Cs}|\x{FFFD})`).
- Captures: `OnigCaptureIndex` readonly struct `{Index, Start, End, Length, IsDefined}`;
  non-participating groups reported as `(i, 0, 0)` for Onigwrap compatibility; arrays rented from
  `OnigCapturePool` (per-length free lists, grows past 16: "C#'s grammar has patterns with a hundred
  groups and more"), reset before every scan.
- Other allocation work: struct `MatchResult`/`LocalStackElement`/`TokenizeStringResult`; reusable
  `LineTokenizer`s per grammar and nesting depth; `AttributedScopeStack._children` cache keyed on
  the grammar's raw metadata object; `Stopwatch.GetTimestamp()` deadline instead of a `Stopwatch`
  per line; the appended newline built as `lineText.ToString() + "\n"` because the scanner cache
  only works for string-backed text (this was the single largest win: csharp −46%).

## Measurements (Apple M4 Pro, .NET 10)

`DocsHeavyTokenizationBenchmark` (40 fences per language), mean per op, Upstream (Onigwrap) →
GregsEx first commit → after allocation work: bash 14.72 → 2.88 → 2.43 ms; ts 48.06 → 14.06 →
10.14; js 91.92 → 21.10 → 15.86; html 9.21 → 2.60 → 1.88; json 1.99 → 0.69 → 0.51; yaml 15.56 →
2.48 → 1.98; csharp 89.03 → 50.85 → 26.64 ms (18.06 ms with preview.24). Allocations upstream →
final: bash 3,564 → 915 KB; ts 8,305 → 978; js 14,510 → 1,259; html 4,813 → 1,506; json 2,222 →
570; yaml 3,164 → 735; csharp 8,008 → 1,704 KB.

`BigFileTokenizationBenchmark` (`bigfile.cs`, 133,439 lines): Onigwrap 4.885 s / 510 MB vs GregsEx
1.737 s / 137 MB; perf worktree down to ≈1.74 s / 69.7 MB. Samples: csharp 391 µs / 23.45 KB, html
81.9 µs / 12.66 KB per file.

Douze docs site (4,000 pages, NativeAOT): 3.31–4.04 s wall → 1.22–1.74 s wall, output
byte-identical.

## Correctness

- Tokens of 2,800 fences byte-identical to Onigwrap; vscode-textmate suites added to the tests
  with 6 cases skipped as pre-existing TextMateSharp gaps (`VsCodeTextMateSuiteTests.KnownDifferences`:
  end-pattern back-reference spanning EOL, `${1/…/}` transforms, nested Markdown includes, rule
  without match regex, injection grammars, back-reference escaping).
- Corpus check (`tools/TextMateSharp.CorpusCheck`): 7,352,306 lines, 8 mismatched, 0 errors in
  21,364 files over 50 grammars; speedups 1.44x (cpp) to 21.72x (yaml), csharp 3.97x, typescript
  4.02x, python 5.49x, sql 14.05x. The 8 Ruby lines involve a nested negated class holding U+0000
  where GregsEx follows vscode-oniguruma.
- Fuzz campaign (`tools/TextMateSharp.Fuzz`): 2,475,156 inputs, 76 grammars, 207 unique findings.
  GregsEx: no open issue, no hangs. Onigwrap: 5 hangs > 60 s, 118 per-line timeouts, 3 wrong-token
  cases from Oniguruma's retry limit, 72 lone-surrogate findings. Onigwrap's result cache is wrong
  for `\G` patterns (suite1 #8, Makefile); GregsEx skips caching those.

## Carry over to Tint

- Let GregsEx parse Oniguruma; keep only the `\xHH` rewrite and the vscode anchor/`\z` logic.
- Keep Onigwrap/vscode-oniguruma semantics: earliest start wins, first pattern on tie, stop at a
  match at offset, non-participating groups as not-participated, invalid pattern throws only when
  reached, work-limit = no match.
- Pool captures per depth, reuse tokenizers, cache scope-stack children, use timestamp deadlines,
  resolve capture names and back-references with a reusable builder, not `Regex.Replace`.
- `\G` results must not be cached across offsets; recompiled end patterns need escaping and
  lone-surrogate handling; the per-line time limit only fires between searches, so the engine's
  work limit is the real guard.
- Reuse the verification toolchain: vscode-textmate suites, SHA-256 token dumps over samples, a
  differential corpus checker and a fuzzer loading the reference and the candidate in separate
  `AssemblyLoadContext`s.
