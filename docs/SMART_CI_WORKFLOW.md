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

CodeQL automatic triggers cover C#/C# projects, XAML, resource/build configuration and its own workflow. Weekly scheduled and manual CodeQL runs remain unfiltered. NuGet package cache is shared by matching Windows restore inputs. The Pages workflow deploys canonical `site/`, so changes to engineering `docs/**` alone do not redeploy an identical website.

OpenSSF Scorecard is also scheduled weekly and can be manually dispatched; it runs immediately on security configuration changes (`.github/**`, `SECURITY.md`, `.gitattributes`), not on every ordinary IEC-101 source commit. Hygiene and dependency-review checks cancel their own superseded PR revisions while retaining default-branch validation.

## Maintainer PR comment commands

Post a new top-level PR comment with exactly:

- `/ci full` — full Windows build, protocol/xUnit tests, coverage, portable GUI and monitor-CLI ZIP, WPF acceptance screenshots.
- `/ci installer` — everything in `/ci full` plus isolated previous-release upgrade, installer registration and live-Master shutdown acceptance.

The dispatcher only accepts authenticated maintainers with repository write access, same-repository open PRs targeting the default branch, and uses the trusted default-branch CI workflow. That workflow checks out and verifies the **exact PR head SHA** rather than interpreting the workflow run's master SHA as tested source. No comment triggers public release or physical hardware.

Comment admission is serialized per PR. An active or successful run with matching PR number, profile, tested SHA AND default-branch CI revision will not be started again. If a previous run failed, investigate before requesting a repeat; use GitHub's Re-run failed jobs for verified infrastructure/transient problems.

## Milestone operation and safety

1. Make coherent changes, use automatic Windows correctness checks, and fix true failures.
2. When an integration SHA is stable, invoke `/ci full` only if GUI/WPF artifact evidence is required. An identical successful request is reused.
3. Invoke `/ci installer` only for installer or release-upgrade readiness.
4. Inspect exact-head screenshots, checksums, release artifacts and tests. Physical IEC-101/RTU FAT and multi-vendor interoperability are separate evidence.
5. Public packages/releases stay in `release-package.yml`; these comment commands cannot publish a release.

Release checks, real serial connectivity and safety requirements are not relaxed by smarter CI scheduling.
