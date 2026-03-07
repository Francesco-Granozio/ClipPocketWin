# Refactor Progress

This document tracks the refactoring work requested for ClipPocketWin.

## Status Legend

- `todo`
- `in_progress`
- `done`
- `blocked`

## Stream A - Debug-Only Logging (P0)

- [done] Configure DI logging services in startup and keep providers active only in `DEBUG` builds.
- [done] Wrap all runtime logger call sites with `#if DEBUG` / `#endif`.
- [done] Keep logging infrastructure available for debug diagnostics while avoiding release logging output.
- [done] Normalize logger usage in refactored runtime classes (no release log emission).

## Stream B - Memory Stability and Allocations (P0)

- [done] Add bounded strategy for in-memory icon/image caches.
- [done] Add cleanup policy for on-disk cache folders.
- [done] Reduce avoidable hot-path list/array allocations in state/UI refresh paths.
- [done] Consolidate duplicated helpers (for example DIB -> BMP conversion).

## Stream C - Architecture Refactor (P1)

- [done] Split monolithic `MainWindow.xaml.cs` responsibilities.
- [done] Split `ClipboardStateService` into focused collaborators.
- [done] Split `WindowsClipboardMonitor` responsibilities.
- [done] Apply one-top-level-type-per-file where currently violated.

## Stream D - Clipboard Model Evolution (P1/P2)

- [done] Design typed clipboard item hierarchy.
- [done] do not introduce backward-compatible serialization discriminator, after refactor clipboard will be deleted
- [done] Remove repeated type-switch logic with polymorphic behavior.

## Stream E - Documentation (P1)

- [todo] Add XML docs to public interfaces.
- [todo] Add XML docs to core public models and services.
- [todo] Add targeted comments only for non-obvious native/interop logic.

## Stream F - CPU Optimizations (Very Low Priority)

- [todo] Keep monitoring only; no immediate work planned.

## Stream G - Color copy fix
- [done] Colors like rgb(124, 243, 121), hsl(119, 84%, 71%) are detected as color and now render with the copied solid color instead of the default application gradient.

## Change Log

- 2026-03-01: Created tracking document and initialized all streams.
- 2026-03-01: Aligned logging strategy with new requirement: logger calls gated by `#if DEBUG`.
- 2026-03-01: Implemented bounded in-memory cache eviction for image/file/source icon caches and added on-disk cleanup limits for drag/image/icon cache directories.
- 2026-03-02: Reduced state/UI hot-path allocations in `ClipboardStateService` and `MainWindow`, consolidated duplicated clipboard helpers (DIB->BMP conversion and text payload resolution), and completed one-top-level-type-per-file fixes in shared/domain/runtime/window files.
- 2026-03-06: Split `MainWindow` card-rendering responsibilities into dedicated classes (`ClipboardCardFactory`, `ClipboardCardViewModel`, cache helpers, syntax highlighter), refactored `ClipboardStateService` with focused collaborators (`ClipboardStateStore`, persistence coordinator, selection service), split `WindowsClipboardMonitor` into monitor/payload/native API classes, and introduced a shared color parser that supports hex/rgb/rgba/hsl/hsla for consistent classification and card rendering.
- 2026-03-06: Introduced a typed clipboard item hierarchy (`TextClipboardItem`, `ImageClipboardItem`, `FileClipboardItem`) with centralized factory creation, migrated persistence to a dedicated clipboard item JSON converter, and started replacing enum-based switches with polymorphic item behavior in state, runtime quick actions, drag handling, and card rendering metadata.
- 2026-03-07: Completed the UI follow-up for Stream D by replacing enum/tag switch filter logic with a dedicated `ClipboardTypeFilter` helper and moving card style mapping to `ClipboardCardStylePalette`, while the typed item hierarchy now exposes polymorphic filter/style/category flags used by UI/runtime.
