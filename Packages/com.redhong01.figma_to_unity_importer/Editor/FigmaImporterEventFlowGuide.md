# Figma Importer Event Flow Guide

This package uses a unified event flow logger:

- Logger: `FigmaImporterEventFlow`
- Log shape: `[FigmaImporter][Flow][Feature][ChainId] START/STEP/END ...`
- Goal: keep feature pipelines deterministic and traceable.

## Feature Chains

`GenerateNodes`

- START: user triggers `Generate Nodes`
- STEP: `RunCreated`
- STEP: `GenerationControlsBound`
- STEP: `GetFileInternalStarted`
- STEP: optional `PauseRequested` / `ContinueRequested`
- STEP: optional `CancelRequested` (`User requested cancel` or `Auto cancel due to generation stall`)
- STEP: optional `GenerationStallDetected`
- STEP: optional `ForceResetGenerationState`
- END: `Completed` / `Canceled` / `Failed` / `Superseded` / `ForceReset` / `WindowDestroyed`

`GetNodes`

- START: manual `Get Node Data` or generate preflight load
- STEP: optional `ResetControlFlags` (manual flow only)
- STEP: `RequestNodeInfo`
- STEP: `NodeInfoLoaded`
- END: `Completed` / `Canceled` / `Failed` (`Failed` when request returns no parsable nodes)

`Diagnostics`

- START: `Run One-Click Diagnostic`
- STEP: `ValidateInput`
- STEP: `OutputDirectoryReady`
- STEP: `FetchPrimaryPayload`
- STEP: optional `RunImportPass`
- STEP: `FetchSamplePayload`
- STEP: `DiagnosticPackGenerated`
- END: `Completed` / `Failed`

`DiagnosticsAgentHandoff`

- START: `Analyze + Fix With Installed Agent` from diagnostics window
- STEP: optional `CreatedIssuePackFallback`
- STEP: `PromptPrepared`
- END: `Completed` / `Delegated` / `TimedOut` / `Failed` / `Skipped`

`ImporterErrorHandoff`

- START: `Analyze + Fix With Installed Agent` from importer error handoff window
- STEP: `IssuePackCreated`
- STEP: `PromptPrepared`
- END: `Completed` / `Delegated` / `TimedOut` / `Failed` / `Skipped`

`Dependencies`

- START: force init or auto init
- STEP: `MissingDependenciesDetected`
- STEP: repeated `DependencyInstallAttempt`
- END: `Completed` / `Skipped` / `Failed` (`Skipped` also covers an already-running init session)

`FrameSyncCheck`

- START: `Check Figma Updates` from `FigmaFrameSyncBinding` inspector
- STEP: `Begin`
- STEP: `RequestContextBuilt`
- STEP: `PayloadFetched`
- STEP: optional `DiffComputed`
- END: `Completed` / `InitializedBaseline` / `Canceled` / `Failed`

`FrameSyncApply`

- START: `Apply Selected Changes To Unity Frame` from `FigmaFrameSyncBinding` inspector
- STEP: `SelectionValidated`
- STEP: optional `OperationPlanBuilt`
- STEP: optional `Applied`
- END: `Completed` / `Skipped` / `Canceled` / `Failed`

`FrameSyncRegenerate`

- START: `Regenerate Current Frame` from `FigmaFrameSyncBinding` inspector
- STEP: `Begin`
- STEP: optional `Regenerated`
- END: `Completed` / `Canceled` / `Failed`

## Rules For New Features

- Every user-facing action must have one `START` and one `END`.
- Add `STEP` entries only for meaningful state transitions.
- Keep step names stable so logs are queryable.
- Avoid duplicate contradictory transitions in a single chain.
- Use `Superseded` when an older run gets replaced by a newer run.
- Route pause/cancel actions through `FigmaNodesProgressInfo` request APIs so all control surfaces share one event path.
