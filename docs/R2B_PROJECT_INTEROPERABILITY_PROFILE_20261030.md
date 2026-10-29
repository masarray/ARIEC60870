# R2-B1 — Project profile Setup and capture provenance

1. Setup > IEC-101/104 > **Import profile**. JSON must follow schema v1 and use a project-owned Id beginning `project-`. Generic and historical convention selectors remain separate.
2. Select **Project custom** and inspect the inline Setup-to-profile difference preview (protocol, Link/COT/CA/IOA widths, link mode, GI policy). Import never edits the selected field widths or initiates TX.
3. Align configuration explicitly. **Connect fails closed** when the selected project policy and Setup disagree. IEC-101 balanced active transmission remains unsupported even if described in the profile.
4. **Export profile** creates a reusable JSON. Setup preferences persist the validated project JSON and the selector, independently of the imported file path.
5. Selected .ariec manifest records active-session declared profile ID, edition, fingerprint and source. Opening a selected capture retains that declaration for re-export without applying it to current Setup. Captures without this metadata remain readable; declarations are not digital signatures.

Safety: Passive Monitor stays RX-only and raw bytes/frames SHA-256 remain unchanged. This milestone does not implement binary-journal re-decode or full-capture parity; that requires a separate read-only byte comparison implementation and tests. Physical multi-vendor FAT remains outstanding.
