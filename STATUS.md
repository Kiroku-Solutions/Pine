# Project Status: Pine (SSoT)

This document tracks the real-time implementation progress based on the master plan (docs/56-IMPLEMENTATION-PLAN.md).

## Overall Progress

- **Phase 0 (Foundations):** ✅ 100% Completed
- **Phase 1 (v0.1 alpha - Core Engine):** ✅ 100% Completed
- **Phase 2 (v0.2 beta - Imports & AI Baseline):** ✅ 100% Completed
- **Phase 3 (v0.3 beta - Budgets, goals, recurring):** 🔴 Pending
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
  - DiagnosticsViewModel / DiagnosticsView.axaml implementing real-time log tailing, environment metrics, and DIB (Diagnostic Information Bundle) export hooks.
  - **Architecture Bugfix**: Overhauled MVVM Toolkit 8.3 [ObservableProperty] syntax using partial properties and an initialization flag to prevent lifecycle theme-reset glitches on view creation.
- **Design System**: 
  - Overhauled the global UI aesthetic, shifting from default Fluent Avalonia colors to a highly polished **S-Tier custom palette**. 
  - Implemented deep umbers for Dark Mode ("Midnight Pine") and subtle warm tones for Light Mode ("Morning Wood"), rooting the entire application's aesthetic in the core Pine orange logo identity.
  - Diagnostics Panel utilizes a custom "hacker terminal" aesthetic for logs while maintaining Pine UI compliance for metrics cards.
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
