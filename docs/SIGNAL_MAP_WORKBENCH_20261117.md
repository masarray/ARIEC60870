# Signal Map workbench — operator workflow (2026-11-17)

This is a source UI change for review, **not an already released installer or field-qualified RTU configuration**.

## Daily workflow
1. Open Signal Map. Press Ctrl+F to search by IOA, name, bay, mnemonic or multiple terms (example: TRAFO POAQ). Enter jumps to the first match, Esc clears the search.
2. Combine search with Bay and Measured analog / Digital status / Commands filters. The header shows visible/total signals; the × button resets filters without altering JSON.
3. The default grid shows eight essential columns: CA, IOA, TI, Name, Bay, business TYPE, Mnemonic and Scaling. The first four stay frozen. The More menu switches on all ten advanced columns.
4. Analog points open Engineering Scaling by default. Unit, mode and Factor or X0/X100/Y0/Y100 remain available. Digital/command points open Mapping & Commands.
5. Mapping & Commands places CA/IOA/TI, name, Bay, business TYPE and Mnemonic before protocol and note fields. Generate name explicitly composes BAY TYPE + TYPE + MNEMO and preserves an existing trailing numeric #. Nothing is auto-renamed while browsing.
6. Other fields remain accessible: group, IEC role, class/COT, command policy, feedback, state map, metadata limits, unit and description. More contains Load, Save As, Validate, optional I/O sync, CA alignment and columns view. Save & Apply preserves its existing behavior.

## Engineering invariants
- View-only search, filters, inspector navigation and column toggles must not dirty the profile.
- Add and Duplicate clear filters first, keeping the new row visible.
- The authoritative JSON schema and IEC mapping, command, quality and wire paths remain unchanged.
- TwoPoint and Factor are mutually exclusive; never guess an unknown TI11 counts-to-MVar conversion. IEEE float needs no extra scaling when RTU values already represent engineering units.
- Sync 31 I/O still requires explicit confirmation and Save As a new copy.

## Acceptance
The actual Windows WPF passive GUI smoke retains the 1360×820 above-fold TI9 endpoint test and now additionally checks essential/advanced columns, IOA and multi-term search, bay/kind filtering, reset, name composition and non-dirty navigation. A Windows CI run and visual screenshot review are still required; no physical FAT has been performed.

## UX foundations
- [Microsoft WPF DataGrid](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/datagrid)
- [Microsoft WPF performance guidance](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-controls)
- [NN/g — Data Tables: Four Major User Tasks](https://www.nngroup.com/articles/data-tables/)
