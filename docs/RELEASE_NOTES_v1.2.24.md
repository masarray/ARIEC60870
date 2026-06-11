# ARIEC60870 v1.2.24 — UX Icon / Logo Rollback

## Fixed

- Rolled back the command rail from custom `ArIcon*` protocol geometries to the cleaner Lucide-style outline icon set.
- Restored the app and rail logo reference to the local `iec60870-icon.png` resource.
- Removed the latest visual regression where icons felt stiff, over-detailed, and less readable at small command-rail sizes.
- Kept the segmented navigation, compact header, status history, scrollbar, and polling-related UI behavior untouched.

## Design rule

Command rail icons must stay simple, monoline, and readable at 18–22 px. Protocol-specific art belongs in branding/illustration areas, not in small primary command buttons.
