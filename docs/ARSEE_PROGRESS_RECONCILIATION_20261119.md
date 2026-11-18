# ArSee — master, release and parallel-workstream reconciliation

**Audit snapshot:** 19 November 2026 (WIB); GitHub repository `masarray/arsee` (ID `1266075932`), default branch `master` at `aafa3ba140f698af4233b7e7012ca00e4a9ff280` **before this docs-only checkpoint**. The legacy `masarray/ARIEC60870` address redirects to the **same repository**, not a fork. Refresh these SHAs and open PRs before applying any later handoff: this record is intentionally time-bounded.

## 1. Verified merged work — do not reimplement as missing

| PR | Exact master merge | Result | Verification boundary |
| --- | --- | --- | --- |
| [#260](https://github.com/masarray/arsee/pull/260) | `e40141c087ea5a87b72cfa479bad8aff17b5e44f` | Exact RC.2 release/13-case field FAT handoff | Documentation and release provenance; **not physical FAT** |
| [#262](https://github.com/masarray/arsee/pull/262) | `5503f1ac8fefe177775217a2f800dd32a2bcab92` | Fail-closed trusted IEC engineering Values projection, no guessed unmapped IOA aliases, active-map reference counter | Source/CI proof, not actual operator screenshot replay |
| [#263](https://github.com/masarray/arsee/pull/263) | `b13f47117762f8520e9bf8eefaf54b6daa222101` | Operator's Feeder/Lab SOGI3/Lab SOGI4 Q TwoPoint defaults and guarded Sync | 1,249 xUnit, 19/19 protocol smoke and WPF acceptance in source PR; field calibration unproven |
| [#264](https://github.com/masarray/arsee/pull/264) | `ee9b0d2b5b33499b7bd9af3dc4043973e1c4bb01` | Canonical `masarray/arsee` API origin, updater allowlist, site download resolver and Pages references | Correct current source, not retroactive modification of installed binaries |
| [#266](https://github.com/masarray/arsee/pull/266) | `aafa3ba140f698af4233b7e7012ca00e4a9ff280` | Installer publisher/support/update URLs, SBOM origin, Help Field Wiki, README/root canonical URL | All PR gates green including native Windows updater FAT; no new public binary published by docs/merge alone |

Full merged history for the SCADA editor also includes #245, #248, #250–#253, #255–#259. No old unmerged PR should overwrite their WPF layout or source-qualified scaling rules.

## 2. Source/release distinction — essential after rename

- **Published stable**: [v3.6.16](https://github.com/masarray/arsee/releases/tag/v3.6.16), with actual installer/ZIP/SBOM/checksum attachments.
- **Most recent published RC**: [v3.6.17-rc.4](https://github.com/masarray/arsee/releases/tag/v3.6.17-rc.4), tag source `1447572ec9c3722ddbe97ab39bfe6d6c41e8c6fe`; package run [#44379465069](https://github.com/masarray/arsee/actions/runs/44379465069). It includes the Feeder/Lab Q default mapping from #263.
- **Critical boundary:** RC.4's immutable tag **precedes both** #264 and #266. Those canonical URL/updater/installer fixes are merged **source** but are **not included in previously published RC.4 executable/installer bytes**. An old installed updater may need a separately verified manual upgrade; do not loosen exact-origin security to compensate or claim its old binary already knows the renamed repository.
- Public-site source `site/seo-manifest.json` still advertises **v3.6.15** as its static fallback, even though actual latest stable is v3.6.16. Dynamic release download code uses the canonical API, but a static fallback mismatch remains to be reconciled in an independent site+docs mirror+SEO PR. The live renamed Pages URL was **not independently browser-verified in this audit**.
- `master` GitHub branch metadata reported `protected=false`. [Issue #159](https://github.com/masarray/arsee/issues/159) remains open; green CI alone does not create branch-protection rulesets.

## 3. Operator Q identity now resolved in bundled 31-point reference

| Bay | Source item | CA / TI / IOA | Signed decoded source X | TwoPoint engineering Y |
| --- | --- | --- | --- | --- |
| **Feeder** | **Q Diri Sendiri** | 105 / TI11 / 790447 | -32768..32767 | **-2078..+2078 MVar** |
| **Kopel — Lab SOGI 3** | **Q Lab** | 105 / TI11 / 790439 | -32768..32767 | **-260..+260 MVar** |
| **Trafo — Lab SOGI 4** | **Q Lab** | 105 / TI11 / 790443 | -32768..32767 | **-260..+260 MVar** |

The source association came from the operator's subsequent bay clarification, while engineering endpoints came from the SCADA photo. Keep literal asymmetrical raw endpoints (thus a small positive value at raw zero); do **not** secretly clamp zero or infer a second CT/PT multiplier. Independent RTU/SCADA raw-domain equality remains **NOT FIELD TESTED**, tracked in [#243](https://github.com/masarray/arsee/issues/243).

**POAQ** remains incoming TI9 CA105 IOA790449 normalized feedback **0..1200**; **POOP** remains outbound TI48 CA105 IOA70537 command metadata **0..1000**. Do not conflate those signals or modify command encoding to solve a display issue. Three screenshot-only extra IOAs (790349, 1790446, 1790447) must preserve the original RX identity until raw HEX + station/simulator database explains them. TI5 tap at -4 versus prior TI32 and 1..19 profile metadata remains a separate, unproven source conflict.

## 4. Live open PRs: exact reconciliation and non-destructive decisions

### [PR #261 — Signal Map](https://github.com/masarray/arsee/pull/261)

As of snapshot: **open, diverged 30 ahead / 4 behind `master`, mergeable=false**. **Do not merge or bulk cherry-pick.** Its older blanket Q ±2078 default and text saying data-item-to-IOA association unknown are **superseded by #263**. Substantial portions of mapped/unmapped Values are already covered by #262.

**Not yet integrated** and worth selectively porting to a new PR based on fresh master:
1. **Runtime profile ownership:** Signal Map `Load` and plain `Save` should not implicitly activate in live Values; only explicit `Save & Apply` should do so. Preserve original station JSON and make clean existing-file Apply possible without rewriting. Use true `_activeMappingProfileFilePath`, never redacted display text, and test close/discard, dirty flag, restart and edit protection.
2. **IEEE float display precision:** consider proper 7-significant-digit UI presentation for TI13 R32 when `-1.207000017` is only representation noise; preserve raw bits, quality and frame forensic decimal. Include extremes, negative, near-zero and scientific notation cases.
3. **Diagnostics/provenance:** include only genuinely additional source-qualified RX little-endian IOA and active-profile provenance, after diffing #262 and checking any no-tooltip live-evidence UX rules.

Require full Windows Release build, tests, packaged WPF and negative regression. Do not silently overwrite custom Factor/TwoPoint or any of the three operator-corrected Q bay ranges. Reconciliation comment was posted on PR #261.

### [PR #265 — Help Center](https://github.com/masarray/arsee/pull/265)

**Open draft, diverged 16 ahead / 2 behind**. Owns the layout redesign, native WPF screenshot/acceptance runner and shared design tokens. Automated CI/CodeQL is green for its current head but manual EN/ID and **actual OS DPI 100/125/150%** visual verification remains required by its own contract. Its HelpWindow file overlaps #266; the corrected `https://masarray.github.io/arsee/{path}` online Wiki URL must survive integration. Reconciliation comment posted to PR #265. Do not merge for mere cosmetic checkmark or replace its work from another parallel thread.

### [PR #145 — historical IEC-101 P0 core](https://github.com/masarray/arsee/pull/145)

**Open draft, diverged 43 ahead / 117 behind**. Much reliability work has separately landed on later master through stages 1–7, but this does not prove all branch code is redundant. Do not merge or force-rebase the huge historical branch. Reconcile remaining IEC-101 differences by *wire evidence, requirement and current master tests*, especially 1200-bps SOE 60/500/2000, FC11 late reply, physical COM A/B reconnect, GI and SBO/ACTCON/ACTTERM/feedback. Keep TX safety, one authoritative transaction core and source-qualified evidence.

## 5. Unclosed field/reliability gates

- [#243](https://github.com/masarray/arsee/issues/243): independent Q calibration readings and SCADA/RTU raw-domain proof, per exact CA/IOA/TI, original QDS and source HEX; source mapping is resolved **but physical calibration is not**.
- [#120](https://github.com/masarray/arsee/issues/120): decoder truth and Frame Trace parity across IEC-101/103/104.
- [#159](https://github.com/masarray/arsee/issues/159): authorized master protection/rulesets; requires GitHub repository settings owner permission.
- [#68](https://github.com/masarray/arsee/issues/68), [#70](https://github.com/masarray/arsee/issues/70): UI stability and FT1.2 resync flakiness remain tracked, no closure is inferred here.
- Old source code names, Windows executable `ARIEC60870.exe` and published binary asset names remain intentionally stable for compatibility. This is *not* an unfinished GitHub rename.

## 6. Next implementation order / handoff

1. **P0 — salvage PR #261's unique live Apply state machine and float display**, in a fresh exact-master branch with focused tests; do not import old Q/reference defaults. Observe actual same-session Values behavior after Save/Apply.
2. **P0 — independent capture-based field FAT** for operator Q/POAQ and unmapped IOAs; source and comparator must be separately documented. Never inject physical controls without authorized isolation.
3. **P1 — review PR #265 native WPF images**, preserve its current design and #266 Wiki URL; merge only after visual QA, conflict reconciliation and exact-head CI.
4. **P1 — IEC-101/103/104 field reliability verification**, narrow missing requirements instead of merging stale PR #145.
5. **P1 — reconcile public-site stable fallback** (v3.6.15 → latest *verified* stable v3.6.16), regenerate docs mirror and verify Pages deployment/new live URL; do not promote RC.4 to stable.
6. **P2 — enforce branch protection** per #159 with authorized owner configuration.

**Acceptance reporting rule:** distinguish **Merged source**, **automated-tested**, **published binary**, **installed runtime**, and **independent physical FAT**. They are five different statuses. This documentation reconciles progress; it changes no decoder, TX command, station calibration, installer bytes or existing releases.
