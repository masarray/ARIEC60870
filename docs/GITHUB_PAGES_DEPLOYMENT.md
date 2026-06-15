# GitHub Pages Deployment

ARIEC60870 uses a canonical static site source under `site/`.

Recommended setup:

1. Open the repository on GitHub.
2. Go to **Settings → Pages**.
3. Set **Source** to **GitHub Actions**.
4. Push to `main` or `master`, or run the **Deploy GitHub Pages site** workflow manually.

The workflow publishes `site/` directly. This avoids maintaining multiple hand-edited landing copies.

Compatibility files are intentionally minimal:

- Root `index.html` redirects to `site/` for local repository browsing or legacy branch-based setup.
- Root `404.html` redirects to `site/` for legacy branch-based setup.
- Historical `landing/` and `docs/` HTML mirrors are removed so the public site has one source of truth.

If the site still shows 404:

- Confirm GitHub Pages source is set to **GitHub Actions**.
- Confirm the **Deploy GitHub Pages site** workflow completed successfully.
- Confirm `site/index.html`, `site/sitemap.xml`, `site/robots.txt`, and `site/site.webmanifest` exist.
- Wait a few minutes after the first deployment.
- Open the repository URL without adding `/site` when using GitHub Actions mode.
