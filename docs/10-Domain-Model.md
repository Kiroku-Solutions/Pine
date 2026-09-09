# 10 — Domain Model

| Field            | Value                                                                                |
|------------------|--------------------------------------------------------------------------------------|
| Document ID      | `PINE-DOC-10-DOMAIN-MODEL`                                                           |
| Status           | Partially implemented — the money aggregates are real; several contexts are unbuilt  |
| Reconciled       | 2026-09-09 · `Account`, `Transaction`, `Money`, `Budget`, `Attachment` and their invariants are implemented and tested. **`Attachment` is built as of the Phase 3 attachment work**: §7's "Hash MUST be set" is a SHA-256 the aggregate computes and verifies on read, and §6.2's "Attachments MUST be stored encrypted at rest" is satisfied by SQLCipher, because the bytes are a BLOB in `pine.db` rather than a file beside it — see `60-Production-Readiness-Audit` §12.68 for why. `Rule` and `Tag` still do not exist (TD-37), so §6.14 and the `Tag` row in §7 describe a design that is not built. |
| Owner            | Engineering Review Board                                                             |
| Created          | 2026-07-08                                                                           |
| Last Updated     | 2026-07-08                                                                           |
| Specification Standard | DDD strategic + tactical                                                       |
| Glossary         | `PINE-DOC-03-GLOSSARY`                                                               |
| Prerequisites    | `00-Vision`..`09-System-Architecture`                                                |
| Decisions closed | None                                                                                 |
| Related Documents| `11-Data-Model`, `31-Rule-Engine`, `46-Features`, `47-User-Stories`                  |

---

## 1. Purpose

Define the *domain model* of Pine: aggregates, entities, value
objects, domain events, and the ubiquitous language for each bounded
context. This is the *behavioural* model; persistence details live in
`11-Data-Model`.

## 2. Scope

In scope: the *behavioural* model of every bounded context in
`09-System-Architecture` §7.

Out of scope: persistence shapes and indexes → `11-Data-Model`;
transport and serialisation → `11-Data-Model`; AI/ML models →
`13-AI-Architecture`; plugin contracts → `33-Plugin-SDK`.

## 3. Definitions

| Term        | Working definition                                                                                |
|-------------|------------------------------------------------------------------------------------------------------|
| Aggregate   | A cluster of domain objects treated as a single unit for writes. The aggregate root is the only entry point. |
| Entity      | An object with identity and lifecycle.                                                              |
| Value Object| An immutable object defined by its attributes, with no identity.                                    |
| Domain Event| A first-class fact about something that happened in the past.                                       |
| Ubiquitous Language | The shared vocabulary of a bounded context, used in code, tests, and documentation.       |
| Repository  | A port that abstracts persistence of aggregates.                                                    |
| Invariant   | A condition that must always hold in the model.                                                    |

> **Assumption DM-A-1.** Money is a value object (decimal). The
> ADR-07 in `09-System-Architecture` is binding for this document.

## 4. Bounded Contexts (model view)

The strategic map is owned by `09-System-Architecture` §7. The
table below assigns the *ubiquitous language* and the *aggregates*
for each context.

| Bounded Context | Ubiquitous language                                                            | Aggregates (roots)                                  |
|-----------------|----------------------------------------------------------------------------------|------------------------------------------------------|
| Accounts        | Account, balance, currency, subtype, opening/closing balance, statement period. | `Account`                                            |
| Transactions    | Transaction, split, attachment, category, tag, transfer.                        | `Transaction`                                        |
| Budgets         | Budget, period, threshold, rollover, alert.                                      | `Budget`                                             |
| Savings         | Goal, target, contribution, projected completion.                                | `SavingsGoal`                                        |
| Investments     | Holding, asset, cost basis, P/L, corporate action, valuation.                   | `InvestmentAccount`, `Holding`                       |
| Reports         | Report definition, parameter, output.                                            | `ReportDefinition`                                   |
| AI              | Model, prompt, prediction, confidence, explanation.                              | `AIModel` (config), `Prediction` (result)            |
| Sync            | Sync state, change set, conflict, resolution.                                    | `SyncState`                                          |
| Plugins         | Plugin manifest, permission, install state, version.                             | `PluginInstallation`                                 |
| Settings        | Storage mode, security setting, locale, theme, telemetry consent.               | `UserSettings`                                       |
| Identity        | User, credential, device, session, role.                                         | `User`, `Device`                                     |
| Notifications   | Notification, channel, delivery receipt.                                         | `NotificationChannel`, `Notification`                |
| Import/Export   | Importer, exporter, profile, source, target.                                     | `ImportProfile`, `ExportProfile`                     |
| Rule Engine     | Rule, trigger, condition, action, audit entry.                                   | `Rule`                                               |
| Forecasting     | Forecast, scenario, explanation.                                                 | `Forecast`                                           |

## 5. Cross-Context Domain Events

The following events are published by their owning context and
consumed by other contexts. The events are *contracts*; the schemas
live in `11-Data-Model`.

| Event ID              | Publisher     | Payload                                                                                  | Subscribers (examples)                          |
|-----------------------|---------------|-------------------------------------------------------------------------------------------|--------------------------------------------------|
| `TransactionRecorded` | Transactions  | transaction id, account id, amount, currency, category, timestamp.                        | Reports, Budgets, Rule Engine, Forecasting, AI, Sync. |
| `BudgetExceeded`      | Budgets       | budget id, period, threshold, ratio.                                                      | Notifications, Rule Engine.                       |
| `GoalReached`         | Savings       | goal id, target, achieved at.                                                             | Notifications, Rule Engine.                       |
| `HoldingChanged`      | Investments   | holding id, new valuation, valuation source, timestamp.                                   | Net Worth, Reports, Sync.                         |
| `AnomalyDetected`     | AI            | transaction id, score, reason.                                                            | Notifications, Rule Engine.                       |
| `ForecastProduced`    | Forecasting   | forecast id, scenario, output, explanation.                                                | Reports, Notifications.                           |
| `RuleFired`           | Rule Engine   | rule id, trigger, action, timestamp.                                                      | Notifications.                                    |
| `SyncCompleted`       | Sync          | sync id, result, conflicts resolved, timestamp.                                           | Settings, Reports (audit).                        |
| `ConflictDetected`    | Sync          | sync id, conflict id, entities, strategy.                                                 | Notifications.                                    |
| `PluginInstalled`     | Plugins       | plugin id, version, permissions.                                                          | Settings, Security.                               |
| `PluginUninstalled`   | Plugins       | plugin id.                                                                                | Settings, Security.                               |
| `SettingsChanged`     | Settings      | settings id, change set.                                                                  | All (for live behaviour).                         |

**Rationale.** Events are the *only* cross-context integration
mechanism that keeps the contexts independent. Direct references
between aggregates are forbidden; an aggregate that needs data from
another context reads from a projection or subscribes to its events.

## 6. Canonical Aggregates (per context)

### 6.1 Accounts

- **Aggregate root.** `Account`.
- **Identity.** `AccountId` (UUID v7).
- **Key attributes.** name, subtype (cash, bank, card, investment,
  crypto, custom), currency, opening balance, opening date, status
  (open, closed, archived), statement period.
- **Invariants.**
  - An account MUST have exactly one reporting currency.
  - An account's opening balance MUST be a non-negative number for
    liability accounts; a non-positive number for asset accounts
    (modelled by subtype).
  - Closing an account MUST NOT delete its transactions.
- **Behaviours.** open, rename, change currency (with revaluation
  audit), close, archive, unarchive.

### 6.2 Transactions

- **Aggregate root.** `Transaction`.
- **Identity.** `TransactionId`.
- **Key attributes.** account id, posted at, amount, currency,
  category, tags, attachments, source (manual, imported, recurring,
  split), reconciliation state.
- **Invariants.**
  - The sum of a transaction's splits MUST equal the transaction's
    amount.
  - A transaction in a closed account MUST NOT be editable except
    for tagging.
  - Attachments MUST be stored encrypted at rest.
- **Behaviours.** record, edit, split, attach, categorise, tag,
  reconcile, unreconcile, search.

### 6.3 Budgets

- **Aggregate root.** `Budget`.
- **Identity.** `BudgetId`.
- **Key attributes.** category (or set), period (week, month,
  quarter, year, custom), amount, currency, soft threshold, hard
  threshold, rollover policy.
- **Invariants.**
  - A budget MUST have at least one category.
  - The hard threshold MUST be greater than or equal to the soft
    threshold.
- **Behaviours.** create, edit, pause, resume, archive, evaluate
  (against transactions).

### 6.4 Savings

- **Aggregate root.** `SavingsGoal`.
- **Identity.** `GoalId`.
- **Key attributes.** name, target amount, target date, contributing
  account(s), current progress, status.
- **Invariants.**
  - A goal's target amount MUST be positive.
  - A goal's progress MUST equal the sum of contributions minus
    withdrawals.
- **Behaviours.** create, fund, withdraw, re-target, close.

### 6.5 Investments

- **Aggregate root.** `InvestmentAccount`.
- **Identity.** `InvestmentAccountId`.
- **Children.** `Holding` (entity under the account).
- **Key attributes.** account: name, custodian, base currency.
  holding: asset, quantity, cost basis, acquisition date, current
  price source, current price, last revalued at.
- **Invariants.**
  - A holding MUST have a positive quantity.
  - Cost basis MUST be in the account's base currency or be
    convertible to it.
  - A revaluation MUST record the price source and timestamp.
- **Behaviours.** add holding, revalue, record corporate action,
  dispose, transfer.

### 6.6 Reports

- **Aggregate root.** `ReportDefinition`.
- **Identity.** `ReportDefinitionId`.
- **Key attributes.** name, type, parameters, schedule, output
  format, scope.
- **Invariants.**
  - A scheduled report MUST have a valid cron expression.
  - A report's scope MUST be expressible as a specification.
- **Behaviours.** create, edit, run, schedule, export, archive.

### 6.7 AI

- **Aggregate root.** `AIModel` (configuration only).
- **Identity.** `ModelId`.
- **Key attributes.** name, type (OCR, categorisation, anomaly,
  recommendation, forecasting), version, source (built-in, plugin,
  user-supplied), permissions, status.
- **Result entity.** `Prediction` (immutable result of a model run).
- **Invariants.**
  - A model MUST declare its type and version.
  - A model MUST NOT call a paid third-party API.
- **Behaviours.** register, enable, disable, swap, run (returns
  `Prediction`).

### 6.8 Sync

- **Aggregate root.** `SyncState`.
- **Identity.** `SyncId` (per sync provider).
- **Key attributes.** provider id, last sync at, pending change set,
  last error, encryption envelope.
- **Invariants.**
  - The payload MUST be encrypted client-side.
  - Conflicts MUST be resolved by the strategy in `14-Synchronization`.
- **Behaviours.** start, pause, resume, push, pull, resolve conflict.

### 6.9 Plugins

- **Aggregate root.** `PluginInstallation`.
- **Identity.** `PluginInstallationId`.
- **Key attributes.** plugin id, version, declared permissions,
  granted permissions, install state, source.
- **Invariants.**
  - Granted permissions MUST be a subset of declared permissions.
  - A plugin MUST be uninstallable at any time.
- **Behaviours.** install, update, grant permission, revoke
  permission, uninstall.

### 6.10 Settings

- **Aggregate root.** `UserSettings`.
- **Identity.** `SettingsId` (one per user).
- **Key attributes.** storage mode, sync target, security setting,
  AI configuration, plugin list (per SCOPE-22), locale, theme,
  telemetry consent.
- **Invariants.**
  - Telemetry consent MUST default to denied.
  - A change to a setting MUST be auditable.
- **Behaviours.** change locale, change theme, opt in/out, change
  storage mode.

### 6.11 Identity

- **Aggregate root.** `User`.
- **Identity.** `UserId`.
- **Children.** `Device` (entity).
- **Key attributes.** credentials, devices, role(s), recovery
  options.
- **Invariants.**
  - A user MUST have at least one credential.
  - A device MUST be tied to one user at a time.
- **Behaviours.** add device, remove device, change credential,
  assign role, revoke role.

### 6.12 Notifications

- **Aggregate root.** `NotificationChannel`.
- **Identity.** `ChannelId`.
- **Key attributes.** kind (system, in-app, email, push, custom
  plugin), destination, status.
- **Result entity.** `Notification` (immutable delivery attempt).
- **Invariants.**
  - A channel MUST have a destination.
  - A delivery MUST be retried with exponential backoff.
- **Behaviours.** add channel, remove channel, send, retry, mark
  delivered, mark failed.

### 6.13 Import/Export

- **Aggregate root.** `ImportProfile` / `ExportProfile`.
- **Identity.** `ProfileId`.
- **Key attributes.** name, source/target format, field mapping,
  transformation steps, schedule, scope.
- **Invariants.**
  - A profile MUST be replayable.
  - A profile MUST NOT execute code from the source file.
- **Behaviours.** create, edit, run, schedule, replay, archive.

### 6.14 Rule Engine *(not built)*

> This section specifies a design, not code. The `Rule` entity and its table
> were dropped in TD-37: what existed was a shell with an unconstrained
> `TargetCategoryId` `Guid`, no evaluator and no consumer. The engine described
> in `31-Rule-Engine` is Phase 3 work and is expected to be built on a real
> `CategoryId` relationship rather than on that shell.

- **Aggregate root.** `Rule`.
- **Identity.** `RuleId`.
- **Key attributes.** name, trigger, condition (a specification),
  action, enabled flag, audit log.
- **Invariants.**
  - A rule MUST have a trigger and an action.
  - A rule MUST be auditable end-to-end.
- **Behaviours.** create, edit, enable, disable, fire, audit.

### 6.15 Forecasting

- **Aggregate root.** `Forecast`.
- **Identity.** `ForecastId`.
- **Key attributes.** scenario, model, data window, output,
  explanation, generated at.
- **Invariants.**
  - A forecast MUST include an explanation.
  - A forecast MUST NOT be silently used to mutate user data.
- **Behaviours.** generate, explain, accept, reject.

## 7. Canonical Value Objects

| Value Object  | Definition                                                                                  | Invariants                                                              |
|---------------|----------------------------------------------------------------------------------------------|--------------------------------------------------------------------------|
| `Money`       | An amount in a specific currency.                                                            | Non-null amount, non-null currency code.                                  |
| `Currency`    | An ISO 4217 code or a crypto asset symbol.                                                   | Non-empty; valid per the active registry.                                 |
| `ExchangeRate`| A rate at a specific timestamp.                                                              | Positive; timestamped.                                                   |
| `DateRange`   | A pair of instants in a specific timezone.                                                   | `from <= to`.                                                            |
| `Category`    | A hierarchical label (e.g. `Food / Restaurants`).                                            | Non-empty; tree depth ≤ 5 (configurable).                                |
| `Tag` *(not built)* | A free-form label.                                                                      | Non-empty; case-insensitive.                                              |
| `Attachment` | The file itself, stored encrypted, with a content hash. | Hash MUST be set — and IS: SHA-256, computed by the aggregate from the content and verified on every read. Content is non-empty and at most 25 MB; the row is bound to its transaction by a real foreign key. |
| `Locale`      | A language-and-region identifier.                                                            | Conforms to BCP 47.                                                      |
| `Timezone`    | An IANA TZ identifier.                                                                       | Conforms to IANA.                                                        |
| `Confidence`  | A value in [0, 1].                                                                            | In range.                                                                 |
| `Permission`  | A typed permission token.                                                                     | Conforms to the SDK permission grammar.                                   |
| `Version`     | A SemVer tuple.                                                                              | Conforms to SemVer 2.0.0.                                                |

## 8. Ubiquitous Language Coverage

The ubiquitous language is the union of:

- All identifiers in §6, §7 and §4.
- The glossary terms in `03-Glossary` whose `TERM-*` is owned by a
  bounded context in §4.
- The cross-context events in §5.

Any new term MUST be added either to the glossary or to this
document; otherwise it is a defect.

## 9. Domain-Level Assumptions

| ID          | Assumption                                                                                            |
|-------------|--------------------------------------------------------------------------------------------------------|
| DM-A-1      | Money is a decimal value object (ADR-07).                                                             |
| DM-A-2      | Time is abstract (`IClock`) and is locale-aware (ADR-08).                                              |
| DM-A-3      | A user has exactly one set of `UserSettings` at any time.                                             |
| DM-A-4      | Cross-context integration is event-driven; direct references are forbidden.                            |
| DM-A-5      | Investment valuation is always paired with a source and a timestamp (auditability).                    |

## 10. Deferred Domain Decisions

| ID     | Decision                                                                                  | Owning document(s)         |
|--------|-------------------------------------------------------------------------------------------|----------------------------|
| DM-D-1 | Whether "Mutual Funds" remains a separate module or a subtype of `InvestmentAccount`.      | `02-Product-Scope` PS-D-2   |
| DM-D-2 | Whether "AI Assistant" and "Receipt Scanning" share a `Prediction` model or have separate ones. | `13-AI-Architecture`        |
| DM-D-3 | Whether `Forecast` can be a subtype of `Prediction`.                                     | `13-AI-Architecture`        |

## 11. Quality Checklist

- [x] Bounded contexts, aggregates, entities, value objects, events
      and ubiquitous language are all present.
- [x] Invariants are stated for every aggregate.
- [x] Cross-context events are the only cross-context integration.
- [x] No TODO placeholders.
- [x] No contradiction with `00-Vision`..`09-System-Architecture`.

## 12. Revision History

| Version | Date       | Author                | Change                                                       |
|---------|------------|-----------------------|--------------------------------------------------------------|
| 0.1     | 2026-07-08 | Engineering Review Board | Initial domain model.                                     |
