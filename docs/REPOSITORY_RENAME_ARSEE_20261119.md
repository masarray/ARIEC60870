# ArSee repository-rename migration — 19 November 2026

## Identity and canonical links

This is a **repository rename, not a fork or source history reset**.
GitHub reported the same repository ID (`1266075932`) for both the
legacy `masarray/ARIEC60870` path and canonical
[masarray/arsee](https://github.com/masarray/arsee).
The default branch remains `master`, with historical pull requests,
issues, tags and release attachments retained.

| Purpose | Current address |
| --- | --- |
| Repository | https://github.com/masarray/arsee |
| Git remote | https://github.com/masarray/arsee.git |
| Public releases | https://github.com/masarray/arsee/releases |
| Latest stable updater API | https://api.github.com/repos/masarray/arsee/releases/latest |
| Release assets | https://github.com/masarray/arsee/releases/download/<tag>/<asset> |
| Official GitHub Pages project site | https://masarray.github.io/arsee/ |

The repository's GitHub homepage setting already points to the new Pages
path. GitHub repository redirects work for many ordinary old repository
links, but **GitHub Pages project-site URLs are not automatically redirected**
on rename. Pages source lives in `site/`, deployment uses GitHub Actions,
and the `docs/` mirror is retained for compatibility. After merging any
website change verify Pages is deployed successfully and inspect the live
new URL before calling migration complete.

## Security-critical runtime URL handling

`ReleaseUpdateService` uses an exact HTTPS origin and exact official
installer asset name/digest. The new canonical release API and origin are
`masarray/arsee`. Do **not** solve the rename by relaxing download URL
checks to an arbitrary host, fuzzy case matching, redirects, or unverified
asset names. Existing installer filenames, executable names, namespaces,
working-folder layout, project files and update handshake deliberately
remain `ARIEC60870` for binary compatibility; the product's public
name is **ArSee**.

The website resolver `site/release-download.js` (and generated
`docs/` mirror) compares `browser_download_url` with a trusted release
origin. Both the API and that origin must switch together. Only complete
published releases with official ZIP, installer, checksum and SBOM qualify.

**Legacy installed releases:** their compiled update client may still
require the former exact URL and can reject newly canonical GitHub release
asset URLs. Source changes do not retroactively patch installed binaries.
If the existing app cannot discover/install a newer stable release, the
operator should manually install a newer signed/verified-from-source
release from the canonical Releases page after saving station data and
checking SHA256. Never bypass asset verification. Do not replace running
engineering/SCADA sessions without operator approval.

## Maintainer workstations

After confirming the repository's new location and a clean local state,
update an existing clone with:

```bash
git remote set-url origin https://github.com/masarray/arsee.git
git remote -v
git fetch origin --prune --tags
```

Review the changed remote output before normal push/pull work. Avoid
force-push, recreating the repository, or deleting old releases/tags.
CI, page deployment, and release workflows must continue using the same
`master` source-traceable history.

## Verification gates

- Exact same GitHub repository ID for canonical and redirected legacy paths
- README, support links, About homepage, SEO canonicals, sitemap, robots,
  web manifests, English/Indonesian pages and docs compatibility mirror
- Release updater API and strict URL validation, including negative tests
  for wrong HTTPS origin and legacy origin
- Site JS uses new GitHub releases API and exact asset URLs
- Windows Release / xUnit / WPF CI, repository hygiene, CodeQL and
  dependency review pass
- GitHub Pages workflow succeeds on merged `site/` source; visit actual
  https://masarray.github.io/arsee/ after deployment
- Do not claim legacy updater auto-upgrade or old Pages URLs redirect
  without independently testing them

This migration changes repository and website links; it does **not**
assert IEC 60870 field interoperability, physical RTU FAT approval or a
new binary release.
