# ArSee public website

Canonical English/Indonesian site: https://masarray.github.io/ARIEC60870/

**Current public preview:** [v3.6.8-rc.5](https://github.com/masarray/ARIEC60870/releases/tag/v3.6.8-rc.5) · **last stable:** [v3.6.7](https://github.com/masarray/ARIEC60870/releases/tag/v3.6.7). The preview is not physical SICAM FAT certified; draft PR #145 is not released.

- `index.html`, `id/index.html`: bilingual product landing.
- `download.html`, `quick-start.html`: Windows installer/portable paths and integrity.
- `wiki.html`, `id/wiki.html`: field-oriented reference content.
- `faq.html`, `id/faq.html`: honest scope and limitations.
- `seo-manifest.json`, `sitemap.xml`, `llms.txt`: published release metadata.

Edit `site/` and run `scripts/sync-github-pages-docs-mirror.ps1` to refresh `docs/`. Do not maintain two separate public sites.

Local preview: `python -m http.server 8080 --directory site`.

The product is **ArSee**. Keep `/ARIEC60870/` Pages canonical until the repository rename and website cutover. See [rename runbook](../docs/ARSEE_REBRANDING_REPOSITORY_RENAME_20261112.md).
