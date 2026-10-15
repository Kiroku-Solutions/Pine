# 05 — Personas

| Field            | Value                                                                                |
|------------------|--------------------------------------------------------------------------------------|
| Document ID      | `PINE-DOC-05-PERSONAS`                                                               |
| Status           | Non-normative — user archetypes                                                      |
| Reconciled       | 2026-08-31 · Personas drive use cases; they are not themselves implementable. **2026-10-05 · PERS-07 (the plugin author) is retired, not re-aimed: ADR-0021 decided there is no plugin host and no SDK for a third-party author to target, so there is no compiled-in-seam equivalent of her goals to redirect her toward. §6.2 and §7's coverage row are struck through rather than deleted (TD-147). See `04-Stakeholders` for the matching `SH-PLUGIN-AUTHOR` correction.** |
| Owner            | Engineering Review Board                                                             |
| Created          | 2026-07-08                                                                           |
| Last Updated     | 2026-07-08                                                                           |
| Specification Standard | IEEE 29148 (stakeholder needs)                                                  |
| Glossary         | `PINE-DOC-03-GLOSSARY`                                                               |
| Prerequisites    | `00-Vision`, `01-Business-Requirements`, `02-Product-Scope`, `03-Glossary`, `04-Stakeholders` |
| Decisions closed | None                                                                                 |
| Related Documents| `04-Stakeholders`, `06-Use-Cases`, `07-Functional-Requirements`, `47-User-Stories`, `48-Acceptance-Criteria` |

---

## 1. Purpose

Refine the primary stakeholders of Pine into *personas*: archetypal
users with goals, frustrations, environment, proficiency and
accessibility needs. Personas are the bridge between stakeholders
(`04-Stakeholders`) and use cases (`06-Use-Cases`), and they drive
user stories (`47-User-Stories`).

## 2. Scope

In scope: five primary personas (one per primary stakeholder), one
contributor persona and one plugin-author persona.

Out of scope: anti-personas (stakeholders Pine refuses to serve —
already captured in `04-Stakeholders` §6).

## 3. Definitions

| Term      | Working definition                                                                                |
|-----------|----------------------------------------------------------------------------------------------------|
| Persona   | An archetypal user with goals, frustrations, environment, proficiency and accessibility needs.     |
| Goal      | What the persona is trying to achieve.                                                             |
| Frustration | A pain the persona experiences with current tools.                                              |
| Environment | Platforms, devices, network conditions, locale in which the persona operates Pine.              |
| Accessibility need | A documented accessibility requirement the persona has.                               |

> **Assumption PR-A-1.** Personas are *archetypes*, not real users.
> They are used to drive requirements and tests, not to exclude
> real users.

## 4. Persona Template

Each persona uses the template below. Every persona MUST fill every
field; missing fields are a defect.

| Field                | Description                                                                       |
|----------------------|-----------------------------------------------------------------------------------|
| ID                   | `PERS-*`                                                                          |
| Name                 | Short, memorable name                                                              |
| Photo / icon         | *Not used* (no images)                                                            |
| Quote                | A first-person quote capturing the persona's relationship with money              |
| Stakeholder link     | `SH-*` from `04-Stakeholders`                                                     |
| Demographics         | Age range, region, household, occupation (range, not exact)                        |
| Platforms            | Which of the 5 platforms this persona uses Pine on                                |
| Network              | Typical network availability (always-on, intermittent, offline)                  |
| Proficiency          | Self-assessed (low / medium / high) for finance, for tech                         |
| Primary goals        | 2–5 goals                                                                          |
| Frustrations         | 2–5 frustrations                                                                   |
| Accessibility needs  | Any (vision, hearing, motor, cognitive). "None" is allowed.                         |
| Locale               | Primary locale (used to drive i18n tests)                                          |
| Currency             | Primary reporting currency                                                        |
| Anti-patterns        | Behaviour Pine must not encourage in this persona                                |

## 5. Primary Personas

### 5.1 PERS-01 — Sara, the privacy-conscious end user

- **Stakeholder link.** `SH-END-USER`.
- **Quote.** "If a free product is free because I am the product, I
  want a different product."
- **Demographics.** 28–45, urban, single or partnered, knowledge
  worker.
- **Platforms.** Windows desktop + Android phone.
- **Network.** Intermittent (commute, travel).
- **Proficiency.** Medium finance, medium tech.
- **Primary goals.**
  1. Track accounts and transactions in one place.
  2. Keep her financial data on her devices, not on someone else's
     server.
  3. Get a clear monthly picture without entering everything by hand.
  4. Use the app without a network connection.
- **Frustrations.**
  1. Ad-supported finance apps that profile her.
  2. Subscription paywalls on basic features.
  3. Apps that require an account to start.
  4. Apps that send data to "improve the service".
- **Accessibility needs.** None declared; UI must still meet WCAG 2.2 AA.
- **Locale.** `en-GB` (also tested with `es-EC`).
- **Currency.** EUR (also tested with USD, GBP).
- **Anti-patterns.** Pine must not show ads, must not require a
  cloud account to start, must not phone home by default.

### 5.2 PERS-02 — Daniel, the power user

- **Stakeholder link.** `SH-POWER-USER`.
- **Quote.** "I will customise the tool until it does what I need,
  and not before."
- **Demographics.** 30–55, mixed, technical professional.
- **Platforms.** Linux + Windows.
- **Network.** Always-on at desk, intermittent on the move.
- **Proficiency.** High finance, high tech.
- **Primary goals.**
  1. Replace a fragile spreadsheet with a real engine.
  2. Use a self-hosted AI model and swap it when a better one appears.
  3. Write custom rules and custom reports.
  4. Sync his encrypted vault to his own Git repository.
- **Frustrations.**
  1. Tools that hide the data model.
  2. Tools that bundle AI without a way to opt out.
  3. Tools with no scripting or extension story.
- **Accessibility needs.** None declared.
- **Locale.** `en-US`.
- **Currency.** USD.
- **Anti-patterns.** Pine must not require a vendor-specific AI model
  to be present, must not hide its plugin boundaries, must not
  restrict rule writing.

### 5.3 PERS-03 — María, the multi-platform user

- **Stakeholder link.** `SH-MULTI-PLATFORM-USER`.
- **Quote.** "I want the same data on my laptop, my phone and my
  partner's tablet."
- **Demographics.** 25–40, urban, dual-income household.
- **Platforms.** macOS + iOS + iPadOS.
- **Network.** Mostly always-on; travels.
- **Proficiency.** Medium finance, medium tech.
- **Primary goals.**
  1. One dataset, identical UX across Apple devices.
  2. Add a transaction on the phone, see it on the laptop.
  3. Share a read-only view with her partner (in scope as a
     role-ready architecture feature, not a "social" feature).
- **Frustrations.**
  1. Apps that drift between platforms.
  2. Apps that work on macOS but not on iPad.
  3. Cloud-only apps that stop working on a plane.
- **Accessibility needs.** Larger font (DPI scaling).
- **Locale.** `es-ES` (also tested with `es-EC`, `ca-ES`).
- **Currency.** EUR.
- **Anti-patterns.** Pine must not produce a "lite" iPad build that
  drops a capability available on macOS, must not require network for
  any core feature.

### 5.4 PERS-04 — Hiro, the international user

- **Stakeholder link.** `SH-INTL-USER`.
- **Quote.** "I work in JPY, my mortgage is in USD, and my family
  sends money in IDR. I want a tool that does not make me choose a
  single currency."
- **Demographics.** 30–50, Tokyo / Jakarta region, professional.
- **Platforms.** Windows + Android.
- **Network.** Always-on at home, intermittent on the move.
- **Proficiency.** Medium finance, medium tech.
- **Primary goals.**
  1. Track accounts in JPY, USD, IDR, EUR.
  2. See net worth in a single reporting currency.
  3. Use the UI in Japanese, with Japanese regional formatting and
     timezones.
  4. Use the app in an RTL locale too (testing).
- **Frustrations.**
  1. Apps that round in surprising ways.
  2. Apps that ignore the user's timezone.
  3. Apps with broken RTL rendering.
- **Accessibility needs.** None declared.
- **Locale.** `ja-JP` (RTL smoke test with `ar-EG`).
- **Currency.** JPY (reporting), USD, IDR, EUR.
- **Anti-patterns.** Pine must not assume a single currency, must not
  assume a LTR locale, must not use a single timezone for date math.

### 5.5 PERS-05 — Lina, the AI-curious user

- **Stakeholder link.** `SH-AI-CURIOUS-USER`.
- **Quote.** "I want a tool that can read a receipt, and a tool that
  will not leak the photo to a stranger."
- **Demographics.** 25–45, mixed, professional.
- **Platforms.** Android + Windows.
- **Network.** Intermittent.
- **Proficiency.** Medium finance, low-to-medium tech.
- **Primary goals.**
  1. Photograph a receipt, have Pine fill the expense form.
  2. Get local, explainable category suggestions.
  3. Get a local forecast for next month.
  4. Keep her photos on her device.
- **Frustrations.**
  1. "Free" receipt scanners that upload the photo to a server.
  2. AI suggestions she cannot understand or disable.
  3. AI that requires an account and a subscription.
- **Accessibility needs.** None declared.
- **Locale.** `fr-FR`.
- **Currency.** EUR.
- **Anti-patterns.** Pine must not upload receipt photos by default,
  must not call a paid third-party AI API, must always show a
  confidence score, must always allow a user override.

## 6. Contributor and Plugin Personas

### 6.1 PERS-06 — Tom, the contributor

- **Stakeholder link.** `SH-CONTRIBUTOR`, `SH-DOCS-CONTRIBUTOR`.
- **Quote.** "I want my first PR to be small, well-scoped and
  welcomed."
- **Demographics.** Any age, anywhere, software engineer.
- **Platforms.** Linux.
- **Network.** Always-on.
- **Proficiency.** Medium-to-high tech.
- **Primary goals.**
  1. Find a "good first issue" they can fix in a few hours.
  2. Get a fair code review.
  3. Get public credit.
- **Frustrations.**
  1. Style guides that are 50 pages long.
  2. CI that fails for reasons unrelated to the change.
  3. Stale maintainer activity.
- **Accessibility needs.** None declared.
- **Anti-patterns.** Pine must not block PRs on cosmetic-only feedback
  during the first review, must keep CI green on `main`.

### 6.2 PERS-07 — Aiko, the plugin author

> **Retired, ADR-0021 (TD-147).** Pine will not load third-party code; there is no
> plugin host, no SDK and no marketplace for this persona's goals to be served by,
> and no compiled-in-seam equivalent to re-aim her at — the seams are maintained by
> the project, not by independent authors. The body below is kept as written rather
> than deleted, as the record of a goal the product has declined to serve.

~~- **Stakeholder link.** `SH-PLUGIN-AUTHOR`.
- **Quote.** "I want a stable SDK and a marketplace that does not
  punish me for being independent."
- **Demographics.** 25–50, mixed, software engineer.
- **Platforms.** macOS, Windows, Linux.
- **Proficiency.** High tech.
- **Primary goals.**
  1. Build a plugin that uses a stable public contract.
  2. Distribute it through the official marketplace.
  3. Get analytics on installs and updates.
- **Frustrations.**
  1. SDKs that break every release.
  2. Permission models that are vague.
  3. Marketplaces that bury independent plugins.
- **Anti-patterns.** Pine must not break the SDK without a deprecation
  window, must not require paid listing.~~

## 7. Persona Coverage Matrix

| Persona ID | Stakeholder                  | Primary SCOPE-* modules exercised                                  |
|------------|------------------------------|---------------------------------------------------------------------|
| PERS-01    | `SH-END-USER`                | SCOPE-01..05, 23                                                  |
| PERS-02    | `SH-POWER-USER`              | SCOPE-21, 22, 31; ~~uses plugin SDK~~ uses compiled-in extension seams (ADR-0021) |
| PERS-03    | `SH-MULTI-PLATFORM-USER`     | SCOPE-01, 02, 03, 23                                               |
| PERS-04    | `SH-INTL-USER`               | SCOPE-01, 02, 03, 11, 16                                           |
| PERS-05    | `SH-AI-CURIOUS-USER`         | SCOPE-19, 21, plus the receipt scanning flow                       |
| PERS-06    | `SH-CONTRIBUTOR`             | All, via the SSoT and the contribution flow                        |
| ~~PERS-07~~ | ~~`SH-PLUGIN-AUTHOR`~~ | ~~SCOPE-22; uses the Plugin SDK~~ **Retired, ADR-0021 (TD-147)** |

**Rationale.** Each persona must map to at least one SCOPE-* module.
This prevents personas from becoming decorative.

## 8. Persona-Level Assumptions

| ID          | Assumption                                                                                            |
|-------------|--------------------------------------------------------------------------------------------------------|
| PR-A-1      | Personas are archetypes; real users may combine traits.                                              |
| PR-A-2      | A real user with accessibility needs must be supported even if not in a primary persona.              |
| PR-A-3      | The five primary personas may be split into additional personas later, without violating the SSoT.    |

## 9. Deferred Decisions

| ID     | Decision                                                                                  | Owning document(s)         |
|--------|-------------------------------------------------------------------------------------------|----------------------------|
| PR-D-1 | When to add a new persona (criteria for promotion from stakeholder to persona).          | `05-Personas` (next rev.)  |
| PR-D-2 | Persona-specific UX research method (interviews, surveys, log analysis).                 | `42-UX-Guidelines`         |
| PR-D-3 | How to handle "shared" / "joint" personas (e.g. partners).                                | `05-Personas` (next rev.)  |

## 10. Quality Checklist

- [x] Five primary personas, one per primary stakeholder.
- [x] One contributor and one plugin-author persona. **(The plugin-author persona, PERS-07, is retired — ADR-0021, TD-147.)**
- [x] Every persona uses the full template; no empty fields.
- [x] Anti-patterns are present and trace to principles.
- [x] Coverage matrix maps personas to SCOPE-* modules.
- [x] No TODO placeholders.
- [x] No contradiction with upstream SSoT documents.
- [x] IEEE 29148-style structure.

## 11. Revision History

| Version | Date       | Author                | Change                                                       |
|---------|------------|-----------------------|--------------------------------------------------------------|
| 0.1     | 2026-07-08 | Engineering Review Board | Initial personas for Pine.                                |
