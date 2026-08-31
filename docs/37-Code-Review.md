# 37 — Code Review

| Field            | Value                                                                                |
|------------------|--------------------------------------------------------------------------------------|
| Document ID      | `PINE-DOC-37-CODE-REVIEW`                                                            |
| Status           | Aspirational — no review process operates                                            |
| Reconciled       | 2026-08-31 · No pull requests, no `CODEOWNERS`, no review template. Commits reach branches directly. |
| Owner            | Engineering Review Board                                                             |
| Created          | 2026-07-08                                                                           |
| Last Updated     | 2026-07-08                                                                           |
| Specification Standard | Internal (review practice)                                                     |
| Glossary         | `PINE-DOC-03-GLOSSARY`                                                               |
| Prerequisites    | `00-Vision`..`36-Branching-Strategy`                                                |
| Related Documents| `20-Coding-Standards`, `21-Definition-of-Done`, `25-ADR`, `35-Git-Workflow`, `36-Branching-Strategy` |

---

## 1. Purpose

Define Pine's code review practice: roles, taxonomy, checklists,
SLAs, anti-patterns, and what to *not* do in review. Review is the
*teacher* and the *gatekeeper* of quality.

## 2. Scope

In scope: roles, taxonomy, checklists, SLAs, anti-patterns,
security review, documentation review, ADR-driven review.

Out of scope: CI/CD gates → `28-CI-CD`; coding standards →
`20-Coding-Standards`; definition of done → `21-Definition-of-
Done`; branching → `36-Branching-Strategy`.

## 3. Definitions

| Term             | Working definition                                                                                |
|------------------|------------------------------------------------------------------------------------------------------|
| Author           | The person who opened the PR.                                                                      |
| Reviewer         | A person who reviews and approves the PR.                                                          |
| Approver         | A reviewer with merge rights.                                                                      |
| Reviewer         | Code or document reviewer.                                                                          |
| Self-review      | A review performed by the author; not allowed at Pine.                                              |
| Security review  | A specialised review performed by a trust-team member.                                              |
| Docs review      | A review that focuses on documentation.                                                              |
| ADR review       | A review that closes or proposes an ADR.                                                            |

> **Assumption CR-A-1.** A PR that is open for > 7 days without
> progress is closed and reopened on demand. Stale review is its
> own defect.

## 4. Review Roles

### 4.1 Author

- Authors the change.
- Writes the PR template fields.
- Responds to review feedback within the SLAs in §10.
- Updates the PR as needed.

### 4.2 Reviewer

- Reviews the change.
- Approves, requests changes, or comments.
- Verifies the PR template fields.

### 4.3 Approver

- A reviewer with merge rights for the branch.
- Performs the merge.

### 4.4 Trust Team

- Performs security review for sensitive changes.
- Performs plugin security review per `33-Plugin-SDK` §8.4.

### 4.5 Docs Maintainer

- Reviews documentation changes.
- Verifies glossary consistency.

## 5. Review Taxonomy

Pine uses a *three-axis* review:

| Axis          | What it covers                                                              |
|---------------|-------------------------------------------------------------------------------|
| Behaviour     | Does the change do what the PR claims?                                       |
| Quality       | Is the change small, focused, idiomatic, testable, maintainable?              |
| Security      | Does the change expand attack surface? Permissions, encryption, validation.  |

A change is approved when **all three axes pass**.

## 6. Review Checklists

### 6.1 Code Review Checklist

- [ ] Linked issue and ADR (if applicable).
- [ ] Tests cover behaviour and failure modes.
- [ ] No dead code, no unused exports.
- [ ] Names are intentional.
- [ ] Public API surface matches the API guidelines
      (`34-API-Guidelines`).
- [ ] Persistence changes are idempotent (`14-Synchronization`).
- [ ] Plugin calls are sandboxed (`12-Plugin-Architecture`).
- [ ] Logging is structured and PII-free (`30-Observability`).
- [ ] Doc comments updated.
- [ ] Changelog entry added.
- [ ] No force-push to protected branch.
- [ ] DCO signed off.
- [ ] Mermaid diagrams render (`26-Diagrams`).

### 6.2 Security Review Checklist

- [ ] No secret in source.
- [ ] No path traversal.
- [ ] No injection (SQL, regex, shell, JSON, YAML, plugin
      marshalling).
- [ ] Authentication and authorisation checks in place.
- [ ] No PII in logs or telemetry.
- [ ] No external network call without explicit permission.
- [ ] Cryptographic choices match the security baseline
      (`15-Security`).
- [ ] Threat model updated if the change widens attack surface
      (`38-Threat-Model`).

### 6.3 Docs Review Checklist

- [ ] Document ID matches `PINE-DOC-NN-*`.
- [ ] Glossary IDs are reused (`03-Glossary`).
- [ ] Cross-references match the referenced document's section.
- [ ] No TODO placeholders.
- [ ] Mermaid blocks parse (`mermaid.parse`).
- [ ] RFC 2119 keywords are correct.
- [ ] Document is in English (per `27-Development-Guidelines` §10).
- [ ] Revision history is up to date.

## 7. Approval Rules

| Change kind                       | Approvers required                                                    |
|-----------------------------------|-----------------------------------------------------------------------|
| Policy document (`00`, `01`, `02`) | 2 (one Engineering, one Maintainer Council member if Vision-affecting) |
| Other SSoT documents              | 1 (with domain expertise)                                            |
| Core code (domain, application)   | 1                                                                     |
| Infrastructure code               | 1                                                                     |
| UI code                            | 1                                                                     |
| AI model artefact                 | 1 + security review                                                   |
| Plugin SDK change                 | 1 + security review                                                   |
| Plugin marketplace update         | 1 (trust team)                                                        |
| Documentation                      | 1 (docs maintainer)                                                   |
| Translation                       | Locale lead                                                           |
| Security patch                     | 2                                                                     |
| Hotfix                             | 1 (security-sensitive: 2)                                            |

A PR author MUST NOT count as an approver.

## 8. Anti-Ps in Review

### 8.1 Author Anti-Ps

- Opening a PR without filling the template.
- Pinging only one domain expert when two are needed.
- Closing a PR without incorporating review.
- Force-pushing after a reviewer has started.

### 8.2 Reviewer Anti-Ps

- Approving without running the code.
- Approving with `LGTM` only.
- Adding comments without verified facts.
- Reviewing past the SLA without extending it explicitly.

### 8.3 Anti-Pattern Checklist

- "Drive-by approvals" — not allowed.
- "Rubber stamps" — not allowed.
- "Drive-by nitpicks" — small format complaints belong in linters.

## 9. SLAs

| Stage                     | SLA                                                       |
|---------------------------|-----------------------------------------------------------|
| First reviewer response   | 1 business day                                            |
| Subsequent responses      | 1 business day each round                                 |
| Final approval            | Within 3 business days of last round resolving             |
| Security review           | 3 business days for medium risk; 5 for high risk          |
| Docs review               | 2 business days                                           |
| Hotfix review             | 4 hours                                                   |
| Policy-document review    | 5 business days                                           |

### 9.1 SLA Failure

- An SLA failure is *not* a defect; it is a signal.
- A repeated SLA failure for the same reviewer is escalated to
  their manager.
- A repeated SLA failure for the same author is escalated to the
  Engineering Review Board.

## 10. Mentorship in Review

- A first-time contributor receives *teaching* reviews.
- A teaching review includes a short rationale per major comment.
- A teaching review may take longer than the SLA, but the extra
  time is documented.

## 11. Tools

- The CI system flags the missing template fields.
- The CI system enforces the required checks per branch.
- The CI system enforces the protected-branch rules.
- The CI system produces a *review quality* signal monthly for
  reviewer calibration.

## 12. Cross-Cutting Constraints

- A PR MUST be reviewed before merge.
- A PR MUST have a passing CI gate before merge.
- A policy-document change MUST have *two* approvals.
- A security-sensitive change MUST have a *security review*.
- A self-approval is *never* allowed.

## 13. Deferred Decisions

| ID       | Decision                                                                                  | Owning document(s)         |
|----------|-------------------------------------------------------------------------------------------|----------------------------|
| CR-D-1   | Calibration rubric for review-quality signal.                                                | `37-Code-Review` (next rev.) |
| CR-D-2   | Whether to require CODEOWNERS for plugin submissions.                                         | `33-Plugin-SDK`            |
| CR-D-3   | Whether to require a *demo* in the PR for user-visible changes.                             | `42-UX-Guidelines`         |

## 14. Quality Checklist

- [x] Roles, taxonomy, checklists, approvals, anti-patterns,
      SLAs, mentorship, tools are all present.
- [x] No TODO placeholders.
- [x] No contradiction with `00-Vision`..`36-Branching-Strategy`.

## 15. Revision History

| Version | Date       | Author                | Change                                                       |
|---------|------------|-----------------------|--------------------------------------------------------------|
| 0.1     | 2026-07-08 | Engineering Review Board | Initial code review document.                                |
