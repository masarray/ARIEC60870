# ARIEC60870 Documentation

Start here when browsing the repository documentation.

## User-facing pages

- [User Guide](USER_GUIDE.md) — everyday use, workspace overview, evidence review, and PDF export.
- [Quick Start](QUICK_START.md) — shortest path from download to first evidence report.
- [Troubleshooting](TROUBLESHOOTING.md) — transport, addressing, General Interrogation, mapping, and report review checks.
- [Validation Matrix](VALIDATION_MATRIX.md) — supported protocol evidence areas and release validation notes.
- [Release Packaging](RELEASE_PACKAGING.md) — what the Windows user ZIP contains and how it is verified.

## Engineering references

- [Architecture](ARCHITECTURE.md) — product boundaries, protocol ownership, and desktop/engine separation.
- [Mapping Profile Schema](MAPPING_PROFILE_SCHEMA.md) — JSON mapping profile structure for readable signal names.
- [Master Polling Policy](MASTER_POLLING_POLICY.md) — IEC-101/103 polling behavior, GI follow-up, and Class 1/Class 2 handling.
- [IEC-101 Dual Link Redundancy](IEC101_DUAL_LINK_REDUNDANCY.md) — active/standby dual serial-link engine, failover/recovery policy, and evidence rules.
- [IEC-101 Dual Link Workspace](IEC101_DUAL_LINK_WORKSPACE.md) — dedicated desktop layout for controller, active link, standby link, recovery status, image status, and failover evidence.
- [IEC-101 Dual Link FAT Checklist](IEC101_DUAL_LINK_FAT_CHECKLIST.md) — startup, manual switch, failure/recovery proof, and report acceptance checklist.
- [Event Log Policy](EVENT_LOG_POLICY.md) — how event evidence should be captured and presented.
- [Native PDF Engine](NATIVE_PDF_ENGINE.md) — built-in PDF report generation boundary.
- [Testing Strategy](TESTING_STRATEGY.md) — smoke tests, regression tests, and CI expectations.

## Website and GitHub Pages

- [GitHub Pages Deployment](GITHUB_PAGES_DEPLOYMENT.md) — canonical `site/` source, `/docs` compatibility mirror, and asset rules.
- [GitHub SEO](GITHUB_SEO.md) — metadata, structured data, sitemap, and search-facing page rules.
- [Repository Hygiene](GITHUB_REPOSITORY_HYGIENE.md) — public-source boundaries and clean release checks.
- [Security Automation](GITHUB_SECURITY_AUTOMATION.md) — Dependabot, Dependency Review, Scorecard, and low-noise update policy.

## Archived notes

Historical implementation notes and older release notes are kept under [archive/](archive/). They are useful for project history, but the current user-facing truth should come from the files listed above.
