# ARIEC60870 User Guide

This guide is written for engineers who want to run the Windows application without reading the source code first.

## What ARIEC60870 is for

ARIEC60870 helps you run a focused IEC 60870 communication check and keep readable evidence from the session.

Use it for:

- IEC 60870-5-101 serial RTU/gateway checks.
- IEC 60870-5-103 serial protection relay checks.
- IEC 60870-5-104 TCP/IP endpoint checks.
- General Interrogation evidence.
- Value, event, diagnostic, and raw frame review.
- Professional PDF evidence reports for FAT/SAT notes, troubleshooting records, and handover.

ARIEC60870 is not a SCADA server, not a redundant master station, and not a replacement for an approved project FAT/SAT procedure.

## Download the application

1. Open the latest release page: <https://github.com/masarray/ARIEC60870/releases/latest>
2. Download the Windows ZIP asset named like this:

   ```text
   ARIEC60870-vX.Y.Z-win-x64.zip
   ```

3. Extract the ZIP to a normal local folder, for example:

   ```text
   C:\Tools\ARIEC60870\
   ```

4. Open the extracted folder and double-click:

   ```text
   ARIEC60870.exe
   ```

The user release package is intentionally simple. There is one desktop executable and no start batch file.

## Windows first-run note

If Windows SmartScreen warns about an unknown publisher, review the file source and checksum, then choose the Windows option that allows a trusted local run. Public open-source releases may show this warning until the executable is code-signed.

## Connect to an IEC-104 device

Use this mode when the device is reachable over TCP/IP.

1. Open **Setup**.
2. Select **IEC-104 TCP/IP**.
3. Enter the server IP address or hostname.
4. Enter the TCP port. The standard IEC-104 port is usually `2404`.
5. Set the **Common Address** and ASDU field sizes according to the device interoperability/profile document.
6. Enable **General Interrogation** when you need a startup data snapshot.
7. Click **Start**.
8. Confirm that STARTDT, I-frame activity, GI response, values, events, and diagnostics appear in the workspaces.

## Connect to an IEC-101 serial device

Use this mode for serial RTU/gateway telecontrol communication.

1. Connect the serial adapter to the test link.
2. Confirm the COM port in Windows Device Manager.
3. Open **Setup**.
4. Select **IEC-101 serial**.
5. Set COM port, baud rate, parity, data bits, stop bits, link address, common address, COT size, CA size, and IOA size from the project/device profile.
6. Keep **Reset FCB** enabled for a normal first test unless the project procedure says otherwise.
7. Enable **General Interrogation** when a startup snapshot is needed.
8. Click **Start**.
9. Review link reset, Class 2 polling, ACD/DFC status, Class 1 drain, GI sequence, and decoded values/events.

## Connect to an IEC-103 serial relay

Use this mode for protection relay serial communication.

1. Connect the serial adapter to the relay communication port.
2. Confirm COM port, baud rate, parity, stop bits, and link address.
3. Open **Setup**.
4. Select **IEC-103 serial**.
5. Set the relay link address and polling/timing values.
6. Load a user-owned mapping profile if you want FUN/INF values shown with readable signal names.
7. Click **Start**.
8. Review Class 2 polling, Class 1 event drain, GI evidence, FUN/INF rows, timestamps, and diagnostics.

## Load a mapping profile

Mapping profiles turn protocol addresses into readable signal names.

Use a mapping profile when:

- FAT/SAT evidence must match the approved signal list.
- IOA, FUN, or INF numbers need project-specific labels.
- Reports will be reviewed by people who do not want raw protocol numbers only.

Keep mapping files user-owned and project-approved. Do not publish project/customer mapping files publicly.

## Review evidence during a session

Start with high-level evidence, then drill down when needed:

1. **Operator Evidence** — what happened in readable rows.
2. **Value Viewer** — latest decoded values.
3. **Event Log** — event and state-change records.
4. **Diagnostics / Findings** — warnings, errors, and recommended checks.
5. **Frame Trace** — raw TX/RX evidence for protocol-level review.
6. **Report** — final evidence scope and PDF export.

## Export a professional PDF report

1. Run a session or open a saved capture.
2. Open the **Report** workspace.
3. Click **Refresh** if the preview is not up to date.
4. Review the evidence scope.
5. Click **Export PDF**.
6. Choose the target file name and folder.
7. Open the PDF and review it before sharing.

The PDF is generated directly by the built-in native PDF engine. No browser print workflow is required.

## Before sharing a PDF outside the project team

Check the exported file for:

- station or project names;
- IP addresses, COM ports, and communication settings;
- device names and mapping labels;
- raw protocol frames;
- customer-sensitive evidence.

Sanitize the session or use demo data before sharing screenshots publicly.
