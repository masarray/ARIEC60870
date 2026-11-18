# Field evidence: SCADA database engineering ranges (18 November 2026)

**Status: REFERENCE ONLY — UNVERIFIED FOR ANY ARIEC60870 IOA.** This is a transcription and engineering interpretation of one operator-supplied handwritten SCADA database-range photograph, not a gateway export, authenticated device calibration, IEC telegram, or field FAT. The original image stays with the operator; **do not commit the photograph, letterhead, company details or station credentials to this public repository**.

Related sources: [31-point IEC-101 I/O list analysis](SCADA_IO_LIST_101_MAPPING_ANALYSIS_20261117.md), [engineering scaling contract](IEC10X_ENGINEERING_SCALING.md), [profile schema](MAPPING_PROFILE_SCHEMA.md), [field FAT issue #243](https://github.com/masarray/ARIEC60870/issues/243).

## 1. Faithful transcription of the visible range table

The photographed page is headed `scada.sdb` / `Data Sets`; it includes a handwritten `Data Item / Range` table. Range labels below reproduce what is legible, with spelling normalized only for readability.

| SCADA item as written | Raw Low | Raw High | Eng Low | Eng High | Evidence and caveat |
| --- | ---: | ---: | ---: | ---: | --- |
| Q — Diri Sendiri | -32768 | 32767 | -2078 | +2078 | Original circled ±1732 is **crossed out**; handwritten **±2078** is written above as the apparent correction |
| Q — Lab | -32768 | 32767 | -260 | +260 | Separate range from Q — Diri Sendiri |
| Tpi | -4 | 14 | -4 | 14 | Identity-range table entry |
| Poaq | 0 | 32767 | 0 | 1000 | Unipolar range |
| P | -2078 | +2078 | -2078 | +2078 | Identity-range table entry |

**What the photograph does *not* establish:** IEC Type ID, CA/IOA, physical unit (MW/MVar/tap), field device, station/version, dataset-to-IOA association, the meaning of SCADA's "Raw" in relation to ASDU bytes, which Q row belongs to each bay, live wire values, approved setpoint encoder or independent FAT. The superseded ±1732 text is retained above as a historical observation, not an active suggested calibration. Do not re-label all Q signals as ±2078 merely because the mnemonic matches.

## 2. Interpretation of the 120% engineering injection point (operator clarification, 18 November 2026)

The operator subsequently clarified the intended nominal base and test level: **2000 A**, **500 kV**, **120% injected/test scaling point**, giving approximately **2078**. For a balanced three-phase calculation with 500 kV **line-to-line** and 2000 A **line current**:

```text
S_nominal = sqrt(3) × U_LL × I / 1000
          = sqrt(3) × 500 kV × 2000 A / 1000
          = 1732.0508075688772 MVA

S_120%   = 1.20 × S_nominal
          = 2078.4609690826524 MVA
          ≈ 2078 (SCADA handwritten rounded engineering endpoint)

Equivalent current at 500 kV if applying 120% through current only:
I_120%   = 1.20 × 2000 A = 2400 A
```

This **explains why ±1732 was apparently superseded by ±2078** on the photographed range table. The earlier ±1732 corresponds to 100% nominal three-phase apparent power; the handwritten ±2078 corresponds to approximately **120% of that base**. It does **not** prove that a real system transmitted ±2078 MW or MVar, that primary equipment is rated for 120% continuous operation, nor that a specific bay's TI11 IOA uses this table. Actual **P** and **Q** are components of apparent power (in ideal balanced conditions, S² = P² + Q²). Their display envelopes may be configured independently for test purposes, but P and Q cannot both equal S magnitude simultaneously at a fixed S.

The word *injection* is treated as a reported SCADA/engineering test scaling value, **not** proof of a 500 kV physical primary injection. A simulated secondary-level test or database injection may use equivalent primary engineering values. Distinguish test range from live electrical operating limits and actual injected waveforms.

The operator clarification strengthens confidence in the **origin of the 2078 range**, **not the association to a particular CA/IOA**. Status remains reference-only, not an approval to overwrite default signal profiles.

## 3. Difference between SCADA database raw and decoded IEC values

The existing ArSee `TwoPoint` mode uses **decoded IEC source X**:

```text
Engineering Y = Y0 + (X - X0) * (Y100 - Y0) / (X100 - X0)
```

| Actual received IEC TI | Decoded X in ArSee | How the SCADA note can inform a mapping |
| --- | --- | --- |
| TI11/12/35 scaled measured value (SVA) | signed I16 counts (-32768..32767) | **Only if** the photographed SCADA raw domain corresponds to the same gateway TI11 counts, enter X0=-32768, X100=32767 and the *approved matching* Y endpoints. This remains provisional pending IOA identification |
| TI9/10/21/34 normalized measured value (NVA) | signed I16 / 32768, not the original raw count | For the documented Poaq range, the photo's raw 0..32767 corresponds to decoded 0..32767/32768; the existing ArSee profile uses conceptual 0..1 pu => 0..1000 |
| TI13/14/36 measured IEEE-754 short float | original floating-point value | The P raw=engineering identity is compatible with identity Factor **if** incoming float is already in the required engineering units; never convert float using TI11 counts assumptions |
| TI32 step position with CP56Time2a | step-position representation, not an analog NVA/SVA item | Tpi -4..14 is range metadata and **not** eligible for measured-analog TwoPoint conversion |

The source 31-point I/O PDF identifies **three TI11 Q** objects, IOA 790447 (FEEDER), 790439 (KOPEL), 790443 (TRAFO), and **three TI13 P** objects, IOA 790446, 790438, 790442. It **does not prove** those Q IOAs correspond to either Q row on this newly supplied SCADA page. It also identifies TI9 Poaq IOA 790449 as 0..1000 and TI32 tap IOA 790448 as **1..19**, which **conflicts** with this photograph's Tpi -4..14. These may reflect distinct systems/configurations; stop short of reconciling by guessing a tap offset.

**Practical policy:** no default, sample, operator profile, sync migration or command encoding is changed from this photograph alone. In particular preserve the default TI11 Q display as `counts` pending an authorized association and field measurement. The older *documentation-only* ±1732.0508 Q proposal is a nominal CT/PT hypothesis and **not evidence** that the ±2078 corrected SCADA row applies to that same gateway.

## 4. Conditional ArSee calibration entries (NOT an automatic profile)

These are **reference presets**, *not* a complete importable `ariec10x-ioa-profile-v1` and not approved for a specific point. Assign them only after checking CA + IOA + actual TI + exact SCADA data-item association.

| SCADA data item | IEC condition | ArSee mode | X0 | X100 | Y0 | Y100 | Unit |
| --- | --- | --- | ---: | ---: | ---: | ---: | --- |
| Q — Diri Sendiri | Same-source TI11 SVA signed I16 | TwoPoint | -32768 | 32767 | -2078 | 2078 | Confirm MVar |
| Q — Lab | Same-source TI11 SVA signed I16 | TwoPoint | -32768 | 32767 | -260 | 260 | Confirm MVar |
| Poaq | Actual TI9 NVA + corresponding database range | TwoPoint | 0 | 1 | 0 | 1000 | Confirm MW/other unit |
| P | TI13 float already in engineering units | Factor identity | — | — | — | — | Confirm MW |
| Tpi | TI32 step-position indication | no analog TwoPoint | — | — | — | — | Confirm tap positions |

For **Poaq**, the conceptual +1 NVA is not representable in signed I16: raw maximum 32767 decodes to **0.999969482421875** and the current X100=1 profile renders **999.969482421875** at that limit. An alternative, if the project explicitly requires the last representable count to display exactly 1000, is X100=32767/32768; this choice must be decided with a field comparator, not applied silently.

For the signed Q rows, **literal endpoint mapping** of -32768..32767 yields a tiny positive zero offset:

| Conditional Q range | Per raw count slope | Engineering output at raw=0 | at raw=13641 (illustration only) |
| --- | ---: | ---: | ---: |
| ±2078 | 4156/65535 ≈ 0.0634164950 | +0.03170825 | +865.0961 |
| ±260 | 520/65535 ≈ 0.00793469139 | +0.00396735 | +108.2411 |

A **zero-centered** symmetric calibration using X0=-32767 and X100=32767 gives 0 at raw 0, but **extrapolates** one count at raw -32768. Decide whether the SCADA database uses exact endpoints or zero-centered signed scaling by checking SCADA output at raw=0 and near both extremes. Neither fit should clamp data or rewrite QDS OV/IV.

No conversion should be inferred from a historical example of raw 13641 and a separate observed physical reference alone. Different Q range choices produce radically different values; this is why field-source association is a hard gate.

## 5. Review and acceptance workflow for Signal Map

1. Retain the original site-owned mapping profile and the photograph in a private FAT bundle, with configuration revision, source, SHA-256, approver and date; record any units absent from the note as **UNKNOWN**.
2. Obtain the SCADA `scada.sdb` export or RTU data-item mapping and verify the specific *device / station / CA / IOA / Type ID* behind "Q — Diri Sendiri" and "Q — Lab". Determine whether raw is *IEC signed integer* or a separate SCADA internal representation.
3. Inspect at least two independent points (prefer negative, zero and positive when available safely), using exact RX ASDU bytes, signed decoded X, measured SCADA Y, timestamps and QDS. Reject an incorrect endianness, signedness, double CT/PT scaling or stale value before interpreting the numerical difference.
4. In **Signal Map → Details → Scaling** select the approved measured object and explicitly configure **TwoPoint** X0/X100/Y0/Y100. For confirmed TI13 engineering float, use **Factor identity**. Preserve non-analog TI32 and any TI48 command policy and encoder untouched.
5. Use Validate and **Save As** a separately named *site-approved* JSON profile; avoid Sync overwriting user adjustments. In Value Viewer, Events/history, Frame Trace and CSV/report confirm identical calibrated display, with original HEX and quality unmodified.
6. Record PASS/FAIL/UNKNOWN/NOT TESTED per IOA. Synthetic unit tests, WPF screenshot tests or CI cannot satisfy the required gateway-specific FAT.

## 6. Candidate future enhancement (requires separate review)

Consider a **reference-range worksheet/import preview** showing `SCADA data item | raw min/max | eng min/max | IEC TI | CA/IOA | decoded source domain | unit | provenance | approval`. Require manual association and a dry-run diff before any profile write; show a blocking ambiguity when multiple Q candidates match; refuse TI32/TI48 analog conversion. This adds value without changing the existing one-authoritative TwoPoint engine or introducing a second scaling formula.

**Release boundary:** This note documents new field evidence; it does not attest any approved range, deploy a new default, qualify IEC-101/104 interoperability or authorize RTU control.
