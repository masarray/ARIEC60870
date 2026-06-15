# ARIEC60870 v0.1 Foundation Release

This release creates the first working project foundation:

- Apache-2.0 repo structure
- .NET 8 class library and CLI
- offline log parser
- FT1.2 fixed and variable frame decoder
- link-layer semantic decode
- starter IEC-103 ASDU decoder
- semantic finding engine
- Markdown/JSON report generation
- sample trace

Known limitations:

- no live serial capture yet
- no active master/slave yet
- no full vendor FUN/INF mapping yet
- ASDU Type 4/private/generic objects are preserved as unknown raw payload until profile decoder is added
