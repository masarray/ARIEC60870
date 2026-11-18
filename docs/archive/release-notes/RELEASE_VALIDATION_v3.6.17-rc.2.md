# ArSee v3.6.17-rc.2 — post-publication package verification (2026-11-18)

**Result: SOFTWARE PACKAGE PUBLISHED / exact-source package workflow SUCCESS.  
Physical IEC-101/104, utility SCADA calibration and 1200-bps SOE FAT: NOT TESTED by this report.**

This record is based on the GitHub Release API, immutable tag Git ref and the completed Windows Package workflow, not inferred from PR description or private test traffic. It is safe to publish as a release provenance record. It does not change the released bits.

## Immutable source and binary artifacts

- GitHub release: [v3.6.17-rc.2](https://github.com/masarray/ARIEC60870/releases/tag/v3.6.17-rc.2), marked **prerelease**; published **2026-11-18 08:20:56 UTC**
- Exact release tag `v3.6.17-rc.2` → commit `16e55eeee47756f5d584e0a4e843ef10c488aebf`
- Release branch: `release/rc-3.6.17-rc.2`; release source is two release-only commits ahead of base `master@7935b1b7dc097a4cbab6d65941bcbe4716ba0ed4` (source `Directory.Build.props` version 3.6.17 and archived RC2 notes). No field engine changes were added to those two release-specific commits.
- [Package Actions #44265585992](https://github.com/masarray/ARIEC60870/actions/runs/44265585992) at the same `16e55eeee47756f5d584e0a4e843ef10c488aebf`: **completed/success**. Resolve, Windows Build/Verify/SBOM/Attest and Release Publish jobs all finished **success**. Runtime GUI/WPF smoke, xUnit and EXE/package verification *steps* in that exact run report success.

GitHub Release assets and API-reported SHA-256 digests:

| Release asset | SHA-256 (hex) | Bytes |
| --- | --- | ---: |
| `ARIEC60870-v3.6.17-rc.2-setup-win-x64.exe` | `635db81a3e10b88b765c1bfcee30cebb2f3a8a9f764b0a39187d0671483963a6` | 70,056,300 |
| `ARIEC60870-v3.6.17-rc.2-win-x64.zip` | `671f8f43dee6a79e25c83cc57fc99d234642627fb87d4307d469afe40478bcc5` | 69,302,701 |
| `ARIEC60870-v3.6.17-rc.2-sbom.spdx.json` | `d134532a982e8c077ca5d854b3787354ce87d3d788bf577f317b0bec9208e6d6` | 4,715 |
| `SHA256SUMS.txt` | `e8d9423b9adf4c3f39f5a96d5c96c6576789c50dc03f7fdfaf8a14a6e5edcb17` | 318 |

Independent Windows operator-side verification **after download**:

```powershell
Get-FileHash -Algorithm SHA256 .\ARIEC60870-v3.6.17-rc.2-setup-win-x64.exe
Get-FileHash -Algorithm SHA256 .\ARIEC60870-v3.6.17-rc.2-win-x64.zip
```

Compare the results with the immutable published release digests above (or the corresponding entries inside `SHA256SUMS.txt`). The API-reported checksums are release artifact metadata, **not** a claim that the user's own copy was examined.

## What this software candidate adds relative to rc.1

- Merged [#256](https://github.com/masarray/ARIEC60870/pull/256): Event Log **observed FC10/FC11** request evidence, only for matched, qualified IEC101 exchanges; also CSV RequestFC. **FC11 does not establish the original Class for an individual signal.**
- Merged [#257](https://github.com/masarray/ARIEC60870/pull/257): operator-corrected **POAQ TI9 IOA790449 feedback 0..1200** but **POOP TI48 IOA70537 command 0..1000**; signed Q still uncalibrated by default.
- Merged [#258](https://github.com/masarray/ARIEC60870/pull/258): explicit per-point candidate Q (±2078 or ±260) or POAQ (0..1200) reference, with identity gate and atomic Undo, **never bulk Q assignment**.
- Merged [#259](https://github.com/masarray/ARIEC60870/pull/259): original profile save protection for staged reference ranges; Save / Save & Apply requires a distinct new JSON destination; existing approved mappings remain unchanged.

This candidate **excludes** the separate [draft IEC101 shared transaction/FCB core #145](https://github.com/masarray/ARIEC60870/pull/145). No blanket link-layer reliability, multiple-RTU interoperability, or GI/SOE completeness qualification may be borrowed from that unmerged work.

## Remaining acceptance gates and provenance

| Gate | Status at report time | Evidence needed to close |
| --- | --- | --- |
| Release tagged bytes, Package/portable EXE and WPF CI | **PASS — exact release source** | Package #44265585992 and release API digests above |
| Installed-updater runtime from existing installed stable | **NOT CLAIMED by RC2** | Separate genuine installed-updater FAT; stable controller requires it before promoting a new stable |
| POOP command 0..1000 vs POAQ feedback 0..1200 on a real RTU | **NOT TESTED** | Actual signed/normalized IEC bytes, SCADA rev, source-qualified independent feedback comparator; no unsolicited command TX |
| TI11 Q FEEDER/KOPEL/TRAFO selected range (Diri Sendiri ±2078 or Lab ±260) | **UNKNOWN** | Prove SCADA database data item → exact CA/IOA/TI and independent negative/zero/positive references; see issue #243 |
| Observed FC10/FC11 truthful across physical link sources | **NOT TESTED physically** | Independent directed capture with matching RX/TX/physical link identity; verify negative controls (unmatched, other protocols) |
| GI COT7/20/10, 1200-bps 60/500/2,000 SOE, COM reconnect/dual-link | **NOT TESTED** | Isolated permitted field FAT and measured loss/overflow/timing evidence |
| Independent third-party IEC101 master or IEC104 device interoperability | **NOT TESTED** | Reproducible paired peer evidence on matching physical/transport configuration |

See the [v3.6.17-rc.2 field execution sheet](../../FIELD_FAT_RC_3_6_17_RC2.md). Existing archived [v3.6.16-rc.1 sheet](../../FIELD_FAT_RC_3_6_16.md) remains historical and must **not** be used as an RC2 version label. Do not advertise stable/prod field readiness from this source-level and CI-only record.
