# IEC101MasterTester Reference Study - Single Connection Only

## What was studied

The uploaded `IEC101MasterTester-master.zip` was inspected as a finished product reference.

Only the **single active connection** pattern is relevant for ARIEC60870.

The following parts are intentionally not carried over:

- NUC dual redundancy
- active/standby channel orchestration
- dual-link slave host
- redundancy dashboards
- slave simulator scope

## Useful patterns to carry forward

### 1. One active master service owns communication

IEC101MasterTester has a dedicated `Iec101MasterService` responsible for:

- opening COM port
- creating master stack instance
- applying settings
- polling
- GI
- clock sync
- command enqueue
- raw TX/RX callbacks
- ASDU callbacks
- link state callbacks

ARIEC60870 mirrors this with:

```text
ARIEC60870.Master.Iec103MasterSession
```

### 2. ACD-driven Class 1 policy

The reference project already treats ACD as a first-class communication fact:

```text
Class 1 should be prioritized when ACD=1.
Class 2 is background polling.
GI is one-shot, not cyclic polling.
```

This is exactly the right direction for IEC-103.

ARIEC60870 implements:

```text
Class 2 normal cycle
Class 1 drain only when ACD=1 / GI follow-up
No Class 1 bombardment when slave returns NO DATA
```

### 3. Evidence-first product behavior

The reference project separates:

- Line Monitor = technical evidence
- Event Log = operator-facing events
- Findings = higher-level diagnostic verdicts

ARIEC60870 follows this:

```text
TX/RX evidence event
decoded FT1.2 frame
decoded link control
decoded ASDU
semantic finding later
```

### 4. Settings are explicit and operator-safe

IEC101MasterTester stores connection settings such as:

- COM port
- baud rate
- data bits
- parity
- stop bits
- link address
- response timeout
- poll interval
- GI on connect
- clock sync on connect

ARIEC60870 single master settings now include the IEC-103 equivalent:

```text
PortName
BaudRate
DataBits
Parity
StopBits
LinkAddress
CommonAddress
ResponseTimeoutMs
Class2PollIntervalMs
MaxClass1DrainFrames
ResetFcbOnConnect
SendGeneralInterrogationOnConnect
SendClockSyncOnConnect
```

## Product decision

ARIEC60870 is not a generic passive viewer anymore.

The direction is:

```text
IEC-103 Master Tester for protection relay slaves,
with deep evidence and controlled polling behavior.
```

Offline forensic analysis remains useful, but the main product path is now active master testing.
