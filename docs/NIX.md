# Nix dev environment

`flake.nix` provides a reproducible dev shell and a couple of helper apps, so you don't need the .NET SDK, `act`, `grpcurl`, etc. installed globally to work on this repo. None of it is required - the plain `dotnet` CLI (see the [README](../README.md)) works fine on its own - but it saves pinning versions yourself.

**Prerequisite:** [Nix](https://nixos.org/download/) with flakes enabled (`experimental-features = nix-command flakes` in `nix.conf`, or pass `--extra-experimental-features "nix-command flakes"` on each command below).

## Entering the shell

```bash
nix develop
```

Drops you into a shell with everything `.nix/shell.nix` lists on `PATH`: the pinned .NET 10 SDK (`dotnet-sdk_10`), `icu`/`openssl`/`zlib` (.NET's native dependencies, also wired into `LD_LIBRARY_PATH`), `act` (see [CI](CI.md)), `grpcurl` (see the [README](../README.md)), `tshark` (inspect gRPC/gRPC-Web wire traffic), `jq`, `yq-go`, and `python3` (the latter two mainly for poking at `.github/workflows/dotnet.yml` and running the coverage-gate/license-check snippets embedded in it outside of a full `act` run), and a `jdk` - not used by anything above, but SonarLint's C#/.NET analyzer runs on the JVM, so the IDE plugin (VS Code, JetBrains, Visual Studio) needs a JDK on `PATH` to run any analysis at all. The shell hook also puts `~/.dotnet/tools` on `PATH`, for any `dotnet tool install --global` tools.

**Automatic, via direnv:** `.envrc` is just `use flake`. With [direnv](https://direnv.net/) installed, `direnv allow` once in the repo root, and the shell above loads automatically on `cd` into the directory (and unloads on `cd` out) - no need to remember to run `nix develop` yourself. `.direnv/` (its cache) is gitignored.

## Apps

```bash
nix run .#format      # dotnet format . - applies fixes across the whole solution (not --verify-no-changes;
                       # that's what CI's "Check formatting" step runs instead, see docs/CI.md)

nix run .#ci-local     # runs .github/workflows/dotnet.yml locally via act (needs Docker running); see docs/CI.md
                       # for what it covers, its caveats, and passing extra `act` flags after `--`
```

## `nix fmt`

Formats this repo's own `*.nix` files (`nixfmt`, set as the flake's `formatter`) - unrelated to the C# solution, which `nix run .#format` handles instead.
