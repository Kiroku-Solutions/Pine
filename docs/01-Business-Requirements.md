# 01 — Business Requirements

| Field            | Value                                                                                |
|------------------|--------------------------------------------------------------------------------------|
| Document ID      | `PINE-DOC-01-BUSINESS-REQUIREMENTS`                                                 |
| Status           | Non-normative — business context and objectives                                      |
| Reconciled       | 2026-08-31 · Business objectives and success measures. Nothing here can be implemented or unimplemented by code. **2026-10-05 · ADR-0021 withdrew the plugin host and `33-Plugin-SDK`; Pine will not load third-party code. KR-01-2, KR-01-3, KR-05-1, BD-03, BC-06, BR-A-4, BP-06 and the stakeholder/metric rows naming plugins are struck through below rather than deleted (TD-147).** |
| Owner            | Engineering Review Board (acting on behalf of Project Lead)                          |
| Created          | 2026-07-08                                                                           |
| Last Updated     | 2026-07-08                                                                           |
| Specification Standard | IEEE 29148 (business requirements)                                             |
| Glossary         | See `03-Glossary` (key terms inlined below)                                          |
| Prerequisite     | `PINE-DOC-00-VISION` (approved)                                                      |
| Related Documents| `00-Vision`, `02-Product-Scope`, `04-Stakeholders`, `05-Personas`, `22-Release-Plan`, `23-Roadmap`, `25-ADR` |

---

## 1. Purpose

Capture the business context, business objectives, success measures,
business constraints, regulatory posture and high-level business
policies for Pine, in a way that is:

- Traceable to `00-Vision`.
- Independent of any technical implementation choice.
- Testable through the *acceptance* layer (`47-User-Stories`,
  `48-Acceptance-Criteria`, `49-Test-Cases`).

## 2. Scope of this Document

In scope: business context, business objectives and Key Results (OKR-style),
business policies (privacy, monetisation, support, distribution),
business constraints (licensing, legal, financial, organisational),
business success metrics and how they will be measured, regulatory
posture and the *refusal list* (what Pine will not do for regulatory
reasons), business-level assumptions and deferred decisions.

Out of scope (handled elsewhere):

- Concrete functional requirements → `07-Functional-Requirements`.
- Non-functional requirements (performance, reliability, security
  policies) → `08-NonFunctional-Requirements`, `15-Security`.
- Architecture and technology choices → `09-System-Architecture`,
  `25-ADR`.
- Product scope (in/out) → `02-Product-Scope`.
- Personas, use cases → `05-Personas`, `06-Use-Cases`.
- Release cadence → `22-Release-Plan`, `23-Roadmap`.

## 3. Definitions

| Term              | Working definition                                                                                |
|-------------------|----------------------------------------------------------------------------------------------------|
| BR                | Business Requirement. A statement at the business level, not a feature spec.                      |
| KR                | Key Result. A measurable indicator that tracks progress on an Objective.                          |
| MVP               | Minimum Viable Product. **Not used in Pine** (see `02-Product-Scope` §6, closing OD-05).        |
| OKR               | Objectives and Key Results — the framework used to track business progress.                        |
| SLO               | Service Level Objective — a numeric reliability target a feature is measured against.              |
| Telemetry         | Any data Pine sends off the device about its own operation.                                        |
| Donation model    | The accepted business model: voluntary community donations. No ads, no subscriptions, no fees.    |

> **Assumption BR-A-1.** Where a term is also defined by an external
> standard (e.g. ISTQB, IEEE 29148, GDPR, Basel/IFRS, RFC 2119), Pine
> adopts the external definition verbatim unless explicitly noted.

## 4. Business Context

### 4.1 Market Position

Pine enters a market dominated by three categories of personal finance
products:

1. **Surveillance / ad-supported** products (free in money, expensive in
   privacy).
2. **Closed subscription** products (paywalled, data-locked, vendor
   lock-in).
3. **Read-only open-source trackers** (good for transparency, weak on
   automation and AI).

Pine is positioned as a **fourth category**: a privacy-respecting,
offline-first, AI-assisted, ~~plugin-based~~ open-source platform.
**Amended by ADR-0021 (TD-147):** "plugin-based" described loading
third-party code, which Pine will not do; the extension seams are
compiled in.

**Rationale.** Positioning Pine as a *new* category (rather than a
competitor inside an existing one) is consistent with principles P-01
(Privacy First), P-04 (No Ads), P-05 (No Subscription) and P-09
(AI Powered). It also makes messaging, marketing and contribution
calls-to-action coherent.

### 4.2 Stakeholders (Business-level)

A full stakeholder analysis is owned by `04-Stakeholders`. The
business-level summary is:

| Stakeholder           | Business interest                                                          |
|-----------------------|------------------------------------------------------------------------------|
| End users             | Reliable, private, multi-platform finance management.                       |
| Community             | Healthy, transparent, merit-based governance.                              |
| ~~Plugin authors~~ (no longer served, ADR-0021; TD-147) | ~~Sustainable marketplace, predictable SDK evolution.~~ |
| Translators / localisers | Clear i18n pipeline and locale ownership.                                |
| Security researchers  | Public threat model and responsible disclosure.                            |
| Donors / sponsors     | Visible accountability for funds and outcomes.                             |
| Future maintainers    | A codebase and governance that survives founder turnover.                   |
| Independent forks     | Clear trademark policy that distinguishes official Pine from forks.       |

### 4.3 Business Drivers

| ID    | Driver                                                                                  |
|-------|------------------------------------------------------------------------------------------|
| BD-01 | Users want a tool that does not monetise their data.                                    |
| BD-02 | Users want a tool that works without an internet connection.                             |
| BD-03 | ~~Plugin ecosystems create long-tail value that a single vendor cannot build alone.~~ **Withdrawn, ADR-0021 (TD-147):** Pine will not have a plugin ecosystem; this driver no longer applies. |
| BD-04 | Local AI capability is becoming table-stakes for personal data.                         |
| BD-05 | Cross-platform parity is a hard requirement, not a marketing claim.                     |
| BD-06 | Sustainable funding for an open-source project requires a transparent donation model.   |
| BD-07 | A clear trademark prevents malicious forks from damaging trust.                        |

**Rationale.** Each business driver is linked back to one or more
Vision principles. If a driver is removed, the corresponding principle
must be re-examined.

## 5. Business Objectives and Key Results

Each Objective (O-…) is a *qualitative* goal. Each Key Result (KR-…) is
*measurable*. KRs that depend on telemetry must respect the telemetry
policy (`30-Observability`) — i.e. measurement must be possible without
violating P-01.

### 5.1 Objective O-01 — Adoption

> Pine is installed and used by a broad, privacy-conscious audience
> across all five target platforms.

| Key Result | Target                                                   | Measurement source                          |
|------------|----------------------------------------------------------|---------------------------------------------|
| KR-01-1    | ≥ 100,000 active installs within 12 months of v1.0 GA.   | Optional, opt-in telemetry (default off).   |
| KR-01-2    | ~~≥ 5 distinct sync provider plugins published by the community within 18 months of v1.0 GA.~~ **Withdrawn, ADR-0021 (TD-147): no plugin registry will exist.** | ~~Public plugin registry.~~ |
| KR-01-3    | ~~≥ 10 distinct AI model plugins published by the community within 18 months of v1.0 GA.~~ **Withdrawn, ADR-0021 (TD-147): no plugin registry will exist.** | ~~Public plugin registry.~~ |
| KR-01-4    | ≥ 20 locales with ≥ 80 % translation coverage within 24 months of v1.0 GA.                 | Crowdin / Weblate export.                   |

### 5.2 Objective O-02 — Trust

> Pine is, and is perceived as, the privacy-respecting choice.

| Key Result | Target                                                  | Measurement source                            |
|------------|---------------------------------------------------------|-----------------------------------------------|
| KR-02-1    | Public threat model published before v1.0.0 GA.         | `38-Threat-Model` in repo.                    |
| KR-02-2    | Zero high-severity unresolved security issues at GA.    | Security advisories + `15-Security`.          |
| KR-02-3    | Independent security audit completed before v1.0.0 GA.  | Audit report linked from `15-Security`.       |
| KR-02-4    | Telemetry is **off by default** at all times.            | Config snapshot test in CI.                   |

### 5.3 Objective O-03 — Quality

> Pine is reliable, maintainable and testable.

| Key Result | Target                                                                                | Measurement source                            |
|------------|---------------------------------------------------------------------------------------|-----------------------------------------------|
| KR-03-1    | ≥ 90 % line + branch coverage in CI for the core packages (CI gate).                  | CI report.                                    |
| KR-03-2    | 0 compiler warnings with warnings-as-errors enabled.                                  | CI report.                                    |
| KR-03-3    | Mutation score ≥ 70 % on the domain layer.                                            | Mutation testing report.                      |
| KR-03-4    | Mean time to recover (MTTR) from a P1 incident ≤ 24 h (post-GA).                       | Incident log.                                 |
| KR-03-5    | 100 % of user stories have at least one acceptance criterion traceable to a test case. | `19-Traceability-Matrix`.                     |

### 5.4 Objective O-04 — Sustainability

> Pine is funded and maintained without compromising its principles.

| Key Result | Target                                                                              | Measurement source                          |
|------------|-------------------------------------------------------------------------------------|---------------------------------------------|
| KR-04-1    | A documented donation and sponsorship policy is published before v0.5.              | `24-Contributing`, public website.          |
| KR-04-2    | Annual public finance report published for the prior year.                          | Public website.                             |
| KR-04-3    | No business model that violates principles P-01..P-13 is ever adopted.                | Governance records.                         |
| KR-04-4    | At least 3 independent maintainers with merge rights by v1.0 GA.                     | Public maintainers list.                    |

### 5.5 Objective O-05 — Modularity

> Pine is, and stays, a platform rather than a monolith.

| Key Result | Target                                                                              | Measurement source                          |
|------------|-------------------------------------------------------------------------------------|---------------------------------------------|
| KR-05-1    | ~~Plugin SDK is stable (semver) and used by ≥ 3 third-party plugins before v1.0.~~ **Withdrawn, ADR-0021 (TD-147): there is no plugin SDK and no third-party plugins.** | ~~Plugin registry.~~ |
| KR-05-2    | Public architecture tests in CI assert dependency direction rules.                  | `18-Test-Strategy`, CI report.              |
| KR-05-3    | At least one AI model swap demonstrated end-to-end (on-device) before v1.0 GA.      | Demo recording + test report.               |

**Rationale.** Objectives O-01..O-05 cover adoption, trust, quality,
sustainability and modularity. They are the *only* business objectives
tracked at the Vision level. New objectives must be approved as a
Vision revision.

## 6. Business Policies (Normative)

The policies below are binding for every downstream document and code
change. Any exception requires an ADR and a Vision revision.

| ID      | Policy                                                                                  | Traced principle(s) |
|---------|------------------------------------------------------------------------------------------|----------------------|
| BP-01   | Pine does not embed advertising, sponsored placements or third-party trackers.          | P-04                 |
| BP-02   | Pine does not charge users for the core product. No feature is paywalled.                | P-05                 |
| BP-03   | Pine does not sell or share user data with any third party, ever.                        | P-01, P-12           |
| BP-04   | All AI runs locally; paid third-party AI APIs are prohibited.                            | P-01, P-09, P-12     |
| BP-05   | Telemetry is **opt-in** with a clear, reversible consent flow.                           | P-01, P-11           |
| BP-06   | Source code is released under GPL v3. ~~Plugin baseline licence is defined in `33-Plugin-SDK`.~~ **Withdrawn, ADR-0021 (TD-147): `33-Plugin-SDK` is itself Withdrawn; no plugin licence baseline will be produced.** | P-03              |
| BP-07   | The Pine name and logo are protected by trademark. Forks must not mislead users.         | P-11                 |
| BP-08   | Security vulnerabilities are handled through a public, responsible-disclosure process.   | P-01, P-11           |
| BP-09   | Accessibility is a release-blocking quality gate, not an enhancement.                    | P-06, P-08           |
| BP-10   | Internationalisation is a release-blocking quality gate, not an enhancement.            | P-06, P-08           |
| BP-11   | Donations and sponsorship are accepted, recorded and reported publicly.                  | P-04, P-05, P-11     |

**Rationale.** Business policies are the *executable form* of the
Vision principles. They make the principles testable in CI and review.

## 7. Business Constraints

### 7.1 Legal and Licensing Constraints

| ID      | Constraint                                                                              |
|---------|------------------------------------------------------------------------------------------|
| BC-01   | The core must remain GPL v3-compatible. Any third-party dependency that conflicts with GPL v3 is rejected unless isolated behind a clearly documented boundary. |
| BC-02   | Trademark policy is published before any public beta.                                   |
| BC-03   | Privacy policy is published before any public beta.                                     |

### 7.2 Financial Constraints

| ID      | Constraint                                                                              |
|---------|------------------------------------------------------------------------------------------|
| BC-04   | Pine has **no paid product** to subsidise misuse of user data. The only sustainable model is voluntary community support. |
| BC-05   | All donations, sponsorships and grants are recorded and reported annually.              |
| BC-06   | No exclusive commercial partnership is allowed if it would bias ~~plugin neutrality~~ (no longer applicable, ADR-0021), AI model selection or storage backends. |

### 7.3 Organisational Constraints

| ID      | Constraint                                                                              |
|---------|------------------------------------------------------------------------------------------|
| BC-07   | All significant decisions are recorded as ADRs and remain public.                       |
| BC-08   | Public roadmap, public changelog, public release notes are required from v0.5.         |
| BC-09   | A documented code of conduct is required from the first public contribution channel.    |

### 7.4 Technical-Business Constraints

| ID      | Constraint                                                                              |
|---------|------------------------------------------------------------------------------------------|
| BC-10   | All five target platforms (Windows, Linux, macOS, Android, iOS) are first-class. No platform is a "lite" build. |
| BC-11   | All core features must work without network access.                                    |
| BC-12   | Local AI must run on a mainstream consumer laptop and a mid-range phone for the MVP.    |

**Rationale.** These constraints translate Vision principles into
*checkable* business commitments. They are referenced by the test
strategy (`18-Test-Strategy`) and the release checklist
(`53-Release-Checklist`).

## 8. Business Success Metrics and Measurement Plan

| Metric                                          | Source                                  | Cadence     |
|-------------------------------------------------|------------------------------------------|-------------|
| Active installs                                 | Opt-in telemetry (default off)           | Monthly     |
| Crash-free session rate                          | Opt-in diagnostics (default off)         | Per release |
| ~~Plugin count (categories from `12`)~~ (withdrawn, ADR-0021) | ~~Plugin registry~~ | — |
| Locale coverage                                  | Translation platform                    | Per release |
| Mean time to recover (P1)                       | Incident log                            | Quarterly   |
| Security advisories open at release time        | Security tracker                        | Per release |
| Donation volume                                  | Public finance report                    | Quarterly   |
| Coverage of core packages                        | CI report                                | Per PR      |
| Number of maintainers with merge rights          | Public maintainers list                  | Quarterly   |
| Public ADRs accepted per quarter                 | `25-ADR` index                          | Quarterly   |

## 9. Regulatory Posture and Refusal List

### 9.1 Regulatory Posture

Pine is a **personal information tool**, not a regulated financial
product. It does not:

- Execute payments.
- Hold funds on behalf of users.
- Provide investment advice.
- Act as a custodian of crypto or securities.
- Act as a money services business.

If a future change moves Pine into a regulated activity, an ADR is
required *and* Vision revisions §9.2 and §13 must be updated.

### 9.2 Refusal List (Business-level)

Pine will *refuse* business opportunities that:

- Require embedding advertising or trackers.
- Require paywalled features.
- Require breaking Offline First.
- Require sending user data to a third party by default.
- Require exclusive relationships with a single AI vendor, sync
  provider, or storage vendor.
- Conflict with the GPL v3 licence of the core.
- Compromise the privacy, security, or accessibility posture.

**Rationale.** A written refusal list protects future maintainers and
potential sponsors from negotiating against the project's principles.
It also makes principle drift auditable.

## 10. Distribution and Support Posture

| Area        | Posture                                                                                  |
|-------------|------------------------------------------------------------------------------------------|
| Distribution | Pine is distributed from official channels (website, GitHub, official package managers). Forks may distribute under their own names but not under the Pine trademark. |
| Support     | Community-driven (forum, chat, issue tracker). No paid support tier.                       |
| Updates     | Auto-update is **opt-in** on desktop, follows platform store conventions on mobile.      |
| Bug reports | Public issue tracker, public security advisory process.                                   |
| Feature requests | Public backlog (`44-Backlog`), public triage cadence.                                |

## 11. Business-Level Assumptions

| ID          | Assumption                                                                                            |
|-------------|--------------------------------------------------------------------------------------------------------|
| BR-A-1      | `03-Glossary` is authoritative for terminology; working definitions in §3 will be reconciled.          |
| BR-A-2      | The donation + sponsorship model is sufficient to fund long-term maintenance, infra, security audits and one staff maintainer. |
| BR-A-3      | Telemetry is acceptable to users *only* if it is off-by-default, opt-in, anonymous and reversible.    |
| BR-A-4      | ~~A community-maintained plugin ecosystem can be bootstrapped within 18 months of v1.0 GA.~~ **Withdrawn, ADR-0021 (TD-147): the assumption is moot — there will be no plugin ecosystem to bootstrap, and this is a decision, not a deferral.** |
| BR-A-5      | The trademark policy can be defined and applied without contradicting the GPL v3 licence of the code. |

## 12. Deferred Business Decisions

These items are *business* decisions that do not block this document
but must be resolved before their owning documents are produced.

| ID     | Deferred decision                                                                  | Owning document(s)         |
|--------|------------------------------------------------------------------------------------|----------------------------|
| BR-D-1 | Pricing, tiers and donation platforms (OpenCollective, GitHub Sponsors, custom).   | `24-Contributing`, public website |
| BR-D-2 | Trademark jurisdictions and enforcement budget.                                    | `TRADEMARK.md`             |
| BR-D-3 | Conflict-of-interest policy for maintainers.                                       | `24-Contributing`          |
| BR-D-4 | Sponsorship acceptance criteria (what is acceptable, what is not).                 | `24-Contributing`          |
| BR-D-5 | Code of conduct text and enforcement path.                                         | `24-Contributing`          |
| BR-D-6 | Sustainability plan and "bus factor" target.                                       | `24-Contributing`, `54-Maintenance` |

## 13. Relationship to Other Documents

- **Upstream:** `00-Vision` (authoritative).
- **Downstream inputs:** this document feeds into `02-Product-Scope`,
  `04-Stakeholders`, `05-Personas`, `06-Use-Cases`,
  `07-Functional-Requirements`, `08-NonFunctional-Requirements`,
  `22-Release-Plan`, `23-Roadmap`, `24-Contributing`, `25-ADR`.
- **Cross-cutting:** `15-Security`, `30-Observability`,
  `38-Threat-Model` will reference the business policies and refusal
  list.

## 14. Document Quality Checklist (Business Requirements-specific)

- [x] Business context and drivers are present.
- [x] Objectives and measurable Key Results are defined.
- [x] Business policies are normative and traced to Vision principles.
- [x] Business constraints (legal, financial, organisational,
      technical-business) are explicit.
- [x] Business success metrics include their measurement source.
- [x] Regulatory posture and refusal list are explicit.
- [x] All deferred business decisions are listed with their owning
      document.
- [x] No TODO placeholders.
- [x] No contradiction with `PINE-DOC-00-VISION`.
- [x] IEEE 29148-style structure (Purpose, Scope, Definitions, Body,
      Assumptions, etc.).

## 15. Revision History

| Version | Date       | Author                | Change                                                       |
|---------|------------|-----------------------|--------------------------------------------------------------|
| 0.1     | 2026-07-08 | Engineering Review Board | Initial draft of the Pine Business Requirements document. |

---

## Appendix A — Traceability Table (Business Policies → Vision Principles)

| Business Policy | Vision Principle(s)                              |
|-----------------|---------------------------------------------------|
| BP-01           | P-04 No Ads                                       |
| BP-02           | P-05 No Subscription                              |
| BP-03           | P-01 Privacy First, P-12 User-Controlled Data    |
| BP-04           | P-01 Privacy First, P-09 AI Powered, P-12        |
| BP-05           | P-01 Privacy First, P-11 Transparent             |
| BP-06           | P-03 Open Source                                  |
| BP-07           | P-11 Transparent                                  |
| BP-08           | P-01 Privacy First, P-11 Transparent             |
| BP-09           | P-06 Cross Platform, P-08 Highly Modular         |
| BP-10           | P-06 Cross Platform, P-08 Highly Modular         |
| BP-11           | P-04 No Ads, P-05 No Subscription, P-11 Transparent |

This table is normative: any change to a business policy must update
this mapping and, if needed, the corresponding Vision principle.
