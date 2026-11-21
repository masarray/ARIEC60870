// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0
(function () {
  // Shared accessible mobile menu; only enhanced when JS is available.
  const primaryNav = document.querySelector(".site-header .nav-links");
  const primaryHeader = primaryNav?.closest(".site-header");
  if (primaryNav && primaryHeader) {
    const indonesian = document.documentElement.lang === "id";
    const toggle = document.createElement("button");
    toggle.type = "button";
    toggle.className = "nav-toggle";
    toggle.textContent = "☰  Menu";
    toggle.setAttribute("aria-label", indonesian ? "Buka menu navigasi" : "Open navigation menu");
    toggle.setAttribute("aria-expanded", "false");
    primaryNav.id ||= "site-primary-navigation";
    toggle.setAttribute("aria-controls", primaryNav.id);
    primaryHeader.insertBefore(toggle, primaryNav);
    const close = () => {
      primaryNav.classList.remove("is-open");
      toggle.setAttribute("aria-expanded", "false");
      toggle.setAttribute("aria-label", indonesian ? "Buka menu navigasi" : "Open navigation menu");
    };
    toggle.addEventListener("click", () => {
      const opened = primaryNav.classList.toggle("is-open");
      toggle.setAttribute("aria-expanded", String(opened));
      toggle.setAttribute("aria-label", indonesian ? (opened ? "Tutup menu navigasi" : "Buka menu navigasi") : (opened ? "Close navigation menu" : "Open navigation menu"));
    });
    primaryNav.addEventListener("click", (event) => {
      if (event.target.closest("a")) close();
    });
    document.addEventListener("pointerdown", (event) => {
      if (!primaryHeader.contains(event.target)) close();
    });
    document.addEventListener("keydown", (event) => {
      if (event.key === "Escape" && primaryNav.classList.contains("is-open")) { close(); toggle.focus(); }
    });
    window.matchMedia("(max-width: 980px)").addEventListener?.("change", close);
    document.documentElement.classList.add("nav-enhanced");
  }

  // P3: Keep content visible with or without JavaScript. Avoid per-card motion tracking,
  // ripple nodes and entry animations; navigation, FAQ and gallery remain enhanced.

  // Accordion enhancement with deep links; static HTML remains readable without JavaScript.
  const faqCards = Array.from(document.querySelectorAll(".faq-accordion-card"));
  const setFaqOpen = (card, open) => {
    card.classList.toggle("is-open", open);
    const trigger = card.querySelector(".faq-accordion-trigger");
    const panel = card.querySelector(".faq-accordion-panel");
    trigger?.setAttribute("aria-expanded", String(open));
    panel?.setAttribute("aria-hidden", String(!open));
    if (panel) panel.inert = !open;
  };
  if (faqCards.length) {
    faqCards.forEach((card) => {
      const trigger = card.querySelector(".faq-accordion-trigger");
      const panel = card.querySelector(".faq-accordion-panel");
      if (!trigger || !panel) return;
      setFaqOpen(card, false);
      trigger.addEventListener("click", () => {
        const willOpen = !card.classList.contains("is-open");
        const group = card.closest(".faq-accordion-list");
        (group ? Array.from(group.querySelectorAll(".faq-accordion-card")) : faqCards)
          .forEach((other) => setFaqOpen(other, false));
        if (willOpen) setFaqOpen(card, true);
      });
    });
    document.documentElement.classList.add("faq-enhanced");
    const revealFaq = (id) => {
      const trigger = document.getElementById(id);
      if (!trigger?.classList.contains("faq-accordion-trigger")) return;
      const card = trigger.closest(".faq-accordion-card");
      if (!card) return;
      const group = card.closest(".faq-accordion-list");
      (group ? Array.from(group.querySelectorAll(".faq-accordion-card")) : faqCards)
        .forEach((other) => setFaqOpen(other, false));
      setFaqOpen(card, true);
      trigger.scrollIntoView({ block: "start" });
    };
    const revealFromHash = () => {
      const hash = window.location.hash.slice(1);
      if (!hash) return;
      try { revealFaq(decodeURIComponent(hash)); } catch (_) { /* Ignore malformed fragments. */ }
    };
    document.querySelectorAll(".faq-quicklinks a[href^='#']").forEach((link) => {
      link.addEventListener("click", () => revealFaq(link.getAttribute("href").slice(1)));
    });
    window.addEventListener("hashchange", revealFromHash);
    revealFromHash();
  }

  // User-controlled screenshot journey: arrows, bounds, and opener focus restoration.
  const lightbox = document.querySelector(".lightbox");
  const lightboxImage = lightbox?.querySelector("img");
  const lightboxCaption = lightbox?.querySelector("figcaption");
  const lightboxClose = lightbox?.querySelector(".lightbox-close");
  const lightboxPrev = lightbox?.querySelector(".lightbox-prev");
  const lightboxNext = lightbox?.querySelector(".lightbox-next");
  const screenshots = Array.from(document.querySelectorAll(".zoom-trigger"));
  let activeScreenshot = -1;
  let screenshotOpener = null;

  function showScreenshot(index) {
    if (!lightbox || !lightboxImage || !lightboxCaption || index < 0 || index >= screenshots.length) return;
    const trigger = screenshots[index];
    activeScreenshot = index;
    lightboxImage.src = trigger.dataset.full || "";
    lightboxImage.alt = trigger.querySelector("img")?.alt || "";
    lightboxCaption.textContent = String(index + 1) + " / " + String(screenshots.length) + " · " + (trigger.dataset.title || "Screenshot");
    if (lightboxPrev) lightboxPrev.disabled = index === 0;
    if (lightboxNext) lightboxNext.disabled = index === screenshots.length - 1;
    if (!lightbox.open) {
      screenshotOpener = trigger;
      lightbox.showModal();
    }
  }

  screenshots.forEach((trigger, index) => {
    trigger.addEventListener("click", () => showScreenshot(index));
  });

  lightboxPrev?.addEventListener("click", () => showScreenshot(activeScreenshot - 1));
  lightboxNext?.addEventListener("click", () => showScreenshot(activeScreenshot + 1));
  lightbox?.addEventListener("keydown", (event) => {
    if (event.key === "ArrowLeft" && activeScreenshot > 0) {
      event.preventDefault();
      showScreenshot(activeScreenshot - 1);
    }
    if (event.key === "ArrowRight" && activeScreenshot < screenshots.length - 1) {
      event.preventDefault();
      showScreenshot(activeScreenshot + 1);
    }
  });
  lightboxClose?.addEventListener("click", () => lightbox?.close());
  lightbox?.addEventListener("click", (event) => {
    if (event.target === lightbox) lightbox.close();
  });
  lightbox?.addEventListener("close", () => {
    lightboxImage?.removeAttribute("src");
    activeScreenshot = -1;
    screenshotOpener?.focus();
    screenshotOpener = null;
  });
  // Lightweight autoplay enhancement: the page remains usable with JS disabled.
  // Motion starts only when the hero is visible and the visitor has not asked
  // for reduced motion or data saving. Native controls always allow manual pause.
  const autoPreview = document.querySelector("video[data-auto-preview]");
  if (autoPreview && "IntersectionObserver" in window &&
      !window.matchMedia("(prefers-reduced-motion: reduce)").matches &&
      !navigator.connection?.saveData) {
    const observer = new IntersectionObserver((entries) => {
      if (!entries.some(entry => entry.isIntersecting && entry.intersectionRatio >= 0.35)) return;
      observer.disconnect();
      autoPreview.play().catch(() => { /* Native Play control is the fallback. */ });
    }, { threshold: 0.35 });
    observer.observe(autoPreview);
    document.addEventListener("visibilitychange", () => {
      if (document.hidden && !autoPreview.paused) autoPreview.pause();
    });
  }

})();
