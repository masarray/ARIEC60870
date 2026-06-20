# Demo media policy

The public landing page uses `site/assets/screenshots/IEC-60870.webm`, a compressed WebM preview kept under the 750 KiB site-asset hygiene limit.

The higher quality animated demo files remain in `docs/assets/screenshots/` for README and documentation use:

- `docs/assets/screenshots/IEC-60870.gif` for README rendering.
- `docs/assets/screenshots/IEC-60870.webm` for documentation/demo source quality.

Do not commit large GIF/WebM files under `site/assets`. If the demo is refreshed, regenerate a lightweight `site/assets/screenshots/IEC-60870.webm` and keep it below the CI limit.
