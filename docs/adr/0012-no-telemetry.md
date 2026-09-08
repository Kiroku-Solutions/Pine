# 0012 — Pine collects no telemetry, and performs no network I/O at all

| Field          | Value                                                       |
|----------------|-------------------------------------------------------------|
| ADR            | `0012`                                                      |
| Title          | No telemetry                                                 |
| Status         | Accepted                                                     |
| Date           | 2026-08-31                                                   |
| Authors        | Written retrospectively from the implementation (TD-40)      |
| Reviewers      | n/a — this repository has one contributor                    |
| Supersedes     | The seeded row `0012` in `25-ADR` §11, "Telemetry is opt-in, default-off" |
| Superseded by  | —                                                            |
| Closes OD      | `OD-10` (telemetry policy)                                   |
| Deciders       | The implementation. See the note below.                      |

> **This ADR does not record the decision its citations assume.** Eight places across four documents
> cite `ADR-0012` as the authority for a telemetry switch that is *opt-in and default-off*; they are
> listed under **Citations** below. No such ADR existed, and — more importantly — no such switch
> exists either. What was built is stronger than what was cited, and writing this file the way the register
> implied would have understated the actual posture. TD-40 asked for "write them or drop the
> citations"; the honest third answer is *write what happened*.

## Context

`00-Vision` §5 makes local-first, privacy-preserving operation a non-negotiable principle. The
document set turned that into a *policy*: telemetry exists, it is granular, and it is off until the
user turns it on (`30-Observability` §10). A consent checkbox was scaffolded in the settings UI to
match.

Two things then happened in the implementation.

1. The consent checkbox was **wired to nothing**. It stored a preference no code read, and no code
   would ever have read it, because there was no telemetry client to gate. It was removed as part of
   the same sweep that deleted the seeded fictional account and the dead `CloudSync` option (audit
   §12.22, **TD-80**). A control that appears to govern something and governs nothing is worse than
   no control: it invites a user to believe they have made a choice.
2. The audit found, and repeated verification confirms, that **`src/` contains no network client of
   any kind** — no `HttpClient`, no `WebClient`, no `WebRequest`, no `Socket`. This is recorded as the
   project's single biggest strength: there is no egress path to gate, opt into, or misconfigure.

So the policy in the documents describes a mechanism, and the mechanism was never the thing. The
decision Pine actually took is simpler and harder to get wrong.

## Decision

**Pine collects no telemetry. There is no telemetry client, no consent switch, and no network I/O
anywhere in the application.**

The absence is the control. There is no setting to misread, no default to regress, no consent state
to persist incorrectly, and no endpoint that could be added by a dependency without someone noticing
the first `HttpClient` in the tree.

Diagnostics serve the same purpose locally: the JSONL log sink writes to disk with redaction at a
single boundary (`LogRedactor`), and the diagnostics panel reads that file back. Nothing leaves the
machine unless the user copies it there themselves.

## Consequences

**Positive.**
- The privacy claim is verifiable by a grep rather than by reading consent-handling code, which is
  the difference between a property and an intention.
- There is no consent state to get wrong across five platform heads.
- `38-Threat-Model` TM-54 ("data leaves the device without consent") is not mitigated by a default —
  it is unreachable.

**Negative, and stated rather than glossed.**
- **The project has no crash reporting and no usage signal.** A crash on a user's machine is visible
  only if that user finds the log file and sends it. `PineCrashLogger` exists for exactly this and
  puts the detail on disk; nothing carries it further.
- **`30-Observability` §10, §7 and the SLO material describe a telemetry surface that does not
  exist.** That document is marked `Partially implemented` and those sections are annotated in place.
- **This makes any future update check a decision with real weight.** `29-Deployment` §5.1 specifies
  an opt-in, default-off update check. Building it would be Pine's *first* network call and would
  end the property this ADR records. Iteration 20 declined to build it inside a packaging ticket for
  that reason (audit §12.42). It needs its own ADR superseding this one, not a quiet addition.

**Neutral.**
- The AI stack is unaffected: inference is on-device, and the categoriser's training data never
  leaves the vault (see `32-AI-Training`, TD-16).

## Alternatives Considered

- **Opt-in, default-off telemetry, as the register said.** Rejected on evidence rather than on
  principle: it was scaffolded, it was inert, and shipping an inert consent control is a lie told to
  a user in a checkbox. If telemetry is ever wanted, the switch is the last part to build, not the
  first.
- **Anonymous, aggregate crash reporting only.** Not rejected on the merits — deferred. It would
  still require the first network client, and that is a decision worth taking deliberately, in its
  own ADR, with the cost to this one stated.
- **Keep the checkbox and disable it.** Rejected for the same reason as the first option: a disabled
  control still describes a capability that does not exist.

## Risks

| Risk | Mitigation |
|---|---|
| A defect that only manifests on user machines is never seen | **Unmitigated, and accepted.** The local JSONL log and `61-Manual-Test-Plan` are what stand in for it |
| A future dependency introduces network I/O unnoticed | **Closed, iteration 21.** `NetworkEgressTests` fails the build if any `.cs` file under `src/` names a networking type, or if any project references a networking package. The property is now enforced rather than intended |
| The documents keep asserting a telemetry switch | `30-Observability` and `29-Deployment` are now marked and annotated; this ADR supersedes the seeded row they cite |

## Compliance

- `00-Vision` §5 — local-first, privacy-preserving. Satisfied more strongly than the policy required.
- `15-Security` §5 — telemetry policy. Closed by absence.
- `38-Threat-Model` TM-54, and the mitigation table row that cross-references this ADR.
- `30-Observability` §10 — **contradicted**. That document describes a switch; there is none.

## Citations

Every place in the document set that cites this ADR, quoted rather than numbered. Line numbers were
tried first and were wrong within one commit — the `Reconciled` row that item 10 added to each header
shifted every one of them by one line, which is the whole argument against citing a moving target.
`DocumentStatusTests.EveryAdrQuotationStillAppearsInTheDocumentItCites` fails the build if any quote
below stops matching the document it names.

- `30-Observability.md` — "telemetry* principles (per `00-Vision` §5 and ADR-0012)"
- `30-Observability.md` — "The telemetry switch is *off by default* (ADR-0012)"
- `30-Observability.md` — "Privacy posture is consistent with ADR-0012 and `15-Security`"
- `29-Deployment.md` — "settings), and *default-off* (ADR-0012)"
- `29-Deployment.md` — "Telemetry posture is consistent with ADR-0012"
- `32-AI-Training.md` — "Privacy posture is consistent with ADR-0012 and `15-Security`"
- `38-Threat-Model.md` — "Data leaves the device without consent"
- `38-Threat-Model.md` — "Telemetry opt-in"

## Traceability

- Closes `OD-10` (telemetry policy).
- Supersedes the seeded `0012` row in `25-ADR` §11, which said "opt-in, default-off".
- Cited by `29-Deployment`, `30-Observability`, `32-AI-Training`, `38-Threat-Model`,
  `56-IMPLEMENTATION-PLAN`.
- The consent checkbox removal is audit **TD-80**, §12.22.
- The "no network egress" finding is the audit's §11 "biggest strength".
