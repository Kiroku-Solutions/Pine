# 47 — User Stories

| Field            | Value                                                                                |
|------------------|--------------------------------------------------------------------------------------|
| Document ID      | `PINE-DOC-47-USER-STORIES`                                                           |
| Status           | Draft — pending review                                                               |
| Owner            | Engineering Review Board                                                             |
| Created          | 2026-07-08                                                                           |
| Last Updated     | 2026-07-08                                                                           |
| Specification Standard | IEEE 29148 / ISTQB (story practice)                                          |
| Glossary         | `PINE-DOC-03-GLOSSARY`                                                               |
| Prerequisites    | `00-Vision`..`46-Features`                                                           |
| Related Documents| `04-Stakeholders`, `05-Personas`, `43-Testing-Artifacts`, `44-Backlog`, `45-Epics`, `46-Features`, `48-Acceptance-Criteria` |

---

## 1. Purpose

Define Pine's user story practice: the *story* unit, the format,
the *INVEST* quality bar, the slicing heuristic, the relationship
to personas, features, and acceptance criteria, and the discipline
of writing good stories.

## 2. Scope

In scope: definition, format, INVEST, slicing, persona alignment,
acceptance alignment, ownership, tooling.

Out of scope: features → `46-Features`; epics → `45-Epics`;
acceptance criteria → `48-Acceptance-Criteria`; personas →
`05-Personas`; backlog → `44-Backlog`.

## 3. Definitions

| Term             | Working definition                                                                                |
|------------------|------------------------------------------------------------------------------------------------------|
| User story       | A short, persona-led statement of value, with acceptance criteria.                                 |
| Persona          | A representative user (per `05-Personas`).                                                          |
| INVEST           | A heuristic for story quality (Independent, Negotiable, Valuable, Estimable, Small, Testable).      |
| Story map        | A visualisation of stories by backbone, walking skeleton, and supporting stories.                  |
| Walking skeleton  | A thin end-to-end slice that crosses the system.                                                    |
| Spike            | A time-boxed research item (not a user story; lives in the backlog per `44-Backlog` §3).           |

> **Assumption US-A-1.** A user story is a *promise of
> conversation*, not a contract. The acceptance criteria and the
> tests are the contract.

## 4. User Story Format

```markdown
# US-NNNN — Short Title

| Field        | Value                                                           |
|--------------|------------------------------------------------------------------|
| As a         | PERS-NN (Persona)                                                |
| I want       | \<capability\>                                                  |
| So that     | \<outcome\>                                                     |
| Feature      | FEAT-NN                                                         |
| Epic         | EPIC-NN                                                         |
| FR           | FR-XXX                                                         |
| NFR          | NFR-XXX                                                        |
| Priority     | P0 / P1 / P2 / P3                                              |
| Story points | 1 / 2 / 3 / 5 / 8 / 13                                          |
| Status       | Draft / Ready / In Progress / Done / Wontfix                   |
| Owner        | Person or team                                                  |
| Acceptance   | AC-XXXX (≥ 1)                                                   |
| Test cases   | TC-XXXX (≥ 1)                                                   |
| Notes        | Free-form                                                       |
```

### 4.1 Prose Form

The user-facing prose is:

> As a `<persona>`, I want `<capability>`, so that `<outcome>`.

The "so that" is *not optional*: it explains the value.

## 5. INVEST

A user story is *INVEST*:

| Letter | Quality     | How to verify                                                                  |
|--------|--------------|---------------------------------------------------------------------------------|
| I      | Independent  | The story can ship without another story.                                       |
| N      | Negotiable   | The team can refine the implementation.                                         |
| V      | Valuable     | The story delivers user value.                                                  |
| E      | Estimable    | The team can size it.                                                           |
| S      | Small        | The story fits in a cycle.                                                      |
| T      | Testable     | The story has acceptance criteria.                                              |

A story that fails INVEST is *re-scoped* or *split*.

## 6. Slicing

### 6.1 Heuristic

A story is split by:

- Persona (different personas, different paths).
- Capability (thin slice first).
- Edge case (handle a narrow case first).
- Platform (one platform first).
- State (one state at a time).

The chosen slice MUST be valuable on its own.

### 6.2 Anti-Patterns

- A "compound" story that bundles capabilities.
- A "task" story (e.g. "refactor the database") without user
  value.
- A "spike" labelled as a story.
- A story that requires another story to ship.

## 7. Persona Alignment

A story MUST be tagged with a persona. The persona is from
`05-Personas`. The persona is *primary* (the user whose value is
the story's reason to exist).

## 8. Acceptance Criteria

A story MUST have ≥ 1 acceptance criterion. The criteria follow
the *Given/When/Then* model and live in `48-Acceptance-Criteria`.

### 8.1 Coverage

- An AC covers one behaviour.
- An AC is binary.
- An AC has a single Then clause per Given/When.

## 9. Story Maps

A story map is a 2-axis visualisation:

- **Horizontal axis**: the *backbone* of the user journey.
- **Vertical axis**: the *layers* — walking skeleton at the top,
  supporting stories below.

A feature's stories are organised on a story map before the
feature is *Ready*.

## 10. Priority and Estimation

- Priority is set by the sponsor / owner; the criteria are in
  `44-Backlog` §6.
- Story points are Fibonacci.
- A story sized > 13 MUST be split.

## 11. Status

- A story is *Done* when:
  - The code is merged.
  - The acceptance criteria pass.
  - The test cases pass.
  - The documentation is updated.
  - The changelog is updated.
  - The feature flag (if any) is enabled for the user or staged
    rollout cohort.

A story is *Wontfix* when the value is judged insufficient; the
reason is recorded.

## 12. Tools

- The CLI `pine story new` scaffolds a story.
- The CLI `pine story ready US-NNNN` checks INVEST.
- The CLI `pine story status US-NNNN` prints the status.

## 13. Cross-Cutting Constraints

- A user story MUST have a persona.
- A user story MUST have ≥ 1 AC.
- A user story MUST have a priority and a size.
- A user story MUST be independent of other stories in the same
  feature.
- A user story MUST be testable.

## 14. Deferred Decisions

| ID        | Decision                                                                                  | Owning document(s)         |
|-----------|-------------------------------------------------------------------------------------------|----------------------------|
| US-D-1    | Whether to mandate a specific story-map tool.                                              | `47-User-Stories` (next rev.) |
| US-D-2    | Whether to use a non-Fibonacci scale (per BL-D-1).                                          | `44-Backlog`               |
| US-D-3    | Whether to publish a public story list.                                                     | `47-User-Stories` (next rev.) |

## 15. Quality Checklist

- [x] Definition, format, INVEST, slicing, persona, AC, story
      map, priority, status, tools are all present.
- [x] IEEE 29148 / ISTQB referenced.
- [x] No TODO placeholders.
- [x] No contradiction with `00-Vision`..`46-Features`.

## 16. Revision History

| Version | Date       | Author                | Change                                                       |
|---------|------------|-----------------------|--------------------------------------------------------------|
| 0.1     | 2026-07-08 | Engineering Review Board | Initial user stories document.                                |
