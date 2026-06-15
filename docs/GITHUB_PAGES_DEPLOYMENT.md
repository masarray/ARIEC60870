# GitHub Pages Deployment

ARIEC60870 keeps the public website source under `site/` and publishes it with the **Deploy GitHub Pages site** workflow.

Recommended setup:

1. Open the repository on GitHub.
2. Go to **Settings → Pages**.
3. Set **Source** to **GitHub Actions**.
4. Push to `main` or `master`, or run the **Deploy GitHub Pages site** workflow manually.

The workflow uploads `site/` directly as the Pages artifact, so the website root is served at:

```text
https://masarray.github.io/ARIEC60870/
```

## Compatibility fallback

Some repositories may still be configured as **Deploy from branch → /docs** from an older GitHub Pages setup. Removing `docs/index.html` in that state causes a 404 even when the canonical site exists under `site/`.

To prevent that failure mode, this repository now keeps a generated `/docs` compatibility mirror:

```text
site/   canonical source for GitHub Actions Pages
docs/   documentation plus generated static site mirror for branch-based /docs Pages
```

The mirror is not a second hand-edited website. Refresh it after changing `site/` with:

```powershell
./scripts/sync-github-pages-docs-mirror.ps1
```

Repository hygiene tests verify that important runtime files in `docs/` stay identical to `site/`.

## If the site shows 404

Check these items in order:

1. Confirm whether GitHub Pages is using **GitHub Actions** or **Deploy from branch**.
2. If using GitHub Actions, confirm the **Deploy GitHub Pages site** workflow completed successfully.
3. If using **Deploy from branch → /docs**, confirm `docs/index.html`, `docs/styles.css`, `docs/assets/`, `docs/sitemap.xml`, and `docs/site.webmanifest` are committed.
4. Confirm `site/index.html`, `site/sitemap.xml`, `site/robots.txt`, and `site/site.webmanifest` exist.
5. Wait a few minutes after the first deployment.
6. Open the repository URL without adding `/site` when using GitHub Actions mode.
