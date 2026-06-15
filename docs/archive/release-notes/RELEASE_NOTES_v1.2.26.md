# Release Notes v1.2.26 — Public Release Hygiene + Apache-2.0 Notice Audit

## Public repository readiness

- Rewrote the top-level `README.md` into a clean public product overview instead of a long rolling changelog.
- Added `docs/PUBLIC_RELEASE_AUDIT.md` with release packaging rules and clean-room risk notes.
- Added `SECURITY.md` to reduce the risk of sensitive relay/customer traces being posted publicly.
- Tightened `CONTRIBUTING.md` with source hygiene, clean-room, SPDX, and public issue expectations.

## License / attribution

- Kept the project license as Apache-2.0.
- Added centralized package/repository metadata in `Directory.Build.props`.
- Added SPDX license identifiers to comment-capable project/source files.
- Updated `NOTICE` to explicitly mention Lucide icon references and System.IO.Ports.
- Updated `THIRD_PARTY_NOTICES.md` with Lucide ISC and System.IO.Ports MIT license treatment.

## Wording cleanup

- Removed internal product-family wording from current public-facing docs.
- Replaced vendor-specific guardrail wording with neutral `vendor-specific` wording where appropriate.
- Kept historical release notes intact as historical records, but moved the public entry point to a cleaner README.

## Runtime behavior

- No protocol engine behavior change.
- No polling policy change.
- No UI layout behavior change beyond prior IEC/103 branding package.
