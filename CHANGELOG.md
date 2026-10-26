# Changelog

All notable changes to the Tint packages. Versions follow [Semantic Versioning](https://semver.org); the three
packages always ship together with the same version.

## 0.1.0 (preview)

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

### Tint.Grammars

The 84 grammar files of Visual Studio Code's built-in extensions (51 extensions, synced from `microsoft/vscode`
`3ac0f056`), generated with Tint's source generator: one class per grammar (`CSharpGrammar.Instance`),
`TintGrammars.CSharp`-style properties, `TintGrammars.All` and `TintGrammars.Find(scopeName)`. Trimming keeps only
the grammars an application references. Licenses in `licenses/THIRD-PARTY-NOTICES.txt`.

### Tint.Themes

The 19 color themes of Visual Studio Code (Dark+, Light+, Dark Modern, the 2026 themes, high contrast, Monokai,
Solarized, Abyss and the others), generated the same way: `TintThemes.DarkPlus`, `TintThemes.All`,
`TintThemes.Find(name)`.

### Verification

- vscode-textmate suites: 95 tokenization cases and the 72-file themes suite, through the loader and generated paths.
- Differential corpus check against TextMateSharp 2.0.4 over 10,345 files and 3.3 million lines, every difference
  judged by vscode-textmate ([report](docs/research/corpus-check-2026-10-25.md)), and a 20-minute differential fuzz
  campaign over the 84 grammars ([report](docs/research/fuzz-2026-10-25.md)).
- Trimmed and NativeAOT smoke apps on linux-x64 and osx-arm64.
