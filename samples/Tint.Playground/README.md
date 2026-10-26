# Tint playground

A Blazor WebAssembly app to try Tint in the browser, in the spirit of [shiki's playground](https://shiki.matsu.io/play):
every grammar of `Tint.Grammars` with a language id, every theme of `Tint.Themes`, live highlighting as you type, a token
inspector and permalinks. It is deployed from `main` to <https://sebastienros.github.io/tint/> by
[`.github/workflows/pages.yml`](../../.github/workflows/pages.yml).

## Run it locally

```bash
dotnet run --project samples/Tint.Playground
```

Then open <http://localhost:5180>. No workload is needed: the development server runs the app interpreted.

## Publish

The deployed site is compiled to WebAssembly ahead of time, trimmed, Brotli and gzip compressed, with invariant
globalization. Ahead-of-time compilation needs the WebAssembly build tools:

```bash
dotnet workload install wasm-tools
```

```bash
dotnet publish samples/Tint.Playground -c Release -o artifacts/playground -p:TintPlaygroundAot=true
```

The site is `artifacts/playground/wwwroot`; serve it from `/` as is, or change `<base href="/">` in its `index.html`
for another path (the Pages workflow writes `/tint/`). Without `-p:TintPlaygroundAot=true` the publish is trimmed but
interpreted, and needs no workload (delete `samples/Tint.Playground/obj` when switching between the two: an
interpreted publish over the native files of an AOT one fails to start). `RunAOTCompilation` is behind that switch because the SDK asks for the workload as
soon as it is set, even for a plain build.

The emscripten toolchain of the workload does not support a .NET installation path with spaces on macOS (its `clang`
wrapper does not quote its own path); install the SDK in a path without spaces to publish with AOT there.

## How it works

- `Tint.Playground.Core` holds the logic without UI, tested by `test/Tint.Tests/Playground`: the permalink state
  (`#lang=csharp&theme=dark-plus&theme2=light-plus&code=…`, the code as base64url of its UTF-8, or `~` and the
  base64url of its raw deflate when that is shorter), the language and theme lists, the token lookup by offset and
  the explanation of a token's style by the theme's rules.
- `PlaygroundEngine` registers every grammar in one `GrammarRegistry`, resolves each theme once, and keeps the eight
  most recently used `Highlighter`s, one per grammar and theme(s).
- The code box is a transparent `<textarea>` over the highlighted `<pre>`, both owned by `wwwroot/js/playground.js`:
  typing calls back into .NET 50 ms after the last key and the script writes the returned HTML, so Blazor never
  re-renders the code. Scrolling the textarea scrolls the `pre`.
- The inspector shows the token under the mouse, or at the caret: its text, colors, font style, scope stack, and the
  theme rules that set its foreground and font style. The tokenizer keeps the resolved style at every level of the
  scope stack, so the deepest level whose style differs from its parent's names the scope that set it, and the rule is
  the most specific selector matching that scope and value. The grammar rule that produced a token is not exposed by
  Tint's public API, so the inspector does not show it.
- The status line shows the line and token counts, the time of `Highlighter.Tokenize` and of `Highlighter.ToHtml`
  (which tokenizes again, once per theme), measured with `Stopwatch`, and the time from the page's first script to
  the first highlight.
- With a second theme, the HTML carries its colors as `--tint-dark` or `--tint-light` variables (after the theme's
  kind) and the "HTML output" panel shows the CSS that switches to it; the toolbar toggle previews it with the same
  rules.
- The samples are shiki's (MIT), copied at build time from `test/Tint.Tests/Fixtures/samples/` to
  `wwwroot/samples/<language id>.txt` and fetched on demand.
