# 00 — Vision

| Field            | Value                                                                |
|------------------|----------------------------------------------------------------------|
| Document ID      | `PINE-DOC-00-VISION`                                                 |
| Status           | Draft — pending review                                               |
| Owner            | Engineering Review Board (acting on behalf of Project Lead)          |
| Created          | 2026-07-08                                                           |
| Last Updated     | 2026-07-08                                                           |
| Specification Standard | IEEE 29148 (requirements structure)                            |
| Glossary         | See `03-Glossary` (to be produced; key terms inlined below)          |
| Related Documents| `01-Business-Requirements`, `02-Product-Scope`, `25-ADR`             |

> **Note to reviewers.** This is the first authoritative document of the
> Pine Single Source of Truth (SSoT). Every later document must trace back
> to statements defined here. No code, test, or downstream specification may
> be written on top of an unapproved Vision.

---

## 1. Purpose

Define, in a single concise artefact, the long-term product vision, guiding
philosophy, scope boundary and non-negotiable principles for Pine — an
open-source personal finance platform. This document is the *anchor*: any
later document, ADR, test or feature that contradicts it is, by definition,
a defect.

## 2. Scope of this Document

In scope:

- Mission and long-term product vision.
- Guiding principles (Privacy First, Offline First, etc.).
- Target user value, the problem we are solving.
- Strategic product pillars.
- High-level boundary: what Pine **is** and what it **is not** in its
  current vision.

Out of scope (deferred to dedicated documents):

- Concrete functional and non-functional requirements → `07`, `08`.
- Architectural patterns and technology choices → `09`.
- Domain and data model → `10`, `11`.
- Detailed roadmap, releases, packaging → `22`, `23`, `29`.

## 3. Definitions and Glossary References

The full glossary is owned by `03-Glossary`. Terms used in this document:

| Term      | Working definition                                                                                 |
|-----------|----------------------------------------------------------------------------------------------------|
| Pine      | The open-source personal finance platform described by this SSoT.                                  |
| Offline First | An architectural and UX principle: every core capability must be usable without network access.  |
| Privacy First  | The user's financial data must never leave the device or its encrypted vault without explicit consent. |
| Plugin    | A packaged, isolated extension that adds behaviour to Pine through a documented public contract.  |
| Local AI  | An AI model executed on-device or on the user's own self-hosted runtime, never on a third-party paid API. |
| SSoT      | Single Source of Truth — the `docs/` tree of this project.                                          |
| ADR       | Architecture Decision Record — see `25-ADR`.                                                        |

> **Assumption A-1.** Definitions inlined above are working definitions.
> `03-Glossary` is the authoritative registry and will supersede them.

## 4. Mission Statement

> *Empower every person to take full ownership of their financial life
> with a private, offline-first, AI-assisted platform that they — and only
> they — control.*

**Rationale.** Existing personal-finance tools fall into one of three
unhealthy patterns: (a) ad-supported surveillance products that monetise
user behaviour, (b) closed subscription products that lock user data, or
(c) read-only trackers that require manual entry. Pine exists to deliver
a fourth path: a community-owned, extensible, AI-assisted platform whose
default state is privacy and offline operation.

## 5. Long-term Vision (5–10 year horizon)

Pine aspires to become the **de-facto open standard for personal
finance management** on the desktop and on mobile, comparable in
ecosystem reach to what GnuPG, Nextcloud or Godot are in their domains.

Concretely, the vision is:

1. A **modular core** that can be extended via a public, stable plugin
   SDK.
2. A **local AI layer** for OCR, receipt understanding, categorisation,
   anomaly detection, forecasting and recommendations, replaceable by the
   user.
3. A **trustless sync model** that lets the user choose where encrypted
   data lives, including a Git-backed, WebDAV, or fully self-hosted path.
4. A **community-driven ecosystem** of plugins, themes, importers,
   exporters, financial providers and languages.
5. **Multi-platform parity** on Windows, Linux, macOS, Android and iOS.

**Rationale.** A 5–10 year horizon forces us to design for replacement
(models, providers, storage backends) rather than for a single vendor
relationship. This directly motivates plugin boundaries, swappable AI
models, and an extensible sync layer.

## 6. Guiding Principles (Non-Negotiable)

The principles below are normative: a change to any of them requires an
ADR and a new Vision revision.

| ID    | Principle        | Meaning (one-line)                                                                                  |
|-------|------------------|----------------------------------------------------------------------------------------------------|
| P-01  | Privacy First    | Data leaves the device only when the user explicitly opts in, and only inside an encrypted envelope. |
| P-02  | Offline First    | All core features must work without network. Network is an enhancement, never a dependency.        |
| P-03  | Open Source      | Source code is published under GPL v3 (see Section 12).                                             |
| P-04  | No Ads           | The project will never embed advertising or third-party trackers.                                   |
| P-05  | No Subscription  | The core product is free of charge; no feature is paywalled.                                        |
| P-06  | Cross Platform   | Windows, Linux, macOS, Android, iOS are first-class targets, not afterthoughts.                    |
| P-07  | Plugin Based     | Anything that can reasonably be a plugin must be a plugin.                                          |
| P-08  | Highly Modular   | Bounded contexts, clear contracts, replaceable internals.                                          |
| P-09  | AI Powered       | AI is a first-class citizen for OCR, categorisation, forecasting and recommendations.              |
| P-10  | Community Driven | Governance, contribution and roadmap are public and merit-based.                                  |
| P-11  | Transparent      | Decisions, ADRs, telemetry (if any) and finances are public.                                       |
| P-12  | User-Controlled Data | The user decides storage mode, sync targets, retention, export and deletion.                    |
| P-13  | Cloud is Optional| Pine must be fully usable with no cloud component.                                                  |

**Rationale.** These principles are the contract Pine makes with its
users. Any feature, partner or revenue model that violates a principle
must be rejected, even if commercially attractive.

## 7. Problem Statement and User Value

### 7.1 Problem

People cannot effectively manage their personal finances with current
open tools because:

- Open tools are usually read-only and require manual data entry.
- Closed tools lock the user's data behind subscriptions and accounts.
- Cross-platform parity is rare.
- Local AI on personal devices is uncommon; cloud AI is the norm.
- Plugin ecosystems are immature or non-existent.

### 7.2 User Value Proposition

| User segment          | Value delivered by Pine                                                                                  |
|-----------------------|----------------------------------------------------------------------------------------------------------|
| Privacy-conscious user | Local storage by default, optional encrypted sync, no telemetry by default.                             |
| Power user            | Plugin SDK, custom rules, swappable AI, multiple storage backends.                                       |
| Multi-platform user   | One dataset, identical UX on Windows, Linux, macOS, Android, iOS.                                        |
| International user    | Full i18n, RTL, regional formats, multi-currency, timezones.                                            |
| AI-curious user       | On-device OCR, receipt extraction, categorisation, anomaly detection, forecasting.                      |

## 8. Strategic Product Pillars

The vision is operationalised through the following pillars. Each pillar
maps to dedicated downstream documents.

| Pillar                  | What it means                                                                           | Owning document(s)              |
|-------------------------|------------------------------------------------------------------------------------------|---------------------------------|
| Pillar A — Data Sovereignty | Storage mode selection, encrypted sync, local-first storage.                          | `14-Synchronization`, `15-Security` |
| Pillar B — Modular Core     | Clean Architecture, DDD, plugin host, public contracts.                              | `09-System-Architecture`, `12-Plugin-Architecture`, `25-ADR` |
| Pillar C — Local AI          | Swappable, on-device models for OCR, categorisation, forecasting.                    | `13-AI-Architecture`, `32-AI-Training` |
| Pillar D — Financial Domain  | Rich, well-modelled domain: accounts, transactions, budgets, investments, rules.     | `10-Domain-Model`, `11-Data-Model`, `31-Rule-Engine` |
| Pillar E — Quality           | Testability, observability, accessibility, security, performance.                     | `16-Quality-Attributes`, `18-Test-Strategy`, `30-Observability`, `38-Threat-Model`, `39-Performance`, `40-Accessibility` |
| Pillar F — Ecosystem         | Plugin marketplace, themes, importers, exporters, providers, languages.              | `12-Plugin-Architecture`, `33-Plugin-SDK`, `34-API-Guidelines` |
| Pillar G — Community         | Contribution model, governance, code of conduct, public roadmap.                     | `24-Contributing`, `22-Release-Plan`, `23-Roadmap` |

## 9. Scope Boundary (What Pine Is / Is Not)

### 9.1 Pine **is**

- A local-first personal finance application.
- Cross-platform (desktop + mobile).
- Extensible through a public plugin SDK.
- Capable of running entirely offline.
- Capable of running entirely on-device AI.
- Distributed as open source under GPL v3.

### 9.2 Pine **is not** (in the current vision)

- A banking aggregator that *requires* Open Banking credentials.
- A payment initiation or money-movement product.
- A cryptocurrency exchange or custodian.
- An investment adviser or regulated financial product.
- A cloud-only SaaS.

> **Assumption A-2.** Items in §9.2 may be revisited via ADR and updated
> Vision revisions. None of them are part of the MVP unless explicitly
> reintroduced.

## 10. Storage Strategy (Vision-level)

Per user feedback during the Engineering Review Board gate, the following
rule is **approved** and binding for all downstream documents:

- **Local Only (SQLite)** — the default.
- **Local + Cloud Sync** — user opts in to a sync provider plugin.
- **Cloud Only** — *requires* an encrypted local cache so the app remains
  usable offline. The local cache is part of the product, not an
  implementation detail.

**Rationale.** This rule reconciles "Cloud is optional" and "Offline
First" — both non-negotiable principles. A cloud-only deployment that
silently breaks offline use would violate P-02 and P-13.

## 11. AI Posture (Vision-level)

- AI is a **first-class citizen** and is required for OCR, receipt
  detection, layout analysis, field extraction, validation,
  categorisation, anomaly detection, recommendations, insights and
  forecasting.
- AI runs **locally** whenever technically possible.
- No **paid third-party AI APIs** are allowed.
- The AI subsystem is **modular** and **replaceable**: models, OCR
  engines, categorisers, anomaly detectors and recommender engines are
  plugin contracts.
- Future model replacement must not require application rewrites.

**Rationale.** Lock-in to a single model provider or vendor would
violate P-01 (Privacy First), P-08 (Highly Modular) and P-12
(User-Controlled Data). It would also make the project brittle against
provider outages, deprecations and price changes.

## 12. Licensing and Trademark

- The source code is licensed under **GNU GPL v3**.
- The project name and logo are subject to **trademark protection**; the
  full trademark policy will be defined in `24-Contributing` and a
  dedicated `TRADEMARK.md` (to be produced).
- Plugins distributed through official channels are expected to declare
  their own licence; a plugin licence baseline will be defined in
  `33-Plugin-SDK`.

**Rationale.** GPL v3 protects user freedom and prevents proprietary
re-licensing of the core. Trademark protection is necessary to prevent
malicious forks from misleading users under the Pine name.

## 13. Anti-Goals

To keep focus, Pine explicitly avoids the following in its current
vision:

- Becoming a banking or payments platform.
- Becoming a financial-data broker.
- Becoming a regulated investment adviser.
- Locking the user into a single AI vendor, single storage vendor, or
  single device family.
- Ad-supported or data-monetisation business models.

## 14. Open Decisions and Assumptions

The Engineering Review Board is tracking the following items as
**decisions deferred** to later ADRs. They do **not** block this Vision
but must be resolved before their owning documents are produced.

| ID     | Open decision                                                              | Owning document(s)         |
|--------|----------------------------------------------------------------------------|----------------------------|
| OD-01  | Plugin trust model (signed plugins, allowlist, sandbox, code review rules) | `12-Plugin-Architecture`, `33-Plugin-SDK` |
| OD-02  | Sync conflict resolution strategy (CRDT vs. last-writer-wins vs. domain-aware merge) | `14-Synchronization`       |
| OD-03  | Encryption at rest standard (SQLCipher vs. application-level envelope)     | `15-Security`              |
| OD-04  | Exchange rate provider (plugin-only, local snapshot, hybrid)               | `07-Functional-Requirements`, `14-Synchronization` |
| OD-05  | MVP feature set and target release window                                  | `22-Release-Plan`, `23-Roadmap` |
| OD-06  | Local AI model packaging and on-device update strategy                     | `13-AI-Architecture`, `32-AI-Training` |
| OD-07  | Governance model for community contributions and conflict resolution       | `24-Contributing`          |
| OD-08  | Minimum supported .NET version, Avalonia version and OS baselines          | `09-System-Architecture`   |
| OD-09  | Trademark policy final text and registration jurisdictions                 | `TRADEMARK.md`             |
| OD-10  | Telemetry policy (default-off vs. opt-in vs. opt-out)                      | `30-Observability`, `15-Security` |

> **Assumption A-3.** None of OD-01..OD-10 contradict this Vision. If
> resolving any of them reveals a contradiction, this document must be
> amended before the owning document is approved.

## 15. Success Criteria (Vision-level)

The vision is considered delivered when, **simultaneously**:

- Pine is publicly released on Windows, Linux, macOS, Android and iOS.
- All core financial modules listed in §16 are functional.
- Local AI for OCR and categorisation is functional on a mainstream
  consumer laptop and phone.
- At least one sync provider plugin and one AI provider plugin are
  published by the community (proving the plugin SDK works).
- A published ADR index exists (`25-ADR`) covering all decisions taken
  to date.
- Test strategy, traceability matrix and quality gates are met
  (per `18-Test-Strategy`, `19-Traceability-Matrix`).
- No commercial telemetry is sent without explicit opt-in.

## 16. Financial Modules (Vision-level catalogue)

The following modules are part of the **vision**. Their detailed
behaviour, acceptance criteria and test cases will be defined in
`07-Functional-Requirements` and downstream documents.

Dashboard, Accounts, Transactions, Budgets, Savings Goals, Investments,
Stocks, ETFs, Crypto, Mutual Funds, Net Worth, Cash Flow, Recurring
Expenses, Subscriptions, Financial Calendar, Reports, Analytics,
Forecasting, Import, Export, AI Assistant, Plugin Marketplace, Settings.

**Rationale.** The catalogue is intentionally broad to express the
*full* product. Whether an item ships in MVP, v1.0 or later is the
*release plan*'s concern (`22-Release-Plan`, `23-Roadmap`), not the
Vision's.

## 17. Engineering Posture (Vision-level)

The vision explicitly endorses the following engineering practices.
Detailed rules live in their own documents.

- Clean Architecture, DDD (strategic + tactical), MVVM.
- Dependency Injection, Repository, Specification, Factory, Strategy,
  Mediator (where beneficial), Domain Events, CQRS where appropriate.
- Plugin Host Architecture, Hexagonal boundaries where useful.
- SOLID, GRASP, KISS, YAGNI, DRY, composition over inheritance.
- Architecture Decision Records for every significant decision.
- ISTQB-aligned testing (unit, integration, system, component,
  acceptance, regression, performance, stress, load, accessibility,
  security, mutation, architecture, UI automation, AI validation, sync,
  offline, cloud, plugin).
- Quality gates: 90 % minimum coverage, no warnings, static analysis,
  SonarQube-ready, GitHub Actions, Dependabot, CodeQL, Conventional
  Commits, Semantic Versioning, Definition of Ready / Done.

**Rationale.** These practices are the *evidence* that the principles
in §6 are real, not slogans. Each of them has a dedicated document so
the vision stays short and stable.

## 18. Stakeholders (Vision-level summary)

A full stakeholder analysis is in `04-Stakeholders`. The Vision-level
summary is:

| Stakeholder       | Interest in Pine                                                |
|-------------------|------------------------------------------------------------------|
| End users         | Private, working, multi-platform finance management.             |
| Plugin authors    | Stable, well-documented SDK and marketplace.                     |
| Contributors      | Clear contribution process, public roadmap, recognition.         |
| Local community   | Governance, translation, support, advocacy.                      |
| Security auditors | Threat model, public security posture, responsible disclosure.  |
| Future maintainers| Clean architecture, ADRs, tests, runbooks.                       |

## 19. Risk Posture (Vision-level)

A full risk matrix is in `17-Risk-Analysis` and `52-Risk-Matrix`. The
Vision-level statement is:

> Pine accepts the technical risk of a harder MVP in exchange for the
> strategic gain of a long-lived, modular, privacy-respecting product.
> Pine does **not** accept product risks that would violate the
> principles of §6.

## 20. Document Quality Checklist (Vision-specific)

- [x] Mission statement and long-term vision are present.
- [x] Guiding principles are normative and ADR-protected.
- [x] Scope boundary is explicit (what is / what is not).
- [x] All major downstream areas are referenced with their owning
      documents.
- [x] Every ambiguous or deferred decision is logged as OD-* with its
      owning document.
- [x] Internal traceability: every principle, pillar and module is
      linked to its downstream owner.
- [x] Assumptions are explicit and numbered.
- [x] No undocumented requirements have been silently absorbed.
- [x] IEEE 29148-style structure (Purpose, Scope, Definitions, Body,
      Assumptions, etc.).

## 21. Revision History

| Version | Date       | Author                | Change                                                       |
|---------|------------|-----------------------|--------------------------------------------------------------|
| 0.1     | 2026-07-08 | Engineering Review Board | Initial draft of the Pine Vision document.                |

---

## Appendix A — Why a *Vision* Document, Not Just an ADR

The Vision is **load-bearing**: every ADR, requirement, test, module and
release plan will reference it. If we let the Vision drift, the entire
SSoT drifts. For that reason, the Vision lives at `00-` rather than in
`25-ADR`. ADRs may *change* the Vision only by producing a new
`PINE-DOC-00-VISION` revision and recording the change in this revision
history.
