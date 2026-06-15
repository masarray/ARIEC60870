# Changelog


## [3.6.6] - Unreleased

### Fixed
- Corrected the IEC-103 private/vendor ASDU regression test so it matches the decoder contract for type 205 while still verifying unknown ASDU transparency.

### Changed
- Reworked the Report workspace copy to remove HTML-print workflow language and export a professional PDF report directly.
- Replaced the third-party PDF dependency with a clean-room native PDF evidence report engine using built-in PDF primitives, Type 1 fonts, paged layout, and professional evidence tables.
- Restored a pure Apache-2.0 dependency story for PDF export by removing the PDF generator package dependency.
- Kept `site/` as the canonical GitHub Pages source while restoring a generated `/docs` compatibility mirror so existing branch-based Pages settings do not return 404.
- Removed stale `landing/` source files and added guards to keep the `/docs` mirror synchronized with `site/`.
- Moved README screenshots to canonical `site/assets` paths.
- Added `seo-manifest.json`, `llms.txt`, `humans.txt`, explicit favicon/touch icons, and repository tests that verify sitemap/canonical coverage.

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
- Added SEO-focused landing page metadata, structured data, sitemap, and user-oriented content.

### Latest application release note


## Fixed

### Report export is now visible

The left rail action is now clearly labeled `Report` instead of a generic `Export`, and it is available even before a completed run. If no evidence exists, ARIEC shows a useful message.

### Report workspace

The report tab provides a dedicated place to review the evidence scope before exporting a professional report. Phase 3.6.6 replaces the previous HTML-print workflow with direct PDF export.

It includes:

- `Refresh`
- `Export PDF`
- embedded report-content preview

## Report workflow

1. Run a session or open a `.ariec` capture.
2. Open `Report`.
3. Click `Refresh`.
4. Click `Export PDF`.
5. Attach the generated PDF to FAT/SAT, troubleshooting, or handover records.

## Preserved

- Smart Capture Rules.
- Protocol Trace default workspace.
- Unified `.ariec` capture.
- Left-rail Auto Scroll Latest.


## Earlier releases

Historical notes from v0.1 through v3.6.5 are kept in `docs/archive/release-notes/` to preserve engineering history without overloading the public README.
