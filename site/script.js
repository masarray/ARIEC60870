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

  const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  const kineticItems = document.querySelectorAll(".interactive-surface, .screen-frame, .button, .trust-strip span, .ticker-track span, .notice a");
  const revealItems = document.querySelectorAll(".reveal");

  if (reduceMotion || !("IntersectionObserver" in window)) {
    revealItems.forEach((item) => item.classList.add("is-visible"));
  } else {
    const revealObserver = new IntersectionObserver(
      (entries, observer) => {
        entries.forEach((entry) => {
          if (!entry.isIntersecting) {
            return;
          }

          entry.target.classList.add("is-visible");
          observer.unobserve(entry.target);
        });
      },
      {
        root: null,
        rootMargin: "0px 0px -12% 0px",
        threshold: 0.12,
      },
    );

    revealItems.forEach((item) => revealObserver.observe(item));
  }

  if (!reduceMotion) {
    kineticItems.forEach((item) => {
      item.classList.add("kinetic-ready");

      item.addEventListener("pointermove", (event) => {
        const rect = item.getBoundingClientRect();
        const x = event.clientX - rect.left;
        const y = event.clientY - rect.top;
        const px = x / rect.width;
        const py = y / rect.height;
        const tilt = item.classList.contains("screen-frame") ? 2.4 : 1.4;

        item.style.setProperty("--mx", `${px * 100}%`);
        item.style.setProperty("--my", `${py * 100}%`);
        item.style.setProperty("--rx", `${(0.5 - py) * tilt}deg`);
        item.style.setProperty("--ry", `${(px - 0.5) * tilt}deg`);
      });

      item.addEventListener("pointerleave", () => {
        item.style.removeProperty("--rx");
        item.style.removeProperty("--ry");
        item.style.removeProperty("--tx");
        item.style.removeProperty("--ty");
      });

      item.addEventListener("pointerdown", (event) => {
        item.classList.add("is-pressed");
        const rect = item.getBoundingClientRect();
        const ripple = document.createElement("span");
        ripple.className = "click-ripple";
        ripple.style.left = `${event.clientX - rect.left}px`;
        ripple.style.top = `${event.clientY - rect.top}px`;
        item.appendChild(ripple);
        ripple.addEventListener("animationend", () => ripple.remove(), { once: true });
      });

      item.addEventListener("pointerup", () => item.classList.remove("is-pressed"));
      item.addEventListener("pointercancel", () => item.classList.remove("is-pressed"));
    });
  }


  const faqCards = document.querySelectorAll(".faq-accordion-card");
  faqCards.forEach((card) => {
    const trigger = card.querySelector(".faq-accordion-trigger");
    const panel = card.querySelector(".faq-accordion-panel");
    if (!trigger || !panel) {
      return;
    }

    card.classList.remove("is-open");
    trigger.setAttribute("aria-expanded", "false");
    panel.setAttribute("aria-hidden", "true");

    trigger.addEventListener("click", () => {
      const willOpen = !card.classList.contains("is-open");
      const list = card.closest(".faq-accordion-list");
      const groupCards = list ? list.querySelectorAll(".faq-accordion-card") : faqCards;

      groupCards.forEach((otherCard) => {
        const otherTrigger = otherCard.querySelector(".faq-accordion-trigger");
        const otherPanel = otherCard.querySelector(".faq-accordion-panel");
        otherCard.classList.remove("is-open");
        otherTrigger?.setAttribute("aria-expanded", "false");
        otherPanel?.setAttribute("aria-hidden", "true");
      });

      if (willOpen) {
        card.classList.add("is-open");
        trigger.setAttribute("aria-expanded", "true");
        panel.setAttribute("aria-hidden", "false");
      }
    });
  });

  const lightbox = document.querySelector(".lightbox");
  const lightboxImage = lightbox?.querySelector("img");
  const lightboxCaption = lightbox?.querySelector("figcaption");
  const lightboxClose = lightbox?.querySelector(".lightbox-close");

  document.querySelectorAll(".zoom-trigger").forEach((trigger) => {
    trigger.addEventListener("click", () => {
      if (!lightbox || !lightboxImage || !lightboxCaption) {
        return;
      }

      lightboxImage.src = trigger.dataset.full || "";
      lightboxImage.alt = trigger.querySelector("img")?.alt || "";
      lightboxCaption.textContent = trigger.dataset.title || "Application screenshot";
      lightbox.showModal();
    });
  });

  lightboxClose?.addEventListener("click", () => lightbox?.close());
  lightbox?.addEventListener("click", (event) => {
    if (event.target === lightbox) {
      lightbox.close();
    }
  });
})();
