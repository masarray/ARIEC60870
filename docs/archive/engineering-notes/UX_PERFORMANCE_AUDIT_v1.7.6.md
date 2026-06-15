# UX Performance Audit v1.7.6

## Observed problem

The previous build felt heavy when protocol rows accumulated. The visual symptoms were:

- TX/RX colors only affected one cell, not the full transaction row.
- Timeout rows were not visually red enough.
- Buttons felt static.
- Segmented navigation felt laggy.
- DataGrid scrolling felt heavy when thousands of rows were present.

## Design correction

1. **Traffic tone belongs to the row.**
   A transaction is TX, RX, status, or error. The visible row now follows that semantic tone.

2. **Animation must not compete with protocol rendering.**
   Navigation no longer performs expensive width animation while the grid content is changing.

3. **Visible grid is not the forensic archive.**
   The visible DataGrid is a working viewport. It is capped and batched. The evidence/report pipeline remains the forensic source.

4. **WPF DataGrid must stay virtualized.**
   Virtualization, recycling, and deferred scrolling are enforced at the theme level.

5. **Button feedback should be tactile but cheap.**
   Buttons use short scale and shadow feedback. No continuous ambient animation is used.
