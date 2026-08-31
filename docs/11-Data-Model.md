# 11 — Data Model

| Field            | Value                                                                                |
|------------------|--------------------------------------------------------------------------------------|
| Document ID      | `PINE-DOC-11-DATA-MODEL`                                                             |
| Status           | Draft — pending review                                                               |
| Owner            | Engineering Review Board                                                             |
| Created          | 2026-07-08                                                                           |
| Last Updated     | 2026-07-08                                                                           |
| Specification Standard | Internal (DDD persistence, SQL/JSON contracts)                                 |
| Glossary         | `PINE-DOC-03-GLOSSARY`                                                               |
| Prerequisites    | `00-Vision`..`10-Domain-Model`                                                       |
| Decisions closed | None                                                                                 |
| Related Documents| `10-Domain-Model`, `14-Synchronization`, `15-Security`, `29-Deployment`, `31-Rule-Engine` |

---

## 1. Purpose

Define the *persistence* and *exchange* model of Pine: the SQLite
schema, the JSON contracts used for sync, the contracts used for
import and export, and the versioning rules for migrations.

## 2. Scope

In scope: SQLite schema, indexes, constraints, migration strategy,
JSON schemas for sync and for import/export, hash and integrity
fields, exchange-rate persistence.

Out of scope: domain behaviour → `10-Domain-Model`; AI/ML data
artifacts → `13-AI-Architecture`; plugin manifests →
`12-Plugin-Architecture`; threat model → `38-Threat-Model`.

## 3. Definitions

| Term         | Working definition                                                                                  |
|--------------|------------------------------------------------------------------------------------------------------|
| Schema       | The DDL and the JSON contracts of Pine's persistent state.                                            |
| Migration    | A versioned, reversible change to the schema.                                                         |
| Projector    | A read-model built from the write model.                                                             |
| Outbox       | A pattern that publishes domain events reliably after a write.                                       |
| Soft delete  | A row that is logically deleted but physically present (audit trail).                                |
| Hash         | A cryptographic hash used to detect tampering (default: SHA-256).                                     |

> **Assumption DA-A-1.** SQLite is the local store. Encryption-at-rest
> standard is deferred to `15-Security` (OD-03). This document
> specifies the *shape*; the *envelope* is a security decision.

## 4. SQLite Schema (logical)

The schema below is the *logical* shape; physical indexes, FTS
configurations and per-platform collations are owned by
`29-Deployment`.

### 4.1 Accounts

```sql
CREATE TABLE accounts (
    id                TEXT PRIMARY KEY,         -- UUID v7
    name              TEXT NOT NULL,
    subtype           TEXT NOT NULL,            -- cash | bank | card | investment | crypto | custom
    currency          TEXT NOT NULL,            -- ISO 4217 or crypto symbol
    opening_balance   TEXT NOT NULL,            -- decimal as string
    opening_date      TEXT NOT NULL,            -- ISO 8601 instant
    status            TEXT NOT NULL,            -- open | closed | archived
    statement_period  TEXT,                     -- optional
    metadata          TEXT,                     -- JSON, opaque
    created_at        TEXT NOT NULL,
    updated_at        TEXT NOT NULL,
    deleted_at        TEXT                      -- soft delete
);
```

### 4.2 Transactions

```sql
CREATE TABLE transactions (
    id                TEXT PRIMARY KEY,         -- UUID v7
    account_id        TEXT NOT NULL REFERENCES accounts(id),
    posted_at         TEXT NOT NULL,            -- ISO 8601 instant in user TZ
    amount            TEXT NOT NULL,            -- decimal as string, signed
    currency          TEXT NOT NULL,
    category          TEXT,                     -- hierarchical
    tags              TEXT,                     -- JSON array of strings
    payee             TEXT,
    notes             TEXT,
    source            TEXT NOT NULL,            -- manual | imported | recurring | split
    import_profile_id TEXT,                     -- nullable
    reconciled        INTEGER NOT NULL DEFAULT 0, -- 0/1
    attachments       TEXT,                     -- JSON array of attachment refs
    confidence        TEXT,                     -- JSON object of per-field confidence
    metadata          TEXT,
    created_at        TEXT NOT NULL,
    updated_at        TEXT NOT NULL,
    deleted_at        TEXT
);

CREATE TABLE transaction_splits (
    id                TEXT PRIMARY KEY,
    transaction_id    TEXT NOT NULL REFERENCES transactions(id) ON DELETE CASCADE,
    category          TEXT,
    amount            TEXT NOT NULL,
    currency          TEXT NOT NULL,
    memo              TEXT
);

CREATE INDEX idx_txn_account_posted ON transactions(account_id, posted_at);
CREATE INDEX idx_txn_category ON transactions(category);
CREATE INDEX idx_txn_deleted ON transactions(deleted_at);
```

### 4.3 Budgets

```sql
CREATE TABLE budgets (
    id                TEXT PRIMARY KEY,
    name              TEXT NOT NULL,
    categories        TEXT NOT NULL,            -- JSON array
    period_kind       TEXT NOT NULL,            -- week | month | quarter | year | custom
    period_expression TEXT,                     -- cron-like or RRULE
    amount            TEXT NOT NULL,
    currency          TEXT NOT NULL,
    soft_threshold    REAL,                     -- 0..1
    hard_threshold    REAL,                     -- 0..1, >= soft_threshold
    rollover_policy   TEXT,                     -- none | carry | cap
    enabled           INTEGER NOT NULL DEFAULT 1,
    created_at        TEXT NOT NULL,
    updated_at        TEXT NOT NULL,
    deleted_at        TEXT
);
```

### 4.4 Savings

```sql
CREATE TABLE savings_goals (
    id                TEXT PRIMARY KEY,
    name              TEXT NOT NULL,
    target_amount     TEXT NOT NULL,
    target_currency   TEXT NOT NULL,
    target_date       TEXT,                     -- nullable
    account_ids       TEXT NOT NULL,            -- JSON array
    status            TEXT NOT NULL,            -- active | achieved | abandoned
    created_at        TEXT NOT NULL,
    updated_at        TEXT NOT NULL,
    deleted_at        TEXT
);

CREATE TABLE savings_contributions (
    id                TEXT PRIMARY KEY,
    goal_id           TEXT NOT NULL REFERENCES savings_goals(id),
    transaction_id    TEXT REFERENCES transactions(id),
    amount            TEXT NOT NULL,
    currency          TEXT NOT NULL,
    posted_at         TEXT NOT NULL
);
```

### 4.5 Investments

```sql
CREATE TABLE investment_accounts (
    id                TEXT PRIMARY KEY,
    name              TEXT NOT NULL,
    custodian         TEXT,
    base_currency     TEXT NOT NULL,
    created_at        TEXT NOT NULL,
    updated_at        TEXT NOT NULL,
    deleted_at        TEXT
);

CREATE TABLE holdings (
    id                TEXT PRIMARY KEY,
    account_id        TEXT NOT NULL REFERENCES investment_accounts(id),
    asset_symbol      TEXT NOT NULL,            -- AAPL, BTC, etc.
    asset_class       TEXT NOT NULL,            -- stock | etf | crypto | mutual_fund | custom
    quantity          TEXT NOT NULL,            -- decimal as string
    cost_basis        TEXT NOT NULL,            -- decimal as string, in base_currency
    acquired_at       TEXT NOT NULL,
    price_source      TEXT,                     -- manual | built-in | plugin
    last_price        TEXT,
    last_price_at     TEXT,
    created_at        TEXT NOT NULL,
    updated_at        TEXT NOT NULL,
    deleted_at        TEXT
);

CREATE TABLE corporate_actions (
    id                TEXT PRIMARY KEY,
    holding_id        TEXT NOT NULL REFERENCES holdings(id),
    kind              TEXT NOT NULL,            -- split | dividend | merger | symbol_change
    payload           TEXT NOT NULL,            -- JSON
    effective_at      TEXT NOT NULL
);
```

### 4.6 Reports

```sql
CREATE TABLE report_definitions (
    id                TEXT PRIMARY KEY,
    name              TEXT NOT NULL,
    kind              TEXT NOT NULL,            -- builtin | custom
    spec              TEXT NOT NULL,            -- JSON
    schedule_cron     TEXT,
    output_format     TEXT NOT NULL,            -- csv | excel | pdf | json
    enabled           INTEGER NOT NULL DEFAULT 1,
    created_at        TEXT NOT NULL,
    updated_at        TEXT NOT NULL,
    deleted_at        TEXT
);
```

### 4.7 AI

```sql
CREATE TABLE ai_models (
    id                TEXT PRIMARY KEY,
    name              TEXT NOT NULL,
    type              TEXT NOT NULL,            -- ocr | categorisation | anomaly | recommendation | forecasting
    version           TEXT NOT NULL,            -- SemVer
    source            TEXT NOT NULL,            -- builtin | plugin | user
    manifest          TEXT NOT NULL,            -- JSON
    status            TEXT NOT NULL,            -- active | disabled
    created_at        TEXT NOT NULL,
    updated_at        TEXT NOT NULL
);

CREATE TABLE ai_predictions (
    id                TEXT PRIMARY KEY,
    model_id          TEXT NOT NULL REFERENCES ai_models(id),
    subject_kind      TEXT NOT NULL,            -- transaction | receipt | forecast | recommendation
    subject_id        TEXT,
    input_hash        TEXT NOT NULL,
    output            TEXT NOT NULL,            -- JSON
    confidence        REAL,
    explanation       TEXT,                     -- JSON
    created_at        TEXT NOT NULL
);
```

### 4.8 Sync

```sql
CREATE TABLE sync_states (
    id                TEXT PRIMARY KEY,         -- per provider
    provider_id       TEXT NOT NULL,
    last_sync_at      TEXT,
    pending_changeref TEXT,                     -- opaque
    last_error        TEXT,
    enabled           INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE sync_change_log (
    id                TEXT PRIMARY KEY,
    sync_id           TEXT NOT NULL REFERENCES sync_states(id),
    entity_kind       TEXT NOT NULL,            -- account | transaction | ...
    entity_id         TEXT NOT NULL,
    change_kind       TEXT NOT NULL,            -- created | updated | deleted
    payload_hash      TEXT NOT NULL,
    at                TEXT NOT NULL
);

CREATE TABLE sync_conflicts (
    id                TEXT PRIMARY KEY,
    sync_id           TEXT NOT NULL REFERENCES sync_states(id),
    entity_kind       TEXT NOT NULL,
    entity_id         TEXT NOT NULL,
    local_payload     TEXT NOT NULL,
    remote_payload    TEXT NOT NULL,
    resolution        TEXT,                     -- strategy applied + result
    resolved_at       TEXT
);
```

### 4.9 Plugins

```sql
CREATE TABLE plugin_installations (
    id                TEXT PRIMARY KEY,
    plugin_id         TEXT NOT NULL,
    version           TEXT NOT NULL,
    declared_perms    TEXT NOT NULL,            -- JSON array
    granted_perms     TEXT NOT NULL,            -- JSON array
    state             TEXT NOT NULL,            -- installed | disabled | uninstalled
    source            TEXT NOT NULL,            -- marketplace | file | git
    signature         TEXT,
    trust             TEXT,                     -- JSON
    installed_at      TEXT NOT NULL,
    updated_at        TEXT NOT NULL
);
```

### 4.10 Settings

```sql
CREATE TABLE user_settings (
    id                TEXT PRIMARY KEY,         -- one row
    storage_mode      TEXT NOT NULL,            -- local_only | local_plus_cloud | cloud_only
    sync_provider_id  TEXT,                     -- nullable
    security_profile  TEXT NOT NULL,            -- JSON
    ai_profile        TEXT NOT NULL,            -- JSON
    plugins_profile   TEXT NOT NULL,            -- JSON
    locale            TEXT NOT NULL,
    theme             TEXT NOT NULL,
    telemetry_consent INTEGER NOT NULL DEFAULT 0, -- 0/1
    updated_at        TEXT NOT NULL
);
```

### 4.11 Identity

```sql
CREATE TABLE users (
    id                TEXT PRIMARY KEY,
    display_name      TEXT,
    created_at        TEXT NOT NULL,
    updated_at        TEXT NOT NULL
);

CREATE TABLE credentials (
    id                TEXT PRIMARY KEY,
    user_id           TEXT NOT NULL REFERENCES users(id),
    kind              TEXT NOT NULL,            -- password | pin | biometric
    material          TEXT NOT NULL,            -- KDF-protected; never plaintext
    created_at        TEXT NOT NULL,
    last_used_at      TEXT
);

CREATE TABLE devices (
    id                TEXT PRIMARY KEY,
    user_id           TEXT NOT NULL REFERENCES users(id),
    name              TEXT NOT NULL,
    platform          TEXT NOT NULL,
    trusted           INTEGER NOT NULL DEFAULT 0,
    last_seen_at      TEXT NOT NULL
);

CREATE TABLE roles (
    id                TEXT PRIMARY KEY,
    user_id           TEXT NOT NULL REFERENCES users(id),
    role              TEXT NOT NULL,            -- owner | auditor | child | custom
    granted_at        TEXT NOT NULL,
    revoked_at        TEXT
);
```

### 4.12 Notifications

```sql
CREATE TABLE notification_channels (
    id                TEXT PRIMARY KEY,
    kind              TEXT NOT NULL,            -- system | in_app | email | push | custom
    destination       TEXT,                     -- opaque per kind
    enabled           INTEGER NOT NULL DEFAULT 1,
    created_at        TEXT NOT NULL,
    updated_at        TEXT NOT NULL,
    deleted_at        TEXT
);

CREATE TABLE notifications (
    id                TEXT PRIMARY KEY,
    channel_id        TEXT NOT NULL REFERENCES notification_channels(id),
    subject_kind      TEXT,
    subject_id        TEXT,
    payload           TEXT NOT NULL,            -- JSON
    status            TEXT NOT NULL,            -- pending | delivered | failed
    attempts          INTEGER NOT NULL DEFAULT 0,
    last_attempt_at   TEXT
);
```

### 4.13 Import/Export

```sql
CREATE TABLE import_profiles (
    id                TEXT PRIMARY KEY,
    name              TEXT NOT NULL,
    source_format     TEXT NOT NULL,            -- csv | excel | pdf | ofx | qfx | bank_statement
    field_mapping     TEXT NOT NULL,            -- JSON
    transformations   TEXT,                     -- JSON
    schedule_cron     TEXT,
    created_at        TEXT NOT NULL,
    updated_at        TEXT NOT NULL
);

CREATE TABLE export_profiles (
    id                TEXT PRIMARY KEY,
    name              TEXT NOT NULL,
    target_format     TEXT NOT NULL,            -- csv | excel | pdf | json
    scope             TEXT NOT NULL,            -- JSON spec
    schedule_cron     TEXT,
    created_at        TEXT NOT NULL,
    updated_at        TEXT NOT NULL
);
```

### 4.14 Rule Engine

```sql
CREATE TABLE rules (
    id                TEXT PRIMARY KEY,
    name              TEXT NOT NULL,
    trigger           TEXT NOT NULL,            -- JSON
    condition         TEXT NOT NULL,            -- JSON (a specification)
    action            TEXT NOT NULL,            -- JSON
    enabled           INTEGER NOT NULL DEFAULT 1,
    created_at        TEXT NOT NULL,
    updated_at        TEXT NOT NULL
);

CREATE TABLE rule_audit (
    id                TEXT PRIMARY KEY,
    rule_id           TEXT NOT NULL REFERENCES rules(id),
    fired_at          TEXT NOT NULL,
    trigger_payload   TEXT,
    condition_result  TEXT,
    action_result     TEXT
);
```

### 4.15 Forecasting

```sql
CREATE TABLE forecasts (
    id                TEXT PRIMARY KEY,
    scenario          TEXT NOT NULL,            -- JSON
    model_id          TEXT NOT NULL REFERENCES ai_models(id),
    data_window_from  TEXT NOT NULL,
    data_window_to    TEXT NOT NULL,
    output            TEXT NOT NULL,            -- JSON
    explanation       TEXT NOT NULL,            -- JSON
    generated_at      TEXT NOT NULL
);
```

### 4.16 Exchange Rates

```sql
CREATE TABLE exchange_rates (
    id                TEXT PRIMARY KEY,
    from_currency     TEXT NOT NULL,
    to_currency       TEXT NOT NULL,
    rate              TEXT NOT NULL,            -- decimal as string
    as_of             TEXT NOT NULL,            -- ISO 8601 instant
    source            TEXT NOT NULL
);
CREATE INDEX idx_fx_pair_asof ON exchange_rates(from_currency, to_currency, as_of DESC);
```

## 5. Migrations

- Migrations are *versioned* and *reversible*; each migration has an
  `up` and a `down` script.
- The migration version is stored in `PRAGMA user_version` (or an
  equivalent table).
- Migrations MUST be idempotent and MUST NOT require user data
  deletion.
- Migrations MUST be tested with synthetic data in CI.

## 6. JSON Contracts (sync and exchange)

The JSON contracts below are *informative*; the authoritative
schemas are versioned in `/schemas/` of the repository. All
identifiers are UUID v7 strings. All timestamps are ISO 8601
instants in UTC; the user's timezone is supplied separately.

### 6.1 Sync Envelope

```json
{
  "schema": "pine.sync.v1",
  "device_id": "...",
  "user_id": "...",
  "generated_at": "...",
  "change_set": [
    { "entity": "transaction", "id": "...", "op": "upsert", "payload": { "...": "..." } },
    { "entity": "transaction", "id": "...", "op": "delete" }
  ],
  "cursor": "...",
  "signature": "..."
}
```

- The envelope is encrypted client-side; the sync provider sees only
  ciphertext.
- The `signature` is over the encrypted blob, not the plaintext.

### 6.2 Import (CSV example)

```json
{
  "schema": "pine.import.csv.v1",
  "source": { "format": "csv", "has_header": true, "delimiter": "," },
  "columns": [
    { "source": "Date",        "target": "posted_at", "format": "iso8601" },
    { "source": "Description", "target": "payee" },
    { "source": "Amount",      "target": "amount" },
    { "source": "Currency",    "target": "currency" }
  ],
  "dedupe": { "on": ["account_id", "posted_at", "amount", "payee"] }
}
```

### 6.3 Export (JSON example)

```json
{
  "schema": "pine.export.json.v1",
  "generated_at": "...",
  "scope": "all",
  "accounts":      [ { "...": "..." } ],
  "transactions":  [ { "...": "..." } ],
  "budgets":       [ { "...": "..." } ],
  "holdings":      [ { "...": "..." } ],
  "rules":         [ { "...": "..." } ],
  "forecasts":     [ { "...": "..." } ]
}
```

## 7. Integrity, Hashes, and Audit

- Every export computes a SHA-256 hash and embeds it.
- Every rule firing records a `rule_audit` row.
- Every change to a `Holding` records a `corporate_actions` or a
  revaluation row.
- `users`, `credentials`, `roles` and `devices` are append-only
  audit-wise; soft-deletes are preferred to hard deletes.

## 8. Data-Level Assumptions

| ID          | Assumption                                                                                            |
|-------------|--------------------------------------------------------------------------------------------------------|
| DA-A-1      | The encryption envelope is applied at the database level (OD-03) and is transparent to the schema.      |
| DA-A-2      | UUID v7 is used for all entity identifiers (sortable, collision-resistant).                            |
| DA-A-3      | ~~Money is a decimal string; no floating point is persisted.~~ **Superseded by TD-31.** Money is persisted as a SQLite `INTEGER` count of ten-thousandths — a fixed scale of 4, which is the largest number of minor units ISO 4217 defines. The "no floating point" half stands and is stronger than before: an integer column compares and sorts correctly in SQL, whereas the decimal string it replaced compared lexicographically. The scale is fixed rather than per-currency because an EF value converter sees a single column and cannot read the sibling currency column. |
| DA-A-4      | ~~Timestamps are stored as ISO 8601 strings in UTC~~ **Partly false, and the correction matters.** `Transaction.Date` is a `DateTimeOffset` persisted with the user's own offset appended, because the offset records when the money was actually spent and normalising it away moves displayed dates across a day boundary. Ordering therefore uses a separate `DateUtc` column holding the same instant in UTC (TD-94). Other timestamp columns are written in UTC by the domain. Conversion to the user's timezone remains a presentation concern. |
| DA-A-5      | Soft delete is the default for user data; hard delete is reserved for GDPR / right-to-erasure flows.   |

## 9. Deferred Data Decisions

| ID     | Decision                                                                                  | Owning document(s)         |
|--------|-------------------------------------------------------------------------------------------|----------------------------|
| DA-D-1 | Encryption at rest standard (SQLCipher vs. application-level envelope).                  | `15-Security`              |
| DA-D-2 | FTS configuration for transaction search.                                                 | `29-Deployment`            |
| DA-D-3 | Per-platform collation rules.                                                              | `29-Deployment`            |
| DA-D-4 | Backup file format and integrity footer.                                                   | `15-Security`              |

## 10. Quality Checklist

- [x] Logical schema for every bounded context.
- [x] Indexes for the critical read paths (account+posted, category,
      FX pair+asof).
- [x] Migration rules and CI testing.
- [x] JSON contracts for sync, import and export.
- [x] Hashing, audit and integrity rules.
- [x] No TODO placeholders.
- [x] No contradiction with `00-Vision`..`10-Domain-Model`.

## 11. Revision History

| Version | Date       | Author                | Change                                                       |
|---------|------------|-----------------------|--------------------------------------------------------------|
| 0.1     | 2026-07-08 | Engineering Review Board | Initial data model.                                       |
