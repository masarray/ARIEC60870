# ARIEC60870 Public Release Readiness

This document summarizes the public-release readiness status of the repository. It is intended to help users and contributors understand what is included in the public source tree and what is intentionally excluded.

## Current status

ARIEC60870 is prepared for public source review and Windows portable package distribution.

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
