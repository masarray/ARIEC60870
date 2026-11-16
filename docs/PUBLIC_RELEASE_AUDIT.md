# ARIEC60870 Public Release Readiness

This document summarizes the public-release readiness status of the repository. It is intended to help users and contributors understand what is included in the public source tree and what is intentionally excluded.

## Current status

**Latest published stable: [ArSee v3.6.12](https://github.com/masarray/ARIEC60870/releases/tag/v3.6.12)** (source tag `01ac138530f0c848dd63e4b875706698fdb174fc`). Windows x64 installer and portable ZIP, SHA-256, SPDX SBOM, provenance and exact-source updater FAT passed in [release package run 43802516487](https://github.com/masarray/ARIEC60870/actions/runs/43802516487) and [native FAT run 43801986135](https://github.com/masarray/ARIEC60870/actions/runs/43801986135). The later mainline commit updating the public Pages/README is documentation-only and does **not** change the immutable v3.6.12 release executable.

The historical **v3.6.8-rc.1** audit is archived context, not current publication status. The release contains merged IEC-101 service-correlated reply validation (#208), signal-name parity in Events/Values/Trace/History (#210), and the prior bounded 1200-bps FT1.2/forensic work. CI passing is **not** a substitute for low-baud SICAM A8000/multi-vendor physical FAT: real SOE counts 60/500/2,000, COM recovery, dual-link A/B, GI and SBO/DO confirmation remain unproven.

Ongoing hardening work on other branches (including the shared CA observation policy) must not be claimed as part of the v3.6.12 binary until a new exact-source release is built and published. Draft PR #145 remains unmerged. Repository branch protection / mandatory PR checks are tracked separately in [issue #159](https://github.com/masarray/ARIEC60870/issues/159) and require an administrative ruleset; green YAML alone does not enforce merges.

The repository includes:

- source code for the desktop app, command-line tools, protocol core, and master session logic;
- protocol smoke tests;
- public documentation for quick start, troubleshooting, validation tracking, and release assets;
- static landing page for GitHub Pages;
- Apache-2.0 license, notice files, security policy, and repository metadata;
- sanitized samples only.

## Public package boundary

Public source and release packages are expected to contain:

- source files and tests;
- documentation and sanitized samples;
- generated Windows portable package from a clean build;
- release notes and checksum file;
- license and notice files.

Public source and release packages are not intended to contain:

- real customer or utility traces;
- private COM logs, PCAP files, spreadsheets, PDFs, MSG files, or project exports;
- generated reports from real projects;
- secrets, tokens, private endpoints, or production configuration;
- local IDE state or build output folders.

## Evidence privacy

ARIEC60870 is designed to keep protocol evidence visible while reducing accidental disclosure of local workstation paths. Public reports use mapping profile file names by default instead of full local paths.

Users should still review exported evidence before sharing it outside the project team because relay addresses, project signal names, raw frame evidence, comments, and mapping labels may still be project-sensitive.

## Clean source principle

ARIEC60870 keeps relay signal naming user-owned through mapping profiles. The application decodes protocol-level evidence and avoids shipping built-in vendor-specific signal databases.

Contributor changes should keep protocol behavior, documentation, and samples legally clean and reproducible.
