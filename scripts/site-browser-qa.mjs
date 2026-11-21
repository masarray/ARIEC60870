// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0
// Headless acceptance for the real bilingual GitHub Pages site and the checked-out site/ preview.
// Run with: npm install --no-save --package-lock=false playwright@1.56.1
// Then SITE_ORIGIN=http://127.0.0.1:8765/ SITE_LABEL=preview node scripts/site-browser-qa.mjs
import assert from "node:assert/strict";
import { readFile, mkdir, writeFile } from "node:fs/promises";
import path from "node:path";
import { chromium, firefox } from "playwright";

const origin = new URL(process.env.SITE_ORIGIN || "https://masarray.github.io/arsee/");
const label = process.env.SITE_LABEL || "live";
const outDir = path.join("site-browser-evidence", label);
const version = JSON.parse(await readFile("site/seo-manifest.json", "utf8")).latestStableVersion;
const widths = [360, 390, 620, 768, 980, 1280, 1440];
const routes = ["index.html", "id/index.html", "demo.html", "id/demo.html", "wiki.html",
  "id/wiki.html", "faq.html", "id/faq.html", "download.html", "id/download.html"];
const results = [];
const errors = [];
await mkdir(outDir, { recursive: true });

async function runCase(browserName, browser, width, route) {
  const page = await browser.newPage({ viewport: { width, height: 900 }, reducedMotion: "reduce" });
  const url = new URL(route, origin).href;
  const entry = { label, browser: browserName, width, route, url, passed: false };
  results.push(entry);
  try {
    const response = await page.goto(url, { waitUntil: "domcontentloaded", timeout: 45000 });
    assert(response && response.status() < 400, "Page HTTP failure: " + (response?.status() ?? "no response"));
    assert(await page.locator("h1").first().isVisible(), "H1 not visible");
    const visual = await page.evaluate(() => {
      const heading = document.querySelector("h1").getBoundingClientRect();
      return {
        viewport: window.innerWidth,
        documentWidth: document.documentElement.scrollWidth,
        bodyWidth: document.body.scrollWidth,
        headingLeft: heading.left,
        headingRight: heading.right,
        hasCss: getComputedStyle(document.querySelector(".site-header")).position === "fixed",
        language: document.documentElement.lang,
      };
    });
    entry.visual = visual;
    assert(visual.hasCss, "Shared stylesheet did not load");
    const typography = await page.evaluate(async () => {
      const loadedFaces = await document.fonts.load('400 15px "Inter"');
      await document.fonts.ready;
      const body = getComputedStyle(document.body);
      // FontFace.family quoting differs between Chromium and Firefox; use the
      // actual FontFaceSet.load result, which requires a declared and loaded face.
      const loaded = loadedFaces.length > 0 && loadedFaces.every(face => face.status === "loaded");
      const faces = loadedFaces.map(face => ({ name: face.family, status: face.status }));
      const heading = getComputedStyle(document.querySelector("h1"));
      const sample = document.querySelector(".hero-copy .lead, .doc-hero p, main p");
      const color = getComputedStyle(sample).color;
      return { family: body.fontFamily, loaded, faces, h1Px: parseFloat(heading.fontSize), textColor: color };
    });
    assert(typography.family.startsWith('"Inter"') || typography.family.startsWith("Inter,"),
      "ArSee public pages must use the bundled Inter font: " + typography.family);
    assert(typography.loaded, "Self-hosted Inter font did not load on " + route + ": " + JSON.stringify(typography.faces));
    assert(typography.h1Px <= 37, "Oversized/bulky page headline: " + typography.h1Px);
    const rgb = typography.textColor.match(/[0-9.]+/g)?.slice(0, 3).map(Number);
    assert(rgb && rgb.length === 3, "Could not inspect body text contrast");
    const linear = rgb.map(n => {
      const c = n / 255;
      return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
    });
    const contrast = 1.05 / (0.2126 * linear[0] + 0.7152 * linear[1] + 0.0722 * linear[2] + 0.05);
    assert(contrast >= 4.5, "Text must meet readable WCAG AA contrast: " + contrast.toFixed(2));
    entry.typography = { ...typography, contrast: +contrast.toFixed(2) };

    assert(visual.documentWidth <= visual.viewport + 3, "Horizontal document overflow: " + JSON.stringify(visual));
    assert(visual.headingLeft >= -3 && visual.headingRight <= width + 3, "H1 clipped: " + JSON.stringify(visual));
    assert.equal(visual.language, route.startsWith("id/") ? "id" : "en", "Wrong language");
    const nav = page.locator(".site-header .nav-links");
    assert(await nav.count(), "Missing primary navigation");
    if (width <= 980) {
      const toggle = page.locator(".site-header .nav-toggle");
      assert(await toggle.isVisible(), "Mobile navigation toggle missing");
      assert.equal(await toggle.getAttribute("aria-expanded"), "false");
      if (route === "index.html" || route === "id/index.html") {
        await toggle.click();
        assert.equal(await toggle.getAttribute("aria-expanded"), "true", "Menu did not open");
        assert(await nav.isVisible(), "Open mobile menu not visible");
        await page.keyboard.press("Escape");
        assert.equal(await toggle.getAttribute("aria-expanded"), "false", "Menu Escape failed");
        assert.equal(await page.evaluate(() => document.activeElement?.classList.contains("nav-toggle")), true, "Escape did not restore menu focus");
      }
    } else {
      assert(await nav.isVisible(), "Desktop primary navigation invisible");
    }
    const switched = page.locator(".site-header .lang-switch").first();
    assert(await switched.count(), "Missing bilingual switch");
    const href = await switched.getAttribute("href");
    const targetPath = new URL(href, url).pathname.replace(/\/$/, "/index.html");
    const targetPage = route.startsWith("id/") ? route.slice(3) : "id/" + route;
    assert(targetPath.endsWith("/" + targetPage), "Language switch not matching page: " + href);
    if (route.endsWith("index.html")) {
      const release = await page.locator("body").innerText();
      assert(release.includes("v" + version), "Published stable release identity missing: " + version);
      const video = page.locator("video").first();
      assert.equal(await video.getAttribute("autoplay"), null, "Autoplay must be opt-in for JS progressive enhancement");
      assert(await video.getAttribute("data-auto-preview") !== null, "Lightweight preview hook missing");
      assert(await video.getAttribute("controls") !== null, "Native Play/Pause controls must remain available");
      assert.equal(await video.getAttribute("loop"), "", "Hero motion should use compact looping WebM");
      assert(await video.locator('source[type="video/webm"]').count(), "WebM video source missing");
      assert.equal(await page.locator("#capabilities .capability-item").count(), 8,
        "Both landing languages must expose the same eight complete feature families");
      assert.equal(await page.locator("#screens .zoom-trigger").count(), 11,
        "The product landing must show eleven distinct application screenshots");
      const trustCards = page.locator("#open-engineering .credibility-grid article");
      assert.equal(await trustCards.count(), 3, "Public evidence section needs three source-backed pillars");
      for (const card of await trustCards.all()) {
        const href = await card.locator("a").getAttribute("href");
        assert(href && (href.startsWith("https://github.com/masarray/arsee") || href === "wiki.html"),
          "Source evidence links must resolve to official ArSee locations: " + href);
      }
      const sectionIds = await page.locator("main > section[id]").evaluateAll(
        sections => sections.map(section => section.id));
      assert.deepEqual(sectionIds, ["product", "screens", "capabilities",
        "open-engineering", "wiki", "download"],
        "The product tour must flow from benefits through screenshots, features, trust and download");
      const hero = page.locator(".hero");
      assert.equal(await hero.locator(".metric-row").count(), 0, "Hero must not repeat three bulky metric tiles");
      assert.equal(await hero.locator(".trust-strip").count(), 0, "Hero must not duplicate source-backed benefit copy");
      assert.equal(await hero.locator(".preview-meta").count(), 1, "Video needs one understated preview caption");
      const galleryGrid = page.locator("#screens .screen-grid");
      if (width <= 620) {
        const mobileGallery = await galleryGrid.evaluate(element => ({
          overflow: getComputedStyle(element).overflowX,
          scrollWidth: element.scrollWidth,
          clientWidth: element.clientWidth,
          name: element.getAttribute("aria-label"),
          tabindex: element.getAttribute("tabindex"),
        }));
        assert(mobileGallery.scrollWidth > mobileGallery.clientWidth,
          "Mobile 11-view gallery must provide a real horizontal scrolling track");
        assert.equal(mobileGallery.overflow, "auto", "Swipe gallery must be native-scrollable");
        assert(mobileGallery.name && mobileGallery.tabindex === "0",
          "Swipe gallery must be named and keyboard focusable");
      } else {
        const expectedColumns = width <= 980 ? 2 : 3;
        const columns = await galleryGrid.evaluate(element =>
          getComputedStyle(element).gridTemplateColumns.split(" ").filter(Boolean).length);
        assert.equal(columns, expectedColumns,
          "Gallery should form consistent 2-column tablet and 3-column desktop layouts");
      }
    }
    if (width <= 620 && route.includes("wiki.html")) {
      for (const item of await page.locator(".reading-paths a").all()) {
        const height = await item.evaluate(element => element.getBoundingClientRect().height);
        assert(height >= 43, "Wiki shortcut touch target below 44px: " + height);
      }
    }
    if (width <= 620 && route.includes("faq.html")) {
      for (const item of await page.locator(".faq-quicklinks a").all()) {
        const height = await item.evaluate(element => element.getBoundingClientRect().height);
        assert(height >= 43, "FAQ shortcut touch target below 44px: " + height);
      }
    }
    if (width === 390 && (route === "index.html" || route === "id/index.html" ||
      route === "demo.html" || route === "id/demo.html")) {
      const gallery = page.locator(".zoom-trigger");
      const expectedGallerySize = route.endsWith("index.html") ? 11 : 4;
      assert.equal(await gallery.count(), expectedGallerySize,
        "The product home must expose 11 screenshots; the focused demo retains four");
      if (route.endsWith("index.html")) {
        assert(await page.locator(".gallery-swipe-hint").count(), "Mobile gallery affordance missing");
        const paths = await gallery.evaluateAll(elements => elements.map(element => element.dataset.full));
        assert.equal(new Set(paths).size, 11, "Duplicate application screenshots are not useful");
        for (const element of await gallery.all()) {
          const image = element.locator("img");
          assert.equal(await image.getAttribute("loading"), "lazy", "Screenshots must load lazily");
          assert(await image.getAttribute("alt"), "Screenshot requires descriptive alternative text");
        }
        const isPaused = await page.locator("video[data-auto-preview]").first().evaluate(video => video.paused);
        assert(isPaused, "Reduced-motion preference must not start autoplaying video");
      }
      await gallery.first().click();
      const dialog = page.locator(".lightbox");
      assert(await dialog.evaluate(element => element.open), "Gallery modal did not open");
      assert(await page.locator(".lightbox-prev").isDisabled(), "Previous at first image should be disabled");
      await page.locator(".lightbox-next").click();
      assert((await page.locator(".lightbox figcaption").innerText()).includes("2 / " + expectedGallerySize), "Gallery next failed");
      await page.keyboard.press("ArrowLeft");
      assert((await page.locator(".lightbox figcaption").innerText()).includes("1 / " + expectedGallerySize), "Gallery ArrowLeft failed");
      const imageLoaded = await page.locator(".lightbox img").evaluate(img =>
        img.decode().then(() => img.naturalWidth > 0).catch(() => false));
      assert(imageLoaded, "Expanded gallery image failed to load");
      await page.keyboard.press("Escape");
      assert.equal(await dialog.evaluate(element => element.open), false, "Escape did not close gallery");
      assert.equal(await gallery.first().evaluate(element => document.activeElement === element),
        true, "Gallery opener focus not restored");
    }
    if (width === 390 && route.endsWith("faq.html")) {
      const identifier = route.startsWith("id/") ? "faq-id-q1" : "faq-en-q1";
      await page.goto(url + "#" + identifier, { waitUntil: "domcontentloaded" });
      assert(await page.locator("#" + identifier).evaluate(element =>
        element.closest(".faq-accordion-card")?.classList.contains("is-open")), "FAQ deep link did not expand");
    }
    if (browserName === "chromium" && (width === 390 || width === 1280)) {
      const screenshot = browserName + "-" + width + "-" + route.replaceAll("/", "-") + ".png";
      await page.screenshot({ path: path.join(outDir, screenshot), fullPage: false });
      entry.screenshot = screenshot;
    }
    entry.passed = true;
  } catch (error) {
    entry.error = String(error?.message || error);
    errors.push(entry);
    console.error("FAIL " + browserName + " " + width + " " + route + " " + entry.error);
  } finally {
    await page.close();
  }
}

async function fallbackCase(browserName, browser) {
  const context = await browser.newContext({ viewport: { width: 390, height: 900 }, javaScriptEnabled: false });
  try {
    for (const route of ["faq.html", "id/faq.html"]) {
      const page = await context.newPage();
      const entry = { label, browser: browserName, width: 390, route, mode: "no-js", passed: false };
      results.push(entry);
      try {
        const response = await page.goto(new URL(route, origin).href, { waitUntil: "domcontentloaded" });
        assert(response && response.ok(), "No-JS FAQ not reachable");
        // GitHub Pages may complete DOMContentLoaded before Firefox finishes
        // applying the external stylesheet. Require the final nav to become
        // visible, not an instantaneous measurement in an unstable layout.
        // A genuinely collapsed no-JS menu still fails after the bounded wait.
        const staticNav = page.locator(".site-header .nav-links");
        try {
          await staticNav.waitFor({ state: "visible", timeout: 6000 });
        } catch {
          const detail = await page.evaluate(() => {
            const n = document.querySelector(".site-header .nav-links");
            const style = n ? getComputedStyle(n) : null;
            return { rootClass: document.documentElement.className,
              display: style?.display, visibility: style?.visibility,
              navFound: Boolean(n), stylesheets: [...document.styleSheets].length };
          });
          throw new Error("No-JS navigation must remain visible: " + JSON.stringify(detail));
        }
        assert(await page.locator(".faq-accordion-panel").first().isVisible(), "No-JS FAQ answers hidden");
        entry.passed = true;
      } catch (error) {
        entry.error = String(error?.message || error);
        errors.push(entry);
      } finally {
        await page.close();
      }
    }
  } finally {
    await context.close();
  }
}

async function releaseResolverCase(browserName, browser, route) {
  const page = await browser.newPage({ viewport: { width: 390, height: 900 } });
  const entry = { label, browser: browserName, width: 390, route, mode: "release-api", passed: false };
  const errorsOnPage = [];
  results.push(entry);
  page.on("pageerror", error => errorsOnPage.push(String(error)));
  const repoUrl = "https://github.com/masarray/arsee";
  function fakeRelease(tag, publishedAt, prerelease, complete = true) {
    const prefix = "ArSee-" + tag;
    const names = [prefix + "-setup-win-x64.exe", prefix + "-win-x64.zip",
      "SHA256SUMS.txt", prefix + "-sbom.spdx.json"];
    return {
      tag_name: tag, published_at: publishedAt, prerelease, draft: false,
      html_url: repoUrl + "/releases/tag/" + tag,
      assets: names.slice(0, complete ? 4 : 1).map(name => ({
        name,
        size: name === "SHA256SUMS.txt" ? 288 :
          name.endsWith("-sbom.spdx.json") ? 4500 : 70000000,
        browser_download_url: repoUrl + "/releases/download/" + tag + "/" + name,
      })),
    };
  }
  try {
    // The fake tags are TEST FIXTURES, not real releases. Newer incomplete
    // stable and newer prerelease must not displace the complete stable.
    const apiData = [
      fakeRelease("v9.9.11", "2026-11-24T00:00:00Z", false, false),
      fakeRelease("v9.9.10-rc.1", "2026-11-23T00:00:00Z", true),
      fakeRelease("v9.9.9", "2026-11-22T00:00:00Z", false),
    ];
    await page.route("https://api.github.com/repos/masarray/arsee/releases?per_page=30",
      request => request.fulfill({ status: 200, contentType: "application/json",
        headers: { "access-control-allow-origin": "*" },
        body: JSON.stringify(apiData) }));
    const response = await page.goto(new URL(route, origin).href,
      { waitUntil: "domcontentloaded", timeout: 45000 });
    assert(response && response.ok(), "Release resolver page not reachable");
    await page.waitForFunction(() =>
      document.querySelector("[data-release-version]")?.textContent === "v9.9.9",
      null, { timeout: 8000 });
    const prefix = repoUrl + "/releases/download/v9.9.9/ArSee-v9.9.9-";
    assert(await page.locator('a[href="' + prefix + 'setup-win-x64.exe"]').count(),
      "Resolver did not select complete stable installer");
    assert(await page.locator('a[href="' + prefix + 'win-x64.zip"]').count(),
      "Resolver did not select complete stable portable ZIP");
    assert.equal(await page.locator('[data-release-filename="installer"]').first().textContent(),
      "ArSee-v9.9.9-setup-win-x64.exe", "Installer filename did not follow API release");
    assert.equal(await page.locator(".release-line a").first().getAttribute("href"),
      repoUrl + "/releases/tag/v9.9.9", "Release notes link did not follow API release");
    assert.equal(errorsOnPage.length, 0, "Browser JS errors: " + errorsOnPage.join("; "));
    entry.passed = true;
  } catch (error) {
    entry.error = String(error?.message || error);
    errors.push(entry);
    console.error("FAIL " + browserName + " release resolver " + route + " " + entry.error);
  } finally {
    await page.close();
  }
}

try {
  for (const [name, engine] of [["chromium", chromium], ["firefox", firefox]]) {
    const browser = await engine.launch({ headless: true });
    try {
      for (const width of widths) {
        for (const route of routes) await runCase(name, browser, width, route);
        console.log("Checked " + label + " / " + name + " / " + width + "px");
      }
      await fallbackCase(name, browser);
      for (const route of ["download.html", "id/download.html"])
        await releaseResolverCase(name, browser, route);
    } finally {
      await browser.close();
    }
  }
} finally {
  await writeFile(path.join(outDir, "matrix.json"), JSON.stringify({
    label, origin: origin.href, version, checkedAt: new Date().toISOString(),
    passed: results.filter(test => test.passed).length, failed: errors.length, results,
  }, null, 2));
}
console.log(label + " browser acceptance: " + (results.length - errors.length) + "/" + results.length + " passed");
if (errors.length) process.exitCode = 1;
