# Tint

**TextMate syntax colorization for .NET, built for speed, NativeAOT and trimming.**

Tint tokenizes and colors source code with the same grammars and themes VS Code uses. Grammars and
themes are compiled into your assembly by a source generator and validated at build time; regexes
run on [GregsEx](https://github.com/sebastienros/gregsex), a managed Oniguruma-compatible engine,
with optional ahead-of-time compiled kernels. There is no native dependency and no JSON parsing at
runtime, so it runs anywhere .NET runs, including WebAssembly.

**Status: 0.1.0 preview.** [![NuGet.org](https://img.shields.io/nuget/v/Tint.svg?label=nuget.org)](https://www.nuget.org/packages/Tint) [![Feedz preview](https://img.shields.io/endpoint?url=https%3A%2F%2Ff.feedz.io%2Fsebastienros%2Ftint%2Fshield%2FTint%2Flatest)](https://f.feedz.io/sebastienros/tint/nuget/index.json) Releases are `v*.*.*` tags on nuget.org; `main` publishes previews to Feedz ([docs/release.md](docs/release.md)).

## Install

```bash
dotnet add package Tint            # runtime, loaders, renderers and the source generator
dotnet add package Tint.Grammars   # optional: the VS Code grammars, already generated
dotnet add package Tint.Themes     # optional: the VS Code color themes, already generated
```

## Quick start

Import the grammars and themes you need. The source generator turns each file into a class at
build time and checks every regex, so a bad pattern is a build error, not a runtime surprise.

```xml
<ItemGroup>
  <TintGrammar Include="grammars/csharp.tmLanguage.json" />
  <TintGrammar Include="grammars/JSON.tmLanguage.json" LanguageId="json" FileExtensions=".json;.jsonc" />
  <TintTheme Include="themes/dark_plus.json" />
  <!-- dark_plus.json includes dark_vs.json: make it visible to the generator without a class of its own. -->
  <AdditionalFiles Include="themes/dark_vs.json" />
</ItemGroup>
```

Bind a file to a class of your own with an attribute (the path is relative to the project), or let
the item alone generate one: `source.json` becomes `JsonGrammar` and `dark_plus.json` becomes
`DarkPlusTheme`, in the project's root namespace.

```csharp
using Tint;

[TextMateGrammar("grammars/csharp.tmLanguage.json", LanguageId = "csharp", FileExtensions = [".cs"])]
public partial class CSharpGrammar;

var registry = new GrammarRegistry();
registry.Add(CSharpGrammar.Instance, JsonGrammar.Instance);
var darkPlus = Theme.FromSource(DarkPlusTheme.Instance); // resolve once, share everywhere

var highlighter = registry.CreateHighlighter("source.cs", darkPlus);
string html = highlighter.ToHtml(code);        // <pre class="tint" …><code><span class="line">…
Console.Write(highlighter.ToAnsi(code));       // 24-bit terminal colors
highlighter.ToHtml(code.AsSpan(), textWriter); // or stream to a TextWriter / IBufferWriter<char>
```

A `Highlighter` owns its tokenizers, so it is not thread-safe: keep one per thread and reuse it.

Patterns are parsed on first use. For NativeAOT or WebAssembly apps with a few hot grammars, opt
into ahead-of-time regex kernels with `Kernels = true` on the attribute, `Kernels="true"` on the
item, or `<TintKernels>true</TintKernels>` for the project. Items, metadata, diagnostics and
kernels are described in [docs/generator.md](docs/generator.md).

Grammar files come from any TextMate source: the
[VS Code extensions](https://github.com/microsoft/vscode/tree/main/extensions) or shiki's
[textmate-grammars-themes](https://github.com/shikijs/textmate-grammars-themes). JSON and plist
grammars, VS Code JSON themes (with `include`) and `.tmTheme` files are supported.

### Bundled grammars and themes

`Tint.Grammars` and `Tint.Themes` ship the VS Code grammars (84 files, from 51 extensions) and its
19 color themes already generated, for quick starts, demos and the
[playground](https://sebastienros.github.io/tint/). No items or generator setup needed.

```bash
dotnet add package Tint.Grammars
dotnet add package Tint.Themes
```

```csharp
using Tint;
using Tint.Grammars;
using Tint.Themes;

var registry = new GrammarRegistry();
registry.Add(TintGrammars.CSharp, TintGrammars.Json); // or registry.Add([.. TintGrammars.All])

var tokenizer = registry.CreateTokenizer("source.cs", Theme.FromSource(TintThemes.DarkPlus));

IGrammarSource? markdown = TintGrammars.Find("text.html.markdown"); // by scope name: TintGrammars.Markdown
IThemeSource? monokai = TintThemes.Find("Monokai");                // by theme name or property name
```

Each grammar and theme is a class (`CSharpGrammar`, `DarkPlusTheme`, ...) with a shared
`Instance`; `TintGrammars` and `TintThemes` expose one property per class, named after it without
the suffix (`TintGrammars.CSharp`, `TintGrammars.Markdown`, `TintGrammars.TypeScriptReact`,
`TintThemes.DarkPlus`, `TintThemes.LightVisualStudio`), `All` and `Find`. Grammar classes are named
after their language (`ShellScriptGrammar` for `shellscript`, `PugGrammar` for `jade`); grammars
without one, included by others, after their scope (`HtmlBasicGrammar`, `Yaml12Grammar`,
`JsDocTypeScriptInjectionGrammar`). Patterns are parsed on first use.

Only the classes you reference stay in a trimmed or NativeAOT build: an app that uses
`TintGrammars.CSharp`, `TintGrammars.Json` and `TintThemes.DarkPlus` trims `Tint.Grammars.dll`
from 5.2 MB to 146 KB. Reading `All` or calling `Find` keeps every class, as it must. A grammar
that includes another (Markdown fenced blocks, HTML with CSS and JavaScript) needs that one
registered too; `registry.GetMissingGrammars(scopeName)` lists what is missing.

### Tokens

For editors and custom renderers, work with tokens directly. Line state carries over between
lines, so a document can be re-tokenized incrementally from the first changed line.

```csharp
var tokenizer = registry.CreateTokenizer("source.cs", DarkPlusTheme.Instance);
var state = StateStack.Initial;

foreach (var line in document.Lines)
{
    foreach (var token in tokenizer.TokenizeLine(line, state, out state))
    {
        // token.Start, token.Length, token.Scopes, token.Foreground, token.FontStyle
    }
}
```

`TokenizeLineEncoded` returns `(start, metadata)` pairs in the VS Code binary layout for hosts that
already consume Monaco-style tokens. `highlighter.Tokenize(code)` returns one `HighlightedToken[]`
per line with the colors resolved (`Start`, `Length`, `Color`, `BackgroundColor`, `FontStyle`,
`Scopes`).

A `Tokenizer` is not thread-safe and builds its regex scanners as it reaches rules, so create one per
thread or per open document and keep it alive; the compiled grammar behind it is shared.

### HTML

`ToHtml` writes shiki-compatible markup: the theme's colors on the `pre`, one `<span class="line">`
per line, one `<span style="…">` per run of tokens with the same style holding only what differs from
the theme's defaults, text HTML-escaped. `HtmlRendererOptions` sets the `pre` class, `data-line`
line numbers, and a CSS-class mode with a matching stylesheet:

```csharp
var highlighter = registry.CreateHighlighter("source.cs", darkPlus, new HighlighterOptions
{
    Html = new HtmlRendererOptions { StyleMode = HtmlStyleMode.CssClasses }, // class="tint-fg-4 tint-b"
});
string css = highlighter.GetStyleSheet();
```

With a light and a dark theme, the light colors are written inline and the dark ones as CSS
variables (`--tint-dark`, `--tint-dark-bg`, `--tint-dark-font-style`…), so one CSS rule switches
themes:

```csharp
var highlighter = registry.CreateHighlighter("source.cs",
    light: Theme.FromSource(LightPlusTheme.Instance), dark: darkPlus);

string html = highlighter.ToHtml(code);
```

```css
@media (prefers-color-scheme: dark) {
  .tint, .tint span {
    color: var(--tint-dark) !important;
    background-color: var(--tint-dark-bg) !important;
    font-style: var(--tint-dark-font-style) !important;
    font-weight: var(--tint-dark-font-weight) !important;
    text-decoration: var(--tint-dark-text-decoration) !important;
  }
}
```

More named themes work the same way (`CreateHighlighter(scope, defaultTheme, [new("dim", dim)])`
writes `--tint-dim`). `HtmlRenderer` and `AnsiRenderer` in `Tint.Rendering` can also be used
directly with tokenizers of your own.

### Loading grammar files at runtime

When grammars are only known at runtime, load them instead of generating them. Same tokens,
regexes compiled on first use.

```csharp
var grammar = GrammarLoader.Load("my-language.tmLanguage.json");
registry.Add(grammar);
```

## Build

```bash
dotnet build
dotnet test
dotnet run -c Release --project benchmarks/Tint.Benchmarks
```

Grammars and themes under `grammars/` and `themes/` are synced from a pinned VS Code commit, which
also regenerates `THIRD-PARTY-NOTICES.txt`:

```bash
dotnet run --project tools/Tint.Sync -- sync --commit 3ac0f056
```

The [Sync VS Code grammars and themes](.github/workflows/sync-vscode.yml) workflow runs this
every Monday against VS Code `main` and opens a pull request from the `sync/vscode` branch when
anything changed.

Design notes, the plan and research are indexed in [docs/README.md](docs/README.md).

## License

MIT. Bundled grammars and themes keep their original licenses, listed in
[THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
