# GitHub Repository Hygiene

ARIEC60870 is intended to stay clean and legally safe for public review.

## Commit policy

Commit these:

- `src/**` source code
- `tests/**` unit tests and test harnesses
- `docs/**` documentation
- `landing/**` static GitHub Pages landing page
- `samples/**` only sanitized sample traces
- root legal/config files

Do not commit these:

- build outputs: `bin/`, `obj/`, `dist/`, `build/`, `out/`
- package folders: `node_modules/`
- generated reports and exports
- real field captures: `.log`, `.pcap`, `.pcapng`, `.msg`, `.pdf`, spreadsheets
- secrets or production settings
- ZIP packages or installers

## Why the `.gitignore` is strict

The repository uses a source-first allowlist pattern. This prevents accidental upload of real traces,
customer material, vendor files, generated reports, or compiled binaries.

If a new source folder is added, update `.gitignore` intentionally instead of bypassing it.
