# ClipPocketWin Parity Matrix (OriginalRepo -> Windows)

Baseline reference:
- Source: `OriginalRepo/ClipPocket`
- Target: `ClipPocketWin` solution
- Status legend: `implemented`, `partial`, `not implemented`, `intentionally excluded`

Notes:
- This matrix includes both historical work already completed (pregresso) and pending work.
- Backup/export-import remains intentionally out of scope unless explicitly requested.

| Source Area | Source File(s) | Windows Status | Windows Mapping / Notes |
|---|---|---|---|
| Architecture split | `app/ClipPocketApp.swift` (monolith) | implemented | Layered split in `ClipPocketWin`, `ClipPocketWin.Application`, `ClipPocketWin.Domain`, `ClipPocketWin.Infrastructure`, `ClipPocketWin.Shared`. |
| Domain clipboard model | `Models/ClipboardItem.swift` | implemented | `ClipPocketWin.Domain/Models/ClipboardItem.cs`, `ClipboardItemType.cs`, `RichTextContent.cs`. |
| Domain pinned model | `Models/PinnedClipboardItem.swift` | implemented | `ClipPocketWin.Domain/Models/PinnedClipboardItem.cs`. |
| Domain snippet model | `Models/Snippet.swift` | implemented | `ClipPocketWin.Domain/Models/Snippet.cs` (placeholder extraction/resolution). |
| Keyboard shortcut model | `Models/KeyboardShortcut.swift` | partial | `ClipPocketWin.Domain/Models/KeyboardShortcut.cs` present; runtime hotkey capture/recording pending. |
| Domain limits | Source hard limits in app/core files | implemented | `ClipPocketWin.Domain/DomainLimits.cs` (500 history, 50 pinned, 200 snippets, 1 MB image persistence). |
| Result/error explicit handling | Source uses print/errors | implemented | `ClipPocketWin.Shared/ResultPattern/*`, custom `ErrorCode`. |
| Storage paths and persistence | `ClipPocketApp.swift`, `SettingsManager.swift`, managers | implemented | File repositories in `ClipPocketWin.Infrastructure/Persistence/*` (history/pinned/snippets/settings). |
| Encryption service | `Utilities/HistoryEncryptor.swift` | implemented | `ClipPocketWin.Infrastructure/Security/AesGcmClipboardEncryptionService.cs`. |
| Encryption mode switching | `ClipPocketApp.swift` | partial | Persisted encrypted/plain history supported; full UI migration flow and confirmations pending. |
| Clipboard ingestion | `ClipPocketApp.swift` `checkClipboard/readClipboardItem` | implemented | `ClipPocketWin.Infrastructure/Clipboard/WindowsClipboardMonitor.cs` + `ClipboardStateService`. |
| Type classification | `ClipPocketApp.swift` `detectContentType` | implemented | `ClipPocketWin.Infrastructure/Clipboard/ClipboardItemClassifier.cs` (text/code/url/email/phone/json/color + image/file/rich text paths). |
| Dedupe and limits at runtime | `ClipPocketApp.swift` add logic | implemented | `ClipPocketWin.Application/Services/ClipboardStateService.cs` dedupe + trim + persistence. |
| Incognito behavior | `ClipPocketApp.swift` | partial | Domain setting exists and `ClipboardStateService` skips storing when incognito; full UI/runtime toggle wiring pending. |
| Excluded apps behavior | `Utilities/ExcludedAppsManager.swift`, `Views/ExcludedAppsView.swift` | not implemented | Domain setting exists (`ExcludedAppIds`) but runtime filtering/UI manager still missing. |
| App startup orchestration | `ClipPocketApp.swift` launch flow | implemented | `ClipPocketWin/App.xaml.cs` initializes state + starts runtime monitor. |
| Runtime state debug observability | Source `print` logs | not implemented | Conditional logger was removed; currently only standard `ILogger` logs remain. |
| Global hotkey integration | `Utilities/GlobalHotkey.swift` | implemented | `ClipPocketWin/Runtime/PollingGlobalHotkeyService.cs` + `AppRuntimeService` wiring to panel toggle. |
| Tray icon/status item | `Views/StatusItemView.swift` + app delegate setup | not implemented | Pending Windows tray service and menu actions. |
| Floating panel show/hide runtime | `ClipPocketApp.swift` show/hide/toggle | partial | `ClipPocketWin/Runtime/WindowPanelService.cs` provides runtime show/hide/toggle; click-outside parity behavior still pending. |
| Edge monitor auto show/hide | `Utilities/MouseEdgeMonitor.swift` | implemented | `ClipPocketWin/Runtime/MouseEdgeMonitorService.cs` + `AppRuntimeService` delay-based edge enter/exit integration. |
| Click outside hide | `ClipPocketApp.swift` global mouse monitor | not implemented | Pending in WinUI runtime panel behavior service. |
| Auto-paste after selection | `ClipPocketApp.swift` `autoPasteIfEnabled` | not implemented | Setting exists; action pipeline and focus restore/simulated paste pending. |
| Full MVVM clipboard UI | `Views/ClipboardManagerView.swift` | not implemented | Current `MainWindow.xaml` is static prototype; migration pending. |
| Search and type filters | `Views/ClipboardManagerView.swift` | not implemented | Pending with MVVM migration. |
| Context menu actions | `Views/ClipboardManagerView.swift` | not implemented | Pending with MVVM migration. |
| Drag/drop to external apps | `Views/DraggableClipboardItemCard.swift` | not implemented | Pending WinUI drag/drop implementation. |
| Snippets section UX | `Views/SnippetCard.swift`, `Views/SnippetPlaceholderFormView.swift` | partial | Data model/repository/state exist; interactive UI flow pending. |
| Settings screen parity | `Views/SettingsView.swift` | not implemented | Settings persistence exists; full UI binding and commands pending. |
| Onboarding flow | `Views/OnboardingView.swift` | not implemented | Pending first-run onboarding in WinUI. |
| Update checker | `Utilities/UpdateChecker.swift` | not implemented | Pending GitHub release checker and UI wiring. |
| Quick actions: text transformations | `Utilities/TextTransformations.swift` | not implemented | Pending application service + UI commands. |
| Quick actions: QR/share | `Utilities/QuickActions.swift` | not implemented | Pending implementation in Windows stack. |
| Source app icon cache | `Utilities/SourceAppIconCache.swift` | not implemented | Source app metadata field exists; icon caching/rendering not yet ported. |
| Test coverage (unit/integration) | `ClipPocketTests`, `ClipPocketUITests` (minimal) | not implemented | No `ClipPocketWin.Tests.*` projects yet; planned next milestones. |
| Backup/export-import payloads | `Utilities/ClipboardBackupManager.swift`, settings actions | intentionally excluded | Explicitly removed from Windows scope by user request. |
