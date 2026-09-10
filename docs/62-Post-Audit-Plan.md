# 62 — Post-Audit Implementation Plan

| Field            | Value                                                                                |
|------------------|--------------------------------------------------------------------------------------|
| Document ID      | `PINE-DOC-62-POST-AUDIT-PLAN`                                                        |
| Status           | Non-normative — working plan, superseded by its own execution                        |
| Reconciled       | 2026-09-09 · Written at the close of iteration 37, when all four phases of `60-Production-Readiness-Audit` §10 were ticked (37/37) and 68 debt rows remained open. It plans what comes after the audit's roadmap, not the roadmap itself. Every item names the row it closes, so this document goes stale the moment those rows do — read the register, not this, for current state. |
| Owner            | Engineering Review Board                                                             |
| Created          | 2026-09-09                                                                           |
| Last Updated     | 2026-09-10 — Stage 0 item 0.1 executed and recorded in §3.1                          |
| Prerequisites    | `60-Production-Readiness-Audit` §10 (all phases closed), §11.1 (current verdict)      |
| Related Documents| `60-Production-Readiness-Audit`, `28-CI-CD`, `23-Roadmap`                             |

---

## 1. Purpose

`60-Production-Readiness-Audit` §10 is finished. Its four phases were the remediation programme, and
they are closed. **That is not the same as the product being finished**, and §11.1 puts the current
state at 3.4/5 — not production ready, but with a bounded punch list rather than an open-ended one.

This document is that punch list, ordered. It exists because the audit's roadmap has no Phase 4, so
without it the next iteration would begin by picking a row out of a register of sixty-eight.

## 2. The ordering principle

Three rules decide the order below, in priority:

1. **A guard that does not run is not a guard.** Everything the audit built — architecture rules, the
   coverage ratchet, the egress scan, the document-status tests, 1,283 tests — is inert until
   something triggers it. That makes one row structurally more valuable than any feature.
2. **Anything a user can see that is not true comes next.** This is the defect class the audit spent
   most of its effort removing, and it is the class most likely to cost trust rather than time.
3. **Then correctness the user cannot see, then breadth.** A defect that damages data outranks a
   feature that is missing, and a missing feature outranks a tidy-up.

## 3. Stage 0 — Arm the net (TD-07, TD-54, TD-136)

**One row, and it was first by a wide margin.** CI *was* `workflow_dispatch:` only — a broken head, a
failing test, a coverage regression or a new network dependency all reached `main` unless a person
remembered to dispatch the workflow. **Item 0.1 closed that on 2026-09-10** (§3.1). Items 0.2 and 0.3
remain: an armed trigger fires the jobs that exist, over the projects those jobs happen to name.

| # | Work | Closes | Notes |
|---|---|---|---|
| 0.1 | ✅ **Done** (2026-09-10) — Re-arm `pull_request: branches: [main]`, and `push:` on `main` | TD-07, TD-136 | Both were armed, not just `pull_request`: the compute case was answered by holding back one job rather than one trigger (see §3.1 below) |
| 0.2 | Build `Pine.slnx` instead of enumerating projects | TD-54 | A project added later is currently outside CI silently. This is the same drift class as TD-07 |
| 0.3 | Assert the gate actually ran | — (TD-136 closed by 0.1) | A workflow that runs and skips its own gate is worse than one that does not run, because it reports green. TD-136's Fix column asked only for the trigger, which 0.1 delivered, so this item now stands on its own reasoning rather than on a register row |

**Definition of done:** open a pull request with a deliberately failing test and watch CI reject it.
Nothing in this stage is finished on the evidence of a YAML diff.

**Estimate:** hours, not days. It is first because of leverage, not size.

### 3.1 Item 0.1, as built (2026-09-10, iteration 38)

`on:` now carries `pull_request: branches: [main]`, `push: branches: [main]` and the original
`workflow_dispatch:`. Both triggers were armed rather than only `pull_request`, and the compute
question this row raised was answered somewhere else: **the one job held back is `package`**, by an
`if: github.event_name != 'pull_request'`. It publishes a ~680 MB artefact that no reviewer opens,
and every defect it could catch is already caught by `desktop` and `test`, which it depends on and
which do run on every pull request. Narrowing a trigger would have removed a gate; narrowing one job
removed a bill. That distinction is the whole of the decision.

**The trigger is now asserted by a test.** `BuildHygieneTests.CiIsArmedOnPullRequestAndPushToMain`
isolates the `on:` block, with comment lines stripped, and fails if either trigger or either
`branches: [main]` scope is gone. This is not ceremony: the triggers were deleted once already, in
`79c5364`, and nothing anywhere went red. The guard was verified by falsification — the block was
reduced back to `workflow_dispatch:` alone, the test failed, the block was restored, the test passed.

Eight documents asserted the old state and were corrected rather than left to rot: `README`,
`STATUS`, `CHANGELOG` §Known limitations, `16-Quality-Attributes` §Reconciled, `28-CI-CD`
§Reconciled, `53-Release-Checklist` §Reconciled, `56-IMPLEMENTATION-PLAN` §Reconciled, and
`61-Manual-Test-Plan` §3 — whose step 3.1 expected `on: workflow_dispatch:` *"and nothing else"* and
would otherwise have become a manual test that fails when the product is right. Its §3 now walks the
definition of done above. **Three of those eight were found by an adversarial re-read, not by the
first sweep**, which is §7's item 4.3 arriving early: a sentence that was true when written goes
false through work done somewhere else, and nothing points at it.

**What is NOT proved by this, and is the honest limit of the row.** Everything above is still a YAML
diff plus a unit test. A trigger block can be correct while no run ever happens — a disabled
workflow, a repository whose Actions are off, a branch-protection rule that was never configured to
require the check. `61-Manual-Test-Plan` §3.2 is the step that closes that gap, and it needs a real
pull request against the real remote.

## 4. Stage 1 — Finish what the user can already see

Ordered by what a user loses if it is wrong.

| # | Work | Closes | Why here |
|---|---|---|---|
| 1.1 | **Restore from a backup, in the application** | TD-56's remaining half | Backups can now be made from Settings and still cannot be restored except by copying files by hand. A backup nobody can restore is a backup in name. This is the highest-value user-facing item in the document |
| 1.2 | **Fix the navigation defect that replaces the whole shell** | TD-121 | `MainViewModel` and `ShellViewModel` both implement `INavigationService`, only the first is registered, so an in-shell navigation drops the shell. It is shipped, visible, and cheap relative to its effect |
| 1.3 | **Dashboard: make the ten inert widgets real, or take them down** | — | Net worth, expenses, income, savings, goals, investments, charts, calendar, AI suggestions and the budget tile render "No data yet" permanently. They are honest — no fabricated numbers — but ten of twelve tiles are furniture. Build the three or four that the existing queries already support (net worth, income, expenses are derivable today) and delete the rest until their subsystem exists. **This is TD-99's lesson applied before it becomes TD-99 again** |
| 1.4 | **The dashboard's "Scan Receipt" button** | — | It has no `Command` binding at all: it renders, it is clickable, it does nothing. One line |
| 1.5 | **Live scanner camera list** | — | Enumerates "Camera 0".."Camera 3" regardless of hardware. Either enumerate real devices or drop the picker |

**Definition of done for 1.3 and 1.5:** the screen shows only what it can measure. The rule from
TD-99 and the language picker is now the house rule and should be applied without a debt row first.

## 5. Stage 2 — Correctness the user cannot see

| # | Work | Closes | Notes |
|---|---|---|---|
| 2.1 | Diagnose the intermittent SQLite handle disposal | TD-140 | Seen on four different integration tests across three iterations. Every test count in the audit currently costs a re-run. It is a flake in the harness, not in the product, which is why it is here and not in Stage 0 |
| 2.2 | Sign the release artefact | TD-108 | SmartScreen warns on first launch and a user cannot distinguish this build from anyone else's. Blocks any real distribution |
| 2.3 | Decide `TD-132`, and stop deferring it | TD-132 | Its Fix column says "build category management, or delete the `Category` aggregate and let the string be the model — but not both, and not neither". It is now load-bearing in THREE subsystems: splits, rules, and budgets, where iteration 37 had to route spend through a name because the id relationship does not exist. Every further subsystem decides it by default |
| 2.4 | Exception sentences below the UI layer | TD-139 | |
| 2.5 | `TD-148`, `TD-117`'s remaining half, `TD-138`, `TD-120`, `TD-21` | several | Small, mechanical |

## 6. Stage 3 — Breadth: the features the documents promise

Only after Stages 0–2. Each of these is a subsystem, not a fix.

| # | Work | Depends on | State |
|---|---|---|---|
| 3.1 | **Sync** | TD-144 (logical clock), TD-145 (tombstones), TD-146 (natural keys) | Its persistence preconditions were built in iteration 35 and its delivery mechanism was un-blocked in 36, when ADR-0021 moved `ISyncProvider` out of the withdrawn plugin SDK. The three rows above are what remain between this schema and a merge. **Do not start with the transport** |
| 3.2 | Rule engine, second half | TD-142 (retroactive application, with a preview), TD-143 (multi-condition UI) | The engine already evaluates up to eight conditions; the form writes one |
| 3.3 | Attachments: the manual path | TD-141 | FR-TXN-02 asks for attaching a document to a transaction by hand; only the scanner path exists |
| 3.4 | Notifications, forecasting, investments | — | Specified, unbuilt, and unblocked by nothing. Schedule deliberately or narrow the documents that promise them |

## 7. Stage 4 — The documents

| # | Work | Closes |
|---|---|---|
| 4.1 | Revise the non-normative documents that still promise a plugin ecosystem | TD-147 |
| 4.2 | Re-scope `TD-84` (the Diagnostic Information Bundle), whose Fix column waits on a plugin host that will now never exist | TD-84 |
| 4.3 | Sweep the register for rows whose *mitigation* has been overtaken, as TD-67's was | — |

**4.3 is the most valuable item in this section and the least obvious.** TD-67 sat at 🟡 P2 because
"neither is reachable from the wizard", and TD-28 made it reachable five iterations later while
fixing something else. Nobody re-read the row. **That is a class, not an incident**: any row whose
severity rests on a mitigation rather than on the defect can be silently promoted by unrelated work.
Search the register for Impact columns that argue "this is not reachable / not used / not exposed"
and re-verify each one against the current code.

## 8. What is deliberately NOT in this plan

- **A plugin host.** ADR-0021. Not deferred — decided.
- **A rewrite of any kind.** §11.2's original verdict said the architecture was sound and that
  remediation beat restructuring; thirty-seven iterations did not contradict it.
- **Lowering a coverage floor.** The gate has blocked four times and has been answered with tests
  four times.
- **New features ahead of Stage 0.** Building on an unarmed net is how the debt in the register got
  there.

## 9. Definition of done for this plan

The plan is finished when §11.1's verdict can honestly be rewritten again, and the specific sentence
that has to become false is: *"Biggest weakness — nothing runs the tests automatically."*

That is Stage 0. Everything after it is ordinary product work, which is the position the audit was
trying to reach.
