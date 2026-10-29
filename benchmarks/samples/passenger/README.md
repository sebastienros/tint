# Passenger fixtures

The ten files of the highlighting benchmark that measured [Passenger](https://github.com/Zaneris/passenger)'s move
from TextMateSharp to Tint (Zaneris/passenger pull request 572), copied unchanged from the reference branch
`reference/tint-instrumentation-current` at commit `1499d3df691bcf8876d05f5cb4d989f1e093957b`, path
`tools/Passenger.HighlightingBenchmarks/fixtures/`. They are Passenger's own sources (MIT License, Copyright (c)
2026 Ted L); `scss.scss` and `less.less` are the samples of its colour tests. Passenger's harness repeats each file to
about 10,000 lines; `EditorFilesBenchmarks` repeats them the same number of times.

| File | Passenger source | Repeats | Lines (repeated, as split on `\n`) |
|---|---|---:|---:|
| `xml.props` | `Directory.Packages.props` | 97 | 9,992 |
| `json.json` | `src/Passenger.Lsp/WebServers/schemas/global.json` | 112 | 9,969 |
| `yaml.yml` | `.github/workflows/ci.yml` | 32 | 9,889 |
| `markdown.md` | `README.md` | 23 | 9,960 |
| `html.html` | `site/index.html` | 14 | 9,759 |
| `css.css` | `site/style.css` | 24 | 9,961 |
| `javascript.js` | `site/site.js` | 58 | 9,919 |
| `shell.sh` | `tools/package-mac.sh` | 26 | 9,673 |
| `scss.scss` | `tests/Passenger.Tests/FileColoursTests.cs` (Sass sample) | 588 | 9,997 |
| `less.less` | `tests/Passenger.Tests/FileColoursTests.cs` (Less sample) | 588 | 9,997 |
