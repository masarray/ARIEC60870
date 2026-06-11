# Contributing to ARIEC60870

ARIEC60870 is intended to remain legally clean, auditable, and safe for corporate engineering review.

## Clean-room rules

- Do not copy source code from commercial, GPL, or unclear-license IEC 60870 protocol stacks.
- Do not port internal class structures, parser logic, state machines, or test suites from third-party protocol libraries.
- Use independently written code, public protocol behavior, legally shareable traces, and self-generated test vectors.
- Do not upload real customer, vendor, utility, or project captures unless they are fully sanitized and legally shareable.
- Keep all dependencies and third-party notices documented in `THIRD_PARTY_NOTICES.md`.

## What belongs in GitHub

Commit only:

- source code under `src/` and `tests/`
- static landing page files under `landing/`
- documentation under `docs/`
- sanitized samples under `samples/`
- GitHub workflows/templates under `.github/`
- legal/config files such as `LICENSE`, `NOTICE`, `.gitignore`, `.gitattributes`, `.editorconfig`, and `THIRD_PARTY_NOTICES.md`

Do not commit:

- `bin/`, `obj/`, `dist/`, `out/`, `node_modules/`
- generated reports, installers, ZIP files, or compiled binaries
- real field logs, private relay captures, or confidential project traces
- PDFs, MSG files, PCAP files, spreadsheets, or customer documents
- secrets, tokens, environment files, or production appsettings

## Code quality expectations

- Keep protocol logic out of WPF. The desktop app is a shell over `ARIEC60870.Master` and `ARIEC60870.Core`.
- Keep UI collections bounded and render through batched updates for high-volume polling.
- Preserve raw frame evidence even when adding friendlier operator wording.
- Do not introduce vendor-specific final signal names unless they come from a user-supplied mapping profile.
- Add or preserve SPDX license identifiers in source files where the format supports comments.

## License

By contributing, you agree that your contribution is provided under the Apache License, Version 2.0.
