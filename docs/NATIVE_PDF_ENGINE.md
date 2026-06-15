# Native PDF Evidence Report Engine

ARIEC60870 generates PDF evidence reports with a small project-owned writer instead of a third-party PDF layout package. The goal is not to become a general-purpose PDF framework; the goal is to create stable, professional FAT/SAT evidence reports with a clean Apache-2.0 dependency story.

## Design goals

- **Clean-room ownership**: all report PDF generation code is project-owned and released under Apache-2.0.
- **Small surface area**: only the primitives required by ARIEC60870 are implemented.
- **Deterministic output**: generated directly by the application with no HTML conversion step and no runtime PDF generator package dependency.
- **Professional report layout**: report header, verdict card, executive summary, counters, setup details, evidence tables, acceptance notes, and page footer.
- **Field durability**: output should open in common PDF readers and keep readable text in the content stream for simple inspection.

## Implemented PDF primitives

The writer currently emits PDF 1.4 using:

- document catalog,
- pages tree,
- page objects,
- uncompressed content streams,
- cross-reference table,
- trailer and `startxref`,
- built-in Type 1 fonts: Helvetica, Helvetica-Bold, and Courier,
- vector rectangles, lines, and text operators.

This is intentionally simple. It avoids images, embedded fonts, JavaScript, annotations, encryption, incremental updates, and advanced tagged-PDF structures.

## Layout model

`EvidencePdfReportService` converts `EvidencePdfReportModel` into a paged report using a compact top-down layout engine. It handles:

- page creation,
- repeated header and footer,
- summary cards,
- key/value cards,
- evidence table headers,
- table row wrapping,
- section continuation across pages,
- footer page numbers and evidence hash.

The report model remains independent from the PDF writer, so the UI can keep using HTML for preview while export uses native PDF.

## Deliberate limitations

This engine is application-specific. It is not intended for arbitrary rich documents. Current limitations are acceptable for protocol evidence reports:

- text is sanitized to PDF built-in font compatible ASCII,
- rounded cards are represented by normal vector rectangles for maximum compatibility,
- no custom font embedding,
- no image embedding,
- no PDF/A conformance claim,
- no accessibility tagging claim.

Future improvements can add font embedding, PDF/A metadata, richer typography, or optional compression without changing the public `EvidencePdfReportService.Save(...)` contract.

## Regression checks

Tests verify that the report service writes a real `%PDF` file and that repository hygiene tests prevent accidental reintroduction of HTML-print report export wording or third-party PDF generator dependencies.
