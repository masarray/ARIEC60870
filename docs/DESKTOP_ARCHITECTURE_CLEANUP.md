# Desktop Architecture Cleanup

ARIEC60870 Desktop is a WPF shell around protocol, transport, mapping, evidence, and reporting modules. The desktop layer may coordinate UI state, but it should not become the owner of protocol rules, file formats, or long-lived business logic.

## Current Phase B Boundary

Phase B splits the former monolithic `MainWindow.xaml.cs` code-behind into feature-owned partial files. This is an intentional intermediate step: it reduces contributor friction without rewriting runtime behavior or changing protocol semantics.

```text
src/ARIEC60870.Desktop/
  MainWindow.xaml
  MainWindow.xaml.cs                    # shell fields, constructor, port/profile bootstrap only
  Features/
    MainWindow.CommandDock.cs           # manual control command dock and validation UI
    MainWindow.SetupPreferences.cs      # setup load/save and protocol-mode combo persistence
    MainWindow.Session.cs               # connect/start/stop, settings build, transport/session factory
    MainWindow.RuntimeProof.cs          # GI coverage, command proof, scan health, runtime CA proof
    MainWindow.LiveEvidencePipeline.cs  # queue flush, visible evidence routing, backpressure policy UI
    MainWindow.FrameInspector.cs        # selected-frame inspector, protocol map, hex hover behavior
    MainWindow.WorkspaceSelection.cs    # trace/evidence list selection, freeze/resume live view behavior
    MainWindow.CaptureFiles.cs          # .ariec archive open/save and capture manifest models
    MainWindow.TriggerCapture.cs        # user-defined smart capture rules and trigger evidence window
    MainWindow.Reporting.cs             # report workspace orchestration and PDF export wiring
    MainWindow.Export.cs                # TSV/data-grid export helpers
  Services/
    LocalWorkspacePaths.cs              # local app-data path ownership
  ViewModels/
    *Row.cs                             # bindable row models used by WPF grids/lists
```

## Why Partial Classes First?

The runtime is protocol-sensitive: IEC-101/103/104 polling, GI proof, Class 1/Class 2 handling, command lifecycle, and capture evidence should not be rewritten blindly. Partial files are used here as a low-risk architecture step before deeper service extraction.

This gives maintainers immediate benefits:

- smaller files with clear feature names;
- safer pull requests because changes land in a bounded context;
- easier future migration to services/viewmodels;
- no intentional behavior change in protocol timing, evidence capture, or report output.

## Ownership Rules

### MainWindow.xaml.cs

Allowed:

- field declarations;
- constructor and timer wiring;
- first-run bootstrap such as port refresh and default profile load.

Not allowed for new code:

- report generation;
- capture archive serialization;
- protocol proof rules;
- command lifecycle validation;
- event/value classification;
- path constants;
- large UI gesture handlers.

### Features/

Feature partial files may access WPF controls, but each file must stay focused on one workspace or one workflow. New desktop logic should be placed in the closest feature file instead of growing `MainWindow.xaml.cs`.

### Services/

Services own reusable logic that should not know about WPF controls. New path, file-format, report, and persistence code should move here when it can be extracted safely.

Current extracted service:

- `LocalWorkspacePaths` centralizes local app-data paths used by setup persistence and trigger captures.

## Next Extraction Targets

The remaining cleanup path should move from partial code-behind to testable services:

1. `CaptureArchiveService` for `.ariec` read/write, manifest validation, and frame hashing.
2. `EvidencePdfReportService` for QuestPDF-based professional PDF generation.
3. `SetupPreferencesStore` for JSON persistence and version-safe migration.
4. `ProtocolProofViewModel` for GI/command proof state.
5. `LiveEvidenceWorkspaceViewModel` for trace/evidence freeze, selection, and follow-live behavior.

## Architecture Guardrails

The repository test suite contains desktop architecture checks. These checks intentionally fail when:

- `MainWindow.xaml.cs` grows back into a large monolithic code-behind;
- expected feature partial files are missing;
- local app-data paths are hard-coded outside `LocalWorkspacePaths`;
- public view row models are defined inside the WPF shell instead of `ViewModels/`.

These guardrails are not style-only. They protect the project from slowly drifting back into a hard-to-review God class.
