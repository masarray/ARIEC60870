# IEC-103 Class 1 / Class 2 Classification Audit

ARIEC60870 classifies the visible transaction class from the **master request context**, not from the ASDU type name.

## Class 1

Class 1 is event/priority data requested by the master using the IEC-103 request class 1 link function. In normal running, ARIEC60870 enters Class 1 drain only when:

- a secondary/slave response indicates **ACD=1**, meaning Class 1 data is pending; or
- the master is in a bounded General Interrogation follow-up window.

The drain stops on NO DATA, GI END, ACD clear, DFC busy, timeout, or drain limit. ARIEC60870 intentionally avoids the bad pattern: Class 1 -> NO DATA -> Class 1 -> NO DATA.

## Class 2

Class 2 is the normal background polling cycle. A response to a Class 2 request may still set ACD=1. That does **not** mean the current response is Class 1; it means the relay is advertising pending Class 1 data, and the master may then switch to Class 1 drain.

## UI meaning

The `Class` column in Line Monitor represents the transaction/request class that caused the TX/RX pair. For protocol evidence, inspect the Frame Interpreter: control field, ACD, DFC, ASDU, FUN/INF, and relay timestamp are shown separately.
