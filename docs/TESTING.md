# Testing

Two test layers, both run by `dotnet test`:

- **Unit tests** (`TacticalApi.Simulator.Tests`) cover the store semantics (merge, last-write-wins, delete, expiry, object cap), every merger (via `AllMergers` a meta-test asserts full oneof coverage), the event broker, and the track mapping. Source-specific unit test coverage is documented in each source project's own README.
- **E2E tests** (`TacticalApi.Simulator.E2ETests`) boot the *real* host via `WebApplicationFactory<Program>` and exercise it through *real* gRPC calls on an in-memory transport: add/get round-trip, partial merge over the wire, delete, error headers, snapshot-then-live-events on the subscribe stream, stale-update rejection, the gRPC-Web transport (same path as the official Rheinmetall test client), the expiry sweeper, and the HTTP status endpoint. Background sources are disabled by default in the fixture so tests stay deterministic; individual tests opt back in via configuration overrides. `AdapterIntegrationE2ETests` goes further, composing a real Host (real Kestrel socket) and a real `Adapter.Synthetic` in-process - two separate DI containers talking only over gRPC, exactly like running them as separate processes - to prove each synthetic scenario end-to-end over the wire: the base scenario (all eleven object types), the convoy escort (route, vehicles, a guaranteed ambush, the SALUTE report), and the combat outpost defense (perimeter/OPs, the defend task, a guaranteed ground assault, the SITREP). See `Sources.Synthetic`'s README for what each scenario models.

Run locally with the same coverage gate as CI:

```bash
dotnet test --settings coverlet.runsettings --collect:"XPlat Code Coverage"
```
