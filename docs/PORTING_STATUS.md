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
  - backup payload,
  - shared domain limits (500 history, 50 pinned, 200 snippets, 1 MB image persistence limit).
- Added domain abstraction interfaces for repositories and encryption.
- Implemented application services:
  - `ClipboardStateService` for runtime state orchestration,
  - `ClipboardBackupService` for export/import payload handling,
  - DI registration via `AddClipPocketApplication`.
- Implemented infrastructure services:
  - file-based history/pinned/snippet/settings repositories,
  - AES-GCM encryption service with local key file,
  - DI registration via `AddClipPocketInfrastructure`.
- Wired Presentation startup to initialize DI and load application state at launch.

## Pending (next milestone)
- Clipboard capture pipeline (Windows clipboard monitor + type classification).
- Global hotkey manager and tray icon integration.
- Floating panel runtime behavior (show/hide, edge monitor, auto-hide).
- Full MVVM UI migration from static prototype to state-driven views.
- Settings UI binding to `ClipPocketSettings` and persistence commands.
- Quick actions (transformations, export, QR, share), onboarding, update checker.
- Test projects (unit + integration) and parity validation suite.
