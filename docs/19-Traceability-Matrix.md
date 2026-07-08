# 19 — Traceability Matrix

| Field            | Value                                                                                |
|------------------|--------------------------------------------------------------------------------------|
| Document ID      | `PINE-DOC-19-TRACEABILITY-MATRIX`                                                    |
| Status           | Draft — pending review                                                               |
| Owner            | Engineering Review Board                                                             |
| Created          | 2026-07-08                                                                           |
| Last Updated     | 2026-07-08                                                                           |
| Specification Standard | IEEE 29148 (requirements traceability)                                          |
| Glossary         | `PINE-DOC-03-GLOSSARY`                                                               |
| Prerequisites    | `00-Vision`..`18-Test-Strategy`                                                      |
| Related Documents| `07-Functional-Requirements`, `08-NFR`, `45-Epics`, `46-Features`, `47-User-Stories`, `48-Acceptance-Criteria`, `49-Test-Cases` |

---

## 1. Purpose

Provide a single, auditable traceability matrix that links every
requirement (FR, NFR) forward to design (architecture, domain,
plugin, AI, sync), to implementation (code), to test (acceptance
criterion, test case) and to release. The matrix is the evidence
that Pine meets the IEEE 29148 traceability requirement.

## 2. Scope

In scope: every FR in `07-Functional-Requirements`, every NFR in
`08-NonFunctional-Requirements`, every ADR in `25-ADR`, and every
test case in `49-Test-Cases`.

Out of scope: backlog ordering → `44-Backlog`; release schedule →
`22-Release-Plan`; quality gates → `16-Quality-Attributes`.

## 3. Definitions

| Term                  | Working definition                                                                                |
|-----------------------|------------------------------------------------------------------------------------------------------|
| Trace link            | A directed link from one artefact to another.                                                       |
| Forward traceability  | Requirement → design → implementation → test.                                                       |
| Backward traceability | Test → implementation → design → requirement.                                                       |
| Coverage              | A measure of how much of a source set is covered by a target set.                                   |
| Orphan                | A requirement with no forward link.                                                                  |
| Pendulum              | A test with no backward link to a requirement.                                                       |

> **Assumption TR-A-1.** Orphans and pendulums are defects. They
> MUST be resolved before a release is tagged.

## 4. Link Types

| Link type            | From                | To                          | Cardinality |
|----------------------|---------------------|------------------------------|--------------|
| `derives-from`       | FR                  | SCOPE, UC, PERS              | N            |
| `addresses-risk`     | FR                  | Risk (R-*)                    | 0..N         |
| `traced-to-arch`     | FR                  | ADR-*                          | 0..N         |
| `traced-to-domain`   | FR                  | Aggregate or value object      | 0..N         |
| `implements`         | FR                  | Code path (file:line)          | 0..N         |
| `tested-by`          | FR                  | TC-*                           | 1..N         |
| `accepted-by`        | FR                  | AC-*                           | 1..N         |
| `traced-to-feature`  | FR                  | Feature (FEAT-*)               | 0..N         |
| `traced-to-story`    | FR                  | US-*                            | 0..N         |
| `traced-to-nfr`      | FR (when NFR-like)  | NFR-*                          | 0..1         |
| `metric-traced`      | NFR                 | Measurement (SLO-*)           | 1            |

## 5. Forward Traceability (Vision → FR)

| Vision principle | FR (top examples)                                                                                |
|------------------|--------------------------------------------------------------------------------------------------|
| P-01 Privacy First | FR-SEC-01, FR-SEC-02, FR-SEC-04, FR-AI-01, FR-SYNC-02.                                       |
| P-02 Offline First | FR-CORE-01, FR-CORE-02, FR-CORE-03, FR-REL-01, FR-AI-04.                                      |
| P-03 Open Source  | FR-CORE-08, FR-PLUGIN-01, FR-PLUGIN-02.                                                         |
| P-04 No Ads       | FR-CORE-07.                                                                                       |
| P-05 No Subscription | FR-CORE-08.                                                                                    |
| P-06 Cross Platform | FR-CORE-06, FR-LOC-05, FR-MOD-03.                                                            |
| P-07 Plugin Based | FR-PLUGIN-01, FR-PLUGIN-02, FR-PLUGIN-03.                                                       |
| P-08 Highly Modular | FR-PLUGIN-01, FR-PLUGIN-03, FR-CORE-03.                                                       |
| P-09 AI Powered   | FR-AI-01, FR-AI-02, FR-AI-06.                                                                    |
| P-10 Community Driven | FR-CORE-08, FR-PLUGIN-04.                                                                  |
| P-11 Transparent  | FR-SEC-08, FR-IMPORT-04, FR-CORE-04.                                                              |
| P-12 User-Controlled Data | FR-SEC-04, FR-SEC-05, FR-SEC-06, FR-IMPORT-04.                                            |
| P-13 Cloud Optional | FR-CORE-03, FR-SYNC-04, FR-REL-01.                                                             |

> **Assumption TR-A-2.** Every principle is covered by at least one
> FR. A principle without an FR is a defect.

## 6. Forward Traceability (FR → Architecture)

| FR group | Architecture artefact (ADR / Section)                                              |
|----------|--------------------------------------------------------------------------------------|
| FR-CORE-* | ADR-01 (Clean Architecture), ADR-02 (.NET + Avalonia + MVVM)                       |
| FR-SEC-*  | ADR-01, `15-Security` (envelope), ADR-04 (plugin host)                              |
| FR-LOC-*  | `41-Localization`, ADR-02                                                            |
| FR-AI-*   | `13-AI-Architecture`, ADR-05 (AI models are swappable adapters)                     |
| FR-PLUGIN-*| `12-Plugin-Architecture`, ADR-04, ADR-05                                            |
| FR-SYNC-* | `14-Synchronization`, ADR-06                                                         |
| FR-IMPORT-* | `11-Data-Model` §6, `13-AI-Architecture` (OCR)                                     |

## 7. Forward Traceability (FR → Domain)

| FR group | Domain artefact                                                                       |
|----------|----------------------------------------------------------------------------------------|
| FR-ACC-*  | `Account` aggregate                                                                   |
| FR-TXN-*  | `Transaction`, `TransactionSplit`                                                      |
| FR-BDG-*  | `Budget` aggregate                                                                    |
| FR-SAV-*  | `SavingsGoal` aggregate                                                                |
| FR-INV-*  | `InvestmentAccount`, `Holding`, `CorporateAction`                                      |
| FR-NW-*   | Net Worth projection (read model)                                                      |
| FR-CF-*   | Cash Flow projection (read model)                                                      |
| FR-RX-*   | `Rule` aggregate (detection rules)                                                     |
| FR-SUB-*  | `Rule` + `Notification`                                                                |
| FR-CAL-*  | `Notification` + `Forecast` (low-balance projection)                                  |
| FR-RPT-*  | `ReportDefinition` aggregate                                                           |
| FR-AN-*   | Cohort / distribution projections (read models)                                        |
| FR-FC-*   | `Forecast` aggregate                                                                   |
| FR-AIA-*  | `AIModel` + `Prediction`                                                               |
| FR-SET-*  | `UserSettings` aggregate                                                               |

## 8. Forward Traceability (FR → Acceptance / Test)

| FR group   | AC count (planned) | TC count (planned) |
|------------|---------------------|---------------------|
| FR-CORE-*  | ≥ 12                | ≥ 12                |
| FR-SEC-*   | ≥ 8                 | ≥ 8                 |
| FR-LOC-*   | ≥ 6                 | ≥ 6                 |
| FR-AI-*    | ≥ 8                 | ≥ 8                 |
| FR-PLUGIN-*| ≥ 6                 | ≥ 6                 |
| FR-SYNC-*  | ≥ 6                 | ≥ 6                 |
| FR-IMPORT-*| ≥ 6                 | ≥ 6                 |
| FR-DASH-*  | ≥ 6                 | ≥ 6                 |
| FR-ACC-*   | ≥ 6                 | ≥ 6                 |
| FR-TXN-*   | ≥ 5                 | ≥ 5                 |
| FR-BDG-*   | ≥ 4                 | ≥ 4                 |
| FR-SAV-*   | ≥ 3                 | ≥ 3                 |
| FR-INV-*   | ≥ 5                 | ≥ 5                 |
| FR-NW-*    | ≥ 3                 | ≥ 3                 |
| FR-CF-*    | ≥ 2                 | ≥ 2                 |
| FR-RX-*    | ≥ 3                 | ≥ 3                 |
| FR-SUB-*   | ≥ 3                 | ≥ 3                 |
| FR-CAL-*   | ≥ 3                 | ≥ 3                 |
| FR-RPT-*   | ≥ 4                 | ≥ 4                 |
| FR-AN-*    | ≥ 2                 | ≥ 2                 |
| FR-FC-*    | ≥ 3                 | ≥ 3                 |
| FR-AIA-*   | ≥ 4                 | ≥ 4                 |
| FR-SET-*   | ≥ 3                 | ≥ 3                 |
| **Total**  | **≥ 109**           | **≥ 109**           |

> **Assumption TR-A-3.** The counts above are *minimums*. The
> authoritative counts live in `48-Acceptance-Criteria` and
> `49-Test-Cases`. A release is blocked if any FR has zero AC or
> zero TC.

## 9. Backward Traceability (Test → Requirement)

- Each TC references at least one FR and at least one AC.
- A TC with no FR is a *pendulum* and is a release-blocker.
- The full backward matrix is generated by CI from the test
  metadata.

## 10. ADR Traceability

| ADR ID | Title                                  | Linked FR                       |
|--------|-----------------------------------------|----------------------------------|
| ADR-01 | Use Clean Architecture + DDD            | FR-CORE-*, FR-SEC-*              |
| ADR-02 | Use Avalonia UI + .NET + MVVM           | FR-CORE-06                       |
| ADR-03 | Use SQLite as the local store           | FR-SEC-01, FR-IMPORT-*           |
| ADR-04 | Plugin host inside the process          | FR-PLUGIN-01..06                 |
| ADR-05 | AI models are swappable adapters        | FR-AI-01, FR-AI-05               |
| ADR-06 | Sync providers are swappable adapters   | FR-SYNC-01                       |
| ADR-07 | Money is a value object (decimal)       | XCD-04 in `09`                   |
| ADR-08 | Time and clock are abstracted           | XCD-03 in `09`                   |
| ADR-09 | Use mediation selectively               | SA-07 in `09`                    |
| ADR-10 | Use CQRS for read-heavy contexts        | FR-DASH-*, FR-RPT-*              |

## 11. Risk Traceability

| Risk ID | Linked FR (top examples)                                                |
|---------|--------------------------------------------------------------------------|
| R-01    | (cross-cutting; mitigated by maintainer rotation in `54-Maintenance`)   |
| R-02    | FR-AI-01, FR-AI-02                                                       |
| R-03    | FR-SYNC-01, FR-SYNC-03                                                   |
| R-04    | FR-AI-05                                                                 |
| R-05    | (cross-cutting; mitigated by `TRADEMARK.md` and marketplace allow-list)  |
| R-06    | (cross-cutting; mitigated by `29-Deployment` and platform compliance)    |
| R-07    | FR-SEC-*, NFR-SEC-*                                                      |
| R-08    | (cross-cutting; mitigated by `24-Contributing`)                           |
| R-09    | (cross-cutting; mitigated by public finance report)                       |
| R-10    | FR-PLUGIN-*                                                              |
| R-11    | FR-CORE-06, NFR-MOD-03                                                   |
| R-12    | NFR-MOD-04, NFR-MOD-05                                                   |
| R-13    | (cross-cutting; mitigated by Dependabot + SBOM + reproducible builds)   |
| R-14    | (cross-cutting; mitigated by `24-Contributing` and `54-Maintenance`)     |
| R-15    | (cross-cutting; mitigated by `TRADEMARK.md`)                              |
| R-16    | NFR-REL-02, NFR-REL-03                                                   |
| R-17    | FR-AI-05                                                                 |
| R-18    | FR-SYNC-05, FR-SYNC-06                                                   |
| R-19    | (cross-cutting; mitigated by counsel network)                            |
| R-20    | (cross-cutting; mitigated by the absence of a central data store)        |

## 12. Quality Gate Traceability

| Quality gate | Source                                            |
|--------------|----------------------------------------------------|
| QG-01..15    | `16-Quality-Attributes` §4                          |
| QG-08        | This document (FR → AC → TC coverage)              |
| QG-13        | `38-Threat-Model`                                  |
| QG-14        | `25-ADR`                                           |

## 13. CI Enforcement

- The matrix is generated by a CI job from the artefact metadata
  (FRs, ACs, TCs, ADRs, Risks, FRs).
- The job fails the build if it detects:
  - An FR with no AC and no TC.
  - A TC with no FR.
  - An ADR with no FR.
  - A risk ≥ 12 with no linked FR.
- The job publishes an HTML report in the CI artefacts.

## 14. Deferred Decisions

| ID     | Decision                                                                                  | Owning document(s)         |
|--------|-------------------------------------------------------------------------------------------|----------------------------|
| TR-D-1 | Tooling for the matrix generator (custom vs. off-the-shelf).                              | `28-CI-CD`                 |

## 15. Quality Checklist

- [x] Forward traceability: Vision → FR → Arch → Domain → AC → TC.
- [x] Backward traceability: TC → FR.
- [x] ADR traceability and risk traceability.
- [x] CI enforcement strategy.
- [x] No TODO placeholders.
- [x] No contradiction with `00-Vision`..`18-Test-Strategy`.

## 16. Revision History

| Version | Date       | Author                | Change                                                       |
|---------|------------|-----------------------|--------------------------------------------------------------|
| 0.1     | 2026-07-08 | Engineering Review Board | Initial traceability matrix.                              |
