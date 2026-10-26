# Known differences from vscode-textmate

Cases of the vscode-textmate suites that Tint cannot pass because of a genuine difference between GregsEx and
vscode-oniguruma. Each entry gives the suite, the case, the pattern involved and the exact reason; the test is
skipped with the same reason (`KnownDifferences` in `test/Tint.Tests/TokenizationSuiteTests.cs`).

None at the moment: all 95 tokenization cases (`first-mate/tests.json`, `suite1/tests.json`,
`suite1/whileTests.json`) and all 72 files of the themes suite pass through the loader path.

## Deliberate API-level choices

These do not change tokens as the suites compare them, but differ from vscode-textmate's raw output:

- `Token.Length` stops at the end of the line: vscode-textmate's last token can extend over the `\n` it appends.
- `ThemeLoader` reads VS Code JSON color themes as VS Code does before handing rules to vscode-textmate
  (`ThemeLoadMode.VSCode`, the default): the global entries of tmTheme-style settings (a legacy `settings` array,
  or the `.tmTheme` that `tokenColors` points at) become `editor.foreground` / `editor.background` unless the
  file's `colors` sets them; a default rule built from those colors, or from the theme type's defaults
  (`#BBBBBB`/`#1E1E1E` dark, `#333333`/`#FFFFFF` light, `#FFFFFF`/`#000000` high contrast, `#292929`/`#FFFFFF`
  high contrast light), always comes first; every other scopeless rule is dropped. `.tmTheme` files are read as
  written. The themes suite loads its fixtures with `ThemeLoadMode.Raw`, since vscode-textmate's tests give the
  files to the tokenizer as written (`light_vs.json` has neither a type nor a global rule, so VS Code's defaults
  would change its colors).
- Encoded token attributes hold 255 language ids, 511 foreground and 255 background color ids. vscode-textmate
  masks larger ids silently; Tint throws `InvalidOperationException` when a registry or theme would exceed them.
- `GrammarInfo.BalancedBracketScopes` defaults to `*`, VS Code's default, when a grammar declares none.

## Against vscode-textmate, found by the corpus check and the fuzzer

- GregsEx's work limit: a search that exhausts it counts as no match (design section 5.4), where Oniguruma's own retry
  limit is higher or not reached. One fuzz input hits it: after a 7,548-character line of `:::` in C, a function
  pattern gives up and `Py_DECREF(data);` reads as a definition instead of a call
  ([fuzz-2026-10-25.md](fuzz-2026-10-25.md)). `Tokenizer.LimitExceededCount` counts these searches. No corpus file
  reaches the limit.
- Registry contents, not a tokenizer difference: vscode-textmate loads the grammars a grammar depends on from its
  patterns and includes, not from its captures, so a grammar referenced only in a capture (`source.js` in
  CoffeeScript's backticks) is missing unless something else loaded it. Tint resolves every registered grammar.
  With all grammars loaded, vscode-textmate and Tint agree.

## Against TextMateSharp 2.0.4

`tools/Tint.CorpusCheck` and `tools/Tint.Fuzz` compare Tint with TextMateSharp 2.0.4 (Onigwrap) and settle each
difference with vscode-textmate 9.3.2 over vscode-oniguruma (`tools/Tint.CorpusCheck/referee`). Every difference
below is TextMateSharp diverging from vscode-textmate; in each, vscode-textmate tokenizes like Tint. Numbers are from
[corpus-check-2026-10-25.md](corpus-check-2026-10-25.md) and [fuzz-2026-10-25.md](fuzz-2026-10-25.md).

### What the checkers adapt for TextMateSharp

So that differences come from the tokenizers and not from file reading, the reference gets the grammar and theme
files in a form it can read. None of these changes what vscode-textmate does with the files:

- Strings among `captures` (VS Code's XML grammar has `"name"` and `"end"` inside a `captures` object, the YAML
  grammars a `"comment"`): vscode-textmate skips keys that are not capture numbers, TextMateSharp casts every value
  to a rule (`InvalidCastException` in `Raw.TryGetObject`, which also breaks every grammar that embeds XML or YAML:
  Markdown, PHP, Ruby, Perl, LaTeX, reStructuredText, F#). The reference gets the grammar without them.
- A `begin` rule without `end` or `while` (XML's malformed comment rule): vscode-textmate compiles it with `\uFFFF`
  as its end, TextMateSharp passes `null` to `Regex.Match` (`ArgumentNullException`). The reference gets
  `"end": "\uFFFF"`.
- Theme `include`: VS Code flattens the included theme's rules before the theme's own, into one list resolved by
  specificity. TextMateSharp keeps the include in a second trie that is consulted only when the theme's own rules
  have no match for a scope, so `support.type` in `dark_plus.json` beats `support.type.property-name` in
  `dark_vs.json` (JSON keys turn teal instead of light blue). The reference gets the flattened theme.
- Comments and trailing commas in VS Code's theme JSON: TextMateSharp's JSON reader does not skip them; the reference
  gets plain JSON.
- `TextMateSharp.Grammars.RegistryOptions.GetInjections` returns nothing: injection grammars (JSDoc, Markdown math,
  the Razor and PHP injections) never apply with the bundled registry. The checkers give the reference the
  injections of `grammars/manifest.json`.

### Remaining differences

With identical grammar files, 1,845 of 10,345 corpus files (76,312 scope-mismatched lines, cascades included, and
87,681 style-only lines) differ; vscode-textmate agrees with Tint on every one of them. By grammar, from the first
differing line of each file:

- YAML (499 of 500 files): the YAML 1.2 grammar (RedCMD's, in VS Code since 2024) leans on `\G` and empty-width
  `begin` rules; TextMateSharp loses the mapping from the first `key: value` line (`- name: x` becomes
  `invalid.illegal.unrecognized.yaml`).
- Ruby (352 files): method parameters (`def initialize(message = nil, …)`): TextMateSharp leaves ` = nil` in
  `meta.function.method.with-arguments.ruby` without the separator and default-value scopes.
- Markdown (332 files): in 35 the YAML front matter (`---`) closes on another line; most others differ in styles
  only.
- PowerShell (411 files, styles only) and CSS, SCSS, Less, HTML and Razor (styles): theme rules with parent scopes
  (`source.powershell variable.other.member`, `source.css variable`) resolve differently in TextMateSharp.
- Shell (88 files): function definitions (`name() {`) lose `meta.function.shell`.
- SQL (7 files): nested brackets (`ARRAY[['b','d']]`).
- HTML and Razor (17 files): attributes of `<svg>` elements become `invalid.illegal.character-not-allowed-here.html`.
- C++ (21 files, styles only), Perl (3), Objective-C and Objective-C++ (4), Groovy (1), Less (3): isolated
  constructs (`new` after `>(`, `s{…}{…}g` substitutions, pointer declarations, `$it.key` interpolation,
  `@var: "…"`).

All 16,970 `match`, `begin`, `end` and `while` patterns of the synced grammars compile with Onigwrap, so none of
these comes from a pattern TextMateSharp rejects; they were not investigated further.

### Grammar versions

TextMateSharp.Grammars 2.0.4 bundles older copies of many VS Code grammars: by the `version` header of the grammar
files, 41 of the 71 grammar files both bundles have differ: C#, C++ (and its embedded variants), CSS, Less, Dart,
Dockerfile, F#, Go, Java, JavaScript and JSX, TypeScript and TSX, Julia, Lua, Makefile, Objective-C and
Objective-C++, PHP, PowerShell, Python, R, Ruby, Rust, Shell, SQL, Swift, YAML, BibTeX, Razor, Handlebars, Markdown,
Log, Pug, TeX and LaTeX. The full table is in the corpus report. With TextMateSharp's own grammars and theme, 7,831 of the 10,320 files and 529,226 lines differ from
Tint; most of that is grammar versions, not engines.

