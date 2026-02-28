# ClipPocketWin Parity Matrix (OriginalRepo -> Windows)

Baseline reference:
- Source: `OriginalRepo/ClipPocket`
- Target: `ClipPocketWin` solution
- Status legend: `implemented`, `partial`, `not implemented`, `intentionally excluded`

Notes:
- This matrix includes both historical work already completed (pregresso) and pending work.
- Backup/export-import remains intentionally out of scope unless explicitly requested.

Current SAL snapshot:
- implemented: 26
- partial: 5
- not implemented: 4
- intentionally excluded: 1
- strict SAL: 74.29% (`implemented / (total - intentionally excluded)`)
- weighted SAL: 81.43% (`(implemented + 0.5 * partial) / (total - intentionally excluded)`)

| Source Area | Source File(s) | Windows Status | Windows Mapping / Notes |
|---|---|---|---|
| Architecture split | `app/ClipPocketApp.swift` (monolith) | implemented | Layered split in `ClipPocketWin`, `ClipPocketWin.Application`, `ClipPocketWin.Domain`, `ClipPocketWin.Infrastructure`, `ClipPocketWin.Shared`. |
| Domain clipboard model | `Models/ClipboardItem.swift` | implemented | `ClipPocketWin.Domain/Models/ClipboardItem.cs`, `ClipboardItemType.cs`, `RichTextContent.cs`. |
| Domain pinned model | `Models/PinnedClipboardItem.swift` | implemented | `ClipPocketWin.Domain/Models/PinnedClipboardItem.cs`. |
| Domain snippet model | `Models/Snippet.swift` | implemented | `ClipPocketWin.Domain/Models/Snippet.cs` (placeholder extraction/resolution). |
| Keyboard shortcut model | `Models/KeyboardShortcut.swift` | implemented | `ClipPocketWin.Domain/Models/KeyboardShortcut.cs` keeps default `Ctrl+ò`; WinUI settings now supports recording a new shortcut, resetting to default, persisting to `ClipPocketSettings`, and applying changes live via `IGlobalHotkeyService.UpdateShortcutAsync`. |
| Domain limits | Source hard limits in app/core files | implemented | `ClipPocketWin.Domain/DomainLimits.cs` (500 history, 50 pinned, 200 snippets, 1 MB image persistence). |
| Result/error explicit handling | Source uses print/errors | implemented | `ClipPocketWin.Shared/ResultPattern/*`, custom `ErrorCode`. |
| Storage paths and persistence | `ClipPocketApp.swift`, `SettingsManager.swift`, managers | implemented | File repositories in `ClipPocketWin.Infrastructure/Persistence/*` (history/pinned/snippets/settings). |
| Encryption service | `Utilities/HistoryEncryptor.swift` | implemented | `ClipPocketWin.Infrastructure/Security/AesGcmClipboardEncryptionService.cs`. |
| Encryption mode switching | `ClipPocketApp.swift` | partial | Persisted encrypted/plain history supported; full UI migration flow and confirmations pending. |
| Clipboard ingestion | `ClipPocketApp.swift` `checkClipboard/readClipboardItem` | implemented | `ClipPocketWin.Infrastructure/Clipboard/WindowsClipboardMonitor.cs` + `ClipboardStateService`. |
| Type classification | `ClipPocketApp.swift` `detectContentType` | implemented | `ClipPocketWin.Infrastructure/Clipboard/ClipboardItemClassifier.cs` (text/code/url/email/phone/json/color + image/file/rich text paths). |
| Dedupe and limits at runtime | `ClipPocketApp.swift` add logic | implemented | `ClipPocketWin.Application/Services/ClipboardStateService.cs` dedupe + trim + persistence. |
| Incognito behavior | `ClipPocketApp.swift` | implemented | Domain/runtime behavior existed and is now fully wired in WinUI settings (`Incognito Mode` toggle persists through `SaveSettingsAsync`). |
| Excluded apps behavior | `Utilities/ExcludedAppsManager.swift`, `Views/ExcludedAppsView.swift` | implemented | Runtime filtering skips capture for matching foreground app identifiers in `ExcludedAppIds`; WinUI editor now updates and persists `ExcludedAppIds` through `ClipPocketSettings`. |
| App startup orchestration | `ClipPocketApp.swift` launch flow | implemented | `ClipPocketWin/App.xaml.cs` initializes state + starts runtime monitor. |
| Runtime state debug observability | Source `print` logs | not implemented | Conditional logger was removed; currently only standard `ILogger` logs remain. |
| Global hotkey integration | `Utilities/GlobalHotkey.swift` | implemented | `ClipPocketWin/Runtime/PollingGlobalHotkeyService.cs` + `AppRuntimeService` wiring to panel toggle. |
| Tray icon/status item | `Views/StatusItemView.swift` + app delegate setup | implemented | `ClipPocketWin/Runtime/WindowsTrayService.cs` provides tray icon plus Show/Hide/Toggle/Exit actions wired to `AppRuntimeService`. |
| Floating panel show/hide runtime | `ClipPocketApp.swift` show/hide/toggle | implemented | `ClipPocketWin/Runtime/WindowPanelService.cs` + `ClipPocketWin.Application/Services/AppRuntimeService.cs` provide show/hide/toggle including global outside-click hide polling. |
| Edge monitor auto show/hide | `Utilities/MouseEdgeMonitor.swift` | implemented | `ClipPocketWin/Runtime/MouseEdgeMonitorService.cs` + `AppRuntimeService` delay-based edge enter/exit integration. |
| Click outside hide | `ClipPocketApp.swift` global mouse monitor | implemented | `AppRuntimeService` now runs a global outside-click monitor loop and hides panel when click starts outside panel bounds. |
| Auto-paste after selection | `ClipPocketApp.swift` `autoPasteIfEnabled` | implemented | `WindowsAutoPasteService` handles clipboard payload write + focus restore + simulated `Ctrl+V`; double-click flow now hides panel before paste, performs foreground retry checks, and uses corrected Win32 `SendInput` interop layout so `Ctrl+V` dispatch no longer fails with `Win32Error=87`, while context-menu `Copy` remains copy-only. |
| Full MVVM clipboard UI | `Views/ClipboardManagerView.swift` | partial | Main cards are now state-driven from `IClipboardStateService`, selectable, visually aligned per type (colors/icons), and include source-app icon + live relative timer in header; timer updates now mutate labels in-place (no list rebind/flicker/scroll reset). Image cards now render real thumbnails constrained to card bounds, with fallback text only when decode fails. Full MVVM sections/search/filters/context actions are still pending. |
| Search and type filters | `Views/ClipboardManagerView.swift` | implemented | `MainWindow` now applies live search (contains + fuzzy subsequence) and type filter chips over real clipboard/pinned data. |
| Context menu actions | `Views/ClipboardManagerView.swift` | implemented | WinUI card context menu supports `Copy` (without direct paste), `Save to File`, `Copy as Base64`, `URL Encode`, `URL Decode`, plus core state actions (`Pin/Unpin`, `Delete`, `Clear History`). |
| Drag/drop to external apps | `Views/DraggableClipboardItemCard.swift` | implemented | WinUI cards now support external drag/drop payloads for text, files, and images (image/file drag uses real file payloads instead of internal clipboard copy). |
| Snippets section UX | `Views/SnippetCard.swift`, `Views/SnippetPlaceholderFormView.swift` | partial | Data model/repository/state exist; interactive UI flow pending. |
| Settings screen parity | `Views/SettingsView.swift` | implemented | New WinUI `SettingsWindow` opened from panel gear implements requested sections: launch-at-login, keyboard shortcut recorder/reset, history limit + clear history, privacy/incognito + excluded apps manager (running processes + manual add), and about section. Encryption/snippets controls intentionally omitted per requested scope. |
| Onboarding flow | `Views/OnboardingView.swift` | not implemented | Pending first-run onboarding in WinUI. |
| Update checker | `Utilities/UpdateChecker.swift` | partial | Settings now includes `Check for Updates` placeholder button (fittizio). Real GitHub release check workflow remains pending. |
| Quick actions: text transformations | `Utilities/TextTransformations.swift` | partial | Windows quick-actions subset is available from context menu (`Copy as Base64`, `URL Encode`, `URL Decode`); broader transformation catalog parity is still pending. |
| Quick actions: QR/share | `Utilities/QuickActions.swift` | not implemented | Pending implementation in Windows stack. |
| Source app icon cache | `Utilities/SourceAppIconCache.swift` | implemented | WinUI now resolves and caches source application icons (path/process fallback), renders them in card header, and uses icon-derived vibrant accent fallback similarly to Swift behavior. |
| Test coverage (unit/integration) | `ClipPocketTests`, `ClipPocketUITests` (minimal) | not implemented | No `ClipPocketWin.Tests.*` projects yet; planned next milestones. |

Operational note:
- Logging output now goes to both console and `%LocalAppData%\\ClipPocketWin\\logs\\app.log` for runtime diagnostics (including global hotkey state transitions).
- Double-click paste tracing now includes hide-before-paste and focus activation attempt/check logs to diagnose target-window handoff.
| Backup/export-import payloads | `Utilities/ClipboardBackupManager.swift`, settings actions | intentionally excluded | Explicitly removed from Windows scope by user request. |
