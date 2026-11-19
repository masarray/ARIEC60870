# ArSee Engineering Handbook — single-source content contract

**Status:** implementation on Help Center PR #265, pending merge and user review.  
**Authoring principle:** explain standards with original words and evidence, never copy licensed IEC tables or entire clauses.

## Canonical source

- `site/learning/iec60870-handbook.json` is the **only editable manuscript** for educational IEC chapters.
- `site/wiki.html` and `site/id/wiki.html` use `site/learning/handbook-reader.js` to fetch that JSON and display the chosen English/Indonesian strings; `handbook-reader.css` supplies the responsive layout.
- The WPF project includes that *same* JSON through `Link="Learning/iec60870-handbook.json"`, `CopyToOutputDirectory="PreserveNewest"`. `HelpWindow.Handbook.cs` loads it from the application package, so education works **offline**.
- Existing contextual ArSee-specific F1 help remains authored in `HelpWindow.xaml.cs`; its keys (for example, `frame-trace` and `class-polling`) must not change. New educational keys are prefixed `learn-`, loaded before the field-help entries.
- `docs/wiki.html`, `docs/id/wiki.html` and `docs/learning/` are **generated compatibility mirrors**, not independent manuscripts. Regenerate them with `scripts/sync-github-pages-docs-mirror.ps1` after site edits. CI regression tests require exact equality.

## JSON contract v1

```text
schemaVersion: 1
id, updated, license, noticeEn, noticeId
chapters[]:
  key: unique, begins learn-
  number: unique two-digit chapter index
  categoryEn, categoryId, titleEn, titleId, summaryEn, summaryId
  searchText
  sources[]: public HTTPS catalogue / interoperability references
  sections[]:
    id, number: unique chapter-scoped subchapter anchors
    headingEn, headingId
    paragraphsEn[], paragraphsId[]
    bulletsEn[], bulletsId[]
    framesEn[], framesId[]
    lab (optional):
      wireKind: conceptual | iec101-fixed | iec101-single | iec104-u | iec104-s | iec104-i
      wireHex: uppercase spaced bytes or null for conceptual cases
      expectedValid: boolean
      expectedFields: decoder-check metadata; invalid cases include failure reason
      scenarioEn, scenarioId, stepsEn[], stepsId[]
      questionEn, questionId, answerEn, answerId
```

Both readers use the same ordering and section structure. The R3 handbook contains **16 bilingual chapters, 70 numbered educational sections and 20 guided trace labs** (14 with independently checked raw bytes, including deliberate invalid cases). New `number`, `id`, and `bookEdition` fields provide canonical section labels and stable deep-link anchors in the public Wiki. The WPF chapter tree derives numbering from the same ordered chapters, offers a compact IEC101/IEC103/IEC104 badge, and expands only the active chapter. The website uses DOM `textContent` rather than injecting HTML. WPF uses typed text and packaged JSON, not a network request. Keep bilingual sections aligned: every material statement in one language must have a faithful counterpart in the other.

## Scope and correctness checks

A useful learning chapter answers **what**, **why**, **normal request/response**, **what can fail**, and **what evidence proves it**. NUC and redundancy must clearly distinguish (1) IEC normative behavior, (2) companion-standard interoperability options, (3) published/selected Norwegian User Conventions, (4) vendor/project policy, and (5) ArSee implementation examples.

**Redundancy:** IEC 101 unbalanced A/B standby link-status probes must not be conflated with application Class 1/Class 2 polling. FCB, ACK and event continuity are link- and source-specific. IEC 104 frontend takeover has independent TCP/APCI sequence state and selected STARTDT ownership. GI after switchover is **profile/deployment policy**, not a single IEC-wide mandate.

**Commands:** distinguish link/TCP/APCI ACK from ACTCON/ACTTERM and the actual process-state feedback. Do not claim physical action on positive protocol confirmation alone.

**License:** IEC 60870 publications are copyrighted and may be used for authorized clause-level conformance review. The free handbook is an independently written engineering explanation: no complete reproduction of the IEC book, normative tables, figures, test case annexes or vendor manuals.

## R3 verified trace-lab teaching contract

Each example remains in the same canonical manuscript. The desktop builds a quiet WPF lesson panel with selectable raw bytes and a button to reveal/hide the explanation; the website renders an accessible disclosure with `aria-expanded` and `aria-controls`. Content is identical in English and Indonesian. For conceptual exercises, `wireHex=null` is intentional and **must not** be presented as a complete transmittable telegram.

`scripts/verify-handbook-wire-examples.py` independently checks all hexadecimal FT1.2 and IEC 104 I/S/U examples, actual declared lengths, control-reserved bits, one-octet link checksums, and the listed decoded metadata. It must reject deliberately malformed examples for the expected reason. This gate runs in Windows CI before protocol/xUnit tests. The lab checker is educational correctness evidence, **not** a conformance certificate for external RTUs.

- IEC101 E5 is context-dependent and has no native FC/address/ACD; do not infer secondary FC9 or ACK without a unique matching request. A2 is not a valid IEC101 short NACK under the selected companion procedure.
- IEC104 I/S/U control validity is not TCP packet validity. All state/sequence claims are per TCP association, and STARTDT success does not prove GI completion or field command feedback.
- IEC103 generic relay semantics require device interoperability data. An IEC103 link-status example proves the FT1.2 envelope, not FUN/INF, trip or breaker position.
- A/B redundancy cases distinguish observed active-owner data, standby probes and missing evidence across links. No example encourages automatically replaying uncertain physical controls.

## Validation and publishing

1. Edit only `site/learning/iec60870-handbook.json` for educational text; keep source links credible and both languages present.
2. Run the compatibility-mirror script for `docs/` after changing the web.
3. Run `SharedHandbookTests` and `HelpCenterLayoutTests`; the Windows CI performs WPF native screenshot acceptance and checks packaging.
4. Review the downloadable Windows preview at real 100%/125%/150% display scaling.
5. Merge only after review. The Pages workflow deploys **`site/`** on master; PR branches do not replace the live Wiki automatically.

## External reference baseline

- IEC 60870-5 series catalogue: https://webstore.iec.ch/en/publication/3755
- IEC 60870-5-101:2003+A1:2015: https://webstore.iec.ch/en/publication/23822
- IEC 60870-5-103:1997: https://webstore.iec.ch/en/publication/3745
- IEC 60870-5-104:2006+A1:2016: https://webstore.iec.ch/en/publication/25035
- Example deployment-specific IEC104 interoperability appendix: https://tekniskregelverk.banenor.no/siteassets/tekniske-spesifikasjoner/kontaktledning/dokumenter/fram-agreement-kl-rtu-appendix-interoperability-ice-60870-5-104.pdf

Avoid changing these to assert a new IEC edition without checking the authoritative IEC catalogue and the applicable project-controlled documents.
