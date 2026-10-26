# GregsEx audit (for Tint)

Read from `origin/main` of https://github.com/sebastienros/gregsex (HEAD 0510cf29, "Compiled
kernels: lookaround, \G, atomic/possessive groups, and Oniguruma (#25)"). NuGet stable: 0.4.3
(2026-10-24). Previews on Feedz (`https://f.feedz.io/sebastienros/gregsex/nuget/index.json`) as
`0.1.0-preview.N` (latest 0.1.0-preview.56 at the time of writing); previews carry the 0.1.0
`VersionPrefix`, so pin them exactly.

## What it is

Multi-flavor regex library: `RegularCoreProfile`, `EcmaScriptProfile`, `XsdProfile`, `XPathProfile`,
`PosixProfile`, `Pcre2Profile`, `OnigurumaProfile` ("Oniguruma 6.9.10 default (Ruby) syntax, as
TextMate grammars use it: possessive/atomic groups, variable-length look-behind, subexpression
calls, absent repeater; a TextMate multi-pattern scanner", UTF-16). Targets
`netstandard2.0;net8.0;net10.0`, strong-named, `IsAotCompatible`. One package `GregsEx`; the
generator `GregsEx.Generator.dll` ships inside it under `analyzers/dotnet/cs`.

Architecture: per-flavor parser → AST → semantic IR → plan → immutable `RegexPattern` → exclusive
mutable workspace. Backends: backtracking VM, Thompson NFA reference, PCRE2 frame VM, dedicated
Oniguruma backtracker (`Flavors/Oniguruma/OnigMatcher.cs`). Prefilters: literal/`SearchValues`,
FIRST-set, required-unit. Optional runtime compilation via a private FastExpressionCompiler copy
(`Compile()`, `RequiresDynamicCode`, disabled under AOT by a feature switch).

## Public API

```csharp
public sealed class RegexPattern {
  static RegexPattern Parse(string pattern, RegexProfile profile, RegexCompileOptions? options = null);
  static bool TryParse(ReadOnlySpan<char> pattern, RegexProfile profile, out RegexPattern? result, out RegexDiagnostic diagnostic, RegexCompileOptions? options = null);
  int GroupCount /* includes group 0 */;
  RegexWorkspace CreateWorkspace(RegexWorkspaceOptions? options = null);
}
public sealed class RegexWorkspace : IDisposable {
  RegexSearchStatus Find(ReadOnlySpan<char> input, int startAt, out RegexMatchRange match, RegexMatchOptions options = default);
  RegexSearchStatus FindCaptures(ReadOnlySpan<char> input, int startAt, Span<RegexCapture> groups, out RegexMatchRange match, RegexMatchOptions options = default);
}
public readonly record struct RegexMatchRange(int Index, int Length);
public readonly record struct RegexCapture(bool Participated, int Index, int Length);
public enum RegexSearchStatus { Match, NoMatch, InvalidInput, DestinationTooSmall, LimitExceeded, Cancelled, TimedOut }
public readonly record struct RegexMatchOptions { CancellationToken CancellationToken; TimeSpan? Timeout; RegexMatchLimits? Limits; }
```

Allocation-free once warm; captures go into a caller `Span<RegexCapture>`. `startAt` bounds the
match start, not what lookbehind/anchors see (pass the whole line plus an offset, never a slice).
`\G` matches at `startAt`; `\A` sees the whole input. No culture dependency. Limits:
`RegexMatchLimits(maxWork=10_000_000, maxWorkspaceBytes=64MiB)`, polled every 4,096 work units.

Oniguruma typed API (`Public/Oniguruma/`): `OnigurumaPattern.Parse(string, OnigurumaProfile?,
OnigurumaCompileOptions?)`, `TryParse(ReadOnlySpan<char>, profile, out pattern, out
RegexDiagnostic, options)`, `GroupCount`, `GetGroupNumbers(ReadOnlySpan<char> name)`,
`GetGroupName(int)`, `CreateWorkspace()`; `OnigurumaWorkspace.Find/FindCaptures(ReadOnlySpan<char>,
int startAt, …)`; `OnigurumaScanner(IReadOnlyList<OnigurumaPattern>)` with
`FindNextMatch(string | ReadOnlyMemory<char> | ReadOnlySpan<char> input, int startAt, out int
patternIndex[, RegexMatchOptions])`, `Captures` (`ReadOnlySpan<RegexCapture>`),
`LimitExceededCount`. Presets `RegexProfiles.Oniguruma` and `RegexProfiles.OnigurumaTextMate`
(ONIG_OPTION_CAPTURE_GROUP). `OnigurumaOptions { None, IgnoreCase, Multiline (dot matches newline),
Extended, CaptureGroup, DontCaptureGroup }`.

`OnigurumaScanner.Find`: loops patterns in order, keeps `bestStart`, breaks early when `bestStart ==
startAt`; per-pattern cache keyed on `ReferenceEquals(_lastText[i], text)` + start/length and
`_lastPosition[i] <= startAt && (!_lastMatched[i] || captures[0] >= startAt)`, skipped when the
program uses `\G`; `LimitExceeded` counts as no match and is cached as one; one matcher and one
capture buffer for all patterns; not thread-safe. The cache only works for string-backed memory.

## Source generator

`[GeneratedPattern(pattern, flags)]` (namespace `GregsEx.SourceGeneration`) on `static partial
RegexPattern Name();` or `static partial OnigurumaPattern Name();` with `OnigurumaOptions`. Emits a
cached lazy field, a `Create()` doing `Parse(...).WithGeneratedKernels(0x<fingerprint>UL,
Plain.Run, Capture.Run)`, and static kernel classes with `Run(ReadOnlySpan<char> input, int
startAt, Span<int> caps, Span<int> stack, long maxSteps, ref long work, ref KernelGuard guard)`.
When the planner declines (back-references, subexpression calls, conditionals, absent operator,
`\K`, text-segment boundaries, non-BMP literals, loops over a nullable body) it reports GREGEX003
(Info, "runs on the interpreter") and emits a plain `Parse`. Invalid pattern → GREGEX001 error.
Kernels bail out to the interpreter on subjects with surrogates. No public programmatic emit API:
`SourcePlanner.TryPlan` and `KernelEmitter.EmitClass` are internal.

## Syntax checklist (Oniguruma flavor)

Supported: lookahead/lookbehind incl. variable-length; atomic `(?>)`; possessive `*+ ++ ?+`;
named groups `(?<n>)` `(?'n')`; backrefs `\n \k<n> \k<-n> \k<+n> \k<name>`; conditionals
`(?(n)..|..)` `(?(<name>)..|..)`; subexpression calls `\g<name> \g<n> \g<0> \g<-n> \g<+n>`;
`\p{..} \p{^..} \P{..}` with Oniguruma's 886 property names; POSIX brackets `[:alpha:]`
`[:^alpha:]`; `\h`/`\H` = hex digit; `\R`, `\X`, `\K`, `\O`, `\N`, `\y \Y`; `\x{H…}`, `\uHHHH`,
`\xHH` as encoded bytes; anchors `^ $ \A \z \Z \G \b \B`; inline options `(?imxWDSP-imxWDSP)`
isolated and scoped, `(?y{g})`; comments `(?#...)`; class set ops incl. `&&`; absent repeater
`(?~x)`. Case folding follows Oniguruma including multi-code-point folds. Errors:
`RegexParseException : ArgumentException` with `RegexDiagnostic(Code, Offset, Length, Message)`.
Unsupported: callouts, absent stoppers `(?~|..|..)`, `\k<n+level>`, `(?y{w})`, `(?C) (?I) (?L)`
("none occurs in tm-grammars 1.32.22"). Not Oniguruma syntax: `(?P<n>)`, `\Q...\E`, `(?R)`.

## Performance notes

README (Apple M4 Pro): compiled/generated kernels accelerate matching 7x–14x over the interpreter
with 0 B steady-state allocation. First-use: `Parse` + Auto FEC compile + workspace ≈ 262 µs and
169 KB per pattern; interpreted construction 1.4–10 µs. TextMate benchmarks (`bigfile.cs` 5.8 MB):
`element_access` 35.0 ms, `member_ident` 69.6 ms, `\p{Alpha}` identifier 22.8 ms, `keywords`
31.5 ms. OG-005: the work limit differs from Oniguruma's per-start retry limit, so results near
either limit may differ; a 2026-10-24 differential agreed on 88,475,049 searches except 95 native
retry-limit errors.

## Asks from Tint

1. Public kernel-emission façade in `GregsEx.Generator.dll` wrapping the internal
   `SourcePlanner.TryPlan` + `KernelEmitter.EmitClass` + `CoreKernelPlan.Fingerprint()`; proposed
   shape in `docs/design.md` section 8.3, filed as sebastienros/gregsex#26. 0.5.0 (= commit 0510cf29) made the consumption side public
   (`OnigurumaPattern.WithGeneratedKernels`, `CoreKernel`, `KernelGuard`) but not emission.
2. `OnigurumaScanner` search over `ReadOnlySpan<char>` with caller-supplied text identity for the
   result cache.
3. Done: 0.5.0 on nuget.org (2026-10-25) includes PR #25's compiled Oniguruma kernels.
