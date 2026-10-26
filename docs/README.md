# Tint documentation

- [Design](design.md): architecture, grammar and theme model, tokenizer, source generator,
  packaging and performance targets.
- [Plan](plan.md): phases P0 to P7, what each delivers and when it is done.

## Research

- [vscode-textmate](research/vscode-textmate.md): the tokenization algorithm Tint ports, the
  reference for every behavior question.
- [VS Code corpus](research/vscode-corpus.md): inventory of the bundled grammars and themes,
  licenses and regex construct statistics.
- [GregsEx](research/gregsex.md): audit of the regex engine Tint builds on.
- [Landscape](research/landscape.md): TextMateSharp, TextMateLib and shiki.
- [TextMateSharp + GregsEx branch](research/textmatesharp-gregsex-branch.md): lessons from
  running TextMateSharp on GregsEx.

## Repository data

- `grammars/` and `themes/` are synced from `microsoft/vscode` by `tools/Tint.Sync` (never
  hand-edit them); `grammars/manifest.json` and `themes/manifest.json` describe each file and its
  upstream, and `THIRD-PARTY-NOTICES.txt` lists the licenses. The scheduled
  `.github/workflows/sync-vscode.yml` workflow re-runs the sync weekly and opens a pull request
  from the `sync/vscode` branch when VS Code moved.
- `test/Tint.Tests/Fixtures/vscode-textmate/` holds the vscode-textmate test suites.
