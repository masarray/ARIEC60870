# ArSee public website

Canonical English/Indonesian site: https://masarray.github.io/arsee/

**Latest stable:** [v3.6.16](https://github.com/masarray/arsee/releases/tag/v3.6.16) · **previous stable:** [v3.6.15](https://github.com/masarray/arsee/releases/tag/v3.6.15). Physical SICAM FAT is not certified and draft IEC-101 engine PR #145 remains unreleased.

- `index.html`, `id/index.html`: bilingual product landing.
- `download.html`, `quick-start.html`: Windows installer/portable paths and integrity.
- `wiki.html`, `id/wiki.html`: field-oriented reference content.
- `faq.html`, `id/faq.html`: honest scope and limitations.
- `seo-manifest.json`, `sitemap.xml`, `llms.txt`: published release metadata.

Edit `site/` and run `scripts/sync-github-pages-docs-mirror.ps1` to refresh `docs/`. Do not maintain two separate public sites.

Local preview: `python -m http.server 8080 --directory site`.

The product is **ArSee**. The canonical Pages path is `/arsee/`; `/ARIEC60870/` is historical and is not the active GitHub Pages project URL. See [rename runbook](../docs/ARSEE_REBRANDING_REPOSITORY_RENAME_20261112.md).
