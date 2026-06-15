# ARIEC60870 v0.2.1 Compile Fix

This maintenance release fixes a CLI compile issue in `Program.cs`.

## Fixed

- Replaced `arg.StartsWith('-', StringComparison.Ordinal)` with `arg.StartsWith("-", StringComparison.Ordinal)`.
- The previous expression passed a `char` where the overload with `StringComparison` requires a `string`, causing CS1503 in Visual Studio.

## Notes

- No protocol logic changed.
- No external protocol stack was added.
- License remains Apache-2.0.
