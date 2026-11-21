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
      assert.equal(await video.getAttribute("autoplay"), null, "Unwanted autoplay");
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
      assert.equal(await gallery.count(), 4, "Four gallery screenshots expected");
      await gallery.first().click();
      const dialog = page.locator(".lightbox");
      assert(await dialog.evaluate(element => element.open), "Gallery modal did not open");
      assert(await page.locator(".lightbox-prev").isDisabled(), "Previous at first image should be disabled");
      await page.locator(".lightbox-next").click();
      assert((await page.locator(".lightbox figcaption").innerText()).includes("2 / 4"), "Gallery next failed");
      await page.keyboard.press("ArrowLeft");
      assert((await page.locator(".lightbox figcaption").innerText()).includes("1 / 4"), "Gallery ArrowLeft failed");
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

try {
  for (const [name, engine] of [["chromium", chromium], ["firefox", firefox]]) {
    const browser = await engine.launch({ headless: true });
    try {
      for (const width of widths) {
        for (const route of routes) await runCase(name, browser, width, route);
        console.log("Checked " + label + " / " + name + " / " + width + "px");
      }
      await fallbackCase(name, browser);
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
