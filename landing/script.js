// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0
(function () {
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
