# GitHub security automation

ARIEC60870 uses GitHub-native security automation in a conservative, low-noise configuration.

## Dependabot

`.github/dependabot.yml` scans the actual package manifest directories used by the repository:

- GitHub Actions workflows from `/`.
- NuGet packages from the first-class project folders that contain `PackageReference` entries.

The NuGet configuration intentionally uses `directories:` instead of a single root `/` scan so Dependabot does not miss test and runtime project manifests in this multi-project .NET repository.

Routine version updates are grouped by purpose:

- `github-actions-minor-patch`
- `dotnet-test-tooling`
- `dotnet-runtime-packages`

Major updates are intentionally ignored by routine Dependabot automation. They can include runner/runtime/API breaking changes and should be handled as planned maintenance branches, not bulk automatic PRs.

When GitHub announces a required major migration:

1. create a dedicated maintenance branch;
2. update actions or NuGet packages deliberately;
3. run CI, Pages, repository hygiene, release package, and Scorecard workflows;
4. merge only after verification.

## OpenSSF Scorecard

`.github/workflows/scorecard.yml` follows the Scorecard action publishing restrictions:

- no workflow-level write permissions;
- top-level `contents: read` only;
- `id-token: write` is granted only on the Scorecard job;
- `security-events: write` is granted only on the Scorecard job;
- supported `push` and `schedule` triggers are used;
- SARIF is uploaded to GitHub code scanning.

The README keeps a low-noise status badge set. Scorecard results remain available through the Scorecard workflow and GitHub code scanning output rather than a remote API badge.

## Dependency Review Action

GitHub Dependency Review is useful, but it only works after the repository Dependency Graph is available. For that reason, `.github/workflows/dependency-review.yml` is guarded by the repository variable `ENABLE_DEPENDENCY_REVIEW`.

Recommended setup:

1. Open **Settings → Security and analysis**.
2. Enable **Dependency graph**.
3. Enable Dependabot alerts/security updates if available for the repository.
4. Open **Settings → Secrets and variables → Actions → Variables**.
5. Add repository variable `ENABLE_DEPENDENCY_REVIEW` with value `true`.

Until that variable is set, the workflow posts a notice and exits successfully so Dependabot pull requests are not blocked by an unavailable GitHub API feature. Once the repository is configured, setting the variable to `true` turns the workflow into an enforcement gate.

## Troubleshooting

If Dependabot pull requests do not appear, confirm that version updates are enabled for the repository and that the manifest directories in `.github/dependabot.yml` still exist.

If the Dependency Review workflow says dependency review is not supported, enable **Settings → Security and analysis → Dependency graph** and set `ENABLE_DEPENDENCY_REVIEW=true`.

If Scorecard fails with `Resource not accessible by integration`, verify that repository Actions permissions allow `GITHUB_TOKEN` to read repository metadata and upload code scanning alerts. For private repositories, the Scorecard job also includes `issues: read`, `pull-requests: read`, and `checks: read` to avoid GraphQL/SAST visibility gaps.

## Current policy

- Minor and patch updates: automated Dependabot PRs.
- Major updates: planned maintenance branch.
- Dependency Review: opt-in enforcement after Dependency Graph is enabled.
- Scorecard: scheduled and push-based visibility for public security posture.
