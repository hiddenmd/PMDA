# PMDA — Post-Mission Data Analysis — Design

Date: 2026-10-17
Status: Section 1 approved; sections 2–5 are proposed defaults, to be reviewed.

## Goal

Import binary flight recordings from three customers into SQL Server as a single
common, time-ordered event stream (emitter detections, countermeasures, nav,
warnings, ...), and analyse them from both a desktop and a web frontend.

## Constraints

- Input: binary files described by an ICD. 50 MB – 1 GB, 1–50 M events per flight.
- Three customers. Content is broadly the same; file headers / record framing differ.
- Backend C# (.NET 9). Database: SQL Server.
- Frontends: desktop and web, both high-performance on dense timelines.
- Assumption: one deployment + database per customer (data never mixed).

## Architecture (approach 1: modular monolith + shared core)

```
PMDA.Core      event model, interfaces. No dependencies.
PMDA.Parsers   shared payload decoders + per-customer adapters (header/framing)
PMDA.Import    streaming import pipeline (adapter -> decoders -> sink)
PMDA.Data      SQL Server: schema, SqlBulkCopy sink, query layer (Dapper)
PMDA.Api       ASP.NET Core: import, window queries, aggregates        (later)
PMDA.Desktop   Avalonia + GPU chart; can host the API in-process       (later)
PMDA.Web       React + TS + WebGL chart against PMDA.Api               (later)
```

## Section 1 — Event model + parser plugin (approved)

Pipeline: `file -> ICustomerAdapter -> IPayloadDecoder(s) -> FlightEvent stream -> sink`

- `ICustomerAdapter` — `CanRead(fileStart)` for auto-detect, `ReadHeader`, `ReadRecords`
  yielding `RawRecord(MessageType, TimestampTicks, Payload)`. Framing only.
- `IPayloadDecoder` — one per message type, shared across customers. A customer
  may register an override decoder for a message type whose layout differs.
- `FlightEvent` — FlightId, Seq, TimeUtc, EventType, SubType, EmitterId, Frequency, Pri,
  PulseWidth, Bearing, Amplitude, Lat, Lon, Alt, Count, Extra (raw leftovers).
  SubType/Count cover countermeasures (e.g. chaff vs flare, number dispensed).
- Parsing with `Span<byte>` / `BinaryPrimitives`, no per-record allocation in hot paths.
- Corrupt records: logged with file offset and skipped; import continues.
- Customer auto-detected from header, manual override allowed.

## Section 2 — Import & storage (proposed)

- Stream the file; never load it whole. Events batched (default 102 400 — the minimum bulk
  insert that SQL Server compresses directly into a columnstore rowgroup) to an `IEventSink`.
- SQL sink uses `SqlBulkCopy` into `FlightEvent` table with a clustered columnstore index.
- `Flight` table: FlightId, CustomerId, Tail, MissionId, StartUtc, EndUtc, SourceFile,
  FileSha256, ImportStatus, EventCount, ErrorCount.
- Re-import of a file with same SHA-256 is rejected (idempotent).
- Import is all-or-nothing per flight: failure marks the flight Failed and deletes its rows.

## Section 3 — Query API (proposed)

- `GET /flights`, `GET /flights/{id}`
- `GET /flights/{id}/events?from&to&types&limit` — raw window, capped.
- `GET /flights/{id}/timeline?from&to&buckets=N` — server-side downsampled counts /
  min-max per bucket, so the UI never pulls millions of rows.
- `POST /imports` (upload or server path) + progress.

## Section 4 — Frontends (proposed)

- Desktop: Avalonia, GPU chart (SciChart if licensable, else ScottPlot).
- Web: React + TypeScript, WebGL chart (SciChart.js or uPlot).
- Both: timeline lanes per event type, zoom/pan driving `/timeline` requests,
  event table virtualized, details panel.

## Section 5 — Testing (proposed)

- Parsers: unit tests on hand-built byte buffers per message type and per adapter,
  including corrupt/truncated records.
- Pipeline: tests with an in-memory sink.
- Data: integration tests against SQL Server (LocalDB or container) — not yet
  available on the dev machine.
- Performance benchmark: synthetic 50 M-event file, target import < 5 min.

## Open items

- Real ICDs for customers A/B/C (until then a clearly-labelled synthetic sample format is used for tests).
- Full list of event types and emitter parameters.
- SQL Server instance for development.
