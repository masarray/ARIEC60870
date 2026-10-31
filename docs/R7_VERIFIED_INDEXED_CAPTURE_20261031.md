# R7 CAPTURE-02 — verified indexed archive browsing

## Problem and ownership

Before this change, every `Previous`/`Next` click in portable `.ariec` Trace search re-opened the ZIP, decompressed `frames.jsonl`, revalidated SHA-256, parsed all records and re-ran the query, even when only 150 visible rows were required. This increased latency with archive size while the WPF viewport remained bounded.

The capture remains the authority. This checkpoint changes read-only archive **navigation**, not protocol decode, active Master state or passive RX-only behavior.

## Implementation

1. `CaptureArchiveReader.OpenIndexed` validates original ZIP structure, manifest, uncompressed-byte SHA-256, row count and JSONL safety limits **before exposing search results**; it simultaneously spools normalized valid JSONL rows to a delete-on-close OS temporary file. Source length/mtime are checked across opening to reject ordinary mid-read replacements.
2. A `CaptureArchiveIndex` query scans the immutable verified spool once, uses existing `CaptureArchiveQuery.Matches` (authoritative IEC101/104 decode where requested) and builds a disk-backed fixed-width offset list. It retains at most one query offset index, rather than materializing millions of objects. A cancelled query cannot publish an incomplete index.
3. A page reads at most 500 indexed rows via seeks. `Previous`/`Next` on the same query needs no ZIP scan, no all-record parse and no repeated hash of the same verified snapshot.
4. Indexes are scoped to an archive session; closing archive navigation or changing session discards both delete-on-close temporary files. If the original file's path, size or modification timestamp changes, the UI reopens/reverifies it. The existing generation and cancellation checks prevent stale results from replacing newer Trace views.
5. Legacy archives without hashes remain **LEGACY UNVERIFIED**, not upgraded into trusted captures. SHA-256 proves consistency with the archive's manifest, not author authenticity or a digital signature. An indexed snapshot is a read-only view of bytes verified at open time, not a claim about subsequent external edits to the original ZIP.

## Automated regression contract

- 50,000 original rows, accurate first/last/middle pages and no full ZIP rescan after initial indexing, including when the source file is removed after opening.
- Query changes, source A/B separation, accurate matched totals, cancelled query retains last committed index.
- Refuse manifest SHA-256 mismatch, explicitly label unverified legacy, preserve existing negative typed field checks and other archive safety tests.
- WPF navigation maintains the existing 150-row bounded page, single source selection/inspector and safe cancellation.

## Remaining R7 release gates

This is a meaningful **CAPTURE-02 indexed browsing checkpoint**, not completion of all R7. Byte-accurate IEC103/104 inspector and byte-offset parity through Live/Replay/JSON/PDF, corruption/unknown decoding coverage, multi-day performance metrics, Windows screenshot acceptance and physical FAT still require independent evidence. A verified hash is not a digital signature. Do not call R7 complete until the canonical PRD/ARD TRACE-02 + CAPTURE-02 gates pass.

No vendor/customer capture, private telemetry, proprietary decoder code or competing protocol parser is added.
