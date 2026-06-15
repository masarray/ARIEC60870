# Changelog


## [3.6.6] - Unreleased

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
- Canonical `site/` source for GitHub Pages deployment.

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

### Report Preview workspace

A new `Report Preview` tab has been added.

It includes:

- `Refresh`
- `Export HTML / PDF`
- embedded HTML preview browser

The preview uses the same standalone report generator as export.

## Report workflow

1. Run a session or open a `.ariec` capture.
2. Open `Report Preview`.
3. Click `Refresh`.
4. Click `Export HTML / PDF`.
5. Open the generated HTML in a browser and print/save as PDF.

## Preserved

- utility-style standalone HTML report.
- Smart Capture Rules.
- Protocol Trace default workspace.
- Unified `.ariec` capture.
- Left-rail Auto Scroll Latest.


## Earlier releases

Historical notes from v0.1 through v3.6.5 are kept in `docs/archive/release-notes/` to preserve engineering history without overloading the public README.
