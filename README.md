# Tint

**TextMate syntax colorization for .NET, built for speed, NativeAOT and trimming.**

Tint tokenizes and colors source code with the same grammars and themes VS Code uses. Grammars and
themes are compiled into your assembly by a source generator and validated at build time; regexes
run on [GregsEx](https://github.com/sebastienros/gregsex), a managed Oniguruma-compatible engine,
with optional ahead-of-time compiled kernels. There is no native dependency and no JSON parsing at
runtime, so it runs anywhere .NET runs, including WebAssembly.

> Tint is under active development. The API below describes the intended 0.1.0 surface.

## Install

```bash
dotnet add package Tint
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

var highlighter = registry.CreateHighlighter("source.cs", DarkPlusTheme.Instance);
string html = highlighter.ToHtml(code);
```

Patterns are parsed on first use. For NativeAOT or WebAssembly apps with a few hot grammars, opt
into ahead-of-time regex kernels with `Kernels = true` on the attribute, `Kernels="true"` on the
item, or `<TintKernels>true</TintKernels>` for the project. Items, metadata, diagnostics and
kernels are described in [docs/generator.md](docs/generator.md).

Grammar files come from any TextMate source: the
[VS Code extensions](https://github.com/microsoft/vscode/tree/main/extensions) or shiki's
[textmate-grammars-themes](https://github.com/shikijs/textmate-grammars-themes). JSON and plist
grammars, VS Code JSON themes (with `include`) and `.tmTheme` files are supported.

### Bundled grammars and themes

`Tint.Grammars` and `Tint.Themes` ship the VS Code grammars and themes already generated, for
quick starts, demos and the [playground](https://sebastienros.github.io/tint/).

```bash
dotnet add package Tint.Grammars
dotnet add package Tint.Themes
```

```csharp
var registry = new GrammarRegistry();
registry.Add(TintGrammars.All);          // or registry.Add(TintGrammars.CSharp, TintGrammars.Json)

var highlighter = registry.CreateHighlighter("source.ts", TintThemes.GitHubDark);
Console.Write(highlighter.ToAnsi(code)); // terminal output
```

Only the grammars you reference stay in a trimmed or NativeAOT build.

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
already consume Monaco-style tokens.

A `Tokenizer` is not thread-safe and builds its regex scanners as it reaches rules, so create one per
thread or per open document and keep it alive; the compiled grammar behind it is shared.

### HTML with light and dark themes

```csharp
var highlighter = registry.CreateHighlighter("source.cs",
    light: LightPlusTheme.Instance, dark: DarkPlusTheme.Instance);

string html = highlighter.ToHtml(code); // colors as CSS variables, switch with one CSS rule
```

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
