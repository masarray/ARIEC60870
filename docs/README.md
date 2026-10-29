> **2026-10-29 current design authority:** [IEC 101/103/104 Interoperability PRD](IEC10X_INTEROPERABILITY_PRD_20261029.md), [Interoperability ARD](IEC10X_INTEROPERABILITY_ARD_20261029.md), and [NUC research](IEC10X_NUC_RESEARCH_20261029.md). This file retains historical implementation context; any older status, scope or convention assumption that conflicts with these design documents is superseded for **future requirements**. Merged master code and exact CI remain the authority for what is already implemented. Do not treat a new design target as completed capability.

# ARIEC60870 public site

This folder contains the public ARIEC60870 product site and field-learning pages served by GitHub Pages.

Core pages:

- `index.html` — English product landing page.
- `id/index.html` — Indonesian product landing page.
- `wiki.html` and `id/wiki.html` — IEC 60870 Field Wiki indexes.
- `iec101-*.html`, `iec103-*.html`, `iec104-*.html` — practical protocol learning pages.
- `faq.html` and `id/faq.html` — user-facing FAQ pages.

Local preview:

```bash
python -m http.server 8080 --directory site
```

Then open `http://localhost:8080`.
