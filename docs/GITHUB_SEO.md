# GitHub and Landing Page SEO Checklist

ARIEC60870 is discovered through GitHub search, external search engines, shared landing-page links, and direct release links. Keep repository wording clear, user-oriented, and consistent across README, landing page, manifest, Open Graph metadata, release notes, and GitHub About settings.

## Repository About section

Recommended GitHub repository description:

```text
Free Apache-2.0 Windows IEC 60870-5-101 / 103 / 104 protocol evidence analyzer for FAT/SAT, commissioning, SCADA, RTU, and protection relay testing.
```

Recommended website URL:

```text
https://masarray.github.io/ARIEC60870/
```

## Recommended GitHub topics

Use focused topics that match how relay, SCADA, RTU, and substation automation engineers search:

```text
iec60870
iec60870-5-101
iec60870-5-103
iec60870-5-104
iec101
iec103
iec104
scada
rtu
substation-automation
protection-relay
protocol-analyzer
fat-sat
commissioning
wpf
dotnet
windows-desktop
```

The same list is documented in `.github/repository-metadata.yml` so maintainers can copy it into GitHub settings.

## README search coverage

The README should naturally include these phrases in user-facing sections:

- IEC 60870-5-101 tester
- IEC 60870-5-103 master tester
- IEC 60870-5-104 client tester
- IEC 60870 protocol analyzer
- SCADA protocol analyzer
- RTU communication test
- protection relay testing
- FAT/SAT evidence
- commissioning troubleshooting
- raw frame trace
- event log
- user-owned mapping profile

Avoid keyword stuffing. The wording must read like a product page for engineers who want to understand, download, build, and evaluate the application.

## Landing-page SEO coverage

The landing page should include:

- canonical URL
- concise `<title>` and meta description
- Open Graph and Twitter preview metadata
- absolute preview image URL
- SoftwareApplication structured data
- FAQPage structured data
- BreadcrumbList structured data
- visible FAQ section
- robots.txt and sitemap.xml
- clear download CTA to GitHub Releases
- mobile-friendly layout and readable heading hierarchy

## Release SEO and trust

Each GitHub Release should include:

- versioned asset names
- checksum file
- clear package difference between portable and singlefile ZIPs
- short evaluation notes
- link to changelog
- license and clean-room statement when relevant

## Manual GitHub settings not controlled by source code

These cannot be changed from repository files alone. Set them from the GitHub web UI:

1. Repository description.
2. Repository website URL.
3. Repository topics.
4. Social preview image, if a custom preview is preferred.
5. GitHub Pages source set to GitHub Actions.


## GitHub Pages source of truth

The canonical landing page source is `site/`. The Pages workflow publishes this folder directly, so SEO changes should be made there first. Keep `title`, `description`, Open Graph metadata, structured data, `sitemap.xml`, `robots.txt`, `llms.txt`, `seo-manifest.json`, and `site.webmanifest` aligned with the current release line.

A generated `/docs` compatibility mirror is committed only to support repositories that still use **Deploy from branch → /docs**. Refresh it with `./scripts/sync-github-pages-docs-mirror.ps1` after site changes. Do not reintroduce the old `landing/` source.
