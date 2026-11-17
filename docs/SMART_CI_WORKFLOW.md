# Smart CI — ArSee / ARIEC60870

Goal: fast iteration without hiding real regressions, duplicating Windows ZIPs or describing CI as physical FAT. This is the operational contract for `.github/workflows/ci.yml`, `ci-comment.yml`, `codeql.yml` and `pages.yml`.

## Automatic triggers

| Change | Windows build/tests | Windows ZIP + WPF acceptance on master | CodeQL | Pages deployment |
| --- | --- | --- | --- | --- |
| Markdown-only or `docs/**` | No; lightweight `build-test` context succeeds on Ubuntu | No | No | No |
| `site/**` only | No | No | No | Yes |
| `.github/workflows/ci.yml` only | Yes, including protocol smoke + six xUnit suites | No | No | No |
| Tests-only `.cs` | Yes | No | Yes | No |
| C# production, XAML, build inputs | Yes | Yes | Yes | No |
| Installer or runtime scripts | Yes | Yes | No (unless code also changes) | No |
| `.github/workflows/codeql.yml` | Yes | No | Yes | No |
| `.github/workflows/pages.yml` | Yes | No | No | Yes |
| Unknown files / oversized diff / compare API error | Windows tests, fail-safe | Package on master if classification unavailable | Glob-filtered, weekly scan remains | Only site or Pages workflow |

`build-test` remains the historical status context, even when no Windows runner is needed. Pull requests with source/test/workflow changes run Release build with warnings-as-errors, protocol smoke, six xUnit regression suites, and TRX logs. A pre-existing non-blocking `dotnet format` runs for C#/formatter configuration changes; legacy whitespace diagnostics are NOT proof of a clean-format gate.

Master pushes affecting `src/**`, `installer/**`, `scripts/**`, resources, or build inputs additionally collect coverage, publish the portable GUI/monitor CLI ZIPs with checksums and run real-WPF acceptance/screenshots. Master pushes changing only test code or CI YAML skip these expensive packages. For documentation-only or site-only changes, Windows compilation is unrelated and skipped. All unknown/large changes fall back to the safe full Windows route.

Actions workflow edits run a lightweight Ubuntu `actionlint` gate with a pinned, checksum-verified binary, in addition to the normal Windows correctness tests. Linting is deliberately scoped to workflow YAML changes and does not run on ordinary source/documentation commits.

CodeQL automatic triggers cover C#/C# projects, XAML, resource/build configuration and its own workflow. Weekly scheduled and manual CodeQL runs remain unfiltered. NuGet package cache is shared by matching Windows restore inputs. The Pages workflow deploys canonical `site/`, so changes to engineering `docs/**` alone do not redeploy an identical website.

OpenSSF Scorecard is also scheduled weekly and can be manually dispatched; it runs immediately on security configuration changes (`.github/**`, `SECURITY.md`, `.gitattributes`), not on every ordinary IEC-101 source commit. Hygiene and dependency-review checks cancel their own superseded PR revisions while retaining default-branch validation.

## Maintainer PR comment commands

Post a new top-level PR comment with exactly:

- `/ci full` — full Windows build, protocol/xUnit tests, coverage, portable GUI and monitor-CLI ZIP, WPF acceptance screenshots.
- `/ci installer` — everything in `/ci full` plus isolated previous-release upgrade, installer registration and live-Master shutdown acceptance.

The dispatcher only accepts authenticated maintainers with repository write access, same-repository open PRs targeting the default branch, and uses the trusted default-branch CI workflow. That workflow checks out and verifies the **exact PR head SHA** rather than interpreting the workflow run's master SHA as tested source. The `/ci` comments never publish public releases; the separate source-qualified `/release stable` command below may dispatch Package only after exact-SHA evidence gates.

Comment admission is serialized per PR. An active or successful run with matching PR number, profile, tested SHA AND default-branch CI revision will not be started again. If a previous run failed, investigate before requesting a repeat; use GitHub's Re-run failed jobs for verified infrastructure/transient problems.

## Source-qualified stable release by a single comment

Comment **exactly** `/release stable` on the **latest merged same-repository PR whose
resulting merge commit equals current `master` HEAD**. The comment must originate
from a repository collaborator with write access. Do not use an old PR, unmerged PR,
a different issue, an anonymous bot or a copied stale SHA.

The trusted default-branch `.github/workflows/release-comment.yml` controller
checks exact merge SHA, source `Directory.Build.props`, versioned release notes,
a stable version newer than the latest published stable and immutable tag ownership.
It rejects malformed or expired (>24 h) requests. The comment is the durable
authorization, not a shortcut around release validation.

The controller **reuses** successful exact-SHA master CI, 19 protocol smoke,
six xUnit suites, real-WPF acceptance and actual native Windows installed-updater
FAT. A PR-bound manual CI workflow that checked out different bytes is **never**
accepted as master FAT. Failed gates block; pending CI/CodeQL/hygiene/Package runs
are awaited rather than duplicated. If exact-source native FAT is absent, it
dispatches **one** `CI profile=updater`; after source-build, installer,
and native updater FAT jobs succeed, a tiny exact-SHA `release-resume`
handoff explicitly dispatches the trusted controller. The CI-completion webhook
remains an additional idempotent path, not the sole continuation mechanism.
The controller then dispatches **one** Package run, without another user comment.

Durable short-lived **Actions artifact reservations**, uploaded before dispatch,
bind comment ID, commit SHA and release stage. This avoids relying on
`GITHUB_TOKEN` write access to issue comments (which is restricted in this repo)
while preventing duplicate completion callbacks from enqueueing extra
Windows work. The controller verifies artifact provenance against the trusted
default-branch release workflow; **a reservation is not a dispatch receipt**.
For a completed controller run, its exact `Dispatch one pinned next stage` step
must have succeeded before the stage is considered dispatched. While the owner
run is active, other admission runs wait. If the owner fails or is cancelled
after upload, the stage blocks and reports its original workflow run ID:
investigate and issue a **new** authorized comment with a distinct request ID.
No implicit retries, no hiding a failed dispatch as `already-dispatched`, and
no bypass of SHA/version/FAT gates. Artifact-name lookup is bounded; overflow
fails closed rather than ignoring a legitimate reservation. Only the maintainer's
original comment authorizes each request.
Controller admission is serialized repository-wide. Package
also uses a global concurrency key across branches/tags, and its
`expected_sha` lease refuses moving HEAD before Windows packaging begins.
It still independently verifies FAT and then compiles/tests/attests immutable
Setup/ZIP/SBOM/SHA-256 assets. Failed publication needs investigation and a
**fresh explicit release command**, never a blind retry loop.

The command does not change the public website/README's last-published download
links by assumption. Sync them after the GitHub Release package succeeds.
Physical RTU/SCADA FAT remains independent and cannot be inferred from CI.

## Milestone operation and safety

1. Make coherent changes, use automatic Windows correctness checks, and fix true failures.
2. When an integration SHA is stable, invoke `/ci full` only if GUI/WPF artifact evidence is required. An identical successful request is reused.
3. Invoke `/ci installer` only for installer or release-upgrade readiness.
4. Inspect exact-head screenshots, checksums, release artifacts and tests. Physical IEC-101/RTU FAT and multi-vendor interoperability are separate evidence.
5. `/ci` comments never publish. A separately authorized `/release stable` comment may invoke `release-package.yml` only after exact-source CI, native updater FAT, version and security gates pass.

Release checks, real serial connectivity and safety requirements are not relaxed by smarter CI scheduling.

## Release/RC source and publication safety

The `Package` workflow retains its full Windows tests, WPF evidence, SBOM, SHA256 and provenance checks. The early Ubuntu `resolve` gate additionally rejects invalid SemVer, inconsistent RC/stable flags, unsafe release-notes paths, and public publication from an unrelated branch. A default-branch stable release must equal `Directory.Build.props`; RC branches/tags must match their exact version and the base version in source.

Before scheduling Windows packaging, the trusted GitHub API checks an existing release tag (including annotated tags) against the **actual workflow source SHA**. A tag pointing elsewhere is an error: bump the RC number rather than republishing changed code under the same tag. A successfully published release at the same tag/SHA short-circuits redundant Windows packaging. The release job rechecks tag ownership just before publication and refuses to clobber assets of an existing public release; drafts can be updated deliberately.

Do not equate a green reusable release run with field-tested physical interoperability. RC download links point to immutable release-source evidence, but real Siemens/other-vendor IEC101 FAT remains separate.

## Remaining GitHub repository policy (requires repository Settings access)

At the 2026-11-11 audit, `master` was **not protected** and GitHub returned no rulesets. The GitHub connector used here exposes repository ruleset/branch reads but no rule mutation capability. Workflow YAML cannot impose required checks or disallow direct pushes. The repository administrator should configure **Settings → Rules → Rulesets → New branch ruleset**, targeting `master`, to require pull requests and required checks (`build-test` and the always-run repository hygiene job), disallow force pushes/deletions and decide deliberate maintainer bypasses. Keep the path-filtered CodeQL check optional unless all PRs are guaranteed to create its status, or use an always-present wrapper. The dependency-review job currently has an opt-in switch: a green status **does not prove actual dependency review** unless repository variable `ENABLE_DEPENDENCY_REVIEW=true` and the dependency graph/action are available. Do not claim that check is enforced before verifying its logs.
