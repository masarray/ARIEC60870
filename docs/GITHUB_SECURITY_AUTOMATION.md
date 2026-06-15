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


## Dependency Review Action

GitHub Dependency Review is useful, but it only works after the repository Dependency Graph is available. GitHub documents that dependency review becomes available when the dependency graph is enabled. For this reason, `.github/workflows/dependency-review.yml` is intentionally guarded by the repository variable `ENABLE_DEPENDENCY_REVIEW`.

Recommended setup:

1. Open **Settings → Security and analysis**.
2. Enable **Dependency graph**.
3. Enable Dependabot alerts/security updates if available for the repository.
4. Open **Settings → Secrets and variables → Actions → Variables**.
5. Add repository variable `ENABLE_DEPENDENCY_REVIEW` with value `true`.

Until that variable is set, the workflow posts a notice and exits successfully so Dependabot pull requests are not blocked by an unavailable GitHub API feature. Once the repository is configured, setting the variable to `true` turns the workflow into an enforcement gate with `fail-on-severity: high`.

## Troubleshooting

If Dependabot does not open pull requests, check **Insights → Dependency graph → Dependabot** and confirm that version updates are enabled for the repository. If the Dependency Review workflow says that dependency review is not supported, enable **Settings → Security and analysis → Dependency graph** and set `ENABLE_DEPENDENCY_REVIEW=true`.

If Scorecard fails with `Resource not accessible by integration`, verify that repository Actions permissions allow `GITHUB_TOKEN` to read repository metadata and upload code scanning alerts. For private repositories, the Scorecard job also includes `issues: read`, `pull-requests: read`, and `checks: read` to avoid GraphQL/SAST visibility gaps.
