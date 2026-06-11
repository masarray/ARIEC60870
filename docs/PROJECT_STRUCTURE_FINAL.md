# Final Project Structure Direction

This is the intended mature structure for ARIEC60870. Current versions may keep some classes in fewer projects, but future changes should migrate toward this boundary instead of mixing UI/protocol/reporting responsibilities.

## Final Repository Shape

```text
ARIEC60870/
  src/
    ARIEC60870.Core/           # Pure protocol models, FT1.2 parser, control decode, ASDU decode
    ARIEC60870.Master/         # Active master state machine, polling policy, transport orchestration
    ARIEC60870.Mapping/        # Future optional split: mapping profile schema, validation, lookup
    ARIEC60870.Reports/        # Future optional split: Markdown/HTML/PDF/evidence bundle
    ARIEC60870.Desktop/        # WPF cockpit only; no protocol ownership
    ARIEC60870.Cli/            # Developer/test harness and batch/offline analysis

  tests/
    ARIEC60870.Core.Tests/
    ARIEC60870.Master.Tests/
    ARIEC60870.Mapping.Tests/
    ARIEC60870.TestVectors/

  samples/
    traces/                  # Sanitized sample traces only
    mapping-profiles/         # User-style sample profiles, not vendor truth
    reports/                 # Generated examples only if small/sanitized

  docs/
    ARCHITECTURE.md
    PRODUCT_BENCHMARK_AND_STRATEGY.md
    PROJECT_STRUCTURE_FINAL.md
    MASTER_POLLING_POLICY.md
    EVENT_LOG_POLICY.md
    MAPPING_PROFILE_SCHEMA.md
    ROADMAP.md
    CLEAN_ROOM_POLICY.md
    RELEASE_NOTES_*.md

  landing/                   # Static GitHub Pages site
  .github/workflows/         # CI and GitHub Pages workflows
  AGENTS.md
  README.md
  LICENSE
  NOTICE
  THIRD_PARTY_NOTICES.md
```

## Responsibility Boundary

### ARIEC60870.Core

Owns:

- FT1.2 frame parser
- checksum/length validation
- control field decode
- ASDU decode
- CP32Time2a decode
- offline trace analysis primitives
- protocol-level models

Must not own:

- serial port lifecycle
- WPF state
- reports with UI assumptions
- active master loop

### ARIEC60870.Master

Owns:

- transport abstraction
- active master session
- startup sequence
- Class 2 normal polling
- ACD-driven Class 1 event drain
- timeout/recovery behavior
- master evidence and counters
- Value Viewer and Relay Event Log runtime updates until those are split into a runtime module

Must not own:

- WPF controls
- vendor signal truth
- external protocol stack code

### ARIEC60870.Mapping

Future split target. May currently live under Core.

Owns:

- mapping schema
- user profile load/save/validate
- lookup by Type/FUN/INF
- state maps and units

Must not own:

- protocol decode correctness
- guessed vendor mappings

### ARIEC60870.Reports

Future split target. May currently live under Core/Master.

Owns:

- session summary
- evidence export
- report sections
- markdown/html/pdf generation

Must not own:

- master polling decisions
- WPF controls

### ARIEC60870.Desktop

Owns:

- connection form
- master control buttons
- live KPI cards
- Value Viewer grid
- Relay Event Log grid
- Frame Monitor grid
- Selected Frame Inspector
- Findings panel
- report export action

Must not own:

- Class 1/Class 2 polling policy
- FCB/FCV state
- ASDU parsing
- event edge detection rules

### ARIEC60870.Cli

Owns:

- command-line entry point
- developer regression harness
- offline trace analysis command
- active master command for engine testing

Must not become the main product UX.

## Naming Rules

Use vendor-neutral names unless the file is explicitly a user-provided profile.

Allowed:

```text
GenericRelay
UserMappingProfile
Iec103SignalMapping
RelayEventLog
ValueViewer
```

Forbidden without validated user data:

```text
SiprotecProfile
ABBProfile
SchneiderProfile
OfficialRelayMap
```

## Physical Repo Hygiene

Allowed in GitHub:

- source code
- docs
- landing page
- sanitized sample trace
- sanitized sample mapping profile
- CI workflow
- license/notice files

Forbidden in GitHub:

- `bin/`, `obj/`, `out/`, `dist/`, `node_modules/`
- real project logs/captures unless sanitized
- `.msg`, `.pcap`, `.pcapng`, raw relay databases, private customer docs
- secrets, tokens, serial number captures, customer names
