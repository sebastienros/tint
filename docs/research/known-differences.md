# Known differences from vscode-textmate

Cases of the vscode-textmate suites that Tint cannot pass because of a genuine difference between GregsEx and
vscode-oniguruma. Each entry gives the suite, the case, the pattern involved and the exact reason; the test is
skipped with the same reason (`KnownDifferences` in `test/Tint.Tests/TokenizationSuiteTests.cs`).

None at the moment: all 95 tokenization cases (`first-mate/tests.json`, `suite1/tests.json`,
`suite1/whileTests.json`) and all 72 files of the themes suite pass through the loader path.

## Deliberate API-level choices

These do not change tokens as the suites compare them, but differ from vscode-textmate's raw output:

- `Token.Length` stops at the end of the line: vscode-textmate's last token can extend over the `\n` it appends.
- `ThemeLoader` builds the default rule of a VS Code JSON color theme from `editor.foreground` and
  `editor.background` and drops other scopeless rules, as VS Code does before handing rules to vscode-textmate.
  `.tmTheme` files and JSON themes without those colors are passed through unchanged.
- `GrammarInfo.BalancedBracketScopes` defaults to `*`, VS Code's default, when a grammar declares none.
