# Tint repository instructions

- Read [docs/design.md](docs/design.md) and [docs/plan.md](docs/plan.md) before changing code. The
  tokenizer follows `vscode-textmate` semantics exactly; when in doubt, the reference is
  [docs/research/vscode-textmate.md](docs/research/vscode-textmate.md) and the vscode-textmate test
  suites under `test/Tint.Tests/Fixtures/vscode-textmate/`.
- Performance comes first: no allocations on the per-line path once warm, no reflection, no runtime
  code generation, no `dynamic`. BenchmarkDotNet `[MemoryDiagnoser]` is the allocation authority.
  Measure before and after any change to a hot path and put the numbers in the pull request.
- Keep `IsAotCompatible` and `IsTrimmable` on every shipped assembly and the build warning-free
  (`TreatWarningsAsErrors`). The one exception is the `netstandard2.0` target of `Tint` (for .NET
  Framework hosts), which the AOT and trimming analyzers do not support; the modern targets keep both. System.Text.Json goes through a source-generated context.
- GregsEx is the only regex engine. Patterns are Oniguruma syntax parsed by GregsEx; the only
  rewrites are the `\xHH` → `\x{HH}` pre-processing, the `\z` rewrite and the `\A`/`\G` anchor
  variants described in the design. Never forward matching to `System.Text.RegularExpressions`.
- Layout: `src/` (shipped), `test/` (xunit.v3 on Microsoft.Testing.Platform, sentence-style test
  names, plain `Assert`), `benchmarks/`, `samples/`, `tools/`, `grammars/` and `themes/` (synced
  from VS Code with provenance by `tools/Tint.Sync`; never hand-edit), `docs/`. The scheduled
  `sync-vscode.yml` workflow re-runs the sync weekly and opens a pull request from the long-lived
  `sync/vscode` branch; review and merge that pull request instead of syncing by hand.
- Code style is `.editorconfig`: 4-space C#, `var` everywhere, braces always, file-scoped
  namespaces, `_camelCase` fields. Run `dotnet format Tint.slnx --severity warn --verify-no-changes`
  before opening a pull request (after `dotnet build src/Tint.Generator`: format loads the Debug
  configuration, and the generator sample needs the generator's Debug build).
- Preserve fixture provenance and license notices. Regenerate `THIRD-PARTY-NOTICES.txt` with
  `tools/Tint.Sync` when grammars or themes change.
- Work on a branch, open a pull request against `main`, never push to `main` directly. Do not
  publish packages or deploy the playground manually; CI does that on tags and on `main`.
- Never describe skipped or unrun checks as passing.
