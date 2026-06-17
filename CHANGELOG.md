## Unreleased

### UX polish
- Stabilized the WPF scrollbar skin with a fixed macOS-like drag thumb to prevent tiny clipped vertical thumbs in virtualized trace lists.
- Reworked the app scrollbar thumb template again: full transparent hit target, Rectangle-based inner pill, no Border clipping, larger top/bottom track gutter, and no fixed visual height outside the allocated Thumb bounds.
- Stabilized WPF scrollbar rendering with an Apple-style full-hit-area thumb template that keeps the rounded pill inside the allocated Thumb bounds.
- Replaced text-only report preview with an embedded WebView2 PDF preview generated from the same native PDF engine as Export PDF.
- Hardened trace selection so clicks in the right scrollbar zone are never treated as trace row selection.

## Unreleased

### IEC-101 Dual Link Release UX Cleanup
- Reduced the primary desktop workspaces to the field workflow: Redundancy, Values, Events, Trace, and Report for IEC-101 dual-link mode.
- Moved Evidence Summary out of the primary path by renaming it to Evidence Ledger and hiding advanced/supporting tabs from the segmented release navigation.
- Rebuilt the Redundancy workspace into a compact health strip, Link A/Link B cards, image/switch card, and filtered redundancy timeline.
- Removed the Active GI button from the Redundancy workspace so GI, clock sync, read, and control actions stay in the command dock and route through the active link only.

### Final release micro-polish
- Removed the command preview card and repeated command-target status copy from the command dock.
- Removed low-value helper text from the command dock surface while keeping tooltips on all command controls.
- Replaced the report preview WebBrowser with a WPF FlowDocument preview so it uses the same modern scrollbar styling as the rest of the app.
- Matched Auto Scroll rail button sizing with the other left-rail buttons and softened the rail background gradient.
- Refined scrollbar thumb templates so short thumbs remain rounded instead of looking clipped at the bottom.

# Changelog


## [3.6.6] - Unreleased

### Public wording and release readiness
- Aligned active public branding around **ARIEC60870 Evidence Analyzer** across README, product metadata, app title, docs, SBOM metadata, and report output.
- Rewrote `docs/ROADMAP.md` so implemented native PDF export is no longer described as planned work.
- Synchronized GitHub security automation documentation with the current Dependabot policy: minor/patch automation, planned maintenance for major updates.
- Expanded the GitHub Pages site from a minimal landing page into an SEO-oriented marketing and user guide site covering product purpose, features, supported protocols, download flow, use cases, licensing, commercial-use notes, FAQ, and troubleshooting.
- Aligned README with the expanded user website by adding the product website/user guide hub, direct site links, and explicit Apache-2.0 commercial-use guidance.
- Restored rich structured data on the GitHub Pages home page using current native-PDF and evidence-analyzer wording.
- Changed the manual release workflow pre-release default to `false` for stable public release preparation.
- Bumped public version metadata to `3.6.6` for the next stable public package.
- Added a full xUnit regression gate to the release packaging path before the user-facing ZIP is produced.
- Added release warning gates to CI, release packaging, and the local packaging script with `TreatWarningsAsErrors=true`.
- Made `dotnet format --verify-no-changes` visible in CI as an advisory formatting signal without blocking existing release validation.
- Corrected the README local packaging command so it uses the repository version by default instead of a stale hard-coded version.
- Clarified desktop cleanup documentation so it no longer overstates the current `MainWindow` ownership boundary.

### Fixed
- Corrected the IEC-103 private/vendor ASDU regression test so it matches the decoder contract for type 205 while still verifying unknown ASDU transparency.

### Changed
- Reworked the Report workspace copy to remove legacy conversion workflow language and export a professional PDF report directly.
- Replaced the third-party PDF dependency with a clean-room native PDF evidence report engine using built-in PDF primitives, Type 1 fonts, paged layout, and professional evidence tables.
- Restored a pure Apache-2.0 dependency story for PDF export by removing the PDF generator package dependency.
- Kept `site/` as the canonical GitHub Pages source while restoring a generated `/docs` compatibility mirror so existing branch-based Pages settings do not return 404.
- Removed stale `landing/` source files and added guards to keep the `/docs` mirror synchronized with `site/`.
- Moved README screenshots to canonical `site/assets` paths.
- Added `seo-manifest.json`, `llms.txt`, `humans.txt`, explicit favicon/touch icons, and repository tests that verify sitemap/canonical coverage.


### IEC-101 Dual Link Redundancy Phase 3
- Added standby recovery latch behavior: failed standby links now require consecutive good supervision probes before being marked recovered.
- Added `ManualOnly` failback as the safe default and an opt-in preferred-link failback policy guarded by recovery threshold and anti-ping-pong logic.
- Added recovery/failback evidence events for `RecoveryStarted`, `RecoveryProbeSucceeded`, `RecoveryCompleted`, `AutoFailbackRequested`, and `AutoFailbackBlocked`.
- Expanded dual-link snapshots and workspace text with recovery summary and failback policy visibility.
- Added regression coverage for active-link timeout promotion, old-active standby recovery, and opt-in preferred-link failback evidence.

### IEC-101 Dual Link Redundancy
- Added manual switchover proof support in the IEC-101 Dual Link workspace. The UI queues the request while the engine still validates standby promotability and records evidence.
- Added active-link GI action in the dedicated dual-link workspace so FAT/SAT proof can refresh the application image without sending GI on standby.
- Expanded dual-link snapshots with application-image object count, GI timing, standby supervision timing, and last failover route/reason.
- Added dual-link session regression tests for manual failover evidence and stabilization-window behavior.
- Added `docs/IEC101_DUAL_LINK_FAT_CHECKLIST.md` for startup, manual switch, failure proof, and report acceptance checks.

### Added
- Phase C test credibility upgrade with first-class xUnit regression suites for Core, Master, Reporting, Desktop capture contracts, and Repository hygiene.
- CI coverage collection and `.trx` result artifacts for every xUnit suite.
- `docs/TESTING_STRATEGY.md` and `tests/README.md` to make the validation model clear for contributors and users.
- Desktop architecture cleanup documentation and repository guardrail tests for WPF code-behind ownership.
- Feature-owned `MainWindow` partial files for command dock, setup preferences, session control, runtime proof, live evidence routing, frame inspection, workspace selection, capture files, trigger capture, reporting, and export.
- `LocalWorkspacePaths` service to centralize ARIEC60870 local app-data path ownership.
- OpenSSF Scorecard workflow for public security posture visibility.
- Repository hygiene xUnit test project for version alignment, required files, README links, workflow permission checks, and site asset hygiene.
- Lightweight SPDX 2.3 JSON SBOM generation script attached to release packages.
- Release build provenance attestation for generated ZIP, checksum, and SBOM artifacts.
- Canonical `site/` source for GitHub Pages deployment plus `/docs` compatibility mirror for legacy branch-based Pages settings.

### Changed
- `MainWindow.xaml.cs` is reduced to shell bootstrap responsibilities; large UI workflows now live in `src/ARIEC60870.Desktop/Features/`.
- `StatusHistoryRow` and `TriggerCaptureRow` moved from the shell file into `ViewModels/` as bindable row models.
- Release workflow now separates read-only build/package permissions from GitHub Release publishing permissions.
- CodeQL now uses `security-extended` and `security-and-quality` queries.
- Dependabot updates are grouped for GitHub Actions and .NET/NuGet packages.
- CI uploads protocol smoke-test log, xUnit `.trx` files, and XPlat Code Coverage collector output.
- Raw screenshot folder was removed from the public landing site tree.

All notable public changes for ARIEC60870 are summarized here. Detailed legacy release notes are archived under `docs/archive/release-notes/`.

## [3.6.5] - 2026-06-15

### Public repository hardening

- Cleaned the README into a product-facing public landing document instead of an internal release log.
- Aligned project version metadata, release package defaults, SEO landing metadata, manifest, and release workflow defaults.
- Added a neutral global IEC-101/104 sample profile name for public distribution.
- Added GitHub issue templates, pull request template, support guide, code of conduct, Dependabot, CodeQL, and repository metadata notes.
- Improved release automation with optional portable and single-file Windows package output.
