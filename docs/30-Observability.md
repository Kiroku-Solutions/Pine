# 30 — Observability

| Field            | Value                                                                                |
|------------------|--------------------------------------------------------------------------------------|
| Document ID      | `PINE-DOC-30-OBSERVABILITY`                                                          |
| Status           | Partially implemented — logging is real; metrics, traces and the DIB are not         |
| Reconciled       | 2026-10-12 · §4.4: log encryption at rest is stated as the honest ceiling, not a gap — pre-unlock logs stay plaintext and strictly redacted, by value shape and by field name (TD-82). Earlier: 2026-08-31 · The JSONL sink, the record shape, the level vocabulary, redaction, rotation and retention are implemented (TD-14, TD-15). §4.4 (log encryption), §5, §7, §8 and §9 are already annotated aspirational in place (TD-82, TD-84). **2026-10-05 · ADR-0021 withdrew the plugin host; the DIB's plugin-inventory content in §8 and §9.2 is struck through, not carried forward as aspirational, because there is no plugin host left for it to ever describe (TD-84, `62-Post-Audit-Plan` §7 item 4.2). §9's remaining aspirational contents — sync run summaries, import summaries, the SLO budget summary and the signature — are unaffected and still wait on their own subsystems.** |
| Owner            | Engineering Review Board                                                             |
| Created          | 2026-07-08                                                                           |
| Last Updated     | 2026-07-08                                                                           |
| Specification Standard | Internal (observability practice)                                              |
| Glossary         | `PINE-DOC-03-GLOSSARY`                                                               |
| Prerequisites    | `00-Vision`..`29-Deployment`                                                         |
| Related Documents| `13-AI-Architecture`, `15-Security`, `22-Release-Plan`, `29-Deployment`, `39-Performance` |

---

## 1. Purpose

Define how Pine *observes itself*: logging, metrics, tracing, error
reporting, local debug surfacing, and the diagnostic bundle. The
observability posture respects the *local-first* and *opt-in
telemetry* principles (per `00-Vision` §5 and ADR-0012).

## 2. Scope

In scope: logging format, log levels, metrics surface, tracing
infrastructure, error reporting (opt-in), local debug surfacing,
diagnostics bundle, privacy posture.

Out of scope: deployment signals → `29-Deployment` §11; AI
inference telemetry → `13-AI-Architecture` §11; performance
budgets → `39-Performance`; security event logging → `15-Security`
§14.

## 3. Definitions

| Term             | Working definition                                                                                |
|------------------|------------------------------------------------------------------------------------------------------|
| Log              | A timestamped record of an event.                                                                   |
| Metric           | A numerical measurement with a name and labels.                                                     |
| Trace            | A causal chain of spans representing a request or job.                                              |
| Span             | A unit of work in a trace.                                                                          |
| PII              | Personally Identifiable Information (see `15-Security` §3).                                          |
| DIB              | Diagnostic Information Bundle: a redacted, signed artefact sent to the user / maintainers.         |
| SLO              | Service Level Objective (see `08-NonFunctional-Requirements` §11).                                  |
| SLI              | Service Level Indicator: a raw measurement used to evaluate an SLO.                                 |

> **Assumption OBS-A-1.** Observability exists to *protect the
> user*, not to extract value from them. A measurement that does
> not improve the user experience is excluded.

## 4. Logging

### 4.1 Format

- Logs are *structured* (JSON).
- A log record MUST contain:
  - `timestamp` (RFC 3339, UTC).
  - `level` (`trace`, `debug`, `info`, `warn`, `error`).
  - `event` (a stable, dotted, snake_case identifier).
  - `version` (the application version).
  - `trace_id` (if a trace is active).
  - `span_id` (if a span is active).
- A log record MAY contain a `context` object with sanitised
  runtime fields.

### 4.2 Levels

| Level     | Use                                                                     |
|-----------|--------------------------------------------------------------------------|
| trace     | Per-detail (e.g. SQL parameter values)                                    |
| debug     | Diagnostic for maintainers                                               |
| info      | Lifecycle events                                                        |
| warn      | Recoverable anomalies                                                    |
| error     | User-affecting failures                                                  |
| fatal     | Application cannot continue                                              |

### 4.3 Privacy Rules

- A log MUST NOT include a `PII` field (see `15-Security` §3 and
  §10).
- A log MUST NOT include raw transaction descriptions, payee names,
  categories, amounts, or dates without aggregation or
  hash-anonymisation.
- A log MUST NOT include a path on the user's filesystem that
  crosses the data directory.
- A log MUST NOT include the user's account ID, profile name, or
  sync provider ID.

### 4.4 Local Storage

- Logs are stored under `<profile>/logs/<date>.log.jsonl`.
- Logs rotate by size (per default: 50 MB per file, 10 files).
- Logs are encrypted at rest using the same key as the data store.
  **Status: Decided — this is the honest ceiling, not a gap (TD-82, decision of
  2026-10-07).** Pre-unlock logs stay plaintext, because encrypting before a key
  exists is structurally impossible, and are instead held to strict redaction of
  personal data. Not implemented, and deliberately so as of
  iteration 11 of `60-Production-Readiness-Audit`. The records that matter most —
  startup, KDF failure, crash, every error before the vault is unlocked — exist
  precisely when no data-store key is available, so encrypting with it would
  cover the least important half of the log. The shipped posture is instead:
  redaction at the boundary (§4.3, enforced by `LogRedactor`) plus a location
  outside the vault. The redaction is enforced at two levels, both tested
  (`PreUnlockLogRedactionTests`): by VALUE SHAPE (paths, e-mail, amounts, long
  digit runs) and by NAME, because a payee, an account name or a passphrase has
  no shape — a structured field whose key names personal data (`Payee`,
  `AccountName`, `Passphrase`, `Amount`, `Path`, …) is written as `<redacted>`,
  a `decimal` is always `<amount>`, and `key=value` / `key: value` text with such
  a key has its value replaced (an unquoted value runs to the next `word=` /
  `word:` token, a `,` or `;`, or the end of the line, so
  `payee=Starbucks Reserve Roastery` is masked whole). This clause is therefore reworded to what is
  guaranteed: **logs are plaintext, outside the vault, and free of personal
  data.** A post-unlock encrypted sink is not planned.
- A log file older than the user's retention preference is deleted.
  **Status: Partially implemented.** Retention is by file COUNT (10 files, above),
  not by age, and there is no user preference to read: no such setting exists in
  the app, and `IOptions`-based configuration is itself still TD-27.

## 5. Metrics

### 5.1 Surface

Metrics are exposed via:

- A local HTTP endpoint on `127.0.0.1` only, protected by a per-
  profile token (for maintainer use).
- An OpenTelemetry-compatible exporter when telemetry is opted-in
  per §9.

### 5.2 Cardinality Discipline

- A metric label MUST be enumerated or low-cardinality.
- A metric label MUST NOT include a `PII` field.
- A metric label MUST NOT include raw transaction details.
- A metric label MAY include a hash (e.g. `account_hash`) for
  per-account analysis.

### 5.3 SLO-Backed Metrics

The metrics used to evaluate the SLOs in `08-NonFunctional-
Requirements` §11 are:

| SLO          | Metric                                                  |
|--------------|----------------------------------------------------------|
| Launch SLO    | `pine.launch.duration_ms` histogram                      |
| UI SLO        | `pine.ui.interaction.duration_ms` histogram, p95        |
| Sync SLO      | `pine.sync.run.duration_ms` histogram, p95               |
| Import SLO    | `pine.import.doc.duration_ms` histogram, p95             |
| AI inference  | `pine.ai.inference.duration_ms` histogram, p95          |
| Crash-free    | `pine.crash.count` counter                               |

## 6. Tracing

### 6.1 Infrastructure

- Traces follow OpenTelemetry conventions.
- A span is created for each: launch, sync run, import, AI
  inference, plugin invocation.
- A trace is correlated to logs via `trace_id` and `span_id`.

### 6.2 In-Process Tracing

- In-process spans have low overhead.
- Sampling is *head-based* (e.g. 1% by default) with
  *always-on* sampling for errors.

### 6.3 Cross-Process Tracing

- Cross-process traces (e.g. plugin invocation) carry a context
  header.
- The plugin SDK is responsible for propagating the context.

## 7. Error Reporting

- Error reports are *opt-in* (`13-Diagnostics` flag in user
  settings).
- An error report is a *DIB* (§9) uploaded to the project-mirrored
  endpoint.
- An error report is anonymised before upload (§9).
- A user may attach their *account-hash* to a report to enable
  reproduction; the user is informed that the report contains the
  hash.
- Pine does not send a crash report without user opt-in.

## 8. Local Debug

- The application surfaces a `Diagnostics` panel in the settings.
- The Diagnostics panel shows:
  - Log tail. **Status: Implemented** as of iteration 12 of
    `60-Production-Readiness-Audit` (TD-15). `ILogTailReader` reads the newest
    records back out of the §4.4 files and the panel renders them; an empty log
    is shown as an empty state rather than padded with a synthetic record.
  - SLO budget status (last 30 days). **Status: Aspirational.** §7's error
    budgets are not computed anywhere; there is no SLO subsystem to query.
  - Local metrics summary. **Status: Aspirational.** §5 is unimplemented.
  - ~~Plugin inventory.~~ **Removed, 2026-10-05 (ADR-0021; `62-Post-Audit-Plan`
    §7 item 4.2; register: TD-84).** This was never merely unbuilt — ADR-0021
    withdrew the plugin host Pine was going to run, so there is no inventory of
    loaded plugins that will ever exist to show. Struck rather than left
    "Aspirational," which would say it is still coming.
  - Sync run history. **Status: Aspirational.** Sync does not exist, and
    `60-Production-Readiness-Audit` §10 sequences it behind row versions,
    audit columns and stable natural keys.
- The Diagnostics panel may export the DIB (§9) to the user's
  clipboard or filesystem. **Status: Aspirational (TD-84).** The panel used to
  carry an `Export DIB` button whose handler appended a log line and was
  otherwise `// To be implemented`. It was removed rather than reimplemented in
  iteration 12: four of the seven §9.2 contents have no source (see §9), so a
  button under that name could only export something other than a DIB.

## 9. Diagnostic Information Bundle (DIB)

**Status: Aspirational (TD-84); re-scoped 2026-10-05 (`62-Post-Audit-Plan` §7
item 4.2).** No DIB is produced by any code path as of iteration 12 of
`60-Production-Readiness-Audit`. Of §9.2's original eight contents, three have
no subsystem behind them yet but could in principle still be built — sync run
summaries, import summaries and the SLO budget summary — and §9.3's signature
has no signing key, no key distribution and no verification path. **A fourth,
the plugin inventory, is not merely unbuilt: ADR-0021 removed the plugin host
it would have described, so there is nothing for that bullet to ever report.**
It is struck from §9.2 below rather than carried forward as aspirational. The
two contents that *are* available today (application version and OS/runtime
versions via `IRuntimeInfoProvider`, and the log tail via `ILogTailReader`) are
surfaced directly on the Diagnostics panel instead. This clause is retained as
the target shape, not as a description of the build.

### 9.1 Purpose

A user-generated bundle the user (or, with consent, a maintainer)
can use to reproduce or diagnose an issue without exposing user
data.

### 9.2 Contents

A DIB contains:

- Application version and channel.
- OS and runtime versions.
- Last N log lines (info-level and above).
- Last N sync run summaries (counts only; no payloads).
- Last N import summaries (counts only).
- SLO budget summary (last 30 days).
- ~~Plugin inventory (id, version, source, permissions, risk score).~~
  **Removed, 2026-10-05 (ADR-0021; `62-Post-Audit-Plan` §7 item 4.2; register:
  TD-84).** Pine compiles in everything it runs; there is no third-party
  plugin and nothing loaded from outside the application to inventory.
- Reproduction hints (e.g. "the user reproduced by importing
  a CSV with format X").

A DIB MUST NOT contain:

- Transaction content (descriptions, payees, categories, amounts).
- Account names or numbers.
- Sync provider credentials or tokens.
- File paths inside the data directory beyond the bundle's own
  path.
- Logs that include PII by accident.

### 9.3 Format

A DIB is a signed ZIP with `manifest.json` and `signature.sig`.

### 9.4 Privacy Posture

- A DIB uploaded to the project endpoints is stored in the EU
  region (per `00-Vision` §7).
- A DIB is retained for ≤ 90 days unless the user is in an active
  support thread.

## 10. Telemetry Opt-In

- The telemetry switch is *off by default* (ADR-0012).
- The switch lives in user settings under `Privacy`.
- The switch covers: error reports, SLO metrics, AI inference
  signals (per `13-AI-Architecture`).
- The switch is *granular*: a user may opt into errors but not
  metrics.
- The switch is revocable at any time.
- A change to the switch takes effect on next launch.

## 11. Server-Side Observability (Pine Cloud, deferred)

When Pine Cloud exists (deferred OD-D-3), observability will be:

- Prometheus-compatible metrics.
- Loki-compatible logs.
- Tempo-compatible traces.
- OpenTelemetry export.

Pine Cloud observability uses the same SLO set as the client.

## 12. Cross-Cutting Constraints

- An observability surface MUST NOT include PII.
- An observability surface MUST be opt-in for *out-of-device*
  delivery.
- An observability surface MUST be reproducible from logs alone;
  the SLO metrics MUST be derivable from logs in postmortems.
- An observability surface MUST NOT depend on a vendor SaaS
  without a documented purpose.

## 13. Deferred Decisions

| ID       | Decision                                                                                  | Owning document(s)         |
|----------|-------------------------------------------------------------------------------------------|----------------------------|
| OBS-D-1  | Whether the local HTTP metrics endpoint is enabled by default for the in-app Diagnostics panel. | `30-Observability` (next rev.) |
| OBS-D-2  | DIB retention default (90 vs. 180 days).                                                   | `30-Observability` (next rev.) |
| OBS-D-3  | Vendor choice for the project-mirrored endpoint (self-hosted vs. managed).                  | `30-Observability` (next rev.) |
| OBS-D-4  | Head-based sampling rate default.                                                          | `30-Observability` (next rev.) |
| OBS-D-5  | Whether to publish a real-time public SLO dashboard.                                        | `30-Observability` (next rev.) |

## 14. Quality Checklist

- [x] Logging, metrics, tracing, error reporting, local debug,
      DIB, telemetry opt-in, server-side observability are all
      present.
- [x] Privacy posture is consistent with ADR-0012 and `15-Security`.
- [x] No TODO placeholders.
- [x] No contradiction with `00-Vision`..`29-Deployment`.

## 15. Revision History

| Version | Date       | Author                | Change                                                       |
|---------|------------|-----------------------|--------------------------------------------------------------|
| 0.1     | 2026-07-08 | Engineering Review Board | Initial observability document.                            |
