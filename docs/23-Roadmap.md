# 23 — Roadmap

| Field            | Value                                                                                |
|------------------|--------------------------------------------------------------------------------------|
| Document ID      | `PINE-DOC-23-ROADMAP`                                                                |
| Status           | Non-normative — roadmap                                                              |
| Reconciled       | 2026-08-31 · A statement of intended order of work. `60-Production-Readiness-Audit` §10 is the roadmap actually being executed. **2026-10-05 · ADR-0021 withdrew the plugin host and `33-Plugin-SDK`; the v0.4, v0.5, v1.0, v1.1 and v1.2 rows in §5 that scheduled a plugin SDK and marketplace are struck through rather than deleted (TD-147).** |
| Owner            | Engineering Review Board                                                             |
| Created          | 2026-07-08                                                                           |
| Last Updated     | 2026-07-08                                                                           |
| Specification Standard | Internal (roadmap)                                                              |
| Glossary         | `PINE-DOC-03-GLOSSARY`                                                               |
| Prerequisites    | `00-Vision`..`22-Release-Plan`                                                       |
| Related Documents| `22-Release-Plan`, `29-Deployment`, `44-Backlog`, `45-Epics`, `46-Features`          |

---

## 1. Purpose

Publish, in a single artefact, the *high-level* roadmap of Pine:
which capabilities are targeted for which release windows, and which
capabilities are explicitly *out of the current roadmap horizon*.
The roadmap is *informative*; the schedule is in `22-Release-Plan`
and the backlog ordering is in `44-Backlog`.

## 2. Scope

In scope: capability-level roadmap per release window, out-of-
horizon capabilities, transparency rules, update cadence.

Out of scope: backlog items → `44-Backlog`; epics → `45-Epics`;
features → `46-Features`; release mechanics → `22-Release-Plan`.

## 3. Definitions

| Term             | Working definition                                                                                |
|------------------|------------------------------------------------------------------------------------------------------|
| Release window   | A time-bounded phase of public availability (alpha, beta, GA) per `22-Release-Plan`.                |
| Theme            | A coherent set of capabilities grouped for communication.                                          |
| Out-of-horizon   | A capability that is *not* planned for the current 12-month horizon.                                |
| Re-prioritisation| A documented change to a roadmap item, recorded as an ADR.                                          |

> **Assumption RM-A-1.** The roadmap is a *communication* tool. It is
> not a contract; the SSoT is the contract.

## 4. Roadmap Posture

Pine ships the *full product* from the start (see `02-Product-Scope`
§6, closing OD-05). The roadmap therefore describes *the order in
which the public gets access to capabilities*, not the *scope* of
the product.

The roadmap is organised by *themes* per release window. A theme is a
coherent set of capabilities that, together, unlock a class of user
value. Themes are not the same as epics (which live in `45-Epics`).

## 5. Themes per Release Window

The roadmap below is *illustrative*; the live roadmap is on the
project website. Windows are expressed as `v0.Y` minor lines per
`22-Release-Plan`.

| Window       | Theme(s)                                                                                  | Capability focus                                                                                  |
|--------------|--------------------------------------------------------------------------------------------|----------------------------------------------------------------------------------------------------|
| v0.1 (alpha) | Core engine.                                                                               | Local SQLite store, encryption, accounts, transactions, categories, basic dashboard, basic settings. |
| v0.2 (beta)  | Imports and the local AI baseline.                                                        | CSV/Excel/OFX/QFX/PDF/bank statement import, OCR model (default), categorisation model (default). |
| v0.3 (beta)  | Budgets, goals, recurring.                                                                | Budgets, savings goals, recurring detection, rule engine baseline.                                 |
| v0.4 (beta)  | Investments.                                                                               | Investment accounts, holdings, cost basis, P/L, ~~plugin~~ compiled-in price sources (ADR-0021).    |
| v0.5 (beta)  | ~~Sync, plugins, marketplace.~~ Sync. **Withdrawn, ADR-0021 (TD-147): no plugin SDK, no marketplace.** | ~~Encrypted sync (plugin), plugin SDK stable, marketplace, public Plugin SDK v1.0.~~ Encrypted sync, with compiled-in providers (`14-Synchronization` §8). |
| v0.6 (beta)  | Reports, analytics, forecasting.                                                          | Built-in reports, cohort/distribution analytics, forecasting (on-device).                          |
| v0.7 (beta)  | AI assistant, recommendation, anomaly.                                                   | Assistant, recommendation engine, anomaly detection, explanation layer.                            |
| v0.8 (beta)  | Internationalisation and accessibility.                                                  | Full i18n baseline, RTL, regional formatting, WCAG 2.2 AA in built-in UI.                         |
| v0.9 (rc)    | Hardening.                                                                                | Performance budgets, mutation suite, security audit, threat model published, accessibility audit.  |
| v1.0 (GA)    | Public release on all five platforms.                                                   | All SCOPE-* items shipped; sync stable; AI stable; ~~plugin SDK stable~~ (withdrawn, ADR-0021).      |
| v1.1         | Quality of life.                                                                          | Undo, keyboard shortcuts, ~~plugin~~ compiled-in provider update UX, mobile polish.                 |
| v1.2         | ~~Ecosystem.~~ **Withdrawn, ADR-0021 (TD-147): no theme/chart/notification plugin marketplace.** | ~~Theme marketplace, chart plugins, notification plugins.~~ |
| v1.3         | Forecasting maturity.                                                                     | Scenario editor, confidence band UX, model swap UX.                                                |
| v1.4         | Security and privacy maturity.                                                            | Independent audit follow-up, key rotation UX, recovery UX.                                         |
| v2.0         | Architectural maturation.                                                                 | Major version with documented breaking changes; previewed in v1.9.                                 |

> **Assumption RM-A-2.** Windows are targets, not commitments. A
> window slipping is a *transparency event*; a capability moving
> between windows is an *ADR*.

## 6. Out-of-Horizon Capabilities (current 12-month horizon)

The following are explicitly *out of the current horizon*. They
remain in product scope (see `02-Product-Scope`); they are simply
not on the *current* roadmap.

- Banking Open Banking *aggregator* flows (`02-Product-Scope`
  anti-goal; remains out).
- Payment initiation (anti-goal; remains out).
- Cryptocurrency exchange / custodian (anti-goal; remains out).
- Regulated investment adviser (anti-goal; remains out).
- A second official Pine application outside the personal-finance
  domain.

## 7. Roadmap Transparency Rules

- The roadmap is published.
- A change to a roadmap item is recorded in the public changelog and
  in an ADR.
- A removed roadmap item is justified in the ADR (e.g. the item is
  no longer a priority, or a better path is available).
- An added roadmap item is justified in the ADR.
- The roadmap is reviewed at every minor release.

## 8. Cross-Cutting Roadmap Constraints

- A roadmap item MUST NOT violate a Vision principle.
- A roadmap item MUST NOT contradict the product scope.
- A roadmap item MUST have a known owner and a known risk.
- A roadmap item MUST NOT silently widen or shrink the product
  scope; an item that does is recorded as a product-scope change.

## 9. Roadmap Maintenance

- The roadmap is a *living* document.
- The roadmap is updated per release.
- The roadmap is updated on every ADR that changes a release plan.
- The roadmap is updated on every new open decision in `00-Vision`
  or its successors.

## 10. Deferred Decisions

| ID     | Decision                                                                                  | Owning document(s)         |
|--------|-------------------------------------------------------------------------------------------|----------------------------|
| RM-D-1 | Whether the roadmap should expose "explore" items (capabilities under research, not committed). | `23-Roadmap` (next rev.)  |
| RM-D-2 | Public visualisation tool for the roadmap (Mermaid vs. off-the-shelf).                     | `26-Diagrams`              |

## 11. Quality Checklist

- [x] Per-window themes, out-of-horizon list, transparency rules,
      cross-cutting constraints, maintenance are all present.
- [x] Roadmap posture is consistent with `02-Product-Scope` §6 (no
      MVP, full product).
- [x] No TODO placeholders.
- [x] No contradiction with `00-Vision`..`22-Release-Plan`.

## 12. Revision History

| Version | Date       | Author                | Change                                                       |
|---------|------------|-----------------------|--------------------------------------------------------------|
| 0.1     | 2026-07-08 | Engineering Review Board | Initial roadmap.                                           |
