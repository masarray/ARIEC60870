# IEC-101/104 Signal List: Engineering measurement scaling

## Why a raw 13641 may display as 13641 MVar

IEC-101/104 sends a Type ID and measurement encoding, **not** a guaranteed
project-specific MVar/CT/VT conversion coefficient. The Signal List's `Unit`
is a label, not an instruction to divide the measured number. The bundled
sanitized example profile declares FEEDER REACTIVE POWER (Q) at CA=105,
IOA=790447, TI=11 (M_ME_NB_1), Unit=MVar and an engineering min/max for
test acceptance, but **no approved scaling factor**. Thus a wire SVA of
13641 with the default scale 1 remains 13641, not automatically 1730 MVar.

The installed application version in the operator's 17 November screenshot
was 3.6.12. These improvements are code **after** the immutable v3.6.13 tag
and must pass PR CI and a future separately published release before appearing
in a public binary.

## Project configuration in Signal List

Select the correct station CA, IOA and actual IEC Type ID. In the compact
selected-signal editor set:

| Field | Meaning | Default |
| --- | --- | --- |
| Unit | Engineering display suffix (MW, MVar, kV, A...) | empty |
| Scale | RTU resolution / engineering multiplier applied to all supported analog types | 1 |
| Offset | Additive engineering correction after the multipliers | 0 |
| Ratio primary | Approved primary/secondary ratio numerator; opt-in | 1 |
| Ratio secondary | Approved primary/secondary ratio denominator; must be positive | 1 |
| Normalized full-scale | Engineering units at +1 per-unit NVA, only TI9/10/21/34 | 1 |

After editing: **Validate row → Validate profile → Save & Apply**. The
operator must obtain the correct RTU/gateway scale, measurement range and
CT/VT configuration from approved project data. Do **not** choose a ratio
merely to force one sample to display a desired value. If the RTU already
sends primary engineering units, keep the additional CT/VT ratio at 1/1
to avoid double conversion.

For an accepted measured information object:

- **Normalized IEC TI9/10/21/34**: NVA = signed I16 / 32768.
  Engineering = NVA × NormalizedFullScale × Scale × (RatioPrimary / RatioSecondary) + Offset.
- **Scaled IEC TI11/12/35**: SVA = signed I16.
  Engineering = SVA × Scale × (RatioPrimary / RatioSecondary) + Offset.
- **Short float IEC TI13/14/36**: source = original IEEE-754 R32.
  Engineering = source × Scale × (RatioPrimary / RatioSecondary) + Offset.

All calculation inputs are taken from the already-decoded **original
information-element bytes**. Failed decoder checks, incorrect Type family,
NaN/infinite floats, and missing data cannot become an engineering reading.
Only an exact, unambiguous CA/IOA/type-family Signal List match may supply
a numeric calibration. A safe *name-only* alias for a different station
CA can supply a label but **not** a ratio or scale from another station.

For a synthetic TI11 reading of 13641, for example, Scale=0.1,
RatioPrimary=1, RatioSecondary=1 and Offset=0 gives 1364.1 MVar.
This example is **not** a recommendation for any real feeder. The displayed
engineering value of 1730 MVar mentioned in field feedback requires the
actual approved scale and/or secondary ratio; no constant is inferred.

## Evidence and validation boundaries

Original raw HEX, signed source integer/IEEE bytes, COT/CA/IOA, timestamp,
QDS/OV/IV, active GI/command transactions and device value are immutable.
The engineering unit is **presentation**, not raw protocol truth, and must
not replace a device-side or laboratory reference.

Values and Event Log apply the calibrated display on new verified RX,
Frame Trace uses the same formatter, and indexed Event Log pages can
re-project from preserved original wire bytes after a profile switch.
The CSV Value column follows the displayed Event Log value; original
HEX remains separately exported. Values rows with previously buffered
source numbers may update on the next verified RX; do not treat a
stale cached value as fresh physical data.

Synthetic tests and green Windows CI do not qualify RTU-specific
calibration, 1,200-bps SOE completeness, or multi-vendor FAT.
