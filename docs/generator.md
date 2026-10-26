# Source generator

`Tint.Generator` is a Roslyn incremental source generator shipped inside the `Tint` package
(`analyzers/dotnet/cs`). It turns TextMate grammar and theme files into `IGrammarSource` and
`IThemeSource` classes compiled into your assembly: no JSON at run time, every regex validated at
build time, patterns parsed on first use, and optional ahead-of-time GregsEx kernels. The design
behind it is in [design.md](design.md) sections 4, 5.5 and 8.

## Items

`Tint.props` (imported from the package's `build/` and `buildTransitive/` folders) defines two item
types. They only make the files visible to the compiler as `AdditionalFiles` with a `TintKind`:

```xml
<ItemGroup>
  <TintGrammar Include="grammars/csharp.tmLanguage.json" />
  <TintTheme Include="themes/dark_plus.json" />
  <!-- Included by dark_plus.json; an AdditionalFiles item so it gets no class of its own. -->
  <AdditionalFiles Include="themes/dark_vs.json" />
</ItemGroup>
```

Inputs: `.tmLanguage.json` and other `.json` grammars (comments and trailing commas allowed),
plist grammars (`.tmLanguage`, `.plist`), VS Code JSON themes with `include` and `tokenColors`
paths, and `.tmTheme` themes. As in the runtime loaders, a file is JSON when its name ends with
`.json` and a property list otherwise.

Theme `include` and `tokenColors` paths resolve relative to the including file among the project's
`AdditionalFiles` (a `TintTheme` item or a plain `AdditionalFiles` item). A missing include is
`TINT008`.

## Attributes

The generator adds two attributes to the compilation, `internal`, marked `[Embedded]` and
`[Conditional("TINT_KEEP_ATTRIBUTES")]`, so they cost nothing at run time and never clash across
assemblies:

```csharp
using Tint;

[TextMateGrammar("grammars/csharp.tmLanguage.json", Kernels = true, LanguageId = "csharp")]
public partial class CSharpGrammar;

[TextMateTheme("themes/dark_plus.json")]
internal partial class DarkPlus;
```

The class must be a non-generic, non-static `partial class` (it may be nested in other partial
types). It binds to the `AdditionalFiles` item whose path, relative to the project directory,
equals the attribute's path (an absolute path is taken as is); separators and `.`/`..` segments are
normalized, and the comparison ignores case. A path may start with `$(TintGrammarsSourcesPath)` or
`$(TintThemesSourcesPath)`, expanded as MSBuild expands it in an item (see
[VS Code sources packages](#vs-code-sources-packages)). A relative path that matches no item that way
binds the one item whose path ends with it, so `"csharp/syntaxes/csharp.tmLanguage.json"` finds an
item included from a package folder; when several items end with it, nothing binds. The class keeps
its own name, namespace and accessibility. A path that matches no item is `TINT001`.

`TextMateGrammarAttribute` properties: `Kernels`, `LanguageId`, `Aliases`, `FileExtensions`,
`EmbeddedLanguages` (`scope=languageId` pairs), `InjectTo`, `InjectionSelector`, `TokenTypes`
(`selector=comment|string|regex|other` pairs), `BalancedBracketScopes`, `UnbalancedBracketScopes`.
They win over the item's metadata of the same name. `TextMateThemeAttribute` has `ThemeMode`.

## Item-only classes

A `TintGrammar` or `TintTheme` item that no attribute binds generates a class by itself, in the
project's `RootNamespace`:

| Input | Class |
|---|---|
| grammar `source.cs` | `CSharpGrammar` |
| grammar `source.json` | `JsonGrammar` |
| grammar `text.html.basic` | `HtmlBasicGrammar` |
| theme `dark_plus.json` | `DarkPlusTheme` |

Grammar names are the scope segments after the first in PascalCase, with a few language
abbreviations spelled out (`cs` → `CSharp`, `js` → `JavaScript`, `ts` → `TypeScript`, `fs` →
`FSharp`, `vb` → `VisualBasic`, ...), plus `Grammar`. Theme names are the file name in PascalCase
plus `Theme`. Two items that produce the same fully qualified name are `TINT002`; set `Name` or
`Namespace` on one of them.

## Item metadata

| Metadata | Applies to | Meaning |
|---|---|---|
| `Name` | both | Class name of an item-only class. |
| `Namespace` | both | Namespace of an item-only class (default: `RootNamespace`). |
| `Accessibility` | both | `internal` (default) or `public`, for item-only classes. |
| `Kernels` | grammars | `true` or `false`: ahead-of-time kernels for this grammar (default: `TintKernels`). |
| `LanguageId` | grammars | `GrammarInfo.LanguageId`. |
| `Aliases` | grammars | `GrammarInfo.Aliases`. |
| `FileExtensions` | grammars | `GrammarInfo.FileExtensions`, with the leading dot. |
| `EmbeddedLanguages` | grammars | `scope=languageId` pairs. |
| `InjectTo` | grammars | Scope names this grammar injects into. |
| `InjectionSelector` | grammars | Injection selector when the file has none. |
| `TokenTypes` | grammars | `selector=type` pairs, the type one of `comment`, `string`, `regex`, `other`. |
| `BalancedBracketScopes` | grammars | Selectors of tokens with balanced brackets (default: every scope). |
| `UnbalancedBracketScopes` | grammars | Selectors of tokens without balanced brackets. |
| `ThemeMode` | themes | `VSCode` (default): VS Code's default rule from `editor.foreground`/`editor.background` and no other scopeless rules. `Raw`: the rules as written, as vscode-textmate's tests use them. |

Lists are MSBuild lists (`.json;.jsonc`); commas work too. `Tint.props` rewrites the semicolons to
commas, and `#` and `%` to `%23` and `%25` (the generator decodes them), because the analyzer config
file the compiler reads cuts values at `;` and `#` (`C#` would arrive as `C`). An entry that does
not parse is `TINT010` and is ignored. `GrammarInfo.Dependencies` is computed from the grammar's
includes of other scopes.

## Project properties

| Property | Meaning |
|---|---|
| `TintKernels` | `true` turns kernels on for every grammar of the project without its own `Kernels` value. |
| `TintGrammarsClass` | Name of a static class to generate listing every grammar of the compilation (see below); `My.Namespace.Name` picks the namespace, otherwise `RootNamespace`. |
| `TintThemesClass` | The same for themes. |
| `TintGregsExPackagePath` | The GregsEx package directory (the one holding `lib/` and `analyzers/`), when it cannot be derived from the `GregsEx.dll` reference. |

`RootNamespace` and `ProjectDir` are read as well (the SDK makes both visible to the compiler).

### Aggregate classes

With `<TintGrammarsClass>TintGrammars</TintGrammarsClass>` the generator adds:

```csharp
public static partial class TintGrammars
{
    public static IReadOnlyList<IGrammarSource> All { get; }      // every grammar, ordered by scope name
    public static IGrammarSource? Find(string scopeName);          // FrozenDictionary, ordinal
    public static CSharpGrammar CSharp => CSharpGrammar.Instance;  // one property per class
    ...
}
```

`TintThemesClass` does the same for themes, ordered by class name, with `Find(string name)`
matching the theme's `name` or its property name (`Dark+` or `DarkPlus`), ignoring case. Property
names are class names without the `Grammar`/`Theme` suffix; a name that would be empty, start with a
digit, or clash with `All`, `Find`, the class or another property keeps the full class name (then a
number). The class is `public` when every listed class is public, otherwise `internal`. `All` and
`Find` live in nested holder classes, so an application that only reads `TintGrammars.CSharp` keeps
only `CSharpGrammar` when trimmed. `Tint.Grammars` and `Tint.Themes` are built this way.

## Generated code

A grammar class implements `IGrammarSource` with `public static X Instance`, `ScopeName`, `Info` (a
static `GrammarInfo`) and `GetGrammar()`, which builds the `RawGrammar` once with object
initializers that mirror the file (one `RawRule` per rule, repositories as `FrozenDictionary`,
includes through the `IncludeReference` factories). Generated and loaded grammars come from one
reading of the file: the generator compiles `GrammarLoader.ReadGrammar`, the theme reader,
`DocumentTree` and `PatternRewriter` from `src/Tint` (`TINT_GENERATOR`), with a small JSON reader
in place of System.Text.Json.

Every regex is `RegexSource.FromRewritten(text, hasAnchorA, hasAnchorG, hasBackReferences,
factory)` with the `\z`, `\xHH` and `\x{7fffffff}` rewrites already applied. The pattern table has
one entry per distinct text, including each `\A`/`\G` variant a pattern needs, resolved at
generation time. Entries parse on first use with `OnigurumaPattern.Parse(text,
RegexProfiles.OnigurumaTextMate, OnigurumaCompileOptions.TextMate)`, and bind kernels when they
were emitted. `end` and `while` patterns with back-references have no factory: the runtime resolves
and compiles them per match, as it does for loaded grammars. So does a pattern that failed
validation (it never matches, as with the loader). `public static FrozenDictionary<string, int>
PatternIndex` maps each table text to its index.

The texts are stored as one UTF-8 literal with an offset table and decoded on first use: half the
size of UTF-16 string literals for these mostly ASCII patterns. Measured on the VS Code C++ grammar
(385 table entries): 545 KB of string literals become 273 KB, and the assembly goes from 773 KB to
499 KB; the C# grammar goes from 165 KB to 135 KB. A grammar whose texts contain a lone surrogate,
which has no UTF-8 form, keeps string literals.

A theme class implements `IThemeSource` with `Instance`, `Name` and `GetTheme()`, which returns a
`RawTheme` built from literals: the rules with includes flattened and the `ThemeMode` applied at
generation time, and the UI colors as a `FrozenDictionary`.

Generated files start with `// <auto-generated/>`, `#nullable enable` and `#pragma warning disable`,
use only global-qualified names, and use no reflection, so they compile under the consumer's
`TreatWarningsAsErrors` and nullable settings and stay trimming and NativeAOT safe.

## Kernels

Kernels are opt-in (design 5.5): about 3 KB to 11 KB of IL per pattern and a slower build, for a
1.3× geometric-mean speedup on grammar patterns. Turn them on with `Kernels = true` on the
attribute, `Kernels="true"` on the item, or `<TintKernels>true</TintKernels>`. They need a project
targeting .NET 8 or later (the generator checks that `MemoryExtensions.IndexOfAnyInRange` exists)
and a reachable GregsEx; otherwise the grammar is generated without them and `TINT006` says why.
`TINT011` reports how many patterns got kernels and how many GregsEx left to the interpreter.

Measured on the VS Code C# grammar: 293 of 338 table entries get kernels, the generated source
grows from 0.45 MB to 5.8 MB, the assembly from 135 KB to 954 KB, and the Roslyn compile of the
generated code from 0.65 s to 2.8 s.

## How the generator reaches GregsEx

Kernels must come from the GregsEx version the consumer runs (the fingerprint binds a kernel to the
exact parse), and a generator's output is invisible to other generators
([research/generator-chaining.md](research/generator-chaining.md)), so Tint's generator emits them
itself through GregsEx's public `GregsEx.SourceGeneration.KernelSource.TryEmit` façade
(GregsEx 0.5.1 or later). It has no compile-time reference to GregsEx:

1. It finds the compilation's `GregsEx.dll` reference. In a NuGet package that file is
   `<package>/lib/<tfm>/GregsEx.dll`, so the package directory is two levels up, and the façade is
   `<package>/analyzers/dotnet/cs/GregsEx.Generator.dll`. The `TintGregsExPackagePath` property
   replaces this discovery (for a GregsEx project reference, or a custom layout).
2. It loads `GregsEx.Generator.dll` once per path: the copy the compiler already loaded for
   GregsEx's own generator when there is one (same location, or same assembly version), otherwise
   `Assembly.LoadFile`.
3. It calls `TryEmit(pattern, OnigurumaOptions.CaptureGroup, plainClass, captureClass,
   OnigurumaCompileOptions.TextMate)` by reflection, reading the enum value and the compile options
   from the loaded assembly.

Every table entry goes through `TryEmit`, kernels or not, as build-time validation: `Invalid` is
`TINT004` with the rule path (for example `repository.comment.begin`), the GregsEx offset and
message; `Declined` and `Succeeded` are valid. When kernels are on and the project targets
.NET 8+, the returned kernel classes go into a second file, `X.Kernels.g.cs`, bound with
`WithGeneratedKernels(fingerprint, Plain.Run, Capture.Run)`.

When GregsEx cannot be located or the façade is missing, or any reflection step fails, the
generator reports `TINT007`, skips validation and kernels, and still generates the grammar: the
lazy parse path needs nothing from the build.

## VS Code sources packages

`Tint.Grammars.Sources` and `Tint.Themes.Sources` carry the grammar and theme files of VS Code (the
ones `Tint.Grammars` and `Tint.Themes` are generated from) as plain files, for projects that generate
exactly the grammars and themes they use, with their own class names, accessibility and kernels.
They contain no assembly and are development dependencies: `dotnet add package` writes
`PrivateAssets="all"`; add it yourself to a hand-written reference so the files stay out of your own
package's dependencies.

```xml
<ItemGroup>
  <PackageReference Include="Tint" Version="..." />
  <PackageReference Include="Tint.Grammars.Sources" Version="..." PrivateAssets="all" />
  <PackageReference Include="Tint.Themes.Sources" Version="..." PrivateAssets="all" />
</ItemGroup>
<ItemGroup>
  <TintGrammar Include="$(TintGrammarsSourcesPath)csharp/syntaxes/csharp.tmLanguage.json" Kernels="true" />
  <TintTheme Include="$(TintThemesSourcesPath)theme-defaults/dark_plus.json" />
  <AdditionalFiles Include="$(TintThemesSourcesPath)theme-defaults/dark_vs.json" />
</ItemGroup>
```

Files keep their VS Code layout, `<extension>/syntaxes/<file>` for grammars and
`<extension>/<file>` for themes, with `manifest.json` (scope names, languages, upstream provenance)
at the root of each folder; `THIRD-PARTY-NOTICES.txt` is under `licenses/`. Each package's
`build/` and `buildTransitive/` props, written by `tools/Tint.Sync` with the bundles' item lists,
define:

| Name | Kind | Meaning |
|---|---|---|
| `TintGrammarsSourcesPath` | Property | The package's `content/grammars/` folder, absolute, ending with a separator. Compiler-visible, so attribute paths can start with it. |
| `TintGrammarsSourcesAll` | Item | Every grammar, with the metadata the `Tint.Grammars` items carry (`Name`, `LanguageId`, `Aliases`, `FileExtensions`, `EmbeddedLanguages`, `InjectTo`, `TokenTypes`, bracket scopes) and no `Accessibility`. |
| `TintThemesSourcesPath` | Property | The package's `content/themes/` folder. |
| `TintThemesSourcesAll` | Item | Every theme, with the `Tint.Themes` class names. |
| `TintThemesSourcesIncludes` | Item | Files themes include that are not themes themselves (empty in the current sync: `dark_vs.json` and `light_vs.json` are themes). |

Nothing is compiled until the project includes it. Pick from the lists with MSBuild item functions,
or take all of them:

```xml
<TintGrammar Include="@(TintGrammarsSourcesAll->WithMetadataValue('LanguageId', 'csharp'))" Kernels="true" />
<TintGrammar Include="@(TintGrammarsSourcesAll->WithMetadataValue('LanguageId', 'json'))" />
<TintTheme Include="@(TintThemesSourcesAll)" />
```

`TINT005` names the file in `Tint.Grammars.Sources` for each missing scope VS Code ships, for
example `source.css ($(TintGrammarsSourcesPath)css/syntaxes/css.tmLanguage.json)`.

## Diagnostics

| Id | Severity | Meaning |
|---|---|---|
| `TINT001` | Error | `[TextMateGrammar]`/`[TextMateTheme]` path matches no `AdditionalFiles` item. |
| `TINT002` | Error | Two items or attributes generate the same fully qualified class name. |
| `TINT003` | Error | The grammar or theme file cannot be read, is not valid JSON or plist, or is not a grammar (line and column when the parser gives one). |
| `TINT004` | Error | Invalid regex: rule path, GregsEx offset and message. The pattern never matches at run time. |
| `TINT005` | Info | The grammar includes other grammars; lists the scopes to register alongside it, with their `$(TintGrammarsSourcesPath)` file when VS Code ships them. |
| `TINT006` | Info | Kernels were requested but are not emitted (GregsEx unreachable, or the project targets an older framework). |
| `TINT007` | Info | GregsEx is not reachable at build time; regexes are not validated. |
| `TINT008` | Error | A theme `include` or `tokenColors` file is not an `AdditionalFiles` item. |
| `TINT009` | Error | The attribute is on a class that is not partial, or is static or generic. |
| `TINT010` | Warning | An item metadata or attribute value does not parse and is ignored. |
| `TINT011` | Info | Kernel summary: patterns compiled ahead of time and patterns left to the interpreter. |

## Known issues

- The Roslyn 5.0 compiler of the .NET 10.0.100 SDK crashes in nullable analysis on method group
  conversions under C# 12 when GregsEx is referenced (fixed in Roslyn 5.3, .NET 10.0.200). Tint's
  generated grammars avoid method groups, but kernel bindings pass GregsEx's `Run` methods as
  method groups, as GregsEx's own generator does: with kernels on, a C# 12 project (the .NET 8
  default) needs SDK 10.0.200 or later.
- `dotnet format` loads the Debug configuration; in this repository build `src/Tint.Generator` in
  Debug first so the sample's generated types resolve.
