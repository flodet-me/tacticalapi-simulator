# TacticalApi.Simulator.Sources.Replay

Capture a TacticalAPI situation to a file and push it back later. Everything else here invents data live; this is the only part that lets the same data happen *twice* — which turns a bug someone hit into a fixture you can run, and a live-API demo into one that works on a train.

Both halves are registered by `AddReplaySources` and run in `Adapter.Replay`, each behind its own `Enabled` flag: one process, two jobs.

## Two ways to record, and which to use

| | `Adapter:Recording` (Core) | `Adapter:Recorder` (here) |
| --- | --- | --- |
| Captures at | An adapter's write path (`ISituationIngest`) | Any endpoint's read path (`SubscribeSituationObjectEvents`) |
| Records | The exact `UpdateSituationObject` sent on the wire | Each object turned back into the update that recreates it |
| Fidelity | Lossless | Faithful, but a reconstruction |
| Scope | Only that adapter's own traffic | Everything in that situation, whoever caused it |
| Runs in | Any `Adapter.*` process | `Adapter.Replay` |

Use `Adapter:Recording` when the traffic you want is your own adapter's — `Adapter__Recording__Enabled=true` on `Adapter.OpenSky` gives an offline OpenSky replay with nothing lost. Use the recorder when you want whatever a *server* is doing: a third-party implementation, or a situation fed by clients you don't control.

## `SituationRecorder`

Subscribes to `Adapter:Ingest:Address` like any other client and writes what it sees.

1. Opens `SubscribeSituationObjectEvents`. The stream opens with the full current situation; `IncludeInitialSnapshot` (default `true`) decides whether that first batch is recorded — i.e. whether a replay reconstructs the situation as it stood or starts from what changed afterwards.
2. Each object is split by `is_deleted`: deleted → a `DeleteSituationObject` (so a replay reproduces the removal instead of resurrecting it), everything else → `SituationObjectToUpdate` (Core).
3. Each received batch becomes one frame, stamped with its offset from the first.
4. A dropped connection is retried every 5s rather than ending the recording — long captures outlive blips.

The conversion is descriptor-driven, not eleven hand-written mappings: the two message families come from the same contract and mirror each other field for field. Three things it cannot carry — `creation_meta_data` (the server stamps its own), `additional_attributes` (documented as created by the underlying service), and all but the first of several `foreign_keys` (one update carries exactly one).

## `ReplayPlayer`

Reads a recording and pushes it back through `ISituationIngest`, so it drives whatever `Adapter:Ingest:Address` points at — including an implementation that is not this repo's Host.

| Setting | Behavior |
| --- | --- |
| `Speed` | `1.0` is the original pace; `10.0` makes ten minutes take one, which is what makes a recording a test fixture rather than only a demo. |
| `TickInterval` | How often the player checks for due frames — the granularity of replay timing. |
| `Loop` | Starts over at the end. Off by default, so a fixture ends when the recording does. |
| `RestampReportingTime` (default `true`) | Moves each `reporting_time` to now **and shifts `expiry_time` by the same amount**. Both halves matter: without the first, last-write-wins rejects an old recording as stale; without the second, every object expires the instant it lands, since `expiry_time` is an absolute instant long past. Shifting preserves what the recording captured — how long each object was meant to live. Turn it off when the server's staleness or expiry handling *is* what's under test. |

It is a `BackgroundService`, not an `ISimulationSource`: a source is a produce-every-N-seconds loop that only emits updates, and a replay is neither — it owns its own clock and reproduces deletes as deletes.

## The recording format

Newline-delimited JSON, one frame per line (`RecordingFormat` in Core), matching the `.jsonl` convention the file log sink uses:

```json
{"seq":1,"offsetMs":0,"updates":[{"symbol":{"identity":{"stringIdentity":"opensky:3c6444"}, "...":"..."}}]}
{"seq":2,"offsetMs":4796,"updates":[...],"deletes":[...]}
```

The envelope is hand-written; the objects inside are canonical protobuf JSON — still the contract's own model, not a re-modelled copy. So a recording is readable, greppable and hand-editable:

```bash
# What is in this recording?
jq -r '.updates[]? | keys[0]' recording.jsonl | sort | uniq -c

# Keep only the first minute.
jq -c 'select(.offsetMs <= 60000)' recording.jsonl > short.jsonl
```

Line-oriented rather than one JSON array, and flushed after every frame, so a recording whose process was killed is usable up to its last whole line — the reader skips an unreadable final line with a warning instead of abandoning the file.

## End to end

```bash
# 1. Host + something producing data.
dotnet run --project src/simulator/TacticalApi.Simulator.Host
dotnet run --project src/adapter/TacticalApi.Simulator.Adapter.Synthetic

# 2. Record what the Host's situation does.
Adapter__Recorder__Enabled=true Adapter__Recorder__Path=recordings/demo.jsonl \
  dotnet run --project src/adapter/TacticalApi.Simulator.Adapter.Replay

# 3. Later: wipe the situation and replay it at 5x, with nothing else running.
curl -X POST http://localhost:4268/api/control/reset
Adapter__Replay__Enabled=true Adapter__Replay__Path=recordings/demo.jsonl Adapter__Replay__Speed=5 \
  dotnet run --project src/adapter/TacticalApi.Simulator.Adapter.Replay
```

Full `Adapter:Recorder` / `Adapter:Replay` reference: [Configuration](../../../docs/CONFIGURATION.md).

## Tests

| Test | Covers |
| --- | --- |
| `RecordingFormatTests`, `RecordingWriterTests` | one line per frame, offsets relative to the first, truncated-line tolerance, the ingest decorator recording exactly what it forwards |
| `SituationObjectToUpdateTests` | **all eleven object types** through store → update → store, requiring equality — what stands between the descriptor-driven converter and a silent mis-mapping |
| `ReplayPlayerTests` | frame ordering, restamping, the expiry shift, deletes replayed as deletes |
| `RecordReplayE2ETests` | a real Host over real gRPC: record, reset, replay, check the situation came back |
