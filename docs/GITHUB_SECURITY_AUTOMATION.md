# GitHub security automation

ARIEC60870 uses GitHub-native security automation in a conservative, low-noise configuration.

## Dependabot

`.github/dependabot.yml` scans the actual package manifest directories used by the repository:

- GitHub Actions workflows from `/`.
- NuGet packages from the first-class project folders that contain `PackageReference` entries.

The NuGet configuration intentionally uses `directories:` instead of a single root `/` scan so Dependabot does not miss test and runtime project manifests in a multi-project .NET repository. Updates are grouped by purpose to keep pull requests reviewable:

- `github-actions-minor-patch`
- `github-actions-major`
- `dotnet-test-tooling`
- `dotnet-runtime-packages`
- `dotnet-major-updates`

Major updates are grouped separately so breaking changes are visible and can be reviewed deliberately.

## OpenSSF Scorecard

`.github/workflows/scorecard.yml` follows the Scorecard action publishing restrictions:

- no workflow-level write permissions,
- top-level `contents: read` only,
- `id-token: write` is granted only on the Scorecard job,
- `security-events: write` is granted only on the Scorecard job,
- supported `push` and `schedule` triggers are used,
- SARIF is uploaded to GitHub code scanning.

The README badge uses the Scorecard API badge so it reflects the published Scorecard result rather than only the workflow run status.

## Troubleshooting

If Dependabot does not open pull requests, check **Insights → Dependency graph → Dependabot** and confirm that version updates are enabled for the repository.

If Scorecard fails with `Resource not accessible by integration`, verify that repository Actions permissions allow `GITHUB_TOKEN` to read repository metadata and upload code scanning alerts. For private repositories, the Scorecard job also includes `issues: read`, `pull-requests: read`, and `checks: read` to avoid GraphQL/SAST visibility gaps.
