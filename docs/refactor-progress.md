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
- [todo] Reduce avoidable hot-path list/array allocations in state/UI refresh paths.
- [todo] Consolidate duplicated helpers (for example DIB -> BMP conversion).

## Stream C - Architecture Refactor (P1)

- [todo] Split monolithic `MainWindow.xaml.cs` responsibilities.
- [todo] Split `ClipboardStateService` into focused collaborators.
- [todo] Split `WindowsClipboardMonitor` responsibilities.
- [todo] Apply one-top-level-type-per-file where currently violated.

## Stream D - Clipboard Model Evolution (P1/P2)

- [todo] Design typed clipboard item hierarchy.
- [todo] Introduce backward-compatible serialization discriminator.
- [todo] Remove repeated type-switch logic with polymorphic behavior.

## Stream E - Documentation (P1)

- [todo] Add XML docs to public interfaces.
- [todo] Add XML docs to core public models and services.
- [todo] Add targeted comments only for non-obvious native/interop logic.

## Stream F - CPU Optimizations (Very Low Priority)

- [todo] Keep monitoring only; no immediate work planned.

## Change Log

- 2026-03-01: Created tracking document and initialized all streams.
- 2026-03-01: Aligned logging strategy with new requirement: logger calls gated by `#if DEBUG`.
- 2026-03-01: Implemented bounded in-memory cache eviction for image/file/source icon caches and added on-disk cleanup limits for drag/image/icon cache directories.
