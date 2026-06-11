# ARIEC60870 v1.2.8 - Protection Relay Behavior Simulator

## Added

- Protection relay behavior model for the IEC-103 slave simulator.
- Random phase pickup simulation for phase A, B, or V/C.
- Trip event generated approximately 200 ms after pickup for the selected phase.
- Pickup/trip latch behavior: signals remain ON until reset.
- Auto reset after 20 seconds by default.
- Next fault cycle after 10 seconds by default.
- IEC-103 command reset support using configurable Type 20 command address, default FUN=255 / INF=19.
- Animated Class 2 current measurands for phase A, B, and V/C.
- Sample user mapping updated so Value Viewer and Event Log show readable protection/current names during simulation.
- ASDU Type 9 decoder now extracts the first signed 16-bit measurand value as a universal numeric hint. Final scaling/unit still comes from the user mapping profile.

## Protocol behavior

- Class 1 data is exposed through ACD=1 and drained only when the master asks for Class 1.
- Class 2 polling returns animated measurands only when no Class 1 event queue is pending.
- Empty Class 1 queue returns NO DATA.
- The simulator remains a deterministic test environment, not a vendor relay model.
