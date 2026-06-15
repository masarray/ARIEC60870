# Evidence Summary Design — v1.8.1

## Purpose

Evidence Summary answers: **what has been proven in the session?**

It is not a duplicate of the Protocol Trace. Routine TX/RX traffic belongs to the Protocol Trace.

## Included by default

- Signal/value proof
- Digital state change
- Quality issue
- Timestamp issue
- GI milestone
- Command lifecycle milestone
- Timeout / NACK / failed transaction
- Important diagnostics

## Suppressed by default

- Normal Request Class 1
- Normal Request Class 2
- Normal ACK / routine poll traffic
- Repeated unchanged signal values
- Internal polling housekeeping

## Why

This makes the first tab useful for FAT/SAT and commissioning review, while Protocol Trace remains the raw telegram evidence for protocol engineers.
