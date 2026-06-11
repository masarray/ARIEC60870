# ARIEC60870 v1.2.22 — TabItem Trigger Compile Fix

## Fixed
- Removed invalid WPF `TabItem.IsPressed` trigger from `ModernTheme.xaml`.
- The segmented navbar still keeps hover and selected pill visuals, but avoids button-only properties that do not exist on `TabItem`.

## Notes
- No IEC-103 protocol logic changed.
- This is a compile/XAML safety fix for v1.2.21.
