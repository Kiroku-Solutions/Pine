# 08 — Non-Functional Requirements

| Field            | Value                                                                                |
|------------------|--------------------------------------------------------------------------------------|
| Document ID      | `PINE-DOC-08-NONFUNCTIONAL-REQUIREMENTS`                                             |
| Status           | Draft — pending review                                                               |
| Owner            | Engineering Review Board                                                             |
| Created          | 2026-07-08                                                                           |
| Last Updated     | 2026-07-08                                                                           |
| Specification Standard | IEEE 29148 (non-functional requirements)                                       |
| Glossary         | `PINE-DOC-03-GLOSSARY`                                                               |
| Prerequisites    | `00-Vision`, `01-Business-Requirements`, `02-Product-Scope`, `07-Functional-Requirements` |
| Related Documents| `16-Quality-Attributes`, `18-Test-Strategy`, `39-Performance`, `40-Accessibility`, `15-Security` |

---

## 1. Purpose

Specify, in normative language (RFC 2119), the *quality* attributes
Pine must satisfy: performance, reliability, scalability, security,
accessibility, usability, testability, modularity, portability,
observability, resilience, maintainability, internationalisation and
data protection. Each NFR is uniquely identified (`NFR-*`),
quantified where possible, and traceable to a test in
`18-Test-Strategy`.

## 2. Scope

In scope: every quality attribute the project committed to in the
Vision and Business Requirements.

Out of scope: functional behaviour → `07-Functional-Requirements`;
architecture → `09-System-Architecture`; security policies →
`15-Security`; threat model → `38-Threat-Model`; performance budgets →
`39-Performance`; accessibility conformance → `40-Accessibility`.

## 3. Definitions

| Term | Working definition                                                                                  |
|------|------------------------------------------------------------------------------------------------------|
| NFR  | Non-Functional Requirement, identified `NFR-*`.                                                      |
| SLO  | Service Level Objective — a numeric reliability or performance target.                              |
| p50, p95, p99 | Latency percentiles measured at the system boundary.                                    |
| RPO  | Recovery Point Objective — maximum acceptable data loss in a failure.                                |
| RTO  | Recovery Time Objective — maximum acceptable downtime in a failure.                                  |

> **Assumption NFR-A-1.** Where an NFR is below, "the system" means
> Pine running on the minimum supported configuration, defined in
> `09-System-Architecture` (OD-08 closed in §4).

## 4. NFR Template

| Field        | Description                                                                       |
|--------------|-----------------------------------------------------------------------------------|
| ID           | `NFR-*`                                                                           |
| Title        | Attribute + target                                                                |
| Category     | Performance, Reliability, Security, Accessibility, etc.                           |
| Statement    | One normative statement (RFC 2119)                                                |
| Target       | Numeric, where possible                                                           |
| Measurement  | How the target is measured                                                        |
| Rationale    | Why the target matters                                                            |
| Test link    | Pointer to test in `18-Test-Strategy` / `49-Test-Cases`                           |
| Traced principles | Vision principles invoked                                                  |

## 5. Performance

| ID            | Statement (abridged)                                                                                  | Target                          | Measurement                                       |
|---------------|--------------------------------------------------------------------------------------------------------|----------------------------------|---------------------------------------------------|
| NFR-PERF-01   | The system MUST launch to a usable dashboard in less than N seconds on a reference device.            | 3 s (cold), 1 s (warm) on a 4-year-old mainstream laptop; 2 s (cold) on a 3-year-old mainstream phone. | Cold-start timer, warm-start timer. |
| NFR-PERF-02   | The dashboard MUST become interactive in less than N seconds.                                          | 1 s after launch on the same hardware. | Time-to-interactive timer. |
| NFR-PERF-03   | Transaction search across N years of data MUST return in less than N ms at p95.                       | 250 ms at p95 for 10 years of synthetic data. | Search latency benchmark. |
| NFR-PERF-04   | Receipt scanning on a mainstream phone MUST complete in less than N seconds.                          | 6 s end-to-end on a 3-year-old mid-range phone. | Receipt scan benchmark. |
| NFR-PERF-05   | Sync of a N MB change set MUST complete in less than N seconds on a typical broadband link.          | 10 s for 1 MB change set on a 50 Mbps link. | Sync benchmark. |
| NFR-PERF-06   | Memory footprint MUST stay under N MB on a reference device for the dashboard.                        | 300 MB resident set on desktop; 150 MB on mobile. | Memory benchmark. |
| NFR-PERF-07   | The system MUST NOT block the UI thread for more than N ms for any user-visible operation.            | 100 ms (target 16 ms / 60 fps). | UI-thread blocking detector. |
| NFR-PERF-08   | The system MUST throttle background work when the device is on battery.                              | Battery-aware scheduler enabled. | Battery benchmark. |

**Rationale.** Performance is the *quality* face of "everything must
continue working offline" — slow local code is still local, and
local-first is meaningless if local is unusable.

## 6. Reliability and Resilience

| ID            | Statement                                                                                              | Target                          | Measurement                                       |
|---------------|--------------------------------------------------------------------------------------------------------|----------------------------------|---------------------------------------------------|
| NFR-REL-01    | The system MUST be usable offline for every core feature.                                              | 100 % of FR-CORE-* on offline.   | Offline test matrix.                              |
| NFR-REL-02    | The system MUST NOT lose user data on a crash or power loss.                                          | 0 data loss on hard kill.        | Crash-injection test.                             |
| NFR-REL-03    | The system MUST recover from a corrupted local database.                                               | 100 % recovery for the user's last committed state. | Corruption-injection test. |
| NFR-REL-04    | The system MUST emit structured logs, metrics and traces for every failure.                            | 100 % of exceptions traced.      | Log audit.                                        |
| NFR-REL-05    | The system MUST have a documented incident response runbook.                                          | Runbook present and reviewed.   | Runbook review.                                   |
| NFR-REL-06    | The system MUST support RPO and RTO targets documented in `54-Maintenance`.                            | RPO 0 for committed data; RTO 1 h for a single device. | DR drill.                                         |
| NFR-REL-07    | The system MUST degrade gracefully when a plugin is missing or broken.                                | 100 % of FR-PLUGIN-* tested with missing plugin. | Plugin-degradation test. |

## 7. Security

| ID            | Statement                                                                                              | Target                          | Measurement                                       |
|---------------|--------------------------------------------------------------------------------------------------------|----------------------------------|---------------------------------------------------|
| NFR-SEC-01    | The system MUST encrypt user data at rest.                                                            | AES-256 (or stronger) at rest.   | Code review + audit.                              |
| NFR-SEC-02    | The system MUST encrypt user data in transit.                                                          | TLS 1.3 (or stronger) for any network call. | TLS configuration test.                          |
| NFR-SEC-03    | The system MUST NOT log secrets, PII or financial data.                                               | 0 leaks in CI.                   | Log audit.                                        |
| NFR-SEC-04    | The system MUST support PIN, password and biometric authentication.                                    | 100 % of FR-SEC-01 covered.     | Auth test matrix.                                 |
| NFR-SEC-05    | The system MUST support independent security audit.                                                   | Audit completed before v1.0.0 GA. | Audit report.                                     |
| NFR-SEC-06    | The system MUST publish a public threat model.                                                        | `38-Threat-Model` published and current. | Public artifact.                                |
| NFR-SEC-07    | The system MUST follow responsible disclosure.                                                        | security@pine mailbox + security.txt. | Public policy.                                    |
| NFR-SEC-08    | The system MUST NOT include any code that bypasses authentication or encryption.                      | 0 bypasses in CI gate.           | Static analysis + review.                         |

## 8. Accessibility

| ID            | Statement                                                                                              | Target                          | Measurement                                       |
|---------------|--------------------------------------------------------------------------------------------------------|----------------------------------|---------------------------------------------------|
| NFR-A11Y-01   | Built-in UI MUST meet WCAG 2.2 AA.                                                                     | 0 critical / serious violations. | Automated + manual audit.                         |
| NFR-A11Y-02   | Every interactive element MUST be reachable and operable via keyboard.                                | 100 % keyboard coverage.        | Keyboard test.                                    |
| NFR-A11Y-03   | The system MUST support platform screen readers.                                                       | 100 % of built-in UI announced.  | Screen-reader test.                               |
| NFR-A11Y-04   | The system MUST respect platform font-size and DPI settings.                                          | UI scales 100 %..200 % without truncation. | DPI test.                              |
| NFR-A11Y-05   | State MUST NOT be conveyed by colour alone.                                                            | 0 colour-alone cases.            | A11y review.                                      |
| NFR-A11Y-06   | The system MUST provide text alternatives for any non-text content it generates.                      | 100 % coverage.                  | A11y test.                                        |

## 9. Internationalisation

| ID            | Statement                                                                                              | Target                          | Measurement                                       |
|---------------|--------------------------------------------------------------------------------------------------------|----------------------------------|---------------------------------------------------|
| NFR-I18N-01   | Every user-visible string MUST be externalised.                                                        | 100 % externalised.              | String audit.                                     |
| NFR-I18N-02   | The system MUST render RTL locales end-to-end in built-in UI.                                          | 0 visual regressions in RTL.     | RTL snapshot tests.                               |
| NFR-I18N-03   | The system MUST format numbers, dates, calendars, currencies and timezones per locale.                 | Conformance to CLDR for the supported locales. | Locale test matrix.                              |
| NFR-I18N-04   | The system MUST allow adding a new locale without code changes.                                        | New locale = translation files only. | Locale-add test.                                  |

## 10. Usability

| ID            | Statement                                                                                              | Target                          | Measurement                                       |
|---------------|--------------------------------------------------------------------------------------------------------|----------------------------------|---------------------------------------------------|
| NFR-UX-01     | The system MUST provide a non-blocking first-run flow.                                                 | 0 mandatory modals.              | First-run usability test.                         |
| NFR-UX-02     | The system MUST provide undo for any destructive action.                                               | 100 % of destructive actions undoable for N seconds. | Undo test.                |
| NFR-UX-03     | The system MUST support keyboard shortcuts documented in `42-UX-Guidelines`.                           | 100 % of common actions have a shortcut. | Shortcut test.                                |
| NFR-UX-04     | The system MUST NOT use dark patterns.                                                                 | 0 dark patterns in review.       | UX review.                                        |

## 11. Testability

| ID            | Statement                                                                                              | Target                          | Measurement                                       |
|---------------|--------------------------------------------------------------------------------------------------------|----------------------------------|---------------------------------------------------|
| NFR-TST-01    | The system MUST achieve ≥ 90 % line + branch coverage in CI for the core packages.                    | 90 % gate, warnings as errors.   | CI report.                                        |
| NFR-TST-02    | The system MUST achieve a mutation score ≥ 70 % on the domain layer.                                  | 70 % gate.                       | Mutation report.                                  |
| NFR-TST-03    | The system MUST have zero compiler warnings with warnings-as-errors enabled.                            | 0 warnings.                      | CI report.                                        |
| NFR-TST-04    | The system MUST have architecture tests that assert dependency direction and module boundaries.       | 0 violations.                    | Arch tests.                                       |
| NFR-TST-05    | The system MUST run an automated accessibility audit in CI.                                           | 0 critical / serious issues.     | CI report.                                        |
| NFR-TST-06    | The system MUST run an automated performance benchmark in CI.                                          | Regressions > 10 % fail the build. | Benchmark report.                                |

## 12. Maintainability

| ID            | Statement                                                                                              | Target                          | Measurement                                       |
|---------------|--------------------------------------------------------------------------------------------------------|----------------------------------|---------------------------------------------------|
| NFR-MAINT-01  | The system MUST follow the coding standards in `20-Coding-Standards`.                                  | 0 violations in CI.              | Lint + style report.                              |
| NFR-MAINT-02  | The system MUST use ADRs for every significant decision.                                               | ADR index current.               | `25-ADR` audit.                                   |
| NFR-MAINT-03  | The system MUST use Conventional Commits.                                                              | 100 % of commits conform.        | Commit lint.                                      |
| NFR-MAINT-04  | The system MUST use Semantic Versioning for the core and for the Plugin SDK.                            | 100 % of releases SemVer.        | Tag audit.                                        |
| NFR-MAINT-05  | The system MUST have a public changelog.                                                                | Changelog current at all times.  | Changelog audit.                                  |

## 13. Modularity and Portability

| ID            | Statement                                                                                              | Target                          | Measurement                                       |
|---------------|--------------------------------------------------------------------------------------------------------|----------------------------------|---------------------------------------------------|
| NFR-MOD-01    | The system MUST isolate plugins in a documented boundary.                                              | 100 % of plugins in boundary.    | Plugin isolation test.                            |
| NFR-MOD-02    | The system MUST allow replacing any AI model without code changes.                                     | 100 % model replacement via plugin only. | Model swap test.                                |
| NFR-MOD-03    | The system MUST support the five target platforms at parity.                                           | No platform-specific SCOPE-* drop. | Per-platform test matrix.                        |
| NFR-MOD-04    | The system MUST be buildable from source on all five platforms.                                        | Reproducible builds on all five. | Build matrix.                                     |
| NFR-MOD-05    | The system MUST be signable for distribution on all five platforms.                                   | Signed artefacts on all five.    | Signing pipeline.                                 |

## 14. Observability

| ID            | Statement                                                                                              | Target                          | Measurement                                       |
|---------------|--------------------------------------------------------------------------------------------------------|----------------------------------|---------------------------------------------------|
| NFR-OBS-01    | The system MUST emit structured logs.                                                                  | 100 % of failures logged with correlation id. | Log audit.                |
| NFR-OBS-02    | The system MUST emit metrics for performance counters (CPU, memory, IO, GPU when available).            | All platforms.                   | Metric export.                                    |
| NFR-OBS-03    | The system MUST emit traces for cross-module flows.                                                    | Receipt flow, sync flow, import flow. | Trace export.                            |
| NFR-OBS-04    | Telemetry MUST be off by default and opt-in.                                                            | Default = off, opt-in only.      | Config test.                                      |
| NFR-OBS-05    | The system MUST allow the user to inspect what would be sent before opting in.                          | Preview available.                | UX review.                                        |
| NFR-OBS-06    | The system MUST support a local-only diagnostics bundle for support.                                   | Bundle available, no network call required. | Diagnostics test.                         |

## 15. Data Protection and Privacy

| ID            | Statement                                                                                              | Target                          | Measurement                                       |
|---------------|--------------------------------------------------------------------------------------------------------|----------------------------------|---------------------------------------------------|
| NFR-DP-01     | The system MUST treat all financial data as sensitive PII.                                              | 100 % of data classes marked.    | Data classification audit.                        |
| NFR-DP-02     | The system MUST follow Privacy by Design and Security by Design.                                        | Documented per feature.          | DPIA when in scope.                               |
| NFR-DP-03     | The system MUST support data export in a portable, documented format.                                  | 100 % of data exportable.        | Export test.                                      |
| NFR-DP-04     | The system MUST support data deletion.                                                                | 100 % of user data deletable.    | Deletion test.                                    |
| NFR-DP-05     | The system MUST publish a public privacy policy.                                                       | Policy present.                  | Public artifact.                                  |

## 16. Scalability

| ID            | Statement                                                                                              | Target                          | Measurement                                       |
|---------------|--------------------------------------------------------------------------------------------------------|----------------------------------|---------------------------------------------------|
| NFR-SC-01     | The system MUST support at least 10 years of personal transaction history on a single device.          | 1 M transactions benchmark.      | Synthetic dataset test.                           |
| NFR-SC-02     | The system MUST support at least 50 accounts and 20 categories.                                        | 50 / 20 benchmark.               | Synthetic dataset test.                           |
| NFR-SC-03     | The system MUST keep memory and CPU usage within the budgets of §5 as the dataset grows.               | Same as NFR-PERF-* with the §16 dataset. | Combined test.                          |

## 17. Resilience Targets (SLOs)

| SLO ID    | Indicator                                  | Target                                      | Window         |
|-----------|--------------------------------------------|---------------------------------------------|----------------|
| SLO-01    | Crash-free session rate                    | ≥ 99.5 %                                    | Rolling 30 d    |
| SLO-02    | Receipt scan success rate                  | ≥ 95 % end-to-end success on the reference device. | Rolling 30 d    |
| SLO-03    | Sync conflict-free convergence             | ≥ 99 % of syncs converge without user intervention. | Rolling 30 d    |
| SLO-04    | Search latency                             | p95 ≤ 250 ms for the §16 dataset.           | Rolling 30 d    |
| SLO-05    | Time to interactive (warm start)           | ≤ 1 s on reference hardware.                | Per release     |
| SLO-06    | Plugin isolation violations                 | 0 in CI                                      | Per build       |

> **Assumption NFR-A-2.** SLOs are measured on opt-in telemetry only
> (per the telemetry policy in `01-Business-Requirements` §5.2). Where
> opt-in telemetry is not available, the SLO is measured on synthetic
> data in CI.

## 18. Deferred NFR Decisions

| ID     | Decision                                                                                  | Owning document(s)         |
|--------|-------------------------------------------------------------------------------------------|----------------------------|
| NFR-D-1 | Final reference devices per platform (for §5, §6, §16).                                  | `09-System-Architecture`   |
| NFR-D-2 | The exact set of supported locales and the locale acceptance criteria.                   | `41-Localization`          |
| NFR-D-3 | The exact set of SLOs published in the status page.                                       | `54-Maintenance`           |
| NFR-D-4 | Whether to ship a public status page post-GA.                                            | `54-Maintenance`           |

## 19. Quality Checklist

- [x] Every NFR is uniquely identified and uses RFC 2119 keywords.
- [x] Every NFR has a measurable target.
- [x] Categories cover all quality attributes in the Vision.
- [x] SLOs are quantified and tied to opt-in or CI measurement.
- [x] No TODO placeholders.
- [x] No contradiction with `00-Vision`..`07-Functional-Requirements`.

## 20. Revision History

| Version | Date       | Author                | Change                                                       |
|---------|------------|-----------------------|--------------------------------------------------------------|
| 0.1     | 2026-07-08 | Engineering Review Board | Initial non-functional requirements.                      |
