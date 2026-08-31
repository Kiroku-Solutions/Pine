# 06 — Use Cases

| Field            | Value                                                                                |
|------------------|--------------------------------------------------------------------------------------|
| Document ID      | `PINE-DOC-06-USE-CASES`                                                              |
| Status           | Partially implemented — normative for behaviour; several flows do not exist          |
| Reconciled       | 2026-08-31 · The vault, transaction, import and scan flows are implemented. Anything touching sync, plugins or rules is not. |
| Owner            | Engineering Review Board                                                             |
| Created          | 2026-07-08                                                                           |
| Last Updated     | 2026-07-08                                                                           |
| Specification Standard | IEEE 29148 (use cases)                                                          |
| Glossary         | `PINE-DOC-03-GLOSSARY`                                                               |
| Prerequisites    | `00-Vision`, `01-Business-Requirements`, `02-Product-Scope`, `03-Glossary`, `04-Stakeholders`, `05-Personas` |
| Related Documents| `07-Functional-Requirements`, `46-Features`, `47-User-Stories`, `48-Acceptance-Criteria` |

---

## 1. Purpose

Capture, in narrative form, the *interactions* between Pine personas
and Pine. Use cases are the bridge between personas (`05-Personas`)
and the functional requirements (`07-Functional-Requirements`). They
are normative for behaviour; their acceptance criteria live in
`48-Acceptance-Criteria`.

## 2. Scope

In scope: end-to-end use cases that cross module boundaries, and the
key building-block use cases inside each module. This document does
not enumerate every micro-interaction; those are owned by
`07-Functional-Requirements`.

## 3. Definitions

| Term           | Working definition                                                                                  |
|----------------|------------------------------------------------------------------------------------------------------|
| Actor          | A persona or external system interacting with Pine.                                                  |
| Precondition   | A state the system (and/or the actor) must be in before the use case starts.                          |
| Postcondition  | A state the system is guaranteed to be in when the use case ends successfully.                        |
| Trigger        | The event that starts the use case.                                                                  |
| Main flow      | The step-by-step happy path.                                                                          |
| Alternate flow | A variation of the main flow that also ends in success.                                              |
| Exception flow | A variation that ends in a defined failure state.                                                    |
| Use case ID    | `UC-*`                                                                                               |

> **Assumption UC-A-1.** Use cases are written from the persona's point
> of view, not from the implementation's.

## 4. Use-Case Template

Each use case uses the template below. Empty fields are a defect.

| Field            | Description                                                                       |
|------------------|-----------------------------------------------------------------------------------|
| ID               | `UC-*`                                                                            |
| Name             | Verb-noun                                                                         |
| Primary actor    | Persona ID (`PERS-*`) or external system                                          |
| Stakeholder(s)   | `SH-*`                                                                            |
| Trigger          | What starts the use case                                                          |
| Preconditions    | Required state                                                                    |
| Postconditions   | Guaranteed state on success                                                       |
| Main flow        | Numbered steps                                                                    |
| Alternate flows  | Numbered `A1, A2, …`                                                              |
| Exception flows  | Numbered `E1, E2, …`                                                              |
| Traced SCOPE-*   | The scope items the use case exercises                                            |
| Traced principles| Vision principles invoked (P-01..P-13)                                          |

## 5. End-to-End Use Cases

### 5.1 UC-01 — First-run setup (PERS-01, PERS-05)

- **Primary actor.** PERS-01, PERS-05.
- **Stakeholder(s).** `SH-END-USER`, `SH-AI-CURIOUS-USER`.
- **Trigger.** User installs Pine and launches it for the first time.
- **Preconditions.** Pine is installed; user has not run it before on
  this device.
- **Postconditions.** Pine is usable offline; user has chosen a
  storage mode; telemetry is off by default.
- **Main flow.**
  1. Pine starts without requiring an account.
  2. Pine presents a non-blocking welcome screen with three choices:
     "Use local only", "Use local + cloud sync", "Use cloud only".
  3. User chooses a mode.
  4. Pine creates the local encrypted vault and shows an empty
     dashboard.
  5. Pine shows a one-time opt-in for telemetry with the default OFF.
- **Alternate flow A1.** User picks "Use local + cloud sync".
  1. Pine offers a list of installed sync provider plugins.
  2. User picks one, authenticates against the provider, and the
     encrypted sync is enabled.
- **Exception flow E1.** User closes the welcome screen without
  choosing.
  1. Pine defaults to Local Only and continues.
- **Traced SCOPE-*:** SCOPE-01, SCOPE-23.
- **Traced principles:** P-01, P-02, P-04, P-05, P-13.

### 5.2 UC-02 — Photograph a receipt and create an expense (PERS-05)

- **Primary actor.** PERS-05.
- **Stakeholder(s).** `SH-AI-CURIOUS-USER`.
- **Trigger.** User taps "Scan receipt" on the mobile app.
- **Preconditions.** Pine is installed; on-device OCR model is present.
- **Postconditions.** A confirmed expense is recorded; the photo is
  stored locally and encrypted; the user can audit and override.
- **Main flow.**
  1. User takes a picture of the receipt.
  2. Pine runs Receipt Detection, OCR, Layout Analysis, Field
     Extraction, Validation, and Categorisation on-device.
  3. Pine presents a confirmation screen with extracted fields,
     confidence scores, and a thumbnail of the photo.
  4. User confirms, edits a field, or rejects.
  5. On confirm, Pine writes the expense to the local encrypted
     database.
- **Alternate flow A1.** Field confidence below threshold.
  1. Pine highlights the field in red and asks the user to confirm or
     correct.
- **Exception flow E1.** OCR model missing.
  1. Pine shows a clear error and a link to the plugin marketplace.
- **Traced SCOPE-*:** SCOPE-21, SCOPE-19 (importer side), SCOPE-23.
- **Traced principles:** P-01, P-02, P-09, P-12.

### 5.3 UC-03 — Import a CSV bank statement (PERS-01, PERS-04)

- **Primary actor.** PERS-01 or PERS-04.
- **Stakeholder(s).** `SH-END-USER`, `SH-INTL-USER`.
- **Trigger.** User opens the Import wizard and selects CSV.
- **Preconditions.** At least one account exists; the CSV is on disk.
- **Postconditions.** Transactions are imported, deduplicated, and
  reconciled against the chosen account; a copy of the source file is
  stored alongside the import profile for reproducibility.
- **Main flow.**
  1. User picks the source file.
  2. Pine auto-detects the dialect, header row, date format and
     decimal separator (or asks the user).
  3. User maps source columns to the canonical fields.
  4. User previews the first N rows.
  5. User commits the import; Pine stores the import profile so the
     same import is repeatable.
- **Alternate flow A1.** Locale-specific format.
  1. User overrides the detected date and decimal formats.
- **Exception flow E1.** Duplicate detection finds N matches.
  1. Pine shows the matches and asks the user to skip, merge or
     import-as-new.
- **Traced SCOPE-*:** SCOPE-03, SCOPE-19.
- **Traced principles:** P-02, P-12.

### 5.4 UC-04 — Create a budget and receive an alert (PERS-01)

- **Primary actor.** PERS-01.
- **Stakeholder(s).** `SH-END-USER`.
- **Trigger.** User opens the Budgets module and creates a budget.
- **Preconditions.** At least one category exists.
- **Postconditions.** A budget is stored; the rule engine evaluates
  the budget on every relevant transaction; a notification is
  produced if a threshold is crossed.
- **Main flow.**
  1. User picks a category and a period.
  2. User sets a limit and optional soft threshold.
  3. User enables a notification channel (system, in-app, or a
     notification provider plugin).
  4. Pine records the budget.
- **Alternate flow A1.** User picks a built-in rule template.
  1. Pine pre-fills the rule and the user adjusts it.
- **Exception flow E1.** Notification provider missing.
  1. Pine shows the in-app notification and a link to the marketplace.
- **Traced SCOPE-*:** SCOPE-04, SCOPE-23, SCOPE-31.
- **Traced principles:** P-02, P-07.

### 5.5 UC-05 — Sync across devices (PERS-03)

- **Primary actor.** PERS-03.
- **Stakeholder(s).** `SH-MULTI-PLATFORM-USER`.
- **Trigger.** User enables sync and authenticates against a sync
  provider plugin.
- **Preconditions.** Same storage mode choice on both devices; sync
  provider plugin installed on both.
- **Postconditions.** Both devices converge to the same state;
  conflicts, if any, are resolved according to the strategy in
  `14-Synchronization` and the user is shown a clear summary.
- **Main flow.**
  1. User picks the sync provider on device A and authenticates.
  2. User does the same on device B with the same encrypted vault
     passphrase.
  3. Pine performs a first sync, showing progress.
  4. On conflict, Pine applies the strategy in `14-Synchronization`
     and notifies the user.
- **Alternate flow A1.** User has multiple devices.
  1. The same flow is repeated for each device.
- **Exception flow E1.** Cloud target unreachable.
  1. Pine queues the changes locally and retries with exponential
     backoff. The app stays usable offline.
- **Traced SCOPE-*:** SCOPE-23, plus the sync provider plugin
  (SCOPE-22, sync category).
- **Traced principles:** P-01, P-02, P-13.

### 5.6 UC-06 — Install a plugin (PERS-02, PERS-07)

- **Primary actor.** PERS-02 (user) and PERS-07 (author).
- **Stakeholder(s).** `SH-PLUGIN-AUTHOR`, `SH-POWER-USER`.
- **Trigger.** User opens the Plugin Marketplace and chooses a plugin.
- **Preconditions.** Marketplace is reachable (or local cache is
  available offline for previously installed plugins).
- **Postconditions.** Plugin is installed, listed in the active
  plugins, and exposed under its declared permissions.
- **Main flow.**
  1. User searches or browses the marketplace.
  2. User reviews the plugin page, including the declared permissions
     and the trust signals (signature, source, version, author).
  3. User clicks Install.
  4. Pine prompts for confirmation of the declared permissions.
  5. Plugin is downloaded (or installed from the local cache) and
     activated.
- **Alternate flow A1.** Plugin requires network.
  1. Pine warns the user if the plugin is offline-incompatible and
     requires explicit consent.
- **Exception flow E1.** Trust check fails.
  1. Pine blocks the install and explains why.
- **Traced SCOPE-*:** SCOPE-22.
- **Traced principles:** P-01, P-07, P-12.

### 5.7 UC-07 — Add an investment holding and see net worth (PERS-04)

- **Primary actor.** PERS-04.
- **Stakeholder(s).** `SH-INTL-USER`.
- **Trigger.** User opens the Investments module and adds a holding.
- **Preconditions.** An investment account exists; an investment
  provider plugin is installed (or prices are entered manually).
- **Postconditions.** Holding is recorded; cost basis and P/L are
  computed; net worth is updated and normalised to the user's
  reporting currency using the latest available exchange rate.
- **Main flow.**
  1. User picks the account and the asset type.
  2. User enters quantity, cost basis and acquisition date.
  3. Pine fetches the current price from the investment provider
     plugin (or the user enters it).
  4. Pine updates the dashboard widgets the user has on screen.
- **Alternate flow A1.** Multiple currencies.
  1. Pine normalises to the user's reporting currency and shows the
     conversion inline.
- **Exception flow E1.** Provider plugin missing.
  1. Pine offers a "manual price" mode and a link to the marketplace.
- **Traced SCOPE-*:** SCOPE-06, SCOPE-07, SCOPE-08, SCOPE-09, SCOPE-10, SCOPE-11.
- **Traced principles:** P-02, P-07, P-09.

### 5.8 UC-08 — Create a custom rule (PERS-02)

- **Primary actor.** PERS-02.
- **Stakeholder(s).** `SH-POWER-USER`.
- **Trigger.** User opens the Rule Engine and creates a rule.
- **Preconditions.** Pine is installed and has at least one
  transaction or budget.
- **Postconditions.** Rule is stored; the rule engine evaluates it on
  every relevant event; the rule is auditable.
- **Main flow.**
  1. User picks a trigger (e.g. "monthly budget threshold crossed").
  2. User picks a condition (e.g. "category = Restaurants").
  3. User picks an action (e.g. "send notification to channel X").
  4. User enables the rule.
- **Alternate flow A1.** Rule from a plugin template.
  1. User picks a template from a plugin and customises it.
- **Exception flow E1.** Rule references a missing plugin.
  1. Pine disables the rule and warns the user.
- **Traced SCOPE-*:** SCOPE-31.
- **Traced principles:** P-07, P-08.

### 5.9 UC-09 — Update Pine across platforms (PERS-03, PERS-04)

- **Primary actor.** Any user.
- **Stakeholder(s).** `SH-MULTI-PLATFORM-USER`, `SH-INTL-USER`.
- **Trigger.** Pine detects a new release.
- **Preconditions.** Auto-update is opt-in.
- **Postconditions.** Pine is updated on the device, user data is
  preserved, the user can roll back within the current minor.
- **Main flow.**
  1. Pine notifies the user of the update.
  2. User accepts; Pine downloads the update.
  3. Pine restarts and resumes from the local encrypted database.
- **Alternate flow A1.** Auto-update enabled.
  1. Pine downloads the update in the background and prompts for a
     restart.
- **Exception flow E1.** Update is incompatible with the local data
  format.
  1. Pine refuses the update, explains why, and offers to export the
     data first.
- **Traced SCOPE-*:** SCOPE-23.
- **Traced principles:** P-02, P-12.

### 5.10 UC-10 — Export the user's data (PERS-01, PERS-02)

- **Primary actor.** PERS-01 or PERS-02.
- **Stakeholder(s).** `SH-END-USER`, `SH-POWER-USER`.
- **Trigger.** User opens Export and picks a format.
- **Preconditions.** Pine has data to export.
- **Postconditions.** A file is produced, integrity-checked and
  offered to the user with a hash; the export format is documented.
- **Main flow.**
  1. User picks the format (CSV, Excel, PDF, JSON) and scope
     (everything, filtered).
  2. Pine builds the export locally.
  3. Pine computes a hash and shows it to the user.
  4. Pine writes the file to a user-chosen location.
- **Alternate flow A1.** Scheduled export.
  1. User schedules a recurring export.
- **Exception flow E1.** Disk full.
  1. Pine aborts cleanly, frees no user data, and explains.
- **Traced SCOPE-*:** SCOPE-20.
- **Traced principles:** P-01, P-12.

## 6. Building-Block Use Cases (per module)

The list below references the *kind* of building-block use case each
module must support. Detailed flows are owned by
`07-Functional-Requirements`; this section only lists the *categories*
that must exist for each SCOPE-* item.

| SCOPE-*   | Building-block use case categories                                                                  |
|-----------|------------------------------------------------------------------------------------------------------|
| SCOPE-01  | Add / remove / move / resize / configure a widget; pin a widget to a view.                          |
| SCOPE-02  | Create / edit / close / reconcile / archive an account.                                              |
| SCOPE-03  | Create / edit / split / attach / categorise / search / export a transaction.                        |
| SCOPE-04  | Create / edit / pause / resume / archive a budget; bind a budget to a notification channel.         |
| SCOPE-05  | Create / fund / re-target / close a savings goal.                                                    |
| SCOPE-06  | Add / edit / revalue / dispose of an investment holding; record a corporate action.                  |
| SCOPE-07  | Same as SCOPE-06 with an equity-specific valuation.                                                  |
| SCOPE-08  | Same as SCOPE-06 with an ETF-specific valuation.                                                     |
| SCOPE-09  | Same as SCOPE-06 with a crypto-specific valuation.                                                   |
| SCOPE-10  | Same as SCOPE-06 with a mutual-fund-specific valuation.                                              |
| SCOPE-11  | Show net worth over time; switch reporting currency; show breakdown.                                |
| SCOPE-12  | Show cash flow over a period; filter by account / category / tag.                                    |
| SCOPE-13  | Confirm / reject a recurring-expense suggestion; configure detection sensitivity.                    |
| SCOPE-14  | Add / edit / remove a subscription; show upcoming renewals; get an alert.                            |
| SCOPE-15  | Show calendar; add / edit / remove a calendar event; show projected low-balance days.                |
| SCOPE-16  | Run a built-in report; build a custom report; export a report.                                       |
| SCOPE-17  | Run a built-in analysis; build a custom analysis.                                                    |
| SCOPE-18  | Show a forecast; configure a forecast; explain a forecast.                                            |
| SCOPE-19  | Choose a source format; map fields; preview; commit; replay an import profile.                       |
| SCOPE-20  | Choose a target format; choose scope; produce; verify with hash.                                      |
| SCOPE-21  | Ask a question; receive an on-device answer; ask for an explanation; reject an answer.              |
| SCOPE-22  | Browse / search / install / update / remove a plugin; review permissions.                            |
| SCOPE-23  | Change storage mode; configure sync; configure security; configure AI; manage plugins; change locale; toggle telemetry; backup / restore. |

## 7. Use-Case Coverage Matrix

| Persona | Use cases it is the primary actor of                                                |
|---------|---------------------------------------------------------------------------------------|
| PERS-01 | UC-01, UC-03, UC-04, UC-10                                                          |
| PERS-02 | UC-06, UC-08, UC-10                                                                  |
| PERS-03 | UC-05, UC-09                                                                         |
| PERS-04 | UC-03, UC-07, UC-09                                                                  |
| PERS-05 | UC-01, UC-02                                                                          |
| PERS-06 | (Contributes to the SSoT; no end-user use case.)                                      |
| PERS-07 | UC-06                                                                                |

**Rationale.** Every primary persona is the actor of at least one
end-to-end use case. If a persona has no use case, it is a defect
in this document.

## 8. Use-Case-Level Assumptions

| ID          | Assumption                                                                                            |
|-------------|--------------------------------------------------------------------------------------------------------|
| UC-A-1      | Use cases are written from the persona's point of view, not the implementation's.                      |
| UC-A-2      | A use case can be enacted offline, online, or both; this is recorded in the postcondition.              |
| UC-A-3      | A use case that touches a plugin must declare which plugin category it expects.                         |

## 9. Deferred Decisions

| ID     | Decision                                                                                  | Owning document(s)         |
|--------|-------------------------------------------------------------------------------------------|----------------------------|
| UC-D-1 | "Shared vault" use case (read-only view for a partner).                                  | `05-Personas` (next rev.), `09-System-Architecture` |
| UC-D-2 | "Family" use case (multiple roles, child accounts).                                       | `09-System-Architecture` (role-ready) |
| UC-D-3 | Plugin-side use cases (authoring, signing, publishing).                                  | `33-Plugin-SDK`            |

## 10. Quality Checklist

- [x] Use cases are persona-driven and SCOPE-traceable.
- [x] End-to-end use cases cover first-run, AI, import, budget, sync,
      plugin, investment, rule, update, export.
- [x] Building-block use cases are listed per SCOPE-* module.
- [x] Coverage matrix maps every primary persona to at least one
      end-to-end use case.
- [x] No TODO placeholders.
- [x] No contradiction with `00-Vision`..`05-Personas`.

## 11. Revision History

| Version | Date       | Author                | Change                                                       |
|---------|------------|-----------------------|--------------------------------------------------------------|
| 0.1     | 2026-07-08 | Engineering Review Board | Initial use-case catalogue.                                |
