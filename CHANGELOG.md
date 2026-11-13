## [3.6.10] - 2026-11-13

### ArSee forensic workflow and IEC-101 receive reliability
- Integrated context-aware Event Journal live-follow and forensic **Open Original Frame** correlation for genuinely captured TX/RX, with explicit partial evidence when an original TX is unavailable (PR #197), and isolated real-WPF test fixtures (PR #198).
- Hardened IEC-101 transaction-owned original-frame evidence, bounded correlation and multi-IOA mapping without inventing matching TX frames (PR #199). Aligned brand PNG in the header without disturbing the sidebar (PR #200).
- Reconciled the bundled IEC-101/104 utility SCADA I/O example with **31 uniquely mapped points**: FEEDER/KOPEL/TRAFO CB status, bay-specific Local/Remote and control command, BUSBAR voltage status and four RTU alarm IOAs; fail closed instead of using wrong-CA or wrong-type signal names (PR #201).
- Improved IEC-101 FT1.2 RX at **1200 bps** with bounded baud-aware frame completion, per-connection persistent framing in single-link and A/B, partial-fragment evidence, and suppression of ambiguous stale E5 after a broken telegram (PR #202).
- Added regression tests for bay identity, wrong CA/Type ID, command-feedback pairing, slow serial frames, incomplete responses, cancellation and cross-poll stale-E5.
- Maintained source-to-master reconciliation of unmerged shared transaction core PR #145; no bulk merge, no automatic command replay, and no assertion of physical RTU interoperability.

### Validation and limitations
- Previous master `3ee6453a6a391015290b9d131b6a15b7e4edc51a` passed 1,032 xUnit tests, protocol smoke, Windows Release build, real-WPF OBS-C acceptance and CodeQL; the final **v3.6.10 release source SHA** must separately pass all matching release gates including native Windows updater FAT before publication.
- Physical SICAM A8000 field FAT, dual-link failover, GI/SBO readback and 60/500/2,000 SOE event-count reconciliation remain pending. An installed application update does not reconnect to any IEC device without operator action.
- Windows Setup is not Authenticode-signed; verify published SHA256SUMS and release provenance. Existing saved IOA profiles are not silently overwritten by the updated bundled sample.

## [3.6.9] - 2026-11-13

### ArSee installed auto-updater milestone
- Ships the in-app, operator-approved stable GitHub Releases updater in the Windows installed edition. The already-published v3.6.8 package predates this new feature, so upgrade to v3.6.9 once using the official installer; subsequent stable releases can be applied from within ArSee.
- Downloads only exact official stable installer assets with SHA-256 and size verification; preserves profiles and capture evidence and never interrupts an active IEC session.
- Runs the updater in an independent staged process after normal WPF close, validates the actual replacement WPF first frame and matching process ID/version/token, and keeps safe rollback backups.
- Adds preflight disk-space and symlink/junction safety checks, plus per-user Inno uninstall registration verification and metadata rollback.
- Introduces version-bound Windows native installer FAT and a release gate that rejects stable publication without a passing same-commit installer/restart acceptance.
- Clarifies that public Windows installers are currently unsigned and that real IEC device communication is never automatically reconnected after an update.

## [3.6.8-rc.3] - 2026-11-10 (public prerelease)

### Release and public presentation
- Published Windows x64 installer and portable ZIP, SHA256 checksums, SPDX SBOM and attested build provenance.
- Refined nine Setup checkboxes into compact, vertically aligned 28×16 DIP toggles with 10×10 DIP thumbs, preserving serial settings and WPF controls.
- Added real Windows/WPF switch geometry checks and IEC-101/Passive Setup screenshots to the release acceptance suite. Retained the 10,000-frame HOLD/AUTO Frame Trace regression.
- Kept the unmerged physical SICAM serial-recovery work in draft PR #136 **out of this public RC** while field FAT is outstanding.
- Updated the bilingual landing website, download choices, release metadata and GitHub Pages compatibility mirror to accurately distinguish this preview from stable v3.6.7.

## Unreleased

### UX polish
- Replaced WebView2 PDF preview with a native WPF FixedDocument/DocumentViewer report preview to remove browser runtime dependency and tab-switch flicker.
- Added a report preview document builder that renders from the same evidence report model used by PDF export.
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

## [3.6.7] - 2026-06-20

### Added
- Added a low-priority lazy release update notifier that checks for newer GitHub releases after startup, stays silent when offline, and never blocks protocol testing.
- Added modern in-app dialogs to replace native WPF message boxes across the desktop application.
- Added user-facing release notes for the v3.6.7 portable Windows package.

### Changed
- Upgraded Smart Findings context handling so manual operator disconnect, GI traffic, redundancy switchover, background scan, and real fault symptoms are classified separately.
- Improved IEC-101 dual-link redundancy recovery so cascaded active-link timeouts prioritize standby rescue/failback instead of getting stuck on a timed-out active link.
- Preserved user-configured IEC-101 response timeout for failover timing instead of widening short timeout values through the low-baud timing guard.
- Improved Values workspace column sizing and corrected setup-field highlight behavior after Smart Correction.

### Fixed
- Fixed release update checker catch ordering so network timeout handling builds cleanly.
- Fixed Smart Findings false positives that treated C_IC_NA_1 General Interrogation as a control command.
- Fixed stale GI and redundancy findings that could remain visible after valid response evidence or operator stop.


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
