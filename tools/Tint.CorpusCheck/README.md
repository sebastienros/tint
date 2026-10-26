# Tint.CorpusCheck

Tokenizes a corpus of real files with Tint and with TextMateSharp 2.0.4 (Onigwrap, native Oniguruma) and reports
every line where they differ, per grammar, with the time each side took. Differences are then judged by
vscode-textmate itself (the referee), so a report says which side is right.

## How it works

- [`corpus.json`](corpus.json) lists the repositories (URL, pinned commit, license, sparse-checkout paths) and the
  per-language cap. [`fetch.sh`](fetch.sh) clones them shallow, blobless and sparse into the git-ignored
  `artifacts/corpus/`; it is idempotent.
- Files map to grammars by the file extensions of the bundled grammars (`grammars/manifest.json`); at most
  `maxFilesPerLanguage` files per language, repositories taking turns in path order.
- Each file is tokenized line by line (terminators removed, state carried) with `TokenizeLine` (offsets and scopes
  compared, token ends clamped to the line) and `TokenizeLine2` (foreground, background and font style compared after
  merging equal neighbours; language ids, token types and bracket flags are left out since they depend on registry
  order and on package.json metadata TextMateSharp does not read). Files run in parallel, largest first.
- Tint runs its bundled grammars (`TintGrammars.All`); Dark+ is read as written (`ThemeLoadMode.Raw`) on both sides.
- `--reference-grammars Same` (the default) gives TextMateSharp the same grammar files Tint's bundle is generated
  from, so differences come from the engines. Two shapes TextMateSharp cannot read are fixed for it only: strings
  among captures (XML, YAML) are dropped and a `begin` without `end` gets the empty pattern, as in vscode-textmate `main` (#252); themes are given
  with comments removed and `include` flattened (see `docs/research/known-differences.md`).
  `--reference-grammars Bundled` uses TextMateSharp.Grammars' own copies instead.
- The report lists, per grammar, files, lines, mismatched files and lines (cascades included), style-only lines,
  exceptions and seconds per side, the `version` header of each grammar in both bundles, and the first mismatch of
  each file with both outputs.
- Referee: [`referee/tokenize.mjs`](referee/tokenize.mjs) runs vscode-textmate 9.3.2 over vscode-oniguruma on the same
  grammar files (all loaded) and Dark+. That is the npm release; Tint ports `main` at `392b23a4`, which adds #252
  (an empty or missing `end` matches immediately) and #276 (back-reference escaping), so a verdict involving either
  favors the release's behavior. For the first mismatch of each file, the checker compares Tint and TextMateSharp with it
  from line 1 to that line and records which side agrees. It needs Node and `npm install` in `referee/`; without it
  the report has no verdicts.

## Run

```bash
tools/Tint.CorpusCheck/fetch.sh
(cd tools/Tint.CorpusCheck/referee && npm ci)
dotnet run -c Release --project tools/Tint.CorpusCheck
```

Options: `--max-files-per-language N`, `--languages csharp,yaml`, `--threads N`, `--time-limit-ms N` (per line, both
sides), `--max-line-length N` (skip minified files), `--reference-grammars Same|Bundled`, `--referee false`,
`--out dir` (default `artifacts/corpus-check`). The exit code is 0 when every line matches.

To look at one file, `dump` prints the tokens of either engine in the referee's format, so the three diff directly:

```bash
dotnet run -c Release --project tools/Tint.CorpusCheck -- dump --scope source.yaml path/to/file.yml > tint.txt
node tools/Tint.CorpusCheck/referee/tokenize.mjs "$PWD" source.yaml path/to/file.yml | grep -v '^S ' > vscode.txt
diff tint.txt vscode.txt
```

The latest report is [docs/research/corpus-check-2026-10-25.md](../../docs/research/corpus-check-2026-10-25.md).
