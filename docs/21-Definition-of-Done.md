# 21 — Definition of Done

| Field            | Value                                                                                |
|------------------|--------------------------------------------------------------------------------------|
| Document ID      | `PINE-DOC-21-DEFINITION-OF-DONE`                                                     |
| Status           | Aspirational — the definition is not applied by any gate                             |
| Reconciled       | 2026-08-31 · Its criteria are not checked by CI, by a PR template (none exists) or by any script. |
| Owner            | Engineering Review Board                                                             |
| Created          | 2026-07-08                                                                           |
| Last Updated     | 2026-07-08                                                                           |
| Specification Standard | ISTQB (DoD) + project convention                                                |
| Glossary         | `PINE-DOC-03-GLOSSARY`                                                               |
| Prerequisites    | `00-Vision`..`20-Coding-Standards`                                                   |
| Related Documents| `18-Test-Strategy`, `19-Traceability-Matrix`, `37-Code-Review`, `53-Release-Checklist` |

---

## 1. Purpose

Define, once, what *Done* means for any change that lands in Pine.
The DoD is the contract between "in progress" and "shipped". It is
the gate that protects the project from "it works on my machine".

## 2. Scope

In scope: every change type (code, documentation, model, AI model,
plugin SDK, design, infrastructure).

Out of scope: Definition of Ready → §11; release tagging →
`22-Release-Plan`; quality gates → `16-Quality-Attributes`.

## 3. Definitions

| Term           | Working definition                                                                                |
|----------------|------------------------------------------------------------------------------------------------------|
| DoD            | Definition of Done — the criteria a change must satisfy to be considered finished.                  |
| DoR            | Definition of Ready — the criteria a backlog item must satisfy before work starts.                   |
| PR             | Pull Request.                                                                                         |
| CI             | Continuous Integration.                                                                              |
| CoC            | Code of Conduct (deferred BR-D-5).                                                                   |

> **Assumption DD-A-1.** A change that does not meet the DoD is, by
> definition, *not* done, regardless of what the PR description says.

## 4. Universal DoD (every change)

A change is *done* when **all** of the following hold:

1. The change is in a merged PR with a Conventional Commits message.
2. The PR has at least one maintainer review (two for
   architectural changes; recorded as an ADR).
3. The CI pipeline is green for the change's branch.
4. No new warnings are introduced.
5. No TODO placeholders are introduced (or an explicit ADR waives
   them).
6. The change is consistent with the SSoT and with the upstream
   documents it references.
7. The change has a public changelog entry (if user-visible).
8. The change has a migration plan (if it changes the data model or
   the sync envelope).
9. The change has a security review (if it changes a security-
   relevant surface).
10. The change has a privacy review (if it changes a privacy-
    relevant surface).

## 5. Code DoD (additions and changes)

In addition to §4, a code change is *done* when **all** of the
following hold:

1. The change has unit tests for every new public type and every new
   public method.
2. The change has integration tests for every new bounded context
   interaction.
3. The change has architecture tests updated to cover the new
   structure (if the structure changed).
4. Coverage is ≥ 90 % on the affected package; the delta is not
   negative.
5. The mutation score on the affected domain code is ≥ 70 %.
6. Lint and StyleCop are green.
7. CodeQL is green.
8. Semgrep is green.
9. The change has a public API note (if it changes a public API).
10. The change has a deprecation note (if it deprecates a public
    API).
11. The change does not introduce a banned pattern (§17 of
    `20-Coding-Standards`).
12. The change does not increase the dependency surface without an
    ADR.

## 6. Documentation DoD

In addition to §4, a documentation change is *done* when **all** of
the following hold:

1. The change uses the document template of the target document.
2. The change is consistent with the upstream documents it
   references.
3. The change updates the revision history.
4. The change is reviewed by at least one maintainer.
5. The change has a Mermaid diagram if it would improve clarity.
6. The change does not contradict the Vision, the Business
   Requirements, the Product Scope or the Glossary.

## 7. AI Model DoD

In addition to §4, an AI model change is *done* when **all** of the
following hold:

1. The model is signed and packaged per `13-AI-Architecture` §4.
2. The model passes the AI validation suite.
3. The model has a documented licence and provenance.
4. The model has a documented set of evaluation metrics on the
   reference datasets.
5. The model has a documented failure mode analysis.
6. The model has a user-visible confidence score.
7. The model has a user-visible explanation.
8. The model is removable and replaceable.
9. The model is not a paid third-party API.

## 8. Plugin SDK DoD

In addition to §4, a Plugin SDK change is *done* when **all** of the
following hold:

1. The change has SemVer implications recorded in the changelog.
2. The change has a deprecation note if it removes a public surface.
3. The change has a migration guide for plugin authors if it is a
   breaking change.
4. The change has a conformance-suite update.
5. The change is reviewed by at least two maintainers.
6. The change is recorded as an ADR.

## 9. Infrastructure and Deployment DoD

In addition to §4, an infrastructure or deployment change is *done*
when **all** of the following hold:

1. The change is reproducible.
2. The change is signed.
3. The change has a rollback plan.
4. The change has a runbook entry.
5. The change is recorded as an ADR if it changes the public
   distribution surface.

## 10. Security and Privacy Change DoD

In addition to §4, a security or privacy change is *done* when
**all** of the following hold:

1. The threat model is updated.
2. A security review is performed.
3. A privacy review is performed.
4. The change is recorded as an ADR.
5. The change is communicated in the security advisory page (if
   user-visible).

## 11. Definition of Ready (DoR)

A backlog item is *ready* when **all** of the following hold:

1. It has at least one user story with a clear persona and a clear
   goal.
2. It has at least one acceptance criterion.
3. It is traced to at least one FR.
4. It is traced to at least one UC.
5. It has an estimate (relative size, in T-shirt sizes).
6. It has a known dependency list.
7. It has a known risk (or "low risk").
8. It is in the current release window or scheduled.

## 12. DoD / DoR Cross-Reference

| Concern              | DoD ref | DoR ref |
|----------------------|---------|---------|
| FR coverage          | §5.1    | §11.3   |
| UC coverage          | §5.2    | §11.4   |
| Acceptance criteria  | §5.1    | §11.2   |
| Test cases           | §5.1    | §11.4   |
| Architecture test    | §5.3    | —       |
| Security review      | §4.9, §10 | —     |
| Privacy review       | §4.10, §10 | —    |
| ADR                  | §4.6, §5.12, §8.6, §10.4 | — |
| Conventional Commits | §4.1    | —       |
| Review count         | §4.2    | —       |
| Public changelog     | §4.7    | —       |

## 13. DoD / DoR Anti-Patterns

- Marking a change as "done" with a "fix later" follow-up without
  an ADR.
- Skipping a security review because the change "looks safe".
- Skipping an architecture test update because the change "is small".
- Adding a TODO placeholder "to track" something.
- Closing a PR without addressing the review comments.

## 14. Deferred Decisions

| ID     | Decision                                                                                  | Owning document(s)         |
|--------|-------------------------------------------------------------------------------------------|----------------------------|
| DD-D-1 | CoC text and reporting process (related to "respectful review" expectations).              | `24-Contributing` (BR-D-5) |
| DD-D-2 | Formal security review SLA per change type.                                                | `54-Maintenance`           |

## 15. Quality Checklist

- [x] DoD is defined for every change type.
- [x] DoR is defined.
- [x] Cross-reference to the quality gates and the traceability
      matrix is present.
- [x] Anti-patterns are explicit.
- [x] No TODO placeholders.
- [x] No contradiction with `00-Vision`..`20-Coding-Standards`.

## 16. Revision History

| Version | Date       | Author                | Change                                                       |
|---------|------------|-----------------------|--------------------------------------------------------------|
| 0.1     | 2026-07-08 | Engineering Review Board | Initial DoD.                                                |
