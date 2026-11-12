// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0
// Resolve end-user downloads from fully published GitHub Releases, never CI artifacts.
(() => {
  "use strict";
  const repo = "https://github.com/masarray/ARIEC60870";
  const fallbackTag = "v3.6.8";
    const api = "https://api.github.com/repos/masarray/ARIEC60870/releases?per_page=30";
  const validTag = /^v\d+\.\d+\.\d+(?:-rc\.\d+)?$/;

  function asset(release, name) {
    return (release.assets || []).find((a) =>
      a.name === name &&
      a.browser_download_url === repo + "/releases/download/" + release.tag_name + "/" + name &&
      a.size > (name === "SHA256SUMS.txt" ? 1 : name.endsWith("-sbom.spdx.json") ? 100 : 1000000));
  }

  function inspect(release) {
    if (!release || release.draft || !validTag.test(release.tag_name)) return null;
    const prefix = "ARIEC60870-" + release.tag_name;
    const zip = asset(release, prefix + "-win-x64.zip");
    const installer = asset(release, prefix + "-setup-win-x64.exe");
    const checksums = asset(release, "SHA256SUMS.txt");
    const sbom = asset(release, prefix + "-sbom.spdx.json");
    return zip && checksums && sbom
      ? { release, zip, installer, checksums, tag: release.tag_name }
      : null;
  }

  function replaceText(node, from, to) {
    if (!node) return;
    node.childNodes.forEach((child) => {
      if (child.nodeType === 3) child.nodeValue = child.nodeValue.replaceAll(from, to);
      else replaceText(child, from, to);
    });
  }

  function update(current) {
    const isId = document.documentElement.lang === "id";
    const oldAssets = repo + "/releases/download/" + fallbackTag + "/";
    const oldNotes = repo + "/releases/tag/" + fallbackTag;

    document.querySelectorAll("a[href]").forEach((link) => {
      const href = link.href;
      if (href.startsWith(oldAssets)) {
        const filename = href.slice(oldAssets.length);
        const matching = filename.endsWith("-setup-win-x64.exe") ? current.installer
          : filename.endsWith("-win-x64.zip") ? current.zip
          : filename === "SHA256SUMS.txt" ? current.checksums : null;
        if (matching) link.href = matching.browser_download_url;
      } else if (href === oldNotes) {
        link.href = current.release.html_url;
        replaceText(link, fallbackTag, current.tag);
      }
    });

    document.querySelectorAll("[data-release-version]").forEach((e) => {
      e.textContent = current.tag;
    });
    document.querySelectorAll("[data-release-filename]").forEach((e) => {
      e.textContent = e.dataset.releaseFilename === "installer"
        ? current.installer.name : current.zip.name;
    });
    document.querySelectorAll(".release-line .release-current").forEach((e) => {
      e.textContent = current.release.prerelease
        ? (isId ? "Prarilis publik terbaru" : "Latest public prerelease")
        : (isId ? "Rilis stabil terbaru" : "Latest stable release");
    });

    const meta = document.querySelector('meta[name="application-version"]');
    if (meta) meta.content = current.tag.slice(1);
    document.querySelectorAll('script[type="application/ld+json"]').forEach((node) => {
      try {
        const structured = JSON.parse(node.textContent);
        if (structured["@type"] !== "SoftwareApplication") return;
        structured.softwareVersion = current.tag.slice(1);
        structured.downloadUrl = current.zip.browser_download_url;
        node.textContent = JSON.stringify(structured);
      } catch (_) { /* Unrelated metadata is not release data. */ }
    });
  }

  async function refresh() {
    try {
      const response = await fetch(api, {
        headers: { Accept: "application/vnd.github+json" },
        cache: "no-store",
      });
      if (!response.ok) return;
      const raw = await response.json();
      if (!Array.isArray(raw)) return;
      const packages = raw.map(inspect).filter(Boolean).sort((a, b) =>
        Date.parse(b.release.published_at || 0) - Date.parse(a.release.published_at || 0));
      // One release must contain matching installer, ZIP, checksum and SBOM.
      // A half-uploaded stable tag must not replace an already working public RC.
      const current = packages.find((p) => p.installer);
      if (current) update(current);
    } catch (_) {
      // Offline/API rate-limited visitors retain verified, pre-published stable links.
    }
  }
  refresh();
})();
