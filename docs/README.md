# ArSee documentation and GitHub Pages mirror

Official site: https://masarray.github.io/arsee/ · [Public prerelease v3.6.17-rc.7](https://github.com/masarray/arsee/releases/tag/v3.6.17-rc.7) · [Latest stable v3.6.16](https://github.com/masarray/arsee/releases/tag/v3.6.16).

**Canonical public site: `site/`.** HTML, CSS and SEO assets copied into `docs/` are a generated **GitHub Pages compatibility mirror**, not a separate website. After editing `site/`, run `scripts/sync-github-pages-docs-mirror.ps1` and the repository tests. Never hand-edit the mirror independently.

Engineering docs: [User Guide](USER_GUIDE.md), [Quick Start](QUICK_START.md), [Architecture](ARCHITECTURE.md), [Testing Strategy](TESTING_STRATEGY.md), [Release Notes](RELEASE_NOTES.md). Future product targets are defined by [IEC-101/103/104 PRD](IEC10X_INTEROPERABILITY_PRD_20261029.md), [ARD](IEC10X_INTEROPERABILITY_ARD_20261029.md) and [NUC research](IEC10X_NUC_RESEARCH_20261029.md); **only merged code and exact CI** establish released implementation.

Draft PRs, simulator results and unverified field behavior must not be marketed as released or certified.

**Active IEC-101 P0 implementation authority:** [Transaction/serial reliability blueprint](IEC101_RELIABILITY_IMPLEMENTATION_PLAN.md) · [Deterministic test & field FAT matrix](IEC101_RELIABILITY_VERIFICATION_MATRIX.md) · [Milestone issue #143](https://github.com/masarray/arsee/issues/143). This is an implementation target, **not** a released feature claim; it supplements cross-protocol PRD/ARD and keeps draft IEC-101 PR #145 separate from the published v3.6.14 stable software.

The latest **public RC.7** adds concise FC10/FC11 polling tooltips, state-accurate Latest Event highlighting and bilingual Help Center updates; stable v3.6.16 remains available. RC.7 was built from release branch SHA `dfbdac844e3ce5b5a82c94669a4ec0bb8d59a054` and passed [packaging workflow #44478198266](https://github.com/masarray/arsee/actions/runs/44478198266). No physical IEC/SCADA FAT certification is implied; draft transaction/reconnect PR #145 is unmerged. See [repository and Pages rename plan](ARSEE_REBRANDING_REPOSITORY_RENAME_20261112.md).
