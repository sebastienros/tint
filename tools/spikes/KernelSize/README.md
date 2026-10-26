# Kernel-size spike (P2)

Throwaway projects behind [docs/research/kernel-spike.md](../../../docs/research/kernel-spike.md):
how big and how slow to build are GregsEx source-generated kernels for whole TextMate grammars, and
what does the lazy `OnigurumaPattern.Parse` fallback cost instead. Standalone: not part of any
solution, never built by CI. They inherit the repository's `Directory.Build.props`,
`Directory.Packages.props` (GregsEx `0.5.0`) and `nuget.config`.

| Path | What |
| --- | --- |
| `grammars/` | `csharp.tmLanguage.json`, `TypeScript.tmLanguage.json` and `cpp.tmLanguage.json` from microsoft/vscode @ `3ac0f056` (`extensions/{csharp,typescript-basics,cpp}/syntaxes/`; converted from dotnet/csharp-tmLanguage, microsoft/TypeScript-TmLanguage and jeff-hykin/better-cpp-syntax, all MIT, as each file's `information_for_contributors` and `version` say). |
| `samples/lines.txt` | 20 lines of `mediumfile.cs` from danipen/TextMateSharp (`src/TextMateSharp.Demo/testdata/samplefiles/`, adapted from Avalonia/WPF, MIT). |
| `Emit/` | Walks a grammar's rules, applies the `\xHH`, `\z` and `\A`/`\G` pre-processing of the design, de-duplicates and writes one pattern method per distinct text, plus a `.tsv` manifest. |
| `Spike.CSharp/`, `Spike.TypeScript/`, `Spike.Both/` | `[GeneratedPattern]` kernels for the C# grammar, the TypeScript grammar, and both (de-duplicated) in one assembly. |
| `Spike.CSharpLazy/` | The C# grammar as lazy `OnigurumaPattern.Parse` methods (no attribute). |
| `Spike.Cpp/` | Extra stress case beyond the brief: the C++ grammar's kernels (long patterns, many `\A` variants). |
| `AotProbe/` | NativeAOT binary size and cold cost: `dotnet publish AotProbe -c Release -p:Variant=kernel\|lazy\|none`. |
| `Measure/` | First-use cost, allocations, kernel/interpreter cross-check and matching throughput for the C# grammar. |
| `measure_build.py`, `run_measure.py` | Drivers; write `results/*.json`. |

Reproduce (from this directory):

```bash
dotnet build Emit -c Release
dotnet Emit/bin/Release/net10.0/Emit.dll --class CSharpPatterns --namespace Tint.Spike --out Spike.CSharp/CSharpPatterns.cs grammars/csharp.tmLanguage.json
dotnet Emit/bin/Release/net10.0/Emit.dll --class TypeScriptPatterns --namespace Tint.Spike --out Spike.TypeScript/TypeScriptPatterns.cs grammars/TypeScript.tmLanguage.json
dotnet Emit/bin/Release/net10.0/Emit.dll --class BothPatterns --namespace Tint.Spike --out Spike.Both/BothPatterns.cs grammars/csharp.tmLanguage.json grammars/TypeScript.tmLanguage.json
dotnet Emit/bin/Release/net10.0/Emit.dll --class CppPatterns --namespace Tint.Spike --out Spike.Cpp/CppPatterns.cs grammars/cpp.tmLanguage.json
dotnet Emit/bin/Release/net10.0/Emit.dll --lazy --class CSharpLazyPatterns --namespace Tint.Spike --out Spike.CSharpLazy/CSharpLazyPatterns.cs grammars/csharp.tmLanguage.json
python3 -I measure_build.py Spike.CSharp Spike.TypeScript Spike.Both Spike.CSharpLazy Spike.Cpp
python3 -I run_measure.py
```

The emitted `*.cs` and `*.tsv` files are committed so the libraries build without running `Emit`.
