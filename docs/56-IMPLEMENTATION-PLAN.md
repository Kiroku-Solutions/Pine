# 56 — Implementation Plan (Phased)

| Field            | Value                                                                                |
|------------------|--------------------------------------------------------------------------------------|
| Document ID      | `PINE-DOC-56-IMPLEMENTATION-PLAN`                                                    |
| Status           | Draft — pending review                                                              |
| Owner            | Engineering Review Board                                                             |
| Author           | Mavis (MiniMax), commissioned by the project team                                    |
| Created          | 2026-07-08                                                                           |
| Last Updated     | 2026-07-08                                                                           |
| Specification Standard | Internal (program / project plan)                                            |
| Glossary         | `PINE-DOC-03-GLOSSARY`                                                               |
| Prerequisite     | All `00-…–55-…` docs are *Draft — pending review*. This plan is consistent with them. |
| Decisions closed | None — this plan executes decisions already closed in `00–14` and `25` (ADR seeded). |
| Related Documents| Every SSoT document `00`–`55` plus `docs/_mermaid_validator/`.                       |

> **Read this first.** This is the *implementation* counterpart to the Vision (`00`), the
> Scope (`02`), the Roadmap (`23`), the Release Plan (`22`), the System Architecture
> (`09`) and the Definition of Done (`21`). Every claim in this plan is traceable to one
> or more SSoT documents; nothing here is invented against the SSoT. If a downstream doc
> must change, an ADR is opened first.

---

## 1. Purpose

Translate the Single Source of Truth into a **single, sequenced, executable plan** that a
5-person Engineering Review Board (plus plugin authors, translators and security
researchers) can deliver from day zero to GA and beyond. The plan:

- honours the 13 vision principles (`P-01..P-13` in `00-Vision` §6);
- addresses **every** `SCOPE-*` capability in `02-Product-Scope` §8;
- sequences work along the **release windows** in `23-Roadmap` §5 (v0.1 alpha → v1.0 GA
  → post-GA → v2.0);
- closes every remaining **Open Decision (`OD-*`)** in `00-Vision` §14 / `25-ADR` (or
  binds the decision to the next ADR window);
- subjects every phase to the **quality gates** (`QG-01..QG-15`) of `16-Quality-Attributes`
  §4;
- keeps the **Definition of Done** (`21-Definition-of-Done`) and the **branch / CI / ADR
  process** (`35-Git-Workflow`, `36-Branching-Strategy`, `28-CI-CD`, `25-ADR`) binding at
  every step;
- respects the **release cadence** of `22-Release-Plan`: ~12–18 months for majors, 6–8
  weeks for minors, hotfixes in days.

It is **not** an MVP plan — `02-Product-Scope` §6 closes OD-05 with no MVP. Every
capability is in scope; phases describe the *order of public exposure*, not the *size of
the product*.

---

## 2. How to read this plan

The plan is organised as:

1. **Methodology.** Phases, workstreams, how the SSoT is consumed, exit criteria.
2. **Foundation (Phase 0).** The substrate that every later phase inherits.
3. **Capability Phases (1-10).** One per release window in `23-Roadmap`.
4. **Post-GA (Phase 11).** `23-Roadmap` v1.1–v1.x + the `55-Future-Vision` 3–5 yr horizon.
5. **Architectural maturation (Phase 12).** `23-Roadmap` v2.0.
6. **Cross-cutting Workstreams.** Security, AI, observability, i18n, a11y, perf,
   testing, docs, community — applied across every phase.
7. **Doc-Trace Matrix.** Every one of the 56 SSoT docs → the phases that own it.
8. **Risks, Gates, Tooling, Communications.**

Two key ideas you must internalise:

- A phase is *owned* by a Release Window (per `23-Roadmap`). Capability delivery is
  sequenced inside the phase, not pushed across phases.
- The **quality gate** is *the* deliverable. A phase is not done until every `QG-*` that
  applies to it is green; the release-blocker is enforced by CI, not by humans
  remembering.

---

## 3. Methodology

### 3.1 Phases, windows and gates (one screen)

| # | Phase / Window                | Scope (themes per `23-Roadmap` §5)                                       | Exit gate (release-blockers per `16-Quality-Attributes` §4) | Public posture (per `22-Release-Plan` §5) |
|---|-------------------------------|------------------------------------------------------------------------|----------------------------------------------------------|------------------------------------------|
| 0 | Foundations                    | Repo, CI, governance, doc tooling, scaffold.                            | `QG-01..04, QG-10..15`                                     | Internal-only                            |
| 1 | v0.1 alpha — *Core Engine*      | Local SQLite, encryption (ADR-0013), accounts, transactions, dashboard, settings. | All `QG-*`                                                  | alpha: internal maintainers + invited testers (`22-Release-Plan` §5) |
| 2 | v0.2 beta — *Imports + AI baseline* | CSV / Excel / OFX / QFX / PDF / bank-statement import; OCR (default), categorisation (default). | All `QG-*` (a11y exempt in beta per `22-Release-Plan`)        | beta: opt-in (`22-Release-Plan` §5)       |
| 3 | v0.3 beta — *Budgets, goals, recurring* | Budgets, savings goals, recurring detection, rule engine baseline.    | All `QG-*`                                                  | beta                                      |
| 4 | v0.4 beta — *Investments*        | Investment accounts, holdings, cost basis, P/L, plugin price sources.    | All `QG-*`                                                  | beta                                      |
| 5 | v0.5 beta — *Sync, plugins, marketplace* | Encrypted sync (plugin), Plugin SDK stable, marketplace, SDK 1.0.      | All `QG-*`                                                  | beta                                      |
| 6 | v0.6 beta — *Reports, analytics, forecasting* | Built-in reports, cohort / distribution analytics, on-device forecasting. | All `QG-*`                                                  | beta                                      |
| 7 | v0.7 beta — *Assistant, recommendation, anomaly* | Assistant, recommendation engine, anomaly detection, explanation layer.  | All `QG-*`                                                  | beta                                      |
| 8 | v0.8 beta — *Internationalisation + accessibility* | Full i18n baseline, RTL, regional formatting, WCAG 2.2 AA in built-in UI. | All `QG-*`                                                  | beta                                      |
| 9 | v0.9 rc — *Hardening*            | Performance budgets, mutation suite, security audit, threat model published, accessibility audit. | All `QG-*`                                                  | rc: opt-in (`22-Release-Plan` §5)         |
| 10| v1.0 GA — *Public release*      | All `SCOPE-*` shipped; sync, AI, plugin SDK stable; on five platforms.  | All `QG-*`                                                  | stable (`22-Release-Plan` §5)             |
| 11| v1.x Post-GA                    | Quality of life, ecosystem, forecasting maturity, security maturity.   | All `QG-*`                                                  | stable (LTS for last two minors per `22-Release-Plan` §7) |
| 12| v2.0 Architectural maturation   | Major with breaking changes; previewed in v1.9.                         | All `QG-*` + ADR-led migration                               | stable (24-month LTS per `22-Release-Plan` §7) |

### 3.2 Cross-cutting workstreams (run in every phase)

These run **continuously**, alongside whichever capability phase is current. Each
workstream has a named owner (per the RACI in `04-Stakeholders` §9) and produces the
artefacts named in the corresponding SSoT doc.

| Code | Workstream                          | Owning SSoT doc(s)                              | What it produces                              |
|------|-------------------------------------|------------------------------------------------|----------------------------------------------|
| WS-1 | **Governance & ethics**             | `00`, `01`, `02`, `15`, `24`                     | ADRs, CoC draft, donation policy                |
| WS-2 | **Doc tooling & SSoT integrity**    | `25`, `26`, `27`, plus the `_mermaid_validator/` | ADR index, Mermaid validators, glossary hygiene |
| WS-3 | **Engineering practice**           | `20`, `21`, `35`, `36`, `37`                     | Coding standards, DoD, branching, reviews      |
| WS-4 | **CI / CD**                         | `28`, `22`                                       | Build matrix, gates, signing, attestation      |
| WS-5 | **Release & deployment**            | `22`, `29`, `36`                                 | Cadence, channels, rollback, feature flags    |
| WS-6 | **Security**                        | `15`, `38`, `12`, `33`                           | Encryption, threat model, plugin sandbox       |
| WS-7 | **AI subsystem**                    | `13`, `32`, `30`, `31`                           | Model registry, training pipeline, runtime    |
| WS-8 | **Observability**                   | `30`, `22`, `29`                                 | Logs, metrics, traces, DIB                    |
| WS-9 | **Test engineering**                | `18`, `19`, `43`–`51`                            | Test pyramid, mutation, traceability          |
| WS-10| **Performance**                     | `39`, `16`, `29`                                 | Frame/stopwatch budgets, nightly benchmarks    |
| WS-11| **Accessibility**                   | `40`, `08`, `42`                                 | WCAG 2.2 AA, a11y test suite, manual audit     |
| WS-12| **Internationalisation**            | `41`, `03`, `42`                                 | Locales, ICU, RTL, pseudo-locale builds        |
| WS-13| **UX / design system**              | `42`, `40`, `41`                                 | Tokens, IA, components                       |
| WS-14| **Sync & conflict**                 | `14`, `11`, `18`                                 | Envelope, three-layer merge, sync conformance  |
| WS-15| **Plugin ecosystem**                | `12`, `33`, `24`                                 | SDK, marketplace, allow-list                  |
| WS-16| **Community & contribution**        | `24`, `21`                                       | Contributor docs, DCO, community channels      |
| WS-17| **Maintenance & incident response** | `54`, `17`, `52`                                 | Runbooks, on-call, post-mortems                |
| WS-18| **Telemetry & analytics opt-in**    | `30`, `01` (BP-05)                                | Opt-in flows, observability hooks             |

### 3.3 What "addressing" a document looks like

For every `00-…55-…` document, the plan records:

- the **phases** in which the document's content is **executed**;
- the **workstreams** that own its continuous aspects;
- the **gates** that verify it.

Section §7 ("Doc-Trace Matrix") does this for every doc. Section §4 ("Phase catalogue")
does the inverse: for every phase, the docs and the artefacts produced.

### 3.4 Exit criteria (the gates we cannot bypass)

A phase is **done** only when, *simultaneously*:

- All `SCOPE-*` items named in the phase have at least one passing test case
  (`18-Test-Strategy` §12, `49-Test-Cases`).
- The release-blockers from `16-Quality-Attributes` §4 (`QG-*`) relevant to the
  phase's scope are green.
- The release window's pre-release gate (`53-Release-Checklist` §4) passes.
- Every chapter in `21-Definition-of-Done` holds for the changes that landed in the
  phase: universal DoD, code DoD, docs DoD, AI model DoD (where AI was touched),
  plugin SDK DoD (where the SDK was touched), infra DoD (where infra was touched),
  security & privacy DoD (where security was touched).

### 3.5 Engineering practice that does not change across phases

These are non-negotiable from Phase 0 onwards:

- **Trunk-based** branching (`36-Branching-Strategy` §4). `main` is always releasable.
- **Conventional Commits** + `Signed-off-by:` (DCO, `35-Git-Workflow` §5 + `25-ADR`
  ADR-0018).
- **Squash-merge**, linear history (`35-Git-Workflow` §7.3).
- **Two-reviewer approval** for any policy doc or any architectural PR (`37-Code-Review`
  §7); one reviewer with domain expertise otherwise.
- **Warnings-as-errors**, `Nullable enable`, `var`-only when obvious (`20-Coding-Standards`
  §4).
- **Architecture tests** that fail the build if dependencies flow the wrong way
  (`09-System-Architecture` §6 + `18-Test-Strategy` §4).
- **Mermaid parse gate** in CI (`26-Diagrams` §9 + `28-CI-CD` §6).
- **OpenTelemetry instrumentation** on hot paths from day one (`30-Observability` §6).
- **Hexagonal boundaries** (ports / adapters) at every external concern (`09-System-Architecture`
  §5 + `12-Plugin-Architecture` §5).
- **Money is a `decimal` value object**; never floating point (`09-System-Architecture`
  XCD-04 + `20-Coding-Standards` §13).
- **Time is `IClock`**; randomness is `IRandom` (`09-System-Architecture` XCD-03 +
  `20-Coding-Standards` §11).
- **Errors are values** at the application boundary (`09-System-Architecture` XCD-08 +
  `20-Coding-Standards` §9 + `34-API-Guidelines` §8).
- **Plugin contracts are DTOs**, not plugin types (`33-Plugin-SDK` §5.2).

---

## 4. Foundation — Phase 0

The phase is **non-public**; it produces the substrate that every later phase builds on.
It addresses documents 09, 11, 12, 13, 14, 15, 20, 21, 22, 25, 26, 27, 28, 30, 35, 36, 37, 38
at the *foundation* level — i.e. we lay the rails, not the stations.

### 4.1 Goals

1. A **monorepo** matching `27-Development-Guidelines` §5, with the exact project
   topology laid out below.
2. A **working `main`** that builds, tests, lints, formats, runs architecture tests,
   parses every Mermaid block in `docs/`, generates the ADR index, and signs
   nothing yet.
3. A `dotnet` global tool **`pine`** that exposes profile-scoped slash commands per
   `27-Development-Guidelines` §4.4 (at least `pine dev init`, `pine dev mermaid`,
   `pine test unit`, `pine backlog list`, `pine epics list`, `pine risks new`).
4. A **DDx baseline**: Pinned `.NET SDK LTS`, Node 20, SQLite CLI 3.45+, Git 2.40+ per
   `27-Development-Guidelines` §4.1.
5. A **policy baseline**: `PINE-DOC-00-VISION` approved and immutable (per `25-ADR`
   ADR-0017 / `00-Vision` §6 — principle change requires a new Vision revision).
6. The **20 seeded ADRs** of `25-ADR` §11 written in `docs/adr/NNNN-*.md`, with the
   `INDEX.md` regenerated by a CI step.

### 4.2 Repo topology (per `27-Development-Guidelines` §5)

```
pine/
├── src/
│   ├── Pine.Core/                  # domain (15 BCs, aggregates, value objects, domain events)
│   ├── Pine.Application/          # commands, queries, handlers, cross-context events
│   ├── Pine.Infrastructure/       # adapters (SQLite, encryption, OS-keychain, ...)
│   ├── Pine.Cli/                  # dotnet global tool ('pine')
│   ├── Pine.Plugins.Sdk/          # public SDK contracts (in default ALC)
│   ├── Pine.Plugins.Host/         # custom AssemblyLoadContext loader (per `33-Plugin-SDK`)
│   ├── Pine.Ai.Default/           # bundled default models (manifest + weights + tokenizer)
│   ├── Pine.Ai.Explainer/         # default explainability layer
│   ├── Pine.Ui.Avalonia/          # Avalonia UI shell (Linux / Win / macOS)
│   ├── Pine.Ui.Ios/               # iOS shell (backed by Avalonia)
│   ├── Pine.Ui.Android/           # Android shell (backed by Avalonia)
│   ├── Pine.Ui.Macos/             # macOS shell (backed by Avalonia)
│   ├── Pine.Ui.Windows/           # Windows shell (backed by Avalonia)
│   ├── Pine.Ui.Web/               # Web/PWA shell (backed by Avalonia)
│   └── Pine.Docs/                 # docs site (Astro + Mermaid CLI)
├── tests/
│   ├── Pine.Tests.Unit/           # xUnit + FsCheck + NSubstitute
│   ├── Pine.Tests.Integration/    # xUnit + Testcontainers (where needed)
│   ├── Pine.Tests.Mutation/        # Stryker.NET (nightly)
│   ├── Pine.Tests.Performance/     # BenchmarkDotNet + nightly k6
│   └── Pine.Tests.Accessibility/   # axe-equivalent + manual scripts (per `40-Accessibility`)
├── tools/
│   ├── MermaidValidator/          # the `_mermaid_validator/` Node tool
│   └── PineCliScripts/
├── docs/
│   ├── 00-Vision.md … 56-IMPLEMENTATION-PLAN.md
│   └── adr/NNNN-slug.md
├── CHANGELOG.md
├── README.md
└── CODEOWNERS
```

> **Note on Avalonia.** `27-Development-Guidelines` §5 lists `Pine.Ui.Web` as part of
> the Web shell. Pine is not "web only" (`00-Vision` §9.2); the Web shell is a
> *secondary* distribution channel (`29-Deployment` §4) and ships at v1.0 only.
> Phase 0 creates *scaffolds* for every UI shell; only desktop shells are wired in
> Phase 1.

### 4.3 CI pipeline (matches `28-CI-CD` §4.1)

Day-one CI gates, all required on `main`:

| Stage           | Tool / behaviour                                                                                                                  |
|-----------------|-----------------------------------------------------------------------------------------------------------------------------------|
| lint & format   | `dotnet format` + `markdownlint` + `shellcheck` + `yamllint`; warnings-as-errors (`20-Coding-Standards` §16, `28-CI-CD` §5) |
| Mermaid parse   | `tools/MermaidValidator/validate.js` runs on every doc change (`docs/_mermaid_validator/` is the ref impl) (`26-Diagrams` §6, §9) |
| Unit tests      | `dotnet test Pine.Tests.Unit` (`18-Test-Strategy` §7.1)                                                                            |
| Integration tests| `dotnet test Pine.Tests.Integration` with hermetic profile (`18-Test-Strategy` §7.2)                                                |
| ADR + traceability | `pine test trace` and a CI step that fails on FR without AC/TC and on TC without FR (`19-Traceability-Matrix` §13)            |
| Architecture tests | `NetArchTest` enforces dependency direction (`09-System-Architecture` §6)                                                            |
| Build matrix    | `dotnet build -c Release` for every RID in the `09-OS-baseline` matrix                                                              |
| Sign & attest   | None in Phase 0 (no public artefacts yet); introduced in Phase 9 (`28-CI-CD` §9, `53-Release-Checklist` §4.2)                  |
| Verification    | None in Phase 0; introduced when there is something to verify (Phase 1+)                                                              |

Mutation tests are **nightly on `main`**, not per PR, per `28-CI-CD` §7.3.

### 4.4 Doc tooling (closes the current `tools/` gap)

- The `_mermaid_validator/` package (`validate.js` + `standardize.js`) becomes
  `tools/MermaidValidator/` and a pre-commit hook + a CI stage. v10 parser is pinned
  (`26-Diagrams` §4.2).
- `pine test trace` (per `43-Testing-Artifacts` §12) ships in v0 CLI form.
- `pine dev validate-ssot` runs the *full* SSoT validator: every Mermaid block, every
  ADR in `docs/adr/` is reflected in `INDEX.md`, every `OD-NN` is either closed by an
  ADR or listed in a deferral.

### 4.5 Governance artefacts

- DCO: `CONTRIBUTING.md` is forked from the upstream DCO template. Every commit
  contains `Signed-off-by:` (`35-Git-Workflow` §5.1, `25-ADR` ADR-0018).
- CoC: deferred (BR-D-5 from `01-Business-Requirements` §12). A draft placeholder
  lives in `CODE_OF_CONDUCT.md`, replaced when `24-Contributing` advances.
- Trademark placeholder: `TRADEMARK.md` deferred (OD-09 from `00-Vision` §14).
- CoI placeholder: deferred (BR-D-3).
- Sponsorship policy: deferred (BR-D-4).

### 4.6 Deliverables (the *artefacts* Phase 0 ships)

- Monorepo at the structure above, with **empty** packages that *build* and *test-pass*.
- `Pine.Core` first aggregate: `Account` (per `10-Domain-Model` §6.1) as the
  smallest useful aggregate, with the lock at the aggregate root (`09-System-Architecture`
  XCD-07).
- `Pine.Infrastructure` first adapter: `IAccountRepository` + `SqliteAccountRepository`
  with `INSERT OR ROLLBACK` and `BEGIN IMMEDIATE` semantics (`14-Synchronization`
  §6 — single-writer concurrency per aggregate).
- `Pine.Cli` first command: `pine dev init` (calls `dotnet workload` hint, sets up
  `global.json` and `editorconfig`).
- `CHANGELOG.md` (Keep a Changelog).
- `docs/adr/INDEX.md` regenerated by CI from the 20 seeded ADRs of `25-ADR` §11.

### 4.7 Exit criteria (Phase 0)

- [ ] Every ADR in `25-ADR` §11 has its `docs/adr/NNNN-…​.md` file; `INDEX.md` is in
      sync.
- [ ] CI is green on `main` for `lint & format`, `Mermaid parse`, `unit`, `integration`,
      `architecture`, `ADR + traceability`, `build matrix`.
- [ ] `docs/_mermaid_validator/validate.js` exits 0 against every Mermaid block in the
      SSoT.
- [ ] `pine dev init` works on a fresh checkout on Linux, macOS, Windows.
- [ ] Coding-standards-style lints and `dotnet format` are wired into pre-commit and CI.
- [ ] `21-Definition-of-Done`'s *Universal DoD* holds for the seed commits.

---

## 5. Capability phases (1–10)

Each capability phase:

- **inherits Phase 0**'s rails.
- **delivers** a themed set of `SCOPE-*` items (per `02-Product-Scope` §8) and the
  corresponding `FR-*` (per `07-Functional-Requirements`).
- **is gated** by the release-pre gates of `53-Release-Checklist` §4.
- **publishes** to the indicated channel per `22-Release-Plan` §5.

Each phase ends with a **recap** that updates the traceability matrix
(`19-Traceability-Matrix` §13) and re-evaluates the `17-Risk-Analysis` heat map.

---

### Phase 1 — v0.1 alpha — *Core Engine* (≈ 6 weeks of work for a small team; alpha internal)

**Owns docs (in addition to Foundation workstreams):** `00` (vision-level principles),
`01`, `02`, `03`, `04`, `05`, `06` §UC-01/UC-05/UC-10, `07-Functional-Requirements`
(FR-CORE-*, FR-SEC-01..06, FR-ACC-01..06, FR-TXN-01..05, FR-DASH-01..06, FR-SET-01..03),
`08-NFR` §Performance, Reliability, Maintainability, Testability, Modularity,
`10-Domain-Model` §6.1 / §6.2 / §6.10 (Settings), `11-Data-Model` §4.1 / §4.2 /
§4.10 / §4.16, `42-UX-Guidelines` §7 IA + §8.6 Empty states + §9.1 First run,
`44-Backlog`, `45-Epics` (open the ones that will live across all phases).

**Capabilities delivered:**

- First-run flow (UC-01): non-blocking welcome; storage-mode choice
  (`Local Only` / `Local + Cloud Sync` / `Cloud Only`); offline-first default;
  telemetry opt-in OFF.
- Local SQLite store, **AES-256-GCM encryption at rest** with **Argon2id** auth
  key derivation (`15-Security` §4 / ADR-0013). Decision between SQLCipher and an
  application-level envelope is owned by `29-Deployment`; Phase 0 picks the
  default (recommendation: SQLCipher for the first platform, application-level
  envelope behind a port for those platforms where SQLCipher is awkward).
- 5 first-class account subtypes (cash, bank, card, investment, crypto, custom) per
  `02-Product-Scope` §8 SCOPE-02; multi-currency accounts; running balance;
  reconciliation flag.
- Manual + imported transactions; splits; attachments (encrypted); per FR-TXN-*.
- Categorical tagging through the rule engine (`SCOPE-04` partial — without budgets
  yet; rules live in their own bounded context per `10-Domain-Model` §6.14).
- Dashboard skeleton (FR-DASH-01..05) with the 11 built-in widgets of
  `02-Product-Scope` §8.1 — many can show "no data yet" empty states in this phase.
- Settings (FR-SET-01..03): storage mode, security, locale, theme, telemetry OFF.
- Diagnostics panel skeleton (logs tail, SLO budget, plugin inventory,
  sync history — most empty until later phases plug in). Per `30-Observability` §8.

**Cross-cutting in this phase:**

- WS-1: Close OD-08 (`09-System-Architecture` §4 already does — confirm ADR-0003
  on file; pin to latest LTS .NET + Avalonia 12.0.x in `22-Release-Plan`).
- WS-4: CI extended with the **build matrix** RID list.
- WS-6: `15-Security` *Secure Defaults* table fully enforced.
- WS-11: a11y tooling (axe-equivalent) in CI, but the first run can ship empty
  audit records (acceptable for alpha per `22-Release-Plan` §13 Pre-release).
- WS-12: English-US source only; pseudo-locale build is wired but no translations.
- WS-13: Design tokens (`42-UX` §6) are defined; first-party UI uses them.

**Phase 1 deliverables:**

- Pine boots on Linux/Windows/macOS, Android, iOS; dashboard renders.
- `FR-CORE-*` (8), `FR-SEC-01..06`, `FR-ACC-*` (6), `FR-TXN-*` (5), `FR-DASH-01..06`,
  `FR-SET-*` (3) are implemented and trace to at least one TC and AC each.
- All `QG-*` gates that apply at *alpha* are green; some gates (`QG-05`
  accessibility — `16-Quality-Attributes` §4) can be deferred to later windows
  with an ADR.
- Sign-off by Engineering + Trust for the alpha tag (`53-Release-Checklist` §5).

**Phase 1 exit:**

- [ ] Five platforms boot, dashboard renders an empty profile.
- [ ] `FR-CORE-*` + `FR-SEC-01..06` + `FR-ACC-*` + `FR-TXN-*` + `FR-DASH-01..06` + `FR-SET-*`
      pass all gates that apply.
- [ ] Crash-injection test (NFR-REL-02) is green on a representative device.
- [ ] Public threat model draft published on the docs site (`38-Threat-Model` §10).

---

### Phase 2 — v0.2 beta — *Imports + Local AI baseline* (~ 8 weeks; beta opt-in)

**Owns docs:** `02-Product-Scope` §8.2 (receipt flow), `06-Use-Cases` §UC-02/UC-03,
`07-FR` FR-AI-01..08, FR-IMPORT-01..06, FR-FR-IMPORT-04, FR-TXN-04,
`09` (composition root), `10` §6.7 (AI), `11` §4.7 (AI tables), `13-AI-Architecture`
**all sections through §7** (receipt pipeline), `30-Observability` §5 (SLO metrics
include AI inference), `32-AI-Training` §6 (model families) §7 (pipeline basics),
`39-Performance` §10 (AI inference budget), `42-UX-Guidelines` §8.5 (notifications
for low-confidence), `43-Testing-Artifacts` (TD added for synthetic receipts).
Also touches `12-Plugin-Architecture` and `33-Plugin-SDK` only at the *AI Model*
plugin contract level — the full SDK ships in Phase 5; here we only need the
`IAIProvider` port from `13-AI-Architecture` §6.

**Capabilities delivered:**

- CSV / Excel / OFX / QFX / PDF / bank-statement **importers** with reproducible
  import profiles (`11-Data-Model` §4.13 and FR-IMPORT-01..06).
- **Receipt scanning pipeline** (`02-Product-Scope` §8.2 + `13-AI-Architecture` §7):
  Image → Receipt Detection → OCR → Layout Analysis → Field Extraction →
  Validation → Categorisation → User Confirmation → Save. Confidence per field.
  Low-confidence fields highlighted.
- **On-device OCR** default model. **Recommendation:** PaddleOCR PP-OCRv5/v6 mobile
  (~10 MB), shipped as part of the bundle (`13-AI-Architecture` §10 — default is
  bundled in the installer). Plugin-overridable.
- **On-device categorisation** default model (very small, e.g. Phi-4-mini / Llama 3.2
  3B-class quantised, or a smaller linear classifier if the chosen recipe is
  hybrid). Plugin-overridable.
- Import wizard UX (UC-03) with dialect auto-detection, column-mapping preview,
  dedupe.
- In-product receipt-photo affordance (UC-02) on Android + iOS.

**Cross-cutting in this phase:**

- WS-6 update: signed model artefact in the installer (`13-AI-Architecture` §9).
- WS-7 update: telemetry *only* for opt-in users, includes AI inference latency.
- WS-9: first AI validation suite (`18-Test-Strategy` §4, level "AI") uses a labelled
  synthetic receipt corpus (`43-Testing-Artifacts`, `51-Test-Data`).
- WS-10: a new SLO metric `pine.ai.inference.duration_ms` histogram (`30-Observability`
  §5.3); budget enforced per `39-Performance` §10.
- WS-12 still English-only; pseudo-locale build is rendered.

**Phase 2 deliverables:**

- FR-AI-01..08 implemented; FR-IMPORT-01..06 implemented; FR-TXN-04 (bulk
  categorisation/tagging) implemented.
- The first AI-run SLO dashboard is operational internally.

**Phase 2 exit:**

- [ ] Receipt E2E scenario (UC-02) passes on the reference Android device and on a
      mid-range iPhone.
- [ ] AI inferences local; no paid third-party API call is possible (architecture
      test enforces it) (`13-AI-Architecture` §13).
- [ ] Import profiles reproducible from the manifest (`11-Data-Model` §6.2).

---

### Phase 3 — v0.3 beta — *Budgets, goals, recurring* (~ 6 weeks)

**Owns docs:** `06` §UC-04, `07-FR` FR-BDG-01..04, FR-SAV-01..03, FR-RX-01..03,
`09-§15` (rule engine cross-cutting decisions), `10` §6.3 / §6.4 / §6.14,
`11-Data-Model` §4.3 / §4.4 / §4.14, `31-Rule-Engine` **all sections**, plus the
`pin-rules` plugin-grammar schema in `33-Plugin-SDK` §5 (rule packs plugin
category).

**Capabilities delivered:**

- Per-category, per-period **budgets** with soft/hard thresholds (FR-BDG-*).
- Target-based, time-bound **savings goals** with projected completion (FR-SAV-*).
- **Recurring detection** (`31-Rule-Engine` §8) with signals (interval, amount
  variance, merchant identity, account, cadence) and stability thresholds.
- **Rule Engine v1** (`31-Rule-Engine` §5–§13): declarative YAML grammar, trigger →
  condition → action, built-in packs (`pine.rules.core`, `pine.rules.recurring`,
  `pine.rules.budget`, `pine.rules.goal`, `pine.rules.access`), plugin ask with 1 s
  timeout, audit log.
- Subscription view (FR-SUB-01..03) backed by recurring rules.
- Configurable notification provider paths (`31-Rule-Engine` §9 + per
  `02-Product-Scope` §7.3).

**Cross-cutting:**

- WS-4: nightly bench now covers rule-run cost (the FR-RX / FR-SUB performance
  acceptance).
- WS-7: rule-engine fallback path documented for AI categoriser failures
  (`13-AI-Architecture` §13).
- WS-11: nudge UX conforms to a11y baseline; quiet hours supported.

**Phase 3 exit:**

- [ ] FR-BDG-*, FR-SAV-*, FR-RX-*, FR-SUB-* implemented and tested.
- [ ] Rule packs signed and shipped (`pine.rules.core` etc.); signed by the SDK
      signing key (`33-Plugin-SDK` §7).
- [ ] Rule-engine fallback works when the AI categoriser is disabled.

---

### Phase 4 — v0.4 beta — *Investments* (~ 6 weeks)

**Owns docs:** `06` §UC-07, `07-FR` FR-INV-01..05, FR-NW-01..03, FR-CF-01..02,
`09` §AI subsys still idle until Phase 7, `10` §6.5 (Investments),
`11-Data-Model` §4.5, `14-Synchronization` §6 conflict-rule rows for holdings /
price, `30-Observability` §5.3 price-refresh metric, `39-Performance` §6 startup
budget w/ investment plugin.

**Capabilities delivered:**

- **Investment accounts** with base currency and custodian.
- **Holdings** with quantity, cost basis, acquisition date, last price, last
  price source, last revalued at.
- **Corporate actions**: split, dividend, merger, symbol-change (`11-Data-Model`
  §4.5).
- **Cost basis**, realised P/L, unrealised P/L (FR-INV-02).
- **Price sources**: built-in feed (manual), plugin feed (per `12-Plugin-Architecture`
  §4 categories).
- **Net worth** across currencies (FR-NW-01..03) with a normalisation port
  (`09-System-Architecture` XCD-06 — `Money` & FX port).
- **Cash flow** (FR-CF-01..02) read-model projected from transactions.

**Cross-cutting in this phase:**

- WS-14: enforcement of the conflict-rule for "Holding price" (most recent wins,
  older kept as historical) per `14-Synchronization` §7.
- WS-15: Investment-provider plugin category matures.
- WS-10: a new "investment refresh" budget — NFR-PERF budget for fetching
  prices for 100 holdings in ≤ N seconds.

**Phase 4 exit:**

- [ ] FR-INV-*, FR-NW-*, FR-CF-* implemented and tested.
- [ ] Sample investment-plugin (e.g. a manual-only stub) exists and runs in CI.

---

### Phase 5 — v0.5 beta — *Sync, plugins, marketplace* (~ 10 weeks; the SDK-stability milestone)

**Owns docs:** `00` §10 (storage rule), `01` (BP-03 third-party data sharing),
`06` §UC-05/UC-06, `07-FR` FR-SYNC-01..06, FR-PLUGIN-01..06,
`09-System-Architecture` §10 (plugin host), `10` §6.8 / §6.9,
`11-Data-Model` §4.8 / §4.9 / §6.1 (sync envelope), `12-Plugin-Architecture` **all
sections**, `14-Synchronization` **all sections** (esp. §7 conflict matrix),
`15-Security` §10 (plugin security), `33-Plugin-SDK` **all sections**, `34-API-Guidelines`
§6 compatibility, `43-Testing-Artifacts` §10 (plugin `tests` directory in the
SDK).

**Capabilities delivered:**

- **Encrypted local-only** Cloud-Only mode with **encrypted local cache** (per
  Vision §10, ADR-0016).
- **Sync providers as plugins** (`14-Synchronization` §8): File (default fallback),
  Git, WebDAV, Google Drive, Dropbox, OneDrive, NextCloud, custom.
- **Three-layer conflict resolution** (ADR-0015): CRDT-friendly primitives for
  set-union fields; domain-aware auto-merge for commutative edits; user prompt
  otherwise — exactly the §7 table of `14-Synchronization`.
- **Plugin SDK 1.0 (stable)** with signed manifests (`33-Plugin-SDK` §7), per-package
  permissions (`12-Plugin-Architecture` §7), risk score (`12-Plugin-Architecture`
  §6), public marketplace listing (`33-Plugin-SDK` §9), conformance suite.
- **Plugin host** in-process (`33-Plugin-SDK` §5.3 — `AssemblyLoadContext` per plugin,
  contract DTOs only at the boundary; native deps and full out-of-process are
  documented as a future hardening per `12-Plugin-Architecture` §17 deferred).
- **Plugin Marketplace** in-app (FR-PLUGIN-04) with the categories of
  `12-Plugin-Architecture` §4.
- **Sample plugins**: a CSV importer, a WebDAV sync provider, a JSON exporter, a
  theme, two widgets.

**Cross-cutting in this phase:**

- WS-6: plugin sandbox review (with `15-Security` §10 / `12-Plugin-Architecture` §7).
- WS-15: allow-list initial draft.
- WS-4: nightly plug-in-conformance suite green; **plugin isolation violations**
  is SLO-06.
- WS-9: SDK conformance suite ships.

**Phase 5 exit:**

- [ ] FR-SYNC-01..06 implemented; FR-PLUGIN-01..06 implemented; the sync-envelope
      version is `pine.sync.v1`; conflict matrix test is green.
- [ ] SDK package `Pine.Plugins.Sdk` has SemVer 1.0.0 tag; the conformance
      suite runs in CI; **three** third-party plugins (per `23-Roadmap` exit for
      beta KR-05-1) exist in the wild.
- [ ] Public Plugin Marketplace page is published (even if listing only the
      sample plugins for the beta — `33-Plugin-SDK` §9).

---

### Phase 6 — v0.6 beta — *Reports, analytics, forecasting* (~ 6 weeks)

**Owns docs:** `06` §8 building-blocks for SCOPE-16/17/18, `07-FR` FR-RPT-01..04,
FR-AN-01..02, FR-FC-01..03, `10` §6.6 (Reports), §6.15 (Forecast) plus the
Forecasting projections, `11-Data-Model` §4.6 / §4.15, `13-AI-Architecture` §9
(Forecasting Architecture), §12 confidence + explainability, `31-Rule-Engine`
nudges for report-ready events.

**Capabilities delivered:**

- Built-in **report set** (categories, accounts, year-in-review, tax-ready
  exports) — `02-Product-Scope` §8 SCOPE-16.
- **Report builder** (light, in-app; not a full BI tool).
- **Cohort, distribution, correlation analytics** (SCOPE-17).
- **Forecasting** for cash flow, balance, category spend (SCOPE-18) with explanation
  layer per `13-AI-Architecture` §9 and rejectable forecasts.
- **Report subscription**: scheduled export (`02-Product-Scope` §8 SCOPE-16,
  `07-FR` FR-IMPORT-05).

**Cross-cutting:**

- WS-10: nightly benchmark covers report-pipeline latency for §16 synthetic
  dataset.
- WS-12: still pseudo-locale.
- WS-9: report outputs traceable to test data (`51-Test-Data`).

**Phase 6 exit:**

- [ ] FR-RPT-*, FR-AN-*, FR-FC-* implemented.
- [ ] Forecast explanation string is locale-aware.

---

### Phase 7 — v0.7 beta — *Assistant, recommendation, anomaly* (~ 8 weeks)

**Owns docs:** `00` §11 (AI posture, with no third-party APIs), `06` §SCOPE-21
building-blocks, `07-FR` FR-AIA-01..04, `13-AI-Architecture` §8 (Assistant)
§10 (Anomaly) §11 (Recommendation) §12 (Confidence + Explainability), §13
(on-device guarantees), `30-Observability` AI metrics, `32-AI-Training` §11/§12
(updates / corrections), `42-UX-Guidelines` §8.7 error states for AI refusals.

**Capabilities delivered:**

- **AI Assistant** (SCOPE-21) — grounded in the user's projections, citations shown,
  on-device, refuses paid APIs by construction (`13-AI-Architecture` §8).
- **Recommendation engine** (per `13-AI-Architecture` §11) — never a *purchase*
  prompt; educational; explainable.
- **Anomaly detection** stream (per `13-AI-Architecture` §10) — rule-engine
  subscribes.
- **Local fine-tuning** toggle per profile (per `13-AI-Architecture` §13) —
  default-off until v1.0.

**Cross-cutting:**

- WS-7: federated evaluation plumbing (per `32-AI-Training` §7.2).
- WS-9: AI regression suite runs nightly; per-model KPIs tracked.
- WS-10: a new SLO `pine.ai.assistant.first_token_ms` p95.

**Phase 7 exit:**

- [ ] FR-AIA-01..04 implemented; assistant never auto-mutates user data
      (`13-AI-Architecture` §9).
- [ ] Anomaly → rule-engine → notification round-trip demo passes.

---

### Phase 8 — v0.8 beta — *Internationalisation + accessibility* (~ 8 weeks)

**Owns docs:** `00` §7 / §9.1 (RTL release), `02-Product-Scope` §7.5 §7.6,
`07-FR` FR-LOC-01..06, FR-A11Y-01..06, `08-NFR` §8 / §9, `40-Accessibility`
**all sections**, `41-Localization` **all sections**, `42-UX-Guidelines` §5 / §6
(refresh for tokens), `46-Features` (no platform-specific regressions).

**Capabilities delivered:**

- **11 first-wave locales** of `41-Localization` §4 (en-US, en-GB, es-ES, es-419,
  de-DE, fr-FR, pt-BR, it-IT, ja-JP, ar-SA, he-IL) — full UI coverage before the
  "complete" badge.
- **RTL** end-to-end (icons mirrored; charts reoriented; bidi discipline
  per `41-Localization` §7).
- **Pseudo-locale** QA build (`41-Localization` §9) — `en-XA`, `ar-XB`.
- **WCAG 2.2 AA** conformance in built-in UI (`40-Accessibility` §4); a public
  conformance report.
- **Keyboard-only** walkthrough on every platform; screen reader covered on
  VoiceOver / TalkBack / NVDA / Narrator.

**Cross-cutting:**

- WS-12: every FR backed by an ICU MessageFormat test.
- WS-11: a11y automated gate (`40-Accessibility` §10.1) on every UI PR.
- WS-13: design tokens for `ar` and `he` (RTL gradients, font fallbacks).

**Phase 8 exit:**

- [ ] FR-LOC-01..06 + FR-A11Y-01..06 implemented.
- [ ] `a11y@pine` mailbox exists with an initial response SLA (`40-Accessibility`
      §14); `40-Accessibility` §10.3 user-test round logged.
- [ ] All strings in the resource set; build fails on missing key for
      "complete" locales (`41-Localization` §13).

---

### Phase 9 — v0.9 rc — *Hardening* (~ 8 weeks)

**Owns docs:** `00` §15 (success criteria), `08-NFR` §11 (SLOs, hardened),
`15-Security` §12 (independent audit), `16-Quality-Attributes` §5 (measurement plan
fully wired), `17-Risk-Analysis` reviewed, `18-Test-Strategy` §7.3 (mutation
nightly enforced), `19-Traceability-Matrix` (coverage gap is P1),
`22-Release-Plan` §8 (sign-off), `28-CI-CD` §9 (signing on),
`30-Observability` §5.3 (all SLO metrics), `38-Threat-Model` **published**
(§10), `39-Performance` §12 nightly suite, `53-Release-Checklist` **enforced**
in CI as pre-release gate.

**Capabilities delivered:**

- **Performance benchmarks** enforced as a release gate; `pine perf bench compare`
  in CI per `39-Performance` §12.3.
- **Mutation suite** nightly, with ≥ 70 % score on the domain layer
  (`18-Test-Strategy` §7.3).
- **CodeQL + Semgrep + Dependabot + SBOM (CycloneDX)** all wired into PR gates
  (`28-CI-CD` §7.6).
- **Independent security audit** either completed or contracted with a target
  close-out before v1.0 (`01-BR` KR-02-3 + `15-Security` §12).
- **Public threat model** published on the docs site (`38-Threat-Model` §10).
- **Public ADRs** accepted and indexed (`25-ADR` §7 + `16-Quality-Attributes` §4
  QG-14).
- **All deferred decisions resolved** except those whose owning document is
  explicitly post-v1.0 (e.g. `00-Vision` OD-D-3 Pine Cloud, deferred).
- **App-store submission prep** (Apple / Google / Microsoft / Snap), per
  `29-Deployment` §5.

**Cross-cutting:**

- WS-4: full build matrix enabled in CI (`28-CI-CD` §8); signed build artefacts.
- WS-6: threat-model → test-case traceability reach 100 % for TM-Critical rows
  (`19-Traceability-Matrix` §11).
- WS-9: test-data lifecycle and deprecation rules active (`51-Test-Data` §8).
- WS-17: incident-response runbook adopted (`15-Security` §12, `54-Maintenance` §6).

**Phase 9 exit (= v0.9-rc criteria per `22-Release-Plan` §5):**

- [ ] Every `QG-01..15` gate is green in CI for `release/rc` (`16-Quality-Attributes`
      §4). No exceptions without ADR.
- [ ] `38-Threat-Model` is published; every TM-≥Critical has a mitigation TC
      (`19-Traceability-Matrix` §11, `52-Risk-Matrix` §12).
- [ ] Performance benchmarks within ±10 % of baseline (`39-Performance` §13);
      no P0/P1 regression unowned.
- [ ] The list of *open* `OD-*` from `00-Vision` §14 contains only post-v1.0
      items (Pine Cloud, LSP partnership, etc.). Any `OD-*` that survives to
      v1.0 must have an ADR-driven exception recorded.

---

### Phase 10 — v1.0 GA — *Public release* (~ 4 weeks; cut + stabilisation only)

**Owns docs:** `00` §15 (vision-level success criteria), `01` (KR-01,
KR-02, KR-03, KR-04, KR-05), `07-Functional-Requirements` — *every* `FR-*` covers
real code, `22-Release-Plan` §10, `29-Deployment` **all sections**, `36-Branching-Strategy`
§6 (release branches), `40-Accessibility` §10.3 (annual a11y audit cycle has
fired at least once), `41-Localization` §13 (locales marked complete are
100 %), `53-Release-Checklist` **entire checklist**.

**What Phase 10 actually does (and does *not* do):**

- This is **not** a development phase. By GA, every capability from Phase 1–9 is
  shipped on the appropriate channel. The job of Phase 10 is to:
  1. Cut the v1.0 tag (`22-Release-Plan` §8, `35-Git-Workflow` §8).
  2. Run the full release checklist (`53-Release-Checklist` §4).
  3. Smoke-test on each real device (`53-Release-Checklist` §6).
  4. Publish release notes, blog post, changelog, security advisories (`22-Release-Plan`
     §9, `53-Release-Checklist` §5).
  5. Initiate post-release 24-hour SLO watch (`53-Release-Checklist` §6).
  6. Open a v1.1 release branch (`36-Branching-Strategy` §6).
- Phase 10 *never* adds features that were not already in a beta (`22-Release-Plan`
  §3).

**Phase 10 exit (= v1.0 GA criteria from `23-Roadmap` §5 + `00-Vision` §15):**

- [ ] All 23 `SCOPE-*` modules listed at `02-Product-Scope` §8 are shipped to the
      Stable channel on five platforms.
- [ ] All five vision-level success criteria from `00-Vision` §15 hold
      simultaneously.
- [ ] All `FR-*` covered, traceable, and at least one TC passing.
- [ ] `44-Backlog` is at zero P0, KR-04-1 (≥ 1 staff maintainer funded) holds, and
      the public finances are published.

---

### Phase 11 — v1.x — *Post-GA*

**Owns docs:** `22-Release-Plan` §7 (support matrix — last two minor lines + 18
months / 24-month LTS for majors), `23-Roadmap` §5 v1.1–v1.4 themes, `46-Features`
(new features added as new minor lines), `47-User-Stories` (new per release),
`48-Acceptance-Criteria` + `49-Test-Cases` (per release), `53-Release-Checklist` (per
release), `54-Maintenance` (EoL, dependency updates, security backports).

**Themes (per `23-Roadmap` §5):**

- **v1.1 — Quality of life.** Undo, keyboard shortcut catalogue, plugin update UX,
  mobile polish. Closes §11.1.
- **v1.2 — Ecosystem.** Theme marketplace, chart plugins, notification plugins.
  Closes §11.2.
- **v1.3 — Forecasting maturity.** Scenario editor, confidence-band UX, model-swap
  UX. Closes §11.3.
- **v1.4 — Security & privacy maturity.** Independent audit follow-up, key rotation,
  recovery UX. Closes §11.4.
- Cross-cutting each release: **dependency sweep** (`54-Maintenance` §7),
  **battery sweep** (`39-Performance` §11), **a11y sweep** (`40-Accessibility` §11),
  **locale sweep** (`41-Localization` §11).

**The post-GA pattern per release:**

1. Trim the `16-Quality-Attributes` QG table for what is relevant to this minor.
2. Plan with `44-Backlog` + `45-Epics`.
3. Execute the minor via 6–8 week cadence (`22-Release-Plan` §4).
4. Cut the minor per `35-Git-Workflow` §8.
5. Back-merge to all supported lines (`54-Maintenance` §4).

---

### Phase 12 — v2.0 — *Architectural maturation*

**Owns docs:** `22-Release-Plan` §6 (release branches + stabilisation),
`23-Roadmap` §5 v2.0 row, `33-Plugin-SDK` §10 (plugin SDK breaking change
handling), `34-API-Guidelines` §6.2 (breaking-change discipline),
`55-Future-Vision` (3–5 yr directional commitments).

**Inputs from `23-Roadmap` §5:**

- v2.0 is a **major** (`22-Release-Plan` §4) — previewed in v1.9.
- Likely breaking changes:
  - **LTS** baseline switch (e.g. .NET 12 LTS drop on `09-System-Architecture` §4).
  - **Plugin SDK 2.0** with deprecation window for 1.x (per
    `33-Plugin-SDK` §13's lifecycle).
  - **Schema-version bump** in the SQLite schema + sync envelope (`11-Data-Model` §6).
  - **Rule grammar major version** (`31-Rule-Engine` RE-D-1 maybe resolved here).
  - Possibly: **out-of-process plugins** if the in-process host proves
    insufficient for security (`12-Plugin-Architecture` TM-D-2).

**Process gate:** every breaking change is preceded by a minor release with the
deprecation flag on (`22-Release-Plan` §10, `34-API-Guidelines` §7).

---

## 6. Cross-cutting Workstreams — *the spine*

The capability phases are the visible work; the workstreams are the connective tissue
that prevents a vision-drift.

### 6.1 WS-1 Governance & ethics (`00`, `01`, `24`)

- **Always.** Maintainers Council + Engineering Review Board are seeded at Phase 0.
- The `00-Vision` *Principles* (`P-01..P-13`) are immutable at the implementation
  layer; a CI grep test fails any commit that introduces a feature violating a
  principle.
- Every two cycles the refusal list of `00-Vision` §9.2 is re-read against the
  product surface; any miss is an issue.
- ADRs are immutable; supersede, never edit (`25-ADR` §6).

### 6.2 WS-2 Doc tooling & SSoT integrity (`25`, `26`, `27`)

- `MermaidValidator` is the source of truth for syntax; v10 pin (`26-Diagrams` §4.2).
- The ADR `INDEX.md` is regenerated by CI on every change to `docs/adr/` (`25-ADR`
  §7).
- Every doc carries an "RFC 2119" / "Terminology" lint that fails the build if a new
  `TERM-*` appears in body text without an entry in `03-Glossary` (§3 Authoring
  rule).
- A weekly bot writes **stale-doc alerts** (bodies referencing things not yet in
  the SSoT) into the backlog (`44-Backlog` §11 hygiene).

### 6.3 WS-3 Engineering practice (`20`, `21`, `35`, `36`, `37`)

- Coding-standards lints and `dotnet format` are wired in Phase 0 and stay wired.
- The four types of DoD (`21-Definition-of-Done` §5–§10) are *checklist* items in
  the PR template from Phase 0 onwards.
- Review SLAs (`37-Code-Review` §9) are monitored monthly; SLA-failure is *signal*
  not punishment.
- Branch-protection rules from `36-Branching-Strategy` §8 are configured on day
  one.

### 6.4 WS-4 CI / CD (`28`)

- Phase 0 wires lint, mermaid, architecture, unit, integration.
- Phase 5 wires the build matrix (5 OS × architecture), nightly mutation nightly,
  and the SDK conformance suite runs in CI.
- Phase 7 adds nightly AI validation.
- Phase 9 adds signing, SBOM, SLSA provenance, dependency review.
- `28-CI-CD` §9 key custody is *real* before Phase 9 (`15-Security` §9).

### 6.5 WS-5 Release & deployment (`22`, `29`, `36`)

- Cadence: alpha → beta → RC → stable per `22-Release-Plan` §5.
- Channels per `29-Deployment` §4 are configured by Phase 5 (so the alpha can be
  installed by invited testers).
- Feature flags per `29-Deployment` §8 wrap every capability that touches user
  state in a minor — never silently break a user's existing behaviour.

### 6.6 WS-6 Security (`15`, `38`, `12`, `33`)

- Encryption baseline (ADR-0013) is implemented in Phase 1.
- Threat-model updates are *part* of every architectural PR that widens the
  attack surface (`38-Threat-Model` §11).
- Plugin isolation: in-process `AssemblyLoadContext` in Phase 5, document out-of-process as a future hardening per `38-Threat-Model` TM-D-2.
- CVE / coordinated disclosure SLA = 90 days (`54-Maintenance` §6.2).

### 6.7 WS-7 AI subsystem (`13`, `32`, `31`)

- Model registry is *the* contract (`13-AI-Architecture` §6).
- Default models ship bundled; user can swap via plugin (`13-AI-Architecture` §10).
- Architecture test: no `IAIProvider` may be implemented with a paid third-party
  call (`13-AI-Architecture` §13, `12-Plugin-Architecture` §13).

### 6.8 WS-8 Observability (`30`, `22`, `29`)

- Structured logging on hot paths from Phase 1.
- SLO metrics in `30-Observability` §5.3 are present by Phase 2.
- Telemetry defaults to OFF; opt-in flow is exercised in Phase 1 and tightened
  through every phase.
- DIB (`30-Observability` §9) ships in Phase 1 as a stub and matures in Phase 9.

### 6.9 WS-9 Test engineering (`18`, `19`, `43`–`51`)

- Test pyramid: component → integration → system → acceptance → non-functional →
  architecture → plugin → sync → AI (`18-Test-Strategy` §4).
- Coverage ≥ 90 % on core packages; mutation ≥ 70 % on the domain layer
  (`16-Quality-Attributes` §4 QG-02, QG-03).
- Traceability matrix is *generated* by CI on every change (`19-Traceability-Matrix`
  §13).

### 6.10 WS-10 Performance (`39`, `16`, `29`)

- Frame budget (`39-Performance` §5) is wired from the first UI PR.
- Cold/warm-start budgets (`39-Performance` §6) are CI-enforced from Phase 1.
- Memory budgets (`39-Performance` §7) are CI-monitored from Phase 1 and enforced
  from Phase 9.
- The `pine perf bench` slash command is added at Phase 6 (`39-Performance` §12.3).

### 6.11 WS-11 Accessibility (`40`, `08`, `42`)

- axe-equivalent on every UI PR from Phase 1.
- Manual audit per platform per minor release (from `40-Accessibility` §10.2).
- Annual external auditor cadence begins at v1.0 (`40-Accessibility` §10).
- `40-Accessibility` §10.3 user testing with AT users starts no later than Phase 8.

### 6.12 WS-12 Internationalisation (`41`, `03`, `42`)

- All strings externalised from Phase 1 (the *rule*, not the locale — locale work
  ramps in Phase 8).
- ICU MessageFormat in use from Phase 1.
- Pseudo-locale build from Phase 1.
- Locale workflows at `41-Localization` §8 — locale leads — are seeded by
  Phase 6 so translation work begins before Phase 8's wall-clock work.

### 6.13 WS-13 UX / design system (`42`, `40`, `41`)

- Design tokens (`42-UX-Guidelines` §6) and the IA (`42-UX-Guidelines` §7)
  defined in Phase 1.
- Component library grows capability-by-capability.
- The in-product *What's new* surface is a Phase 6 deliverable (`42-UX-Guidelines`
  §12) but the "empty state" pattern is in place from Phase 1.

### 6.14 WS-14 Sync & conflict (`14`, `11`, `18`)

- The **field-class conflict matrix** (`14-Synchronization` §7) is the contract.
  Each new field added to a bounded context in Phase 1+ is added with an explicit
  row in §7.
- Sync conformance test is in CI from Phase 5.
- Cloud-Only encrypted-local-cache (`14-Synchronization` §10) ships in Phase 5.

### 6.15 WS-15 Plugin ecosystem (`12`, `33`, `24`)

- Permissive trust model (signed manifests + user opt-in permission grants) is
  in `12-Plugin-Architecture` §8; implemented in Phase 5.
- Marketplace allow-list + risk-score published once the marketplace is live
  (Phase 5).
- Sample plugins ship with the beta to seed the ecosystem (Phase 5).

### 6.16 WS-16 Community & contribution (`24`, `21`)

- DCO is in place from Phase 0 (`CONTRIBUTING.md`).
- CoC, CoI, sponsorship acceptance criteria deferred (`24-Contributing` §9–§10)
  — Phase 0 publishes placeholder pages; they get a real text by the time the
  first community beta (Phase 2) is shipped.
- A Maintainer Council is seeded in Phase 0 (`24-Contributing` §6); the first
  election is at v1.0.

### 6.17 WS-17 Maintenance & incident response (`54`, `17`, `52`)

- Runbooks exist for the hot paths by Phase 9.
- Post-mortem template (`54-Maintenance`) used after every P1 incident from v1.0.
- A monthly risk summary (`52-Risk-Matrix` §10) is published from v1.0.

### 6.18 WS-18 Telemetry & analytics opt-in (`30`, `01`)

- Opt-in flow implemented in Phase 1 (`15-Security` §5 + `30-Observability` §10).
- Granular toggles (errors vs metrics vs AI signals) refined in Phase 7.
- Default-OFF always. The CI tests **never** ship code that flips the default.

---

## 7. Doc-Trace Matrix — every SSoT doc → the phases & workstreams that own it

> Single source for the question: *"Is doc `XX` covered by the plan?"*

| SSoT doc                       | Owning phase(s) | Owning workstream(s) | Verified by |
|-------------------------------|------------------|-----------------------|-------------|
| `00-Vision`                   | All             | WS-1 (immutable)     | CI grep of principles; `19-Traceability-Matrix` §5 |
| `01-Business-Requirements`     | All             | WS-1, WS-8, WS-18    | Refusal list review; telemetry-policy CI test |
| `02-Product-Scope`             | All             | WS-2                  | All `SCOPE-*` shipped by v1.0; covered in each phase |
| `03-Glossary`                  | Phase 0 onwards | WS-2                  | Glossary hygiene linter; doc-tooling lint |
| `04-Stakeholders`              | Phase 0, refreshed quarterly | WS-1, WS-16 | Public stakeholder reference; RACI matrix |
| `05-Personas`                  | Phase 0 + Phase 8 (international) | WS-13        | Persona coverage of FRs per `05` §7 matrix |
| `06-Use-Cases`                  | Phase 1 onwards | WS-2, WS-9            | Every UC maps to ≥ 1 FR + ≥ 1 AC + ≥ 1 TC |
| `07-Functional-Requirements`    | Capability phases 1–8 | All           | `19-Traceability-Matrix` §8; every FR has AC+TC |
| `08-NonFunctional-Requirements` | Phase 1 onwards | WS-10, WS-11, WS-14   | All NFR-* measurable in CI (cold start, perf, a11y) |
| `09-System-Architecture`       | Phase 0 onwards | WS-3, WS-4, all       | Architecture tests (`NetArchTest`); ADR-driven decisions |
| `10-Domain-Model`               | Phase 0 onwards | All                   | Every aggregate has invariant tests in `Pine.Tests.Unit` |
| `11-Data-Model`                 | Phase 1 onwards | All                   | Migrations versioned; sync envelope v1 in Phase 5 |
| `12-Plugin-Architecture`        | Phase 5         | WS-15, WS-6            | Plugin conformance suite; manifest signing |
| `13-AI-Architecture`            | Phase 2, 5, 7   | WS-7, WS-6            | Architecture test: no paid third-party AI path |
| `14-Synchronization`            | Phase 5         | WS-14                  | Field-class matrix test; conflict suite in CI |
| `15-Security`                   | Phase 0 onwards | WS-6, WS-1            | Secure-defaults tests; threat-model update |
| `16-Quality-Attributes`        | Phase 9 (gates enforced) | WS-3          | The 15 QG-* gates in CI |
| `17-Risk-Analysis`              | Phase 1 (initial) + ongoing | WS-17        | 5×5 risk register; monthly review |
| `18-Test-Strategy`             | Phase 0 onwards | WS-9                  | Levels, types, tooling all in CI |
| `19-Traceability-Matrix`        | Phase 0 onwards | WS-9                  | CI job regenerates matrix; orphans/pendulums fail the build |
| `20-Coding-Standards`          | Phase 0 onwards | WS-3                  | Lint + style + format gate |
| `21-Definition-of-Done`         | Phase 0 onwards | WS-3                  | PR template checklist |
| `22-Release-Plan`               | All             | WS-5                  | Cadence + sign-off |
| `23-Roadmap`                    | All             | WS-5                  | Theme windows match capability phases |
| `24-Contributing`               | Phase 0 + post-GA | WS-16                | DCO live; CoC drafted by v1.0 |
| `25-ADR`                        | Phase 0 onwards | WS-2                  | `INDEX.md` regenerated by CI; 20 seeded ADRs at Phase 0 |
| `26-Diagrams`                   | Phase 0 onwards | WS-2                  | Mermaid parse gate in CI |
| `27-Development-Guidelines`     | Phase 0 onwards | WS-3                  | Monorepo + `pine` CLI + environment parity |
| `28-CI-CD`                      | Phase 0 onwards | WS-4                  | Pipeline stages, gates |
| `29-Deployment`                  | Phase 5 onwards | WS-5, WS-8            | Channels, feature flags, rollback |
| `30-Observability`              | Phase 1 onwards | WS-8                  | Logs, metrics, traces; DIB; SLO metrics |
| `31-Rule-Engine`                | Phase 3         | WS-7                  | Rule grammar, packs, audit, conflict resolution |
| `32-AI-Training`                | Phase 2 onwards | WS-7                  | Reproducibility manifest; evaluation set |
| `33-Plugin-SDK`                 | Phase 5         | WS-15, WS-4           | SDK API + samples + SemVer; conformance suite |
| `34-API-Guidelines`             | All             | WS-3                  | Backwards-compat snapshot; deprecation cycle |
| `35-Git-Workflow`               | Phase 0 onwards | WS-3                  | Conventional commits, DCO, PR template |
| `36-Branching-Strategy`          | Phase 0 onwards | WS-3                  | Trunk-based; release branches |
| `37-Code-Review`                | Phase 0 onwards | WS-3                  | Review SLAs; three-axis checklist |
| `38-Threat-Model`               | Phase 0 (draft) → Phase 9 (published) | WS-6 | STRIDE + LINDDUN enumeration per subsystem |
| `39-Performance`                | Phase 1 onwards | WS-10                 | Budgets in CI; nightly bench |
| `40-Accessibility`              | Phase 1 onwards (hardening Phase 8) | WS-11 | Axe gate; manual audit per release |
| `41-Localization`               | Phase 1 (engine) → Phase 8 (locales) | WS-12 | String extraction; pseudo-locale; ICU |
| `42-UX-Guidelines`              | Phase 1 onwards | WS-13                 | Design tokens; IA |
| `43-Testing-Artifacts`          | Phase 0 onwards | WS-9                  | Test pyramid + `pine test *` CLI |
| `44-Backlog`                    | Phase 0 onwards | WS-3, WS-17           | Format, slicing, FIB; `pine backlog list` |
| `45-Epics`                      | Phase 0 onwards | WS-3                  | Epic lifecycle & capacity rules |
| `46-Features`                   | Phase 0 onwards | WS-3                  | Feature flags per `29-Deployment` §8 |
| `47-User-Stories`               | Phase 0 onwards | WS-3, WS-9           | INVEST quality bar; trace to FR/AC/TC |
| `48-Acceptance-Criteria`         | Phase 0 onwards | WS-9                  | ≥ 1 positive/negative/edge AC per story |
| `49-Test-Cases`                 | Phase 0 onwards | WS-9                  | Naming, isolation, deterministic; tags |
| `50-Test-Scenarios`             | Phase 0 onwards | WS-9                  | Cohesive path; linked TCs |
| `51-Test-Data`                  | Phase 0 onwards | WS-9, WS-12          | PII-free, reproducible, versioned |
| `52-Risk-Matrix`                | Phase 1 (initial) → ongoing | WS-17 | Heat map; cadence: monthly + per release |
| `53-Release-Checklist`           | Phase 5 onwards (formal) | WS-5 | Pre-release gates; sign-off |
| `54-Maintenance`                | Post-v1.0       | WS-17                 | Support matrix, hotfix SLAs, EoL |
| `55-Future-Vision`              | Updated on every major | WS-1                | Updated at v1.0, v2.0, v3.0 |
| `_mermaid_validator/` tooling  | Phase 0         | WS-2                  | CI Mermaid parse gate |

> If a row above has no "Verified by", it's because the doc is operational/structural
> (e.g. `24-Contributing`, `53-Release-Checklist`) — the verification is the existence
> and currency of the artefact it prescribes.

---

## 8. Open Decisions and how this plan addresses them

`00-Vision` §14 / `25-ADR` lists open decisions. The plan binds each to a phase where it
is *closed* (or, where post-v1.0 is the right place, explicitly deferred with an ADR).

| OD-*  | Topic                                                       | Closed in / by phase / ADR                                          |
|-------|-------------------------------------------------------------|----------------------------------------------------------------------|
| OD-01 | Plugin trust model baseline                                | Phase 0 scaffolding; **closed** in `12-Plugin-Architecture` / ADR-0005 / ADR-0014 |
| OD-02 | Sync conflict resolution strategy                          | **closed** in `14-Synchronization` / ADR-0015 — executed by Phase 5 |
| OD-03 | Encryption at rest standard                                | **closed** in `15-Security` §4 / ADR-0013 — executed by Phase 1       |
| OD-04 | Exchange-rate provider (plugin-only)                       | Phase 4 — closed by `12-Plugin-Architecture` §4 (Investment provider category) plus an FX plugin |
| OD-05 | MVP / no MVP                                                | **closed** in `02-Product-Scope` §6 / ADR-0017 — no MVP                  |
| OD-06 | Local AI model packaging / on-device update                | **partially closed** in `13-AI-Architecture` §4 — rest closed in `32-AI-Training` by Phase 5 |
| OD-07 | Governance model                                           | Phase 0 (Maintainer Council seeded); rules settle by v1.0 |
| OD-08 | Tech baselines (.NET, Avalonia, OS)                        | **closed** in `09-System-Architecture` §4 / ADR-0003 — pin in Phase 0; revisited at v2.0 |
| OD-09 | Trademark policy                                           | Placeholder in Phase 0; text by v1.0 GA (`24-Contributing`)        |
| OD-10 | Telemetry policy                                            | **closed** in `15-Security` §5 / ADR-0012 — wired by Phase 1       |
| OD-11 | Monorepo topology                                           | Phase 0 — fixed in `27-Development-Guidelines` §5                    |
| OD-D-3 | Pine Cloud (deferred)                                      | **post-v1.0** — see `55-Future-Vision` 3–5 yr horizon                |
| OD-D-7 | OTA-only build for embedded partners                       | **post-v1.0** — `29-Deployment` §13 (DEP-D-5 deferred)               |
| DG-D-4 | Mutation testing tool choice                              | Phase 0 — pre-pick Stryker.NET (matches Phase 9 nightly)             |
| RMX-D-1 | Public risk register                                      | post-v1.0 (initial internal-only)                                    |
| TR-D-1 | Traceability matrix tool                                    | Phase 0 CI job (custom)                                             |

---

## 9. Risks and their relationship to the plan

`17-Risk-Analysis` §5 lists strategic risks. The plan treats the high-impact ones as
either mitigations inside a workstream or as release-blockers in a specific phase.

| Risk-ID (per `17`) | What it asks                                                          | Where the plan mitigates                                                                                  |
|--------------------|------------------------------------------------------------------------|-------------------------------------------------------------------------------------------------------------|
| **R-01 (20)**      | Team too small                                                          | WS-16: pair maintenance + bus factor rotation; double-review rule for `00-`/`01-`/`02-`; succession plan in `54-Maintenance` |
| **R-02 (10)**      | Paid AI drift                                                          | Architecture test in Phase 2 forbids paid AI imports; plugin trust model reinforces at Phase 5; AI Validation suite in Phase 7 |
| **R-03 (12)**      | Sync provider breaks                                                 | Phase 5 wraps sync as plugin contract; user can swap at any time; provider health metric in `30-Observability` |
| **R-04 (12)**      | On-device model unsupported                                            | Model registry is pluggable; AI Validation tests run nightly on user data                    |
| **R-05 (10)**      | Malicious fork                                                         | Trademark (deferred OD-09) + marketplace allow-list at Phase 5 + public "official vs fork" page post-v1.0 |
| **R-06 (12)**      | App Store rule conflict                                                | Platform compliance checklist at Phase 9 (`29-Deployment` §4)                                    |
| **R-07 (15)**      | Critical security vulnerability close to a release                  | Hotfix branch path from `36-Branching-Strategy` §7 (≤ 24 h fix window per `54-Maintenance` §6.1); `53-Release-Checklist` §4.5 |
| **R-08 (9)**       | CoC violation                                                          | CoC draft by Phase 2 beta launch (per `24-Contributing` §10); conflict-of-interest by Phase 9 |
| **R-09 (15)**      | Donation model insufficient                                              | Annual finance report cadence from v1.0 (`24-Contributing` §8.3); OkRs in `01-Business-Requirements` §5.4 |
| **R-10 (12)**      | No plugin ecosystem                                                    | SDK at Phase 5 is the *mitigation*; sample plugins ship with the beta |
| **R-11 (12)**      | Platform vendor policy change                                         | Pine's update manifest is signed per platform; rollback per `29-Deployment` §7 |
| **R-12 (9)**       | OS baseline drift                                                       | OD-08 closure pins `.NET LTS` + Avalonia + OS baselines in `09-System-Architecture` §4       |
| **R-13 (12)**      | Dependency compromise                                                  | Dependabot; SBOM; reproducibility manifest; CodeQL (`28-CI-CD` §7.6)                              |
| **R-14 (12)**      | Knowledge concentration                                                | ADRs mandatory; pair-maintenance rule; "good first issue" labelling (`21-DoD` + `44-Backlog`) |
| **R-15 (8)**       | Commercial clone name                                                  | Trademark placeholder + market allow-list                                                |
| **R-16 (10)**      | User data lost due to corruption                                        | Crash-safe transactions (SQLite WAL); encryption-at-rest integrity check; DR drill in `54-Maintenance` §6 |
| **R-17 (9)**       | Model regression                                                       | Phase 2 SLO `pine.ai.inference.duration_ms`; Phase 7 AI validation suite; user-visible confidence |
| **R-18 (10)**      | Sync conflict bug silently drops change                                | `14-Synchronization` §7 field-class matrix + Phase 5 conflict suite in CI                                              |
| **R-19 (5)**       | GPL dispute                                                             | Counsel network + CLA-not-DCO posture (`24-Contributing` §5)                                          |
| **R-20 (4)**       | Government data request                                                 | No central store to seize; DIB redaction discipline (`30-Observability` §9.4)                     |

---

## 10. Quality gates, applied to every phase

The 15 gates of `16-Quality-Attributes` §4 are not aspirational — they are CI-enforced.
Below is the *minimum* per-phase coverage; stricter gates activate as the risk
increases.

| Gate ID    | Becomes a *must* in CI from        | What CI does                                                       |
|------------|-----------------------------------|--------------------------------------------------------------------|
| QG-01 (no warnings, wae) | Phase 0            | `dotnet build /warnaserror`                                       |
| QG-02 (≥ 90 % cov core)   | Phase 1            | Coverlet; core-packages only (gated)                            |
| QG-03 (≥ 70 % mutation)    | Phase 9 nightly    | Stryker.NET nightly on `main`                                  |
| QG-04 (arch tests)        | Phase 0            | `NetArchTest` in `Pine.Tests.Unit`                              |
| QG-05 (a11y 0 critical)    | Phase 1 (axe-equivalent on UI PRs), Phase 8 (WCAG 2.2 AA) | axe-equivalent + manual audit records in `40-Accessibility` §10 |
| QG-06 (no high vuln)      | Phase 1 (warn-only), Phase 9 (blocker) | Dependabot + CodeQL + Semgrep |
| QG-07 (perf budgets)      | Phase 1 (cold-start), Phase 2 (AI inference), Phase 5 (sync) | BenchmarkDotNet + k6 nightly |
| QG-08 (FR → AC → TC)      | Phase 1 onwards    | `pine test trace` in CI; orphan/pendulum fail the build        |
| QG-09 (100 % externalised strings) | Phase 1 onwards | String-extract job; per-locale resource check |
| QG-10 (Conventional Commits)        | Phase 0            | Commit-lint action                                              |
| QG-11 (SemVer tagging)              | Phase 0            | Tag audit CI step                                               |
| QG-12 (changelog current)          | Phase 1 onwards    | Per-PR changelog reminder; release notes draft on stable         |
| QG-13 (threat model current)       | Phase 9 published  | Threat-model PR template                                       |
| QG-14 (public ADRs index)          | Phase 0            | ADR index regenerated on every change                          |
| QG-15 (SonarQube gate)             | Phase 9            | SonarQube scan on `main` (if server available)                |

A **gate** is the *only* way to exit a phase for a given capability area. If a gate
fails, the phase does not close; back-merge in a hotfix is required (per
`54-Maintenance` §6.2).

---

## 11. Tooling decisions to take *now* (during Phase 0)

`18-Test-Strategy` §11 lists defaults. Phase 0 *commits* to the following, and they are
not revisited without an ADR:

| Concern              | Tool (default)                                                                                                    |
|----------------------|--------------------------------------------------------------------------------------------------------------------|
| Unit testing         | **xUnit** + **FsCheck** + **NSubstitute**                                                                              |
| Mutation             | **Stryker.NET**                                                                                                       |
| Architecture         | **NetArchTest**                                                                                                       |
| Integration          | xUnit + **Testcontainers**                                                                                            |
| System (UI)          | xUnit + Avalonia headless + Appium (mobile)                                                                            |
| Acceptance (BDD)     | **SpecFlow** (or equivalent; locked in Phase 0)                                                                       |
| Performance          | **k6** for HTTP/sync paths; **BenchmarkDotNet** for in-process hot paths                                              |
| Security             | **OWASP ZAP**, **CodeQL**, **Semgrep**                                                                                  |
| Accessibility        | axe-equivalent + manual scripts                                                                                       |
| i18n                 | ICU MessageFormat + snapshot tests                                                                                   |
| AI validation        | Custom harness + labelled synthetic datasets (`51-Test-Data`)                                                         |
| Sync                 | Custom multi-device harness                                                                                            |
| Plugin               | SDK conformance suite (`33-Plugin-SDK` §10)                                                                            |
| Coverage             | **Coverlet**                                                                                                            |
| Reporting            | **Allure** + CI artefacts                                                                                              |
| Docs site            | **Astro** (per `28-CI-CD` §6) + Mermaid CLI                                                                           |
| CI host              | **GitHub Actions** (interchangeable runners per `28-CI-CD` §17)                                                          |
| Container            | Only for server-side optional components (`28-CI-CD` §8.1)                                                              |
| Coordination         | Slack channels per `04-Stakeholders` §8; meetings per `24-Contributing` §6                                            |

> **Snyk/CycloneDX/SLSA** are picked in Phase 9 when the release-blockers kick in
> (`28-CI-CD` §7.6).

---

## 12. Communications and release cadence

Source of truth: `22-Release-Plan` §9 and `53-Release-Checklist` §8. The plan mirrors
those and adds:

- **Internal weekly**: Maintainer Council. Inputs: `44-Backlog` summary,
  `52-Risk-Matrix` status, SLO top-N, open ADR count.
- **Internal bi-weekly**: Engineering Review Board. Inputs: `17-Risk-Analysis`
  deltas, perf regressions, ADR proposals, release timing.
- **Public per release**: changelog + release notes + security advisory (if
  applicable) per `22-Release-Plan` §9.
- **Public monthly**: SLO top-N overview (post-v1.0).
- **Public quarterly**: risk summary (post-v1.0) per `52-Risk-Matrix` §10.
- **Public annual**: independent security audit (per `01-BR` KR-02-3) +
  accessibility audit (per `40-Accessibility` §11) + governance / finance report
  (per `24-Contributing` §8.3).

---

## 13. Annex A — Document-to-Phase lookup (quick reference)

If you only have time for one screen, this is the one to read first. Every other doc
is the source of truth for the listed topic; this plan only sequences the work.

- **Vision / scope / glossary**: never stale. CI guard (`pine dev validate-ssot`).
- **Architecture / domain / data**: foundation (Phase 0).
- **Plugin / sync / AI**: Phase 5 (plugin + sync), Phase 2 (AI OCR/cat),
  Phase 7 (assistant).
- **Security / threat model / observability**: wired from Phase 1; published at
  Phase 9.
- **Quality / test / DoD / coding standards**: every PR, every phase.
- **Process / CI / release / deployment**: Phase 0 (process), Phase 5 (deployment
  channels), Phase 9 (signing).
- **Performance / accessibility / localisation**: Phase 1 (engine),
  Phase 6 (perf hard target), Phase 8 (a11y + i18n depth).
- **Risk / release checklist / maintenance**: monthly + per release + post-v1.0.
- **Agile artefacts (epics → features → stories → AC → tests → data)**: every phase;
  `44-Backlog` is the live source.

---

## 14. Annex B — Anti-patterns the plan refuses

These are the same anti-patterns `16-Quality-Attributes` §10 enumerates. The plan
treats each as an explicit *release-blocker* if it sneaks in.

- Skipping a quality gate and promising to "fix later".
- Treating code coverage as a goal (it is a side-effect of testing).
- Writing tests that exist only to flip a coverage line.
- Disabling architecture tests to make a feature "fit".
- Shipping a behaviour change without a changelog entry or ADR.
- Treating migration as an afterthought (every schema change is a versioned migration).
- Defaulting telemetry to ON.
- Plugging in a paid third-party AI service in any code path.
- Marking a phase "done" without `pine test trace` returning green.
- Treating `00-Vision` principles as slogans rather than CI-enforced invariants.
- Architecting around the rollout, instead of around the principles.

---

## 15. Revision History

| Version | Date       | Author                | Change                                                       |
|---------|------------|-----------------------|--------------------------------------------------------------|
| 0.1     | 2026-07-08 | Mavis (MiniMax), on assignment from the project team | Initial phased implementation plan. Consumes every `00-..55-…` SSoT doc and the `_mermaid_validator/` package; adds no invented content. |

---

*This plan is single-use now: it executes the vision, scope, architecture, quality
gates and release cadence already present in the SSoT. Treat it the way `22-Release-Plan`
treats the product: open to revision only through an ADR and a new revision row above.*
