# ArSee public RC.6 release handoff — 2026-11-12

## Verified baseline
- [x] PRs #174–#180 (desktop UI/identity) merged; latest GUI base `9cf621d10b7579543b3b63330e5358c5964c8c3e`.
- [x] Windows CI `43138763275` **success** (build, 19 protocol smokes, xUnit, real WPF acceptance, package artifact).
- [x] CodeQL `43138763359` and repository hygiene `43138763366` **success**.
- [x] Portable test artifact inspected: `ARIEC60870.Desktop.exe`, fonts, profile and startup notes; nested ZIP SHA256 matches upload's `SHA256SUMS.txt`.
- [x] Public branding/README/EN+ID landing and docs mirror reconciled from PR #173; existing RC.5 links intentionally unchanged.
- [x] Draft IEC-101 engine PR #145 excluded.
- [ ] Readiness must be reconfirmed against latest `master` immediately before actual public-release trigger.

## Safe next publication operation (NOT automatically triggered by this document)
1. Create the exact `release/rc-3.6.8-rc.6` ref **only** after current-master checks pass; a push to that ref automatically publishes through `.github/workflows/release-package.yml`. Do not create this ref merely for testing.
2. `Directory.Build.props` version must equal `3.6.8`, and archived release notes `docs/archive/release-notes/RELEASE_NOTES_v3.6.8-rc.6.md` must exist on the release ref.
3. Verify Package full .NET Release warning gate, 19 protocol smokes, six xUnit suites, actual published single-file EXE GUI/WPF smoke and 10,000-frame HOLD/AUTO acceptance, installer compilation, ZIP structure, previous-release upgrade/running-master safe shutdown, SHA256, SBOM and attested provenance. Fail closed on any red check.
4. The new tag `v3.6.8-rc.6` must point to exactly the tested release source; **never** reuse/repoint an existing public tag.
5. Only after GitHub Release is published with **installer + portable ZIP + checksums** update `site/`, `docs/`, README EN/ID download CTAs to direct `releases/download/v3.6.8-rc.6/...` asset URLs. Test every public hyperlink (avoid a release-page-only detour). Preserve public RC.5 archive and stable v3.6.7 link.
6. If repository slug is administratively changed to `arsee`, handle the separate GitHub Pages cutover described in `docs/ARSEE_REBRANDING_REPOSITORY_RENAME_20261112.md`—the old Pages project URL does not automatically redirect.
7. Capture the exact release run, source commit, release asset SHA256 values and landing Pages deployment result in the release handoff.

## Known evidence limitation
The Windows GitHub-hosted runner constrained actual WPF viewport dimensions to ~1004 client DIP in named 1366×768 and 1540×960 tests. Synthetic acceptance passed, but neither physical IEC-101 A8000 interoperability nor all requested display resolutions were field-qualified. Describe this accurately in public notes.
