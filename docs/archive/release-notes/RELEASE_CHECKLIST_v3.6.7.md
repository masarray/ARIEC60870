# ARIEC60870 v3.6.7 Release Execution Checklist

Use this checklist before running the GitHub release package workflow.

## Version

- Version: `3.6.7`
- Tag: `v3.6.7`
- Release notes file: `docs/archive/release-notes/RELEASE_NOTES_v3.6.7.md`
- User package expected from workflow: `ARIEC60870-v3.6.7-win-x64.zip`

## Local verification

```powershell
dotnet restore ARIEC60870.sln
dotnet build ARIEC60870.sln --configuration Release -p:TreatWarningsAsErrors=true
dotnet test ARIEC60870.sln --configuration Release
```

## Optional local package preview

```powershell
pwsh ./scripts/publish-windows-portable.ps1 -Version 3.6.7
```

Expected output:

```text
artifacts/release/ARIEC60870-v3.6.7-win-x64.zip
artifacts/release/SHA256SUMS.txt
```

## GitHub Actions workflow inputs

Run workflow: **Package**

Recommended inputs:

```text
version: 3.6.7
publish_release: true
prerelease: false
draft: false
release_notes_file: docs/archive/release-notes/RELEASE_NOTES_v3.6.7.md
```

## Post-release smoke check

1. Download `ARIEC60870-v3.6.7-win-x64.zip` from GitHub Releases.
2. Extract to a clean folder.
3. Confirm the package has exactly one executable named `ARIEC60870.exe`.
4. Start the app and open Setup.
5. Run one IEC-101/104/103 demo or known-good field session.
6. Confirm Smart Findings, Values, Trace, Report export, and modern dialogs open correctly.
7. Confirm the update notification does not block startup or protocol activity.
