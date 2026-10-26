# Landscape: TextMateSharp, TextMateLib, shiki

## TextMateSharp (danipen/TextMateSharp, 2.0.4)

- Packages `TextMateSharp` (core, netstandard2.0, C# 8, ~10.3k LOC) and `TextMateSharp.Grammars`.
  Regex engine: Oniguruma through the native `Onigwrap` package (13 RIDs plus wasm static libs).
  A straight port of vscode-textmate via eclipse/tm4e.
- `TextMateSharp.Grammars`: 51 VS Code extension folders copied by hand (no update script; several
  grammars stale since 2022), 74 `.tmLanguage.json` files, 21 themes, all `EmbeddedResource` JSON
  parsed at runtime with an embedded SimpleJSON; `RegistryOptions` parses every `package.json` at
  construction. Each folder keeps VS Code's `cgmanifest.json`; no aggregated third-party notices.
- API: `Registry(IRegistryOptions)`, `LoadGrammar(scope)`, `IGrammar.TokenizeLine(LineText,
  IStateStack, TimeSpan)` → `IToken { StartIndex, EndIndex, List<string> Scopes }`,
  `TokenizeLine2` → `int[]` pairs with vscode's binary metadata, `Theme.Match(scopes)`,
  `Theme.GetColor(id)` (linear scan), `TMModel` background tokenizer model for AvaloniaEdit.
- Internals: `RegExpSourceList` + `CompiledRule { OnigScanner, RuleIds }`, four anchor variants,
  `ResolveBackReferences` via `Regex.Replace` and a native recompile per distinct end, per-grammar
  `lock` around tokenization, `lineText + "\n"` copy per line, `List<string>` scopes per token.
- Tests: ~750 NUnit cases, hand-written expectations, no vscode-textmate suites (the GregsEx branch
  adds them). Benchmark: one BenchmarkDotNet case on `bigfile.cs` (133k lines), no published numbers
  except in PR #134: 5.15 s / 514 MB on .NET 10, M4 Pro.
- Open issues of note: #57 WASM not working (since 2023), #88 MAUI macOS arm64 cannot load
  `libonigwrap`, #119 package validation, #2 XML plist. No `IsAotCompatible`/`IsTrimmable`.

## TextMateLib (jbltx/TextMateLib, 0.2.1)

- A C++17 port of vscode-textmate (file names mirror it) with C FFI, Emscripten/JS, and a C#
  P/Invoke wrapper (`packages/tml-cs`, `net9.0`, legacy `DllImport`, natives for win-x64,
  linux-x64, osx-x64, osx-arm64 only). MIT, single author, first commit 2025-11, last 2026-08.
- Regex: statically linked Oniguruma (`ONIG_ENCODING_UTF8`, `ONIG_SYNTAX_DEFAULT`,
  `ONIG_OPTION_CAPTURE_GROUP`). Trick: `onig_regset_new` + `onig_regset_search(…,
  ONIG_REGSET_POSITION_LEAD)` to scan a rule's patterns in one call, then re-run the winner for
  captures; no per-regex result cache.
- Features: JSON grammars only; VS Code JSON themes parsed but none shipped; injections, embedded
  grammars, balanced brackets ported; scope tokens (`IReadOnlyList<string>`, `PtrToStringAnsi` per
  scope) and vscode-style encoded tokens (`uint` metadata, `ArrayPool` buffer); a C++-only session
  API with per-line caching not exposed to C#; no renderers; no AOT/trim annotations.
- Benchmarks (README, M4 Max): vs vscode-textmate 9.3.2 and shiki 1.29.2 in MB/s on `vscode.d.ts`
  (0.71 MB), jQuery, Bootstrap CSS, `typing.py`. Scope tokenization TS: tml-cs 3.6 / vscode-textmate
  3.7 / native 4.0 MB/s. Themed TS: tml-cs 4.7 / vscode-textmate 3.7 / shiki 1.9 / native 4.9.
  Their conclusion: "throughput is dominated by Oniguruma regex matching, which every engine shares."
  It does not compare with TextMateSharp and does not claim to be the fastest .NET library.

## shiki and textmate-grammars-themes

- `tm-grammars` 1.32.22: 242 grammars + 18 injections from 262 sources (56 from VS Code plus
  community repos); `tm-themes`: 65 themes. Licenses resolved per repository through the GitHub
  API and written into a `NOTICE` file; `cleanup.ts` minifies regexes with `oniguruma-parser`
  (`allowOrphanBackrefs`, `captureGroup`), strips comments, keeps a key whitelist, and verifies
  the sample highlights identically before and after. Index metadata per grammar: `name,
  displayName, categories, scopeName, source, aliases, license, embedded, embeddedIn, byteSize,
  hash`.
- Lazy loading: one module per language (`import('@shikijs/langs/abap')`) exporting the grammar
  plus its embedded languages; heavy embedders (markdown, mdx, vue…) defer embedded grammars.
  Themes likewise one module each.
- Output: `ThemedToken { content, offset, color, bgColor, fontStyle, htmlStyle, explanation }`;
  HTML as `<pre class="shiki" style="background-color;color"><code><span class="line"><span
  style="color:…">`, adjacent same-style tokens merged. Dual themes: default theme inline, others
  as CSS variables (`--shiki-dark:#…`, `--shiki-dark-bg`, optional font-style variables), or
  `light-dark()`.
- Playground (`shiki.matsu.io/play`): language select, theme select split light/dark, a transparent
  textarea over the highlighted output, samples fetched per language, state in localStorage.
- JavaScript engine (`oniguruma-to-es`): transpiles `\h`, `\O`, `\N`, `\R`, `\X`, `\p{…}`, POSIX
  classes, nested classes and `&&`, `\A \z \Z`, `\G` as search start, possessive quantifiers,
  atomic groups, named/relative backrefs, subroutines `\g<…>` and recursion with depth limit 20
  (shiki uses 5), absence repeater, `\K`, flags `i m x D S W`. Unsupported: conditionals, `\y`,
  callouts, Unicode blocks, lookahead inside lookbehind. 237/238 languages supported.
