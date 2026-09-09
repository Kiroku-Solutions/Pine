# 07 — Functional Requirements

| Field            | Value                                                                                |
|------------------|--------------------------------------------------------------------------------------|
| Document ID      | `PINE-DOC-07-FUNCTIONAL-REQUIREMENTS`                                               |
| Status           | Partially implemented — no FR-to-code traceability exists                            |
| Reconciled       | 2026-08-31 · Some requirements are met and some are not, and nothing links an `FR-*` id to the code or the test that would show which (`19-Traceability-Matrix` is aspirational). Read requirement by requirement, not as a whole. |
| Owner            | Engineering Review Board                                                             |
| Created          | 2026-07-08                                                                           |
| Last Updated     | 2026-07-08                                                                           |
| Specification Standard | IEEE 29148 (functional requirements)                                          |
| Glossary         | `PINE-DOC-03-GLOSSARY`                                                               |
| Prerequisites    | `00-Vision`, `01-Business-Requirements`, `02-Product-Scope`, `03-Glossary`, `04-Stakeholders`, `05-Personas`, `06-Use-Cases` |
| Related Documents| `08-NFR`, `10-Domain-Model`, `11-Data-Model`, `12-Plugin-Architecture`, `13-AI-Architecture`, `14-Synchronization`, `19-Traceability-Matrix`, `46-Features`, `47-User-Stories`, `48-Acceptance-Criteria`, `49-Test-Cases` |

---

## 1. Purpose

Specify, in normative language (RFC 2119), the *functional*
behaviour Pine must deliver. Each requirement is uniquely identified
(`FR-*`), traceable to a SCOPE-* item, a use case (`UC-*`) and a
persona (`PERS-*`), and linked to acceptance criteria and test cases
in the corresponding downstream documents.

## 2. Scope

In scope: every behaviour needed to fulfil the product scope in
`02-Product-Scope` §8.

Out of scope: quality attributes (performance, security, etc.) →
`08-NFR`, `16-Quality-Attributes`; architecture and technology choices →
`09-System-Architecture`; data shapes → `11-Data-Model`.

## 3. Definitions

| Term     | Working definition                                                                                  |
|----------|------------------------------------------------------------------------------------------------------|
| FR       | Functional Requirement, identified `FR-*`.                                                            |
| Actor    | Persona (`PERS-*`) or external system.                                                                |
| System   | Pine, including its core, plugins and on-device AI.                                                  |
| Normative keyword | RFC 2119 keyword (MUST, MUST NOT, SHOULD, SHOULD NOT, MAY).                             |
| Stub     | A requirement whose implementation is a no-op for the relevant release window but whose interface is shipped. |

> **Assumption FR-A-1.** Normative keywords follow RFC 2119. "MUST"
> means a release-blocking requirement. "SHOULD" means a
> release-blocking requirement unless an explicit ADR waives it.

## 4. Requirement Template

| Field           | Description                                                                       |
|-----------------|-----------------------------------------------------------------------------------|
| ID              | `FR-*`                                                                            |
| Title           | Verb-noun                                                                         |
| Source SCOPE    | `SCOPE-*`                                                                         |
| Use case        | `UC-*`                                                                            |
| Persona         | `PERS-*`                                                                          |
| Statement       | One normative statement (RFC 2119 keyword)                                       |
| Rationale       | Why this is required                                                              |
| Acceptance      | Pointer to `FR-AC-*` in `48-Acceptance-Criteria`                                  |
| Test cases      | Pointer to `TC-*` in `49-Test-Cases`                                              |
| Traced principles | Vision principles invoked (P-01..P-13)                                         |
| Status          | Proposed / Approved / Deferred / Withdrawn                                       |

## 5. Cross-Cutting Functional Requirements

### 5.1 FR-CORE-* — Core lifecycle

- **FR-CORE-01.** The system MUST start without requiring an account.
- **FR-CORE-02.** The system MUST provide an offline-first first-run
  flow that ends in a usable dashboard.
- **FR-CORE-03.** The system MUST offer the three storage modes
  defined in `00-Vision` §10: Local Only, Local + Cloud Sync, Cloud
  Only. Cloud Only MUST be accompanied by an encrypted local cache
  that preserves offline use.
- **FR-CORE-04.** The system MUST allow the user to switch storage
  mode; the switch MUST be auditable.
- **FR-CORE-05.** The system MUST allow the user to back up and
  restore the local encrypted database.
- **FR-CORE-06.** The system MUST support all five target platforms
  (Windows, Linux, macOS, Android, iOS) at parity.
- **FR-CORE-07.** The system MUST NOT embed advertising, sponsored
  placements, or third-party trackers.
- **FR-CORE-08.** The system MUST NOT require payment for any core
  feature.

### 5.2 FR-SEC-* — Security and privacy surface

- **FR-SEC-01.** The system MUST authenticate the user on first
  access (PIN, password or biometric) on devices that support it.
- **FR-SEC-02.** The system MUST encrypt user data at rest.
- **FR-SEC-03.** The system MUST encrypt user data in transit.
- **FR-SEC-04.** The system MUST require explicit, revocable opt-in
  for any data leaving the device.
- **FR-SEC-05.** The system MUST display, in clear language, every
  permission a plugin requests and MUST require explicit consent.
- **FR-SEC-06.** The system MUST allow the user to revoke a plugin
  permission and uninstall the plugin at any time.
- **FR-SEC-07.** The system MUST be role-ready; the introduction of
  multiple roles MUST NOT require a schema migration.
- **FR-SEC-08.** The system MUST publish a public threat model and
  keep it in sync with the product surface.

### 5.3 FR-LOC-* — Localisation and accessibility

- **FR-LOC-01.** The system MUST externalise every user-visible
  string.
- **FR-LOC-02.** The system MUST support RTL locales end-to-end in
  built-in UI; built-in themes MUST be RTL-clean.
- **FR-LOC-03.** The system MUST format numbers, dates, calendars,
  currencies and timezones per the user's locale.
- **FR-LOC-04.** The system MUST ship with English at v0.5 and MUST
  allow additional locales without code changes.
- **FR-LOC-05.** The system MUST meet WCAG 2.2 AA in built-in UI
  (keyboard, screen reader, contrast, focus order, no
  colour-alone signalling).
- **FR-LOC-06.** The system MUST respect the platform font-size and
  DPI settings.

### 5.4 FR-AI-* — AI capabilities

- **FR-AI-01.** The system MUST run all AI capabilities on-device or
  on the user's self-hosted runtime; the system MUST NOT call a paid
  third-party AI API.
- **FR-AI-02.** The system MUST implement the receipt scanning flow
  in `00-Vision` §11: Take Picture → Receipt Detection → OCR → Layout
  Analysis → Field Extraction → Validation → Expense Categorisation
  → User Confirmation → Save.
- **FR-AI-03.** The system MUST surface a per-field and per-document
  confidence score for every AI-derived value and MUST require user
  confirmation for low-confidence values.
- **FR-AI-04.** The system MUST allow the user to override any AI
  suggestion.
- **FR-AI-05.** The system MUST allow the user to replace any AI
  model with a different on-device model, without code changes.
- **FR-AI-06.** The system MUST provide built-in models for OCR,
  categorisation, anomaly detection, recommendation, insights and
  forecasting.
- **FR-AI-07.** The system MUST explain a forecast and a
  recommendation in human-readable form.
- **FR-AI-08.** The system MUST allow the user to disable any AI
  capability entirely.

### 5.5 FR-PLUGIN-* — Plugin surface

- **FR-PLUGIN-01.** The system MUST expose a stable, versioned
  Plugin SDK (`33-Plugin-SDK`).
- **FR-PLUGIN-02.** The system MUST support at least the following
  plugin categories: sync, widget, theme, AI model, OCR, financial
  provider, importer, exporter, report, analytics, chart,
  notification, recommendation, investment provider, language.
- **FR-PLUGIN-03.** The system MUST execute plugins in a documented
  isolation boundary and MUST enforce declared permissions.
- **FR-PLUGIN-04.** The system MUST provide an in-app Plugin
  Marketplace (SCOPE-22) for discovery, install, update and removal.
- **FR-PLUGIN-05.** The system MUST record the trust signals of a
  plugin (signature, source, version, author) and present them to
  the user before install.
- **FR-PLUGIN-06.** The system MUST support offline use of
  previously installed plugins; plugins that require network MUST
  declare so and MUST NOT silently fail.

### 5.6 FR-SYNC-* — Synchronisation

- **FR-SYNC-01.** The system MUST implement sync as a plugin
  contract; no sync provider may be hard-coded.
- **FR-SYNC-02.** The system MUST encrypt data client-side before
  sync; the sync provider MUST see only ciphertext.
- **FR-SYNC-03.** The system MUST allow the user to choose any
  installed sync provider.
- **FR-SYNC-04.** The system MUST remain fully usable offline; sync
  is an enhancement.
- **FR-SYNC-05.** The system MUST implement a conflict-resolution
  strategy as defined in `14-Synchronization`; the strategy MUST be
  deterministic and auditable.
- **FR-SYNC-06.** The system MUST surface a clear summary of
  conflicts and the chosen resolution to the user.

### 5.7 FR-IMPORT-* — Import and export

- **FR-IMPORT-01.** The system MUST support import from CSV, Excel,
  PDF, OFX, QFX and bank statement files.
- **FR-IMPORT-02.** The system MUST record a reproducible import
  profile per source so an import can be replayed.
- **FR-IMPORT-03.** The system MUST support export to CSV, Excel,
  PDF and JSON.
- **FR-IMPORT-04.** The system MUST compute a hash of every export
  and display it to the user.
- **FR-IMPORT-05.** The system MUST support scheduled export.
- **FR-IMPORT-06.** The system MUST NOT execute any content from
  imported files (no macros, no scripts).

## 6. Module Functional Requirements

### 6.1 Dashboard (SCOPE-01)

- **FR-DASH-01.** The dashboard MUST be widget-based.
- **FR-DASH-02.** The dashboard MUST allow drag, drop, resize, hide,
  show, create, delete, and configure for every widget.
- **FR-DASH-03.** The dashboard MUST ship with the built-in widgets
  in `02-Product-Scope` §8.1.
- **FR-DASH-04.** The dashboard MUST support plugin-defined widgets.
- **FR-DASH-05.** The dashboard MUST persist its layout per device
  and sync the layout through the sync layer.
- **FR-DASH-06.** The dashboard MUST be keyboard-navigable and
  screen-reader friendly.

### 6.2 Accounts (SCOPE-02)

- **FR-ACC-01.** The system MUST support the account subtypes in
  `02-Product-Scope` §8: cash, bank, card, investment, crypto,
  custom.
- **FR-ACC-02.** The system MUST support multi-currency accounts
  and MUST store the currency per account.
- **FR-ACC-03.** The system MUST support reconciliation against an
  imported statement.
- **FR-ACC-04.** The system MUST show a running balance per account.
- **FR-ACC-05.** The system MUST support opening and closing an
  account; closing an account MUST NOT delete its transactions.
- **FR-ACC-06.** The system MUST support archiving an account.

### 6.3 Transactions (SCOPE-03)

- **FR-TXN-01.** The system MUST support manual, imported, recurring
  and split transactions.
- **FR-TXN-02.** *(Implemented for scanned receipts; a manual file picker is not built.)* The system MUST allow attaching files (receipt,
  contract, photo) to a transaction; attachments MUST be stored
  encrypted at rest.
- **FR-TXN-03.** The system MUST support search, filter and sort
  across transactions.
- **FR-TXN-04.** The system MUST support bulk categorisation and bulk
  tagging.
- **FR-TXN-05.** The system MUST support exporting the filtered
  transaction set.

### 6.4 Budgets (SCOPE-04)

- **FR-BDG-01.** The system MUST support per-category, per-period
  budgets.
- **FR-BDG-02.** The system MUST support rolling budgets.
- **FR-BDG-03.** The system MUST support soft and hard thresholds
  per budget and MUST emit notifications via the rule engine.
- **FR-BDG-04.** The system MUST support pausing, resuming and
  archiving a budget.

### 6.5 Savings Goals (SCOPE-05)

- **FR-SAV-01.** The system MUST support target-based, time-bound
  savings goals.
- **FR-SAV-02.** The system MUST compute and show progress and a
  projected completion date.
- **FR-SAV-03.** The system MUST allow linking a goal to one or more
  accounts.

### 6.6 Investments (SCOPE-06..10)

- **FR-INV-01.** The system MUST support the asset classes in
  `02-Product-Scope` §8: stocks, ETFs, crypto, mutual funds, custom.
- **FR-INV-02.** The system MUST compute cost basis, realised P/L
  and unrealised P/L.
- **FR-INV-03.** The system MUST support corporate actions
  (splits, dividends).
- **FR-INV-04.** The system MUST allow a price source to be a
  built-in feed, a plugin feed, or a manual entry.
- **FR-INV-05.** The system MUST record the source of every price.

### 6.7 Net Worth (SCOPE-11)

- **FR-NW-01.** The system MUST compute net worth from all open
  accounts.
- **FR-NW-02.** The system MUST support a user-chosen reporting
  currency and MUST use historical exchange rates for past values.
- **FR-NW-03.** The system MUST show net worth over time as a chart.

### 6.8 Cash Flow (SCOPE-12)

- **FR-CF-01.** The system MUST show income, expense and net cash
  flow over a user-chosen period.
- **FR-CF-02.** The system MUST support breakdown by category,
  account and tag.

### 6.9 Recurring Expenses (SCOPE-13)

- **FR-RX-01.** The system MUST detect recurring expenses based on
  configurable rules.
- **FR-RX-02.** The system MUST require user confirmation before
  promoting a transaction to a recurring expense.
- **FR-RX-03.** The system MUST show projected future occurrences.

### 6.10 Subscriptions (SCOPE-14)

- **FR-SUB-01.** The system MUST show all known subscriptions with
  next renewal date and amount.
- **FR-SUB-02.** The system MUST emit a renewal reminder via the
  rule engine.
- **FR-SUB-03.** The system MUST show trend alerts when a
  subscription's amount changes.

### 6.11 Financial Calendar (SCOPE-15)

- **FR-CAL-01.** The system MUST show a calendar of upcoming bills,
  paydays, goal milestones, and projected low-balance days.
- **FR-CAL-02.** The system MUST allow user-defined calendar events.
- **FR-CAL-03.** The system MUST honour the user's timezone.

### 6.12 Reports (SCOPE-16)

- **FR-RPT-01.** The system MUST ship with a built-in report set
  (categories, accounts, year-in-review, etc.).
- **FR-RPT-02.** The system MUST support a report builder.
- **FR-RPT-03.** The system MUST allow exporting a report to CSV,
  Excel and PDF.
- **FR-RPT-04.** The system MUST compute reports locally; no
  report computation may require the network.

### 6.13 Analytics (SCOPE-17)

- **FR-AN-01.** The system MUST support cohort, distribution and
  correlation analyses on the user's data.
- **FR-AN-02.** The system MUST compute analyses locally.

### 6.14 Forecasting (SCOPE-18)

- **FR-FC-01.** The system MUST support forecasting for cash flow,
  balance and category spend.
- **FR-FC-02.** The system MUST explain a forecast in human-readable
  form, including the model used and the data window.
- **FR-FC-03.** The system MUST allow the user to disable
  forecasting.

### 6.15 Import (SCOPE-19)

See §5.7.

### 6.16 Export (SCOPE-20)

See §5.7.

### 6.17 AI Assistant (SCOPE-21)

- **FR-AIA-01.** The system MUST provide a conversational assistant
  grounded in the user's own data.
- **FR-AIA-02.** The assistant MUST run on-device; the system MUST
  NOT call a paid third-party AI API.
- **FR-AIA-03.** The assistant MUST show its sources and its
  confidence.
- **FR-AIA-04.** The assistant MUST allow the user to reject an
  answer and to inspect the data used.

### 6.18 Plugin Marketplace (SCOPE-22)

See §5.5.

### 6.19 Settings (SCOPE-23)

- **FR-SET-01.** Settings MUST cover storage mode, sync, security,
  AI, plugins, locales, themes, telemetry (opt-in) and backup.
- **FR-SET-02.** Settings MUST be persisted per device and synced
  through the sync layer.
- **FR-SET-03.** Settings MUST allow export and import as a JSON
  file (for portability), without leaking secrets.

## 7. Requirements Summary by Source

| Source         | Count    |
|----------------|----------|
| FR-CORE-*      | 8        |
| FR-SEC-*       | 8        |
| FR-LOC-*       | 6        |
| FR-AI-*        | 8        |
| FR-PLUGIN-*    | 6        |
| FR-SYNC-*      | 6        |
| FR-IMPORT-*    | 6        |
| FR-DASH-*      | 6        |
| FR-ACC-*       | 6        |
| FR-TXN-*       | 5        |
| FR-BDG-*       | 4        |
| FR-SAV-*       | 3        |
| FR-INV-*       | 5        |
| FR-NW-*        | 3        |
| FR-CF-*        | 2        |
| FR-RX-*        | 3        |
| FR-SUB-*       | 3        |
| FR-CAL-*       | 3        |
| FR-RPT-*       | 4        |
| FR-AN-*        | 2        |
| FR-FC-*        | 3        |
| FR-AIA-*       | 4        |
| FR-SET-*       | 3        |
| **Total**      | **109**  |

> **Note.** The count is the number of *statements*, not the number
> of acceptance criteria. `48-Acceptance-Criteria` will produce
> `FR-AC-*` rows, typically several per `FR-*`.

## 8. Deferred Functional Decisions

| ID     | Decision                                                                                  | Owning document(s)         |
|--------|-------------------------------------------------------------------------------------------|----------------------------|
| FR-D-1 | Whether Forecasting (SCOPE-18) ships in the first release or is gated by a separate ADR. | `13-AI-Architecture`        |
| FR-D-2 | Whether the Report Builder ships with templates only, builder only, or both.               | `02-Product-Scope` PS-D-5   |
| FR-D-3 | Default notification channel per platform.                                                 | `42-UX-Guidelines`          |
| FR-D-4 | The exact list of built-in investment providers (none, by default; plugins are required). | `02-Product-Scope`          |

## 9. Quality Checklist

- [x] Every FR is uniquely identified and uses RFC 2119 keywords.
- [x] Every FR is traceable to a SCOPE-* item and at least one
      UC-* use case.
- [x] Cross-cutting and per-module requirements are both present.
- [x] No FR silently widens or shrinks product scope.
- [x] No TODO placeholders.
- [x] No contradiction with `00-Vision`..`06-Use-Cases`.

## 10. Revision History

| Version | Date       | Author                | Change                                                       |
|---------|------------|-----------------------|--------------------------------------------------------------|
| 0.1     | 2026-07-08 | Engineering Review Board | Initial functional requirements.                          |
