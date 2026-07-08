# 55 — Future Vision

| Field            | Value                                                                                |
|------------------|--------------------------------------------------------------------------------------|
| Document ID      | `PINE-DOC-55-FUTURE-VISION`                                                           |
| Status           | Draft — pending review                                                               |
| Owner            | Engineering Review Board                                                             |
| Created          | 2026-07-08                                                                           |
| Last Updated     | 2026-07-08                                                                           |
| Specification Standard | Internal (long-horizon thinking)                                              |
| Glossary         | `PINE-DOC-03-GLOSSARY`                                                               |
| Prerequisites    | `00-Vision`..`54-Maintenance`                                                        |
| Decisions closed | None (this document is itself forward-looking)                                       |
| Related Documents| `00-Vision`, `23-Roadmap`, `45-Epics`                                                |

---

## 1. Purpose

Sketch the **long-horizon vision** of Pine: the 3–10 year
horizon, the *directional* commitments, the *aspirational*
capabilities, and the *unknowns* the project will need to
navigate. This document is the *speculative* counterpart to the
*authoritative* documents.

## 2. Scope

In scope: 3–10 year horizon, directional commitments,
aspirational capabilities, unknown-but-likely forces, decision
posture.

Out of scope: current roadmap → `23-Roadmap`; current vision →
`00-Vision`; current epics → `45-Epics`; release plan →
`22-Release-Plan`.

## 3. Definitions

| Term             | Working definition                                                                                |
|------------------|------------------------------------------------------------------------------------------------------|
| Horizon          | The time window a document is thinking about.                                                       |
| Direction        | A directional commitment (e.g. "deeper on-device AI").                                              |
| Aspiration       | A capability the project would like to deliver but has not committed to.                            |
| Unknown          | A force the project has identified but cannot predict the impact of.                                |
| Pivotal moment   | A moment in time when the project's posture may need to change.                                     |

> **Assumption FV-A-1.** The future vision is *intentionally
> vague*. The document is updated when a *directional* commitment
> is changed.

## 4. Posture

- The future vision is *not* a roadmap.
- The future vision is *not* a contract.
- The future vision is *not* a set of promises.
- The future vision is a *directional* commitment and a
  *set of aspirations* the project is honest about.

## 5. Three-Year Horizon

### 5.1 Directional Commitments

- Pine is a *complete* personal-finance platform on five
  platforms.
- Pine's AI is *mostly on-device*, with optional cloud for
  consented workloads.
- Pine's plugins are a *first-class* extension surface.
- Pine's sync is *pluggable* and *zero-knowledge* by default.
- Pine's community is *bilingual* (English + Spanish at minimum)
  with a *locale-lead* per locale.

### 5.2 Aspirations

- A *complete* personal-finance experience that includes
  forecasting, scenario planning, and decision support — all
  opt-in, all on-device by default.
- A *transparent* AI: every recommendation has an explanation; every
  explanation is auditable.
- A *first-class* accessibility experience: WCAG 2.2 AA in
  first-party UI; AAA where feasible; *first-class* screen-reader
  and voice-control UX.
- A *pluggable* AI: users can swap the default model for a model
  of their choice, with a clear trust posture.

### 5.3 Unknowns

- The shape of the on-device AI market in 3 years (compute,
  memory, capability).
- The shape of the regulatory landscape (privacy, financial
  advice, AI).
- The shape of the *user* — what does the typical Pine user look
  like in 3 years?

## 6. Five-Year Horizon

### 6.1 Directional Commitments

- Pine continues to ship the *full product* on five platforms.
- Pine continues to be *local-first* with *opt-in* cloud.
- Pine continues to be *open* (weights when feasible, evaluation
  always, contracts when necessary).
- Pine continues to be *auditable* (a public SLO dashboard, a
  public threat model, a public security advisory page).
- Pine continues to be *community-led* (a Maintainer Council with
  election mechanisms).

### 6.2 Aspirations

- A *spouse-mode* and *family-mode* that respects the
  per-user-encryption posture while enabling shared visibility.
- A *plugin market* that is *self-sustaining* (revenue to plugin
  authors, not just recognition).
- A *data export* that is *fully portable* (no lock-in to a
  vendor format).
- A *cross-vendor* sync, where the user picks a sync provider
  *they* trust, with a documented trust posture.

### 6.3 Unknowns

- The shape of the *sync market* — what does a "trustable" sync
  provider look like in 5 years?
- The shape of the *open data* market — what does it mean to
  donate anonymised financial data in 5 years?
- The shape of the *AI* market — what is the right boundary
  between on-device and cloud in 5 years?

## 7. Ten-Year Horizon

### 7.1 Directional Commitments

- Pine continues to *exist* as a long-lived open-source project.
- Pine continues to *uphold* the principles in `00-Vision` §3.
- Pine continues to *evolve* the surface; the principles do not.

### 7.2 Aspirations

- A *personal-finance* experience that is *invisible* — the user
  is in flow, the system is a quiet helper.
- A *community* that has produced a *generation* of plugin
  authors, model authors, and locale leads.
- A *regulation* that recognises the *local-first* posture as
  best-in-class for privacy.

### 7.3 Unknowns

- The *shape of money* in 10 years.
- The *shape of identity* in 10 years.
- The *shape of trust* in 10 years.

## 8. Pivotal Moments

Pine will face *pivotal moments* in the next 10 years. The
project's posture is to *recognise* them and *respond* with
principles, not panic.

| Pivotal moment                                       | Posture                                                                  |
|------------------------------------------------------|--------------------------------------------------------------------------|
| A new platform (e.g. AR glasses, voice assistants).   | Evaluate against the principles; ship if the user can keep local-first. |
| A new AI paradigm (e.g. on-device general assistants). | Re-evaluate the trust posture; default to on-device.                    |
| A new regulatory regime (e.g. AI-specific laws).     | Comply; publish a *posture* document.                                    |
| A new competitor with a different posture.           | Do not chase; double down on principles.                                  |
| A new *vulnerability* in a dependency.               | Patch fast; publish a security advisory; rotate keys if needed.         |

## 9. Decision Posture

- The future vision is *updated* on every major release.
- The future vision is *re-evaluated* when a *pivotal moment* is
  reached.
- The future vision is *owned* by the Maintainer Council with
  input from the community.

## 10. Cross-Cutting Constraints

- The future vision MUST NOT contradict the principles in
  `00-Vision` §3.
- The future vision MUST NOT make promises.
- The future vision MUST be honest about unknowns.

## 11. Aspirational Capability Sketch (Indicative)

The following is a *non-binding* sketch of capabilities that may
ship in the long horizon. The list is *illustrative*, not
committed.

| Capability                                   | Horizon   | Notes                                                                  |
|----------------------------------------------|-----------|-------------------------------------------------------------------------|
| Spouse / family mode                          | 3–5 years | Per-user encryption, shared visibility; opt-in.                        |
| AI assistant for financial decisions          | 3–5 years | On-device; explanation layer; never replaces a professional adviser.   |
| Public plugin marketplace                     | 1–3 years | Per `33-Plugin-SDK`; reviewed; auditable.                              |
| Cross-vendor sync                              | 3–5 years | Pluggable sync provider; documented trust posture per provider.         |
| Decision-support scenarios                    | 3–5 years | Sandboxed; user-confirmed; never automatic.                             |
| Anonymised aggregate market signal            | 3–5 years | Differential privacy; opt-in.                                          |
| Multi-profile per user                        | 3–5 years | E.g. personal + small business.                                        |
| Plugin revenue share (if marketplace exists)  | 3–5 years | Transparent; not a subscription model.                                  |

## 12. What Pine Will Not Do

Pine's posture is clear on the things Pine *will not do*, even
in the long horizon:

- Pine will not become a bank, custodian, broker, or payment
  initiator.
- Pine will not collect and sell user data.
- Pine will not run paid third-party AI APIs on user data.
- Pine will not show advertising in the first-party UI.
- Pine will not accept exclusivity in plugin, AI, or sync.
- Pine will not sacrifice privacy, security, or accessibility for
  a feature.

## 13. Deferred Decisions

| ID        | Decision                                                                                  | Owning document(s)         |
|-----------|-------------------------------------------------------------------------------------------|----------------------------|
| FV-D-1    | Election mechanism for the Maintainer Council.                                            | `24-Contributing`          |
| FV-D-2    | Spouse / family mode encryption posture.                                                    | `15-Security` (next rev.)  |
| FV-D-3    | Plugin revenue share (when marketplace exists).                                              | `33-Plugin-SDK` (next rev.) |
| FV-D-4    | Anonymised market signal default opt-in posture.                                            | `32-AI-Training`           |

## 14. Quality Checklist

- [x] Three-, five-, and ten-year horizons, pivotal moments,
      decision posture, aspirational sketch, "will not do" are
      all present.
- [x] No TODO placeholders.
- [x] No contradiction with `00-Vision`..`54-Maintenance`.

## 15. Revision History

| Version | Date       | Author                | Change                                                       |
|---------|------------|-----------------------|--------------------------------------------------------------|
| 0.1     | 2026-07-08 | Engineering Review Board | Initial future vision document.                              |
