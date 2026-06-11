# Product Benchmark and Strategy

## Purpose

This document converts public IEC-103 ecosystem research into ARIEC60870 product direction.

ARIEC60870 should become a focused **IEC 60870-5-103 Active Master Tester + Analyzer** for protection relay communication testing, not a passive decoder only.

## Public Ecosystem Findings

### IEC-103 protocol role

IEC 60870-5-103 is a companion standard for the informative interface of protection equipment. The public summaries consistently describe IEC-103 as protection-equipment oriented, serial/unbalanced, FT1.2-framed, and different from IEC-101 because the information object address is split into **FUN** and **INF**.

Practical implication for ARIEC60870:

- master/slave relay testing matters more than generic packet viewing
- FUN/INF must remain first-class evidence
- user-defined mapping is mandatory because real signal naming is project/device specific

### Commercial protocol stacks

Commercial IEC-103 stacks show that active master/client and slave/server behavior is a recognized need for serial unbalanced links, customization, and ASDU coverage.

Practical implication:

- the market expects active master/client behavior, not just offline decode
- ARIEC60870 should keep a clear engine boundary and be capable of future slave/simulator mode
- Apache-2.0 clean-room licensing is a differentiator because commercial stacks usually require paid/commercial licensing for closed use

### Protocol analyzers / dissectors

Public protocol references and packet analyzers expose IEC-103 fields such as checksum, ASDU address/type/COT, CP32Time2a, PRM, FCB, FCV, DFC, DPI, function type, information number, and link address.

Practical implication:

- ARIEC60870 raw decode must be at least this transparent for field use
- ARIEC60870 should go beyond dissector behavior by explaining master decisions and communication health

### Axon Test5 benchmark

Axon Test5 publicly lists DNP3, IEC 60870-5-104, and Modbus. Its workflow pattern is valuable: Viewer, Config, Commands, AutoTest, Monitor, signal mapping, frame decomposition, and report generation.

Practical implication:

- ARIEC60870 should not become a multiprotocol Axon clone
- ARIEC60870 should become narrower and deeper: a specialist IEC-103 master tester
- the cockpit pattern should include Setup, Master Commands, Value Viewer, Relay Event Log, Monitor, Findings, and Report

### GitHub / open-source landscape

Some public IEC-103 examples and packet dissectors help with market awareness, but they are not used as implementation source and do not define the product workflow.

Practical implication:

- do not depend on public repos as source code
- keep ARIEC60870 as independent clean-room implementation
- use public projects only as market/feature awareness, not as implementation source

## Strategic Product Positioning

```text
Narrower than Axon.
Deeper than generic dissectors.
Legally cleaner than commercial protocol-stack wrappers.
Focused on IEC-103 active master testing with readable evidence.
```

## v1.0 Product Capability Target

ARIEC60870 v1.0 should provide:

1. Active IEC-103 master connection to one slave relay.
2. Serial COM setup with link/common address and timing options.
3. Startup sequence: Reset Link, Reset FCB, optional Clock Sync, GI.
4. controlled polling: Class 2 normal, Class 1 only on ACD=1 / bounded GI drain.
5. Raw frame monitor: TX/RX, checksum, PRM/FCB/FCV/ACD/DFC.
6. ASDU decode: Type, COT, CA, FUN, INF, DPI/value, relay timestamp.
7. User mapping profile: user-provided FUN/INF/type to signal name/state/unit.
8. Value Viewer: latest state/value snapshot.
9. Relay Event Log: relay-timestamped edge/state-change events.
10. Findings: polling, timeout, checksum, GI, mapping, and timing warnings.
11. Evidence report: Markdown/HTML/JSON first, PDF later.

## Not v1.0

- dual redundancy
- built-in vendor signal profiles
- full disturbance file extraction
- full slave simulator
- IEC-101/104 scope creep
- vendor private command library

## Differentiator

The core differentiator is not just “can decode hex.”

The real value is:

```text
Can the tester explain whether the master/slave behavior makes engineering sense?
```

Examples:

- Master requested Class 1 because ACD=1: healthy.
- Master stopped Class 1 after NO DATA: healthy.
- Slave never sends GI END: warning.
- Relay event has timestamp invalid flag: warning.
- FUN/INF unmapped: not an error, but needs project mapping.
- Checksum error burst: physical/serial quality warning.
