# CI

`.github/workflows/dotnet.yml`, on every push/PR to `main`:

```text
 build-and-test ──► docker-images  (needs: it for version metadata only)
 contract-drift-check   weekly schedule / workflow_dispatch only
```

| Job | Steps |
| --- | --- |
| **`build-and-test`** | restore → format check → NuGet vulnerability scan → build (Release, warnings as errors, versioned from run number + sha) → test with coverage → source SBOM + license check → publish the Host and every `Adapter.*` as a downloadable artifact |
| **`docker-images`** | a `strategy.matrix` over the five container executables, each with its own colocated Dockerfile — all five in parallel: build image tagged with the computed version, scan for CVEs, generate an image-level SBOM |
| **`contract-drift-check`** | moves the `external/tacticalapi` submodule to upstream `main` and builds against it — early warning for upstream contract changes |

A project's container build lives with its own code (`src/simulator/…Host/Dockerfile`, `src/adapter/…Adapter.OpenSky/Dockerfile`, …), not as a shared stage picked by `--target`. Each Dockerfile also works standalone: `docker build -f src/<project>/Dockerfile .` **from the repo root** — the context must be the root, since each project needs `Directory.Build.props`/`Directory.Packages.props`/`global.json` and the whole `src/` tree for its project references.

The SDK install + NuGet cache steps are a shared composite action (`.github/actions/setup-build-env`).

## The `host` matrix entry does two things the others don't

It is the only one of the five with an HTTP surface — the adapters are plain outbound-push console apps — which is also why that job's `Set up build environment` step is gated on `matrix.name == 'host'`.

| Extra step | Detail |
| --- | --- |
| **Smoke test** | Runs the built image, polls `docker inspect`'s own `HEALTHCHECK` until `healthy` (fails after 240s or on `unhealthy`), then confirms `/healthz` and `/metrics` answer from outside the container. On every build, PRs included — it's a build-correctness check, not a publish step. |
| **Conformance suite** | Runs `Tool.Conformance` against a second container started from the built image. |

Conformance runs against the **image, not the source tree**: that image is the artifact that ships, and the suite is this repo's own statement about what the contract requires — so a change that quietly breaks a rule fails here instead of surprising whoever integrates against the published image. It goes over gRPC-Web (the official test client's transport, which the plain-HTTP smoke test does not exercise), with `--include-slow` and a shortened `Simulator__ExpirySweepInterval`, and with `--strict`: advisory severity exists so the tool doesn't call *somebody else's* implementation non-conformant over behavior the contract leaves open, but this repo's image is the reference and should hold every reading the suite documents, all eleven object types included. The JUnit report is uploaded as an artifact.

On a `push` to `main` only (never on `pull_request`), a passing scan is pushed to GHCR (`ghcr.io/<owner>/<image>`, versioned tag + `:latest`) with the run's own `GITHUB_TOKEN`. Note the job's `permissions:` block restates `contents: read` alongside `packages: write` — setting `permissions` at all *replaces* the default grant rather than extending it.

## Why the jobs are split this way

| Decision | Reason |
| --- | --- |
| `format-check` + `build-and-test` + `sbom` merged into one job | One restore instead of three. Trade-off: a formatting typo fails after the whole pipeline (minutes) instead of in ~15s, since steps in a job are sequential. |
| `docker-images` kept separate | Each `docker build` is self-contained (its own restore+build+publish), so it needs nothing `build-and-test` produced — and splitting lets the images build and scan concurrently. |

## Formatting: five checks, one source of truth

`.editorconfig` governs every file in the repo, not just `*.cs` — but no single tool both understands all those file types *and* applies their rules correctly, so there are five steps:

| # | Check | Scope |
| --- | --- | --- |
| 1 | `dotnet format --verify-no-changes` | C# style (`csharp_style_*`) + whitespace, syntax-aware |
| 2 | `nixfmt --check` | every `*.nix`, syntax-aware |
| 3 | `dprint check` | every `*.json` — canonical formatting (`dprint.json`) |
| 4 | `markdownlint-cli2` | every `*.md` — heading levels, fence languages, table and list structure (`.markdownlint-cli2.jsonc`) |
| 5 | [editorconfig-checker](https://editorconfig-checker.github.io/) | every tracked file: charset, line endings, trailing whitespace, final newline |

(3) and (4) are the structural layer (5) cannot see: it checks whitespace, not whether a JSON file is canonically formatted or a markdown heading level skips a step.

(2), (3) and (5) are downloaded as sha256-verified standalone release binaries rather than via Nix — this workflow otherwise needs no Nix install at all ([NIX.md](NIX.md)). Versions are pinned to whatever the locked `nixpkgs` input resolves today (nixfmt `v1.5.0`, dprint `0.57.4`, editorconfig-checker `4.0.2`); keep the pins in sync if `nixpkgs` moves.

**(4) is the one exception to that pattern.** markdownlint-cli2 is Node-based with no standalone binary, so it installs from npm at an exact pinned version (`0.23.2`) instead — the runner image already has Node for the actions themselves. dprint's json *plugin* is the other gap: it is fetched from `plugins.dprint.dev` at run time, pinned by version in `dprint.json` but not sha256-verified.

Markdown rule choices worth knowing (`.markdownlint-cli2.jsonc`): `MD013` (line length) is **off**, because these docs are deliberately table-heavy and a table row is one line however long its cells are; `MD024` is `siblings_only`, so each source README can have its own "Configuration" heading.

```bash
nix run .#format              # applies every fixable set locally
nix run .#editorconfig-check  # read-only counterpart: (2)-(5); (1) is plain dotnet format
```

**editorconfig-checker's `Indentation`/`IndentSize` checks are disabled** repo-wide (`.editorconfig-checker.json`). It only verifies that leading whitespace is a multiple of `indent_size`, with no concept of a Markdown fenced block's embedded-language indentation (the directory trees in [ARCHITECTURE.md](ARCHITECTURE.md)), a hanging/aligned C# continuation, or a Dockerfile `LABEL ... \` continuation — all three present here, all valid, none a multiple of any fixed size. Enabling it produced dozens of false positives against exactly those patterns. Indentation for `.cs`/`.nix`/`.json`/`.md` is left to tools (1)-(4), which parse the language; `.editorconfig`'s `indent_size` still stands for editors' auto-indent regardless.

Adding a new file type: adjust its `.editorconfig` section first — that is what the checker *and* every editor read — and add a sixth CI step only if it needs its own syntax-aware formatter the way C#, Nix, JSON and Markdown do. XML (`*.csproj`, `*.props`, `*.slnx`, `*.runsettings`) deliberately has none: those 19 files are already uniformly 2-space and no standalone XML formatter is worth a pinned binary for them.

## Supply chain: SBOM, licenses, vulnerabilities

| # | Check | Notes |
| --- | --- | --- |
| 1 | Vulnerable NuGet packages | `dotnet list package --vulnerable --include-transitive`, fails the job on any hit. Partly redundant with `NuGetAuditMode=all` + `TreatWarningsAsErrors` (restore-time audit covers only *direct* references by default); the explicit step exists for a readable report instead of a warning buried in build output. |
| 2 | Source SBOM | [CycloneDX](https://cyclonedx.org/) 1.7 JSON via `dotnet-CycloneDX <solution> --exclude-test-projects --exclude-dev --set-version …`, scoped to the Host, every `Adapter.*` and their `Sources.*` — what is actually in the images' application layers. `--exclude-dev` drops packages flagged `developmentDependency` (e.g. `SonarAnalyzer.CSharp`, build-time only); without it the license step below fails on a package nothing ships. Uploaded as `sbom-<run>-<sha>`. |
| 3 | License allow-list | Inline Python over the SBOM's `components[].licenses`; fails on missing metadata or a license outside `MIT`, `Apache-2.0`, `BSD-2-Clause`, `BSD-3-Clause`, `ISC`, `0BSD`. Extend `ALLOWED` in that step when adding an acceptable dependency with another license. |
| 4 | Image CVE scan (per matrix entry) | Official Trivy image from Docker Hub against the local Docker socket, *not* the `aquasecurity/trivy-action` marketplace action — its bundled `setup-trivy` needs `secrets.GITHUB_TOKEN` to fetch the binary, which this workflow otherwise never needs. Covers base-image OS packages and .NET deps (`--severity CRITICAL,HIGH --ignore-unfixed`); DB from the public `mirror.gcr.io` mirror, no auth. |
| 5 | Image SBOM (per matrix entry) | A second Trivy run (`--format cyclonedx`, no vuln scan) — this is what (2) cannot see: the OS packages baked into the base layer (`dotnet/aspnet` for the host, the smaller `dotnet/runtime` for adapters). Uploaded as `image-sbom-<name>-<run>-<sha>`. |

Two findings worth not rediscovering:

- Step 5 writes via **stdout redirection** (`> image-sbom.json`), not Trivy's `--output <path>` through a bind mount. That mount is resolved by the Docker daemon, which under act's nested-container setup disagrees with the calling shell about what "the host path" is, so the file silently never appeared.
- The install command is `dotnet tool install --global CycloneDX`, but the resulting command is **`dotnet-CycloneDX`**, not `cyclonedx`.

## Running CI locally

Needs Docker running on the host. `act` is in the Nix dev shell; outside it, use the app.

```bash
nix run .#ci-local   # everything act can run for a push event
act                  # equivalent, inside the Nix dev shell
```

`contract-drift-check` is skipped (schedule/dispatch only). The runner image is pinned in `.actrc` (`catthehacker/ubuntu:act-latest`) — act's default "micro" image lacks Node.js and a Docker CLI, which the composite action and `docker build` both need. With this image, job containers get the host's Docker socket mounted automatically, like a GitHub-hosted runner.

| Caveat | |
| --- | --- |
| `actions/cache` | Works, but into act's own local storage — no hits shared with real CI. |
| Secrets | None needed anywhere in this workflow. |
| `actions/upload-artifact@v7` | Does **not** work under plain `act`: it needs `ACTIONS_RUNTIME_TOKEN`, which act supplies only via `--artifact-server-path <dir>`, and even then the upload fails against act's bundled artifact server (protocol mismatch with newer versions, confirmed both ways). Every step *before* each upload runs and reports correctly — verified by swapping the uploads for a plain `ls` and running the job through, SBOM generation and image scanning included. Act's limitation, not a workflow bug. |
