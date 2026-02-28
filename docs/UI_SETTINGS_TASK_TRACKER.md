# UI Settings Porting Tracker

Last update: 2026-02-27

## Scope
- Align WinUI panel structure with macOS reference.
- Add section-specific empty states for `Pinned`, `Recent`, `History`.
- Improve file card icon fidelity with Windows APIs.
- Add syntax highlighting for code cards.
- Implement a dedicated settings window with requested sections and behaviors.
- Exclude encryption/snippets features from the new settings UX.

## Task List
| ID | Task | Status | Notes |
|---|---|---|---|
| T1 | Refactor panel header layout and transparency | DONE | Header aligned to reference and acrylic transparency reduced (`MinTintOpacity=0.12`). |
| T2 | Empty states by section | DONE | Distinct empty card copy/icon for pinned/recent/history plus filtered-results fallback. |
| T3 | File card Windows type icon | DONE | Shell icon lookup via `SHGetFileInfo` with extension fallback and cache. |
| T4 | Code card syntax coloring | DONE | Added `RichTextBlock` token highlighting for code cards. |
| T5 | Settings window UI and navigation | DONE | Added dedicated `SettingsWindow` opened by gear button. |
| T6 | Launch at login setting behavior | DONE | Added HKCU Run-key registration helper and toggle wiring. |
| T7 | Keyboard shortcut record + reset default Ctrl+o-grave | DONE | Shortcut recording + reset implemented with live hotkey update. |
| T8 | Data management settings | DONE | History limit toggle/value + clear history action implemented. |
| T9 | Privacy settings and excluded apps manager | DONE | Incognito toggle and excluded apps dialog (running apps + manual add). |
| T10 | About + placeholder updates button | DONE | About section and fake update button added. |
| T11 | Documentation parity updates | TODO | Update `PORTING_STATUS` and `PARITY_MATRIX`. |
| T12 | Validation | DONE | `dotnet build` and `dotnet format --verify-no-changes` executed. |

## Progress Log
- 2026-02-27: Tracker created and baseline tasks registered.
- 2026-02-27: Implemented panel/header redesign, section empty states, file icons via Shell API, code syntax highlighting, and full requested settings surface.
- 2026-02-27: Validation run completed (`build` green; formatting check clean).
- 2026-02-27: UI refinement pass from user feedback:
  - settings window reduced and compacted,
  - settings and panel now hide minimize/maximize controls (close only),
  - top bar settings icon clipping fixed,
  - filter row width aligned to search box width.
- 2026-02-27: Settings visual pass (requested palette/alignment updates):
  - settings header background set to `#1E1E1E`,
  - settings scroll area background set to `#23282B`,
  - section card borders/backgrounds unified to `#1F2324`,
  - toggles aligned right, with Apple-like blue/gray palette and hidden `On`/`Off` text,
  - `Clear History` action styled in red.
