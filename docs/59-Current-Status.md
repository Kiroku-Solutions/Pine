# 59 · Current Status — Where We Are

| | |
|---|---|
| **Updated** | 2026-08-28 |
| **Branch of record** | `main` |
| **Focus of this cycle** | Production-readiness remediation (`docs/60`), Phase 0 — iteration 6 closed item 8 (TD-02) |

A living snapshot of the project's state so anyone can see, at a glance, what is
done, what is pending, and where we are heading. Update the date + relevant
sections whenever a meaningful chunk of work lands.

---

## 1. What just shipped (this cycle)

### 1.0 Remediation iteration 6 — corrections and balance repair (TD-02)

The last original P0 data-integrity finding. Until this landed, Pine could record transactions
atomically, import them without losing any and split them correctly — and could not **correct a single
one of them**. Any bad record, from any cause, was permanent, and a balance that had drifted could not
be recomputed.

- **Void, not delete, and not edit.** A transaction is marked void with a required reason; the row, its
  splits and its import key all survive, so the correction is auditable. Its effect on the account
  balance is undone in the *same* database transaction, so the flag and the balance can never disagree.
  Voiding one leg of a transfer voids the other and moves both balances.
- **Balance repair.** Any account can be recomputed from its opening figure plus every transaction
  still in force, and the result is *reported* — old figure, new figure, drift, rows counted — rather
  than acknowledged. The sum is done in .NET `decimal`: SQLite stores these values as TEXT and would
  coerce a pushed-down `SUM` to floating point (TD-31).
- **`Account` now records its opening balance**, which it never did. Without it, "recompute the balance
  from the ledger" was arithmetically impossible. The migration deliberately does **not** backfill it
  for existing accounts — deriving it from the current balance would bake any existing drift in as the
  account's origin. Those accounts get an honest, on-screen "this first check could not detect drift".
- **UI**: per-row *Void* with confirmation, struck-through voided rows with a badge and the reason in a
  tooltip, and **Settings → Data Integrity** to check any account against its ledger.
- **Tests 170 → 240**, with four falsification mutations proving the guards are load-bearing.
- Found and fixed along the way: an existing migration test that had been passing for the wrong reason
  for two iterations (EF writes `Guid` keys upper-cased and SQLite compares TEXT case-sensitively), and
  a stale schema-version constant. See audit §12.14.

**Not done, deliberately:** editing a transaction (TD-68), undoing a void (TD-69).

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

**The remediation backlog takes precedence over everything below it** — see `docs/60` §10. The next
three items close audit Phase 0:

1. **TD-11** — password entropy floor and idle auto-lock. The encryption is currently defeatable at the
   setup screen rather than at the vault, and this is the last Phase 0 item whose cost grows once real
   vaults exist.
2. **TD-18 / TD-19** — a scale and rounding policy for `Money`, plus line-item reconciliation. These are
   the only money-path invariants with no test, and both need designing rather than back-filling:
   `Money` has no scale policy at all today, so "splits sum to the total" holds only because nothing
   rounds yet.
3. **TD-47 / TD-48** — remove the seeded fictional $1,000 account and the `CloudSync` setting nothing
   reads.

Then, from this cycle's own list:

4. Address tech-debt #1 (messenger) — plan is written and ready to execute.
5. Optionally reuse `ImageLightbox` in Live Scanner / Crop.
6. Decide on legacy-vault healing vs. "recreate" policy for pre-release testers (#2).
7. Localization pass (there was Spanish text hardcoded in an English UI, now fixed —
   but no i18n framework yet; see `docs/41-Localization.md`).

---

## 4. Build / run notes

- Shared UI builds clean: `dotnet build src/Pine.Ui.Avalonia/Pine.Ui.Avalonia.csproj`.
- Desktop head: `dotnet run` from `src/Pine.Ui.Windows` (a running instance locks the
  output DLL — fully close it before rebuilding).
- Mobile heads (`Pine.Ui.Android`, `Pine.Ui.Ios`) require the android/ios workloads
  (`dotnet workload restore`); unrelated to the desktop build.
