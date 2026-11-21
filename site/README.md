# ArSee public website

Canonical English/Indonesian site: https://masarray.github.io/arsee/

**Latest public stable:** [v3.6.19](https://github.com/masarray/arsee/releases/tag/v3.6.19) · **previous stable:** [v3.6.18](https://github.com/masarray/arsee/releases/tag/v3.6.18) · [RC.7 prerelease archive](https://github.com/masarray/arsee/releases/tag/v3.6.17-rc.7). [Stable installer](https://github.com/masarray/arsee/releases/download/v3.6.19/ArSee-v3.6.19-setup-win-x64.exe) and [portable ZIP](https://github.com/masarray/arsee/releases/download/v3.6.19/ArSee-v3.6.19-win-x64.zip) are official, source-bound published assets. Physical SICAM/SCADA FAT is not certified; historical IEC-101 draft #145 is closed superseded.


For registered legacy v3.6.17/v3.6.18 installations in `Programs/ARIEC60870`, close IEC/capture sessions and run the official v3.6.19 installer directly for a guarded move to `Programs/ArSee`. Do **not** manually uninstall or delete `%LOCALAPPDATA%/ARIEC60870`, which contains operator data. The old in-app updater cannot relocate its own program folder.

- `index.html`, `id/index.html`: bilingual product landing with concise hero copy.
- `demo.html`, `id/demo.html`: localized product demo, screenshots, and video.
- `download.html`, `id/download.html`: localized Windows package and safety guide.
- Shared `script.js` progressively enhances compact mobile navigation; without JavaScript, navigation, screenshots, and FAQ content remain visible.
- P3 keeps a single self-hosted CSS bundle with shared layout/focus tokens. Static content is visible before script load; costly card tilt/ripple/reveal listeners are intentionally omitted. Mobile screenshot zoom hints remain visible on touch devices.
- `download.html`, `quick-start.html`: Windows installer/portable paths and integrity.
- `wiki.html`, `id/wiki.html`: field-oriented reference content.
- `faq.html`, `id/faq.html`: honest scope and limitations.
- `seo-manifest.json`, `sitemap.xml`, `llms.txt`: published release metadata. Public RCs are built on release branches; do not silently bump stable master source version to match them.

Edit `site/` and run `scripts/sync-github-pages-docs-mirror.ps1` to refresh `docs/`. Do not maintain two separate public sites.

Local preview: `python -m http.server 8080 --directory site`.

The product is **ArSee**. The canonical Pages path is `/arsee/`; `/ARIEC60870/` is historical and is not the active GitHub Pages project URL. See [rename runbook](../docs/ARSEE_REBRANDING_REPOSITORY_RENAME_20261112.md).
