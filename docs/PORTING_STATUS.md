# ClipPocketWin Porting Status

## Implemented in this iteration
- Introduced a clean layered architecture with separate projects:
  - `ClipPocketWin.Domain`
  - `ClipPocketWin.Application`
  - `ClipPocketWin.Infrastructure`
  - existing `ClipPocketWin` now acts as Presentation/WinUI host.
- Added domain models aligned with Swift source:
  - clipboard item + typed content model,
  - pinned item,
  - snippet with placeholder extraction and resolution,
  - keyboard shortcut,
  - app settings,
  - shared domain limits (500 history, 50 pinned, 200 snippets, 1 MB image persistence limit).
- Added domain abstraction interfaces for repositories and encryption.
- Implemented application services:
  - `ClipboardStateService` for runtime state orchestration,
  - DI registration via `AddClipPocketApplication`.
- Implemented infrastructure services:
  - file-based history/pinned/snippet/settings repositories,
  - AES-GCM encryption service with local key file,
  - DI registration via `AddClipPocketInfrastructure`.
- Wired Presentation startup to initialize DI and load application state at launch.
- Added Windows clipboard capture pipeline with infrastructure monitor + runtime orchestration:
  - polling-based monitor using Windows clipboard sequence tracking,
  - deterministic type classification for `text`, `code`, `url`, `email`, `phone`, `json`, `color`, `image`, `file`, `rich text`,
  - capture wired to `ClipboardStateService` and startup runtime activation in `App.xaml.cs`.
- Added runtime orchestration and panel trigger services:
  - `AppRuntimeService` in Application to coordinate clipboard runtime + hotkey + edge monitor + tray startup,
  - polling global hotkey service wired to panel toggle,
  - edge monitor service wired to panel show/hide with `AutoShowDelay`/`AutoHideDelay`,
  - panel visibility control service (`show/hide/toggle`) attached to `MainWindow` handle,
  - tray currently runs in degraded mode with explicit `TrayStartFailed` until native tray adapter is implemented.
- Added `docs/PARITY_MATRIX.md` with full Source->Windows parity tracking, including pre-existing implemented scope and pending items (backup excluded).

## Pending (next milestone)
- Native tray icon integration.
- Floating panel runtime refinements (click-outside hide and parity-level behavior).
- Full MVVM UI migration from static prototype to state-driven views.
- Settings UI binding to `ClipPocketSettings` and persistence commands.
- Quick actions (transformations, export, QR, share), onboarding, update checker.
- Test projects (unit + integration) and parity validation suite.
