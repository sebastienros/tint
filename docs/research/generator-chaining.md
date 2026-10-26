# Can Tint's generator emit `[GeneratedPattern]` methods for GregsEx's generator?

Experiment run on 2026-10-25 with Roslyn 5.0 (SDK 10.0.400) and GregsEx 0.5.0: a tiny incremental
generator emits

```csharp
public static partial class Emitted
{
    [GregsEx.SourceGeneration.GeneratedPattern("[a-z]+(ing|ed)", GregsEx.OnigurumaOptions.CaptureGroup)]
    public static partial GregsEx.OnigurumaPattern Word();
}
```

into a project that also references GregsEx (whose `PatternGenerator` runs in the same build), next
to an identical hand-written `Direct.Word()`.

| Where the emit happens | GregsEx sees it? | Result |
|---|---|---|
| `RegisterPostInitializationOutput` | **Yes** | `Demo.Emitted.Word.g.cs` generated, `ExecutionBackend == Compiled` at runtime |
| `RegisterSourceOutput` (main pipeline, here depending on `AdditionalTextsProvider`) | **No** | `CS8795: Partial method 'Emitted.Word()' must have an implementation part` |

Why: the generator driver collects every generator's post-initialization sources, adds them to the
compilation, and only then runs the main pipelines of all generators against that compilation. Main
pipeline outputs are added after all generators have run and are never fed back.

Why this does not solve Tint's problem: `IncrementalGeneratorPostInitializationContext` exposes only
`AddSource` and a cancellation token. It has no `AdditionalFiles`, no analyzer config options, no
compilation and no project path, so it cannot read a grammar file. Reading files from disk inside a
generator is also what Roslyn's extended analyzer rules forbid (RS1035): no incrementality, stale
output in the IDE, no way to know the project directory. A grammar class must be produced in the main
pipeline, where its `[GeneratedPattern]` methods are invisible to GregsEx's generator.

Conclusion: kernels have to be emitted by Tint's generator itself, through a public GregsEx façade
(sebastienros/gregsex#26), or patterns stay interpreted.

The experiment projects are not kept in the repository; the table above is what matters.
