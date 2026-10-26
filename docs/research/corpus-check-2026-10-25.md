# Corpus check, 2026-10-25

`tools/Tint.CorpusCheck` (see its [README](../../tools/Tint.CorpusCheck/README.md)): every file tokenized line by line with Tint (its bundled grammars) and with TextMateSharp 2.0.4 (Onigwrap) reading the same grammar files, Dark+ read as written on both sides; `TokenizeLine` offsets and scopes and `TokenizeLine2` colors and font styles compared on every line. The first mismatch of each file is then judged by vscode-textmate 9.3.2 over vscode-oniguruma on the same files (`tools/Tint.CorpusCheck/referee`, every grammar loaded).

- Machine: Apple M4 Pro (macOS 15.8.1 arm64, 14 cores), .NET 10.0.11; 12 threads, 158 s for the tokenization.
- 10,418 files selected (at most 500 per language, repositories taking turns), 10,345 tokenized: 3,343,607 lines; 73 skipped for a line over 10,000 characters (minified files).
- Repositories (pinned in `tools/Tint.CorpusCheck/corpus.json`): [aspnetcore](https://github.com/dotnet/aspnetcore/tree/ceb0eec49d8d2c77f4f7c26b85ddb6e96940dfed) (MIT), [vscode](https://github.com/microsoft/vscode/tree/1c20ad34761ad529d85ecaacd0ce4f31ef515185) (MIT), [cpython](https://github.com/python/cpython/tree/1c327a4464fa47f5d3aaf4c6c0184cb14ef2e21e) (PSF-2.0), [go](https://github.com/golang/go/tree/17d7bdecd0259971b1ace357f8efdb3efbf916b4) (BSD-3-Clause), [rust](https://github.com/rust-lang/rust/tree/859220648edee0282af5b56f0b490cd4beb074db) (MIT OR Apache-2.0), [curl](https://github.com/curl/curl/tree/bc677a8f16a51e728136eff130509cfac57aad19) (curl), [laravel](https://github.com/laravel/framework/tree/5d7797528723107cd4f495326838a7d7d67fbb20) (MIT), [node](https://github.com/nodejs/node/tree/3983092f63284fe57e53006cba91948fbbd84967) (MIT), [powershell](https://github.com/PowerShell/PowerShell/tree/dad69345f3fae3d0530f87ec73c8441649760483) (MIT), [rails](https://github.com/rails/rails/tree/d5c10191684c845d4ddb53587d3a2965270369fc) (MIT), [guava](https://github.com/google/guava/tree/5ae56d77e64305752b919ff2c96713f47ac0c94c) (Apache-2.0), [fmt](https://github.com/fmtlib/fmt/tree/f87a75e15262614ce8aa0e6a0664df9d0823fb92) (MIT), [postgres](https://github.com/postgres/postgres/tree/022a80fe6c1d82f7777bd52f9d3e5f9952533109) (PostgreSQL), [kubernetes-examples](https://github.com/kubernetes/examples/tree/d6b8cd27eacb51e651a1aa6f7c190a28713eff6e) (Apache-2.0).

## Result

- No exception on either side, no line out of the 10 s limit on either side.
- 1,845 files differ (76,312 lines with different scopes, cascades after a first difference included, and 87,681 lines with the same scopes and different styles). **vscode-textmate agrees with Tint on all 1,845**: every difference is TextMateSharp diverging. They are described in [known-differences.md](known-differences.md#against-textmatesharp-204).
- Time in each engine, summed over threads and including the first use of every rule: Tint 214 s, TextMateSharp 1,632 s (7.6x). The per-grammar speedups below are indicative (12 threads interleave both engines); BenchmarkDotNet numbers are in `benchmarks/README.md`.
- With TextMateSharp's own grammars and theme (`--reference-grammars Bundled`), files and lines differing: 7,831 of 10,320 (+98 skipped) files, 529,226 lines with different scopes. Most of that is the older grammar copies TextMateSharp bundles (table below).

Columns: files tokenized (and skipped), lines, files with a difference, lines with different scopes, lines with only different styles, lines where only one side stopped early, exceptions per side, seconds per side, TextMateSharp time over Tint time.

| Language | Scope | Files | Lines | Mismatched files | Mismatched lines | Style-only lines | Stopped early | Tint exceptions | Reference exceptions | Tint s | Reference s | Speedup |
|---|---|--:|--:|--:|--:|--:|--:|--:|--:|--:|--:|--:|
| bat | `source.batchfile` | 48 | 1,062 | 0 | 0 | 0 | 0 | 0 | 0 | 0.04 | 0.63 | 14.07x |
| bibtex | `text.bibtex` | 1 | 22 | 0 | 0 | 0 | 0 | 0 | 0 | 0.00 | 0.01 | 16.62x |
| c | `source.c` | 500 | 348,445 | 0 | 0 | 0 | 0 | 0 | 0 | 15.61 | 95.11 | 6.09x |
| clojure | `source.clojure` | 1 | 52 | 0 | 0 | 0 | 0 | 0 | 0 | 0.00 | 0.17 | 91.03x |
| coffeescript | `source.coffee` | 2 | 43 | 0 | 0 | 0 | 0 | 0 | 0 | 0.00 | 0.13 | 103.24x |
| cpp | `source.cpp` | 500 | 275,178 | 21 | 2 | 189 | 0 | 0 | 0 | 49.25 | 325.69 | 6.61x |
| csharp | `source.cs` | 500 | 83,499 | 0 | 0 | 0 | 0 | 0 | 0 | 8.89 | 51.40 | 5.78x |
| css | `source.css` | 166 (+26 skipped) | 152,415 | 105 | 0 | 2,264 | 0 | 0 | 0 | 13.36 | 230.82 | 17.28x |
| cuda-cpp | `source.cuda-cpp` | 2 | 177 | 0 | 0 | 0 | 0 | 0 | 0 | 0.22 | 1.72 | 7.80x |
| dart | `source.dart` | 1 | 19 | 0 | 0 | 0 | 0 | 0 | 0 | 0.00 | 0.07 | 154.26x |
| diff | `source.diff` | 81 (+18 skipped) | 2,048 | 0 | 0 | 0 | 0 | 0 | 0 | 0.01 | 0.13 | 13.68x |
| dotenv | `source.dotenv` | 4 | 53 | 0 | 0 | 0 | 0 | 0 | 0 | 0.00 | 0.04 | 85.18x |
| fsharp | `source.fsharp` | 2 | 36 | 0 | 0 | 0 | 0 | 0 | 0 | 0.00 | 0.37 | 129.08x |
| go | `source.go` | 500 | 91,908 | 0 | 0 | 0 | 0 | 0 | 0 | 6.12 | 29.36 | 4.80x |
| groovy | `source.groovy` | 1 | 219 | 1 | 20 | 0 | 0 | 0 | 0 | 0.01 | 0.33 | 47.25x |
| handlebars | `text.html.handlebars` | 2 | 54 | 0 | 0 | 0 | 0 | 0 | 0 | 0.00 | 0.47 | 195.38x |
| hlsl | `source.hlsl` | 1 | 13 | 0 | 0 | 0 | 0 | 0 | 0 | 0.00 | 0.02 | 214.91x |
| html | `text.html.derivative` | 145 (+2 skipped) | 16,931 | 11 | 46 | 4 | 0 | 0 | 0 | 1.15 | 20.05 | 17.47x |
| ignore | `source.ignore` | 117 | 725 | 0 | 0 | 0 | 0 | 0 | 0 | 0.00 | 0.02 | 40.30x |
| ini | `source.ini` | 124 | 709 | 0 | 0 | 0 | 0 | 0 | 0 | 0.00 | 0.08 | 49.88x |
| jade | `text.pug` | 2 | 30 | 0 | 0 | 0 | 0 | 0 | 0 | 0.01 | 0.76 | 79.11x |
| java | `source.java` | 500 | 148,921 | 0 | 0 | 0 | 0 | 0 | 0 | 6.69 | 40.08 | 5.99x |
| javascript | `source.js` | 482 (+18 skipped) | 195,792 | 0 | 0 | 0 | 0 | 0 | 0 | 15.68 | 144.75 | 9.23x |
| javascriptreact | `source.js.jsx` | 1 | 35 | 0 | 0 | 0 | 0 | 0 | 0 | 0.04 | 0.37 | 10.26x |
| json | `source.json` | 496 (+4 skipped) | 476,299 | 0 | 0 | 0 | 0 | 0 | 0 | 3.73 | 11.32 | 3.04x |
| jsonc | `source.json.comments` | 3 | 1,515 | 0 | 0 | 0 | 0 | 0 | 0 | 0.02 | 0.07 | 2.73x |
| jsonl | `source.json.lines` | 1 | 2 | 0 | 0 | 0 | 0 | 0 | 0 | 0.00 | 0.00 | 3.87x |
| julia | `source.julia` | 2 | 197 | 0 | 0 | 0 | 0 | 0 | 0 | 0.03 | 0.34 | 12.42x |
| latex | `text.tex.latex` | 1 | 20 | 0 | 0 | 0 | 0 | 0 | 0 | 0.01 | 0.12 | 18.08x |
| less | `source.css.less` | 3 | 60 | 3 | 53 | 1 | 0 | 0 | 0 | 0.02 | 0.34 | 16.03x |
| log | `text.log` | 2 | 9 | 0 | 0 | 0 | 0 | 0 | 0 | 0.00 | 0.02 | 17.09x |
| lua | `source.lua` | 5 | 208 | 0 | 0 | 0 | 0 | 0 | 0 | 0.00 | 0.12 | 27.96x |
| makefile | `source.makefile` | 3 | 156 | 0 | 0 | 0 | 0 | 0 | 0 | 0.00 | 0.01 | 2.93x |
| markdown | `text.html.markdown` | 500 | 285,019 | 332 | 1,432 | 74,059 | 0 | 0 | 0 | 30.53 | 154.37 | 5.06x |
| objective-c | `source.objc` | 3 | 73 | 3 | 27 | 0 | 0 | 0 | 0 | 0.06 | 0.24 | 3.76x |
| objective-cpp | `source.objcpp` | 1 | 52 | 1 | 21 | 0 | 0 | 0 | 0 | 0.03 | 0.07 | 2.07x |
| perl | `source.perl` | 107 | 32,293 | 3 | 114 | 0 | 0 | 0 | 0 | 0.67 | 4.62 | 6.91x |
| php | `source.php` | 500 | 60,083 | 0 | 0 | 0 | 0 | 0 | 0 | 2.58 | 32.43 | 12.59x |
| powershell | `source.powershell` | 500 | 123,591 | 411 | 165 | 11,161 | 0 | 0 | 0 | 5.71 | 51.53 | 9.03x |
| python | `source.python` | 500 | 187,791 | 0 | 0 | 0 | 0 | 0 | 0 | 7.77 | 67.01 | 8.63x |
| r | `source.r` | 1 | 24 | 0 | 0 | 0 | 0 | 0 | 0 | 0.02 | 0.13 | 7.02x |
| raku | `source.perl.6` | 1 | 27 | 0 | 0 | 0 | 0 | 0 | 0 | 0.00 | 0.03 | 15.69x |
| razor | `text.html.cshtml` | 499 (+1 skipped) | 22,018 | 6 | 28 | 1 | 0 | 0 | 0 | 1.95 | 25.70 | 13.17x |
| restructuredtext | `source.rst` | 3 | 378 | 0 | 0 | 0 | 0 | 0 | 0 | 0.00 | 0.13 | 40.87x |
| ruby | `source.ruby` | 500 | 103,596 | 352 | 2,577 | 0 | 0 | 0 | 0 | 4.60 | 30.22 | 6.56x |
| rust | `source.rust` | 500 | 178,594 | 0 | 0 | 0 | 0 | 0 | 0 | 6.10 | 51.22 | 8.39x |
| scss | `source.css.scss` | 6 | 4,341 | 2 | 0 | 2 | 0 | 0 | 0 | 0.45 | 11.24 | 24.84x |
| shaderlab | `source.shaderlab` | 1 | 15 | 0 | 0 | 0 | 0 | 0 | 0 | 0.01 | 0.02 | 2.38x |
| shellscript | `source.shell` | 240 | 21,847 | 88 | 2,036 | 0 | 0 | 0 | 0 | 0.66 | 5.49 | 8.37x |
| snippets | `source.json.comments.snippets` | 17 | 1,732 | 0 | 0 | 0 | 0 | 0 | 0 | 0.08 | 0.48 | 5.88x |
| sql | `source.sql` | 498 (+2 skipped) | 89,115 | 7 | 115 | 0 | 0 | 0 | 0 | 5.42 | 84.11 | 15.52x |
| swift | `source.swift` | 1 | 13 | 0 | 0 | 0 | 0 | 0 | 0 | 0.02 | 0.07 | 3.07x |
| tex | `text.tex` | 1 | 20 | 0 | 0 | 0 | 0 | 0 | 0 | 0.01 | 0.03 | 3.60x |
| typescript | `source.ts` | 500 | 98,239 | 0 | 0 | 0 | 0 | 0 | 0 | 12.06 | 80.41 | 6.67x |
| typescriptreact | `source.tsx` | 265 (+1 skipped) | 64,876 | 0 | 0 | 0 | 0 | 0 | 0 | 8.19 | 64.87 | 7.92x |
| vb | `source.asp.vb.net` | 2 | 26 | 0 | 0 | 0 | 0 | 0 | 0 | 0.02 | 0.06 | 3.54x |
| xml | `text.xml` | 499 (+1 skipped) | 201,679 | 0 | 0 | 0 | 0 | 0 | 0 | 3.49 | 8.88 | 2.54x |
| xsl | `text.xml.xsl` | 1 | 5 | 0 | 0 | 0 | 0 | 0 | 0 | 0.00 | 0.02 | 66.54x |
| yaml | `source.yaml` | 500 | 71,308 | 499 | 69,676 | 0 | 0 | 0 | 0 | 2.78 | 4.19 | 1.51x |
| **total** |  | 10,345 (+73 skipped) | 3,343,607 | 1,845 | 76,312 | 87,681 | 0 | 0 | 0 | 214.10 | 1632.46 | 7.62x |

## Grammar versions

The `version` header of each grammar file in Tint's bundle (VS Code `3ac0f056`) and in TextMateSharp.Grammars 2.0.4, for the scopes both have: 41 of 71 differ.

| Scope | Same | Tint | TextMateSharp.Grammars |
|---|---|---|---|
| `documentation.injection.js.jsx` | yes | (none) | (none) |
| `documentation.injection.ts` | yes | (none) | (none) |
| `markdown.math.block` | yes | (none) | (none) |
| `markdown.math.inline` | yes | (none) | (none) |
| `source.asp.vb.net` | yes | `…72d44550b3286d0382d7be0624140cf97857ff69` | `…72d44550b3286d0382d7be0624140cf97857ff69` |
| `source.batchfile` | yes | `…6154ae25a24e01ac9329e7bcf958e093cd8733a9` | `…6154ae25a24e01ac9329e7bcf958e093cd8733a9` |
| `source.c` | yes | `…34712a6106a4ffb0a04d2fa836fd28ff6c5849a4` | `…34712a6106a4ffb0a04d2fa836fd28ff6c5849a4` |
| `source.c.platform` | yes | `…60daf83b9d45329524f7847a75e9298b3aae5805` | `…60daf83b9d45329524f7847a75e9298b3aae5805` |
| `source.clojure` | yes | `…45bdb881501d0b8f8b707ca1d3fcc8b4b99fca03` | `…45bdb881501d0b8f8b707ca1d3fcc8b4b99fca03` |
| `source.coffee` | yes | `…0f6db9143663e18b1ad00667820f46747dba495e` | `…0f6db9143663e18b1ad00667820f46747dba495e` |
| `source.cpp` | **no** | `…071dd6ecc9eda347bd84c8aa0e0b557396cb6a40` | `…924295fc44bde1a00fab60da3a2caca4509adb25` |
| `source.cpp.embedded.latex` | **no** | `…dfa69a16a1154dbc820dc1111d72faa6954dd1e2` | `…db888fc191f6b5610cd6866cc49017fc3dfb00b5` |
| `source.cpp.embedded.macro` | **no** | `…071dd6ecc9eda347bd84c8aa0e0b557396cb6a40` | `…924295fc44bde1a00fab60da3a2caca4509adb25` |
| `source.cs` | **no** | `…c2719309e91d2e26be21fce0ecaaca86c7b48ce4` | `…7a7482ffc72a6677a87eb1ed76005593a4f7f131` |
| `source.css` | **no** | `…de9e6beee756760f31b15efbd782735fc25de3db` | `…4a6dc90f332bfa72c88192513435a64013d9aad4` |
| `source.css.less` | **no** | `…63c0cba9792e49e255cce0f6dd03250fb30591e6` | `…87d4d59e8de6796b506b81a16e1dc1fafc99d30f` |
| `source.css.scss` | yes | `…f52ab12f7f9346cc2568129d8c4419bd3d506b47` | `…f52ab12f7f9346cc2568129d8c4419bd3d506b47` |
| `source.cuda-cpp` | yes | `…81e88eaec5170aa8585736c63627c73e3589998c` | `…81e88eaec5170aa8585736c63627c73e3589998c` |
| `source.dart` | **no** | `…b308b4b3502c8639149e63d74844da51031b5f47` | `…9d4857e114b7000d94232d83187ad142961c678a` |
| `source.diff` | yes | `…0593bb775eab1824af97ef2172fd38822abd97d7` | `…0593bb775eab1824af97ef2172fd38822abd97d7` |
| `source.dockerfile` | **no** | `…c2029cb2574647e4bc28ed58486b8e85883eedb9` | `…abd39744c6f3ed854500e423f5fabf952165161f` |
| `source.fsharp` | **no** | `…0cb968a4b8fdb2e0656b95342cdffbeff04a1248` | `…e177bd7f9d3402f70d2f1fb42c74057ed1ccf6fa` |
| `source.go` | **no** | `…c74e22eb9ef32958e3edd130ea750ce78d8b8241` | `…6175663a7a0e23d58ccf9aab95054cb6e5c92aff` |
| `source.groovy` | yes | `…85d8f7c97ae473ccb9473f6c8d27e4ec957f4be1` | `…85d8f7c97ae473ccb9473f6c8d27e4ec957f4be1` |
| `source.hlsl` | yes | `…87c0dca3a39170dbd7ee7e277db4f915fb2de14a` | `…87c0dca3a39170dbd7ee7e277db4f915fb2de14a` |
| `source.ignore` | yes | (none) | (none) |
| `source.ini` | yes | `…2af0cbb0704940f967152616f2f1ff0aae6287a6` | `…2af0cbb0704940f967152616f2f1ff0aae6287a6` |
| `source.ini` | yes | `…2af0cbb0704940f967152616f2f1ff0aae6287a6` | `…2af0cbb0704940f967152616f2f1ff0aae6287a6` |
| `source.java` | **no** | `…f09b712f5d6d6339e765f58c8dfab3f78a378183` | `…29f977dc42a7e2568b39bb6fb34c4ef108eb59b3` |
| `source.js` | **no** | `…48f608692aa6d6ad7bd65b478187906c798234a8` | `…0dfae8cc4807fecfbf8f1add095d9817df824c95` |
| `source.js.jsx` | **no** | `…48f608692aa6d6ad7bd65b478187906c798234a8` | `…0dfae8cc4807fecfbf8f1add095d9817df824c95` |
| `source.json` | yes | `…9bd83f1c252b375e957203f21793316203f61f70` | `…9bd83f1c252b375e957203f21793316203f61f70` |
| `source.json.comments` | yes | `…9bd83f1c252b375e957203f21793316203f61f70` | `…9bd83f1c252b375e957203f21793316203f61f70` |
| `source.julia` | **no** | `…dbaa99358d1dbc2fc6c337f0f6571060dbc4ee1f` | `…7b7801f41ce4ac1303bd17e057dbe677e24f597f` |
| `source.lua` | **no** | `…b295d83bf0e91b5d3a69eb097f9ed351623b95be` | `…bc74f9230c3f07c0ecc1bc1727ad98d9e70aff5b` |
| `source.makefile` | **no** | `…1d4c0b541959995db098df751ffc129da39a294b` | `…ef0c485afc66445a6cf184dc34f7744306304f1f` |
| `source.objc` | **no** | `…119b75fb1f4d3e8726fa62588e3b935e0b719294` | `…bc7dedd28eebe52b374744d3fb34d77ff441569e` |
| `source.objcpp` | **no** | `…5a7eb15eee382dd5aa388bc04fdb60a0d2128e14` | `…bc7dedd28eebe52b374744d3fb34d77ff441569e` |
| `source.perl` | yes | `…a85927a902d6e5d7805f56a653f324d34dfad53a` | `…a85927a902d6e5d7805f56a653f324d34dfad53a` |
| `source.perl.6` | yes | `…d9841a0878239fa43f88c640f8d458590f97e8f5` | `…d9841a0878239fa43f88c640f8d458590f97e8f5` |
| `source.php` | **no** | `…d94fd6b7dff1613d34bc0d36328f9982e65bd545` | `…4cdf06a944a3b3c8884a6085c6adc6b246911646` |
| `source.powershell` | **no** | `…23eed76d483c887a1608609e9dd57e09666411cc` | `…742f0b5d4b60f5930c0b47fcc1f646860521296e` |
| `source.python` | **no** | `…7d0f2b22a5ad8fccbd7341bc7b7a715169283044` | `…b2b4f4ae7b4e6284e80bda8080106b93bd588f9e` |
| `source.r` | **no** | `…b199996070723eefbe7a61e0384b2ae4768eb7f0` | `…ff60e426f66503f3c9533c7a62a8fd3f9f6c53df` |
| `source.regexp.python` | yes | `…c9b3409deb69acec31bbf7913830e93a046b30cc` | `…c9b3409deb69acec31bbf7913830e93a046b30cc` |
| `source.ruby` | **no** | `…e044408a069f43c892143f19c389628eebffbd01` | `…efcb8941c701343f1b2e9fb105c678152fea6892` |
| `source.rust` | **no** | `…ca34cf382a7b250144f2ccb99ff9af96912c79f2` | `…20462d50ff97338f42c6b64c3f421c634fd60734` |
| `source.sassdoc` | yes | `…303bbf0c250fe380b9e57375598cfd916110758b` | `…303bbf0c250fe380b9e57375598cfd916110758b` |
| `source.shaderlab` | yes | `…c72c8b39380ba5a86c58ceed053b5d965ebf38b3` | `…c72c8b39380ba5a86c58ceed053b5d965ebf38b3` |
| `source.shell` | **no** | `…35020b0bd79a90d3b262b4c13a8bb0b33adc1f45` | `…4f8d7bb5cc4d1643674551683df10fe552dd5a6f` |
| `source.sql` | **no** | `…684648ee5414eb2d9df22b0392ef05079f91e44e` | `…b8b58864526c048002b7c3964bdac8aac3713bd9` |
| `source.swift` | **no** | `…2be007b6bc88e54a0edfd3c313ff31276edc923d` | `…7a35637eb70aef3114b091c4ff6fbf6a2faa881b` |
| `source.ts` | **no** | `…48f608692aa6d6ad7bd65b478187906c798234a8` | `…634a12f4fa2b2843c83353c0a395852c3a0c04c4` |
| `source.tsx` | **no** | `…48f608692aa6d6ad7bd65b478187906c798234a8` | `…634a12f4fa2b2843c83353c0a395852c3a0c04c4` |
| `source.yaml` | **no** | `…c42cf86959ba238dc8a825bdd07bed6f5e97c978` | `…e54ceae3b719506dba7e481a77cea4a8b576ae46` |
| `source.yaml` | **no** | `…c42cf86959ba238dc8a825bdd07bed6f5e97c978` | `…e54ceae3b719506dba7e481a77cea4a8b576ae46` |
| `text.bibtex` | **no** | `…0fcf9283828cab2aa611072f54feb1e7d501c2b4` | `…b98c2d4911652824fc990f4b26c9c30be59b78a2` |
| `text.html.basic` | yes | `…0c3d5ee54de3a993f747f54186b73a4d2d3c44a2` | `…0c3d5ee54de3a993f747f54186b73a4d2d3c44a2` |
| `text.html.cshtml` | **no** | `…79e211a2e4287f9f7508089b81311b2c7fdc169f` | `…9b1e979b6c3fe7cfbe30f595b9b0994d20bd482c` |
| `text.html.derivative` | yes | `…390c8870273a2ae80244dae6db6ba064a802f407` | `…390c8870273a2ae80244dae6db6ba064a802f407` |
| `text.html.handlebars` | **no** | `…0ffd0a2d95bfc052c3ff29a3eb4dd3749957b466` | `…85a153a6f759df4e8da7533e1b3651f007867c51` |
| `text.html.markdown` | **no** | `…0812fc4b190efc17bfed0d5b4ff918eff8e4e377` | `…a612b96d62aa1ce305c4a55dc9d577316fab39da` |
| `text.html.markdown.math` | yes | (none) | (none) |
| `text.html.php` | **no** | `…ff64523c94c014d68f5dec189b05557649c5872a` | `…2bf736a814e1a58aa63470c1a29590bd02e924e7` |
| `text.log` | **no** | `…396c8552ff99d0e07142fdd940d75dae212dd28c` | `…8acba9307254d1887ac770057767698c82d926c6` |
| `text.pug` | **no** | `…ae1dd60ca4aa4b45617f236d584216cd8d19eecf` | `…e67e895f6fb64932aa122e471000fa55d826bff6` |
| `text.tex` | **no** | `…76dc409348227db00f6779772f7763dc90cdf22e` | `…b98c2d4911652824fc990f4b26c9c30be59b78a2` |
| `text.tex.latex` | **no** | `…843cd14021feb45069fac66c6e1f98e02a196dea` | `…7b08daed22963695616ef432c9d027427da5f450` |
| `text.tex.markdown_latex_combined` | **no** | `…8795ab7e45ae21070a6914cb57d2f040fc49254f` | `…4ef954d31faccf96545d736a5ccc0e09d8109f12` |
| `text.xml` | yes | `…7bc75dfe779ad5b35d9bf4013d9181864358cb49` | `…7bc75dfe779ad5b35d9bf4013d9181864358cb49` |
| `text.xml.xsl` | yes | `…507de2ee7daca60cf02e9e21fbeb92bbae73e280` | `…507de2ee7daca60cf02e9e21fbeb92bbae73e280` |

## First difference per grammar

One file per grammar with differences, its first differing line and the first differing token on each side (full outputs in the generated report).

- cpp (`source.cpp`), 21 files; `fmt/include/fmt/args.h` line 59 (styles): ``     auto new_node = std::unique_ptr<typed_node<T>>(new typed_node<T>(arg)); ``
  - Tint: `` 51 #C586C0 #FFFFFF fs=0 ``
  - TextMateSharp: `` 51 #569CD6 #FFFFFF fs=0 ``
- css (`source.css`), 105 files; `aspnetcore/src/Components/AI/src/wwwroot/ai-chat.css` line 88 (styles): `` .sc-ai-btn:focus-visible { ``
  - Tint: `` 24 #000000 #FFFFFF fs=0 ``
  - TextMateSharp: `` 10 #9CDCFE #FFFFFF fs=0 ``
- groovy (`source.groovy`), 1 files; `vscode/extensions/vscode-colorize-tests/test/colorize-fixtures/test.groovy` line 87 (tokens): `` devMap.each { println \"$it.key: $it.value\" } ``
  - Tint: `` 26-27 "." source.groovy string.quoted.double.groovy variable.other.interpolated.groovy keyword.other.dereference.groovy ``
  - TextMateSharp: `` 26-32 ".key: " source.groovy string.quoted.double.groovy ``
- html (`text.html.derivative`), 11 files; `aspnetcore/src/Components/WebAssembly/Samples/BlazorWebAssemblyStandalone/wwwroot/index.html` line 19 (tokens): ``         <svg class=\"loading-progress\"> ``
  - Tint: `` 13-18 "class" text.html.derivative meta.element.structure.svg.html meta.attribute.class.html entity.other.attribute-name.html ``
  - TextMateSharp: `` 13-37 "class=\"loading-progress\"" text.html.derivative meta.element.structure.svg.html invalid.illegal.character-not-allowed-here.html ``
- less (`source.css.less`), 3 files; `vscode/extensions/vscode-colorize-tests/test/colorize-fixtures/14119.less` line 1 (tokens): `` #f(@hm: \"broken highlighting in VS Code\") { ``
  - Tint: `` 4-6 "hm" source.css.less meta.selector.less meta.group.less meta.property-value.less variable.other.readwrite.less support.other.variable.less ``
  - TextMateSharp: `` 4-43 "hm: \"broken highlighting in VS Code\") {" source.css.less meta.selector.less meta.group.less meta.property-value.less variable.other.readwrite.less support.other.variable.less ``
- markdown (`text.html.markdown`), 332 files; `aspnetcore/eng/common/AGENTS.md` line 3 (styles): `` Files under `eng/common` come from [Arcade](https://github.com/dotnet/arcade). ``
  - Tint: two more style runs, `` 44 #000000 #FFFFFF fs=4 `` (the link underlined) and `` 76 #000000 #FFFFFF fs=0 ``
  - TextMateSharp: the link not underlined
- objective-c (`source.objc`), 3 files; `go/src/cmd/link/internal/ld/testdata/issue32233/lib/ObjC.m` line 10 (tokens): ``   NSAppearance *darkAppearance; ``
  - Tint: `` 0-15 "  NSAppearance " source.objc meta.block.objc ``
  - TextMateSharp: `` 0-31 "  NSAppearance *darkAppearance;" source.objc meta.block.objc ``
- objective-cpp (`source.objcpp`), 1 files; `vscode/extensions/vscode-colorize-tests/test/colorize-fixtures/test.mm` line 21 (tokens): ``     NSString* defaultDir = NSSearchPathForDirectoriesInDomains(NSDocumentDirectory, NSUserDomainMask, true)[0]; ``
  - Tint: `` 0-4 "    " source.objcpp meta.implementation.objcpp meta.scope.implementation.objcpp meta.function-with-body.objcpp meta.block.objcpp ``
  - TextMateSharp: `` 0-27 "    NSString* defaultDir = " source.objcpp meta.implementation.objcpp meta.scope.implementation.objcpp meta.function-with-body.objcpp meta.block.objcpp ``
- perl (`source.perl`), 3 files; `curl/tests/pathhelp.pm` line 108 (tokens): ``     $cur_dir =~ s{[/\\\\]+}{/}g; ``
  - Tint: `` 16-17 "s" source.perl string.regexp.nested_braces.perl punctuation.definition.string.perl support.function.perl ``
  - TextMateSharp: `` 16-17 "s" source.perl string.regexp.replace.perl punctuation.definition.string.perl keyword.control.regexp-option.perl ``
- powershell (`source.powershell`), 411 files; `aspnetcore/eng/build.ps1` line 261 (styles): ``             $nodeHome = Split-Path -Parent (Split-Path -Parent $node.Path) ``
  - Tint: `` 68 #DCDCAA #FFFFFF fs=0 ``
  - TextMateSharp: `` 73 #000000 #FFFFFF fs=0 ``
- razor (`text.html.cshtml`), 6 files; `aspnetcore/src/Components/test/testassets/BasicTestApp/EventFlagsComponent.razor` line 2 (styles): ``     button:focus { ``
  - Tint: `` 16 #D4D4D4 #FFFFFF fs=0 ``
  - TextMateSharp: `` 10 #9CDCFE #FFFFFF fs=0 ``
- ruby (`source.ruby`), 352 files; `rails/actionpack/lib/abstract_controller/base.rb` line 16 (tokens): ``     def initialize(message = nil, controller = nil, action = nil) # :nodoc: ``
  - Tint: `` 26-27 " " source.ruby meta.function.method.with-arguments.ruby ``
  - TextMateSharp: `` 26-32 " = nil" source.ruby meta.function.method.with-arguments.ruby ``
- scss (`source.css.scss`), 2 files; `vscode/extensions/vscode-colorize-tests/test/colorize-fixtures/test-cssvariables.scss` line 1 (styles): `` :root { ``
  - Tint: `` 0 #D7BA7D #FFFFFF fs=0 ``
  - TextMateSharp: `` 0 #9CDCFE #FFFFFF fs=0 ``
- shellscript (`source.shell`), 88 files; `aspnetcore/eng/build.sh` line 54 (tokens): `` __usage() { ``
  - Tint: `` 9-10 " " source.shell meta.function.shell ``
  - TextMateSharp: `` 9-10 " " source.shell ``
- sql (`source.sql`), 7 files; `postgres/contrib/hstore/sql/hstore.sql` line 79 (tokens): `` select 'aa=>1, c=>3, b=>2, d=>4'::hstore -> ARRAY[['b','d'],['aa','c']]; ``
  - Tint: `` 69-71 "]]" source.sql text.bracketed constant.character.bracket.sql ``
  - TextMateSharp: `` 69-70 "]" source.sql text.bracketed punctuation.definition.bracket.end.sql ``
- yaml (`source.yaml`), 499 files; `aspnetcore/eng/common/core-templates/job/helix-job-monitor.yml` line 3 (tokens): `` - name: timeoutInMinutes ``
  - Tint: `` 0-1 "-" source.yaml meta.stream.yaml meta.document.yaml meta.mapping.yaml meta.map.value.yaml meta.block.sequence.yaml punctuation.definition.block.sequence.item.yaml ``
  - TextMateSharp: `` 0-1 "-" source.yaml meta.stream.yaml meta.document.yaml meta.mapping.yaml meta.map.value.yaml invalid.illegal.unrecognized.yaml ``
