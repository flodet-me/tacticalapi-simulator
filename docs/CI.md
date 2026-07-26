# CI

`.github/workflows/dotnet.yml` runs two jobs on every push/PR to `main`:

- **`build-and-test`**: restore → format check → NuGet vulnerability scan → build (Release, warnings as errors, versioned from the run number + commit sha) → test with coverage → source SBOM + license check → publish each of the four executables (the Host and every `Adapter.*`) as a downloadable artifact.
- **`docker-images`** (`needs: build-and-test`, for its version metadata only): a `strategy.matrix` over the four executables, each with its own colocated Dockerfile (`src/TacticalApi.Simulator.Host/Dockerfile`, `src/TacticalApi.Simulator.Adapter.OpenSky/Dockerfile`, etc. - a project's container build lives with its own code, not as a shared build stage picked by `--target`), that per project builds (but doesn't push) a Docker image tagged with the computed version, scans it for CVEs, and generates an image-level SBOM - all four run in parallel.

`build-and-test` used to also be three parallel jobs (`format-check`, `build-and-test`, `sbom`), each with its own checkout/restore; they were merged into one to cut that down to a single restore instead of three — the trade-off is that a formatting typo fails after the whole pipeline runs (a few minutes) instead of in ~15 seconds, since steps within one job are always sequential. `docker-images` is deliberately its *own* job rather than more `build-and-test` steps: each `docker build` is self-contained - it does its own restore+build+publish from scratch, so it needs nothing `build-and-test` produced - and splitting it out lets the four images build/scan concurrently instead of one after another. Every one of the four Dockerfiles still works standalone for anyone who wants to build just that image without touching the rest of this pipeline: `docker build -f src/<project>/Dockerfile .`, run from the repo root (the build context has to be the repo root - each project needs `Directory.Build.props`/`Directory.Packages.props`/`global.json` and the whole `src/` tree for its project references, not just its own directory).

A third job, `contract-drift-check`, only runs on the weekly schedule (or manual `workflow_dispatch`): it moves the `external/tacticalapi` submodule to upstream `main` and builds against it, as an early warning for upstream TacticalAPI contract changes.

The .NET SDK install + NuGet cache steps are factored into a shared composite action (`.github/actions/setup-build-env`).

## Supply chain checks: SBOM, licenses, vulnerabilities

Within `build-and-test`:

1. **Vulnerable NuGet packages**: `dotnet list package --vulnerable --include-transitive`, failing the job if any project reports one. This is partly redundant with `NuGetAuditMode=all` in `Directory.Build.props` (set alongside this - restore-time NuGet Audit only checks *direct* PackageReferences by default, and `TreatWarningsAsErrors=true` already turns a detected vulnerability into a build failure) - the explicit step exists to give it a dedicated, readable report instead of a warning buried in build output.
2. **Source SBOM**: generates a [CycloneDX](https://cyclonedx.org/) 1.7 JSON SBOM via the `CycloneDX` dotnet tool (`dotnet-CycloneDX <solution> --exclude-test-projects --set-version ...`), scoped to the Host, every `Adapter.*`, and their `Sources.*` dependencies — what's actually in the four Docker images' application layers, not the whole solution's dev/test tooling. Uploaded as a downloadable artifact (`sbom-<run>-<sha>`).
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
