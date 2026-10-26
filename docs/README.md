# Tint documentation

- [Design](design.md): architecture, grammar and theme model, tokenizer, source generator,
  packaging and performance targets.
- [Plan](plan.md): phases P0 to P7, what each delivers and when it is done.
- [Source generator](generator.md): `TintGrammar`/`TintTheme` items, the attributes, metadata,
  diagnostics, kernels and how the generator finds GregsEx.
- [Releasing](release.md): tagging `vX.Y.Z`, what `publish.yml` does, verifying on nuget.org,
  previews on Feedz, the branch ruleset.
- [HTML output](html-output.md): the markup `ToHtml` writes, with rendered samples, CSS-class mode
  and the CSS-variable scheme for several themes.

## Research

- [vscode-textmate](research/vscode-textmate.md): the tokenization algorithm Tint ports, the
  reference for every behavior question.
- [VS Code corpus](research/vscode-corpus.md): inventory of the bundled grammars and themes,
  licenses and regex construct statistics.
- [GregsEx](research/gregsex.md): audit of the regex engine Tint builds on.
- [Landscape](research/landscape.md): TextMateSharp, TextMateLib and shiki.
- [TextMateSharp + GregsEx branch](research/textmatesharp-gregsex-branch.md): lessons from
  running TextMateSharp on GregsEx.
- [Known differences](research/known-differences.md): where Tint differs from vscode-textmate's
  raw output on purpose, and where TextMateSharp 2.0.4 differs from both.
- [Corpus check, 2026-10-25](research/corpus-check-2026-10-25.md): 10,345 files of 14 repositories
  tokenized by Tint and TextMateSharp, every difference judged by vscode-textmate
  (`tools/Tint.CorpusCheck`).
- [Fuzz campaign, 2026-10-25](research/fuzz-2026-10-25.md): 20 minutes of differential fuzzing over
  the 84 bundled grammars (`tools/Tint.Fuzz`).

## Repository data

- `grammars/` and `themes/` are synced from `microsoft/vscode` by `tools/Tint.Sync` (never
  hand-edit them); `grammars/manifest.json` and `themes/manifest.json` describe each file and its
  upstream, and `THIRD-PARTY-NOTICES.txt` lists the licenses. The scheduled
  `.github/workflows/sync-vscode.yml` workflow re-runs the sync weekly and opens a pull request
  from the `sync/vscode` branch when VS Code moved.
- `test/Tint.Tests/Fixtures/vscode-textmate/` holds the vscode-textmate test suites.
