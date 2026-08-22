# TacticalApi.Simulator.Sources.Replay

Capture a TacticalAPI situation to a file and push it back later. Everything else in this repository invents data
live; this is the only part that lets the same data happen twice — which is what turns a bug someone hit into a
fixture you can run, and a live-API demo into one that works on a train.

Both halves are registered by `AddReplaySources` (`ReplayServiceCollectionExtensions.cs`) and run in the same
executable, `Adapter.Replay`, each gated by its own `Enabled` flag. One process, two jobs: run it with
`Adapter:Recorder:Enabled` to capture, and again with `Adapter:Replay:Enabled` to play back.

## Two ways to record, and which one to use

|                        | `Adapter:Recording` (Core)                          | `Adapter:Recorder` (here)                        |
|------------------------|-----------------------------------------------------|--------------------------------------------------|
| Where it captures      | An adapter's own write path (`ISituationIngest`)     | Any endpoint's read path (`SubscribeSituationObjectEvents`) |
| What it records        | The exact `UpdateSituationObject` sent on the wire   | Each object turned back into the update that recreates it |
| Fidelity               | Lossless                                             | Faithful, but a reconstruction                   |
| What it can capture    | Only that adapter's own traffic                      | Everything happening in that situation, whoever caused it |
| Runs in                | Any `Adapter.*` process                              | `Adapter.Replay`                                 |

Use `Adapter:Recording` when the traffic you want is your own adapter's — `Adapter__Recording__Enabled=true` on
`Adapter.OpenSky` gives you an offline OpenSky replay with nothing lost. Use the recorder here when you want
whatever a *server* is doing: a third-party implementation, or a situation being fed by clients you don't control.

## `SituationRecorder`

Subscribes to `Adapter:Ingest:Address` exactly like any other client and writes what it sees.

1. Opens `SubscribeSituationObjectEvents`. Per the contract the stream opens with the full current situation;
   `IncludeInitialSnapshot` (default `true`) decides whether that first batch is recorded, i.e. whether a replay
   reconstructs the situation as it stood or starts from whatever changed afterwards.
2. Each received object is split by `is_deleted`: a deleted object is recorded as a `DeleteSituationObject`
   (so a replay reproduces the removal instead of resurrecting it), everything else goes through
   `SituationObjectToUpdate` (Core) and is recorded as an update.
3. Each received batch becomes one frame, stamped with its offset from the first frame.
4. A dropped connection is retried every 5s rather than ending the recording — long captures outlive blips.

The stored-to-update conversion is descriptor-driven rather than eleven hand-written mappings, because the two
message families are generated from the same contract and mirror each other field for field. Two things cannot be
expressed as an update and are not carried across: `creation_meta_data` (the server stamps its own) and
`additional_attributes` (documented as created by the underlying service). A stored object holding several
`foreign_keys` keeps the first, since one update carries exactly one.

## `ReplayPlayer`

Reads a recording and pushes it back through `ISituationIngest` — so it drives whatever `Adapter:Ingest:Address`
points at, including an implementation that is not this repo's Host.

- `Speed` scales playback. `1.0` is the original pace; `10.0` makes ten minutes of recording take one, which is what
  makes a recording usable as a test fixture rather than only as a demo.
- `TickInterval` is how often the player checks for frames that have come due, and therefore the granularity of
  replay timing.
- `Loop` starts over at the end. Off by default so a replay used as a fixture ends when the recording does.
- `RestampReportingTime` (default `true`) moves each object's `reporting_time` to now **and shifts its
  `expiry_time` by the same amount**. Both halves matter: without the first, last-write-wins rejects an old
  recording as stale; without the second, every object it carries expires the instant it lands, because
  `expiry_time` is an absolute instant and the recording's has long passed. Shifting preserves what the recording
  actually captured — how long each object was meant to live. Turn it off when the thing under test *is* the
  server's staleness or expiry handling.

A `BackgroundService` rather than an `ISimulationSource`: a source is a produce-every-N-seconds loop that only ever
emits updates, and a replay is neither — it owns its own clock and has to reproduce deletes as deletes.

## The recording format

Newline-delimited JSON, one frame per line (`RecordingFormat` in Core), matching the `.jsonl` convention the file
log sink already uses:

```json
{"seq":1,"offsetMs":0,"updates":[{"symbol":{"identity":{"stringIdentity":"opensky:3c6444"}, "...":"..."}}]}
{"seq":2,"offsetMs":4796,"updates":[...],"deletes":[...]}
```

The envelope is hand-written; the objects inside are in canonical protobuf JSON — still the contract's own model,
not a re-modelled copy of it. So a recording is readable, greppable and hand-editable, and `jq` is a perfectly good
recording editor:

```bash
# What is in this recording?
jq -r '.updates[]? | keys[0]' recording.jsonl | sort | uniq -c

# Keep only the first minute.
jq -c 'select(.offsetMs <= 60000)' recording.jsonl > short.jsonl
```

Line-oriented rather than one JSON array, and flushed after every frame, so a recording whose process was killed is
still completely usable up to its last whole line — the reader skips an unreadable final line with a warning
instead of abandoning the file.

## Recording and replaying, end to end

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

## Configuration

See [Configuration](../../../docs/CONFIGURATION.md) for the full `Adapter:Recorder` / `Adapter:Replay` reference.

## Tests

- `RecordingFormatTests`, `RecordingWriterTests` (unit) — one line per frame, offsets relative to the first frame,
  truncated-line tolerance, and the ingest decorator recording exactly what it forwards.
- `SituationObjectToUpdateTests` (unit) — drives **all eleven object types** through store → update → store and
  requires the two situations to come out equal. This is what stands between the descriptor-driven converter and a
  silent mis-mapping.
- `ReplayPlayerTests` (unit) — frame ordering, restamping, the expiry shift, deletes replayed as deletes.
- `RecordReplayE2ETests` (E2E) — a real Host over real gRPC: record it, reset it, replay, and check the situation
  came back.
