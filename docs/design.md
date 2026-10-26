# Tint design

Tint is a TextMate syntax colorizer for .NET: grammars and themes from VS Code, a managed regex
engine (GregsEx's Oniguruma flavor), source-generated grammars and themes, NativeAOT and trimming
support, and a tokenizer built for throughput and minimal allocations.

This document records the design decisions and the reasoning behind them. The implementation plan
with its phases lives in [plan.md](plan.md).

## 1. Goals

1. **Performance first.** Fast load, lazy compilation of only what a document reaches, and the
   fastest possible per-line tokenization with near-zero steady-state allocations.
2. **NativeAOT and trimming.** No reflection, no runtime code generation on hot paths, no native
   dependencies. Unused grammars, themes and renderers are trimmed away.
3. **Source-generated grammars and themes.** Users import the `.tmLanguage.json` and theme files
   they need into their own project; a Roslyn source generator turns them into C# classes whose
   regexes are compiled ahead of time by GregsEx. No JSON parsing at runtime, no regex parsing at
   runtime for generated grammars.
4. **VS Code fidelity.** Same tokens as `vscode-textmate` + `vscode-oniguruma` for the same grammar
   and input, verified by the vscode-textmate test suites and differential testing.
5. **A great playground.** A static WebAssembly site, in the spirit of shiki.matsu.io, to try any
   bundled grammar with any bundled theme.

Non-goals for 1.0: semantic highlighting, folding, indentation rules, language configuration
(brackets, comments), `.NET Framework` support.

## 2. What exists today (summary of the research)

| | Regex engine | Grammars/themes | AOT / trim | Notes |
|---|---|---|---|---|
| vscode-textmate (reference, TS) | Oniguruma (wasm) | none bundled | n/a | The algorithm every port copies; 95 tokenization test cases + 72 theme fixtures. |
| TextMateSharp (.NET) | Oniguruma via native Onigwrap | `TextMateSharp.Grammars`: 51 VS Code grammar folders + 21 themes as embedded JSON, parsed at runtime | no annotations, native P/Invoke | Straight port via tm4e. Your open PR #134 replaces Onigwrap with GregsEx: 2.7x–21x faster, byte-identical tokens over 7.3M corpus lines. |
| TextMateLib (jbltx) | Oniguruma, statically linked C++ | none shipped | none; `net9.0` P/Invoke wrapper, 4 RIDs | C++ port of vscode-textmate. One trick: `onig_regset_search` to scan all of a rule's patterns in one call. Claims parity with vscode-textmate, 2x shiki. |
| shiki (JS) | Oniguruma wasm or JS RegExp via `oniguruma-to-es` | `tm-grammars` 242 grammars / `tm-themes` 65 themes, per-language lazy modules | n/a | Best grammar/theme aggregation pipeline (licenses, cleanup, verification). HTML output with dual-theme CSS variables. |

VS Code corpus (sparse clone of `microsoft/vscode/extensions`, 2026-10-25): 84 grammar files
(83 JSON, 1 plist), 19 themes, 17,314 regex patterns, 2.5 MB of pattern text. Constructs that
matter: 1,292 patterns use `\G`, 644 `\A`, 629 `\Z`, 330 `end`/`while` patterns carry
back-references (dynamic patterns), 243 use `\g<…>` subexpression calls (C# and C++ heavily),
1,667 possessive quantifiers, 516 atomic groups, 2,780 lookbehinds, 170 POSIX brackets, 3
conditionals, 2 absent operators. Longest pattern: 282,084 chars (`source.c.platform`).

GregsEx (`origin/main`, previews on Feedz as `0.1.0-preview.N`): `OnigurumaProfile` implements
Oniguruma 6.9.10 default syntax over UTF-16 including every construct above. `OnigurumaScanner`
already implements the vscode-oniguruma multi-pattern search with its per-pattern result cache.
The source generator emits kernels for `static partial OnigurumaPattern` methods marked
`[GeneratedPattern(pattern, OnigurumaOptions)]`; patterns the planner declines (back-references,
subexpression calls, conditionals, absent operator, `\K`) fall back to the interpreter with an
informational diagnostic, never a build error.

## 3. Architecture

```
┌──────────────────────────────────────────────────────────────────────────┐
│ Tint (runtime)                                                           │
│  Grammar model ─► Rule compiler ─► Tokenizer ─► Tokens / EncodedTokens   │
│  Theme model ─► Scope trie ─► StyleAttributes                            │
│  Registry (scope name → IGrammarSource, injections, embedded languages)  │
│  Loaders: JSON / plist grammars, JSON / tmTheme themes (optional path)   │
│  Renderers: HTML (single and dual theme), ANSI                           │
│  Regex layer: GregsEx OnigurumaPattern / OnigurumaScanner                │
├──────────────────────────────────────────────────────────────────────────┤
│ Tint.Generator (Roslyn source generator, shipped inside the Tint package)│
│  <TintGrammar Include="x.tmLanguage.json"/> ─► partial class XGrammar    │
│  <TintTheme Include="dark_plus.json"/>      ─► partial class DarkPlus    │
├──────────────────────────────────────────────────────────────────────────┤
│ Tint.Grammars / Tint.Themes (bundles generated with the same generator)  │
│ samples/Tint.Playground (Blazor WebAssembly, GitHub Pages)               │
└──────────────────────────────────────────────────────────────────────────┘
```

### Packages

| Package | Content |
|---|---|
| `Tint` | Runtime library and the source generator (`analyzers/dotnet/cs`, same convention as GregsEx). Depends on `GregsEx`. Targets `net8.0;net10.0`. |
| `Tint.Grammars` | Code-generated VS Code grammars, all 84 in one assembly with lazily parsed patterns (about 4–7 MB, see section 8.4). |
| `Tint.Themes` | Code-generated VS Code themes (19). Small, always complete. |

Users who need other grammars import the files themselves: the generator is the primary
distribution model, the bundles are a convenience and what the playground uses.

### Namespaces

`Tint` (public API: `GrammarRegistry`, `Tokenizer`, `Token`, `EncodedToken`, `StateStack`,
`Theme`, `Highlighter`), `Tint.Grammars` (model: `RawGrammar`, `RawRule`, `IGrammarSource`),
`Tint.Themes` (`IThemeSource`, `RawTheme`), `Tint.Rendering` (`HtmlRenderer`, `AnsiRenderer`),
`Tint.Loading` (`GrammarLoader`, `ThemeLoader`), `Tint.Internal.*` (rules, scanners, matcher).

## 4. Grammar model and the `IGrammarSource` contract

Every grammar, whatever its origin, is exposed to the tokenizer through one interface:

```csharp
public interface IGrammarSource
{
    string ScopeName { get; }
    GrammarInfo Info { get; }            // language id, aliases, extensions, embeddedLanguages,
                                         // injectTo, injectionSelector, tokenTypes, bracket scopes
    RawGrammar GetGrammar();             // immutable rule tree, built once, cached by the source
}
```

`RawGrammar` mirrors the TextMate JSON structure (`patterns`, `repository`, `injections`,
`captures`, `begin`/`end`/`while`/`match`, `name`, `contentName`, `applyEndPatternLast`), with
two differences that make it cheap to consume:

- Repository lookups are `FrozenDictionary<string, RawRule>`. Include references are
  pre-parsed into a discriminated `IncludeReference` (`$self`, `$base`, `#name`, `scope`,
  `scope#name`) at construction time instead of string parsing during compilation.
- Every regex is a `RegexSource` value: the Oniguruma source text plus an optional
  `PatternFactory` that returns a precompiled `OnigurumaPattern` for each of the anchor variants
  the pattern needs (section 5.2). A JSON-loaded grammar has no factory and parses lazily; a
  generated grammar has one per distinct pattern.

Three implementations:

1. **Generated** (`Tint.Generator`): a `partial class` per grammar. Rule tree built from static
   data on first access; patterns compiled by GregsEx at build time; a
   `FrozenDictionary<string, int>` maps distinct pattern source text to the pattern table index
   (used for de-duplication at generation time, and exposed for diagnostics and the playground).
2. **Loaded** (`Tint.Loading.GrammarLoader`): JSON (System.Text.Json with a source-generated
   context, so it is AOT-safe) and plist. Regexes are parsed on first use with
   `OnigurumaPattern.TryParse`. Same tokens, slower first use, bigger working set.
3. **Custom**: anyone can implement `IGrammarSource`, for example to build a grammar in code.

The rule compiler (`RuleFactory`, a port of vscode-textmate's) works on `RawGrammar` only, so all
three paths share one implementation and one test suite. Cross-grammar includes (`source.js`
inside HTML) are resolved at runtime through the `GrammarRegistry`, never baked into generated code.

## 5. Regex layer

### 5.1 Engine

GregsEx `OnigurumaPattern` with `RegexProfiles.OnigurumaTextMate` (ONIG_OPTION_CAPTURE_GROUP, as
vscode-oniguruma and Onigwrap use) and raised compile limits (TextMateSharp-gregsex uses
`maxPatternUnits 1<<22`, `maxNestingDepth 4096`, `maxInstructions 1<<24`, `maxCaptureGroups 32767`;
objective-c's platform list alone is 282k units). GregsEx types appear in the public surface only
where generated code needs them (`OnigurumaPattern` factories).

One pre-processing rewrite carried over from Onigwrap: `\xHH` (2–8 hex digits) becomes `\x{HH}`,
because Oniguruma reads `\xHH` as an encoded byte, and `\x{7fffffff}` is clamped to `\x{7ffff}`
(the PHP grammar). Everything else is parsed by GregsEx as written.

### 5.2 Anchors `\A`, `\G` and the `\z` rewrite

vscode-textmate feeds each line separately (with `\n` appended), so `\A` would match every line
start and `\G` every search start. It fixes this with four textual variants of any pattern that
contains `\A` or `\G`: a disallowed anchor is replaced by `\` + U+FFFF (never matches).
`allowA` is true only on the document's first line before the first advance; `allowG` is true only
when the search position equals the anchor position (right after the `begin` or `while` that pushed
the current rule). `\z` is rewritten to `$(?!\n)(?<!\n)` so it means the true line end rather than
"before the appended newline". Tint keeps all of this: the generator emits the variants a pattern
actually needs (1,292 + 644 patterns, 4 patterns need all four), everything else gets one.

### 5.3 Dynamic `end` / `while` patterns

330 `end`/`while` patterns reference `begin` captures (`\1`). vscode-textmate substitutes the
escaped captured text and compiles a fresh regex per distinct resolved string. Tint does the same
through the interpreted path: `OnigurumaPattern.TryParse` on the resolved text, cached per rule in
a small bounded LRU keyed by the resolved string (heredoc delimiters and fence languages repeat).
The static part of such a rule (all the nested `begin`/`match` patterns) stays precompiled; only the
end pattern is dynamic, so the rule's scanner is split: static patterns scanner + one dynamic
pattern, merged by the earliest-start rule.

### 5.4 Scanners

A rule's compiled form is a list of patterns searched together: the earliest match wins, ties go to
the lowest index (which is how `applyEndPatternLast` works: the `end` pattern is index 0 by default
and last with the flag). Tint uses GregsEx `OnigurumaScanner` for this, per (rule, anchor variant):

- one matcher and one capture buffer shared by all patterns, allocation-free once warm;
- per-pattern result cache: a search at a later position on the same text reuses the previous
  result when it did not match or matched at or beyond the new position; patterns containing `\G`
  bypass the cache (Onigwrap gets this wrong, vscode-oniguruma gets it right);
- early exit when a match starts at the search position;
- work-limit exhaustion counts as "no match" and is reported on the tokenizer as a flag, the way
  vscode handles Oniguruma's retry limit.

Why not one combined alternation per rule (the RegSet idea)? A backtracking alternation is
position-major: at each position it tries every alternative before advancing. Pattern-major
scanning lets each pattern use its own prefilter (`SearchValues`, literal and first-set filters)
to skip most of the line, and the result cache makes later searches on the same line free. This
is what the measurements on the TextMateSharp branch were made with, and it is where GregsEx
invests. A combined-alternation experiment stays on the list for after the first benchmarks.

### 5.5 Three execution tiers for a pattern

The kernel-size spike ([research/kernel-spike.md](research/kernel-spike.md)) measured what
ahead-of-time kernels cost and buy for grammar patterns: 3–11 KB of IL per pattern (60–100 MB for
the VS Code corpus), 5–20 minutes of clean build dominated by the ILLink trim analyzer, 1.2 ms of
JIT per kernel on first use, for a 1.33× geometric-mean speedup over all C# patterns (2.5× on the
hottest). Most searches in a tokenizer fail in the prefilters, which the interpreter shares. So Tint
treats pattern execution as three tiers:

1. **Interpreted** (default everywhere): `OnigurumaPattern.Parse` on first use, 18 µs per pattern,
   280 bytes of IL per pattern in a generated grammar. This is the tier the TextMateSharp branch
   measured its 1.4×–22× over Onigwrap with.
2. **Runtime-compiled, tiered** (JIT runtimes only, opt-in): with
   `TokenizerOptions.CompileHotPatterns`, a rule scanner that has been searched
   `HotPatternThreshold` times (default 256; 0 compiles when the rule is first reached) swaps its
   patterns for `OnigurumaPattern.Compile()`d ones (GregsEx's FastExpressionCompiler path). The
   count is per rule scanner, since one search runs every pattern of the rule; the compiled pattern
   is cached on the pattern's `CompiledRegex` and shared by every tokenizer of the registry, a
   pattern GregsEx declines stays interpreted, and a scanner created later starts with the patterns
   already compiled. Guarded by `RuntimeFeature.IsDynamicCodeSupported` and `IsDynamicCodeCompiled`
   and by GregsEx's `GregsEx.RuntimeCompilation.Auto` switch (declared as a feature switch, so the
   trimmer folds it to false when GregsEx's targets turn it off for `PublishTrimmed`/`PublishAot`);
   `PlatformNotSupportedException` and `NotSupportedException` keep the interpreted pattern.
   **Measured in P5 (GregsEx 0.5.1, M4 Pro, [benchmarks/README.md](../benchmarks/README.md)): off
   by default.** Once warm, compiling every reached pattern gains 5% on `bigfile.cs` (1.79 s →
   1.70 s) and 3% on `medium.cs`, and loses 20–26% on the docs-heavy mix (16.1 ms → 19.3–20.3 ms);
   from a cold registry it costs 0.4 s on `bigfile.cs`, 0.25–0.46 s on `medium.cs` and about 1 s on
   the docs mix, with 2–4× the allocations. N = 256 compiles the fewest patterns for the same warm
   C# gain, so it is the default threshold for users who opt in for long-running C#-heavy
   workloads. Revisit when GregsEx removes the per-call fixed cost of compiled kernels (section 15,
   item 4).
3. **Ahead-of-time kernels** (opt-in): the generator emits kernels only when asked (`Kernels = true`
   on the attribute, or `<TintKernels>true</TintKernels>`), through GregsEx's `KernelSource`
   façade (section 8.3). Meant for AOT and WebAssembly deployments of a few hot grammars, and for
   future opt-in per-family kernel packages, not for the bundles.

### 5.6 Text identity for the result cache (GregsEx ask)

`OnigurumaScanner`'s cache keys text identity on the `string` behind a `ReadOnlyMemory<char>`, so
`char[]`-backed memory disables it. TextMateSharp-gregsex therefore allocates `line + "\n"` as a
string per line (one allocation per line, and the largest remaining one). Tint wants to tokenize
from `ReadOnlySpan<char>` into a reusable buffer with zero per-line allocation, so it needs a
scanner overload where the caller supplies the text identity (a line generation counter) instead
of relying on reference equality. Fallback until then: the per-line string.

## 6. Tokenizer

A port of `vscode-textmate`'s `tokenizeString`, kept semantically identical (the research report
in [research/vscode-textmate.md](research/vscode-textmate.md) is the porting reference) and
restructured for allocations:

- **`StateStack`** is the immutable linked list vscode uses (parent, ruleId, enterPos, anchorPos,
  depth, beginRuleCapturedEOL, resolved end rule, name scopes, content-name scopes). Editors keep
  it per line and stop re-tokenizing when a line's resulting state equals the stored one. Equality
  is structural and cached by hash.
- **`AttributedScopeStack`** carries the scope name and the encoded token attributes
  (language id, token type, font style, foreground, background) resolved against the theme once,
  when the scope is pushed. Children are cached per node so re-pushing the same scope from the
  same parent allocates nothing (the branch measured this as a major win).
- **Captures**: capture indices live in a `Span<RegexCapture>` rented from per-depth pools reset
  before each scan; `handleCaptures` uses a struct local stack; capture `patterns` retokenize the
  line prefix recursively as vscode does.
- **`while` rules** are checked at line start, outermost first, popping the stack at the first
  failure.
- **Injections**: selector matcher ported from `matcher.ts` (`L:`/`R:` priorities, `-` negation,
  parentheses, scope paths), evaluated against the content scope names; ties go to the grammar
  rule unless the injection has `L:` priority.
- **Loop guards** [1]–[4] from vscode-textmate (push/pop without advancing) are kept verbatim.
- **Time budget**: an absolute timestamp deadline checked between searches, plus GregsEx's own
  per-search work limit as the real guard against pathological lines.
- **Thread model**: a `Tokenizer` instance is not thread-safe (it owns the scratch buffers). It is
  cheap to create; compiled rules and scanners are shared per grammar and are immutable once built.
  Compilation uses a lock-free publish (first writer wins) so parallel tokenizers never block on
  each other after warm-up.

### 6.1 Output

```csharp
public readonly record struct Token(int Start, int Length, AttributedScopeStack Scopes);
public readonly record struct EncodedToken(int Start, uint Metadata); // vscode binary layout
```

`TokenizeLine(ReadOnlySpan<char> line, StateStack state, out StateStack next)` returns a
`ReadOnlySpan<Token>` (or `ReadOnlySpan<EncodedToken>`) over a buffer owned by the tokenizer,
valid until the next call. No `List<string>` of scope names per token: scopes are the shared
immutable `AttributedScopeStack`, and `Scopes.GetScopeNames()` materializes strings only on
request. The encoded form uses vscode's bit layout (language id 8 bits, token type 2, balanced
bracket 1, font style 4, foreground 9, background 9), so Monaco-style consumers can reuse it, and
adjacent tokens with equal metadata are merged exactly as vscode does.

## 7. Themes

A port of vscode-textmate `theme.ts`: `tokenColors` rules parsed into (scope, parent scopes,
font style, foreground, background), resolved in selector order, inserted in a trie keyed by scope
segments, matched by walking segments as deep as possible then picking the most specific rule whose
parent scopes match the scope stack (`>` means immediate parent). Colors are interned in a
`ColorMap` (1-based ids; 0 = not set) so a token's colors are two array lookups.

Theme files use `include` (`dark_plus.json` includes `dark_vs.json`); the generator and the loader
flatten includes. UI colors (`colors`: `editor.background`, `editor.foreground`, …) are kept for
renderers.

Generated themes are static data: the resolved rule array, the color table and the UI color
dictionary as literals; the trie is built on first use (microseconds). `IThemeSource` is the
common interface, with the same three implementations as grammars.

## 8. Source generator

### 8.1 User model

Everything is a Roslyn incremental source generator shipped inside the `Tint` package. There is no
command-line tool, no MSBuild task and no build step outside the compiler. Two ways to drive it:

**Attribute-driven** (the `[GeneratedRegex]` style, preferred):

```csharp
[TextMateGrammar("grammars/csharp.tmLanguage.json")]
public partial class CSharpGrammar;

[TextMateTheme("themes/dark_plus.json")]
public partial class DarkPlusTheme;
```

```xml
<ItemGroup>
  <PackageReference Include="Tint" Version="…" />
  <TintGrammar Include="grammars/csharp.tmLanguage.json" />
  <TintTheme Include="themes/dark_plus.json" />
</ItemGroup>
```

The item types (defined by `Tint.props`, packed under `build/` and `buildTransitive/`) only make the
files visible to the compiler as `AdditionalFiles`; the attribute decides the class name, namespace,
accessibility and which file it binds to (matched by path relative to the project, with a diagnostic
when no AdditionalFile matches). The attributes are emitted into the consumer as `internal` types
by the generator, so they add no runtime dependency.

**Item-only**: a `TintGrammar` item without a matching attribute still produces a class, named from
the grammar (`CSharpGrammar` for `source.cs`, overridable with `Name` and `Namespace` metadata), in
the project's root namespace. This is the zero-code path for the bundles and quick starts.

Either way the generator emits `partial class CSharpGrammar : IGrammarSource` with a static
`Instance`. Grammars that include other grammars (`source.js` from HTML) get a diagnostic listing
the scopes to register alongside, and `GrammarInfo.Dependencies` lists them.

Supported inputs: `.tmLanguage.json` (JSON with comments), `.tmLanguage` / `.plist`, themes as
`.json` (VS Code, with `include`) and `.tmTheme`. The generator and `Tint.Loading` share one
reading of the files: the generator compiles `GrammarLoader.ReadGrammar`, the theme reader,
`DocumentTree`, the `RawGrammar`/`RawTheme` model and `PatternRewriter` from `src/Tint` under
`TINT_GENERATOR`, with a small hand-written JSON reader (comments, trailing commas) in place of
System.Text.Json, which analyzer hosts cannot load reliably. Items, metadata and diagnostics are
documented in [generator.md](generator.md).

### 8.2 Generated code shape

```csharp
partial class CSharpGrammar : IGrammarSource
{
    public static CSharpGrammar Instance { get; } = new();
    public string ScopeName => "source.cs";
    public GrammarInfo Info => s_info;                       // static readonly literals
    public RawGrammar GetGrammar() => s_grammar ??= Build(); // object initializers, lazily

    // One entry per distinct pattern text (de-duplicated across the grammar), per anchor variant
    // actually needed, texts stored as one UTF-8 literal. Parsed on first use with the TextMate
    // compile limits (tier 1); with Kernels opted in, the entry also binds the emitted kernel
    // classes (tier 3). Each RegexSource gets a factory mapping its anchor variants to entries.
    private static OnigurumaPattern __TintPattern(int index) => …; // Parse(Text(index), …) once
    …
    public static FrozenDictionary<string, int> PatternIndex => …; // pattern text → index
}
```

Design choices: no reflection; every pattern is validated at build time (an invalid regex is a
build error with its position in the grammar file) but parsed at first use, so a grammar whose heavy
rules are never reached never pays for them; de-duplication by pattern text within an assembly (the
C/C++ family shares most of its 1,000+ patterns across four grammars). The `PatternIndex`
dictionary is the lookup surface the design calls for; the hot path binds rules to patterns by index
at generation time and never hashes strings.

### 8.3 Chaining with GregsEx (the one hard dependency)

A generator's main-pipeline output is never visible to other generators (only post-initialization
output is, and that step cannot read `AdditionalFiles`; see
[research/generator-chaining.md](research/generator-chaining.md) for the experiment). A grammar
class depends on its grammar file, so it is main-pipeline output, and the `[GeneratedPattern]`
methods it would carry are invisible to GregsEx's generator. Two options, both planned:

1. **Preferred:** GregsEx exposes a public kernel-emission façade in `GregsEx.Generator.dll`.
   Implemented by [gregsex#27](https://github.com/sebastienros/gregsex/pull/27) (after 0.5.0):
   `GregsEx.SourceGeneration.KernelSource.TryEmit(pattern, OnigurumaOptions, plainClassName,
   captureClassName, OnigurumaCompileOptions?)` returning `KernelSourceResult { Status
   (Succeeded | Declined | Invalid), Fingerprint, GroupCount, PlainClass, CaptureClass,
   DeclineReason, Diagnostic }`. The emitted classes go inside the consumer type in a file that
   starts with `using System;`, and the pattern must be parsed at runtime with the same options and
   compile limits. The caller also has to check, as `PatternGenerator` does, that the consumer
   targets .NET 8 or later (`System.MemoryExtensions.IndexOfAnyInRange` exists in the compilation).
   The shape as proposed was:

   ```csharp
   namespace GregsEx.SourceGeneration;

   public static class KernelSource
   {
       // Succeeded: emit PlainClass (+ CaptureClass when the pattern has groups) and bind with
       //   pattern.WithGeneratedKernels(Fingerprint, <plainClass>.Run, <captureClass>.Run).
       // Declined: the planner left the pattern to the interpreter; DeclineReason says why.
       // Invalid: Diagnostic carries the parse error (code, offset, length, message).
       public static KernelSourceResult TryEmit(
           string pattern, OnigurumaOptions options, string plainClassName, string captureClassName,
           OnigurumaCompileOptions? compileOptions = null);
   }

   public sealed class KernelSourceResult
   {
       public KernelSourceStatus Status { get; }      // Succeeded | Declined | Invalid
       public ulong Fingerprint { get; }
       public string? PlainClass { get; }             // C# source of a static class with Run(...)
       public string? CaptureClass { get; }           // null when GroupCount == 1
       public string? DeclineReason { get; }
       public RegexDiagnostic Diagnostic { get; }
   }
   ```

   How Tint's generator reaches it without a compile-time reference: it finds the compilation's
   `GregsEx.dll` metadata reference, which NuGet places at `<package>/lib/<tfm>/GregsEx.dll`,
   derives `<package>/analyzers/dotnet/cs/GregsEx.Generator.dll` from it, loads that assembly once
   (reusing the copy the compiler already loaded for GregsEx's own generator when it can), and calls
   the façade through a small reflection shim bound to the public signature above, with
   `OnigurumaOptions.CaptureGroup` and `OnigurumaCompileOptions.TextMate` read from the loaded
   assembly. A `TintGregsExPackagePath` compiler-visible property overrides the discovery (a project
   reference to GregsEx, a custom layout). This needs no `GeneratePathProperty` on the consumer's
   package reference, which a transitive reference through Tint would not have. Kernels are
   therefore always emitted by the exact GregsEx version the consumer runs, so the fingerprint
   always matches and no copy of `GregsEx.Generator.dll` ships inside Tint. Every pattern goes
   through `TryEmit` as build-time validation, kernels or not (an `Invalid` status is a build error
   with the rule path and offset). If GregsEx cannot be located, the façade is absent (older
   GregsEx) or any reflection step fails, Tint reports an informational diagnostic, skips
   validation and kernels, and emits the fallback below.
2. **Fallback, available today:** generated pattern methods call `OnigurumaPattern.Parse` lazily.
   Still AOT-safe, still no JSON, interpreted matching speed (the speed the TextMateSharp branch
   measured). The Phase 2 spike measures both.

The bundles (`Tint.Grammars`, `Tint.Themes`) are built with the same generator as user projects,
through `TintGrammar`/`TintTheme` items over `grammars/` and `themes/`. Until option 1 lands they
run on the interpreter like everyone else; when it lands they pick up kernels with no change to this
repository beyond a GregsEx version bump. No second code path, no tool.

### 8.4 Size budget (decided by the spike)

Measured in [research/kernel-spike.md](research/kernel-spike.md) on GregsEx 0.5.0 for the C#,
TypeScript and C++ grammars:

| | Kernels | Lazy `Parse` |
|---|---|---|
| IL per pattern | 3.0 KB (C#) to 10.7 KB (C++) | 280 B |
| C# grammar assembly | 1,013 KB | 95 KB |
| Clean build, C# grammar | 10–12 s (87–93% ILLink trim analyzer) | 0.7–1.4 s |
| First search of every C# pattern | 417 ms (JIT, 1.2 ms per kernel) | 6 ms |
| Search speedup, all C# patterns, geometric mean | 1.33× (2.5× on the 30 hottest; 28 patterns slower) | 1× |
| Full corpus extrapolation | 60–100 MB, 5–20 min build | 4–7 MB, seconds |

Decisions: `Tint.Grammars` ships all 84 VS Code grammars in **one assembly** with lazily parsed
patterns (about 4–7 MB; a trimmed app keeps only the grammar classes it references). No pattern
length threshold on Tint's side: GregsEx already declines patterns above roughly 940 characters.
Kernels are opt-in (section 5.5); per-family kernel packages come only when a tokenizer-level
benchmark shows a gain worth their size, after GregsEx addresses the spike's findings (shared
`SearchValues`/bitmap tables between kernels, per-call surrogate scan overhead, `AggressiveOptimization`
blocking ReadyToRun, trim-analyzer cost). Blazor lazy-loading per language is therefore not needed
for the bundle; it stays an option for kernel packages.

## 9. Bundles and sourcing

`tools/Tint.Sync` is a repository maintenance tool (the equivalent of vscode's `update-grammar`
scripts and shiki's sync scripts), never part of a package. It fetches grammars and themes from a
pinned `microsoft/vscode` commit into
`grammars/<extension>/` and `themes/` with each extension's `cgmanifest.json` (upstream
repository, commit, license) and generates `THIRD-PARTY-NOTICES.txt` from them (45 MIT, 11
TextMate Bundle License, 1 BSD, 1 Apache-2.0 among the grammar registrations). The VS Code
extension `package.json` provides language ids, aliases, extensions, `embeddedLanguages`,
`injectTo`, `tokenTypes` and bracket scopes for `GrammarInfo`. A later addition can pull from
shiki's `textmate-grammars-themes` for languages VS Code does not ship.

## 10. Renderers and the facade

`Highlighter` is the two-line API: `registry.CreateHighlighter("source.cs", theme)` (or `(scope,
light, dark)`, or a default theme plus named themes), then `ToHtml(code)` / `ToAnsi(code)` /
`Tokenize(code)`. It owns one `Tokenizer` per theme, so like a tokenizer it is not thread-safe: one
per thread, reused. `Tokenize` returns one `HighlightedToken[]` per line (`Start`, `Length`, `Color`,
`BackgroundColor`, `FontStyle`, `Scopes`) with the colors the tokenizer resolved.

The renderers (`Tint.Rendering`) are immutable, thread-safe objects built from themes and options;
a render takes the code and the tokenizers (one per theme, checked by reference) and writes to a
`TextWriter`, an `IBufferWriter<char>` or a `StringBuilder` through a generic struct sink, so
every write is a direct call and no per-token string exists. The string overloads write into an
`ArrayPool` buffer, so the result string is the only allocation of a warm call. Both render from
the encoded path (`TokenizeLineEncoded`): its tokens are already merged by metadata. Lines end at
`\n` or `\r\n`; a newline at the very end terminates the last line instead of starting an empty
one (HTML has no trailing empty line; ANSI output ends with a newline when the input does).

- **`HtmlRenderer`** writes shiki's shape: `<pre class="tint" style="background-color:…;color:…"
  tabindex="0"><code>`, one `<span class="line">` per line separated by `\n`, and one
  `<span style="…">` per run of tokens with the same style, holding only what differs from the
  theme's defaults (`color`, `background-color`, `font-style:italic`, `font-weight:bold`,
  `text-decoration:underline|line-through`); unstyled runs are bare text. Text is escaped (`&`,
  `<`, `>`). Colors are the theme's, lower-cased. Options: the `pre` class, `CssClasses` mode
  (`tint-fg-<id>`, `tint-bg-<id>`, `tint-i`/`b`/`u`/`s`, with `GetStyleSheet()` emitting the
  theme's rules), `data-line` line numbers (off by default), class and variable prefixes.
- **Several themes** (at most eight): each theme is tokenized by its own tokenizer over the same
  lines and the token boundaries are zipped, so runs split wherever any theme's style changes. The
  first theme is written inline and the others as CSS variables named after them, theme by theme as
  shiki does: `--tint-dark`, `--tint-dark-bg`, `--tint-dark-font-style`, `--tint-dark-font-weight`,
  `--tint-dark-text-decoration`, with `inherit` for a property one theme sets and another does not
  (custom properties inherit, so a span without a variable takes the `pre`'s). One CSS rule switches
  themes (see the `HtmlRenderer` remarks and the README). CSS-class mode is single-theme.
- **`AnsiRenderer`** writes 24-bit SGR: `ESC[38;2;r;g;bm` for every token's foreground (the
  theme's default included, since the theme's palette assumes its background), `ESC[48;2;…m`
  when `Background` is on, `1`/`3`/`4`/`9` for bold, italic, underline and strikethrough, a reset
  when an attribute turns off, and `ESC[0m` at the end of every styled line.

## 11. Playground

`samples/Tint.Playground`: Blazor WebAssembly, published to GitHub Pages by a workflow. Language
and theme pickers, a code box, live HTML output, token inspector (scopes and matched theme rule per
token, like shiki's `explanation`), permalink state in the URL. Grammar bundles are loaded lazily
with `LazyAssemblyLoader` when they are split per language family, which is the Blazor equivalent
of shiki's per-language modules.

## 12. Performance plan

`benchmarks/Tint.Benchmarks` (BenchmarkDotNet, `[MemoryDiagnoser]`, `[ShortRunJob]`):

- Same workloads as the TextMateSharp branch so numbers are comparable: `bigfile.cs` (133k lines),
  `DocsHeavy` (40 fenced blocks per language), per-sample files (csharp, typescript, json, html,
  yaml, markdown, cpp).
- Baselines: TextMateSharp 2.0.4 (Onigwrap), TextMateSharp with GregsEx (the PR #134 branch,
  as a package when available), TextMateLib 0.2.1 (native, x64/arm64 only).
- Metrics: time per line, MB/s, allocated bytes per line, first-tokenize latency (load + compile),
  startup under NativeAOT.
- Targets: beat the TextMateSharp+GregsEx branch on every workload; beat TextMateLib's themed
  throughput (4.7 MB/s on `vscode.d.ts` with TypeScript) by a wide margin; zero allocations per
  line in steady state for the encoded-token API.

## 13. Correctness plan

- vscode-textmate suites: `first-mate/tests.json` (64), `suite1/tests.json` (22),
  `suite1/whileTests.json` (9), the themes suite (72 files × 13 themes via `tokenizeLine2`).
  Grammars loaded through `Tint.Loading`, then the same suites through generated classes to prove
  the two paths agree.
- Differential testing against TextMateSharp 2.0.4 over a corpus of real files per grammar (the
  branch's `CorpusCheck` approach: 21,364 files, 8 known divergences, all explained).
- Fuzzing harness (the branch's `Fuzz` tool pattern) comparing tokens, time and exceptions.
- AOT smoke (`test/Tint.AotSmoke`) and trim smoke published on linux-x64, linux-arm64, win-x64,
  osx-arm64 in CI, as GregsEx does.

## 14. AOT and trimming

`IsAotCompatible` and `IsTrimmable` on every shipped assembly; `EnableTrimAnalyzer`,
`EnableSingleFileAnalyzer`. Loaders use a System.Text.Json source-generated context. No
`Type.GetType`, no `Activator`, no `dynamic`. Generated grammars are plain static classes, so a
trimmed app keeps only the grammars it references. The playground is the proof: published with
`RunAOTCompilation`, trimmed, lazy-loaded.

## 15. Open items and asks on GregsEx

1. Done in GregsEx 0.5.1 ([gregsex#27](https://github.com/sebastienros/gregsex/pull/27)): the
   `KernelSource` façade (issue [#26](https://github.com/sebastienros/gregsex/issues/26)).
2. Done in GregsEx 0.5.1: `OnigurumaCompileOptions.TextMate`, the compile limits TextMate grammars
   need. Tint uses it for every runtime `Parse` and passes it to `KernelSource.TryEmit`, so the
   fingerprint of an emitted kernel always matches the pattern the runtime parses.
3. `OnigurumaScanner` search over `ReadOnlySpan<char>` with caller-supplied text identity for the
   result cache (section 5.6).
4. From the kernel spike, for kernels to become worth their size: share `SearchValues`/bitmap
   tables between a pattern's plain and capture kernels and across an assembly; remove the per-call
   fixed cost (whole-input surrogate scan, guard setup) that makes trivial patterns slower than the
   interpreter; reconsider `AggressiveOptimization` on `Run` (blocks ReadyToRun, 1.2 ms JIT per
   kernel); generated code the ILLink analyzer can skip; lowering of nullable-body loops and
   subexpression calls (the main decline reasons in C# and C++). P5 confirmed it at the tokenizer
   level for runtime-compiled patterns too: 5% faster on C# once warm, slower on short documentation
   blocks.
5. Done: GregsEx 0.5.0 on nuget.org includes the compiled Oniguruma kernels; Tint pins it.

Decisions deferred to measurements: combined-alternation experiment (5.4). Decided by P5: tier 2
is opt-in, threshold 256 (5.5). Decided by the spike: bundle scope and partitioning, no
kernel length threshold (8.4).

## 16. References

- Research notes: [docs/research/](research/) (TextMateSharp, TextMateLib and shiki,
  vscode-textmate algorithm and VS Code corpus, GregsEx audit, the TextMateSharp-gregsex branch).
- vscode-textmate: https://github.com/microsoft/vscode-textmate
- VS Code grammars and themes: https://github.com/microsoft/vscode/tree/main/extensions
- GregsEx: https://github.com/sebastienros/gregsex
- shiki textmate-grammars-themes: https://github.com/shikijs/textmate-grammars-themes
