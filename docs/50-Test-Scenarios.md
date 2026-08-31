# 50 — Test Scenarios

| Field            | Value                                                                                |
|------------------|--------------------------------------------------------------------------------------|
| Document ID      | `PINE-DOC-50-TEST-SCENARIOS`                                                          |
| Status           | Aspirational — the process is not in use                                             |
| Reconciled       | 2026-08-31 · The scenario artefact tree does not exist. Its `TD-NNNN` reference (:65) means Test Data, not technical debt. |
| Owner            | Engineering Review Board                                                             |
| Created          | 2026-07-08                                                                           |
| Last Updated     | 2026-07-08                                                                           |
| Specification Standard | ISTQB scenario-based testing practice                                     |
| Glossary         | `PINE-DOC-03-GLOSSARY`                                                               |
| Prerequisites    | `00-Vision`..`49-Test-Cases`                                                         |
| Related Documents| `18-Test-Strategy`, `43-Testing-Artifacts`, `49-Test-Cases`, `51-Test-Data` |

---

## 1. Purpose

Define Pine's test scenario (TS) practice: a *higher-level* test
grouping that exercises a *path* through the system, composed of
test cases. Scenarios are the *capability-level* tests that
exercise a flow end-to-end.

## 2. Scope

In scope: definition, format, types, quality bar, composition,
isolation, lifecycle, traceability, tooling.

Out of scope: test cases → `49-Test-Cases`; test data →
`51-Test-Data`; acceptance criteria → `48-Acceptance-Criteria`;
test strategy → `18-Test-Strategy`.

## 3. Definitions

| Term             | Working definition                                                                                |
|------------------|------------------------------------------------------------------------------------------------------|
| Test scenario    | A higher-level grouping of test cases that exercises a path or capability.                          |
| Path             | A sequence of steps a user (or actor) follows.                                                     |
| Capability       | A user-facing function (per `46-Features`).                                                         |
| Smoke scenario   | A minimal end-to-end path that runs in < 5 min.                                                    |
| Regression scenario | A scenario that runs in the full regression suite.                                              |
| Negative scenario | A scenario that exercises a failure path.                                                        |
| Persona-driven   | A scenario that exercises the path of a specific persona.                                          |
| Precondition     | The state required before the scenario runs.                                                       |
| Postcondition    | The state expected after the scenario runs.                                                        |

> **Assumption TS-A-1.** A scenario is a *narrative*; a test case is
> a *check*. Scenarios group checks into a story.

## 4. Test Scenario Format

```markdown
# TS-NNNN — Short Title

| Field            | Value                                                    |
|------------------|-------------------------------------------------------------|
| Capability       | FEAT-NN or capability name                                 |
| Path             | Description of the path covered                            |
| Type             | smoke | regression | negative | persona | exploratory       |
| Persona          | PERS-NN (primary)                                          |
| Test cases       | TC-XXXX, TC-XXXX, ...                                      |
| Preconditions    | Free-form                                                  |
| Steps            | 1. ... 2. ...                                              |
| Postconditions   | Free-form                                                  |
| Test data        | TD-NNNN (when applicable)                                  |
| Profile          | default | sandbox | hermetic                                |
| Owner            | Person                                                      |
| Status           | Active | Skipped | Deprecated                              |
```

## 5. Scenario Types

| Type            | Purpose                                                                            | Use case                                                 |
|-----------------|------------------------------------------------------------------------------------|-----------------------------------------------------------|
| Smoke           | Minimal end-to-end path.                                                            | Gate for every PR.                                        |
| Regression      | Full capability path.                                                              | Gate for the main branch.                                |
| Negative        | Failure paths.                                                                     | Triggered on change; gate for sensitive areas.            |
| Persona         | A persona's typical journey.                                                       | Manual and automated; release check.                      |
| Exploratory     | A guided exploration of a capability.                                              | Manual; not a gate.                                       |
| Migration       | A path that exercises data migration (between versions or formats).                 | Gate for migrations.                                      |

## 6. Quality Bar

A scenario is *good* when:

- It is **cohesive** (one path).
- It is **exercising** a real flow (not a single test).
- It is **linked** to test cases.
- It is **named** in line with its path.
- It is **traceable** to a capability.
- It is **stable** (the path does not drift without a story).

## 7. Composition

A scenario is composed of:

- A persona (or actor).
- A starting state.
- A sequence of steps.
- Expected postconditions.
- A list of test cases that exercise the path.
- A list of test data (when applicable).

### 7.1 Composition Heuristic

- A scenario uses the *primary* persona of the capability.
- A scenario uses a *fresh* profile by default.
- A scenario cites the test cases that exercise each step.
- A scenario cites the test data needed for non-trivial inputs.

## 8. Scenario Maps

A scenario map is a 2-axis visualisation:

- **Horizontal axis**: the user journey.
- **Vertical axis**: the scenarios per persona.

A capability's scenarios are organised on a scenario map before
the capability is *Ready*.

## 9. Status

A scenario is *Active* when:

- All its test cases are *Active*.
- The preconditions are documented.
- The postconditions are documented.

A scenario is *Skipped* when:

- The capability is not yet ready.
- The preconditions cannot be met in the test environment.

A scenario is *Deprecated* when:

- The capability is removed.
- The path is no longer relevant.

## 10. Traceability

- A scenario MUST trace to a capability (`46-Features`).
- A scenario MUST list the test cases that exercise it.
- A scenario MAY trace to a user story and an AC.

## 11. Tools

- The CLI `pine ts new` scaffolds a scenario.
- The CLI `pine ts run TS-NNNN` runs a scenario.
- The CLI `pine ts coverage TS-NNNN` prints the coverage report.

## 12. Cross-Cutting Constraints

- A scenario MUST be cohesive.
- A scenario MUST cite its test cases.
- A scenario MUST cite a capability.
- A scenario MUST NOT call a real network endpoint.

## 13. Deferred Decisions

| ID        | Decision                                                                                  | Owning document(s)         |
|-----------|-------------------------------------------------------------------------------------------|----------------------------|
| TS-D-1    | Whether to require automated scenarios in the main gate.                                  | `50-Test-Scenarios` (next rev.) |
| TS-D-2    | Whether to publish scenario status publicly.                                              | `50-Test-Scenarios` (next rev.) |
| TS-D-3    | Whether to ship a public scenario map.                                                     | `50-Test-Scenarios` (next rev.) |

## 14. Quality Checklist

- [x] Definition, format, types, quality bar, composition, maps,
      status, traceability, tools are all present.
- [x] ISTQB referenced.
- [x] No TODO placeholders.
- [x] No contradiction with `00-Vision`..`49-Test-Cases`.

## 15. Revision History

| Version | Date       | Author                | Change                                                       |
|---------|------------|-----------------------|--------------------------------------------------------------|
| 0.1     | 2026-07-08 | Engineering Review Board | Initial test scenarios document.                              |
