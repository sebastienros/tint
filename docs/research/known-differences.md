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
