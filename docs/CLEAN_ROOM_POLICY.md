# Clean-Room Policy

ARIEC60870 is intended to be legally clean and corporate-friendly under Apache-2.0.

## Allowed Inputs

- Public protocol behavior descriptions
- User-provided traces
- User-created sanitized test vectors
- Independently written code
- Public product pages for feature benchmarking
- Official standards purchased/owned by the user, used for understanding, not copied into the repo

## Forbidden Inputs

- Copying source from external protocol stacks, dissectors, unclear-license sources, commercial stacks, or vendor tools
- Porting class structure/state machine from third-party code
- Copying vendor manual tables into repo without explicit legal clearance
- Shipping customer relay database or private project mapping as samples
- Embedding GPL/commercial protocol stack code

## How to Use External Projects Safely

External projects may be used only for market/feature awareness, not as source code.

Examples:

```text
Allowed: “Public protocol references expose fields such as FCB, ACD, FUN, INF; ARIEC60870 should expose equivalent evidence.”
Forbidden: copy external dissector code, table names, or implementation logic directly.
```

## File Header

New source files should include:

```csharp
// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0
```

## Third-Party Dependencies

Keep dependencies minimal. If added, document in `THIRD_PARTY_NOTICES.md`:

- package name
- license
- purpose
- whether it touches protocol logic

Protocol stack dependencies are not allowed unless the user explicitly changes the project policy.
