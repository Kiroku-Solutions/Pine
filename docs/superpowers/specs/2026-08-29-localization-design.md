# Localization — Design

| Field                  | Value                                                              |
|------------------------|--------------------------------------------------------------------|
| Document ID            | `PINE-SPEC-2026-08-29-LOCALIZATION`                                 |
| Status                 | Approved — pending implementation                                   |
| Closes                 | TD-45                                                               |
| Normative parent       | `docs/41-Localization.md`                                           |
| Related                | `docs/60-Production-Readiness-Audit.md` §237, §530 · `docs/38-Threat-Model.md` · `docs/20-Coding-Standards.md` |
| Created                | 2026-08-29                                                          |

> Written in English to match every other document under `docs/`. The
> audit (§373) flags the repo's English/Spanish mixing as team friction;
> this spec does not add to it.

---

## 1. Problem

The Settings screen offers a Display Language picker with English and
Spanish. Selecting Spanish changes nothing on screen.

## 2. Root cause

**The picker is not miswired. There is nothing on the other end of the
wire.** No localization layer was ever built; the control persists a
number and no code reads it for any purpose.

Evidence:

| Location | Finding |
|---|---|
| `src/Pine.Ui.Avalonia/Views/SettingsView.axaml:113` | ComboBox binds `SelectedIndex` to `SelectedLanguageIndex`. Correct. |
| `src/Pine.Ui.Avalonia/ViewModels/SettingsViewModel.cs:226-230` | `OnSelectedLanguageIndexChanged` does exactly one thing: persist `Appearance.Language` as `"0"` or `"1"`. |
| `src/Pine.Ui.Avalonia/ViewModels/SettingsViewModel.cs:101-105` | Reads the value back into the picker on load. The round trip is complete; the consumer is absent. |
| `grep CurrentUICulture` across `src/` | Zero assignments. The five hits are number/date formatting in `DiagnosticsViewModel` and the `es-EC`/`en-US` cascade in `OnnxNlpFieldExtractor`. |
| `.resx` / `IStringLocalizer` / localization package | None. No `EmbeddedResource`, `NeutralLanguage` or `SatelliteResourceLanguages` in any `.csproj`. |

The structural difference against the sibling control that does work:

```
OnSelectedThemeIndexChanged    -> ThemeManager.ApplyVariant(value)  + persist   two legs
OnSelectedPaletteIndexChanged  -> ThemeManager.ApplyPaletteIndex()  + persist   two legs
OnSelectedLanguageIndexChanged ->            (nothing)              + persist   one leg
```

Theme also re-applies at startup, from `UnlockViewModel.cs:136` via
`ThemeManager.ApplyFromSettingsAsync`. Language has neither the apply
leg nor the startup leg, because there is nothing to apply.

### 2.1 Size of the retrofit

Measured, with the method stated so the numbers can be re-derived:

- **At least 202 hardcoded UI literals** in 19 of the 27 `.axaml` files,
  counted by matching `Text=`, `Content=`, `Watermark=`, `ToolTip.Tip=`,
  `Header=` and `Title=` attribute values that do not open with a brace
  (which would make them bindings). This is a **floor**: the pattern
  misses element-syntax content and multi-line attributes. Largest
  offenders: `ScanReceiptView` (30), `ImportWizardView` (28),
  `SettingsView` (24), `SetupView` (21).
- **Roughly 58 user-facing strings in ViewModels**, matched approximately
  by sentence-shaped literals. The exact set is enumerated in phase 3.

## 3. Decisions

### 3.1 Agreed with the product owner

| # | Decision | Consequence |
|---|---|---|
| D1 | **Full scope**: infrastructure plus translation of every view. | Partial translation would leave most of the app reproducing the original complaint. |
| D2 | **Live switching, no restart.** | Requires a notifying indexer and a markup extension; ViewModels must hold key + arguments rather than a rendered sentence. |
| D3 | **Preference stored outside the vault**, so pre-unlock screens are translated too. | Costs one bit of plaintext metadata on disk. Accepted deliberately; see §3.4. |

### 3.2 Taken by the implementer

| # | Decision | Rationale |
|---|---|---|
| D4 | The out-of-vault store becomes the **single source of truth**; the vault's `Appearance.Language` key is retired. | Writing both is duplicated state waiting to diverge. A UI language is a per-user/per-machine preference, not per-vault data. |
| D5 | **Legacy values migrate.** A `"0"`/`"1"` found in a vault transfers to the new store the first time that vault is opened; the old key is then no longer written. | Nobody loses a choice they already made. |
| D6 | Persisted format becomes **BCP-47** (`"en"`, `"es"`), not an index. | An index breaks the day a language is inserted in alphabetical order. |
| D7 | **Neutral `es`**, not `es-ES`. | One file covers Spain and LatAm; .NET resolves `es-EC` — the locale the OCR extractor already works in — to `es` by itself. |
| D8 | **`en` and `es` only.** | `41-Localization` §14 forbids enabling a locale without a locale lead and a glossary; the eleven locales in §4.1 have neither. Adding a language later is adding one JSON file. |
| D9 | **Language names are never translated** — "English", "Español", each in its own language. | Standard practice; a user hunting for their language must be able to recognise it. |
| D10 | **ICU reduced to what is used (YAGNI).** .NET composite formatting plus a minimal `one`/`other` plural selector, which is sufficient for `en` and `es`. | A full ICU MessageFormat engine is disproportionate. Recorded as a conscious deviation in §11, not an oversight. |
| D11 | **`JsonDocument`, not `JsonSerializer<T>`.** | Reflection-based deserialization is exactly what WASM/iOS trimming breaks silently. Document traversal is immune and needs no source generator. |
| D12 | Registration ships as **`PineLocalizationServiceCollectionExtensions.AddPineLocalization()`**, called from all five composition roots. | House pattern, matching `PineLoggingServiceCollectionExtensions` and `PineDiagnosticsServiceCollectionExtensions`. Audit §12.25 found `DiagnosticsViewModel` registered on one head of five; per-head hand registration is a known trap in this repo. |

### 3.3 Document conflict, resolved

`60-Production-Readiness-Audit.md:530` prescribes `.resx` +
`IStringLocalizer` in TD-45's *Fix* column. `41-Localization.md` §5, the
normative specification, requires `.po`/`.json` files keyed by stable
string IDs, and §5.1 requires ICU MessageFormat.

**The normative document wins.** A *Fix* column is a reviewer's
suggestion; §5 is the standard. Two further reasons reinforce that
conclusion rather than motivate it:

- `.resx` ships translations as satellite assemblies. `Pine.Ui.Web`
  builds on `Microsoft.NET.Sdk.WebAssembly`, which trims on publish, and
  the Android and iOS heads are AOT targets. Satellite assemblies are a
  well-known silent casualty of trimming, and TD-07 records that those
  three heads **have never compiled in CI**. Adding a failure mode nobody
  can currently observe is a poor trade.
- JSON is reviewable in a pull request. `.resx` XML is not.

**Action:** TD-45's *Fix* column is amended to point at
`41-Localization` §5, so the next reader is not sent down the `.resx`
path.

### 3.4 Threat-model note for D3

`ui-preferences.json` sits unencrypted beside `%LOCALAPPDATA%/Pine/logs`,
which already holds unencrypted, PII-free data. The threat model's
information-disclosure entries concern PII in logs (TM-04), local
tampering (TM-40) and backup leakage (TM-41); a UI language preference is
none of these. The leak is one bit — "this user prefers Spanish" — and it
buys three translated pre-unlock screens. Recorded here so the trade is
visible rather than implicit.

## 4. Architecture

```
  en.json ─┐                          ┌─→ {loc:Tr Key}  ──→ every XAML string
           ├──→   Localizer   ────────┤    (markup extension → Binding "[Key]")
  es.json ─┘  (INotifyPropertyChanged)└─→ ILocalizer     ──→ ViewModel strings
                     ▲
                     │  SetLanguage("es") → PropertyChanged("Item[]")
                     │                    → every binding re-evaluates
                     │
     LanguagePreferenceStore  ←→  %LOCALAPPDATA%/Pine/ui-preferences.json
```

The mechanism that makes live switching work: `{loc:Tr Key}` does not
return a string, it returns a `Binding` to the localizer's **indexer**. A
single `PropertyChanged("Item[]")` invalidates every such binding at
once. No visual-tree walk, no view recreation.

This mirrors `ThemeManager`'s shape deliberately — **apply first, persist
second**, plus a re-apply at startup — so the two settings sitting on the
same panel behave identically.

## 5. Components

All new files live in `src/Pine.Ui.Avalonia/Localization/`.

| File | Responsibility |
|---|---|
| `ILocalizer.cs` | The contract: an indexer by key, a `Format` taking key plus arguments, the current culture, the available languages, `SetLanguage`, and a `LanguageChanged` event. Extends `INotifyPropertyChanged`. |
| `Localizer.cs` | The implementation. Holds the active dictionary plus the `en` fallback dictionary. Exposes a static `Instance`. |
| `LanguageOption.cs` | A record of language code plus native name — the picker's item type. |
| `TrExtension.cs` | The `MarkupExtension`. Returns a one-way `Binding` to the indexer, sourced at `Localizer.Instance`. |
| `LanguagePreferenceStore.cs` | Reads and writes the out-of-vault preference; degrades to in-memory when there is no writable location. |
| `PineLocalizationServiceCollectionExtensions.cs` | `AddPineLocalization()`, called by all five composition roots. |
| `en.json`, `es.json` | The string tables, as `AvaloniaResource` — the mechanism already used for `Assets`. |

### 5.1 Why a static instance *and* DI registration

XAML markup extensions cannot resolve services from the container, so
`TrExtension` needs a statically reachable instance. ViewModels and tests
need an injectable interface. Both point at the same object: the instance
is created once and registered as the `ILocalizer` singleton. The static
is a XAML necessity, not a design preference, and nothing except
`TrExtension` may use it.

### 5.2 Key resolution order

Active language, then `en`, then the key itself. In `DEBUG` an unresolved
key renders wrapped in exclamation marks so a missing string is loud
during development; in `RELEASE` it renders as the bare key, because a
user is better served by seeing `Settings.Title` than `!Settings.Title!`.

### 5.3 The picker's surface changes

`SettingsViewModel.SelectedLanguageIndex` (an `int`) is replaced by
`SelectedLanguage` (a `LanguageOption`), and `SettingsView.axaml` moves
from two hardcoded `ComboBoxItem` elements bound by `SelectedIndex` to an
`ItemsSource` over `ILocalizer.Available` bound by `SelectedItem`.

Named explicitly because it is a public surface change on a view model,
and because it is what makes D6 and D8 real: an index cannot survive a
reordered or extended language list, and the hardcoded items are
themselves two of the literals phase 2 has to remove.

## 6. Data flow

### 6.1 Startup

Before the first window is constructed, each head applies the stored
preference, falling back to the OS UI culture's two-letter code, and then
to `en` for anything unsupported. This ordering matters: the pre-unlock
screens are the first thing rendered, and D3 exists to have them
translated.

### 6.2 Language change

1. The settings view model calls `ILocalizer.SetLanguage(code)`.
2. The localizer loads the target dictionary, updates the current
   culture, raises the indexer `PropertyChanged` and `LanguageChanged`.
3. Every `{loc:Tr}` binding re-reads the indexer. ViewModels subscribed
   to `LanguageChanged` re-render from their held key and arguments.
4. Only then does the view model persist through the preference store.

Apply first, persist second, matching `ThemeManager`: a failed write must
not leave the user looking at a language they did not choose.

### 6.3 Legacy migration

On the first vault unlock after this ships, if the preference store holds
nothing and the vault holds `Appearance.Language`, the value maps across
(`"0"` to `en`, `"1"` to `es`). The vault key is then never written
again. Migration is one-way and idempotent: a populated store is never
overwritten from a vault.

## 7. Persistence

`%LOCALAPPDATA%/Pine/ui-preferences.json`, holding a single `language`
field with a BCP-47 code.

A dedicated file rather than an addition to an existing one, so a corrupt
preference can never take down logging or vault discovery.

**Degradation.** `Environment.GetFolderPath` for local application data
returns an empty string on WASM. `PineLoggingOptions.cs:65-73` already
handles that case, and this store follows the same precedent: no writable
location means the preference lives in memory for the session, and the
app falls back to the OS UI culture on next launch. The picker still
works; the choice simply does not survive a restart on that head.

## 8. Error handling

Localization is presentation. Nothing here may take down a screen.

| Failure | Behaviour |
|---|---|
| A JSON resource is missing or malformed | Log at warning, keep the previous dictionary, continue. `en` is embedded in the assembly, so total loss requires a corrupted build. |
| An unknown language code is requested | Fall back to `en`. Never throw. |
| The preference file is unreadable or corrupt | Treat as absent; fall back to OS culture. Overwrite on the next successful write. |
| The preference file cannot be written | The language still changes on screen. Log at warning. The user is not interrupted because a cosmetic write failed. |
| A key is missing from the active language | Resolution order in §5.2. Never throw, never render empty. |

## 9. Testing

The first two are the ones that matter; the rest cover the mechanism.

1. **Regression test for the original defect.** Changing the settings
   view model's selected language must move the localizer's current
   language. This is precisely the assertion that did not exist, which is
   why a picker that did nothing could ship and stay shipped.
2. **Guard test against TD-45 returning.** Walk every `.axaml` and fail on
   a hardcoded user-facing literal outside an explicit allow-list.
   `41-Localization` §14 states that a first-party UI string must not be
   hardcoded; this turns that sentence into an executable rule. The
   allow-list is the honest part: it is checked in with a reason per
   entry.
3. **Key parity.** `en.json` and `es.json` must carry identical key sets
   (`41-Localization` §13: a locale marked *complete* may not contain
   untranslated strings).
4. **Fallback chain.** A key absent from `es` resolves through `en`; a key
   absent from both resolves to itself.
5. **Legacy migration.** `"0"` and `"1"` map correctly; a populated store
   is not overwritten; migration runs once.
6. **Change notification.** `SetLanguage` raises the indexer notification
   and `LanguageChanged`, exactly once per change.
7. **Indexer binding path.** A key containing dots resolves through a real
   binding. See §13.

Tests land in `tests/Pine.Tests.Unit`, alongside the existing 423.

## 10. Phases

| # | Deliverable | Exit condition |
|---|---|---|
| 1 | Infrastructure: localizer, markup extension, preference store, migration, DI in five roots, picker rewired | The switch changes something real. Tests 1 and 3-7 green. |
| 2 | The 202-plus XAML literals extracted to `en.json` and translated into `es.json`, view by view | Guard test 2 green with an empty or justified allow-list. |
| 3 | The ~58 ViewModel strings converted to key plus arguments, re-rendering on `LanguageChanged` | No user-facing literal left in `ViewModels/`. |
| 4 | `STATUS.md` and audit updated: TD-45 closed, §3.3 conflict recorded | The audit no longer points the next reader at `.resx`. |

Phase 1 answers the original complaint. Phases 2 to 4 are what make the
answer true across the whole product.

### 10.1 Glossary gate before phase 2

`41-Localization` §8.4 requires a per-locale glossary. Roughly a dozen
financial terms need fixing before 260 strings bake a choice in: *vault*,
*account*, *statement*, *balance*, *transaction*, *budget*, *receipt*,
*ledger*, *opening balance*, *void*, *import*, *category*. These are
agreed with the product owner at the start of phase 2, not discovered
afterwards in 260 places.

## 11. Deviations from `41-Localization`

| Clause | Deviation | Reason |
|---|---|---|
| §5.1 ICU MessageFormat | Composite formatting plus a minimal `one`/`other` plural selector, not a full ICU engine | D10. Sufficient for `en` and `es`. Revisit when a locale with a richer plural system is enabled. |
| §4.1 eleven first-wave locales | Two locales shipped | D8. §14 forbids enabling a locale without a lead and a glossary. |
| §9 pseudo-locale build | Not in scope | §12. |

Declared rather than silently taken, so a later reader can tell a
decision from an omission.

## 12. Out of scope

- Pseudo-locale builds — `41-Localization` §9.
- RTL mirroring — §7. No RTL locale ships here.
- Locale-driven date, number and currency formatting. This is **TD-21**, a
  separate open item about ambiguous `dd/MM` versus `MM/dd` parsing whose
  severity the audit itself records as understated. Adjacent, genuinely
  important, and deliberately not bundled: folding a parsing-correctness
  fix into a UI-strings change would make both harder to review.
- A translation platform or translation memory — §8.3.
- Plugin-supplied strings — §12.

## 13. Risks to verify during implementation

Stated as unverified, because they are.

| Risk | Verification | Fallback |
|---|---|---|
| Avalonia's binding-path parser may not accept dots inside an indexer expression | Test 7, written first | Keys switch to `/` as separator |
| The asset-loading API may have moved in Avalonia 12 (the codebase is on 12.0.5; the pattern is familiar from 11) | Load a JSON in phase 1 | `EmbeddedResource` plus `Assembly.GetManifestResourceStream`, which is BCL-only and version-proof |
| `TreatWarningsAsErrors=true` in `Directory.Build.props` | The build is the check | — |
| The five composition roots drift, as they did in §12.25 | D12's shared extension, plus a test asserting the localizer resolves from a built container | — |

## 14. Revision history

| Version | Date       | Change |
|---------|------------|--------|
| 0.1     | 2026-08-29 | Initial design, approved in conversation. |
