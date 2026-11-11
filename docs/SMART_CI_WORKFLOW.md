# Smart CI / selective evidence workflow (ArSee / ARIEC60870)

This separates iterative correctness from costly field-ready artifacts. It does **not** change IEC-101 behavior, physical RTU testing, or release authorization.

## Automatic policy

| Trigger | Execution |
| --- | --- |
| Markdown-only PR or master push | Lightweight classification and a successful historical `build-test` check using Ubuntu; no Windows runner |
| Source, XAML, tests, project, scripts, YAML or other PR changes | Windows Release build with warning gate, protocol smoke, all six xUnit regression suites and TRX; existing nonblocking formatter diagnostics only when C#/formatter configuration changed |
| Non-documentation master push | Windows test gate, coverage, self-contained GUI + monitor-cli ZIPs, real-WPF acceptance/screenshots and SHA-256 artifacts |
| Security checks | Existing CodeQL, dependency-review and repository-hygiene workflows remain independent; superseded CodeQL PR analysis is cancelled |
| Release | Existing `release-package.yml` is not changed; publishing still has its own dedicated release gate |

Any non-Markdown file, unclassified change, or failed push comparison defaults to running Windows. The existing `build-test` check name is retained. Standard feature PRs avoid the repeated Windows single-file publishing, WPF acceptance, coverage collection and installer acceptance costs. The pre-existing formatter is nonblocking and reports legacy whitespace debt; it is not a clean-format gate and is not run for unrelated YAML/documentation changes.

NuGet package caching is keyed to project/restore configuration. Superseded pull-request CI is cancelled; default-branch evidence is not cancelled by the concurrency policy.

## PR comment commands (maintainer only)

Add a **new top-level PR Conversation comment** with exactly one of the following:

- `/ci full`: Run build + full protocol/xUnit suites + coverage, publish Windows GUI and monitor-cli artifacts, smoke and real-WPF acceptance with screenshots.
- `/ci installer`: All of the above plus previous-release upgrade, restart-manager and running-Master shutdown acceptance.

The dispatcher executes trusted control-plane code from the default branch, does not check out PR code with write permissions, checks the comment author's current repository `write`/`maintain`/`admin` permission, requires an open same-repo PR targeting the default branch and captures the exact PR head SHA. **The build itself uses the CI workflow from `master`, then checks out and verifies the exact requested PR SHA**; this also supports old PR branches that do not yet contain the new workflow. A changed PR head rejects the dispatched run. The visible workflow run name includes PR number, profile and tested SHA, and an active identical run is not dispatched twice.

The comment commands become available **after** `.github/workflows/ci-comment.yml` is merged into the default branch. The GitHub Actions workflow-dispatch endpoint requires the target workflow to be registered on the default branch.

## Milestone workflow

1. Make a coherent code change and review the automatic Windows regression status.
2. Fix real failures instead of rerunning the entire costly suite. Re-run only a failed job when its failure was demonstrably transient/infrastructure-related.
3. After code stabilizes, request one `/ci full` at the exact integration SHA if a GUI artifact or WPF evidence is needed.
4. Request `/ci installer` only when installer/upgrade behavior or release readiness must be verified. Avoid requesting it on every source commit.
5. For public release, inspect WPF screenshots, artifacts, checksums and exact SHA, then perform the physical IEC-101/RTU FAT independently. Automated CI must never be described as successful physical interoperability.

**Safety:** Comment commands do not merge branches, publish public releases, alter RTU configuration, connect physical serial devices, or grant third-party pull requests write-token execution.
