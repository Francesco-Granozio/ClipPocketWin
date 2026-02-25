# ClipPocketWin - Documento di implementazione da zero (porting funzionale completo)

## 1. Obiettivo

Implementare ClipPocketWin da zero con parita funzionale completa rispetto alla app sorgente macOS (ClipPocket), mantenendo:

- stessa esperienza utente,
- stesse regole di dominio,
- stessi limiti operativi,
- stessa affidabilita su persistenza, privacy e sicurezza.

Questo documento descrive la strategia architetturale e gli step implementativi per consegnare un prodotto production-ready con design moderno e pulito.

## 2. Scope funzionale (baseline obbligatoria)

La prima release Windows deve gia includere tutte le funzionalita del porting:

1. Clipboard history persistente con deduplica.
2. Sezioni UI: Pinned, Recent, History, Snippets.
3. Ricerca full-text e filtri per tipo contenuto.
4. Type detection: text, code, url, email, phone, json, color, image, file, rich text.
5. Copy selection con auto-paste opzionale verso app precedente.
6. Pinned items con limite e persistenza.
7. Snippets con placeholder `{name}` e form di compilazione.
8. Quick actions e text transformations.
9. Encryption history at-rest con migrazione dati.
10. Incognito mode.
11. Excluded apps.
12. Global hotkey configurabile.
13. Tray icon/menu + panel floating.
14. Auto show/hide su edge con delay configurabili.
15. Settings completi (tema, density, font scale, limits, startup, etc).
16. Onboarding first-run.
17. Update checker (GitHub releases).
18. Packaging e distribuzione Windows (MSIX/installer).

## 3. Principi architetturali

### 3.1 Stile

- Clean Architecture (dipendenze verso l interno).
- MVVM in Presentation.
- Result Pattern per error handling esplicito.
- Infrastruttura adapter-based per API OS-specifiche.
- Event-driven interno per sincronizzare runtime e UI.

### 3.2 Struttura soluzione

- `ClipPocketWin` (Presentation WinUI 3)
- `ClipPocketWin.Application` (use case e orchestrazione)
- `ClipPocketWin.Domain` (modello e regole pure)
- `ClipPocketWin.Infrastructure` (file system, crypto, clipboard API, hotkey, tray, edge monitor, updates)
- `ClipPocketWin.Shared` (ResultPattern, tipi condivisi)
- `ClipPocketWin.Tests.Unit`
- `ClipPocketWin.Tests.Integration`
- `ClipPocketWin.Tests.UI` (facoltativo ma consigliato)

### 3.3 Vincoli di qualita

- Nessuna logica business in code-behind UI.
- Nessun throw non gestito nei percorsi applicativi attesi.
- Tutte le operazioni I/O e runtime ritornano `Result` / `Result<T>`.
- Moduli sostituibili tramite interfacce.

## 4. Modello dominio (source of truth)

Entita principali:

- `ClipboardItem`
- `PinnedClipboardItem`
- `Snippet`
- `ClipPocketSettings`
- `KeyboardShortcut`

Regole e limiti chiave (parita sorgente):

- max history hard limit: 500
- max pinned: 50
- max snippets: 200
- image size persistita: <= 1 MB
- deduplica per contenuto equivalente e tipo

Invarianti:

- item null non ammesso
- shortcut valida e normalizzata
- placeholder snippet coerenti

## 5. Contratti applicativi e infrastructural boundaries

Interfacce minime:

- `IClipboardStateService`
- `IClipboardHistoryRepository`
- `IPinnedClipboardRepository`
- `ISnippetRepository`
- `ISettingsRepository`
- `IClipboardEncryptionService`
- `IClipboardMonitor`
- `IGlobalHotkeyService`
- `ITrayService`
- `IEdgeMonitorService`
- `IWindowPanelService`
- `IAutoPasteService`
- `IUpdateService`

Ogni chiamata deve essere modellata con Result Pattern:

- successo: `Result.Success()` / `Result<T>.Success(value)`
- errore atteso: `Result.Failure(new Error(...))`
- eccezioni inattese convertite in `ErrorCode.UnknownError` con context

## 6. Error model (Result Pattern)

`ErrorCode` deve essere organizzato a range:

- `0-99`: generic
- `1000-1499`: domain clipboard/settings/snippets
- `2000-2399`: workflow application
- `3000-3399`: storage/serialization
- `3200-3299`: crypto
- `3400-3499`: startup/host

Linee guida:

- includere sempre messaggio contestuale (operazione + path + modulo)
- preservare `Exception` originale in `Error`
- `OperationCanceledException` gestita separatamente

## 7. Persistenza e formato dati

Directory base:

- `%LocalAppData%/ClipPocketWin/`

File:

- `clipboardHistory.json`
- `clipboardHistory.encrypted`
- `pinnedItems.json`
- `snippets.json`
- `settings.json`
- `.clippocket_encryption_key`

Requisiti:

- serializzazione robusta con fallback safe
- migrazione automatica tra plain/encrypted
- guardrail su file corrotti, payload vuoti, oversize

## 8. Flussi runtime principali

### 8.1 Startup

1. Build container DI.
2. Caricamento settings.
3. Caricamento data store (history/pinned/snippets).
4. Inizializzazione servizi runtime (clipboard monitor, hotkey, tray, edge monitor).
5. Avvio finestra panel host.
6. Non-fatal errors -> app continua in degraded mode con log.

### 8.2 Clipboard capture

1. Evento clipboard change.
2. Lettura payload e classificazione tipo.
3. Validazione dimensioni/formati.
4. Deduplica.
5. Persistenza (se non incognito e history enabled).
6. Broadcast state update alla UI.

### 8.3 Item selection + auto-paste

1. Utente seleziona item.
2. Item scritto nella clipboard in formato nativo.
3. Se auto-paste attivo: restore focus app precedente + invio Ctrl+V.
4. Chiusura panel (configurabile).

### 8.4 Encryption toggle

1. Utente cambia toggle encrypt.
2. Conferma utente.
3. Migrazione file history atomica.
4. Aggiornamento settings.
5. Verifica post-migrazione.

## 9. UI/UX e comportamento panel

Requisiti panel:

- posizione bottom, floating, sempre accessibile
- apertura via hotkey/tray/edge
- auto-hide con delay configurabile
- click outside -> hide
- keyboard navigation completa

Requisiti contenuti:

- card rendering tipizzato per ogni tipo clipboard
- context menu completo
- drag and drop verso app esterne
- feedback visivo per azioni importanti (copy, pin, delete)

## 10. Sicurezza e privacy

- dati solo locali, nessun invio cloud non esplicito
- excluded apps con default sensibili
- incognito mode hard stop sulla cattura
- encryption history con chiave locale protetta
- logging senza contenuti sensibili clipboard (solo metadata)

## 11. Strategia test

### 11.1 Unit test

- dominio (invarianti, limiti, placeholder)
- type detection
- text transformations
- error mapping Result/ErrorCode

### 11.2 Integration test

- repository read/write/clear
- encryption/decryption/migration
- startup init flow con storage degradato

### 11.3 UI/interaction test

- opening panel (hotkey/tray/edge)
- search/filter/navigation
- copy/autopaste
- settings persistence

### 11.4 Parity test matrix

Checklist feature-by-feature con esito:

- implemented
- partially implemented
- not implemented
- intentionally different (con motivazione)

## 12. Roadmap implementativa da zero (con deliverable)

## Fase 0 - Foundation

Deliverable:

- solution layout completa
- DI bootstrap
- Result Pattern e ErrorCode definiti
- pipeline CI base (restore/build/format)

Exit criteria:

- build verde
- architettura compilante senza feature

## Fase 1 - Dominio e storage core

Deliverable:

- modelli dominio completi
- repository file-based
- settings load/save
- limiti hard enforce

Exit criteria:

- test integrazione storage verdi

## Fase 2 - Clipboard ingestion engine

Deliverable:

- monitor clipboard Windows
- classificazione tipi
- deduplica + persistenza

Exit criteria:

- ingest end-to-end stabile

## Fase 3 - Panel runtime services

Deliverable:

- hotkey globale
- tray icon/menu
- panel show/hide
- edge monitor + delays

Exit criteria:

- panel controllabile da tutti i trigger

## Fase 4 - UI completa (parita funzionale)

Deliverable:

- sezioni Pinned/Recent/History/Snippets
- search/filter chips
- card tipizzate
- context menu + drag/drop

Exit criteria:

- flusso utente completo senza workaround

## Fase 5 - Snippets e quick actions

Deliverable:

- snippet CRUD + placeholders
- text transformations
- quick actions principali

Exit criteria:

- snippet flow completo e persistente

## Fase 6 - Privacy/security

Deliverable:

- incognito
- excluded apps
- encryption toggle + migration

Exit criteria:

- test di recovery e migrazione verdi

## Fase 7 - Onboarding/settings/update

Deliverable:

- onboarding first-run
- settings completi
- update checker GitHub

Exit criteria:

- configurazione completa persistita

## Fase 8 - Hardening e release

Deliverable:

- tuning performance
- logging/telemetria locale
- packaging e script release
- parity report finale

Exit criteria:

- go-live checklist completa

## 13. Piano di lavoro parallelo (N stream)

Numero stream `N` dinamico in base ai task indipendenti.

Template consigliato:

- Stream A: Domain + Shared
- Stream B: Infrastructure storage/crypto
- Stream C: Runtime services OS
- Stream D: Presentation MVVM/UI
- Stream E: Test automation + parity matrix

Regola:

- task con dipendenze forti restano sequenziali
- task indipendenti viaggiano in parallelo

## 14. Definition of Done (release gate)

Una release e accettata solo se:

1. Build, format e test verdi.
2. Nessuna feature baseline mancante.
3. Error handling consistente su Result Pattern.
4. Nessun crash riproducibile nei flussi core.
5. Privacy requirements rispettati.
6. Parity matrix compilata e approvata.

## 15. Deliverable finali richiesti

- codice completo su architettura clean
- test suite con coverage dei flussi critici
- parity report con evidenza feature 1:1
- documentazione operativa (build/run/release)

---

Questo documento e pensato come blueprint esecutivo per implementare il prodotto da zero con parita completa del porting, senza passare da una MVP ridotta.
