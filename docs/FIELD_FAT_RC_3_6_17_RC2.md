# ArSee v3.6.17-rc.2 — field execution and evidence gate

**Status: OPEN / NOT TESTED** until independently witnessed RTU/SCADA evidence is supplied. This is an execution sheet for the **actually published** [v3.6.17-rc.2 release](https://github.com/masarray/ARIEC60870/releases/tag/v3.6.17-rc.2), not a software source test result, self-certified calibration, or permission to inject signals into a live utility system.

Exact candidate tag commit: `16e55eeee47756f5d584e0a4e843ef10c488aebf`. Software packaging provenance and checksums: [independent publication verification](archive/release-notes/RELEASE_VALIDATION_v3.6.17-rc.2.md). Older [3.6.16-rc.1 FAT sheet](FIELD_FAT_RC_3_6_16.md) is archival and must not be mistaken for the new executable.

## 0. Before connecting anything

1. Download from the exact [RC2 release](https://github.com/masarray/ARIEC60870/releases/tag/v3.6.17-rc.2), verify installer/ZIP SHA-256 against the archived release digests, record Windows/USB-COM adapter/driver details, ArSee build version, date and test supervisor. Prefer the portable ZIP for non-destructive observation; record installation/update path separately if testing the installer.
2. **Never put a second active IEC101 Master on the production SCADA line.** Use an authorized isolated RTU or physically isolated passive high-impedance taps; user operator must obtain site control permits. Two RX-only COM adapters are required for both directions of one serial line; four for physically independent A/B lines. Merely setting RX-only software does not ensure physical TX/RTS/DTR isolation.
3. Record verified link address (0/1/2 bytes and value), CA/IOA/COT widths, baud/parity/stop bits and handshake, exact RTU/gateway firmware/data-item database revision, and whether **Q Diri Sendiri** or **Q Lab** is associated with each observed CA/IOA/TI. A public sample CA=105, IOA and 1200 bps are not universal device defaults.
4. **Back up the original station-owned JSON profile.** Select a signal and choose `More → SCADA reference range for selected signal...` only after confirming the exact identity. Check the preview; use Ctrl+Z to verify non-destructive rollback. A staged unverified range requires **Save As a NEW JSON file**; original path reuse is refused. A profile copied into ArSee must not be assumed to be a certified calibration.
5. If no field evidence is available, do not fabricate data or mark tests PASS: retain actual results as `NOT TESTED` or `UNKNOWN`, raw TI11 Q as `counts`, and preserve original HEX/quality/TX identity in any export.

## 1. Field FAT matrix — record each observed status, not the planned outcome

| ID | Procedure / target | Minimum independent evidence to retain | Status |
| --- | --- | --- | --- |
| RC2-01 | Verify exact installer/portable SHA, startup version, 31-point Signal Map list and clean load | File hash, release SHA, desktop build/version screenshot, original JSON hash | NOT TESTED |
| RC2-02 | Signal Map: select CA105 TI11 Q IOA790447 / 790439 / 790443 individually, preview both candidate Q ranges | Correct selected CA/IOA/TI, ±2078 and ±260 options, no implicit Q row-to-IOA choice; screenshot + diff | NOT TESTED |
| RC2-03 | Apply one Q candidate, Undo, apply again, Save & Apply new copy, attempt old path | Only eight permitted scaling/engineering fields changed, full Undo restoration, old file hash unchanged; new copy hash | NOT TESTED |
| RC2-04 | POAQ CA105 IOA790449 TI9 receive-only scaling (latest operator correction) | Original RX HEX, decoded NVA fraction, QDS, observed SCADA reference, Y=1200×NVA expected, profile hash | NOT TESTED |
| RC2-05 | POOP CA105 IOA70537 TI48 metadata/feedback separation — **no command TX** | Confirm setpoint engineering range 0..1000, command policy/encoder not changed, POAQ remains separate 0..1200 | NOT TESTED |
| RC2-06 | Verify Q real signed mapping only after identifying SCADA dataset association | Reference negative/zero/positive (if available safely), signed TI11 counts, original QDS, independent engineering readings, configuration approval | NOT TESTED |
| RC2-07 | IEC101 passive/unbalanced FC10 and FC11 RequestFC in Event Log/CSV | Qualified TX request with matched secondary RX and physical line direction, Event FC and CSV RequestFC; FC11 Class1 fallback explained correctly | NOT TESTED |
| RC2-08 | Missing TX/other-source/wrong-service/IEC104 events | Event FC must show `—` when request correlation is not proven; preserve unmatched evidence, no fabricated Class attribution | NOT TESTED |
| RC2-09 | GI transaction start/response/completion, active Master only on authorized isolated bench | COT6 request, COT7 ACTCON, source-qualified COT20–36 objects, COT10 ACTTERM; expected versus observed unique IOA; no false completion | NOT TESTED |
| RC2-10 | Controlled 1200-bps SOE batch 60/500/2000 if device can sustain; skip when unsafe | Source injected count, RTU FIFO/overflow, exact on-wire valid count, ACD/DFC, event ledger count, original capture SHA/time; do not treat bytes as unique events | NOT TESTED |
| RC2-11 | Physical USB/COM start-stop-restart and A/B failover only on disconnected bench | Source/lifecycle epoch, no port deadlock/ghost controls, safe standby ownership, bounded timing; exact driver/firmware | NOT TESTED |
| RC2-12 | Values, Event Log/history, Frame Trace, CSV and offline replay parity | Same CA/IOA/TI/COT/QDS/HEX; scaled display agrees for *approved* profile; no raw bytes modified | NOT TESTED |
| RC2-13 | Windows 100/125/150% DPI and narrow viewport of actual installed/portable app | Real screenshot and keyboard/scroll accessibility, no clipping of Scaling, FC Event column or Save As prompt | NOT TESTED |

**PASS** requires independent operator test evidence. A skipped/unknown case stays **NOT TESTED/UNKNOWN**. Do not equate GitHub CI's synthetic Release/WPF tests with physical FAT or RTU SOE delivery.

## 2. Measurement reference points and expected values — NOT calibration approval

| Identity (source 31-point example) | Wire type | Operator reference | Example calculation / observation |
| --- | --- | --- | --- |
| TRAFO SOGI **POAQ** · CA105 IOA790449 | TI9 normalized inbound | Engineering **0..1200**, corrected 18 Nov | decoded NVA X=0.5 → **600** engineering units; X=32767/32768 → **1199.96337890625** |
| TRAFO SOGI **POOP** · CA105 IOA70537 | TI48 normalized *outbound command* | **0..1000** command metadata | Not a TI9 feedback scale. Do **not** apply 1200 to outbound encoding |
| FEEDER Q · IOA790447 | TI11 signed I16 | **Unknown**, Q Diri Sendiri ±2078 and Q Lab ±260 are *alternative reference definitions* | Need SCADA data-item → CA/IOA approval; no default MVar assignment |
| KOPEL Q · IOA790439 | TI11 signed I16 | **Unknown**, same alternatives | No automatic bulk selection |
| TRAFO Q · IOA790443 | TI11 signed I16 | **Unknown**, same alternatives | Keep `counts` until proven |
| FEEDER/KOPEL/TRAFO P · IOA790446/790438/790442 | TI13 IEEE float | Identity *if* source already expresses MW | No second CT/PT multiplier without device proof |
| TRAFO SOGI tap · IOA790448 | TI32 step | Older I/O list **1..19**, handwritten SCADA database **-4..14** | Treat conflict as **UNKNOWN**; do not invent offset or apply analog TwoPoint |

The corrected ±2078 Q range is consistent with a *nominal apparent power envelope at 120%* for three-phase 500 kV line-to-line × 2000 A: `sqrt(3) × 500 × 2000 / 1000 × 1.20 = 2078.46 MVA`. This is **not** evidence of actual primary injection, real P/Q, a continuously rated device limit, or of a specific TI11 SVA counts-to-MVar map.

**Signed endpoint caveat:** `X0=-32768, X100=32767, Y0=-2078, Y100=+2078` is linear but *not perfectly zero-centered*. At raw X=0, engineering Y≈+0.03170825; Q Lab ±260 similarly gives ≈+0.00396735. Do not silently replace X0 with -32767 or clamp output to hide the difference. Validate SCADA interpretation at zero and both extremes.

## 3. Evidence collection worksheet (one row per distinct physical observation)

Record this schema in a private project CSV or FAT sheet. **Do not upload customer network details, station configuration files or source captures to the public repo.** The existing ArSee `Signal Map → More → Export blank Field FAT worksheet...` provides a 31-point starting CSV whose result remains `NOT TESTED` until manually completed.

| Field | Required meaning |
| --- | --- |
| `ReleaseSHA / ExecutableSHA256 / ProfileSHA256` | Exact candidate, downloaded binary and selected profile provenance |
| `RTU database revision / independent comparator` | Approved SCADA/data-item version and reference measurement source |
| `Physical source / direction / frame offset` | Which A/B wire, RX/TX and precise original HEX evidence |
| `CA / IOA / TI / COT / QDS / OV / IV` | Original IEC identity and quality; no post-hoc mapping substitute |
| `DecodedSourceX / DisplayY / ReferenceY / Unit` | IEC decoded value, ArSee engineering output, independent engineering reading |
| `Active mode + X0/X100/Y0/Y100 or factor` | Exact engineering transform used; inactive fields ignored |
| `RequestFC / matched request ID / stage` | Only when source-matched IEC101 request–reply evidence exists |
| `Expected count / emitted count / received count` | For GI/SOE tests, separate RTU FIFO from real valid wire events |
| `Result / approver / evidence hash` | PASS, FAIL, UNKNOWN, NOT TESTED with engineer approval and original capture checksum |

Record separate histories for a reference and a verified project calibration. Matching one numerical sample does **not** establish a whole calibration range.

## 4. What remains after this RC

- Software package publication and automated WPF/xUnit/source gates were verified in [Package #44265585992](https://github.com/masarray/ARIEC60870/actions/runs/44265585992). **No physical observations** have been supplied here.
- [Issue #243](https://github.com/masarray/ARIEC60870/issues/243): close only after approved Q source–IOA mappings, signed scaling and independent measurements are proven; otherwise continue to present raw counts.
- [Draft #145](https://github.com/masarray/ARIEC60870/pull/145): separate IEC101 per-link transaction/FCB, GI/SBO and COM recovery work remains unmerged and requires a master rebase/reconciliation plus isolated physical FAT. Do not merge this draft solely to make the RC checklist green.
- [Issue #120](https://github.com/masarray/ARIEC60870/issues/120): decoder/Frame Trace protocol-truth release-gate audit remains a separately tracked engineering requirement.
- [Issue #159](https://github.com/masarray/ARIEC60870/issues/159): GitHub master protection/rulesets require authorized repository Settings changes; a green workflow cannot enforce an unconfigured branch protection policy.

**Next decision**: field engineer executes RC2 read-only verification first. Do not promote rc.2 to the stable channel as a substitute for actual independent RTU/SCADA evidence.
