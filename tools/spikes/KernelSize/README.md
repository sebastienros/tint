# Kernel-size spike (P2)

Throwaway projects behind [docs/research/kernel-spike.md](../../../docs/research/kernel-spike.md):
how big and how slow to build are GregsEx source-generated kernels for whole TextMate grammars, and
what does the lazy `OnigurumaPattern.Parse` fallback cost instead. Standalone: not part of any
solution, never built by CI. They inherit the repository's `Directory.Build.props`,
`Directory.Packages.props` (GregsEx `0.5.0`) and `nuget.config`.

| Path | What |
| --- | --- |
| `samples/lines.txt` | 20 lines of `mediumfile.cs` from danipen/TextMateSharp (`src/TextMateSharp.Demo/testdata/samplefiles/`, adapted from Avalonia/WPF, MIT). |
| `Emit/` | Walks the rules of the grammars passed as arguments, applies the `\xHH`, `\z` and `\A`/`\G` pre-processing of the design, de-duplicates and writes one pattern method per distinct text, plus a `.tsv` manifest. |
| `Spike.CSharp/`, `Spike.TypeScript/`, `Spike.Both/` | `[GeneratedPattern]` kernels for the C# grammar, the TypeScript grammar, and both (de-duplicated) in one assembly. |
| `Spike.CSharpLazy/` | The C# grammar as lazy `OnigurumaPattern.Parse` methods (no attribute). |
| `Spike.Cpp/` | Extra stress case beyond the brief: the C++ grammar's kernels (long patterns, many `\A` variants). |
| `Measure/` | First-use cost, allocations, kernel/interpreter cross-check and matching throughput for the C# grammar. |
| `AotProbe/` | NativeAOT binary size and cold cost of the C# kernels, the lazy patterns, or GregsEx alone. |
| `measure_build.py`, `run_measure.py` | Drivers. `measure_build.py` writes `results/build-<project>.json` (not committed); `run_measure.py` writes `results/measure.json`. |
| `results/` | `measure.json`, `measure-auto-compilation.jsonl` and `aot.json` from the run the report quotes. |

The grammars are the repository's own copies under `grammars/` (microsoft/vscode @ `3ac0f056`):
`csharp/syntaxes/csharp.tmLanguage.json`, `typescript-basics/syntaxes/TypeScript.tmLanguage.json`
and `cpp/syntaxes/cpp.tmLanguage.json`. The emitted `Spike.*/*Patterns.cs` and `*Patterns.tsv`
files are not committed (see `.gitignore`); run `Emit` first.

## Reproduce

From this directory, on a clean checkout:

```bash
dotnet build Emit -c Release
```

```bash
dotnet Emit/bin/Release/net10.0/Emit.dll --class CSharpPatterns --namespace Tint.Spike --out Spike.CSharp/CSharpPatterns.cs ../../../grammars/csharp/syntaxes/csharp.tmLanguage.json
```

```bash
dotnet Emit/bin/Release/net10.0/Emit.dll --class TypeScriptPatterns --namespace Tint.Spike --out Spike.TypeScript/TypeScriptPatterns.cs ../../../grammars/typescript-basics/syntaxes/TypeScript.tmLanguage.json
```

```bash
dotnet Emit/bin/Release/net10.0/Emit.dll --class BothPatterns --namespace Tint.Spike --out Spike.Both/BothPatterns.cs ../../../grammars/csharp/syntaxes/csharp.tmLanguage.json ../../../grammars/typescript-basics/syntaxes/TypeScript.tmLanguage.json
```

```bash
dotnet Emit/bin/Release/net10.0/Emit.dll --lazy --class CSharpLazyPatterns --namespace Tint.Spike --out Spike.CSharpLazy/CSharpLazyPatterns.cs ../../../grammars/csharp/syntaxes/csharp.tmLanguage.json
```

```bash
dotnet Emit/bin/Release/net10.0/Emit.dll --class CppPatterns --namespace Tint.Spike --out Spike.Cpp/CppPatterns.cs ../../../grammars/cpp/syntaxes/cpp.tmLanguage.json
```

Build tables (sizes, build times, GREGEX003 declines, analyzer/generator time; ~15 minutes):

```bash
python3 -I measure_build.py Spike.CSharp Spike.TypeScript Spike.Both Spike.CSharpLazy Spike.Cpp
```

First use and throughput (`results/measure.json`):

```bash
python3 -I run_measure.py
```

The same with GregsEx runtime Auto compilation on (`results/measure-auto-compilation.jsonl`):

```bash
dotnet build Measure -c Release -p:GregsExEnableAutoCompilation=true -o artifacts/measure-auto
```

```bash
dotnet artifacts/measure-auto/Measure.dll throughput
```

NativeAOT (`results/aot.json`; run each binary a few times), for `Variant` = `none`, `lazy`, `kernel`:

```bash
dotnet publish AotProbe -c Release -p:Variant=kernel -o AotProbe/pub/kernel
```

```bash
AotProbe/pub/kernel/AotProbe
```
