# CI

`.github/workflows/dotnet.yml` runs two jobs on every push/PR to `main`:

- **`build-and-test`**: restore → format check → NuGet vulnerability scan → build (Release, warnings as errors, versioned from the run number + commit sha) → test with coverage → source SBOM + license check → publish each of the four executables (the Host and every `Adapter.*`) as a downloadable artifact.
- **`docker-images`** (`needs: build-and-test`, for its version metadata only): a `strategy.matrix` over the four executables, each with its own colocated Dockerfile (`src/simulator/TacticalApi.Simulator.Host/Dockerfile`, `src/adapter/TacticalApi.Simulator.Adapter.OpenSky/Dockerfile`, etc. - a project's container build lives with its own code, not as a shared build stage picked by `--target`), that per project builds (but doesn't push) a Docker image tagged with the computed version, scans it for CVEs, and generates an image-level SBOM - all four run in parallel.

`build-and-test` used to also be three parallel jobs (`format-check`, `build-and-test`, `sbom`), each with its own checkout/restore; they were merged into one to cut that down to a single restore instead of three — the trade-off is that a formatting typo fails after the whole pipeline runs (a few minutes) instead of in ~15 seconds, since steps within one job are always sequential. `docker-images` is deliberately its *own* job rather than more `build-and-test` steps: each `docker build` is self-contained - it does its own restore+build+publish from scratch, so it needs nothing `build-and-test` produced - and splitting it out lets the four images build/scan concurrently instead of one after another. Every one of the four Dockerfiles still works standalone for anyone who wants to build just that image without touching the rest of this pipeline: `docker build -f src/<project>/Dockerfile .`, run from the repo root (the build context has to be the repo root - each project needs `Directory.Build.props`/`Directory.Packages.props`/`global.json` and the whole `src/` tree for its project references, not just its own directory).

A third job, `contract-drift-check`, only runs on the weekly schedule (or manual `workflow_dispatch`): it moves the `external/tacticalapi` submodule to upstream `main` and builds against it, as an early warning for upstream TacticalAPI contract changes.

The .NET SDK install + NuGet cache steps are factored into a shared composite action (`.github/actions/setup-build-env`).

## Formatting: three checks, one source of truth

`.editorconfig` is the single source of truth for whitespace/charset conventions across every file in the repo, not just `*.cs` - but no single tool both understands every one of those file types *and* applies its formatting rules correctly, so `build-and-test` runs three separate formatting steps instead:

1. **`dotnet format --verify-no-changes`** - C# style (`csharp_style_*` in `.editorconfig`) plus whitespace, syntax-aware.
2. **`nixfmt --check`** over every `*.nix` file - syntax-aware Nix formatting. Pinned to the same nixfmt version (`v1.2.0`) that `.nix/shell.nix`'s nixpkgs input resolves today, verified against a hardcoded sha256, downloaded as [NixOS/nixfmt](https://github.com/NixOS/nixfmt)'s standalone static release binary rather than via Nix itself - this workflow otherwise needs no Nix install at all (see [docs/NIX.md](NIX.md)). Keep that pin and this one in sync if `nixpkgs` moves to a newer nixfmt.
3. **[editorconfig-checker](https://editorconfig-checker.github.io/)** over every tracked file - charset, line endings, trailing whitespace, and final-newline, checked against whatever `.editorconfig` section matches each file. Downloaded the same way as nixfmt (pinned version `3.8.0` - the version `nix run .#editorconfig-check`'s locked nixpkgs input resolves today - sha256-verified release tarball, no Nix needed).

`nix run .#format` applies all three sets of fixes locally in one command (`dotnet format .`, then `nixfmt` on every `*.nix` file, then the same charset/EOL/trailing-whitespace/final-newline fixes on every other tracked text file - skipping binaries the same way `git grep -I` does). `nix run .#editorconfig-check` is its read-only counterpart (`nixfmt --check` + `editorconfig-checker`, mirroring steps 2-3 above; step 1 is still just `dotnet format --verify-no-changes`).

editorconfig-checker's own **Indentation and IndentSize checks are disabled** (`.editorconfig-checker.json`) repo-wide, on all three tools' checks combined coverage rather than relying on this one for the two languages that already have (1) and (2) above: that checker only verifies that each line's leading whitespace is a multiple of `indent_size` - it has no concept of a Markdown fenced code block's own embedded-language indentation (this repo's directory-tree diagrams in `docs/ARCHITECTURE.md` are one example) or a hanging/aligned continuation line in C# or a Dockerfile `LABEL ... \` continuation (both present in this repo too) - all three are valid, common formatting that isn't a multiple of any fixed `indent_size`. Enabling it produced dozens of false positives against exactly those three patterns when first tried; the two checks it can't get right for this repo's file mix are turned off, and indentation correctness for `.cs`/`.nix` is left to tools (1) and (2) that actually parse the language. `.editorconfig`'s `indent_size` declarations still stand for every file type - editors (VS Code, JetBrains, etc.) read them directly for auto-indent-on-newline regardless of whether this checker verifies them.

Adding a new file type: add (or adjust) its `.editorconfig` section first - that's what both editorconfig-checker and every editor read - then only add a fourth CI step if the new type needs its own syntax-aware formatter the way C# and Nix do.

## Supply chain checks: SBOM, licenses, vulnerabilities

Within `build-and-test`:

1. **Vulnerable NuGet packages**: `dotnet list package --vulnerable --include-transitive`, failing the job if any project reports one. This is partly redundant with `NuGetAuditMode=all` in `Directory.Build.props` (set alongside this - restore-time NuGet Audit only checks *direct* PackageReferences by default, and `TreatWarningsAsErrors=true` already turns a detected vulnerability into a build failure) - the explicit step exists to give it a dedicated, readable report instead of a warning buried in build output.
2. **Source SBOM**: generates a [CycloneDX](https://cyclonedx.org/) 1.7 JSON SBOM via the `CycloneDX` dotnet tool (`dotnet-CycloneDX <solution> --exclude-test-projects --exclude-dev --set-version ...`), scoped to the Host, every `Adapter.*`, and their `Sources.*` dependencies — what's actually in the four Docker images' application layers, not the whole solution's dev/test tooling. `--exclude-dev` drops any package flagged `developmentDependency` in its own nuspec (e.g. `SonarAnalyzer.CSharp`, a `PrivateAssets="all"` Roslyn analyzer referenced repo-wide in `Directory.Build.props` - build-time only, never published into an image); without it, the license allow-list step below fails on such a package's own license even though nothing ships it. Uploaded as a downloadable artifact (`sbom-<run>-<sha>`).
3. **License allow-list**: a small inline Python script (same style as the coverage gate) reads the source SBOM's `components[].licenses` and fails the job if any component has no license metadata or a license outside the allow-list (`MIT`, `Apache-2.0`, `BSD-2-Clause`, `BSD-3-Clause`, `ISC`, `0BSD`). Extend the `ALLOWED` set in that step if a dependency with a different-but-acceptable license is intentionally added.
4. **Docker image CVE scan** (in `docker-images`, once per matrix entry): after each image is built, the official Trivy image pulled straight from Docker Hub scans it against the local Docker socket (`docker run --rm -v /var/run/docker.sock:/var/run/docker.sock aquasec/trivy:latest image ...`), rather than the `aquasecurity/trivy-action` marketplace action - that action's bundled `setup-trivy` step needs `secrets.GITHUB_TOKEN` to fetch the Trivy binary from GitHub Releases, which this workflow otherwise doesn't need at all (see the caveats below). Covers both OS-level packages in the base image and .NET/NuGet dependencies (`--severity CRITICAL,HIGH --ignore-unfixed`); the vulnerability DB comes from a public `mirror.gcr.io` mirror, no auth either.
5. **Docker image SBOM** (also per matrix entry): a second Trivy invocation (`--format cyclonedx`, no vuln scanning) generates an SBOM *of the built image itself* — this is what the source SBOM in step 2 can't see: the OS packages baked into the base layer (`mcr.microsoft.com/dotnet/aspnet` for the host, the smaller `mcr.microsoft.com/dotnet/runtime` for adapters - plain console apps, no ASP.NET Core needed), not just the .NET/NuGet dependency graph. Uploaded as `image-sbom-<name>-<run>-<sha>`. Written via stdout redirection (`> image-sbom.json`), not Trivy's own `--output <path>` writing through a `-v host:/container` bind mount — that mount is resolved by the Docker daemon, which under act's nested-container setup doesn't agree with the calling shell on what "the host path" even is, so the file silently never appeared (caught by testing, not assumed).

The `CycloneDX` install command is `dotnet tool install --global CycloneDX`, but the resulting command is `dotnet-CycloneDX`, not `cyclonedx` — verified directly (a first attempt using the shorter name failed with "command not found").

## Running CI locally

The whole workflow can run locally in Docker via [`act`](https://github.com/nektos/act) — no need to push a branch to see if CI passes.

**Prerequisites:** Docker running on the host. `act` itself is already available inside the Nix dev shell (`.nix/shell.nix`); outside the shell, run it via `nix run .#ci-local`.

```bash
# Run everything act can run for a push event (build-and-test and
# docker-images; contract-drift-check is skipped since it only triggers on
# schedule/dispatch)
nix run .#ci-local

# Equivalent, if you're already inside the Nix dev shell
act
```

The runner image is pinned in `.actrc` (`catthehacker/ubuntu:act-latest`) — act's default "micro" image lacks Node.js and a Docker CLI, which this workflow's composite action and `docker build` step both need. The Docker build step works because act's job containers get the host's Docker socket mounted automatically with this image, the same way GitHub-hosted runners have Docker preinstalled.

Caveats:

- `actions/cache` works, but the cache lives in act's own local storage, not GitHub's — it won't share hits with real CI runs.
- Nothing in this workflow needs `secrets.GITHUB_TOKEN` or any other secret, so there's no credential setup required.
- `actions/upload-artifact@v7` does **not** work under plain `act` — it needs `ACTIONS_RUNTIME_TOKEN`, which act only provides via `--artifact-server-path <dir>`, and even with that flag the upload still fails against act's bundled artifact server (a protocol mismatch with newer `upload-artifact` versions, confirmed by testing both ways). Every step *before* each upload still runs and reports correctly locally (confirmed by temporarily swapping the upload steps for a plain `ls` and running the affected job through - everything up to and including generating both SBOMs and scanning a Docker image passes); only the upload itself is act's limitation, not a workflow bug.
