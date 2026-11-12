# ArSee branding and GitHub repository rename — 2026-11-12

**Product:** ArSee · Open-Source IEC 60870-5-101/103/104 Protocol Tester & Analyzer. The full individual standards are IEC 60870-5-101, IEC 60870-5-103 and IEC 60870-5-104.

| | Before rename | After confirmed cutover |
|---|---|---|
| GitHub | https://github.com/masarray/ARIEC60870 | https://github.com/masarray/arsee |
| Pages | https://masarray.github.io/ARIEC60870/ | https://masarray.github.io/arsee/ |

GitHub redirects old **repository** URLs on rename, but **does not redirect old GitHub Pages project-site URLs**. Do not publish new canonical, Open Graph, sitemap or manifest URLs until the new Pages site actually works.

## Stage 1: completed by branding PR

- Align README, EN/ID hero, HTML title, structured data, SEO, webmanifests and download copy.
- Correct stale RC notes. Latest **public prerelease v3.6.8-rc.5** includes merged indexed Event Journal/history PRs #167–#171; **draft IEC-101 transaction/FCB PR #145 is excluded**. Last non-prerelease is v3.6.7.
- Preserve published asset names `ARIEC60870-v3.6.8-rc.5-setup-win-x64.exe`, `ARIEC60870-v3.6.8-rc.5-win-x64.zip` and `SHA256SUMS.txt`, the executable `ARIEC60870.exe`, namespaces, user profiles, source links and existing Pages URL.
- Site source-of-truth is `site/`; corresponding `docs/` files are generated compatibility mirrors.

## Stage 2: following GitHub Settings → General → Repository name → arsee

1. Verify `github.com/masarray/arsee` and repo redirect; update local clone remotes with `git remote set-url origin https://github.com/masarray/arsee.git`.
2. In one follow-up commit replace repository URLs in badges, README, site, Docs, source metadata, installer publisher/support/update URLs, release workflows and GitHub settings. Preserve historical release tag and binary **filenames**; no release retagging.
3. Replace all absolute **Pages** URLs `https://masarray.github.io/ARIEC60870/` with `https://masarray.github.io/arsee/` in *all* `site/` HTML canonical/hreflang/OG/JSON-LD, sitemap, robots, SEO JSON, webmanifests (scope/start_url), llms/humans and documentation. Regenerate `docs/` with `scripts/sync-github-pages-docs-mirror.ps1`.
4. Keep Pages source at **GitHub Actions**. Verify live EN/ID pages, download/quick start/wiki, OG preview, favicon, manifest, sitemap, stylesheet and the **direct** installer/ZIP/checksum links on `/arsee/`. Check Github About description, homepage, topics and social preview.
5. Preserve old Pages visitors using a separate redirect from an account-level GitHub Pages site or a stable custom domain. A redirect file inside the renamed repository cannot answer requests to the old project URL.
6. Test external CI/actions integrations, update local remotes/search-engine properties and avoid reclaiming the old repository name (which can break GitHub redirects).

## Acceptance boundary

No physical SICAM/RTU/FAT or IEC-conformance certification is inferred from this rename. Do not touch or merge concurrent IEC-101 engine draft #145. RC .5 published assets and SHA256 values remain immutable.
