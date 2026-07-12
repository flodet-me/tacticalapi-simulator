# CI

`.github/workflows/dotnet.yml` runs three jobs in parallel on every push/PR to `main`:

- `format-check`: `dotnet format --verify-no-changes` — fails fast on style/formatting drift without waiting on the full build.
- `build-and-test`: restore → build (Release, warnings as errors, versioned from the run number + commit sha) → test with coverage → publish the host as a downloadable artifact → build (but not push) a Docker image tagged with that same version, using the `Dockerfile` at the repo root → scan that image for CVEs (Trivy).
- `sbom`: scans for vulnerable NuGet packages, then generates a CycloneDX SBOM for what's actually shipped (the Host + its `Sources.*` dependencies, excluding dev/test-only tooling) and enforces a license allow-list against it — see [Supply chain checks](#supply-chain-checks-sbom-licenses-vulnerabilities) below.

A fourth job, `contract-drift-check`, only runs on the weekly schedule (or manual `workflow_dispatch`): it moves the `external/tacticalapi` submodule to upstream `main` and builds against it, as an early warning for upstream TacticalAPI contract changes.

The .NET SDK install + NuGet cache steps are factored into a shared composite action (`.github/actions/setup-build-env`) so the SDK version and cache key are defined once, not per job.

## Supply chain checks: SBOM, licenses, vulnerabilities

The `sbom` job:

1. **Vulnerable NuGet packages**: `dotnet list package --vulnerable --include-transitive`, failing the job if any project reports one. This is partly redundant with `NuGetAuditMode=all` in `Directory.Build.props` (set alongside this - restore-time NuGet Audit only checks *direct* PackageReferences by default, and `TreatWarningsAsErrors=true` already turns a detected vulnerability into a build failure) - the explicit step exists to give it a dedicated, readable report instead of a warning buried in build output.
2. **SBOM**: generates a [CycloneDX](https://cyclonedx.org/) 1.7 JSON SBOM via the `CycloneDX` dotnet tool (`dotnet-CycloneDX <solution> --exclude-test-projects --set-version ...`), scoped to the Host and its `Sources.*` dependencies — what's actually in the Docker image, not the whole solution's dev/test tooling. Uploaded as a downloadable artifact (`sbom-<run>-<sha>`).
3. **License allow-list**: a small inline Python script (same style as the coverage gate) reads the SBOM's `components[].licenses` and fails the job if any component has no license metadata or a license outside the allow-list (`MIT`, `Apache-2.0`, `BSD-2-Clause`, `BSD-3-Clause`, `ISC`, `0BSD`). Extend the `ALLOWED` set in that step if a dependency with a different-but-acceptable license is intentionally added.

The `CycloneDX` install command is `dotnet tool install --global CycloneDX`, but the resulting command is `dotnet-CycloneDX`, not `cyclonedx` — verified directly (a first attempt using the shorter name failed with "command not found").

`build-and-test`'s last step scans the built Docker image itself for CVEs, using the official Trivy image pulled straight from Docker Hub against the local Docker socket (`docker run --rm -v /var/run/docker.sock:/var/run/docker.sock aquasec/trivy:latest image ...`), rather than the `aquasecurity/trivy-action` marketplace action - that action's bundled `setup-trivy` step needs `secrets.GITHUB_TOKEN` to fetch the Trivy binary from GitHub Releases, which this workflow otherwise doesn't need at all (see the caveat below). Covers both OS-level packages in the base image and .NET/NuGet dependencies (`--severity CRITICAL,HIGH --ignore-unfixed`); the vulnerability DB itself comes from a public `mirror.gcr.io` mirror, no auth either.

## Running CI locally

The whole workflow can run locally in Docker via [`act`](https://github.com/nektos/act) — no need to push a branch to see if CI passes.

**Prerequisites:** Docker running on the host. `act` itself is already available inside the Nix dev shell (`.nix/shell.nix`); outside the shell, run it via `nix run .#ci-local`.

```bash
# Run everything act can run for a push event (format-check, build-and-test,
# sbom; contract-drift-check is skipped since it only triggers on
# schedule/dispatch)
nix run .#ci-local

# Run a single job
nix run .#ci-local -- -j format-check
nix run .#ci-local -- -j build-and-test
nix run .#ci-local -- -j sbom

# Equivalent, if you're already inside the Nix dev shell
act -j format-check
```

The runner image is pinned in `.actrc` (`catthehacker/ubuntu:act-latest`) — act's default "micro" image lacks Node.js and a Docker CLI, which this workflow's composite action and `docker build` step both need. The Docker build step works because act's job containers get the host's Docker socket mounted automatically with this image, the same way GitHub-hosted runners have Docker preinstalled.

Caveats:

- `actions/cache` works, but the cache lives in act's own local storage, not GitHub's — it won't share hits with real CI runs.
- Nothing in this workflow needs `secrets.GITHUB_TOKEN` or any other secret, so there's no credential setup required.
- `actions/upload-artifact@v7` does **not** work under plain `act` — it needs `ACTIONS_RUNTIME_TOKEN`, which act only provides via `--artifact-server-path <dir>`, and even with that flag the upload still fails against act's bundled artifact server (a protocol mismatch with newer `upload-artifact` versions, confirmed by testing both ways). This affects every job that uploads an artifact (`build-and-test`, `sbom`) — everything *before* the upload step still runs and reports correctly locally, only the upload itself is act's limitation, not a workflow bug.
