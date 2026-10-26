# Changelog

All notable changes to the Tint packages. Versions follow [Semantic Versioning](https://semver.org); the
packages always ship together with the same version.

## 0.1.2 (2026-10-26)

### Tint

- GregsEx 0.5.3: binding a generated kernel (every pattern of a grammar generated with `Kernels="true"`, on its first
  use in each registry) re-plans the pattern to check its fingerprint, and 0.5.2's new analyses had made that five
  times dearer (the C# grammar's 547 patterns: 37 ms in 0.5.1, 170-200 ms in 0.5.2, 38-52 ms now). A native AOT tool
  highlighting one page with four kernel grammars paid 350 ms more with 0.1.1; it is back to the 0.5.1 cost. Grammars
  generated with `Kernels="true"` must be rebuilt against 0.5.3 (the fingerprint changed).

### Tint.Grammars, Tint.Themes

- No content change; shipped with the same version.

## 0.1.1 (2026-10-26)

### Tint

- GregsEx 0.5.2: ahead-of-time kernels now cover patterns with subexpression calls (`\g<name>`, recursion included),
  which are the VS Code C# grammar's type-name, generic, tuple and declaration patterns, and every kernel gained the
  interpreter's alternative skipping and possessive runs. With `Kernels="true"` the C# grammar gets kernels for 327 of
  its 338 patterns (293 before) and tokenizes TextMateSharp's `bigfile.cs` in 0.75 of the interpreted time (1.34 s
  against 1.79 s on an Apple M4 Pro), where 0.5.1 gained 3-4%. Runtime-compiled hot patterns
  (`TokenizerOptions.CompileHotPatterns`) gain the same. Grammars generated with `Kernels="true"` must be rebuilt
  against 0.5.2: a kernel of another GregsEx revision is ignored at run time and the pattern runs interpreted.

### Tint.Grammars, Tint.Themes

- No content change; shipped with the same version.

## 0.1.0 (2026-10-26)

First public release.

### Tint

TextMate syntax colorization for .NET 8 and .NET 10, without native dependencies, trimming and NativeAOT safe.

- A port of [vscode-textmate](https://github.com/microsoft/vscode-textmate) 9.3.2's tokenizer (`Tokenizer`,
  `StateStack`, injections, `while` rules, captures with retokenization, back-referenced `end`/`while` patterns, time
  budget), over [GregsEx](https://github.com/sebastienros/gregsex)'s Oniguruma flavor. Every case of the
  vscode-textmate test suites passes, through runtime-loaded and generated grammars alike.
- `TokenizeLine` (ranges and scope stacks) and `TokenizeLineEncoded` (vscode-textmate's binary `tokenizeLine2`
  layout, with token types and balanced bracket flags from `GrammarInfo`). No allocation per line once warm.
- `GrammarRegistry`: grammars, embedded languages, injections, missing-grammar reporting, tokenizers and
  highlighters.
- Themes: VS Code JSON color themes (with `include`) and `.tmTheme` files, resolved as VS Code does
  (`ThemeLoadMode.VSCode`) or as written (`ThemeLoadMode.Raw`); `Theme.FromSource`, `Theme.Match`, `ColorMap`.
- `GrammarLoader` and `ThemeLoader` read JSON and plist files at runtime.
- `Highlighter` with shiki-compatible HTML (`HtmlRenderer`: inline styles or CSS classes, line numbers, a light and
  a dark theme or more through CSS variables) and 24-bit ANSI output (`AnsiRenderer`), to strings, `TextWriter`,
  `IBufferWriter<char>` or `StringBuilder`.
- The Tint source generator, in the package: `<TintGrammar>`/`<TintTheme>` items and `[TextMateGrammar]`/
  `[TextMateTheme]` attributes turn grammar and theme files into classes at build time; every regex is validated
  then (an invalid pattern is a build error with its position), patterns are parsed on first use, and hot grammars
  can opt into ahead-of-time compiled GregsEx kernels (`Kernels`).
- Opt-in tier-2 compilation of hot patterns at runtime (`TokenizerOptions.CompileHotPatterns`).
- Themes whose file has no `name` take it from the `DisplayName` item metadata or attribute property (VS Code labels
  Monokai, Monokai Dimmed and Tomorrow Night Blue from their extension's `package.json`).

### Tint.Grammars

The 84 grammar files of Visual Studio Code's built-in extensions (51 extensions, synced from `microsoft/vscode`
`3ac0f056`), generated with Tint's source generator: one class per grammar (`CSharpGrammar.Instance`),
`TintGrammars.CSharp`-style properties, `TintGrammars.All` and `TintGrammars.Find(scopeName)`. Trimming keeps only
the grammars an application references. Licenses in `licenses/THIRD-PARTY-NOTICES.txt`.

### Tint.Themes

The 19 color themes of Visual Studio Code (Dark+, Light+, Dark Modern, the 2026 themes, high contrast, Monokai,
Solarized, Abyss and the others), generated the same way: `TintThemes.DarkPlus`, `TintThemes.All`,
`TintThemes.Find(name)`.

### Tint.Grammars.Sources and Tint.Themes.Sources

File-only development dependencies with the same VS Code grammar and theme files, for applications that generate
their own classes: `$(TintGrammarsSourcesPath)`/`$(TintThemesSourcesPath)` point at the files, `@(TintGrammarsSourcesAll)`
and `@(TintThemesSourcesAll)` list them with the bundles' metadata, and the generator compiles only the ones an
application includes, with its own names and kernels. Nothing ships with the application.

### Verification

- vscode-textmate suites: 95 tokenization cases and the 72-file themes suite, through the loader and generated paths.
- Differential corpus check against TextMateSharp 2.0.4 over 10,345 files and 3.3 million lines, every difference
  judged by vscode-textmate ([report](docs/research/corpus-check-2026-10-25.md)), and a 20-minute differential fuzz
  campaign over the 84 grammars ([report](docs/research/fuzz-2026-10-25.md)).
- Trimmed and NativeAOT smoke apps on linux-x64 and osx-arm64.
