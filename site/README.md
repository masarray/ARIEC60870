# ARIEC60870 GitHub Pages site

This directory is the canonical source for the public GitHub Pages landing site.

The Pages workflow publishes this folder directly, so product/SEO changes should be made here first. Historical mirrors under `landing/` or `docs/` are retained only for compatibility with older links and repository browsing.

## Local preview

Open `index.html` in a browser, or serve the folder with any static web server:

```bash
python -m http.server 8080 --directory site
```

Then open `http://localhost:8080`.
