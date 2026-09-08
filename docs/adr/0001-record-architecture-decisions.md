# 0001 — Record architecture decisions

| Field          | Value                                                       |
|----------------|-------------------------------------------------------------|
| ADR            | `0001`                                                      |
| Title          | Record architecture decisions                                |
| Status         | Accepted                                                     |
| Date           | 2026-07-08                                                   |
| Authors        | Project Lead                                                 |
| Reviewers      | n/a — this repository has one contributor                    |
| Supersedes     | —                                                            |
| Superseded by  | —                                                            |
| Closes OD      | —                                                            |
| Deciders       | Project Lead                                                 |

> **Reformatted, not rewritten, 2026-08-31.** This file was the only ADR in the repository for eight
> months and it did not follow the format its own project mandates: `25-ADR` §4.3 makes the front
> matter table above **mandatory**, and this file carried a `## Status` heading with the word
> "Accepted" underneath it — the Nygard layout the decision below adopts, which the specification
> then silently replaced. The decision, its date and its status are unchanged. The mismatch was
> found by `DocumentStatusTests.EveryAdrDeclaresAStatusFromTheAdrVocabulary`, which was written to
> check ADR-0012 and ADR-0013 and failed on the file nobody had looked at.

## Context

We need to record the architectural decisions made on this project.

## Decision

We will use Architecture Decision Records, as described by Michael Nygard in
*Documenting Architecture Decisions* (thinkrelevance.com, 15 November 2011).

`25-ADR` §4 constrains that format further for this repository: the front-matter table above is
mandatory, and the body sections are Context, Decision, Consequences.

## Consequences

- A decision that is not written down is not a decision anyone else can follow, and this file is the
  authority for writing them down.
- The obligation is only as good as its enforcement, and for eight months there was none: the
  register in `25-ADR` §11 seeded twenty ADRs and marked every one of them Accepted while seventeen
  had no file at all, and `Argon2idKeyDerivationService` cited `ADR-0013` — a table row — as the
  authority for the vault's key derivation. Two of those seventeen were written from the code in
  iteration 20 (ADR-0012, ADR-0013); the rest are still register entries with nothing behind them.
- `DocumentStatusTests.EveryAdrCitedInSourceHasAFile` now fails the build when source code cites an
  ADR that does not exist, which is the narrow part of this obligation a test can hold.
