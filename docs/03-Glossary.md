# 03 — Glossary

| Field            | Value                                                                                |
|------------------|--------------------------------------------------------------------------------------|
| Document ID      | `PINE-DOC-03-GLOSSARY`                                                               |
| Status           | Non-normative — terminology registry                                                 |
| Reconciled       | 2026-08-31 · Defines vocabulary for the other documents. Terms drift with the code, which is a review concern, not an implementation status. |
| Owner            | Engineering Review Board                                                             |
| Created          | 2026-07-08                                                                           |
| Last Updated     | 2026-07-08                                                                           |
| Specification Standard | IEEE 29148 (definitions); internal SSoT registry                              |
| Glossary         | **This document is the authoritative registry**                                      |
| Prerequisites    | `PINE-DOC-00-VISION`, `PINE-DOC-01-BUSINESS-REQUIREMENTS`, `PINE-DOC-02-PRODUCT-SCOPE` |
| Decisions closed | None                                                                                 |
| Related Documents| All Pine SSoT documents reference this glossary                                      |

---

## 1. Purpose

Provide a single, authoritative, cross-referenced registry of every
term used across the Pine Single Source of Truth. The glossary is the
source of truth for terminology; later documents cite it instead of
redefining concepts.

## 2. Scope of this Document

In scope: project-wide terms (product, principles, scope), finance
domain terms (accounts, transactions, budgets, investments), AI domain
terms (models, pipelines, training, validation), plugin, sync and
storage terms, quality, testing, observability and security terms,
methodology and process terms (ADR, SSoT, OKR, DoR, DoD, Mermaid),
anti-terms and aliases (terms we explicitly do not use, or that map to
a different term in Pine).

Out of scope: marketing copy, slogans, brand voice → `24-Contributing`,
`42-UX-Guidelines`; tax or regulatory definitions (Pine is not a
tax/accounting engine); provider-specific terms (CSV, OFX, QFX) — those
are owned by importer/exporter documents, with pointers from this
glossary.

## 3. How to Use this Glossary

- **Authoring rule.** When writing a new SSoT document, *do not* invent
  a new term. Use one of the terms below. If a new term is genuinely
  needed, add it to this glossary first, then reference it.
- **Reading rule.** When reading a SSoT document, an unfamiliar term
  has exactly one meaning: the one defined here.
- **Change rule.** Changes to the glossary are governed by §11.

## 4. Glossary Conventions

| Convention            | Meaning                                                                                |
|-----------------------|----------------------------------------------------------------------------------------|
| `TERM-*`              | Stable identifier used for traceability (e.g. `TERM-PRIVACY-FIRST`).                  |
| Aliases               | Alternative spellings or project-internal synonyms; the *first* listed term is canonical. |
| Anti-term             | A term that is *not* used in Pine, with a pointer to the term that *is* used.          |
| Source                | The upstream document or external standard that defines the concept.                    |
| Owner                 | The SSoT document that owns the *behavioural* meaning of the term.                     |
| Normative             | "Yes" means a deviation from the term's definition requires an ADR.                    |

> **Assumption GL-A-1.** Where a term is also defined by an external
> standard (e.g. ISTQB, IEEE 29148, GDPR, Basel/IFRS, RFC 2119), Pine
> adopts the external definition verbatim unless explicitly noted.

## 5. Project and Methodology Terms

| ID                  | Term (canonical)               | Definition                                                                                | Aliases                          | Anti-term                | Source / Normative | Owner           |
|---------------------|---------------------------------|--------------------------------------------------------------------------------------------|----------------------------------|--------------------------|--------------------|-----------------|
| `TERM-PINE`         | Pine                            | The open-source personal finance platform described by this SSoT.                          | —                                | —                        | `00-Vision`        | `00-Vision`     |
| `TERM-SSOT`         | SSoT (Single Source of Truth)   | The `docs/` tree of this project, treated as the only authoritative specification source.   | Single Source of Truth           | —                        | Internal           | `00-Vision`     |
| `TERM-ADR`          | ADR (Architecture Decision Record) | A document recording a significant architectural decision, its context, options and consequences. | Architecture Decision       | —                        | `25-ADR`           | `25-ADR`        |
| `TERM-OKR`          | OKR (Objectives and Key Results)| Framework used to track business progress. Objectives are qualitative, Key Results are measurable. | —                              | KPI (in Pine, KRs are preferred) | Internal           | `01-Business-Requirements` |
| `TERM-DOR`          | DoR (Definition of Ready)       | The criteria a backlog item must satisfy before a sprint can begin work on it.               | Definition of Ready              | —                        | `21-Definition-of-Done` (related) | `18-Test-Strategy` |
| `TERM-DOD`          | DoD (Definition of Done)        | The criteria a backlog item must satisfy before it is considered finished.                  | Definition of Done               | —                        | `21-Definition-of-Done` | `21-Definition-of-Done` |
| `TERM-MERMAID`      | Mermaid                         | The diagramming language used in Pine documentation.                                        | —                                | Images, screenshots of diagrams | Internal           | `00-Vision`     |
| `TERM-IEEE-29148`   | IEEE 29148                      | The international standard that structures the requirements documents in Pine.             | —                                | —                        | External standard  | `00-Vision`     |
| `TERM-ISTQB`        | ISTQB                           | The testing terminology standard used across Pine.                                         | —                                | —                        | External standard  | `18-Test-Strategy` |
| `TERM-RFC-2119`     | RFC 2119                        | The key-words-for-requirements standard (MUST, SHOULD, MAY, etc.) used in normative text.   | —                                | —                        | External standard  | `00-Vision`     |

**Rationale.** Methodology terms must be standard-aligned where a
standard exists. Using a private vocabulary in those areas would make
external contributors and auditors misread the documents.

## 6. Product and Scope Terms

| ID                  | Term (canonical)               | Definition                                                                                | Aliases                          | Anti-term                | Source / Normative | Owner           |
|---------------------|---------------------------------|--------------------------------------------------------------------------------------------|----------------------------------|--------------------------|--------------------|-----------------|
| `TERM-PRIVACY-FIRST`| Privacy First                  | P-01: data leaves the device only when the user explicitly opts in, and only inside an encrypted envelope. | — | — | `00-Vision` §6     | `00-Vision`     |
| `TERM-OFFLINE-FIRST`| Offline First                  | P-02: all core features must work without network. Network is an enhancement, never a dependency. | — | — | `00-Vision` §6     | `00-Vision`     |
| `TERM-OPEN-SOURCE`  | Open Source                     | P-03: source code is published under GPL v3.                                                | —                                | Source-available         | `00-Vision` §6, §12 | `00-Vision`   |
| `TERM-NO-ADS`       | No Ads                          | P-04: the project will never embed advertising or third-party trackers.                    | —                                | —                        | `00-Vision` §6     | `00-Vision`     |
| `TERM-NO-SUBSCRIPTION` | No Subscription              | P-05: the core product is free of charge; no feature is paywalled.                          | —                                | Freemium                 | `00-Vision` §6     | `00-Vision`     |
| `TERM-CROSS-PLATFORM` | Cross Platform                | P-06: Windows, Linux, macOS, Android, iOS are first-class targets.                         | —                                | Web-only, Lite           | `00-Vision` §6     | `00-Vision`     |
| `TERM-PLUGIN-BASED` | Plugin Based                    | P-07: anything that can reasonably be a plugin must be a plugin.                            | —                                | Hard-coded integration   | `00-Vision` §6     | `12-Plugin-Architecture` |
| `TERM-HIGHLY-MODULAR` | Highly Modular                | P-08: bounded contexts, clear contracts, replaceable internals.                            | Modular                          | Monolith                 | `00-Vision` §6     | `09-System-Architecture` |
| `TERM-AI-POWERED`   | AI Powered                      | P-09: AI is a first-class citizen for OCR, categorisation, forecasting and recommendations. | —                                | —                        | `00-Vision` §6     | `13-AI-Architecture` |
| `TERM-COMMUNITY-DRIVEN` | Community Driven             | P-10: governance, contribution and roadmap are public and merit-based.                     | —                                | Vendor-driven            | `00-Vision` §6     | `24-Contributing` |
| `TERM-TRANSPARENT`  | Transparent                     | P-11: decisions, ADRs, telemetry (if any) and finances are public.                         | —                                | —                        | `00-Vision` §6     | `24-Contributing` |
| `TERM-USER-CONTROLLED-DATA` | User-Controlled Data       | P-12: the user decides storage mode, sync targets, retention, export and deletion.          | Data sovereignty (informal)      | Vendor lock-in           | `00-Vision` §6     | `15-Security`   |
| `TERM-CLOUD-OPTIONAL` | Cloud is Optional             | P-13: Pine must be fully usable with no cloud component.                                    | —                                | Cloud-first              | `00-Vision` §6     | `14-Synchronization` |
| `TERM-MVP`          | MVP (anti-term)                 | *Not used in Pine.* Pine ships the full product; releases are sequenced. The acronym is reserved for other projects. | Minimum Viable Product | MVP | `02-Product-Scope` §6 | `02-Product-Scope` |
| `TERM-CAPABILITY`   | Capability                      | A coherent, user-visible area of the product. May map to one or more modules.                | —                                | —                        | `02-Product-Scope` §3 | `02-Product-Scope` |
| `TERM-MODULE`       | Module                          | The engineering unit (UI, domain, data) that implements a capability.                       | —                                | —                        | `02-Product-Scope` §3 | `02-Product-Scope` |
| `TERM-SCOPE-ITEM`   | Scope Item                      | A single row in the capability catalogue (`SCOPE-*` identifier).                            | —                                | —                        | `02-Product-Scope` §3 | `02-Product-Scope` |
| `TERM-ANTI-GOAL`    | Anti-goal                       | A capability or business behaviour the product explicitly will not provide.                | —                                | —                        | `00-Vision` §9.2, `02-Product-Scope` §5.2 | `02-Product-Scope` |

## 7. Privacy, Security and Storage Terms

| ID                 | Term (canonical)               | Definition                                                                                | Aliases                          | Anti-term                | Source / Normative | Owner           |
|--------------------|---------------------------------|--------------------------------------------------------------------------------------------|----------------------------------|--------------------------|--------------------|-----------------|
| `TERM-LOCAL-ONLY`   | Local Only                      | Storage mode where data lives only in the local SQLite database. Default mode.              | Local-First (overload avoided)   | —                        | `00-Vision` §10    | `14-Synchronization` |
| `TERM-LOCAL-PLUS-CLOUD` | Local + Cloud Sync          | Storage mode where the local SQLite database is mirrored to a user-chosen sync provider plugin. | —                             | —                        | `00-Vision` §10    | `14-Synchronization` |
| `TERM-CLOUD-ONLY`   | Cloud Only                      | Storage mode where the primary data lives in a cloud target, *and* an encrypted local cache exists so the app remains usable offline. | — | — | `00-Vision` §10    | `14-Synchronization` |
| `TERM-ENCRYPTED-LOCAL-CACHE` | Encrypted Local Cache  | A copy of the data, stored locally in encrypted form, sufficient to operate the app offline. | — | Plaintext cache          | `00-Vision` §10    | `15-Security`    |
| `TERM-ENCRYPTION-AT-REST` | Encryption at Rest       | Encryption applied to stored data, regardless of medium. Pine MUST encrypt user data at rest. | — | — | `15-Security`    | `15-Security`    |
| `TERM-ENCRYPTION-IN-TRANSIT` | Encryption in Transit | Encryption applied to data while in motion (e.g. TLS 1.3, WSS). Used for sync and any network call. | — | — | `15-Security` | `15-Security` |
| `TERM-ENCRYPTED-CLOUD-SYNC` | Encrypted Cloud Sync   | Sync model in which the payload is encrypted client-side before leaving the device; the cloud provider sees only ciphertext. | E2EE sync | Server-side-encrypted sync | `00-Vision` §10, §15 | `14-Synchronization` |
| `TERM-THREAT-MODEL` | Threat Model                    | A structured description of assets, adversaries, attack surfaces and mitigations. Updated when the product's surface changes. | — | — | `38-Threat-Model` | `38-Threat-Model` |
| `TERM-PRIVACY-BY-DESIGN` | Privacy by Design         | The principle of designing the system to make privacy the default path, not an opt-in afterthought. | PbD | — | GDPR Art. 25 (informative) | `15-Security` |
| `TERM-SECURITY-BY-DESIGN` | Security by Design       | The principle of designing the system to make security the default path, not a bolt-on.     | SbD | — | OWASP SAMM (informative) | `15-Security` |
| `TERM-ROLE-READY-ARCH` | Role-ready Architecture       | A design that anticipates the introduction of multiple user roles (e.g. auditor, child account) without committing to a particular role model. | — | Hard-coded single-user | `09-System-Architecture` | `09-System-Architecture` |
| `TERM-TELEMETRY`    | Telemetry                       | Any data Pine sends off the device about its own operation. Default: off, opt-in.            | — | Analytics (in Pine, "analytics" means on-device) | `01-Business-Requirements` §5.2 | `30-Observability` |
| `TERM-PII`          | PII (Personally Identifiable Information) | Information that can identify a natural person, directly or indirectly. Pine treats financial data as sensitive PII by default. | Personal data | — | GDPR Art. 4 (informative) | `15-Security` |
| `TERM-DONATION-MODEL` | Donation Model                | The accepted business model: voluntary community donations. No ads, no subscriptions, no fees. | — | SaaS, freemium | `01-Business-Requirements` §4 | `01-Business-Requirements` |

**Rationale.** Storage and security terms are deliberately aligned
with `00-Vision` §10 and §11, and with the user-approved rule that
Cloud Only must preserve offline use. Anti-terms exist where the
common industry term would imply behaviour Pine rejects (e.g.
"freemium" implies a paid tier).

## 8. Plugin, Sync and Ecosystem Terms

| ID            | Term (canonical)               | Definition                                                                                | Aliases                          | Anti-term                | Source / Normative | Owner           |
|---------------|---------------------------------|--------------------------------------------------------------------------------------------|----------------------------------|--------------------------|--------------------|-----------------|
| `TERM-PLUGIN` | Plugin                          | A packaged, isolated extension that adds behaviour to Pine through a documented public contract. | Extension, add-on            | Hard-coded integration   | `00-Vision` §6, `12-Plugin-Architecture` | `12-Plugin-Architecture` |
| `TERM-PLUGIN-SDK` | Plugin SDK                      | The public, versioned contract used to build plugins for Pine (lifecycle, isolation, capabilities). | — | — | `33-Plugin-SDK` | `33-Plugin-SDK` |
| `TERM-PLUGIN-TRUST-MODEL` | Plugin Trust Model       | The set of rules Pine uses to decide which plugins may run, in which mode, and with which permissions. | — | — | OD-01 (`00-Vision` §14) | `12-Plugin-Architecture` |
| `TERM-SYNC-PROVIDER` | Sync Provider                   | A plugin that moves encrypted data between the local device and an external user-chosen target. | — | — | `00-Vision` §10    | `14-Synchronization` |
| `TERM-CRDTS`    | CRDT                            | Conflict-free Replicated Data Type. A family of data structures that can be merged across replicas without coordination. | — | Last-writer-wins (in general) | External (informative) | `14-Synchronization` |
| `TERM-CONFLICT-RESOLUTION` | Conflict Resolution      | The strategy used to merge concurrent edits across replicas. Strategy is owned by `14-Synchronization` (OD-02). | Merge strategy | — | `14-Synchronization` | `14-Synchronization` |
| `TERM-MARKETPLACE` | Plugin Marketplace              | The in-app surface for discovering, installing, updating, removing and reviewing plugins.        | Plugin registry (infrastructure) | App store (implies a fee model) | `00-Vision` §16, `02-Product-Scope` §8 | `12-Plugin-Architecture` |
| `TERM-THEME`    | Theme                           | A plugin (or built-in) that controls visual presentation of built-in UI.                    | Skin                     | —                        | `02-Product-Scope` §7.3 | `42-UX-Guidelines` |
| `TERM-NOTIFICATION-PROVIDER` | Notification Provider   | A plugin that delivers user-facing notifications to a channel (e.g. system, email, push).   | — | — | `02-Product-Scope` §7.3 | `12-Plugin-Architecture` |
| `TERM-LOCALE`   | Locale                          | A language-and-region identifier (e.g. `en-GB`, `es-EC`) used for i18n, formatting and translations. | — | Language (in Pine, "language" is just the language code; "locale" is the full identifier) | RFC 5646 (informative) | `41-Localization` |
| `TERM-RTL`      | RTL                             | Right-to-left script direction. Pine MUST render RTL locales correctly in core and built-in themes. | — | — | Unicode UAX #9 (informative) | `41-Localization` |

## 9. AI and Receipt Scanning Terms

| ID                 | Term (canonical)               | Definition                                                                                | Aliases                          | Anti-term                | Source / Normative | Owner           |
|--------------------|---------------------------------|--------------------------------------------------------------------------------------------|----------------------------------|--------------------------|--------------------|-----------------|
| `TERM-LOCAL-AI`     | Local AI                        | An AI model executed on-device or on the user's own self-hosted runtime, never on a third-party paid API. | On-device AI | Cloud AI (in Pine, "cloud AI" is rejected) | `00-Vision` §3    | `13-AI-Architecture` |
| `TERM-PAID-AI-API` (anti-term) | Paid AI API             | *Not used in Pine.* A paid, third-party hosted AI service. Excluded by BP-04.               | Hosted AI, AI-as-a-service       | —                        | `01-Business-Requirements` §6 | `13-AI-Architecture` |
| `TERM-MODEL`        | Model                           | An AI model used by Pine (OCR, categorisation, anomaly, recommendation, forecasting). A model is a *plugin-attachable* component. | — | — | `13-AI-Architecture` | `13-AI-Architecture` |
| `TERM-OCR`          | OCR                             | Optical Character Recognition. In Pine, an on-device model that converts images to text.    | — | — | `00-Vision` §11    | `13-AI-Architecture` |
| `TERM-RECEIPT-DETECTION` | Receipt Detection        | The model stage that decides whether an image contains a receipt.                            | — | — | `00-Vision` §11    | `13-AI-Architecture` |
| `TERM-LAYOUT-ANALYSIS` | Layout Analysis           | The model stage that understands the structure of a receipt (zones, fields, tables).         | — | — | `00-Vision` §11    | `13-AI-Architecture` |
| `TERM-FIELD-EXTRACTION` | Field Extraction        | The model stage that extracts individual fields (merchant, total, etc.) from a receipt.    | — | — | `00-Vision` §11    | `13-AI-Architecture` |
| `TERM-CONFIDENCE-SCORE` | Confidence Score         | A per-field and per-document value (0..1) expressing the model's certainty. Always surfaced to the user. | — | — | `00-Vision` §12    | `13-AI-Architecture` |
| `TERM-RECEIPT-FLOW` | Receipt Scanning Flow           | The on-device pipeline: Take Picture → Receipt Detection → OCR → Layout Analysis → Field Extraction → Validation → Expense Categorisation → User Confirmation → Save. | — | — | `00-Vision` §11, `02-Product-Scope` §8.2 | `13-AI-Architecture` |
| `TERM-CATEGORISATION` | Categorisation (or Categorization) | The model stage that assigns a category to a transaction.                               | Classification                   | —                        | `00-Vision` §11    | `13-AI-Architecture` |
| `TERM-ANOMALY-DETECTION` | Anomaly Detection       | The model stage that flags unusual transactions or trends.                                  | — | — | `00-Vision` §11    | `13-AI-Architecture` |
| `TERM-RECOMMENDATION-ENGINE` | Recommendation Engine | The model that produces explainable suggestions (savings, budget, anomaly follow-up).        | — | — | `00-Vision` §11    | `13-AI-Architecture` |
| `TERM-FORECASTING-MODEL` | Forecasting Model        | The model that produces cash flow, balance and category spend forecasts.                    | — | — | `00-Vision` §11    | `13-AI-Architecture` |
| `TERM-MODEL-REPLACEMENT` | Model Replacement        | The act of swapping a model (e.g. for a better one) without rewriting the application.      | — | Hard-coded model         | `00-Vision` §11    | `13-AI-Architecture` |
| `TERM-AI-VALIDATION` | AI Validation                   | Testing a model against labelled datasets, including regression of behaviour across versions.| Model testing                    | Unit testing (in Pine, unit tests cover code, AI validation covers models) | `18-Test-Strategy` | `18-Test-Strategy` |
| `TERM-MUTATION-TESTING` | Mutation Testing        | A testing technique that introduces small code mutations to assess the strength of the test suite. | — | — | ISTQB (informative) | `18-Test-Strategy` |

**Rationale.** AI terms are deliberately split by *stage* of the
receipt flow. A reader of the AI architecture document can navigate
from a stage name to its definition here, and back.

## 10. Financial Domain Terms

| ID                  | Term (canonical)               | Definition                                                                                | Aliases                          | Anti-term                | Source / Normative | Owner           |
|---------------------|---------------------------------|--------------------------------------------------------------------------------------------|----------------------------------|--------------------------|--------------------|-----------------|
| `TERM-ACCOUNT`      | Account                         | A container of money or assets tracked by Pine. Subtypes: cash, bank, card, investment, crypto, custom. | — | Wallet (in Pine, "wallet" is reserved for crypto contexts and is itself an account subtype) | `02-Product-Scope` §8 SCOPE-02 | `10-Domain-Model` |
| `TERM-TRANSACTION`  | Transaction                     | A movement of money or value recorded in an account. May be income, expense or transfer.    | — | Entry (in Pine, "entry" is reserved for journal-style accounting contexts, not used) | `02-Product-Scope` §8 SCOPE-03 | `10-Domain-Model` |
| `TERM-SPLIT`        | Split                           | A single transaction divided across multiple categories and/or accounts.                    | Split transaction                | —                        | `02-Product-Scope` §8 SCOPE-03 | `10-Domain-Model` |
| `TERM-CATEGORY`     | Category                        | A label assigned to a transaction for reporting and budgeting. Hierarchical.                  | — | Tag (in Pine, "tag" is a separate free-form label) | `02-Product-Scope` §8 SCOPE-04 | `10-Domain-Model` |
| `TERM-BUDGET`       | Budget                          | A per-category, per-period plan with alerts via the rule engine.                            | — | — | `02-Product-Scope` §8 SCOPE-04 | `10-Domain-Model` |
| `TERM-SAVINGS-GOAL` | Savings Goal                    | A target-based, time-bound plan for accumulating an amount in an account.                   | Goal                             | —                        | `02-Product-Scope` §8 SCOPE-05 | `10-Domain-Model` |
| `TERM-INVESTMENT`   | Investment                      | An asset (stock, ETF, crypto, mutual fund, custom) held in an investment account.           | Holding                          | Position (reserved for futures/derivatives; not in scope) | `02-Product-Scope` §8 SCOPE-06 | `10-Domain-Model` |
| `TERM-COST-BASIS`   | Cost Basis                      | The acquisition cost of an investment, used to compute realised and unrealised P/L.          | — | — | IFRS / US tax (informative) | `10-Domain-Model` |
| `TERM-NET-WORTH`    | Net Worth                       | Aggregate value of all assets minus all liabilities, normalised to a user-chosen reporting currency. | — | — | `02-Product-Scope` §8 SCOPE-11 | `10-Domain-Model` |
| `TERM-CASH-FLOW`    | Cash Flow                       | Income minus expense over a period, broken down by category, account, and optional project. | — | — | `02-Product-Scope` §8 SCOPE-12 | `10-Domain-Model` |
| `TERM-RECURRING-EXPENSE` | Recurring Expense       | A transaction that the rule engine identifies as repeating with a stable amount, category and account. | — | — | `02-Product-Scope` §8 SCOPE-13 | `10-Domain-Model` |
| `TERM-SUBSCRIPTION` | Subscription                    | A recurring service tracked by Pine (e.g. streaming, SaaS).                                 | — | — | `02-Product-Scope` §8 SCOPE-14 | `10-Domain-Model` |
| `TERM-FINANCIAL-CALENDAR` | Financial Calendar     | A time-based view of upcoming bills, paydays, goal milestones, and projected low-balance days. | — | — | `02-Product-Scope` §8 SCOPE-15 | `10-Domain-Model` |
| `TERM-RULE-ENGINE`  | Rule Engine                     | The extensible engine that evaluates user-defined and plugin-defined rules against the user's data. | — | — | `00-Vision` §15    | `31-Rule-Engine` |
| `TERM-MULTI-CURRENCY` | Multi-Currency              | The ability to hold accounts, transactions and investments in multiple fiat and crypto currencies. | — | — | `00-Vision` §15    | `11-Data-Model` |
| `TERM-EXCHANGE-RATE` | Exchange Rate                 | A rate used to convert amounts between currencies. May be historical or current.             | FX rate                          | —                        | `00-Vision` §15    | `11-Data-Model` |
| `TERM-IMPORT`       | Import                          | Bringing data into Pine from external sources (CSV, Excel, PDF, OFX, QFX, bank statements).   | — | — | `00-Vision` §14    | `07-Functional-Requirements` |
| `TERM-EXPORT`       | Export                          | Producing data from Pine in a user-chosen format (CSV, Excel, PDF, JSON).                    | — | — | `00-Vision` §14    | `07-Functional-Requirements` |
| `TERM-OPEN-BANKING` (anti-term) | Open Banking         | *Not used as a product capability.* Bank statement *file* import is in scope; programmatic Open Banking aggregator flows are not. | — | — | `02-Product-Scope` §7.2 | `02-Product-Scope` |

**Rationale.** The financial domain terms are intentionally close to
the `02-Product-Scope` catalogue so that a scope ID (`SCOPE-*`) and a
term ID (`TERM-*`) are the only things an author needs to reference
when describing a capability.

## 11. Quality, Testing, Observability and Accessibility Terms

| ID             | Term (canonical)               | Definition                                                                                | Aliases                          | Anti-term                | Source / Normative | Owner           |
|----------------|---------------------------------|--------------------------------------------------------------------------------------------|----------------------------------|--------------------------|--------------------|-----------------|
| `TERM-CODE-COVERAGE` | Code Coverage                   | The percentage of lines and branches executed by the test suite.                             | — | — | ISTQB (informative) | `18-Test-Strategy` |
| `TERM-MUTATION-SCORE` | Mutation Score                 | The percentage of introduced mutations that are detected (killed) by the test suite.          | — | — | ISTQB (informative) | `18-Test-Strategy` |
| `TERM-ARCHITECTURE-TEST` | Architecture Test           | An automated test that asserts the *structure* of the codebase (e.g. dependency direction).   | — | — | `18-Test-Strategy` | `09-System-Architecture` |
| `TERM-TRACE`    | Trace                           | A distributed-trace span produced by a single unit of work. Used to debug performance and reliability. | — | — | OpenTelemetry (informative) | `30-Observability` |
| `TERM-METRIC`   | Metric                          | A numeric time-series measurement of system behaviour (e.g. request latency).               | — | — | OpenTelemetry (informative) | `30-Observability` |
| `TERM-PERFORMANCE-COUNTER` | Performance Counter     | A platform-provided counter exposed by Pine (CPU, memory, IO, GPU).                          | — | — | OS-specific        | `30-Observability` |
| `TERM-LOG`      | Log                             | A structured record of an event. In Pine, logs are local by default; never sent off-device without explicit opt-in. | — | — | `30-Observability` | `30-Observability` |
| `TERM-A11Y`     | Accessibility (a11y)           | The property of being usable by people with the widest possible range of abilities.          | — | — | WCAG 2.2 (informative) | `40-Accessibility` |
| `TERM-WCAG`     | WCAG                            | Web Content Accessibility Guidelines. Pine treats WCAG 2.2 AA as the *minimum* conformance target for built-in UI; plugins declare their own. | — | — | W3C WAI (informative) | `40-Accessibility` |
| `TERM-I18N`     | i18n (Internationalisation)    | The design and engineering practice of supporting multiple locales.                          | — | l10n alone (in Pine, i18n is the umbrella; l10n is the per-locale act) | RFC 6365 (informative) | `41-Localization` |
| `TERM-L10N`     | l10n (Localisation)            | The act of producing a locale-specific variant (texts, formats).                             | — | — | RFC 6365 (informative) | `41-Localization` |
| `TERM-RFC-2119-KEYWORDS` | RFC 2119 Keywords         | The set of normative keywords (MUST, MUST NOT, SHOULD, SHOULD NOT, MAY, etc.) used in SSoT requirements. | — | — | RFC 2119 | `00-Vision` |
| `TERM-SONARQUBE-READY` | SonarQube-Ready              | The project's code and build pipeline are structured to be analysable by SonarQube.          | — | — | `00-Vision` §17    | `20-Coding-Standards` |
| `TERM-CODEQL`   | CodeQL                          | GitHub-native static analysis engine used in CI.                                             | — | — | GitHub (informative) | `20-Coding-Standards` |
| `TERM-DEPENDABOT` | Dependabot                      | GitHub-native dependency update automation used in Pine.                                    | — | — | GitHub (informative) | `28-CI-CD`       |
| `TERM-CONVENTIONAL-COMMITS` | Conventional Commits | A commit-message convention used by Pine.                                                  | — | — | conventionalcommits.org (informative) | `35-Git-Workflow` |
| `TERM-SEMVER`   | Semantic Versioning (SemVer)    | The versioning scheme used by Pine for the core and for the Plugin SDK.                     | — | CalVer (rejected for the core) | semver.org (informative) | `22-Release-Plan` |
| `TERM-TRADEMARK` | Trademark                       | A legally protected sign (name, logo) identifying the source of goods or services. Pine protects its name and logo. | — | — | `00-Vision` §12    | `24-Contributing` |
| `TERM-RISK-BASED-TESTING` | Risk-Based Testing       | A test strategy that prioritises testing effort by the risk of failure and the cost of failure. | — | — | ISTQB Advanced (informative) | `18-Test-Strategy` |
| `TERM-REQUIREMENT-TRACEABILITY` | Requirement Traceability | The ability to follow a requirement forward to design, code and tests, and backward from tests to requirements. | Traceability | — | IEEE 29148 (informative) | `19-Traceability-Matrix` |

## 12. Anti-terms Index (Pine rejects the following)

| Anti-term                | Why rejected                                                                          | Use instead                |
|--------------------------|----------------------------------------------------------------------------------------|----------------------------|
| MVP                      | Pine ships the full product; releases are sequenced.                                  | Release window             |
| Freemium                 | Implies a paid tier; violates P-05.                                                   | Donation model             |
| Cloud-first              | Implies a network dependency; violates P-02.                                           | Offline First, Cloud Optional |
| CalVer                   | Incompatible with public Plugin SDK versioning; breaks traceability.                    | Semantic Versioning        |
| App store                | Implies a marketplace that takes a fee; violates P-05.                                  | Plugin Marketplace         |
| Web-only                 | Violates P-06.                                                                         | Cross Platform             |
| Wallet (general)         | Overloaded with crypto semantics; in Pine, "account" is the generic term.             | Account                    |
| Tag (in place of Category) | "Tag" is reserved for free-form labels and is not the budgeting primitive.            | Category                   |
| Paid AI API              | Violates BP-04.                                                                       | Local AI                   |
| Open Banking (as a product) | Pine is not a banking aggregator; bank statement *file* import is in scope.           | Import (file-based)        |
| Source-available         | Imposes fewer user freedoms than P-03.                                                 | Open Source                |

**Rationale.** A single anti-term index is easier to maintain than
scattering "do not use" notes across many sections. The list is
normative: introducing one of these terms in a SSoT document requires
an ADR.

## 13. Aliases Index (same concept, different name)

| Canonical term        | Allowed alias(es)        | When the alias is preferred                                  |
|-----------------------|--------------------------|--------------------------------------------------------------|
| Plugin                | Extension, Add-on        | User-facing copy only; engineering text uses "Plugin".        |
| Theme                 | Skin (legacy)            | Legacy import paths only; new code uses "Theme".             |
| Categorisation        | Categorization            | American spelling; both allowed, first is canonical.         |
| Exchange Rate         | FX rate                  | Compact usage in code; documentation uses "Exchange Rate".  |
| Investment            | Holding                  | User-facing copy only.                                       |
| Subscription          | Recurring service        | User-facing copy only.                                       |
| Savings Goal          | Goal                     | User-facing copy only.                                       |
| Locale                | Language (loose)         | User-facing copy only. Engineering must use "Locale".         |
| i18n                  | Internationalisation     | When first introducing the concept.                          |
| l10n                  | Localisation             | When first introducing the concept.                          |
| Accessibility         | a11y                     | Internal notes and short tags.                               |
| Plugin Marketplace    | Plugin registry          | Infrastructure-only contexts.                                |

> **Assumption GL-A-2.** A term and its alias must refer to the same
> concept. If a project surface needs a *different* concept, it must
> get its own `TERM-*` and its own row in §5–§11.

## 14. Governance of this Glossary

- **Owner.** The Engineering Review Board is the registrar of the
  glossary. Changes follow §15.
- **Authoring rule.** New terms are added by adding a row to the
  appropriate table. Removing a term is allowed only if no approved
  SSoT document references it; if any document does, the term becomes
  *deprecated* (alias row) and the referencing document is updated.
- **Conflict rule.** If two terms seem to describe the same concept,
  the *older* `TERM-*` wins. The newer one becomes an alias or is
  removed.
- **External standards rule.** Where a term is also defined by an
  external standard, that definition prevails unless an ADR says
  otherwise.

## 15. Change Control

| Change type             | Required action                                                                       |
|-------------------------|----------------------------------------------------------------------------------------|
| Add a new term          | Add a row; reference the owner document; commit.                                      |
| Update a definition     | Add a row to §16 revision history with rationale; commit.                              |
| Deprecate a term        | Mark as alias; update referencing documents; commit.                                  |
| Remove a term           | Only if no approved document references it; otherwise deprecate.                       |
| Add or change an anti-term | Add or update §12 row; ADR required.                                              |

## 16. Revision History

| Version | Date       | Author                | Change                                                       |
|---------|------------|-----------------------|--------------------------------------------------------------|
| 0.1     | 2026-07-08 | Engineering Review Board | Initial glossary. Subsumes inline definitions from `00-Vision`, `01-Business-Requirements`, `02-Product-Scope`. |

---

## Appendix A — Cross-Reference Map (selected)

| Concept area        | SSoT document(s) of record                                    |
|---------------------|----------------------------------------------------------------|
| Principles (P-01..P-13) | `00-Vision` §6                                              |
| Business policies (BP-01..BP-11) | `01-Business-Requirements` §6                       |
| Storage modes       | `00-Vision` §10, `14-Synchronization`                           |
| AI posture          | `00-Vision` §11, `13-AI-Architecture`                          |
| Scope IDs           | `02-Product-Scope` §8                                          |
| Test terminology    | `18-Test-Strategy`                                             |
| Security terms      | `15-Security`                                                  |
| Localisation terms  | `41-Localization`                                              |
| Accessibility terms | `40-Accessibility`                                             |
| Plugin terms        | `12-Plugin-Architecture`, `33-Plugin-SDK`                      |
| Sync terms          | `14-Synchronization`                                           |
| Observability terms | `30-Observability`                                             |
| Methodology terms   | `20-Coding-Standards`, `21-Definition-of-Done`, `25-ADR`, `35-Git-Workflow` |
