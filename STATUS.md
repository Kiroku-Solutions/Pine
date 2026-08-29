# Project Status: Pine (SSoT)

This document tracks the real-time implementation progress based on the master plan (docs/56-IMPLEMENTATION-PLAN.md).

## Production readiness — read this first

The phase percentages below track **feature delivery against `docs/56-IMPLEMENTATION-PLAN.md`**. They
do not track whether the software is safe to put real money in, and the two have diverged: the
production-readiness audit (`docs/60-Production-Readiness-Audit.md`) rated this codebase
**2.3 / 5 — Not Production Ready** and opened 71 numbered debts. A remediation cycle is running
against it, and **that** is the number to read before shipping anything.

| | |
|---|---|
| Classification | **Not Production Ready** (audit §11) |
| Remediation | Audit **Phase 0: all 11 items worked** (iterations 1–10). Items 1 and 4–11 are ✅. **The phase is not clean**: item 2's encrypted export is still unreachable from the UI (TD-56), and item 3 **regressed after it was ticked** — `79c5364` removed the CI workflow's `push`/`pull_request` triggers, so it has never run and the Android/iOS heads have never compiled. See audit §12.22. **Phase 1: items 1–3 are ✅** (structured logging TD-14/TD-25, iteration 11; real diagnostics TD-15, iteration 12; envelope encryption TD-06, iteration 13). Item 3 shipped password change but **not** biometric unlock (TD-87) and re-wraps rather than rotates the data key (TD-86). Item 4 (per-vault encrypted ML model, TD-16) is next |
| Remaining before the branch is defensible | **TD-07**: re-arm CI — the largest single risk, since every claim above was verified on one machine by hand and the Android/iOS heads have now been edited blind in two consecutive iterations · **TD-85**: four of the five heads throw on navigation buttons the shell renders unconditionally · then TD-83, TD-78, TD-75, TD-81 |
| Test suite | 28 → **542** (423 unit + 119 integration), both green, verified on this machine at iteration 12. TD-74's "98/99" was a misdiagnosis: the integration project had not **compiled** since `f691ba8` — zero tests executed, not one failing. Fixed and reclassified; see audit §12.20 |
| Last remediation | 2026-08-29 — Phase 1 item 3: envelope encryption (TD-06). Vault header v2 carries the data key wrapped with AES-256-GCM under the Argon2id-derived auth key, bound to every other header field; `ChangeVaultPasswordCommand` re-wraps under a fresh salt without re-encrypting the database or closing the session. Pre-envelope vaults (v1, `pine.salt`) are **refused, not adopted** — a deliberate breaking change taken while no vault exists in production. Nine falsification mutations, all killed; 474 unit + 150 integration tests green. Registered and **not** fixed: TD-86 (the data key is never rotated), TD-87 (no platform secure storage, so no biometric), TD-88 (WASM has no AES-GCM). See audit §12.27 |
| Previous remediation | 2026-08-29 — Phase 1 item 2: the Diagnostics screen stopped fabricating (TD-15). Real log tail, real database probe, real version; `Export DIB` and `Seed Demo Data` removed rather than reimplemented. Found and fixed en route: `DiagnosticsViewModel` was registered on one head of five. Found and **not** fixed: TD-85, the same defect across four other pages. See audit §12.25 |

Note the numbering collision: the audit's "Phase 0" is a *remediation* phase and has nothing to do
with the implementation plan's "Phase 0 (Foundations)" below.

---

## Overall Progress

- **Phase 0 (Foundations):** ✅ 100% Completed
- **Phase 1 (v0.1 alpha - Core Engine):** ✅ 100% Completed
- **Phase 2 (v0.2 beta - Imports & AI Baseline):** ✅ 100% Completed
- **Phase 3 (v0.3 beta - Budgets, goals, recurring):** 🟡 In Progress
  - **Pillar 1 (Budgets MVP):** ✅ Completed (Core, Infra, App, UI connected)
  - **Pillar 2 (Savings Goals):** 🔴 Not Started
  - **Pillar 3 (Recurring):** 🔴 Not Started
- **Phase 4 (v0.4 beta - Investments):** 🔴 Pending
- **Phase 5 (v0.5 beta - Sync, plugins, marketplace):** 🔴 Pending
- **Phases 6-10 (Beta to GA):** 🔴 Pending

---

## Current Architecture Baseline
- **Cross-Platform UI (Pine.Ui.Avalonia)**: Contains the pure UI layer using the MVVM pattern with Fluent Design. Agnostic to OS.
- **Composition Root (Pine.Ui.Windows)**: Host executable that runs Avalonia on Windows Desktop.
- **Hexagonal Architecture**: Clean separation between Domain (Core), Application (Use Cases), Infrastructure (Persistence), and UI.
- **Domain-Driven Design (DDD)**: Strong modeling using AggregateRoot, Entity, ValueObject (Money, CurrencyCode), and Domain Events.
- **CQRS via MediatR**: Commands (state changes) and Queries (reads) are segregated.

## Phase 1 Detail (Core Engine)

### ✅ Implemented (Vertical Slice 1 - Core & Security)
- **Cryptography & Persistence:**
  - Argon2id Key Derivation implemented with high-security parameters.
  - Encrypted database using SQLCipher configured via Entity Framework Core.
- **Core Domain:**
  - Account: Base aggregate with support for subtypes (Cash, Bank, Card, Investment, Crypto, Custom).
  - Transaction & TransactionSplit: Implemented with Money value object balance validation.

### ✅ Implemented (Vertical Slice 2 - Domain Expansion)
- Category and Tag entities for hierarchical budgeting.
- Attachment entity (for encrypted receipts).
- Rule Engine (Rule entity for auto-categorization).
- System Configuration (Setting entity for locale, theme, telemetry, etc.).

### ✅ Implemented (Vertical Slice 3 - UI / Flow)
- Welcome/Setup screen in Pine.Ui.Avalonia.
- Form to capture/create the SQLite vault master password.
- MVVM Navigation Routing (ViewLocator pattern).
- **S-Tier UI Overhaul**: 
  - Implementation of **Glassmorphism** (ExperimentalAcrylicBorder) on sidebars and dialogs.
  - Complete integration of the **Squirrel Orange Palette** (Dark & Light themes) via CustomAccentColor and dynamic dictionaries.
  - Premium micro-animations and drop-shadows on Dashboard and Transactions cards.
- **Infrastructure Fixes**: Resolved EF Core shadow key generation issues for Value Objects (Money) utilizing EF 8+ .ComplexProperty().

### ✅ Implemented (Vertical Slice 4: Dashboard & Settings Panel)
- **Goal**: Implement the core application shell, a responsive dashboard skeleton with drag-and-drop mechanics, and a robust user settings panel.
- **Components**:
  - DashboardViewModel / DashboardView.axaml (WrapPanel layout).
  - Custom Canvas-based drag and drop overlay for widgets.
  - Custom iOS-style "jiggle" micro-animation via UI-thread direct transform injection.
  - Unified Interface (IDashboardWidgetViewModel) and 12 baseline placeholder widget ViewModels.
  - Custom dynamic resize Thumb for 2D fluid reflowing of dashboard items.
  - SettingsViewModel / SettingsView.axaml implementing configuration tabs for Appearance, Language, and Vault Security.
  - DiagnosticsViewModel / DiagnosticsView.axaml. **Corrected, iteration 12 (TD-15):** this line claimed real-time log tailing, environment metrics and DIB export hooks. None of the three were true when it was written — the log lines were seven literals in the constructor, the database status was the constant `"Connected (Healthy)"`, and `ExportDib` was one log line plus `// To be implemented`. The log tail and the environment metrics are now real (`ILogTailReader`, `IDatabaseHealthProbe`, `IRuntimeInfoProvider`); the DIB export was **removed**, not implemented, and is tracked as TD-84. See audit §12.25.
  - **Architecture Bugfix**: Overhauled MVVM Toolkit 8.3 [ObservableProperty] syntax using partial properties and an initialization flag to prevent lifecycle theme-reset glitches on view creation.
- **Design System**: 
  - Overhauled the global UI aesthetic, shifting from default Fluent Avalonia colors to a highly polished **S-Tier custom palette**. 
  - Implemented deep umbers for Dark Mode ("Midnight Pine") and subtle warm tones for Light Mode ("Morning Wood"), rooting the entire application's aesthetic in the core Pine orange logo identity.
  - Diagnostics Panel utilizes a custom "hacker terminal" aesthetic for logs while maintaining Pine UI compliance for metrics cards. The aesthetic survived iteration 12; the fabricated content behind it did not.
- **Security Unlock Flow**: Implemented UnlockViewModel and dynamic startup routing in MainViewModel. Fixed critical SQLCipher integration bugs by enforcing SQLitePCL.Batteries_V2.Init() y migrando PineDbContext DI scoping a Transient para garantizar ciclos de vida correctos.

### ✅ Implemented (Pilar 3 - Formularios, Diálogos y Reactividad UI)
- **Data Entry**: CreateAccountViewModel y AddTransactionViewModel conectados a CQRS.
- **Pub/Sub Reactivity**: Integración de WeakReferenceMessenger y DataChangedMessage.
- **UI Subscribers**: Widgets se recargan silenciosamente en background al detectar cambios en los datos.

### ✅ Implemented (Pilar 4 - Cierre de Fase 1 SSoT Compliance)
- **First-Run UX**: Reestructuramos SetupViewModel y la vista con selección paso a paso: Modo de almacenamiento y Opt-In de Telemetría (UC-01).
- **Settings Panel persistente**: Implementamos la entidad Setting, ISettingRepository, e integramos con SettingsViewModel para guardar y cargar configuraciones desde SQLite.
- **Entidades de Dominio**: Completamos y verificamos la inclusión de las entidades Category y Attachment en el Core y EF Core para dar soporte a la Fase 2 (IA y Recibos).

## Phase 2 Detail (Imports & AI Baseline)

### ✅ Implemented (AI Baseline Engine)
- **On-Device OCR**: Integrated PaddleOCR via ONNX Runtime (`PaddleOcrEngine`) for DBNet and CRNN models.
- **Receipt Detection**: YOLOv8-nano integration via ONNX Runtime (`ReceiptDetector`) with fast tensor extraction.
- **Layout & Field Extraction**: Spatial heuristic grouping (Y-axis logic) and advanced `[GeneratedRegex]` for multi-currency amounts and dates (`HeuristicFieldExtractor`).
- **Categorisation**: Fully offline, deterministic ML.NET pipeline using `SdcaMaximumEntropy` (`MlNetCategorizer`).
- **Orchestration**: Wired up all components under `DefaultLocalAiProvider` respecting the `IAIProvider` interface in `Pine.Core`.

### ✅ Implemented (Receipt Scanning & Vision Enhancements)
- **Live Camera Scanner**: Implemented Avalonia UI for real-time camera feed capture (`LiveScannerViewModel`) with camera selection via COM DirectShow.
- **YOLOv8 Segmentation**: Added custom ONNX tensor multiplication and `Cv2.ApproxPolyDP` for perfect quadrilateral receipt extraction.
- **Image Enhancement Pipeline**:
  - UI Stream: Applies `Cv2.Filter2D` Sharpening kernel for a crisp, vibrant visual feedback.
  - OCR Stream: Applies `Cv2.GaussianBlur` and `Cv2.AdaptiveThreshold` for perfect B&W Binarization (CamScanner style) maximizing OCR accuracy.
- **Memory Safety**: Solved high-frequency GC race conditions in Avalonia's `WriteableBitmap` via explicit unmanaged heap `.Lock()` deep copying.

### ✅ Implemented (Multi-Vault Architecture)
- **VaultManager**: Singleton service managing `vaults.json` and generating isolated vault directories per profile.
- **Cross-Platform DI**: Dynamic database path resolution in all OS entry points (`Windows`, `Macos`, `Android`, `Ios`, `Web`).
- **VaultSelection UX**: Premium glassmorphic UI (`VaultSelectionView.axaml`) for choosing or creating separate personal/business finance vaults.

### ✅ Implemented (Imports Engine)
- **Imports Engine UX**: Developed `ImportWizardViewModel` and its wizard interface for parsing files and mapping CSV columns intuitively.
- **AI Integration**: Connected `MlNetCategorizer` to the CQRS layer via `CategorizeTransactionQuery`, providing fully automated category prediction in the Wizard's visual preview and final import handler.
- **Deduplication Filter**: Implemented exact-match (Account, Date, Amount) SQLite transaction filtering in `ImportTransactionsCommandHandler` to prevent accidental finance duplication.

---
*Last updated: July 12, 2026*
