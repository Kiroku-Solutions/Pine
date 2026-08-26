# 59 · Current Status — Where We Are

| | |
|---|---|
| **Updated** | 2026-08-26 |
| **Branch of record** | `main` |
| **Focus of this cycle** | Avalonia UI → production quality, runtime theming, receipt-flow fixes |

A living snapshot of the project's state so anyone can see, at a glance, what is
done, what is pending, and where we are heading. Update the date + relevant
sections whenever a meaningful chunk of work lands.

---

## 1. What just shipped (this cycle)

### 1.1 UI production pass (shared `Pine.Ui.Avalonia`)
Full audit of every screen (dashboard → live scanner) and a state-of-the-art pass:

- **Design system hardening** (`App.axaml`): fixed `RenderTransformOrigin` on all
  scale animations (no more diagonal jitter), added reusable styles
  (`Button.pine-outline`), input focus transitions, a `.reveal` entrance animation,
  and defined **previously-missing tokens** that were breaking rendering
  (`PineSurfaceElevated`, `PineAccentHover`, `PineError`, `PineWarning`, `PineSuccess`,
  `PineScrim`, `PineDangerContainer`).
- **Motion**: page transition on the root shell (`MainWindow`), animated scan line on
  the Live Scanner, card hover polish.
- **Layout fixes**: `ScrollViewer`s on forms that clipped (Add Transaction, Create
  Account, Setup, shell nav); overlap fixes in Scan Receipt (see 1.3).
- **Theming correctness**: replaced hardcoded colors that broke light mode
  (`Foreground="Red"`, alpha whites, budget reds) with theme tokens.
- **Best practices**: `async void` → safe `Task` on startup; `ConfidenceScore` made
  nullable; `Name[0]`/`Payee[0]` crash guarded via a shared `InitialLetter` converter;
  dead "Rules" nav button disabled; accessibility names + `IsDefault` on Unlock.

### 1.2 Runtime theming — "Zen-style" accent palettes
- Two independent axes: **variant** (Light / Dark / System) and **accent palette**.
- 5 palettes shipped (`Themes/*.axaml`), each with tuned light + dark values:
  **Amber** (default), **Amethyst** (violet), **Rose** (pink), **Ocean** (blue),
  **Emerald** (green). Also override FluentAvalonia `SystemAccentColor*` so native
  controls tint too.
- `Utils/ThemeManager.cs`: static, pure-Avalonia controller — swaps the palette in
  `Application.Resources.MergedDictionaries` and sets `RequestedThemeVariant`. **No
  DI, no per-OS-head code** → all heads (Windows/macOS/Web/Android/iOS) inherit it.
- Persistence via existing `ISettingRepository` (per-vault); applied on unlock.
- Picker UI: swatch selector in **Settings → Appearance**.

### 1.3 Receipt / scanner flow
- **Extracted Details overlap** fixed: right column is now
  `Grid RowDefinitions="*, Auto"` (scrollable fields + pinned action bar); the
  `DatePicker`/`Amount` pair was overflowing a narrow star column (DatePicker has a
  ~296px `MinWidth`) → **stacked vertically** in Scan Receipt and Add Transaction.
- **Image viewer**: reusable `Controls/ImageLightbox` (wheel-zoom, drag-pan, reset,
  Esc-to-close). Wired into Scan Receipt; drop-in ready for Live Scanner / Crop.

### 1.4 Database schema management
- **Fixed** `SQLite Error 1: no such table: TransactionLineItems`. Root cause:
  schema was created with `EnsureCreated()` (never incremental, never applies
  migrations) while `Migrate()` was never called anywhere.
- Introduced `Pine.Infrastructure/Data/PineDbInitializer.Migrate(...)`; vault creation
  (`SetupViewModel`) and unlock (`UnlockViewModel`) now use **EF migrations** as the
  single source of truth (creates history + applies pending on unlock).

---

## 2. Known tech debt / follow-ups

| # | Item | Where |
|---|---|---|
| 1 | `WeakReferenceMessenger` recipient lifecycle (closures capturing `this`, no deregistration, one `async void` handler) | **`docs/58-TechDebt-WeakReferenceMessenger-Lifecycle.md`** |
| 2 | Legacy vaults created via `EnsureCreated()` are not auto-healed (we chose "recreate the test vault"); a non-destructive self-healing initializer is designed but not built | 58 / this cycle |
| 3 | Dashboard drag/resize mutates `Width`/`Height` per pointer tick (relayout cost); acceptable for now | `DashboardView.axaml.cs` |
| 4 | Neutrals are not tinted toward the accent (palettes only swap accent family) — optional "fuller Zen" effect | `Themes/*` |
| 5 | Theme applies after unlock (settings are vault-scoped), so login screens use the default palette | `ThemeManager` / settings store |
| 6 | Broader accessibility pass (AutomationProperties, tab order) beyond the key controls done | UI-wide |

---

## 3. Suggested next steps

1. Address tech-debt #1 (messenger) — plan is written and ready to execute.
2. Optionally reuse `ImageLightbox` in Live Scanner / Crop.
3. Decide on legacy-vault healing vs. "recreate" policy for pre-release testers (#2).
4. Localization pass (there was Spanish text hardcoded in an English UI, now fixed —
   but no i18n framework yet; see `docs/41-Localization.md`).

---

## 4. Build / run notes

- Shared UI builds clean: `dotnet build src/Pine.Ui.Avalonia/Pine.Ui.Avalonia.csproj`.
- Desktop head: `dotnet run` from `src/Pine.Ui.Windows` (a running instance locks the
  output DLL — fully close it before rebuilding).
- Mobile heads (`Pine.Ui.Android`, `Pine.Ui.Ios`) require the android/ios workloads
  (`dotnet workload restore`); unrelated to the desktop build.
