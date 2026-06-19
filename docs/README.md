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
