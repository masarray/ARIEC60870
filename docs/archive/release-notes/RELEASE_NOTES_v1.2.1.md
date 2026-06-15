# ARIEC60870 v1.2.1 Release Notes

## Compile fix

- Removed `CharacterSpacing` from the WPF `TextBlock` style in `ModernTheme.xaml`.
- `CharacterSpacing` is a WinUI/UWP-style property and is not available on WPF `System.Windows.Controls.TextBlock`.
- The visual direction remains unchanged; typography still uses restrained uppercase/eyebrow styling without relying on unsupported WPF properties.

## Simulator strategy

- Added `docs/SLAVE_SIMULATOR_STRATEGY.md`.
- Clarified that ARIEC60870 remains an IEC-103 Active Master Tester, while the slave simulator is a supporting test/demo environment.
- Recommended an early standalone IEC-103 slave simulator milestone before professional report work.

