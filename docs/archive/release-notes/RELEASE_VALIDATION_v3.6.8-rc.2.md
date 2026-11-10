# ARIEC60870 v3.6.8-rc.2 — Published public candidate / validation evidence

**Release type:** Public prerelease (`prerelease=true`, `draft=false`), Windows x64.  
**Published:** 2026-11-10 06:27:45 UTC.  
**Immutable tagged source commit:** `d33475c2d42287c8231d345198880ad55d5ad4b9`.  
**Branch/tag source:** `release/rc-3.6.8-rc.2` / `v3.6.8-rc.2`, both at exactly that SHA.  
**Published release:** https://github.com/masarray/ARIEC60870/releases/tag/v3.6.8-rc.2  
**Full package gate:** https://github.com/masarray/ARIEC60870/actions/runs/42768259871

This report is a **post-publication record** based on the exact GitHub Actions Package job and GitHub Release asset metadata. It does not alter or reissue the tagged RC.

## Exact release gate results

The Package run's Resolve, Build/verify/SBOM/attest, and Publish GitHub Release jobs each completed successfully. The Release build uses `-p:TreatWarningsAsErrors=true` and stamped version `3.6.8-rc.2`.

| Checked suite / stage | Observed evidence |
| --- | --- |
| Core xUnit | 93 passed, 0 failed, 0 skipped |
| Master xUnit | 206 passed, 0 failed, 0 skipped |
| Monitor xUnit | 129 passed, 0 failed, 0 skipped |
| Reporting xUnit | 5 passed, 0 failed, 0 skipped |
| Desktop xUnit | 396 passed, 0 failed, 0 skipped |
| Repository xUnit | 23 passed, 0 failed, 0 skipped |
| **Total xUnit** | **852 passed, 0 failed, 0 skipped** |
| Dependency-free protocol smoke | **19/19 passed** |
| Windows Release + portable ZIP | Passed; structure verified |
| Exact release EXE passive GUI smoke | Passed before packaging could proceed |
| Exact release EXE real-WPF observation acceptance | Passed; full report artifact below |
| RC Inno Setup installer | Compiled successfully |
| ZIP, installer, checksums and SPDX SBOM | Generated and uploaded to public release |
| Release attestation | ZIP, SBOM, checksums and installer separately attested |
| Public GitHub Release publication | Succeeded with **four** accessible assets |

**Frame Trace recurrence protection:** The new release gate parses the real Windows `metrics.json` and rejects a build unless the 10,000-frame synthetic WPF acceptance reports `HeldCollectionMutations=0`, source advancement while HOLD, realized newest AUTO tail, no rolling `CollectionChanged.Reset`, and expected latest source frame. It also requires 7+ real-WPF bitmap screenshots. This is source-backed UI regression evidence; it is **not** a physical display/DPI/FAT certificate.

Inspectable artifacts:

- [Exact release Windows/WPF screenshots and metrics](https://github.com/masarray/ARIEC60870/actions/runs/42768259871/artifacts/13740558202)
- [All six release-gate xUnit TRX files](https://github.com/masarray/ARIEC60870/actions/runs/42768259871/artifacts/13740453442)
- [Windows package workflow artifact](https://github.com/masarray/ARIEC60870/actions/runs/42768259871/artifacts/13740683162)

These Actions artifacts may require GitHub authentication and obey artifact retention. The four public GitHub Release assets are independent of these short-lived CI artifacts.

## Published asset fingerprints

All SHA256 values below came from the published GitHub Release asset `digest` fields and matched the package build's reported installer attestation subject. The public `SHA256SUMS.txt` was produced from exactly the four-file release staging area before upload.

| Asset | Bytes | Published SHA256 |
| --- | ---: | --- |
| `ARIEC60870-v3.6.8-rc.2-win-x64.zip` | 69,743,925 | `9ba43bb1168ab76b6ca3646ae1a6f343ab96ebecff7b36e5b1f9c8c6c021b77f` |
| `ARIEC60870-v3.6.8-rc.2-setup-win-x64.exe` | 70,261,137 | `bd8ae5b9eae47a9082b14710656419099d65229814cd5cd4d380218b18c3805e` |
| `ARIEC60870-v3.6.8-rc.2-sbom.spdx.json` | 4,712 | `29114c83093371ba94ca6dcb87d6262efcc21de79bc944d19e3fa3fd46446e53` |
| `SHA256SUMS.txt` | 315 | `6326e15fa9074ae7cc570dd908406916abad1060cb89521c633357a97ee2dc6d` |

Supply-chain proof:

- [Build attestation #65683124](https://github.com/masarray/ARIEC60870/attestations/65683124) — portable ZIP, SBOM, checksums
- [Installer attestation #65683133](https://github.com/masarray/ARIEC60870/attestations/65683133) — exact RC setup EXE digest above

`v3.6.8-rc.1` remains available at its original `a6ea99858db85a6b8c3bd1d59c309d771243e031` commit with its four assets; its tag/release were **not overwritten**.

## Installation/upgrade evidence and boundary

The precursor master source (after PR #137, SHA `9c799be7e6a70598012c7f80f244bfbdcf646655`) passed [Windows master CI #42764404878](https://github.com/masarray/ARIEC60870/actions/runs/42764404878), including its **bounded** v3.6.7-to-v3.6.8 Inno Setup upgrade acceptance. This tested a previously running GUI and simulated active Master session, graceful Restart Manager shutdown, preservation of a user-data sentinel, reinstallation, uninstall, and exact executable version/registry checks. The result supports installer design at that source baseline but is **not a stand-alone clean physical Windows rc.2 post-install FAT**. Run that separately.

## Honest field-readiness limits

- **Unmerged Draft PR #136 is excluded from this RC.** That development's stricter IEC-101 FC9/Table-10 response validation, DFC-busy recovery and 1200-bps serial timing changes have passing CI but await isolated physical SICAM A8000 versus IEC_Test A/B FAT. Do not claim those improvements were shipped in this tagged build.
- Actual SICAM 1200-bps long-soak, physical multi-COM 2/4 RX isolation, high-volume SOE FIFO conditions, GI/ACTTERM and all-vendor IEC-103/104 interoperability remain to be proven.
- HOLD freezes the **bounded 1,200-row viewport**, not source acquisition. Full historical evidence exists only in the separately configured/verified capture/history workflow; do not infer it from a visible grid counter.
- Built binaries are unsigned; testers should verify checksums and use isolated, authorized test systems only. Never issue control commands on live plant from a public RC.

Public testers should provide sanitized original TX/RX HEX, exact version/SHA, COM/UART/link-width profiles, expected/actual results, and device/simulator context through [repository issues](https://github.com/masarray/ARIEC60870/issues).
