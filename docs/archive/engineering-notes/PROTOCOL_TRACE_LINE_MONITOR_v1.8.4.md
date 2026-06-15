# Protocol Trace Line Monitor — v1.8.4

## Purpose

Protocol Trace is the raw telegram evidence surface. It records what crossed the wire and feeds the interpreter.

## Rendering Rules

- No grid cell tooltip.
- No per-cell template.
- No raw field split into many columns.
- One virtualized item per trace line.
- Inspector shows detailed byte/field interpretation.
- Row color is applied only to the trace item container.

## Tone

- TX: blue
- RX: green
- Error / timeout / NACK: red

## Next Step

The next pass should implement clickable raw segment synchronization directly from the line monitor into the existing raw/interpreter chip highlighter.
