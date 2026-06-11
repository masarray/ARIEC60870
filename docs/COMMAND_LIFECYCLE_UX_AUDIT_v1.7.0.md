# Command Lifecycle UX Audit — v1.7.0

## Why the old dock was misleading

The previous dock exposed one generic `Queue Control Command` button plus a `Select first` checkbox. That UI was not good enough for commissioning because command validation is a lifecycle, not a single value write.

A field engineer must be able to prove each stage separately:

1. SELECT open/lower/setpoint
2. OPERATE open/lower/setpoint
3. SELECT close/raise
4. OPERATE close/raise
5. Wrong lifecycle tests such as SELECT OPEN followed by OPERATE CLOSE
6. Slave/server response: activation confirmation, activation termination, negative confirmation, timeout, or feedback change

## New rule

The dock is now action-first:

- Double/single commands show Open/Close actions.
- Regulating step commands show Lower/Raise actions.
- Setpoint commands show Setpoint actions.
- The internal runtime still serializes transport writes, but the UI treats these as operator-priority actions, not background polling queue items.

## Next recommended pass

The next pass should add a command verdict grid:

- Command issued time
- Select/Operate phase
- Expected feedback IOA
- ACTCON observed
- ACTTERM observed
- Negative confirmation observed
- Feedback value observed
- Acknowledge time
- Change time
- Pass/fail against PLN PUSERTIF timing criteria
