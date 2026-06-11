# ARIEC60870 Landing Page

This folder contains the static GitHub Pages landing page for ARIEC60870.

The page is written for users who want to understand what the application does, download the free Windows package, review screenshots, and follow the basic workflow.

Main user-facing sections:

- Free and open-source positioning for new users.
- What ARIEC60870 is for.
- Key desktop screens.
- Controlled IEC-103 polling behavior.
- Evidence model.
- Download and first-use flow.
- GitHub release and documentation links.

Deployment options:

- GitHub Actions publishes `landing/` through `.github/workflows/pages.yml`.
- `/docs` contains a compatibility copy for GitHub Pages branch deployments.
- Root `index.html` redirects visitors to `landing/` when Pages is served from repository root.

SEO files included:

- `index.html` with canonical URL, Open Graph/Twitter metadata, and structured data.
- `robots.txt` pointing to the public sitemap.
- `sitemap.xml` for the GitHub Pages landing URL.
- `site.webmanifest` for richer browser/app preview metadata.
