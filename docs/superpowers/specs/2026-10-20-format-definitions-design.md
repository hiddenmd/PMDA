# Format definitions: vendor `.h` files as data

## Problem

The legacy PMDA reads a vendor `.h` per project and generates one SQL table per message struct. Two things are hard-coded:

- the `.h` dialect itself;
- the step that merges raw recorder events into synthetic records before the `.h` describes them. For example, emitter new/extension/update/delete events become one emitter record, with nav and PRI/FRQ/PPI lists attached.

There are 3–5 projects. Each has a different `.h` and different symbols, and the `.h` changes with every recorder software version.

Today this repo has the same problem in another form: one C# adapter and decoder per customer.

## Goal

A new project or a new `.h` version needs **no code change**. An administrator loads the `.h`, checks the report and dry run, and imports flights.

Analysts keep querying **real SQL tables per message type**, under the legacy names.

## Layers

1. **Format.**
   - `PMDA.Formats/Dialect` parses the annotated-C dialect tolerantly. It accepts:
     - commented-out enums (used only when referenced and not defined);
     - duplicate enumerators;
     - value-to-colour enums;
     - tag typos with spaces (`<Resolution> 0.005`).
   - `Layout` computes bit and byte positions explicitly. Byte order, bit order and pack are project settings, not compiler assumptions (default: little-endian, LSB-first bitfields, MSVC-style storage units).
   - `Decoding` turns a payload into column values (raw × `<Resolution>` + `<Bias>`).
   - Arrays are decoded as follows:
     - fixed arrays are flattened into columns;
     - `<SubStructure>count` arrays become child rows;
     - fields beyond the end of a short payload decode as null.
2. **Framing.** The `<Header>` struct gives:
   - the event ID (`<UserEntityID>`);
   - the time (`<EventTime>`);
   - the length (`<Length>All|DataOnly`, with `<Unit>` = bits per length unit).

   `<Parse>False` fields before the event ID are the sync signature (a setting, scanned to resync).
3. **Recipe** (per project, JSON). It replaces the hard-coded merge with configurable steps:
   - `bind`: an event ID → a struct, for structs without `<ID>`;
   - `track`: emitter lifecycle by key, with new/update/delete event IDs, the status field and the delete code. It writes the derived message (the old ID 200);
   - `join`: extension records → finger child lists;
   - `nav`: the latest aircraft state, attached to tracked rows.

   Fields that the recipe fills (`derived`) are excluded from the byte layout of the raw events.
4. **Meaning (roles).** Roles map fields to the common `FlightEvent`: emitter ID, frequency, PRI, PW, bearing, power, and lat/lon/alt with unit factors. That keeps the map, RWR, timeline and alerts working for every project. Tags such as `<Latitude>` are used where they are present.
5. **Display.** `<Title>`, `<Enum>`, `<ColorEnum>`, `<Level>`, `<InVisible>` and `<Group>` are kept in the definition for a generic records view (a later phase).

## Storage

- **`dbo.RawRecord`** (FlightId, Seq, TimeUtc, EventId, Bytes) holds every record. It is lossless, so the typed tables can always be rebuilt.
- **Typed tables per project schema.** There is one table per `<ID>` struct and per derived message, and one child table per `<SubStructure>` array.
  - Every table has `FlightId`, `RecordSeq` and `TimeUtc`.
  - Tables are created on the first import into a database.
  - New fields are added as nullable columns, so one table serves every `.h` version.
- **Naming is a project setting.** The table name comes from the struct name or its `<Title>`, and the column name from the field name or its `<Title>`. It will be matched to the legacy schema once that is available.
- **Registries.** `dbo.FormatTable` registers the generated tables, so deleting a flight cleans them. `dbo.FormatVersion` stores each `.h` imported into the database.

## Projects

- Projects live under `%ProgramData%\PMDA\formats\{id}\`:
  - `project.json` holds the settings, recipe and roles;
  - `versions\NNN.h` holds the `.h` history.
- Each upload of a `.h` makes a new version, with a parse report and a diff against the previous version.
- A dry run decodes a flight file without writing anything. It reports the records per message, unknown IDs, short records and the tracks found.
- An import picks a project explicitly, or auto-detects when only one project's framing matches.

## Phases

1. Dialect, layout, decoder, framing, recipe, roles, typed tables, import path, API, Formats panel, a demo project and a sample generator. **(this change)**
2. A generic records view in the app (enum labels, colours, `<Level>`/`<Group>` filtering).
3. A legacy naming convention that matches the old schema, plus a check of the generated DDL against a legacy database.
4. Rebuilding typed tables from `RawRecord` after a definition or recipe fix, and choosing "same or new field" in the version diff.

Vendor `.h` files and legacy schemas never go into this repository. The tests use an invented demo `.h` written in the same dialect.
