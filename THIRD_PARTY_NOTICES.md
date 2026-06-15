# Third-Party Notices

ARIEC60870 is released under Apache-2.0. This file records third-party runtime/framework and artwork notices relevant to public redistribution.

## Runtime / Framework

### .NET 8 SDK / runtime

ARIEC60870 targets .NET 8. The desktop application uses WPF on Windows.


### QuestPDF

- Usage: professional PDF evidence report generation in `ARIEC60870.Desktop`
- Package: `QuestPDF`
- Version used by this project: `2026.6.0`
- License: QuestPDF Community License / commercial license depending on user eligibility
- Project website: https://www.questpdf.com/

QuestPDF is used only for local PDF document generation. ARIEC60870 remains Apache-2.0 for project-owned source code, but QuestPDF usage is governed by QuestPDF's own license terms. Users and redistributors should verify their eligibility under the QuestPDF license or obtain an appropriate commercial license when required.

### System.IO.Ports

- Usage: serial COM-port I/O in `ARIEC60870.Master`
- Package: `System.IO.Ports`
- Version used by this project: `8.0.0`
- License: MIT
- Notes: used only for serial communication plumbing, not protocol logic

## Artwork / UI Assets

### Project-owned application icon

- `src/ARIEC60870.Desktop/Assets/Icons/ariec60870-icon.png`
- `src/ARIEC60870.Desktop/Assets/Icons/ariec60870-app.ico`

The stacked `IEC / 103` application icon is original project artwork generated for ARIEC60870. No font files are redistributed in this repository.

### Lucide Icons / Lucide-style outline geometry references

- Usage: WPF command rail and small UI action icons
- Local file: `src/ARIEC60870.Desktop/Resources/LucideIcons.xaml`
- License: ISC

Lucide Icons are used as simple outline icon geometry references for local WPF rendering. The geometry data is kept as a small local resource dictionary so the desktop application has no web/runtime icon dependency.

ISC License notice:

```text
Copyright (c) Lucide Icons and Contributors

Permission to use, copy, modify, and/or distribute this software for any purpose with or without fee is hereby granted, provided that the above copyright notice and this permission notice appear in all copies.

THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
```

## Protocol Stack Policy

The IEC 60870-5-103 framing, decoding, polling policy, evidence handling, and reporting logic in this repository are implemented as clean-room source code under Apache-2.0.

No source code from external protocol stacks, dissectors, commercial IEC protocol stacks, or GPL protocol implementations is included.

## Benchmark-only references

Documentation may mention public products or projects for market/feature awareness. Those references are not dependencies, and no source code, tables, or implementation logic from those projects is included in ARIEC60870.
