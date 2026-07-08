# 17 — Risk Analysis

| Field            | Value                                                                                |
|------------------|--------------------------------------------------------------------------------------|
| Document ID      | `PINE-DOC-17-RISK-ANALYSIS`                                                          |
| Status           | Draft — pending review                                                               |
| Owner            | Engineering Review Board                                                             |
| Created          | 2026-07-08                                                                           |
| Last Updated     | 2026-07-08                                                                           |
| Specification Standard | ISO 31000 (risk management principles)                                          |
| Glossary         | `PINE-DOC-03-GLOSSARY`                                                               |
| Prerequisites    | `00-Vision`..`16-Quality-Attributes`                                                |
| Related Documents| `38-Threat-Model`, `52-Risk-Matrix`, `54-Maintenance`                               |

---

## 1. Purpose

Identify the *strategic* risks of Pine, score them, decide which to
treat, and assign an owner. The *operational* risk matrix lives in
`52-Risk-Matrix`; the *security* risks live in `38-Threat-Model`.

## 2. Scope

In scope: product, technology, community, funding, governance,
legal, market, platform, AI, sync, plugin ecosystem.

Out of scope: security threats → `38-Threat-Model`; day-to-day
operational risks → `52-Risk-Matrix`; security policies → `15-Security`.

## 3. Definitions

| Term         | Working definition                                                                                  |
|--------------|------------------------------------------------------------------------------------------------------|
| Risk         | An uncertain event that, if it occurs, has an impact on objectives.                                 |
| Likelihood   | The chance the risk occurs (1=rare, 5=almost certain).                                              |
| Impact       | The consequence if it occurs (1=negligible, 5=catastrophic).                                        |
| Score        | `Likelihood × Impact` (1..25).                                                                      |
| Inherent     | The score before any treatment.                                                                      |
| Residual     | The score after the agreed treatment.                                                               |
| Treatment    | Mitigate, Transfer, Avoid, Accept.                                                                   |

> **Assumption RA-A-1.** Risks are scored on a 5×5 matrix with
> integer values. The classification bands are: Low (1–6),
> Medium (7–12), High (13–19), Extreme (20–25).

## 4. Risk Identification Strategy

The Engineering Review Board identifies risks by:

- Walking the *product surface* of Pine (the SCOPE-* catalogue).
- Walking the *dependency surface* (upstream open-source, model
  registries, sync providers).
- Walking the *community surface* (contributors, donors, forks).
- Walking the *legal surface* (licences, trademark, privacy).
- Walking the *platform surface* (App Store rules, OS changes).

A new risk is added when:

- A new module enters product scope.
- A new platform is added.
- A new dependency is introduced.
- A new external policy (e.g. App Store rule) materially changes the
  obligations of the project.
- An ADR changes a previously low-risk surface.

## 5. Risk Register (strategic)

| ID     | Risk                                                                                    | Likelihood | Impact | Score | Treatment | Owner            |
|--------|------------------------------------------------------------------------------------------|------------|--------|-------|-----------|------------------|
| R-01   | The team is too small to maintain the long-term vision.                                  | 4          | 5      | 20    | Mitigate  | Maintainers      |
| R-02   | A paid third-party AI provider becomes the *de facto* default despite P-09.               | 2          | 5      | 10    | Mitigate  | Architecture     |
| R-03   | A sync provider changes its API or pricing in a breaking way.                             | 3          | 4      | 12    | Mitigate  | Sync             |
| R-04   | An on-device model becomes unsupported upstream.                                         | 3          | 4      | 12    | Mitigate  | AI               |
| R-05   | The Pine name is used by a malicious fork that damages trust.                             | 2          | 5      | 10    | Mitigate  | Trademark        |
| R-06   | App Store rules change in a way that conflicts with P-04, P-05 or P-13.                  | 3          | 4      | 12    | Mitigate  | Platform         |
| R-07   | A serious security vulnerability is discovered close to a release.                       | 3          | 5      | 15    | Mitigate  | Security         |
| R-08   | A contributor violates the (future) Code of Conduct.                                     | 3          | 3      | 9     | Mitigate  | Community        |
| R-09   | The donation model proves insufficient to fund maintenance.                                | 3          | 5      | 15    | Mitigate  | Community        |
| R-10   | A plugin ecosystem never forms.                                                          | 3          | 4      | 12    | Mitigate  | Ecosystem        |
| R-11   | A platform vendor changes a security policy (e.g. permission model) that breaks the app.  | 3          | 4      | 12    | Mitigate  | Platform         |
| R-12   | A new OS baseline is released and our minimum-supported list lags behind.                  | 3          | 3      | 9     | Mitigate  | Platform         |
| R-13   | A dependency is abandoned or compromised.                                                | 3          | 4      | 12    | Mitigate  | Engineering      |
| R-14   | A contributor leaves and takes deep knowledge with them.                                 | 4          | 3      | 12    | Mitigate  | Maintainers      |
| R-15   | A community fork attempts to commercialise under a confusingly similar name.              | 2          | 4      | 8     | Mitigate  | Trademark        |
| R-16   | A user's data is lost due to a corruption bug.                                            | 2          | 5      | 10    | Mitigate  | Engineering      |
| R-17   | A model swap introduces a regression in categorisation quality.                          | 3          | 3      | 9     | Mitigate  | AI               |
| R-18   | A sync conflict silently drops a user change due to a strategy bug.                       | 2          | 5      | 10    | Mitigate  | Sync             |
| R-19   | The project becomes legally entangled in a GPL dispute.                                  | 1          | 5      | 5     | Accept    | Legal            |
| R-20   | A government requests user data from the project.                                         | 1          | 4      | 4     | Accept    | Legal            |

> **Assumption RA-A-2.** Scored risks are *inherent*. The residual
> scores (after the agreed treatment) live in `52-Risk-Matrix`.

## 6. Treatment Plans (high level)

### 6.1 R-01 — Team too small

- **Treatment.** Maintain a public `bus factor`; rotate ownership of
  every bounded context; require ADR review by at least two
  maintainers for significant changes.
- **Owner.** Maintainers.
- **Linked doc.** `54-Maintenance`.

### 6.2 R-02 — Paid third-party AI

- **Treatment.** Architecture tests in CI assert that no AI module
  has a hard dependency on a paid API. Plugin trust model rejects
  plugins that call paid APIs by default.
- **Owner.** Architecture.
- **Linked doc.** `12-Plugin-Architecture`, `13-AI-Architecture`.

### 6.3 R-03 — Sync provider breakage

- **Treatment.** All sync is behind `ISyncProvider`; the user can
  switch providers at any time; the marketplace shows provider
  health and SLA.
- **Owner.** Sync.
- **Linked doc.** `14-Synchronization`.

### 6.4 R-04 — On-device model unsupported

- **Treatment.** The model registry is pluggable; the user can swap
  models; AI validation tests run on the user's data with the
  current model.
- **Owner.** AI.
- **Linked doc.** `13-AI-Architecture`, `32-AI-Training`.

### 6.5 R-05 — Malicious fork

- **Treatment.** Trademark policy; "official vs. fork" communication
  rules; marketplace allow-list.
- **Owner.** Trademark.
- **Linked doc.** `TRADEMARK.md` (deferred OD-09), `24-Contributing`.

### 6.6 R-06 — App Store rule conflict

- **Treatment.** Compliance checklist per platform; ADR for any
  conflict; transparency report.
- **Owner.** Platform.
- **Linked doc.** `29-Deployment`, `04-Stakeholders`.

### 6.7 R-07 — Critical security vulnerability

- **Treatment.** 90-day disclosure SLA; hotfix playbook; CI security gates;
  audit; incident response runbook.
- **Owner.** Security.
- **Linked doc.** `15-Security`, `38-Threat-Model`, `54-Maintenance`.

### 6.8 R-08 — CoC violation

- **Treatment.** Code of Conduct (deferred BR-D-5); reporting
  channel; conflict-of-interest policy (deferred BR-D-3).
- **Owner.** Community.
- **Linked doc.** `24-Contributing`.

### 6.9 R-09 — Donation model insufficient

- **Treatment.** Public finance report; multi-platform donations;
  sponsor page; transparent costs.
- **Owner.** Community.
- **Linked doc.** `24-Contributing`.

### 6.10 R-10 — No plugin ecosystem

- **Treatment.** Stable Plugin SDK; public SDK roadmap; sample
  plugins.
- **Owner.** Ecosystem.
- **Linked doc.** `12-Plugin-Architecture`, `33-Plugin-SDK`.

### 6.11 R-11..R-12 — Platform changes

- **Treatment.** OS baselines in `09`; platform maintainers; CI on
  all five; early access programs.
- **Owner.** Platform.
- **Linked doc.** `09-System-Architecture`, `29-Deployment`.

### 6.12 R-13 — Dependency compromise

- **Treatment.** Dependabot; SBOM; reproducible builds; CVE feed;
  CodeQL.
- **Owner.** Engineering.
- **Linked doc.** `28-CI-CD`, `20-Coding-Standards`.

### 6.13 R-14 — Knowledge concentration

- **Treatment.** Public ADRs; pair maintenance; "good first issue"
  tagging; public runbooks.
- **Owner.** Maintainers.
- **Linked doc.** `24-Contributing`, `54-Maintenance`.

### 6.14 R-15 — Commercial clone

- **Treatment.** Trademark policy; "official vs. fork" page; SPDX
  licence compliance.
- **Owner.** Trademark.
- **Linked doc.** `TRADEMARK.md`.

### 6.15 R-16 — Data loss

- **Treatment.** Crash-safe transactions; backups; DR drill;
  integrity footers.
- **Owner.** Engineering.
- **Linked doc.** `11-Data-Model`, `15-Security`, `54-Maintenance`.

### 6.16 R-17 — Model regression

- **Treatment.** AI validation tests; user-visible confidence
  scores; explicit model re-confirmation on major version bump.
- **Owner.** AI.
- **Linked doc.** `13-AI-Architecture`, `32-AI-Training`.

### 6.17 R-18 — Sync conflict bug

- **Treatment.** Conflict matrix tests; user-visible
  conflict summary; deterministic algorithm.
- **Owner.** Sync.
- **Linked doc.** `14-Synchronization`.

### 6.18 R-19..R-20 — Legal entanglement / government request

- **Treatment.** Pro-bono counsel network; transparency report; the
  project holds no user data to begin with.
- **Owner.** Legal.
- **Linked doc.** `15-Security`, `TRADEMARK.md`.

## 7. Risk Posture

> Pine accepts the technical risk of a harder MVP in exchange for the
> strategic gain of a long-lived, modular, privacy-respecting product.
> Pine does **not** accept product risks that would violate the
> principles of §6 of `00-Vision`.

## 8. Risk Review Cadence

- **Monthly.** Risk register reviewed by the Engineering Review
  Board.
- **Per release.** Risk register reviewed and updated.
- **Per ADR.** Any new ADR is checked against the risk register.
- **On incident.** A new risk is added or an existing risk is
  re-scored.

## 9. Deferred Decisions

| ID     | Decision                                                                                  | Owning document(s)         |
|--------|-------------------------------------------------------------------------------------------|----------------------------|
| RA-D-1 | Final scoring thresholds for "needs immediate treatment".                                  | `52-Risk-Matrix`           |
| RA-D-2 | Reporting format for the monthly risk review.                                              | `54-Maintenance`           |

## 10. Quality Checklist

- [x] Strategic risks across product, technology, community, funding,
      governance, legal, market, platform, AI, sync, plugin.
- [x] Scored on a 5×5 matrix.
- [x] Treatment plan and owner per risk.
- [x] Review cadence defined.
- [x] No TODO placeholders.
- [x] No contradiction with `00-Vision`..`16-Quality-Attributes`.

## 11. Revision History

| Version | Date       | Author                | Change                                                       |
|---------|------------|-----------------------|--------------------------------------------------------------|
| 0.1     | 2026-07-08 | Engineering Review Board | Initial risk register.                                     |
