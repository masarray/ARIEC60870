# IEC-101/104 SCADA I/O List — source analysis, bipolar engineering ranges and mapping provenance

**Source inspected:** operator-supplied one-page `IO List SCADA 101.pdf`, heading **I/O LIST PENGUJIAN SCADA**, page 1, rows 1–31 and parameter section B (17 November 2026). This project source is the authority for identifiers, type labels, LAB numbers, nominal CT/PT and the explicitly listed 0/1000 setting. It is **not** an RTU analog scaling/configuration database.

**Status:** engineering source shipped in public stable v3.6.14 after software CI,
real-WPF and independent Windows updater FAT. Physical RTU Q scaling remains **UNVERIFIED**. The generic bundled example contains only document-backed mapping; a separate clearly labeled docs-only opt-in **PROVISIONAL** JSON example illustrates one plausible, but unconfirmed, nominal CT/PT conversion. Neither profile modifies original ASDU bytes, IEC QDS, timestamps or a control-command write path.

## 1. Facts supplied by the PDF (page 1)

- 31 source rows, station CAASDU=105 for Main and Backup; link address / CA-ASDU / COT each 2 octets, IOA 3 octets, 1200 bps, 8E1. Public examples keep private network/interface topology redacted.
- Three active-power P analogs: FEEDER IOA 790446, KOPEL 790438, TRAFO 790442, **TI13 R32 IEEE short float**.
- Three reactive-power Q analogs: FEEDER IOA 790447, KOPEL 790439, TRAFO 790443, **TI11 I16 signed scaled value**.
- Each P/Q description: **CT 2000/1 A**, **PT 500/0.1 kV**. The PDF does **not** specify IEC TI11 integer-to-MVar factor, signed transmitted full-scale, SCADA engineering low/high, primary-voltage operating range, or measured calibration pairs.
- **TRAFO SOGI TM POAQ** at IOA 790449, TI9 normalized measured value, is explicitly described as **REAL POWER SETTING (0/1000)**. This supports engineering display endpoints 0 and 1000, assuming X is the decoded per-unit IEC NVA (not the signed 16-bit integer).
- **TRAFO SOGI RCA POOP** at IOA 70537, TI48 normalized **outbound command** also says 0/1000. It is not a measurement; **do not** apply a display TwoPoint calibration to the outbound command encoder based only on this label.
- Tap position TI32 at IOA 790448 says **(1/19)**. This is tap range metadata, not MW/MVar scaling.

## 2. Engineering interpretation (inference, not supplied RTU scaling)

The apparent-power magnitude of a balanced three-phase circuit at the **nominal primary ratings** in the table would be:

```text
CT ratio = 2000 A / 1 A = 2000
PT ratio = 500 kV / 0.1 kV = 5000
Nominal line voltage = 500 kV; current = 2000 A
S_nominal = sqrt(3) * U_LL[kV] * I[A] / 1000
          = sqrt(3) * 500 * 2000 / 1000
          = 1732.0508075688772 MVA
```

Under balanced sinusoidal conditions, P = S*cos(phi) and Q = S*sin(phi);
at this single nominal voltage/current point, **P^2 + Q^2 = S^2**.
A **candidate bipolar envelope** of about **-1732 to +1732 MW** for P and **-1732 to +1732 MVar** for Q is physically interpretable as a *component upper-bound magnitude at nominal CT/PT*, **not** a verified RTU/SCADA calibration, bay operating limit or command limit. P and Q cannot both independently equal +S at the same instant. This estimate must not be silently applied to source frames or alarm/quality truth.

**Critical:** IEC TI11 only specifies the signed I16 wire domain **-32768..32767**. Unlike NVA, it does *not* specify the gateway's engineering resolution. The correct X/Y full-scale for Q is **not uniquely determined** from CT/PT. Do not assert that a raw TI11 13641 must become 1730 MVar; one observed pair is insufficient to establish low and high endpoints.

## 3. Mapping mode and proposed X0/X100/Y0/Y100

| Source analog | Mapping policy | X0 | X100 | Y0 | Y100 | Confidence |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| TI11 Q, FEEDER/KOPEL/TRAFO, original bundled default through RC2 | **Factor identity / raw counts** | — | — | — | — | Safe, since actual RTU scale was not documented in first PDF |\n| TI11 Q, **next operator-directed reference candidate** | **TwoPoint Q Diri Sendiri (provisional)** | -32768 | +32767 | -2078 MVar | +2078 MVar | **Unverified Q row-to-IOA match**; user requested provisional value visible by default |
| TI11 Q, separate provisional candidate | **TwoPoint ± nominal hypothesis** | -32767 | +32767 | -1732.0508075688772 MVar | +1732.0508075688772 MVar | **NOT VERIFIED**; assumes symmetric full-scale signed SVA and theoretical CT/PT envelope |
| TI13 P, FEEDER/KOPEL/TRAFO | **Factor identity** (Scale=1, Ratio=1/1, Offset=0) | — | — | — | — | IEC R32 is real floating point; MW unit still must be checked against the gateway configuration |
| TI9 POAQ normalized, TI9/IOA790449 | **TwoPoint** | 0 per-unit | 1 per-unit (conceptual +FS) | 0 | 1000 | Source-supported positive engineering span; IEC NVA +1 is not exactly representable |
| Hypothetical **other** bipolar normalized analog (not TI9 POAQ) | **TwoPoint example only** | -1 per-unit | +1 per-unit | -1732 MW or MVar | +1732 MW or MVar | Not an assignment to any point in this PDF |
| TI48 POOP command, IOA70537 | **No display analog scaling/encoding change** | — | — | 0 metadata | 1000 metadata | The document names the setpoint range; command encoder is separately authorized |

For the **provisional** TI11 candidate above, zero raw maps exactly to zero engineering. Negative -32768 is one count below X0 and **extrapolates** (not silently clamped); QDS OV/IV flags must remain source-owned. A raw 13641 would be ~**721.058 MVar** under this hypothesis, **not 1730 MVar**: that difference demonstrates why the hypothesis must be checked against actual gateway scale before use.

The original bundled Q through RC2 was therefore labeled `counts`. **This statement describes the historical released profile, not the newer operator-directed source candidate**: that candidate now uses the SCADA photo's Q Diri Sendiri ±2078 reference provisionally on the three source Q IOAs (not ±260 and never approved without the actual RTU database). The separate manual-download documentation candidate `docs/profiles/PROVISIONAL_io_list_scada_101_ctpt_nominal.json` has all four signed Q endpoints for laboratory comparison, but is **not included by default in the Windows installer/ZIP or selected automatically**; an engineer must intentionally download and import it. The documented POAQ positive normalized 0..1000 is mapped in the bundled example; IEEE TI13 P remains unmultiplied.

## 4. Signal names directly from BAY TYPE + TYPE + MNEMO + LAB number

The source PDF's **TYPE** is the business signal category (`TM`, `TSD`, `TSS`, `RCD`, `RCA`, `TC`), **not** the IEC Type ID (`TI=9/11/13/30/31/...`). The final optional `#` is the **LAB** column number (1 or 2); a blank LAB **does not** receive a guessed number or a literal `#` character.

| PDF row | Signal Mapping display name | IEC TI | IOA |
| ---: | --- | ---: | ---: |
| 1 | TRAFO SOGI TC TPI | 32 | 790448 |
| 2 | FEEDER TM P | 13 | 790446 |
| 3 | FEEDER TM Q | 11 | 790447 |
| 4 | KOPEL TM P 1 | 13 | 790438 |
| 5 | KOPEL TM Q 1 | 11 | 790439 |
| 6 | TRAFO TM P 2 | 13 | 790442 |
| 7 | TRAFO TM Q 2 | 11 | 790443 |
| 8 | FEEDER RCD CB | 46 | 68542 |
| 9 | KOPEL RCD CB 1 | 46 | 68539 |
| 10 | TRAFO RCD CB 2 | 46 | 68550 |
| 11 | TRAFO SOGI RCD TC | 47 | 74537 |
| 12 | FEEDER TSD CB | 31 | 16712689 |
| 13 | KOPEL TSD CB 1 | 31 | 16712686 |
| 14 | TRAFO TSD CB 2 | 31 | 16712704 |
| 15 | FEEDER TSD LR | 31 | 16712694 |
| 16 | KOPEL TSD LR | 31 | 16712701 |
| 17 | TRAFO TSD LR | 31 | 16712708 |
| 18 | TRAFO SOGI TSD LRT | 31 | 16712709 |
| 19 | TRAFO SOGI TSD TCC | 31 | 16712710 |
| 20 | BUSBAR TSS VS | 30 | 8388754 |
| 21 | TRAFO SOGI TM POAQ | 9 | 790449 |
| 22 | TRAFO SOGI RCA POOP | 48 | 70537 |
| 23 | RTU TSS TSFT | 30 | 8388710 |
| 24 | RTU TSS RCFT | 30 | 8388711 |
| 25 | RTU TSS TMFT | 30 | 8388712 |
| 26 | RTU TSS ANFT | 30 | 8388713 |
| 27 | RTU/GATEWAY TSS L1FT | 30 | 8388714 |
| 28 | RTU/GATEWAY TSS L2FT | 30 | 8388715 |
| 29 | GATEWAY TSS MPU 1 | 30 | 8388716 |
| 30 | GATEWAY TSS MPU 2 | 30 | 8388717 |
| 31 | GATEWAY TSS IEDF | 30 | 8388725 |

The `Name` is the operator-facing display projection, backed by the unchanged CA/IOA/TI and the separately retained `BayType`, `TestCategory` and `Mnemonic` fields. Descriptive engineering meaning and source designation belong in `Description`/signal details; event/trace/search/CSV retains original source identifiers. A profile switch must reproject by exact source identity and must not mutate raw frames or command targets. Existing **user-owned** mapping profiles are not overwritten.

## 5. Field validation before accepting the provisional scaled Q profile

1. Obtain the RTU/gateway measurement-object database for each TI11 Q IOA, including engineering minimum/maximum, resolution/factor, CT/PT use, whether primary units already applied, and signed input definition.
2. Verify at least **two independent calibrated P/Q points** (prefer negative, zero and positive if the process allows), including signed reversal and values beyond the nominal span. Compare exact raw SVA, SCADA reference values and QDS OV/IV; record timestamps and physical source A/B.
3. Confirm IEEE TI13 P source units really are MW; do not multiply CT/PT ratios twice.
4. Confirm that POAQ 0..1000 is engineering MW and NVA source polarity/zero; retain TI48 controls within documented command protocol authority.
5. Replace provisional X/Y endpoints with project-approved values via **Signal List → Save As → Validate row → Validate profile → Save & Apply**. Preserve user profiles; do not apply this example to live outstations until validated.

Passing synthetic tests or GitHub CI is **not** 1200-bps physical SOE, SCADA conformance, or proof of actual CT/PT calibration.

## 6. Additional SCADA database range evidence (18 November 2026)

The original I/O list remains authoritative for **31 CA/IOA/TI identifiers**, but an operator supplied a separate handwritten SCADA `scada.sdb` engineering-range photograph. It shows Q — Diri Sendiri **raw -32768..32767 → eng -2078..+2078** with an older ±1732 correction crossed out, Q — Lab **-32768..32767 → -260..+260**, Poaq **0..32767 → 0..1000**, P **-2078..+2078 identity**, and Tpi **-4..14 identity**. The operator clarified 2000 A and 500 kV as the nominal base, and 2078 as an approximate **120% engineering injection/range point**: `sqrt(3) × 500 kV × 2000 A / 1000 × 1.20 = 2078.46 MVA`. This explains the crossed-out 1732 nominal estimate, but is **not** a specific TI11 counts-to-MVar calibration for an identified IOA.

The photo does **not** map either Q row to FEEDER/KOPEL/TRAFO, identify an IEC TI, or resolve the Tpi -4..14 vs source I/O list 1..19 discrepancy. The conditional engineering-range interpretation, IEC TI-specific decoded source domains, and field-approval gates are documented in [SCADA database engineering range evidence](SCADA_DB_ENGINEERING_RANGE_EVIDENCE_20261118.md). **Do not** replace the safe bundled TI11 `counts` default or user-owned profiles from this note alone; no command encoding is authorized.

## 7. Later correction to POOP versus POAQ scaling (18 November 2026)

The **original** I/O-list PDF and the handwritten SCADA photograph each state POAQ 0..1000; keep those statements above as factual transcriptions of their respective sources. A **later, explicit operator clarification** supersedes the current reference configuration: **POOP (TI48 command, CA105 IOA70537) 0..1000**, versus **POAQ (TI9 measured feedback, CA105 IOA790449) 0..1200**. For ArSee's *decoded normalized* POAQ feedback, the reference TwoPoint endpoints are `X0=0, X100=1, Y0=0, Y100=1200`, where +1 is the conceptual full-scale NVA boundary. The bundled reference profile and opt-in uncalibrated-identity Sync logic are updated to 1200; existing user-saved TwoPoint/Factor calibrations are preserved. The outbound POOP encoder is **not** modified; its command range metadata remains 0..1000.

**Evidence level:** newest operator statement for this project, **not** visually in either previous source document nor independently verified at RTU/SCADA FAT. See [range evidence chronology](SCADA_DB_ENGINEERING_RANGE_EVIDENCE_20261118.md). Do not interpret 1200 as a proved physical overload limit or as permission for command TX.

## 8. Failed RC2 Values screenshot and next candidate corrective profile (18 November 2026)

The operator's v3.6.17-rc.2 screenshots show old named Values with unscaled TI9 POAQ 0.999969482422 MW and Q 0 MVar, **while the editor separately shows a newly loaded clean reference file**. Exact MainWindow source inspection proves `LoadList_Click` changes the editor but not live `_ioaProfile` until `Save & Apply`; the previously loaded site profile from preferences remains active. The next branch explicitly separates these operations and displays the active-profile provenance. Plain Save no longer leads to automatic Apply on close. It also makes the user-requested provisional Q ±2078 TwoPoint *visible in a fresh bundled reference*, retains POAQ 1200 and POOP 1000, and revises tap range metadata to -4..14 without changing the source I/O list's TI32 or received TI5.

Unmapped values IOA 790349 TI13, 1790446 TI13 and 1790447 TI11 do not match the 31-point data-item list. They require real ASDU bytes and RTU source mapping, **not** an invented decimal-offset correction. A source-qualified diagnostic now surfaces each once per profile epoch, with little-endian IOA bytes. See [root-cause record](SCADA_RC2_VALUES_SCREENSHOT_REGRESSION_20261118.md) and [Issue #243](https://github.com/masarray/ARIEC60870/issues/243).
