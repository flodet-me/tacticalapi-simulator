# CI

`.github/workflows/dotnet.yml` runs three jobs in parallel on every push/PR to `main`:

- `format-check`: `dotnet format --verify-no-changes` — fails fast on style/formatting drift without waiting on the full build.
- `build`: just `dotnet build`, nothing else — the fastest possible "does it still compile" signal, and the one to reach for when you just want to confirm a local build isn't broken (`nix run .#ci-local -- -j build`).
- `build-and-test`: restore → build (Release, warnings as errors, versioned from the run number + commit sha) → test with coverage → publish the host as a downloadable artifact → build (but not push) a Docker image tagged with that same version, using the `Dockerfile` at the repo root.

A third job, `contract-drift-check`, only runs on the weekly schedule (or manual `workflow_dispatch`): it moves the `external/tacticalapi` submodule to upstream `main` and builds against it, as an early warning for upstream TacticalAPI contract changes.

The .NET SDK install + NuGet cache steps are factored into a shared composite action (`.github/actions/setup-build-env`) so the SDK version and cache key are defined once, not per job.

## Running CI locally

The whole workflow can run locally in Docker via [`act`](https://github.com/nektos/act) — no need to push a branch to see if CI passes.

**Prerequisites:** Docker running on the host. `act` itself is already available inside the Nix dev shell (`.nix/shell.nix`); outside the shell, run it via `nix run .#ci-local`.

```bash
# Run everything act can run for a push event ( build-and-test,
# format-check; contract-drift-check is skipped since it only triggers on
# schedule/dispatch)
nix run .#ci-local

# Run a single job - "build" is the quick "is the local build broken" check
nix run .#ci-local -- -j build
nix run .#ci-local -- -j format-check
nix run .#ci-local -- -j build-and-test

# Equivalent, if you're already inside the Nix dev shell
act -j build
```

The runner image is pinned in `.actrc` (`catthehacker/ubuntu:act-latest`) — act's default "micro" image lacks Node.js and a Docker CLI, which this workflow's composite action and `docker build` step both need. The Docker build step works because act's job containers get the host's Docker socket mounted automatically with this image, the same way GitHub-hosted runners have Docker preinstalled.

Caveats:

- `actions/cache` works, but the cache lives in act's own local storage, not GitHub's — it won't share hits with real CI runs.
- Nothing in this workflow needs `secrets.GITHUB_TOKEN` or any other secret, so there's no credential setup required.
