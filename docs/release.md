# Releasing Tint

`Tint`, `Tint.Grammars` and `Tint.Themes` ship together, with the same version. A release is a `vX.Y.Z` tag on a
commit of `main`; CI does the rest. Nobody packs or pushes packages by hand.

## One-time setup

- nuget.org: a trusted publishing policy for the `sebastienros` account, repository `sebastienros/tint`, workflow
  `publish.yml`, environment `release`. The workflow exchanges its GitHub OIDC token for a short-lived API key
  (`NuGet/login`); there is no long-lived nuget.org key in the repository.
- GitHub: an environment named `release` (add required reviewers to make each release wait for an approval).
- Feedz: the `FEEDZ_IO_API_KEY` repository secret, for previews. Without it the preview push is skipped.

## Before tagging

1. `main` is green: `build.yml` (ubuntu and windows: build, tests, formatting) and `aot.yml` (trimmed and NativeAOT
   smoke apps on linux-x64 and osx-arm64).
2. `CHANGELOG.md` has a section for the version, describing what changed for users.
3. The public surface is the one intended: review `PublicAPI.Unshipped.txt` in `src/Tint`, `src/Tint.Grammars` and
   `src/Tint.Themes`, and `PublicAPI.Generated.txt` in the two bundles.
4. Optionally, a corpus check and a short fuzz campaign (`tools/Tint.CorpusCheck`, `tools/Tint.Fuzz`) when the
   tokenizer, GregsEx or the grammars changed since the last report in `docs/research/`.

## Tagging

Tag the release commit of `main` with an annotated tag and push the tag:

```bash
git fetch origin
git tag -a v0.1.0 -m "Tint 0.1.0" origin/main
git push origin v0.1.0
```

Tags are semantic versions: `v1.2.3`, or `v1.2.3-preview.1` for a prerelease on nuget.org.

## What CI does

[`publish.yml`](../.github/workflows/publish.yml) runs on `v*.*.*` tags:

1. `aot`: calls [`aot.yml`](../.github/workflows/aot.yml), which publishes `test/Tint.TrimSmoke` trimmed and
   NativeAOT on linux-x64 and osx-arm64 and runs both; the trimmed `Tint.Grammars.dll` and `Tint.Themes.dll` must
   keep only the classes the app uses. Nothing is published unless this passes.
2. `build`, in the `release` environment:
   - validates the tag: a semantic version, on a commit contained in `origin/main`; the version is the tag without
     its `v`;
   - restores, builds and tests the whole solution in Release with `-p:Version=<version>`;
   - packs the three packages (`.nupkg` and `.snupkg`) into `artifacts/`;
   - attests their build provenance (`actions/attest-build-provenance`);
   - logs in to nuget.org through trusted publishing and pushes every `artifacts/*.nupkg` with `--skip-duplicate`
     (the symbol packages next to them go along), so re-running a failed workflow is safe.

## Verifying

- nuget.org lists the version on [Tint](https://www.nuget.org/packages/Tint),
  [Tint.Grammars](https://www.nuget.org/packages/Tint.Grammars) and [Tint.Themes](https://www.nuget.org/packages/Tint.Themes)
  (indexing takes a few minutes). On each page: the dependencies are `GregsEx` and, for the bundles, `Tint` at the
  same version; the README, icon, MIT license and repository link show; "Source Link" and "Deterministic" are
  green in the NuGet Package Explorer health checks.
- The provenance of a downloaded package:

  ```bash
  gh attestation verify Tint.0.1.0.nupkg --repo sebastienros/tint
  ```

- A clean consumer works:

  ```bash
  dotnet new console -o tint-check && cd tint-check
  dotnet add package Tint.Grammars --version 0.1.0
  dotnet add package Tint.Themes --version 0.1.0
  ```

  then highlight a line with `TintGrammars.CSharp` and `TintThemes.DarkPlus` as in the README, and publish it with
  `-p:PublishAot=true` once.

## After the release

1. Move every line of each `PublicAPI.Unshipped.txt` (after `#nullable enable`) to the matching
   `PublicAPI.Shipped.txt`.
2. Set `<PackageValidationBaselineVersion>` to the released version on the three packages, so package validation
   reports breaking changes against it from then on.
3. Bump `<VersionPrefix>` in `Directory.Build.props` to the next version, so previews sort above the release.
4. Start the next `CHANGELOG.md` section.

## Previews on Feedz

Every push to `main` (and manual runs of `build.yml`) packs the three packages as
`<VersionPrefix>-preview.<run number>` and pushes them to the `sebastienros/tint` Feedz feed with
`--skip-duplicate`. To use them, add the feed next to nuget.org with package source mapping:

```xml
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="tint" value="https://f.feedz.io/sebastienros/tint/nuget/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
    <packageSource key="tint">
      <package pattern="Tint" />
      <package pattern="Tint.*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```

and reference a preview version (`dotnet add package Tint --prerelease`).

## Branch ruleset (once the repository is public)

Rulesets need a public repository (or a paid plan). Apply one ruleset to the default branch:

- block deletion;
- block non-fast-forward pushes (force pushes);
- require a pull request before merging (no required approvals while there is one maintainer);
- optionally, require the `build` and `aot-matrix` checks to pass.

```bash
gh api repos/sebastienros/tint/rulesets --method POST --input - <<'EOF'
{
  "name": "main",
  "target": "branch",
  "enforcement": "active",
  "conditions": { "ref_name": { "include": ["~DEFAULT_BRANCH"], "exclude": [] } },
  "rules": [
    { "type": "deletion" },
    { "type": "non_fast_forward" },
    {
      "type": "pull_request",
      "parameters": {
        "required_approving_review_count": 0,
        "dismiss_stale_reviews_on_push": false,
        "require_code_owner_review": false,
        "require_last_push_approval": false,
        "required_review_thread_resolution": false
      }
    }
  ]
}
EOF
```

The weekly `sync-vscode.yml` workflow pushes to `sync/vscode` and opens a pull request, so it is not affected.
A second ruleset on tags (`refs/tags/v*`, rules `deletion` and `non_fast_forward`) keeps released tags from being
moved or deleted.
