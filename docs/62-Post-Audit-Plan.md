# 62 — Post-Audit Implementation Plan

| Field            | Value                                                                                |
|------------------|--------------------------------------------------------------------------------------|
| Document ID      | `PINE-DOC-62-POST-AUDIT-PLAN`                                                        |
| Status           | Non-normative — working plan, superseded by its own execution                        |
| Reconciled       | 2026-09-09 · Written at the close of iteration 37, when all four phases of `60-Production-Readiness-Audit` §10 were ticked (37/37) and 68 debt rows remained open. It plans what comes after the audit's roadmap, not the roadmap itself. Every item names the row it closes, so this document goes stale the moment those rows do — read the register, not this, for current state. |
| Owner            | Engineering Review Board                                                             |
| Created          | 2026-09-09                                                                           |
| Last Updated     | 2026-10-05 — Stage 4 item 4.3 (§7.3, register sweep: TD-128 closed, mooted). **This plan's named items are exhausted** — Stage 4 is closed. Earlier: 4.2 (§7.2, TD-84 narrowed: plugin inventory struck); 4.1 (§7.1, TD-147 closed: six non-normative documents corrected). Stage 3 (sync) is closed, items 3.0 and 3.1a–3.1h (§6.1–§6.9), with `14-Synchronization` §10 marked out of scope. Earlier: 3.1g (§6.8, §12's sync audit log); 3.1f (§6.7, folder provider/transport/runner: first merge run from the application); 3.1e (§6.6, pairing: TD-154 narrowed); 3.1a–3.1d (§6.2–§6.5); item 3.0 (§6.1, TD-152 and TD-149 closed). Stage 0 closed (§3.1–§3.3); **Stage 1 closed**, items 1.1–1.5 (§4.1–§4.5); **Stage 2 closed as far as a commit can close it**: items 2.1 (§5.1), 2.2 (§5.2, everything but the certificate), 2.3 (§5.3), 2.4 (§5.4) and 2.5 (§5.5, TD-138's device half excepted) |
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
remembered to dispatch the workflow. **Stage 0 is closed: 0.1, 0.2 and 0.3 were all executed on 2026-09-10**
(§3.1, §3.2, §3.3). The trigger is armed, the projects are derived from the solution rather than
listed by hand, and every gate now has to prove it ran.

| # | Work | Closes | Notes |
|---|---|---|---|
| 0.1 | ✅ **Done** (2026-09-10) — Re-arm `pull_request: branches: [main]`, and `push:` on `main` | TD-07, TD-136 | Both were armed, not just `pull_request`: the compute case was answered by holding back one job rather than one trigger (see §3.1 below) |
| 0.2 | ✅ **Done** (2026-09-10) — Build `Pine.slnx` instead of enumerating projects | TD-54 | Done by the row's second option, not its first: the solution cannot be built in one command at all. See §3.2 |
| 0.3 | ✅ **Done** (2026-09-10) — Assert the gate actually ran | — (TD-136 closed by 0.1) | A workflow that runs and skips its own gate is worse than one that does not run, because it reports green. TD-136's Fix column asked only for the trigger, which 0.1 delivered, so this item stood on its own reasoning rather than on a register row. See §3.3 |

**Definition of done:** open a pull request with a deliberately failing test and watch CI reject it.
Nothing in this stage is finished on the evidence of a YAML diff. **This step has not been performed
yet** — everything below was verified locally, by running the scripts and by falsifying each guard.
It is the one thing that needs the real remote, and it is recorded as `61-Manual-Test-Plan` §3.2.

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

### 3.2 Item 0.2, as built (2026-09-10, iteration 38)

**The row's first option is not available, and saying so is half the item.** `dotnet build Pine.slnx`
aborts at restore with two `NETSDK1147` errors on any machine without the android and ios workloads
— this audit has measured that three separate times, and NETSDK1147 is an error, not a warning, so
nothing compiles. A whole-solution build would have to run on macOS with both workloads installed,
on every event, for every project: slower, dearer, and verifying nothing the four legs do not
already verify. So the row's **second** option was taken — *"or assert slnx coverage in CI"* — and
taken further than the wording required.

`scripts/ci-projects.sh <leg>` derives each leg from `Pine.slnx`. First rule that matches wins: a
platform suffix in the TFM (`-android`, `-ios`) picks that platform's leg; a `Microsoft.NET.Test.Sdk`
reference picks the `test` leg; anything else is `desktop`. A project is classified by what it **is**
rather than by where it sits, because a `tests/` folder is a convention and this had to be a fact.
The workflow now names **no project at all** — the seven-path `desktop` list, the two `project:`
entries in the `platform-heads` matrix and the two hard-coded `dotnet test` commands are gone.

**It fails rather than skips, everywhere**, because the failure being designed out is a silent
omission. A project in the solution whose file is missing, whose TFM cannot be read, or whose
platform suffix has no leg, stops the script with a message naming it. So does a leg that matches no
project at all: a job that builds nothing still reports a green tick, so an emptied leg has to be
noticed rather than quietly succeed.

**The chain, which is the actual deliverable:**

1. `EveryProjectFileIsInTheSolution` — every `.csproj` on disk is in `Pine.slnx`. Already existed.
2. `scripts/ci-projects.sh` — every project in `Pine.slnx` lands on exactly one leg, or CI fails.
3. `EveryProjectInTheSolutionIsBuiltBySomeCiLeg` — **new**: runs that script, and asserts the legs
   cover the solution exactly, with nothing in two legs and nothing in none. It also asserts the
   workflow contains no `.csproj` path of its own, so the lists cannot creep back.

Link 3 runs the script rather than reimplementing its rules. A C# copy would agree with the shell
until someone edited one of them, and the test would then be asserting its own opinion about what CI
ought to build instead of the answer CI is actually given.

**Verified by falsification, four mutations:** a twelfth project added to the solution as plain
`net10.0` was picked up on the `desktop` leg with **no edit to the workflow at all** and the suite
stayed green — which is the whole point of the row, demonstrated rather than argued. The same project
given a `net10.0-windows10.0.19041.0` TFM turned the suite red. Re-hardcoding one project path back
into the workflow turned it red. Making the `desktop` and `test` legs overlap turned it red. A fifth
defect was found this way and fixed: under `set -euo pipefail` a project with no `<TargetFramework>`
element killed the script with a bare exit 1 and no message — the silent failure the script exists to
prevent, reproduced inside the script itself.

**The adversarial pass found four defects, and the first one mattered.** The guard written above —
"the script agrees with the solution, and the workflow names no project" — said nothing about whether
any job still *calls* the script. Deleting the entire `desktop` job from the workflow left the suite
**green**: seven projects would have stopped being built, which is TD-54's own failure mode moved one
layer up. `EveryCiLegIsClaimedByExactlyOneJobThatBuildsIt` now asserts that each leg is claimed by
exactly one job and that the job restores, builds, and — for the `test` leg — runs it. Exactly one,
not at least one: at-least-one is what let the second mutation through, pointing the `desktop` job's
restore step at the `test` leg while every leg stayed referenced by something.

The other three were latent but real. A csproj with a commented-out `<TargetFramework>` above the
live one was classified from the **comment**, so XML comments are stripped before anything is
matched. A csproj with two `Condition`-scoped target frameworks was classified by whichever the
developer typed first; that is now refused outright, because deciding it means evaluating MSBuild
conditions and a shell script must not pretend to. And every call site read the script's output as
`for p in $(...)`, which is wrong twice over: it word-splits a path containing a space, and — the
part that would never have been noticed — **a command substitution in a `for` word list does not
propagate its exit status, even under `set -euo pipefail`**. A failing `ci-projects.sh` would have
left the loop iterating zero times and the step reporting success. Every loop in the workflow now
captures the list into a variable first and reads it with `read -r`.

**What is NOT closed by this.** Adding a whole new *platform* is still a hand edit in two files: the
script refuses to classify an unknown TFM suffix, so the failure is loud and immediate rather than
silent, but it is still a failure someone has to answer. That is the honest boundary — this item
removes the drift for projects, not for platforms.

### 3.3 Item 0.3, as built (2026-09-10, iteration 38)

**What "the gate did not run" actually looks like.** A step whose `if:` is false is *skipped*, and a
skipped step does not fail its job. A loop over an empty list iterates zero times. A `run:` replaced
by `echo skipped` exits 0. All three report green, and the tick is what a reader takes as evidence.
The live case here is the coverage floor: it runs `if: matrix.os == 'ubuntu-latest'`, which is
deliberate, and until this item nothing anywhere said it had to run on ubuntu either.

**Receipts.** `scripts/ci-receipts.sh record <name>` is called by a gate at the moment it has
succeeded — after the command it guards, so `set -e` makes the ordering load-bearing — and each job
ends with an unconditional step calling `verify` with the receipts that job was supposed to produce.
The expected set is computed **independently** of the conditions on the steps themselves: the
coverage step says "run on ubuntu", and the verify step says, separately, "on ubuntu the `coverage`
receipt must exist". A mutation now has to edit two statements in two places that agree.

**The coverage receipt is written inside `scripts/coverage-gate.sh`, not by the workflow**, after the
floors have been compared and past the failure branch. A receipt written from the workflow would
prove only that the workflow reached a line, which is true of a step whose command does nothing —
and that is not hypothetical here: iteration 30 recorded a mutation that survived because a guard
matched the *comment* above the coverage step rather than the step.

**`steps.<id>.outcome` would have been shorter and is not enough.** GitHub can report that a step was
skipped. It cannot see inside one: a loop that ran zero times, a script that returned before
comparing anything, and a gate whose `run:` was replaced all have outcome `success`. The receipt is
written by the gate itself, at the point of success, which is the only place that knows.

**What is now covered.** Every project built on the `desktop` and platform legs, every suite run on
the `test` leg, the coverage floor on ubuntu, the LFS-pointer check and the artefact build in
`package`. `EveryJobWithAGateAssertsThatTheGateRan` asserts a job that records must also verify, that
no verify step carries an `if:` — a verification that can be skipped verifies nothing — and that the
workflow never writes the coverage receipt itself.

**Verified by falsification, five mutations.** Giving the verify step an `if: false` turned the suite
red. Deleting the test job's verify step turned it red. Moving the coverage receipt out of the gate
and into the workflow turned it red. Then the two that matter, run for real rather than asserted:
`scripts/coverage-gate.sh` was executed against the live suite and wrote its receipt after passing
all five floors; with the gate neutered, the test job's verify block failed naming `coverage` as the
gate that did not run, while the same block on windows — where the gate is skipped by design —
passed. That is the distinction the whole item is about, and it holds in both directions.

**The adversarial pass found four more, and one of them broke the identity comparison.** Receipts
were first written one file per name, with `/` and `:` folded to `_` to make the name a safe file
name — which made the mapping non-injective: `gate/a` and `gate:a` became the same file, so one
gate's receipt satisfied another gate's verify. A guard whose identity comparison can be fooled is
not a guard. Receipts are now appended as whole lines to one log and matched with `grep -Fxq`: fixed
string, whole line, no pattern interpretation, so `build:src/A` can no longer be satisfied by
`build:src/AB` either.

The other three were in the guard test, and they share one root: **it asked whether a verify call
existed, never what it checked.** Deleting the desktop job's only `record` call passed, because the
job was in the set being judged only by virtue of recording — a test whose scope is defined by the
thing it checks cannot notice that thing's absence, so the set is now derived from the gates
themselves (`dotnet build`, `dotnet test`, the coverage gate, the packaging script). Narrowing
`verify weights artefact` to `verify weights` passed, so every recorded receipt name must now appear
in its job's verify step. Moving a verify step to the top of its job passed, so the verify step must
now come after the last `record`; above them it would read an empty log and could only pass by
checking nothing.

**And the strengthened test immediately found a fifth defect in this item's own work**: the `test`
job builds its projects before running them — a gate, since a warning is an error here — recorded
`build:` receipts for them, and verified only the `test:` ones. It verifies both now.

**The stale-receipt case is closed in both directions.** Every job resets the log immediately after
checkout rather than trusting that a hosted runner starts empty, and `verify` consumes the log once
it passes, so a developer who runs a gate locally and then verifies again after changing something
cannot pass on the earlier run's receipt.

**What is NOT closed by this.** These receipts prove a gate ran; they do not prove it was *worth*
running. The audit's own record contains the sharper case — a build error left `dotnet test
--no-build` running stale binaries, so the suite genuinely ran and genuinely proved nothing about the
code as changed. A receipt would have been written. That is a different guard, and it is not claimed
here.

## 4. Stage 1 — Finish what the user can already see

Ordered by what a user loses if it is wrong. **Stage 1 is closed: 1.1 to 1.5 were all executed on
2026-09-10 and 2026-09-11** (§4.1–§4.5). A backup can be restored, an in-shell page no longer drops the
shell, the dashboard shows only what it can measure, its Scan Receipt button works, and the camera
picker that listed four devices whatever was attached is gone.

| # | Work | Closes | Why here |
|---|---|---|---|
| 1.1 | ✅ **Done** (2026-09-10) — **Restore from a backup, in the application** | TD-56 (closed) | Restore refuses anything it cannot verify first, copies what it is about to replace, and locks the vault afterwards. See §4.1 |
| 1.2 | ✅ **Done** (2026-09-11) — **Fix the navigation defect that replaces the whole shell** | TD-121 (closed) | The shell's navigator is now a type of its own that the container cannot supply, so a page can only get it from the shell it is shown in. The defect was also a lock leak the row did not name. See §4.2 |
| 1.3 | ✅ **Done** (2026-09-11) — **Dashboard: make the ten inert widgets real, or take them down** | — (TD-149 registered) | Five tiles, every one of them reading the ledger: total balance, this month, accounts, budgets, recent transactions. Five deleted. "Net worth" became "total balance", because nothing in the model says which account is a debt. Found on the way: the total the dashboard had been computing and not showing, an import that refreshed nothing, and a "this month" that was UTC's month. See §4.3 |
| 1.4 | ✅ **Done** (2026-09-11) — **The dashboard's "Scan Receipt" button** | — | It opens the receipt page in the shell, and does not exist on a head that cannot scan. Not the one line the row promised: a button that navigates needs the shell it is shown in (TD-121) and the head's capabilities (TD-85). See §4.4 |
| 1.5 | ✅ **Done** (2026-09-11) — **Live scanner camera list** | — (TD-120 widened) | Dropped, by the row's second option, because the first is not available: OpenCV cannot enumerate devices, and probing indices would turn the camera on before the user pressed Start. See §4.5 |

### 4.1 Item 1.1, as built (2026-09-10, iteration 39)

**A backup nobody can restore is a backup in name, and that was the state.** `VaultBackupService`
had been complete since iteration 10 and reachable from Settings since 37; recovery still meant
closing Pine, finding `backups\pine-….db`, and copying it over `pine.db` by hand.

**The order of operations IS the design, and each step exists because of a specific way this can
hurt someone.** `IVaultRestoreService.RestoreAsync`:

1. **Containment.** The backup must sit in this vault's own `backups` directory. Copying a file the
   application was handed over someone's ledger on their say-so is not a feature.
2. **It must open with this vault's key and look like a Pine vault.** SQLCipher reports nothing at
   open time — a wrong key surfaces as "file is not a database" on the first *read* — so the probe
   is a real query. A truncated, corrupt or foreign file dies here.
3. **It must not be from a newer Pine.** EF Core applies only *pending* migrations and is silent
   about migrations it has never heard of, so a vault written by a later build opens without
   complaint and is then written to by a model that does not match it. The comparison is against
   this assembly's own migration list.
4. **The current vault is copied first**, tagged `PreRestore`, through the same service that writes
   every other backup — so the undo is itself an ordinary backup that can be listed and restored.
   If that copy cannot be taken, the restore does not happen.
5. **Then, and only then, the file is replaced.**

Every refusal in 1–3 leaves the vault untouched, which is what makes them refusals rather than
failures, and every test for them asserts both halves: that it stopped, and that the vault still
holds what it held. Step 4 is proved end to end — restore, decide it was a mistake, restore the
`PreRestore` copy, and the ledger is back.

**The handler locks the vault afterwards, and that is the feature rather than a limitation.** Every
`PineDbContext` in flight was built against the file that has just been replaced and every cached
screen describes rows that may no longer exist. Locking forces the one path that re-opens the vault
from scratch. The alternative — reloading every view model — is a larger surface with a worse
failure mode: a stale screen that looks correct. It locks only on success; a refused restore changed
nothing, and a password prompt for it would be a second failure on top of the first.

**Settings offers a list, not a file picker**, for the mirror image of the reason the export half has
none: the *source* is not the user's to choose either, so a picker would offer a choice the service
then refuses. The confirmation is the scrim-and-panel shape the void dialog already uses, and it
names the moment being restored to, because "restore backup" and "throw away everything since 9
September" are the same action and only the second sentence is honest. Sixteen strings, English and
Spanish, no literals — `NoHardcodedUiStringsTests` would not have it otherwise.

**Falsification found five defects, three of them in the tests rather than the code.** Two of the
first tests written passed for the wrong reason: the containment test used a text file, which the
key probe rejected anyway, so **deleting the containment check entirely left it green** — it now
uses a perfectly good backup of this very vault, moved one directory up, which only containment can
refuse. The stale-sidecar test asserted that the restore deletes `pine.db-wal`; deleting those two
lines left it green twice, because SQLite removes the sidecars itself on a clean close. That test now
asserts what is honestly observable, and the service says in its own comments that those two lines
are insurance against the non-clean case which this suite **cannot** construct.

**The fifth was mine and it was real.** The service first called `SqliteConnection.ClearAllPools()`,
which releases handles to every database in the process. xUnit runs test classes in parallel, so in a
full integration run two unrelated migration tests failed and passed in isolation — the shape of
TD-140, made worse by this file. It clears the pool for *this* connection string now, which is both
narrower and more correct. The full integration suite is 248/248.

**The adversarial pass found a data-loss defect, and it was the real one.** The replacement was
`File.Copy(source, pine.db, overwrite: true)`, which opens the user's live vault for writing and
streams into it. A disk that fills, a process that is killed, a power cut or an antivirus lock
partway through leaves `pine.db` as a truncated hybrid — not the old vault, already overwritten, and
not the backup, whose copy never finished. **That is precisely the outcome this class's own comment
said could not happen**, and the sentence the UI shows on failure — "nothing was changed and your
vault is as it was" — was false in exactly that case. The copy now lands on a `.restore-tmp`
neighbour on the same volume and only a complete file is moved into place; a failure in either step
cleans up and refuses, and the sentence is now true.

**No test distinguishes that from the naive spelling, and the code says so.** Every failure this
suite can induce — a read-only destination, a locked file, a missing source — fails before either
version has written a byte, so reverting the three lines leaves the suite green. What the two tests
around it assert is what is honestly assertable: a failed replacement refuses, the vault is still
readable and unchanged, and no staging file is left behind. This is the second place in this item
where a design decision is defended by argument rather than by a test, and both are labelled as such
where a reader of the tests will find them.

The same pass also found that the confirmation read `SelectedBackup` live rather than capturing it:
the list keeps keyboard focus under the scrim, so an arrow key could move the selection between
seeing the panel and pressing Restore. It now captures a `RestoreTarget` when the panel opens, which
is the shape the void dialog already uses, and the panel binds to that. Two things it checked and
cleared: double-clicking Restore cannot re-enter, because `IsRestoringVault` is set before the first
`await` on a single-threaded UI; and locking the vault genuinely tears the shell's page down, so no
cached screen survives to write to the restored file.

**Correction, 2026-09-11 (found while closing 1.2).** This item left **two unit-suite coverage floors
breached**: `Pine.Infrastructure` at 77.63% against 78%, and **overall at 67.51% against 68%** — the
second missed by the first write-up of this correction and found by the independent verifier.
`VaultRestoreService` is tested only by the integration suite, and the gate measures only the unit
suite. The commit reported
"unit 1065, integration 250, both green" and never ran `scripts/coverage-gate.sh` — so CI, now armed,
would have rejected it. Measured, not inferred: the gate was run on a clean checkout of that commit, and
the two coverage reports compared line for line (8,902 vs 8,903 covered lines in that assembly — item
1.2 moved it by one). It was answered with tests rather than a lower floor; see §4.2.

**What is NOT closed by this.** A backup still contains `pine.db` and nothing else — not the vault
header, not preferences. Restoring into a vault whose password was changed after the backup works,
because the data key is never rotated (TD-86), but that is a property of TD-86 rather than a promise
made here. And nothing has been exercised on a screen: these are service, handler and view-model
tests.

### 4.2 Item 1.2, as built (2026-09-11, iteration 40)

**The row understated its own defect, and the first test written found out how.** TD-121 said an
in-shell navigation drops the shell. It does, and the reproduction proved it against the real
Windows container: open the receipt page, press Cancel, and the window shows a bare transactions list
— no sidebar, no lock button. But `MainViewModel`'s lock handler only clears decrypted data when the
window is showing the shell (`if (CurrentPage is not ShellViewModel shell) return;`). With the ledger
promoted to the window, **a lock — idle, suspend, or the user's own — found no shell, returned early,
and left the transactions list on screen with the key already gone.** The second reproduction test
asserts exactly that, and before the fix it failed with `found TransactionsViewModel` where the vault
picker should have been. A navigation defect was also a lock leak, and the register row had never
said so.

**Why it survived eight iterations.** Every unit test of the receipt pages substituted
`INavigationService`, so none of them could see *which* navigator the container would actually hand
over. The one question that mattered — "is this the shell's navigator or the window's?" — was being
answered by a registration nobody read, and every test that might have asked it had replaced the
answer with a mock. The new tests use the real container.

**The fix is a second type, not a second registration.** `IShellNavigator` is a separate interface,
implemented by `ShellViewModel` and nothing else, and it is **never registered**: the shell is
transient — one per open vault (TD-72) — so there is no single instance a container could correctly
supply. The shell hands *itself* to the pages that navigate (`IViewModelFactory.CreateScanReceipt`,
`CreateLiveScanner`, `CreateCropReceipt` now take it), and a page that creates the next one hands on
the navigator it was given. `ShellViewModel` no longer implements `INavigationService`, which now
means the window and nothing else. Two things follow that a registration fix would not have given:

- **Handing a receipt page the wrong navigator is a compile error**, not a runtime choice: its
  parameter is an `IShellNavigator`, and the window's navigator is not one. That is a property of
  pages already typed that way — a *new* page could still ask for the window by another name, which
  is what the architecture rule below exists for, and which the adversarial pass proved it first
  failed to catch.
- **A page from a torn-down shell navigates that dead shell, never the live one.** A singleton
  "current shell" forwarder was the obvious alternative and was rejected for exactly this: a
  receipt save completing after a lock and a re-unlock would have navigated the *new* vault's shell.

The three receipt pages left the container, because a container that can build one can only build it
wrong. That moved TD-85's capability gate: it used to be implicit (the pages were registered only on
a receipt-vision head, so resolving them elsewhere threw), and `ActivatorUtilities` would now happily
build the review page on Android, where every dependency exists. The factory refuses them explicitly
now, naming the head. `BudgetsViewModel` took the window's navigator and never called it — harmless
only until its first caller — so the dependency was deleted rather than retyped. And the crop page,
which had never been shown inside the shell and so had never had to answer the question, now lights
the live scanner's sidebar button, since it is the middle step of that one flow.

**The guard is the partition, stated as a rule.** `ArchitectureTests.EachScreen_ShouldTakeTheNavigatorOfThePlaceItIsShownIn`
asserts that exactly the three pre-vault screens (and `PineUiServices`, the window's own handle)
*reach* the window's navigator, that exactly the three receipt pages take `IShellNavigator`, and that
each interface has exactly one implementation. "Reach" is the word that matters, and it is the
adversarial pass's correction — see below. Two companions close the other doors:
`NoViewModel_ShouldReachTheApplicationStatics` (a view model must not read `App.Services`, which holds
the window's navigator in a static), and
`PineCompositionRootTests.TheShellsNavigator_ShouldNeverBeRegistered_OnAnyHead`.

**The lock handler was inverted too, so the leak no longer depends on the navigation fix.** It used
to clear the screen only if the window was showing the shell — right exactly as long as nothing
holding vault data ever reached the window another way, which is how TD-121 became a lock leak. It now
leaves alone only the screens it recognises as pre-vault (`VaultSelection`, `Setup`, `Unlock`, or
nothing at all) and clears everything else, so the next route to the window fails safe rather than
open. `A_lock_clears_an_in_vault_page_even_when_the_window_shows_it_directly` puts the ledger straight
into the window — the shape the defect produced — and asserts the lock still takes it down.

**The adversarial pass found two holes in the guards and a lifecycle defect the fix had created.**
An independent reviewer was given the diff and told to break it; everything below it demonstrated
with a probe or a mutation, not argued.

- **The architecture rule matched the interface by name.** A page taking `MainViewModel` — the
  concrete window navigator, a registered singleton — or `IEnumerable<INavigationService>` resolved,
  navigated the window, and left **all 1070 unit tests green**: TD-121 again, through the same test
  written to prevent it. The rule now asks whether a parameter *reaches* the navigator — is one,
  derives from one, or wraps one in a generic or an array. A third door, `App.Services.Main`, is a
  static and invisible to any constructor rule; it is closed by the IL rule above.
- **Nothing asserted that `IShellNavigator` stays unregistered**, although both the design and the
  architecture test's own remarks leaned on it. Registering it left the suite green too — and a
  registered one can only be a fresh shell no window shows, so a page handed one navigates nothing
  on screen. Now asserted on every head, by descriptor and by resolution.
- **A retired shell still activated what it was handed.** Pages hold the shell that created them, and
  three of them navigate after an `await` or from a dispatcher post. The window was already safe —
  the probe confirmed a late navigation never reached it — but the dead shell made the page current
  and *active*: enlisted for broadcasts, its first load started against a locked vault, alive until
  collected. A shell now retires when it is deactivated and refuses to navigate after that, releasing
  the page instead; it un-retires if shown again.
- **The crop and review pages could pull the user back.** They sit beside the sidebar now, so a user
  can leave mid-operation — which they could not before, when the page filled the window — and both
  navigated unconditionally when their `await` landed. Both navigate only if still current now; the
  save's broadcast still goes out either way, because the transaction was written. The live scanner's
  two posted captures got the same guard, **and those two are defended by argument, not by a test**:
  reaching them needs a camera frame.
- **Seven comments had gone false** — the shell, its view, the infrastructure registration note, the
  composition root, `PageActivation` (twice), `LiveScannerView` and a test's own reason string all
  still said the receipt pages were registered, or cited the old interface. And `ValidateOnBuild` no
  longer walks the receipt pages' constructors, so a missing dependency of theirs would throw on the
  click rather than at startup. That is now written on `PineCompositionRoot.Build`; it is covered by
  the unit tests that build all three through the real factory on every head, which is a weaker
  guarantee than startup validation, and is labelled as such.

**A second, independent verifier then checked the fixes rather than the diff, and broke the guard a
second time.** Two more doors to the window's navigator passed both architecture rules, one of them
working today with no other change: Avalonia's own `Application.Current.ApplicationLifetime`, whose
`MainWindow.DataContext` *is* `MainViewModel`; and a static on `MainViewModel` itself. The rules
changed shape in answer, not just in scope:

- **"Takes the navigator" now means holds it, by any route.** Every field of every type — instance and
  static, any visibility, property backing fields included — is inspected, not only constructor
  parameters. That catches the static, a settable property and an `Attach(...)` method alike, because
  a page that navigates later has to keep the navigator somewhere. The one shape it cannot see is a
  navigator used inside one call and dropped, which is not one a page can navigate with after an
  `await`. A self-capture exclusion keeps `MainViewModel`'s own async state machines (`<>4__this`) out.
- **The statics rule became an allow-list over the whole assembly** — five named types, each justified
  in the test — instead of a ban on one namespace, and it now names Avalonia's application and lifetime
  types as well as Pine's. The first version let a helper in `Services/` read `App.Services` and be
  injected into a page.

The verifier also caught three comments still stating the old lock rule in the present tense, a crop
guard that returned without releasing its streams, and the omissions in the coverage paragraph below;
all corrected. The lock-handler allow-list is now tied to the architecture rule: the pre-vault lock test
is a theory over exactly the three screens that rule permits the window's navigator.

**Verified by falsification: nineteen mutations, nineteen killed**, each by the test written for it and
several by more than one. Eleven were written after one of the two independent passes, and six of
those are the passes' own surviving routes:

| Mutation | Killed by |
|---|---|
| The factory hands receipt pages the window, wrapped as a shell | Both reproduction tests, the hand-on test, the architecture test |
| The factory hands receipt pages *another* shell | The reproduction test, the hand-on test — behavioural tests, not the architecture rule |
| The crop page does not hand on its shell | The hand-on test (and the architecture rule, since the stand-in implements the interface) |
| The capability check is deleted | Six tests, one per non-vision head per factory/buildability test |
| A receipt page is registered in the container again | 48 tests: `ValidateOnBuild` fails every head |
| The crop page lights nothing | The crop sidebar test |
| The shell implements `INavigationService` as well | The architecture test |
| A new type takes `INavigationService` | The architecture test |
| A retired shell navigates anyway | The retirement test, and the real-container late-cancel probe |
| A late save navigates even though the user left | The late-save test (its user-stays twin is the positive control) |
| A late crop navigates even though the user left | The late-crop test (likewise) |
| The lock handler goes back to "clear only the shell" | The in-vault-page-in-the-window lock test |
| `IShellNavigator` is registered — **the reviewer's survivor** | The never-registered test, on all four heads |
| A page takes `MainViewModel` — **the reviewer's survivor** | The architecture test, now matching by reach |
| A page reads `App.Services` | The application-statics IL rule |
| A type reads Avalonia's desktop lifetime — **the verifier's survivor** | The application-statics allow-list |
| `MainViewModel` exposes itself in a static — **the verifier's survivor** | The architecture test, now inspecting fields |
| A page holds the window's navigator in a settable property | The architecture test, now inspecting fields |
| A helper outside `ViewModels/` reads `App.Services` | The application-statics allow-list, now assembly-wide |

**The coverage gate was red before this item started, and this item is what found it.** Running
`scripts/coverage-gate.sh` — which the previous item never did — failed on `Pine.Infrastructure`
(77% < 78%). A clean checkout of the previous commit measured the same 77.63% — and overall at 67.51%
against 68%, which the first write-up of this paragraph missed. Both breaches were 1.1's (see the
correction in §4.1); but "not this item's" is only half the truth, and the verifier supplied the other
half: without the new tests this item's tree measures overall at 68.10%, a margin of a tenth of a
point, so this item did not cause the overall breach and did not meaningfully cure it either. The gate's own instruction is "raise the tests,
not the floor", and the nearest uncovered lines would have been the cheap answer. The tests written
instead cover the eleven refusals in `FileVaultHeaderStore.Read` that **neither suite had ever
executed** — a missing KDF section, KDF id, any of the five KDF parameters, creation time, vault id or
salt, a zero header version, a literal `null` — each of which is the difference between refusing a
header and deriving the wrong key from a default. Falsified by mutation: changing which field a
refusal names kills its test. The obvious mutation — quietly defaulting a missing field — **does not
compile**: nullable flow analysis, with warnings as errors, already refuses to use a field that was not
checked, so the compiler is the first guard there and the tests are the second. `Pine.Infrastructure`
went 77.63% → 82.50% — **and most of that is generated code, which has to be said**: of the 558
newly covered lines, 445 are the source-generated `VaultHeaderJsonContext` serializer and 113 are
`FileVaultHeaderStore.cs` itself. The gate has no exclusions, by design, so the figure is honest by
its own rules; it is not a claim that 82% of the hand-written infrastructure is tested. The floors
ratchet up to match (overall 68 → 70, Core 88 → 89,
Infrastructure 78 → 82, UI 38 → 40; two left one point under the rounded figure, with the reason in
the script).

**Numbers.** Unit **1102** (1065 before this item), integration **250**, both green; coverage gate
green, run in Release with coverage exactly as CI runs it.

**What is NOT closed by this.** Nothing here has been exercised on a screen: the tests drive the real
container and a real headless dispatcher, not a window. The live scanner's two late-navigation guards
are argued rather than tested. And the review page's Save still reports "could not be saved" for any
exception thrown after the write succeeded — a pre-existing sentence this item noticed and did not
change; after TD-121 the only code between the write and the end of that `try` is a broadcast and a
guarded navigation, so the window for it has narrowed rather than closed.

### 4.3 Item 1.3, as built (2026-09-11, iteration 41)

**Five tiles, and every one of them reads the ledger.** There were twelve; ten rendered "No data yet"
for thirty-nine iterations. What the ledger can feed was built, and the rest was deleted:

| Tile | Reads | Became |
|---|---|---|
| Total balance | `GetAccountsQuery` | was "Net Worth" |
| This month | new `GetMonthlyCashFlowQuery` | replaces Income, Expenses and Savings |
| Accounts | `GetAccountsQuery` | unchanged, already real |
| Budgets | `GetBudgetProgressQuery` | was inert; now the budgets page's own rows |
| Recent transactions | `GetTransactionsQuery` | unchanged, already real |

Calendar, Goals, Investments, Charts and AI Suggestions were deleted, and so was the fallback template
that gave any tile without a view a plausible "No data yet". None has a subsystem behind it, and TD-99's
rule is that a screen shows only what it can measure.

**The plan's premise was half wrong, and checking it first is what found out.** It said net worth was
"derivable today". Two things were not true of that sentence:

- **The number already existed, was tested, and was shown nowhere.** `DashboardViewModel.TotalBalance`
  summed balances per currency through `Money` — the fix that stopped 1 000 USD and 900 EUR reading as
  "1900 EUR" — and five tests pinned it. No view bound it. It moved into a tile, where it is on screen.
- **It is not a net worth.** Net worth is assets minus liabilities, and nothing in the account model says
  which is which: a card's opening debt may have been entered as 500 or −500, and `AccountType` does not
  decide it. So the tile is called *Total balance*, with the caveat on screen — not in a tooltip, which a
  keyboard or touch user never sees.

**One "this month" tile where the plan named two.** Income, expenses and savings are one read of one
month; as three tiles they would query three times and scatter one comparison across the grid. As one
tile, the figure a user wants — did I spend more than I earned? — is the third column of the same row.

**Every tile says what it knows, and only that.** `IsEmpty` is true only after a *successful* read found
nothing, so "No budgets yet" can never appear before the ledger has answered or after it failed. A failed
read puts its own sentence on the tile — "may be missing or out of date, not zero". Tile headings were
English literals assigned in C#, outside the reach of the markup-only localisation test; they are live
keys now, and so is the page's greeting.

**An independent adversarial review found three defects, and the first was a user-visible lie:**

- **"This month" was UTC's month.** The query copied `Budget.ResolveWindow`'s UTC window "for
  consistency". The reviewer's probe: at 21:00 on 30 September in Ecuador it is already October in UTC,
  so the tile showed October — the month's salary gone, that evening's dinner counted as next month's.
  Every path into the ledger stores a date-only entry as *local* midnight (checked for CSV, Excel, receipt
  and manual entry, not assumed), so the window is now the user's local month, each edge at the offset in
  force at that edge. Consistency with a wrong screen was worth nothing: **the budgets' own window has the
  same defect, and is registered as TD-149** rather than widened into this item.
- **An import refreshed nothing.** Every other write path broadcasts `DataChangedMessage`; the import
  wizard never did, and the dashboard's close callback re-read an `Accounts` collection that no view
  bound — the same "computed and unseen" shape as the total balance. The wizard broadcasts now, and the
  dashboard's own unseen read is gone: each tile reads, refreshes and reports for itself.
- **A reload that failed after an empty success showed the failure *and* "No budgets yet"**, because
  `IsEmpty` was only written by a success. A failure now withdraws the empty state.

It also found **the render tests order-dependent** — markup binds the process-wide localizer, which
another test class switches to the machine's language, so on a Spanish machine the English assertions
failed in some orders and passed vacuously in others — five **test gaps** (four of them surviving
mutations), and false text. All addressed; the budget rows now follow a language switch too.

**Rendered, not only computed.** For the first time in this plan a test puts the tiles in a headless
window at their real size and reads back only the text that is *effectively visible*: figures present,
the empty state absent before a read and after a failure, the failure present. It is the nearest thing
to "exercised on a screen" this repository has.

**Verified by falsification: twenty-eight mutations, twenty-eight killed** — sixteen written with the
change and twelve after the review, five of those the reviewer's own survivors (the UTC window, a
commented-out template, and three empty states bound to a count instead of to `IsEmpty`). Two early ones
did not die and were informative: counting transfers as spending survived because a transfer's two legs
cancel out, so the test now uses a lone leg; and a template renamed to a non-existent type failed at
compile time, which proves nothing about the test, so it was redone.

**Numbers.** Unit **1130** (1102 before this item), coverage gate green in Release (UI 40.70% → 43%,
the render tests' doing). Integration **250**, green in three of four runs; the fourth failed
`ImportDeduplicationTests.Import_WithTwoIdenticalPurchasesOnTheSameDay_PersistsBoth` with
`ObjectDisposedException` on a `SQLitePCL.sqlite3` handle — TD-140's exact signature, now seen on a fifth
distinct test. This item's diff reaches no code that suite exercises (in `Pine.Application` and
`Pine.Infrastructure` it is one comment and one new query no integration test calls), so it is recorded
here as TD-140 rather than claimed as clean; it is also why Stage 2 item 2.1 exists.

**What is NOT closed by this.** TD-149, the budgets' UTC window. Tile reads are not cancelled when the
page is left, so a read can still land on a disposed tile — harmless, logged. `ColumnSpan`/`RowSpan` are
set on no tile and read by nothing; width and height are what size a tile. A month containing only
zero-amount transactions shows a row of zeros rather than the empty state, which is true but odd. And
the budgets *page* rows still freeze on a language switch — the tile re-reads; the page does not.

### 4.4 Item 1.4, as built (2026-09-11, iteration 42)

**The row said "one line". It is not one line, and the reason is the two rows before it.** A button that
navigates needs a navigator, and since TD-121 an in-shell page may only navigate the shell it is shown
in — so the dashboard now takes its `IShellNavigator` from `IViewModelFactory.CreateDashboard(shell)`,
like the receipt pages, and left the container for the same reason they did: a registration could only
have supplied the window's navigator. It also needs `PineHeadCapabilities`, because the button must not
exist where the receipt pipeline does not (TD-85's rule, the sidebar's rule).

**The defect was invisible to everything except a rendered control**, which is why the first test reads
one. A missing `Command` binding is not a compile error, not a view-model state, and not something any
existing test could see: the button rendered, was enabled, was clickable, and did nothing. So the tests
render the dashboard **each head really builds**, from its real container, and read the button:

- it has a command, and is enabled, on Windows;
- pressing it lands on `ScanReceiptViewModel` **inside the shell**, with the sidebar lit — a command that
  replaced the whole shell would satisfy "has a command" and be TD-121 again;
- on macOS, Android and iOS it is **not visible and refuses to execute**. Both halves, because visibility
  is what a user sees and `CanExecute` is what a keyboard or a future second binding would reach.

**A flake in the test, found by running the suite rather than the class.** The three head-profile cases
passed alone and failed in the full run, on a button whose `IsVisible` binding had not been applied yet —
bindings are applied from queued dispatcher jobs, and one pump was enough only on an idle machine. The
harness pumps until it settles now. That cannot hide the defect it exists to catch: a binding that is
missing never settles, and the mutations that delete it still fail (re-run after the fix).

**Verified by falsification: eight mutations, eight killed.** The `Command` binding deleted; the command
pointed at the wrong page; the `CanExecute` guard removed; the `IsVisible` binding deleted;
`HasReceiptVision` hard-coded true; the button bound to the import wizard's command instead; the button
left visible but disabled; and the dashboard registered in the container again (53 tests, `ValidateOnBuild`).

**IT SHIPPED WITHOUT AN INDEPENDENT REVIEW — one was commissioned and died on an API session limit
before reading anything — AND THE DEBT WAS PAID THE SAME DAY**, by the review that covered item 1.5. It
found what the author's own eight mutations could not:

- **The button is gated on `HasReceiptVision`, and reading `HasBackgroundTraining` instead left all
  1,139 tests green.** The two flags are identical on all four heads today, and `PineHeadCapabilities`
  says in its own remarks that they are kept apart precisely because one will move before the other.
  The day they diverge, the sidebar hides the receipt pages while this button offers one that throws on
  click — TD-85, again. The flags cannot be made to differ from a test, so the rule is stated where it
  can be: `ArchitectureTests.OnlyCompositionReadsTheTrainingCapability` — training gates a hosted
  service, and no screen may branch on it.
- **The 20× dispatcher pump's comment overclaimed.** "Pumping cannot hide a missing binding" is true and
  was re-verified; the comment read as if it licensed the pump generally, and it does not — a binding
  that is wrong on first application and corrected asynchronously would be invisible to it. The reviewer
  tried to build that case and could not make it run in the headless session, so it is unproven rather
  than dismissed, and the comment now says only what was checked.
- Verified, not merely assumed: pressing twice, or pressing while the receipt page already shows, keeps
  the shell and re-activates a fresh page — no TD-121 regression; nothing resolves `DashboardViewModel`
  from the container any more; and `IsVisible=false` removes the button from hit-testing and tab order,
  so an access key or automation peer would still meet the refused `CanExecute`.

**Numbers.** Unit **1135** (1130 before this item), integration **250**, both green; coverage gate green,
and the floors ratchet up with the render tests' reach: overall 70 → 72, `Pine.Application` 59 → 61,
`Pine.Ui.Avalonia` 40 → **45**.

**What is NOT closed by this.** Nothing has been pressed on a real screen — the tests drive a headless
window — and the manual step is `61-Manual-Test-Plan` §13A.

### 4.5 Item 1.5, as built (2026-09-11, iteration 43)

**The list was four entries long whatever was plugged in.** The constructor built "Camera 0" through
"Camera 3" and selected the first. On a one-webcam laptop three of them named devices that do not
exist; choosing one opened nothing and reported "Failed to open camera". The names were not device
names either — they were OpenCV indices with a word in front.

**The row offered two options and the first is not available.** OpenCV exposes no device enumeration:
the only way to learn whether index 2 exists is to *open* index 2. A list built that way turns the
camera on — lens and indicator light — for each index in turn, on a screen the user has merely
navigated to and before they have pressed Start. That is a privacy surprise bought with names OpenCV
cannot supply anyway. So the picker is gone, the scanner opens the system default camera, and the
failure sentence now says which camera it tried and what to check.

**What the user loses, said plainly.** On a machine with two cameras, index 1 genuinely opened the
second one, so someone with an external webcam could select it. They now get the default. That is a
real capability removed, not a defect tidied, and it is recorded on **TD-120** — the camera seam row —
because choosing a camera honestly needs a platform API that enumerates devices, which is what that
seam is for.

**The independent review broke this item's tests three ways, and found a defect in the lines it had
just edited.** Everything below came from someone who had not written the code:

- **The capture loop's index was pinned by nothing.** The test asserted the *file contained* the string
  `DefaultCameraIndex` — which its own declaration satisfies for ever. Pointing the loop at index 2 left
  all 1,139 tests green: on a one-webcam machine the scanner would then report a failure with a working
  camera attached, which is this item's own defect class. Every `new VideoCapture(...)` call site is now
  checked, and the mutation dies.
- **The picker came back as a text box and nothing noticed.** A settable `int CameraIndex` bound to a
  `TextBox` is the same lie in a different shape: the user types an OpenCV index with no way to know
  whether it exists. Two rules now: the view model exposes no settable camera member of any type, and
  the view contains no input control at all.
- **A `VideoCapture` was leaked on the exact path this item rewrote.** When DirectShow failed, the local
  was reassigned without disposing the first handle; when both failed, neither was disposed, because the
  `finally` releases `_capture` — still null at that point. A user who freed the webcam and pressed
  Start again could be blocked by Pine's own undisposed handles, waiting on a finaliser. Both paths
  dispose now, and the `finally` releases a capture that was opened but never published. **No test
  covers it**: this suite cannot open a camera, and the argument is written where the code is.
- **The screen's opening sentence was still hard-coded English** — "Press Start to activate camera",
  above a button reading "Iniciar cámara". It is a key now, and the fix exposed a second thing: the
  status slot posts to the UI thread, so setting it through the slot alone left the property empty on
  the first frame. It is rendered synchronously and registered with the slot after.
- **A fresh Windows clone failed a test that has nothing to do with this work**: `.gitattributes` had no
  rule for shell scripts, so `core.autocrlf` gave `scripts/coverage-gate.sh` CRLF line endings and
  `BuildHygieneTests` then read no coverage floors at all. One line added.

**Verified by falsification: nine mutations, nine killed.** The five written with the item — the
fabricated list restored under another name and type; the picker restored with a binding (killed at
compile, because compiled bindings no longer find `Cameras`); the picker restored *without* a binding,
which compiles; the vague "Failed to open camera." sentence; `DefaultCameraIndex` moved off the default —
and the four written after the review: the loop opening a literal index, the wrong capability flag, the
picker as a typed index, and the opening sentence hard-coded again.

**Definition of done for 1.3 and 1.5:** the screen shows only what it can measure. The rule from
TD-99 and the language picker is now the house rule and should be applied without a debt row first.

## 5. Stage 2 — Correctness the user cannot see

| # | Work | Closes | Notes |
|---|---|---|---|
| 2.1 | Diagnose the intermittent SQLite handle disposal | TD-140 | Seen on four different integration tests across three iterations. Every test count in the audit currently costs a re-run. It is a flake in the harness, not in the product, which is why it is here and not in Stage 0 |
| 2.2 | 🟡 **Everything but the certificate** (2026-09-12) — Sign the release artefact | TD-108 | SmartScreen warns on first launch and a user cannot distinguish this build from anyone else's. Blocks any real distribution. **The row's Fix column asks for two things and only one of them is in a commit's gift.** A certificate is bought from a CA against a verified legal identity; no engineering work produces one. What was in reach was the other half, and the state the signing seam was actually in: it had never been executed, and it could not have worked. See §5.2 |
| 2.3 | ✅ **Decided and done** (2026-09-13) — the label is the model; the `Category` aggregate is deleted. Decide `TD-132`, and stop deferring it | TD-132, TD-134 | Its Fix column says "build category management, or delete the `Category` aggregate and let the string be the model — but not both, and not neither". It is now load-bearing in THREE subsystems: splits, rules, and budgets, where iteration 37 had to route spend through a name because the id relationship does not exist. Every further subsystem decides it by default |
| 2.4 | ✅ **Done** (2026-09-13) — Exception sentences below the UI layer | TD-139 (closed), TD-150 (registered) | The domain names the message and the presentation layer words it. The row counted two sets of English sentences below the UI; there were four — the import review's row reasons, and the live scanner's guidance, which was Spanish on every screen — and a live defect underneath them: a language switch re-rendered a stale key over the error on screen. TD-45's "every message in the view models" was false too. See §5.4 |
| 2.5 | ✅ **Done** (2026-09-14), TD-138 narrowed — `TD-148`, `TD-117`'s remaining half, `TD-138`, `TD-120`, `TD-21` | TD-148, TD-117, TD-120, TD-21 (closed); TD-138 (narrowed); TD-151, TD-152, TD-153 (registered) | ~~Small, mechanical~~. Two of five were not: the camera seam, and a display culture the whole process follows. TD-148's guard found five unsent requests, among them the command the ledger's rollback tests had been written against, and the fact that no screen creates a budget. See §5.5 |

### 5.1 Item 2.1, as built (2026-09-11, iteration 44)

**The row asked for a diagnosis, and the first move was the one it named: capture it instead of
re-running until green.** TD-140 had been open for twelve iterations, seen on four different
integration tests across three of them, and every test count in the audit had been costing a re-run.
The stack was captured in full on a run where the failure fired:

```
System.ObjectDisposedException: Cannot access a disposed object. Object name: 'SQLitePCL.sqlite3'
   at SqliteConnection.Deactivate()
   at SqliteConnectionInternal.Close()
   at SqliteConnection.Open()
```

**The registered hypothesis was wrong, and the stack is what says so.** The row's Impact column
offered "a pooled connection disposed while another test still holds a command against it" as the
obvious explanation. The failure is not *during* use — it is inside `Open()`, which is the pool
**handing out a connection whose handle is already gone**.

**What disposes pooled handles in this process is `SqliteConnection.ClearAllPools()`, and it is
process-wide.** The test harnesses called it in teardown, because on Windows a pooled handle is what
stops a temp directory being deleted and that method is what the documentation offers. xUnit runs
test classes in parallel, so each teardown reached into the pool of every other class running at
that moment. Every test ever seen failing this way lives in a different *class* of the same
harness (`TempVault`), and those classes run concurrently — which is all the mechanism needs.

**Fixed, and then measured by taking the fix out again.** With it: six full integration runs, 251
tests, zero failures. Without it — `ClearAllPools` restored exactly as it was, same machine, same
session — the failure returned on the first of four runs, with the identical stack, the identical
object name, and one test that had never been seen failing before. Pooling every run before the fix
against every run after gives 3 failures in 11 versus 0 in 6: **Fisher's exact p = 0.24, one-sided —
not significant.** The counts are consistent with the fix and **do not establish it**. What supports
the diagnosis is the other leg: the stack shows the pool returning a dead handle, and the
process-wide call is the only thing in the process that kills pooled handles. The run counts are
corroboration and are reported as corroboration.

**The independent review found four defects, two of them introduced by this fix, and one of them was
something this section had argued away in writing.** Everything below came from someone who had not
written the code:

- **The fix stopped releasing the pools it most needed to release, and leaked encrypted vaults into
  `%TEMP%` on every run.** `TempVault.OpenRaw` builds `Mode=ReadWrite` over an arbitrary path and
  key — a backup file, a foreign vault — which is a *different connection string* and therefore a
  different pool from the one the teardown cleared. Measured by the review: nine leaked vault
  directories out of nineteen tests, where the process-wide call had left zero; reproduced here
  independently. **The first draft of this section presented exactly this as hypothetical** —
  "would still pass every test here… so it is recorded rather than guarded" — and took credit for
  foreseeing it while asserting it had not happened. It had.
- **Separating the in-memory pools ADDED a pooled resource to the one harness that had none, and
  never released it.** `Microsoft.Data.Sqlite` does not pool a private `":memory:"` database —
  closing the connection destroys it — but it *does* pool a URI-named one. So renaming the fixture's
  database to give it its own pool meant every fixture left a live, schema'd, encrypted database
  resident for the whole test process: this row's own defect shape, introduced while fixing it.
  Confirmed here before fixing: eleven tables still present after `Dispose`.
- **The guard was defeated by a one-line block comment, and its own comment denied it was possible.**
  The scanner strips comments before searching, because the fixed files *name* the method in their
  doc comments to explain the defect and a plain substring search would fail on exactly the files
  that are correct. On seeing `/*` it entered block-comment mode without checking for a `*/` later
  on the same line, so `/* TD-140 */ SqliteConnection.ClearAllPools();` switched scanning off for
  the rest of the file — the real defect, reintroduced, compiling, and green. The guard also scans
  `src/` now: production code runs *inside* the integration test process, so the original reasoning
  ("production has no parallel test classes") was wrong.
- **Two claims in the write-up were false and one measurement was of the wrong thing.** "Every
  `DatabaseFixture` shared one pool" was false twice over: `":memory:"` is not pooled, and exactly
  one test class takes that fixture — there was no cross-class sharing to suffer from. It was a
  latent hazard, worth removing, and it is described as latent now. A parenthetical "(measured: …)"
  in the fixture's own comment measured a *file* database and was used to support a claim about an
  in-memory one; it is gone. The claim that the failures "span both harnesses" is also gone: every
  test ever seen failing this way uses `TempVault`, and different *classes* running in parallel is
  all the mechanism needs.

**Chasing the leak the review found led to a defect in the product, which the harness had been
hiding for as long as it made the process-wide call.** `VaultRestoreService` opens a backup file to
verify it before restoring, and `await using` returns that connection to the pool rather than
closing it. On Windows a pooled handle is an open file handle, and `VaultBackupService.PruneAsync`
*deletes* backups — so a restore, **including a refused one**, could leave Pine unable to tidy up
its own backups for the rest of the process. It was invisible because the harness's
`ClearAllPools()` cleaned up after it on every run; scoping that call is what exposed it.
`ARestoreLeavesTheBackupFileDeletable` is the assertion, and it fails without the fix.

**And the last leaked directory was a test reaching around the harness.**
`AttachmentStorageTests.AReceipt_SurvivesABackup` opened the backup with `new PineDbContext(path,
key)` — a connection string the vault knows nothing about, so its pooled handle outlived the vault.
Rather than patch that one call site, `TempVault` now **tracks every connection string it hands
out** and empties each pool on disposal, offers `CreateContext(path)` for exactly this case, and —
the part that matters — **fails loudly instead of shrugging**: the delete loop used to give up
silently after five attempts, which is why nine encrypted vaults per run could accumulate without a
single red test. It now throws, naming the directory and the likely cause. That is the assertion
that would have caught the review's first finding on the run that introduced it. A full suite run
leaves **zero** temp vaults behind, measured, where the first version of this fix left nine.

**Verified by falsification: thirteen mutations, thirteen killed.** Four restore `ClearAllPools` to
each harness it was removed from; one hides it behind the block comment that beat the first guard;
one puts it in production code; three attack the fixture's database name; one removes the fixture's
pool release; one stops the vault tracking a raw connection's pool; one removes the production fix
so a restore locks the backup again; one has a test open a backup around the harness. **One kill has to be confirmed by hand, and how it dies
changed while this was being worked.** The mutation that makes the fixture open a real file first
failed cleanly on the assertion written for it and then *aborted the run*, so no `.trx` was written
and the harness read "zero results" as "nothing failed" — it now reports a resultless run as
inconclusive, because reading one as green is how a falsification pass lies to the person running it.
With the fixture releasing its pool, the same mutation no longer reaches the assertion at all: it
takes the test host down with a native access violation, `0xC0000005`. Either way the run is red, so
the mutation is killed, but it is killed by a crash rather than by a test — said plainly because
"thirteen killed" would otherwise imply thirteen assertions. **The crash is also a lead worth
recording**: `0xC0000005` under SQLCipher is TD-117's signature, and here it is reproducible on
demand from a keyed connection to a file-backed database opened through a URI. It is not claimed as
TD-117's cause.

**What is NOT closed by this.** The row is fixed **in the test harness**, and there is no evidence it
ever affected the product — the same mistake in production code was found and fixed in
`VaultRestoreService` while closing item 1.1. TD-117 is not claimed, though the same call was found
twice in the unit suite and is recorded as a lead. The probe that retired the row's stated hypothesis
(a connection held open across concurrent `ClearAllPools()` calls, which never failed) was a
throwaway and was deleted, so that step rests on a run nobody can repeat — which is why the
diagnosis is argued from the captured stack and the A/B arm instead. And the guard is a text scan:
string concatenation, reflection, or a helper outside `src/` and `tests/` all evade it. It raises the
cost of reintroducing the defect by accident, which is how it was introduced; it is not a proof that
the defect cannot return.


### 5.2 Item 2.2, as built (2026-09-12, iteration 45)

**The row asks for a certificate, and a commit cannot produce one.** TD-108's Fix column reads
"obtain a code-signing certificate, then add `--require-signature` to the CI job". The first clause
is a purchase against a verified legal identity, made by whoever owns this project; nothing in this
repository can stand in for it, and pretending otherwise would be the exact class of claim the audit
exists to remove. So the row stays **open**, and what follows is the other half — plus the state the
signing seam turned out to be in when someone finally ran it.

**The seam had never been executed, and it could not have worked.** The signing step had sat in
`packaging/package.sh` since iteration 20 — twenty-four iterations — behind a condition that was
never true, because no certificate has ever been configured. Running it for the first time on
2026-09-12, against a throwaway self-signed certificate, produced this:

```
$ signtool sign /fd SHA256 /f <cert> /p <password> Pine.Core.dll
SignTool Error: No file digest algorithm specified. Please specify the digest algorithm with the
/fd flag.
```

`/fd SHA256` is on that command line. **Git Bash rewrites an argument that looks like an absolute
POSIX path before the child process sees it, and `/fd` looks like one** — so does `/f`, `/p`, `/tr`
and `/td`. The identical command with `MSYS2_ARG_CONV_EXCL='*'` exported succeeds. The workflow's
`package` job runs on `windows-latest` with `shell: bash`, which is the same shell, so this was not
a local curiosity: **on the day a certificate arrived, the release build would have failed, and the
error would have accused the operator of omitting a flag they had supplied.** That is the finding.
Everything else in this item follows from it.

**What was built, and why each piece is separate.**

- **`packaging/sign.sh`** now holds the whole signing policy, and it does not only sign — it proves
  it signed. The property it asserts is not "the entry point carries a signature" but **"nothing in
  this artefact that Windows will load as code is unsigned"**, file by file, after the fact. It
  refuses: no certificate configured (exit 2, rather than quietly producing an unsigned build); no
  timestamp authority (exit 2 — an untimestamped Authenticode signature stops validating the day the
  certificate expires, which turns a shipped release into a SmartScreen warning on a date nobody
  wrote down); any file left unsigned; any file whose signature does not match its bytes; any
  certificate that does not chain to a trusted root; and **a run that signed nothing at all**, which
  is this repository's recurring failure shape rather than a hypothetical one.
- **It does not re-sign what is already validly signed.** Roughly nine tenths of the artefact is
  .NET runtime and third-party native code that Microsoft and others have already signed. Replacing
  those signatures with Pine's would destroy provenance a reader could otherwise check, cost one
  timestamp-authority round trip per file, and assert authorship of code this project did not write.
  Pine's own binaries arrive unsigned and are therefore signed. The property asserted afterwards is
  the same either way.
- **`packaging/authenticode-status.ps1`** exists because `signtool verify` cannot answer the
  question. Measured: an unsigned file and a file signed by an untrusted root both exit 1, differing
  only in the English sentence printed. A gate that greps an error message for the difference
  between "not signed at all" and "signed, chain not installed here" is a gate built on a string
  Microsoft is free to reword. `Get-AuthenticodeSignature` returns `NotSigned`, `HashMismatch`,
  `UnknownError` and `Valid` as typed values, so that script reports and `sign.sh` decides.
- **`packaging/find-signtool.sh`** derives the SDK path instead of hard-coding it, because a literal
  path works until the runner image rolls and then fails on a release day.
- **The manifest now says WHO.** `"signature"` was `null` in every case; it is now an object with
  the certificate's thumbprint and subject, how many files this build signed, how many carry a
  timestamp, and whether the chain is trusted. "Is it signed" is the less useful question; a
  thumbprint is what lets a downloader tell this project's artefact from a third party's. The
  subject is JSON-escaped, because a certificate subject contains double quotes as a matter of
  course (`CN="…"`) and interpolating one straight into the heredoc would have produced a manifest
  that is not JSON — on exactly the field a release client needs most.
- **The CI `package` job now treats the certificate and the flag as ONE decision.** A configured
  certificate means `--require-signature` is passed in the same branch. The accident TD-108's Fix
  column is guarding is not a forgotten certificate — that is loud — it is a certificate that is
  expired, wrongly encoded or password-mismatched, and a pipeline that shrugs and publishes an
  unsigned build under the release name. Without a certificate the job builds unsigned and says so
  in the log, in a receipt, in the manifest, **and in the name of the uploaded artefact**:
  `pine-win-x64-UNSIGNED`. That last one is the half of TD-108's *Impact* that can be closed without
  a purchase. A person who downloads a file called UNSIGNED has been told what they have; one who
  downloads `pine-win-x64` and meets SmartScreen has been misled by this pipeline.
- **The receipt and the manifest have to agree.** The verify step reads `"signed"` out of the
  manifest — which `package.sh` writes from `sign.sh`'s facts, not from its own hopes — and demands
  the matching receipt. Two statements, edited in two places, which is the same mechanism as every
  other gate here.

**`packaging/selftest-signing.sh` is the part that keeps this from rotting the way the last one
did.** It mints a throwaway self-signed certificate, copies binaries into a fixture shaped like a
real artefact, and runs the real script: no certificate purchase, no network, no 680 MB publish,
about seventeen seconds. **It runs in CI, in the `test` job, on every pull request.** A self-test
that lives in the repository and is never run would repeat TD-108's actual mistake one level up.
§5.1 has already recorded what the alternative costs: the probe that retired TD-140's stated
hypothesis was a throwaway and was deleted, so that step rests on a run nobody can repeat. This one
is committed.

**Thirteen cases, all passing.**

| Case | What it drives | Result |
|---|---|---|
| A | Signs what is unsigned, recurses into subdirectories, leaves a valid third-party signature alone | signs 2 of 3 |
| A' | The facts file names the certificate that actually signed | thumbprint matches |
| A2 | The facts file admits the certificate is not trusted here | `trusted_root=no` |
| A3 | The non-PE files in the stage were left alone | untouched |
| B | No certificate configured | refuses, exit 2 |
| C | A certificate but no timestamp authority | refuses, exit 2 |
| D | Verification meets an unsigned file | refuses, exit 1 |
| E | Verification meets bytes edited after signing | refuses, exit 1 |
| F | Verification meets an untrusted root | refuses, exit 1 |
| G | **Positive control** — an artefact signed properly throughout | passes |
| H | A stage whose only files are not loadable code | refuses, exit 1 |
| J | A run that would sign nothing at all | refuses, exit 1 |
| I | **The pre-fix command form**, kept on file | still fails, as measured |

Case G is there because a gate that says no to everything is indistinguishable from a broken one,
and case I because the defect this item found is worth keeping as a live measurement rather than as
a sentence in a comment: if it ever starts passing, the exported variable has stopped being
load-bearing and the explanation above has become fiction.

**Three refusals in `sign.sh` are NOT covered by any case, and the script says so where they are
written.** A file appearing in the stage between the two surveys, two certificates among the files
one run signed, and a timestamp authority that answers successfully while returning no
countersignature are all states the tool should never produce, and none is reachable from a fixture.
They are kept because the cost is three comparisons and the failure each describes is silent. They
are not claimed as tested, and deleting any of them leaves every case green.

**Six guard tests in `BuildHygieneTests`.** They cover what lives in YAML and cannot be run from
xUnit: that the MSYS export is still there, still ahead of the call, **and still set to the
wildcard**; that nothing in `packaging/` or `scripts/` but `sign.sh` invokes `signtool`, however the
call is spelled; that the invocation carrying `--require-signature` is the one *between* the
certificate assignment and the `else`; that the two artefact names are mapped to the right case
arms; that nothing in the release path sets either escape hatch; and that the self-test runs in the
job that fires on pull requests, with its failure fatal and its receipt demanded.

**The independent review found nine defects, three of them critical, and the first draft of this
section reported numbers that were measurements of the wrong thing.** Everything below came from
someone who had not written the code, and every item was reproduced here before being fixed:

- **The survey matched every file in the artefact, not `.exe` and `.dll`.**
  `Get-ChildItem -LiteralPath … -Include *.exe, *.dll` ignores `-Include` outright: it is a path
  filter, and with `-LiteralPath` it does nothing at all, silently. Measured against a directory
  holding only `a.txt`, `b.json`, `c.pdb` and `sub\d.dat`: all four came back. **So the first real
  signed release would have failed** — `signtool` would have been handed
  `Pine.Cli.runtimeconfig.json`, which a self-contained publish always emits, and answered "this
  file format cannot be signed". That is the same defect shape as the one this item exists to close:
  a signing path that cannot sign, discovered on release day. `--verify-only` also reported a `.txt`
  as "a loadable file, signed and matching its bytes". The filter is a `Where-Object` on the
  extension now.
- **The self-test could not see it, because every fixture was a flat directory of `.exe` copies.**
  A fixture that looks nothing like the artefact tests nothing about the artefact. Every stage now
  carries a `README.txt`, a `.runtimeconfig.json` and a nested subdirectory, and case A's "2 of 3"
  is load-bearing: a survey that matches every file says "5 of 6", and one that stops recursing says
  "1 of 2".
- **"Five mutations, all killed" measured the five mutations the author chose.** The reviewer wrote
  seven of their own and **all seven survived**. Four are now killed by new or strengthened cases —
  the run-that-signs-nothing refusal (case J, and §5.2 had singled that refusal out as the important
  one while no case drove it), the trusted-root fact (A2), the extension filter and the recursion
  (A, A3, H). The remaining three are the ones recorded above as not covered, in the script itself.
- **All six guard tests survived a mutation of the exact thing each guards**, applied together, 21/21
  green: `export MSYS2_ARG_CONV_EXCL=''` (set, before the call, exporting nothing);
  `$PINE_SIGNTOOL sign …` in a new script, unquoted, which the scan's two literal spellings missed;
  `--require-signature` moved to the *unsigned* branch of the same step, where it refuses every
  unsigned build and guards no signed one; **the two artefact names swapped between the case arms**,
  so every unsigned build uploads as `pine-win-x64` — the precise harm the test is named after;
  the escape hatch set in `package.sh` instead of the workflow; and `|| true` on the self-test. Each
  test now asserts the relationship rather than the presence of a word, and each mutation is killed
  by the test written for it.
- **The self-test did not run on pull requests.** It was in the `package` job, which carries
  `if: github.event_name != 'pull_request'` — while the step's own comment claimed it ran on every
  push "with no `if:`" and a guard test asserted the step carried no condition. Both were true of
  the step and false of the job. It is in the `test` job now, on the windows leg, with the condition
  restated in the verify step exactly as the coverage floor's is. `package` depends on `test`, so a
  broken signing path stops the artefact being built at all.
- **`61-Manual-Test-Plan` §13D step 4 was false.** It told a tester that `--require-signature`
  "refuses and exits non-zero, before producing anything", and the refusal sat after two
  self-contained publishes. `package.sh` now answers it in the first hundred milliseconds, before
  anything is built — measured at 0.077s — which makes the step true rather than making the sentence
  vaguer.
- **A false claim left standing elsewhere.** `60-Production-Readiness-Audit` §12.42 still described
  the old seam as a measured outcome: "with `PINE_SIGNTOOL`/`PINE_SIGNING_CERT` set, every `.exe`
  and `.dll` is signed". The adversarial re-read caught the CHANGELOG and the register row and
  missed this one. It now carries a correction, and the paragraph is left in place rather than
  rewritten, because it is the better record: it is what a careful write-up looks like when the code
  under it has never run.
- **The "two-statement agreement" between receipt and manifest was overstated.** Both are downstream
  of one condition — whether a certificate is configured — so there is no single edit that makes
  them disagree without failing the job for another reason. Unlike the coverage receipt, these are
  not independent. The comment says so now.
- **Three smaller ones, all fixed:** every survey line carried a trailing `\r` from PowerShell's
  CRLF into the paths handed to `signtool` (it worked because Windows tolerated it, which is not a
  thing to rely on); `--facts` with no value died on `set -u` instead of printing the usage sentence
  every other argument error prints; and `timestamped_files` was counted over every file carrying the
  thumbprint while being subtracted from the count of files this run signed, which could under-report
  and in principle go negative.

**What the review checked and found true:** the MSYS `/fd` measurement, the `signtool verify`
measurement, that third-party valid signatures are left alone, paths containing spaces, the
`json_escape` ordering, the CI `trap` firing, the base64 decode, that the `.pfx` cannot reach the
uploaded artefact, and that no temp file leaks from `sign.sh`. It found nothing in those categories.

**What is NOT closed by this, stated as plainly as the row deserves.**

- **TD-108 stays open, and its own status does not move.** There is no certificate. Every artefact
  this pipeline produces today is unsigned, SmartScreen will warn on first launch, and a user still
  cannot tell this build from one a third party modified. What changed is that the unsigned state is
  now labelled everywhere it is visible, and that the signed path has been executed and measured
  rather than assumed.
- **Nothing here proves anything about a real certificate.** The self-test's certificate is
  self-signed, so its signed cases run with `PINE_SIGNING_ACCEPT_UNTRUSTED_ROOT=1`. What a user's
  SmartScreen makes of a genuine Authenticode chain — and whether a new certificate accrues
  reputation fast enough to stop the warning at all — is outside anything this repository can
  measure.
- **The self-test has never run on a GitHub runner.** Like everything else in the CI arrangement, it
  is a YAML diff until a real run happens: `61-Manual-Test-Plan` §3.2. It carries a specific new
  risk of its own, recorded rather than discovered later — it needs a Windows SDK and
  `New-SelfSignedCertificate` on the runner image, and it fails rather than skips if there is
  neither.
- **Three of `sign.sh`'s refusals are unfalsifiable from a fixture** and are marked as such in the
  script. They are reasoning, not evidence.
- **The archive and the manifest are still unsigned.** Authenticode covers the `.exe` and `.dll`
  files inside the artefact. `SHA256SUMS` and the `.release.json` beside them are plain files that
  anyone can regenerate, so the per-file checksums detect corruption, not substitution. A detached
  signature over the archive is a separate piece of work and is not claimed.
- **The guard tests are text scans**, and say so in their own remarks. String concatenation, a
  variable holding the flag, or moving the work into a file nothing reads all evade them. They raise
  the cost of undoing this by accident, which is how it was broken in the first place — no test had
  to be defeated for the old seam to be wrong for twenty-four iterations, because nothing ran it.


### 5.3 Item 2.3, as built (2026-09-13, iteration 46)

**The decision: delete the `Category` aggregate, and let the label be the model.** TD-132's Fix
column offered two branches and forbade both "both" and "neither". The row had been deferred for
seventeen iterations, and every subsystem built in that time — splits, rules, budgets — had quietly
taken the second branch anyway, because it was the only one with instances. This item took it on
purpose.

**It was not a judgement call, in the end, because the canonical document had already made it.**
`10-Domain-Model` §6 lists the aggregates Pine is specified to have, and `Category` is not among
them. `Category` appears once, in §7, as a **value object**: "a hierarchical label (e.g.
`Food / Restaurants`)". `Transaction`'s key attributes list "category" as an attribute, and
`Budget`'s list "category (or set)". So the class in `src/Pine.Core/Categories/` — an
`AggregateRoot` with its own repository, command and table — was the thing disagreeing with the
specification. Building category management on top of it would have built the undocumented design;
deleting it brought the code to the documented one. That is the argument, and it does not depend on
anyone's taste.

**What the aggregate was actually doing, measured rather than assumed.** At HEAD nothing in
production writes it: `CreateCategoryCommand` has exactly one kind of caller, tests. No query lists
categories. No screen creates one. Every category input in the UI is a free `TextBox`. The foreign key
from `Budgets` is real — `PRAGMA foreign_keys` is on in both EF and raw connections — so a budget
cannot be written without a category row, and `CreateBudgetCommand` has no production caller either.
**Since `67af1f3`, no path in the application could create a budget at all.**

**The first draft of this section said "never", and that was false.** It said `Categories` was "empty
in every real vault" and that the budgets screen was "a list that could never have had anything in
it". The independent review found the counter-example in history: until `67af1f3` (TD-15), the
Diagnostics screen's **"Seed Demo Data / Test Features"** button created a real `Groceries`
category, coloured `#00FF00`, and a USD 500 monthly budget against it. Any vault where that button was
pressed holds exactly those rows — which is why the migration's name recovery matters, and why the
colour dot on that budget was, in those vaults, showing real data. The migration carries the name
across; the colour has nowhere to go and is lost. Said here rather than presented as though it never
existed.

**What changed.**

- **Deleted:** `Category`, `ICategoryRepository`, `SqliteCategoryRepository`, `CreateCategoryCommand`
  and its tests, the `Categories` DbSet and its mapping, and `CategoryParentIntegrityTests` — six
  tests about a parent-category foreign key on rows nothing ever created. `IBudgetRepository.GetByCategoryAsync`
  went too: it had no caller anywhere, and porting an unused method to a new signature is writing
  unused code with a fresh date on it.
- **`Budget.CategoryId` (Guid) became `Budget.Category` (string)**, the same label
  `TransactionSplit.Category`, `Rule.Category` and the categoriser use. The aggregate refuses a blank
  one — the old `Guid` accepted `Guid.Empty` and produced a budget that could never be measured — and
  stores it trimmed. **That is a rule about creation, not about what is on disk:** EF materialises a
  budget without its constructor, so the query also trims the label it reads and treats a blank one
  as unmeasurable rather than as "the category of every uncategorised split".
- **`GetBudgetProgressQuery` no longer joins through a table.** The "unknown category" branch is
  gone because there is nothing left to be unknown; the one remaining reason for an unknown spend is
  a window with no calendar anchor (a bi-weekly budget with no stored dates). One honest unknown
  instead of a universal one.
- **The budgets page and the dashboard tile lost their colour swatch.** The hex came from
  `Category.ColorHex`. Deriving one from the label — hashing it into a palette — would put a colour on
  screen that the user never chose and that changes when they rename the category, which is the
  shape of invention this audit exists to remove. The `Budgets.Category.Unresolved` string is gone
  from both languages for the same reason: it described a state that can no longer occur.
- **Two migrations, not one, and the split was measured into existence.**
  `DropCategoryAggregate` adds `Budgets.Category`, fills it from `Categories.Name` — **trimmed**, and
  falling back to the budget's own identifier when the row is missing or the name trims to nothing —
  and drops the foreign key and the old column; `DropCategoriesTable` drops the table. The shape was
  found by failing, not by reasoning:
  - **The tool emitted a bare `RenameColumn`**, which would have carried each budget's GUID across
    into the column a person now reads as a category name. The name was in the database.
  - **The first fix — an `UPDATE` writing the name into `CategoryId` — failed** with
    `SQLite Error 19: FOREIGN KEY constraint failed`: that column is still a foreign key at that
    moment. A separate column, filled first, never holds an invalid value.
  - **Dropping the table in the same migration also failed, with the same error.** EF folds every
    rebuild-triggering operation in a migration into one table rebuild emitted at the *end*, so a
    `DropTable` written last is still generated *before* the rebuild that removes the foreign key
    pointing at it, and SQLite's `DROP TABLE` performs an implicit delete the child rows refuse. A
    second migration is a second batch, and the order between batches is not EF's to rearrange.
  - **The rebuild also stripped column defaults.** `Budgets` is regenerated from the model, which does
    not carry the `defaultValue` that `AddConcurrencyTokens` and `AddRowAuditColumns` put on
    `Version`, `RowCreatedAtUtc` and `RowUpdatedAtUtc`. Production writes through EF, which supplies
    all three; a raw-SQL test helper did not, and failed with `NOT NULL constraint failed`. Recorded
    in that helper rather than papered over.

**Tests.** Five integration tests drive the real migrator from a legacy vault: three budgets over two
categories each come out named after **their own** category; a name with surrounding whitespace
arrives trimmed; a name that trims to nothing does not become an empty label; a budget pointing at a
category that does not exist **cannot be written in the first place**; and the table is actually
gone. `MappedEntitySurfaceTests` lists `Category` among the dropped entities and `Categories` among
the dropped tables, by name. Unit tests pin the refusal of a blank category, the trimming, a blank or
untrimmed label read back from a vault, and — replacing a test that pinned the opposite — that a
budget created the way a user would create one measures the ledger. The dashboard's budget tile is
now **rendered** in a test and must show the label; the budgets *page* view is still covered at
view-model level only.

**Coverage went under its floor, and the answer was tests, not a lower floor.** Deleting the
fully-covered `CreateCategoryCommand` shrank `Pine.Application`'s covered lines and took it from above
61% to 60.78%. The gate's own instruction is "raise the tests, not the floor", so the search was for
tests worth writing. It found `CreateRuleCommandValidator` with **zero covered lines** — the validator
that carries the rule engine's most consequential refusal: a rule with no conditions matches every
transaction, so an empty form saved as-is would re-file an entire import under one category. Eleven
tests now pin every refusal, by field, including an inclusive maximum and that all problems are
reported at once. `Pine.Application` is at 62%.

**The independent review found three major and five minor defects, and two of the majors were in the
evidence rather than the code.** Every one was reproduced here before being fixed:

- **MAJOR — migrated labels were not trimmed, so a budget could report a confident spend of 0.** The
  old `Category.Create` refused blank names but never trimmed, so `"  Groceries  "` was a writable
  legacy state. Copied verbatim, the review measured that budget loading untrimmed and reporting a
  KNOWN zero against a month holding a 40 EUR Groceries expense — a fabricated figure, the defect
  class this audit exists to remove, introduced by the item meant to make budgets measurable. The
  migration trims now, and the query trims its own side as well.
- **MAJOR — the name-recovery test could not detect a broken join.** It used one category and one
  budget, so a subquery returning *any* name returned the right one; the review replaced the join
  with `LIMIT 1` and the test stayed green. The claim that the recovery was "complete rather than
  best-effort" rested on a test that could not tell. Three budgets over two categories now.
- **MAJOR — "nothing ever wrote a category or a budget" was false**, as described above. It had been
  written into this section, STATUS, the audit, the migration, the DbContext and two older comments
  (`TrainingExample`, `CategorizeTransactionQuery`); all corrected.
- **MINOR — the table half of the dropped-entity guard checked `"Categorys"`.** It derived table names
  by appending an `s`, which is right for `Tag` and wrong for `Category`; the review stopped the table
  being dropped and that guard stayed green. Table names are written out now.
- **MINOR — nothing rendered the budget label.** Rebinding the row text to the limit in both budget
  views left every test green. The tile is rendered now; the page is not.
- **MINOR — the new validator tests missed a check that only looked at the first condition.** Added.
- **MINOR — `Down` of `DropCategoryAggregate` fails on any vault with a budget**, where the first draft
  said it "restores the schema". Every restored `CategoryId` is `Guid.Empty`, which the re-added
  foreign key refuses; it rolls back cleanly. The remark says so now. There is no way down for a vault
  with budgets.
- **MINOR — a blank label read from a vault was measured as a category**, counting uncategorised
  splits. The query refuses it now, and `10-Domain-Model` §7 no longer claims splits enforce a
  non-empty trimmed label: they enforce neither.
- **Stale text**: two widget comments still said "Unknown category" was rendered into rows, and
  `61-Manual-Test-Plan` still listed a zero-hardcoded spend and `Category 3f2a` names as known gaps.

**What the review checked and found true:** foreign keys are on in production and raw connections;
the hand-copied `DropCategoriesTable` designer is correct (`has-pending-model-changes` reports none,
and a scratch migration comes out empty); `10-Domain-Model` §6 has no `Category` aggregate and §7
lists it as a value object; seventeen iterations; the test counts; the coverage figures; several
budgets on one category, and on different ones, migrate correctly; `PRAGMA foreign_key_check` is
clean afterwards; the table rebuild keeps every `NOT NULL` and the primary key. It also noted that the
`DropCategoryAggregate` designer's target model omits a `Categories` table that still exists between
the two migrations — inaccurate, harmless at runtime, and left as scaffolded.

**Falsified: six mutations, six killed — and then eight more.** A budget accepting a blank category; a label stored
untrimmed; every budget reporting an unknown spend again; the validator no longer refusing an empty
condition list; the migration carrying the GUID across instead of recovering the name; and a
`Category` entity reappearing in the model. Each was killed by the test written for it. The last one
is killed loudly rather than precisely — a model change without a migration fails most of the
migration suite too — and the intended assertion is among the failures.

The eight after the review were the ones it had shown surviving, re-applied against the new tests:
a join ignoring `CategoryId`; no `TRIM`; no `NULLIF`; the table left undropped; the validator checking
only the first condition; the query no longer trimming its side; the query no longer refusing a blank
label; the tile binding the limit instead of the label. Each was killed by the test written for it.

**Measured totals:** unit 1150 → **1165**; integration 254 → **253** (−6
`CategoryParentIntegrityTests`, +5 migration tests); coverage gate green.

**What this closes, and what it does not.**

- **TD-132 is closed.** Not deferred, not narrowed: the aggregate is gone and the label is the model
  in every subsystem.
- **TD-134 is closed with it**, as moot. It recorded that deleting a category through EF silently
  promoted its children to root categories. There is no category entity, no hierarchy, and no delete
  path left for that behaviour to live in.
- **Category management is NOT built, and the hierarchy §7 describes does not exist.** The label is
  flat: "Food / Restaurants" is one string, not a parent and a child. Renaming a category everywhere,
  merging two spellings, and a colour per category are all unbuilt. If they are wanted, they are
  product work on top of the label — a rename that rewrites splits, rules and budgets together — and
  that is a better starting point than an aggregate nothing wrote to.
- **The budgets screen still cannot create a budget.** `CreateBudgetCommand` has no production
  caller. This item made a budget *measurable*; it did not make one *creatable*. That is TD-99's
  half, and it is now the only thing standing between the budgets screen and being useful.
- **A budget whose category row is missing, or whose name was blank, carries its GUID as its label.**
  The `COALESCE` keeps such a row rather than deleting a limit the user chose, writing an invented name
  into the data, or leaving an empty label that would match every uncategorised split. The blank case
  is tested; the missing-row case cannot be, because the schema refuses to let such a row be written.
- **A vault that pressed the old "Test Features" button loses that budget's `#00FF00` colour.** Its
  name survives.
- **There is no way down from `DropCategoryAggregate` for a vault that has budgets.**

### 5.4 Item 2.4, as built (2026-09-13, iteration 47)

**The decision TD-139's Fix column asked for, taken: the domain names the message, the presentation
layer words it.** `IUserFacingFailure` carried a finished English sentence, so `Pine.Core` and
`Pine.Application` — which have no string table — chose the wording of the screen, in English. It
carries `UserFacingMessage`s now: a key and the values the sentence needs. `UserFacingError` returns
those messages instead of a string, and a `LocalizedMessageSlot` words them in the active language and
words them again when the language changes. `Exception.Message` stays English, for the log.

**The row counted two sets. There were four, and a live defect underneath them.**

- **Counted, and converted:** the call sites' fallback sentences — the row said 16 call sites; counting `ObservedTask` and the
  failure policies too, they needed **36** distinct fallback messages — and the sentences of the
  exception types that opt in. `RequestValidationException`'s were really **33 validator sites** naming
  30 distinct messages across eight commands, and seven `VaultRestoreRefusedException` refusals written at their throw sites.
- **Not counted — the import review's reasons.** `ImportTransactionsCommand` wrote an English
  sentence into every `ImportReviewRow`, and the wizard showed it verbatim beside each row. Now
  `ImportReviewText`.
- **Not counted, and found only by the independent review — the live scanner's guidance.**
  `ReceiptDetector` in `Pine.Infrastructure` returned ten **Spanish** literals ("Acércate más a la
  factura") that the scanner put on screen whatever the language: an English screen showed Spanish,
  and "factura" broke the `41-Localization` §18 glossary besides. Now keys in `UserFacingText`.
- **TD-45's closing claim was false.** It recorded that "every status and error message in the view
  models" had been localised, and its guard read only `.axaml`. The master-password change flow, the
  strength meter on **both** Setup and Settings (six band names, two band sentences, seven pieces of
  advice, duplicated word for word and kept in step by a comment), the balance-check report, the lock
  notice, the import headline and the receipt-scan stage lines were all English literals in view
  models. They had to move with the rest, for the reason in the next paragraph.

**The defect, reproduced before anything was changed.** Every call site assigned the funnel's
returned string straight to its property — while a slot on the same property still held whatever key
it had shown before. On the add-transaction form: "Payee is required.", the user fills the payee in,
the save is refused with a concurrency conflict, the conflict sentence appears — and switching the
language to Spanish re-rendered the **stale** key over it. Measured: the screen read
**"El beneficiario es obligatorio."** in place of the conflict. So converting only the sentences was
never an option: any property with a slot has to receive *everything* through the slot, or a language
change resurrects the last key. That is why same-property literals moved too, and why
`LocalizedMessageSlot.SetRendered` — the escape hatch that made this possible — is deleted.

**What changed.**

- `UserFacingMessage` (key, arguments) in `Pine.Core.Common`; `IUserFacingFailure.UserFacingMessages`.
- **Three catalogs, one factory per key**: `UserFacingText` (Core/Infrastructure: vault locked,
  concurrency conflict, corrupted attachment, scanning unavailable, unsupported import format, seven
  restore refusals, ten scanner guidance lines), `ValidationText` (30 distinct validator messages, plus the empty-refusal one) and
  `ImportReviewText` (four row reasons). A factory's parameters are its template's placeholders.
- `ValidationError` gained a required third argument, so a validator cannot report a reason with no
  on-screen form. `ImportReviewRow.Reason` and `ReceiptAnalysisResult.StatusMessage` are messages.
- `UserFacingError.Describe/Report`, `ObservedTask` and `InitializationFailurePolicy` take a
  `UserFacingMessage` fallback and report message lists. An implementer returning an empty list or a
  null element gets the fallback rather than a blank error area.
- The slot gained `Set(UserFacingMessage)` and `Set(IReadOnlyList<UserFacingMessage>)` (joined by a
  space, in order) and renders a message passed **as an argument** in the same language — so "1
  transaction", "5 minutes" and the password band are worded, and pluralised, by the language of the
  sentence around them rather than frozen at the moment they were formatted.
- Seventeen view models and four views' code-behind route every failure through slots. Five view models
  gained slots and `IDisposable`; the four code-behinds call a new `ReportFailure` on their view model,
  because a view has no localizer. `PasswordFeedbackText` replaces the meter's duplicated literals.
- **130 keys** added to `en.json` and `es.json` (329 → 459), the Spanish held to §18's glossary.

**Three sentences were false as well as untranslated.** The unsupported-file message told users to
"choose a statement exported as a spreadsheet **or a PDF**" — PDF import was deleted in iteration 37
(`8b9a12e`). And the validator sentences put **entity identifiers on screen**: "Account
`3f2a…` was not found." and "Transaction `9c1e…` was not found." — the TD-24 rule broken by
messages that had opted in to being shown. The identifiers stay in the English `Message`, which is the
log's; they are not arguments of the user's half.

**`PineOptionsValidationException` came OFF the interface rather than being translated.** It is thrown
by `PineConfiguration.Bind` while `PineCompositionRoot.Build` is still building the container, before
any window, view model or localizer exists; the Windows and macOS heads write it to the crash log and
rethrow, and no path reaches `UserFacingError`. Its opt-in was a claim nothing exercised. Translating it
would have produced Spanish for a crash log. The review confirmed the reachability.

**Tests, and what each guards.**

- `FailuresFollowTheLanguageTests` — the stale-key defect above, a validator refusal on a Spanish
  screen, a fallback following a switch, an import row's reason, a balance report with its count, and
  the lock notice with a nested duration.
- `UserFacingCatalogTests` — by reflection, every catalog factory's key exists in every table with
  exactly as many placeholders as the factory has parameters; every translation uses its English
  original's placeholders; nothing below the UI constructs a message outside a catalog; every
  key-shaped literal in the presentation layer's C# is in the English table, and where the call is on
  the line, a template that takes arguments is handed some and one that takes none is handed none.
- `NoSentenceLiteralsInUiCodeTests` — the view models' half of `41-Localization` §14, which until now
  was executable only for markup: no English sentence literal in `Pine.Ui.Avalonia`'s C#, except six
  registered under TD-150 as a ratchet — and an entry whose sentence is gone fails the test too.
- `DisposingTheWizard_ReleasesItsReviewRows` — each row now listens to the process-lifetime localizer.
- `UserFacingErrorTests`, `ObservedTaskTests` and `LocalizedMessageSlotTests` rewritten against keys;
  integration tests assert keys and arguments, because that project has no string table.

**A flake this item nearly shipped.** Four view models first defaulted to `Localizer.Instance` when
constructed by hand. `TrExtensionTests` switches that shared instance to Spanish for a moment, and xUnit
runs classes in parallel — so tests asserting English failed in the full run and passed alone. A
hand-constructed view model gets a private English localizer now; the container still injects the
singleton, which the review verified for all four.

**The independent review found four major and seven minor problems.** Every one was reproduced before
it was fixed:

- **MAJOR — the scanner's Spanish guidance lines**, described above. The defect this item exists to
  remove, one layer further down than the row looked.
- **MAJOR — "constructed only in a catalog" could be walked around.** The guard matched
  `new UserFacingMessage(`; a fully qualified name and a target-typed `new("Validation.Void.ReasonRequird")`
  both put a mistyped key past all 1227 tests. Both forms are matched now.
- **MAJOR — the sentence guard skipped the one property it most needed to see.** It skipped any line
  containing `Message = "` (meant for `[LoggerMessage]`), so `ErrorMessage = "Your rule was saved
  successfully.";` — a new English sentence *and* the stale-key shape — passed. It also skipped any
  line containing `Log`, which includes `Login`, `Dialog` and `Catalog`. Both narrowed.
- **MAJOR — review-row disposal was untested.** A no-op `Dispose` in `ClearReviewRows` passed everything,
  leaking every row of every import. Tested now.
- **MINOR** — adding `{0}` to a template the UI passes no arguments to survived (the catalog check only
  covered catalogs; the call-site check above is the answer); a key typo in casing (`Rules.Failure.toggle`)
  survived, because the key regex required every segment capitalised while lookup is ordinal; the
  stale-key shape survived untouched in `UnlockViewModel`'s migration-failure path (unreachable today —
  the language can only be switched after unlock — converted anyway, taking two sentences off TD-150);
  "Se importaron 1 de 1 fila" does not agree in Spanish (now "1 de 1 fila importada en '…'"); the
  imported-row date was passed as an invariant `yyyy-MM-dd` string against the type's own remark
  (now a date, formatted in the active culture); a stale `cref` in `ObservedTask`; and **claim 8 was
  false** — see what this does not close.

**Falsified.** The review ran twenty mutations: fourteen killed, six survived, one survivor expected (an
English value in `es.json` — translation *quality* has no automated check, and that is stated rather
than papered over). The six survivors became the review's findings above; after the fixes they and a
seventh (a mistyped scanner key) were re-run: **seven of seven killed**, and the working tree's diff hash
was identical before and after. The original defect was reproduced by mutation too: restoring the
direct assignment turned the stale-key test red with "El beneficiario es obligatorio."

**Measured totals:** unit 1165 → **1238**; integration **253**, unchanged; Windows, macOS and CLI heads
build; coverage gate green.

**What this closes, and what it does not.**

- **TD-139 is closed.** Nothing below the UI writes a sentence for a person any more, and the guards
  make a new one a test failure rather than a review finding.
- **TD-150 is registered for what is still English on screen.** Six sentences the guard names (the
  scan screen's confidence hints, the diagnostics empty-log texts, the receipt date-ambiguity and
  duplicate warnings — the last two carry deliberate culture decisions that belong to TD-21), and the
  review's list of non-sentence text the guard's pattern cannot see: the unlock screen's title, the
  diagnostics status words, "Copied!", three file-picker titles, and raw enum names in the account-type,
  rule field/operator and adjustment-kind lists.
- **Translation quality is not tested.** A Spanish value replaced with English passes. The glossary is
  enforced by review, as §18 says.
- **Unverified, recorded:** the live scanner renders its slot on the capture thread and posts the result
  to the UI thread, so a frame rendered just before a language switch can post just after it and show
  the old language until the next frame. Not reproduced.
- **Not a result type.** Refusal is still an exception; this item changed what reaches a human, not
  how failure travels.

### 5.5 Item 2.5, as built (2026-09-14, iteration 48)

**Five rows the plan called "small, mechanical". Two of the five were not, and three of them were
hiding something the row did not say.** Four commits, one per row that changed code, and one
measurement for the row that could not; then a fifth commit for what the review pass found.

| Row | Outcome | Commit |
|---|---|---|
| TD-148 | ✅ Closed — and the guard written to close it found five more unsent requests | `3d4db1c` |
| TD-117 | ✅ Closed (the misspelled-key half; the abort half closed in iteration 31) | `6116b26` |
| TD-120 | ✅ Closed — the seam exists; choosing between cameras is TD-153 | `1efda4d` |
| TD-21 | ✅ Closed (the on-screen half the row's closure in iteration 18 never covered) | `a5277a0` |
| TD-138 | 🟡 Narrowed — the trimmer half measured, the device half still unanswerable here | — |
| Review pass | Four defects in this item's own code, fixed | see below |

#### TD-148 — `GetBudgetsQuery`, and the guard that found five more

`GetBudgetsQuery` is deleted. The row's Fix column was one type, but the class of defect is "a request
nobody sends", so the guard is `EveryRequestHasASenderTests`: every MediatR request in
`Pine.Application`, read by reflection, must be constructed somewhere in `src/` (comments stripped, a
fully-qualified name counts). Writing it found five more that no row named:

- **`RecordTransactionCommand` was sent only by the integration suite** — 22 call sites, and no product
  code. It was a second way of recording a transaction, without splits or rules, and the ledger's
  *rollback* and *cancellation* tests were written against it. So the atomicity that was proved was
  the atomicity of a path no user reaches; the path the add-transaction form and the receipt scanner
  take had its commit asserted (the currency tests check the unit-of-work log) but never a rollback.
  The command is deleted and the tests drive `AddTransactionCommand`. **Porting them was not a rename**:
  the deleted handler moved the balance *before* staging the row and the real one moves it *after*, so
  the ported fault hook saw the row but not yet the balance, and failed. The fault now fires on the
  account write, where both are in the database.
- **`CreateBudgetCommand` has no sender** — no screen creates a budget. The budgets page measures spend
  for budgets nothing in the product can make. Registered as **TD-152**; the manual test plan already
  said so under the closed TD-99, which is not where an open gap belongs.
- **`CreateImportProfileCommand`, `UpdateImportProfileCommand`, `GetImportProfilesQuery`** — import
  profiles are never saved; the wizard builds a throwaway profile per import. **TD-151.**

The four kept requests are an allow-list ratchet with the row that owns each; an entry that gains a
sender fails the test too. Falsified both ways (the deleted query restored; a sender added for an
allow-listed command), and the port by taking `ITransactionalRequest` off `AddTransactionCommand`: all
three ported atomicity tests die.

#### TD-117 — a misspelled key stops Pine

`PineConfigurationKeys` (in `Pine.Core`, beside `PineOptions.Validate`, whose sentences are the same
kind — for the crash log, not a screen) walks every configuration entry against the options tree,
read by reflection, and each unknown key joins the value bounds in the one
`PineOptionsValidationException` thrown at startup, with *"Did you mean …?"* when exactly one setting is
close. `Pine:Ai:ConfidenceThreshold` now names `Pine:Ai:CategorisationConfidenceThreshold`.

**The top level is not checked wholesale, and the reason was found by grepping, not by thinking.** The
`PINE_` environment prefix is shared with the packaging scripts (`PINE_SIGNTOOL`, `PINE_SIGNING_CERT`,
`PINE_TIMESTAMP_URL`), which reach the configuration root as `SIGNTOOL` and so on. Refusing every
unknown top-level key would have stopped Pine starting on any machine that has built a signed release.
Only a top-level key naming a Pine section is refused — `PINE_Ai__…`, the variable with `Pine__`
forgotten — and a test pins that the signing variables are left alone.

`packaging/appsettings.example.json` ships the "commented example file" the row asked for: every
setting at its default, with the JSON provider's comment support. Tests hold it to binding cleanly,
changing nothing, and naming every settable property — so a new setting that is not documented fails.

#### TD-120 — the camera seam

`ICameraSource` / `ICameraSession` / `ICameraFrame` in `Pine.Core.Ai`; `OpenCvCameraSource` in
Infrastructure owns the backend choice, the DirectShow fallback and the handle released between
attempts, and copies BGRA **row by row at each side's own stride** — the view model used to copy
`stride × height` in one block without comparing the bitmap's stride. `LiveScannerViewModel` asks for
frames and names no native type; `OpenCvAvaloniaBridge`, which nothing called, is deleted.

**What the seam bought beyond the architecture rule: the device handling became testable.** Every
release path the review of item 1.5 fixed had been fixed by reading, because testing it needed a
webcam. With a fake camera: stopping, disposing, a camera failing mid-read, a manual capture, and no
camera at all each release what they opened. The frame copy is tested against synthetic `Mat`s
(padded destination rows, grey frames, a destination too small), because no test may turn a real
camera on. `TheUiAssembly_ShouldNot_DependOnOpenCv` covers the whole assembly with no exemption — the
bridge lived in `Utils/`, outside the namespace the older rule scopes to — and checks the compiled
assembly's references too. Six mutations, six killed.

**Choosing between cameras is still impossible**: OpenCV cannot enumerate devices. The seam is where an
implementation that can would go. **TD-153.**

#### TD-21 — figures and dates in the language the user chose

TD-21 was closed in iteration 18 for receipt *parsing*. What remained, recorded in every "Next" list
since iteration 32, was the screen: the string table followed the chosen language and nothing else did.
`Money`, Avalonia's `StringFormat`, `NumericUpDown`, `DatePicker` and `DataGrid` cells formatted in the
operating system's culture, and two view patterns were hard-coded besides — so a Spanish screen on an
English machine read `1,500.00` and `Sep 14, 2026`, and an English screen on a Spanish machine put
Spanish month names into English sentences.

- **`Localizer.DisplayCulture`**: the chosen language, in the machine's region when that region speaks
  it. Spanish on an `es-EC` machine stays `es-EC`; Spanish on `en-US` becomes `es-ES`; English on
  `es-EC` becomes `en-US`. Following the region regardless of language is the state this row was in;
  following the language regardless of region would give an Ecuadorian who chose Spanish Spain's
  conventions for nothing.
- **`DisplayCultureFollower`** keeps the process culture equal to it, because most of what formats is
  not Pine's code. **Measured before it was done**: both suites were run with the process culture
  forced to `es-EC` — decimal comma, day-first — and passed in full (1258 unit, 252 integration),
  import, import keys, backups and migrations included. Attached only in `InitializeUi`, which no test
  calls, so a test switching the shared localizer does not switch the culture of every parallel test.
- **`Money` is `IFormattable`**, so a sentence writes its figure in the sentence's own culture — a
  composite format hands its provider only to an `IFormattable` argument.
- **`MMM dd, yyyy` and `yyyy-MM-dd`** in three views became the culture's long date without the weekday:
  *May 3, 2026*, *3 May 2026*, *3 de mayo de 2026*. **Not the short date**: `3/5/2026` is 3 May in Quito
  and 5 March in New York — the ambiguity this row was opened for, on the screen where a user checks what
  was imported.
- **The two TD-150 sentences parked on this row are keys**: the receipt's date-ambiguity warning and its
  duplicate warning. The duplicate warning had been invariant on purpose ("24.50 whatever the locale"),
  which, once the total above it followed the language, would have made it the one figure on its screen
  in a different notation. `61-Manual-Test-Plan` §13.8 said the opposite and is rewritten.
- **The Settings page re-words its backup list** on a language change. Every other page is rebuilt when
  next visited; this is the page the language is switched on.

Eleven mutations, eleven killed; the working tree's diff hash identical before and after.

#### TD-138 — measured on the trimmer, still owed a device

The row asks whether a trimmed AOT head keeps `SQLitePCLRaw.batteries_v2`, which `Microsoft.Data.Sqlite`
loads by name. No Android or iOS workload is installed here, but the trimmer those heads run is ILLink,
so the trimmer's half of the question can be asked on a desktop. A probe — `Microsoft.EntityFrameworkCore
.Sqlite.Core` 10.0.11 and `SQLitePCLRaw.bundle_e_sqlcipher` 2.1.11, self-contained, trimmed, win-x64 —
was published four ways: `TrimMode` partial and full, each with and without `Batteries_V2.Init()`.
**All four opened a `Password=` database, reported `cipher_version` 4.5.2, round-tripped a row, and
refused a wrong password.**

**The pass is not vacuous**: in full mode the trimmer cut `Microsoft.Data.Sqlite.dll` from 172 KB to
57 KB and `SQLitePCLRaw.core.dll` from 51 KB to 19 KB, so it really processed those assemblies — and kept
`batteries_v2`. The likely reason is in the DLL: it carries the assembly-qualified literal
`"SQLitePCL.Batteries_V2, SQLitePCLRaw.batteries_v2"`, the form of `Type.GetType` the trimmer resolves
statically. That mechanism is inferred from the literal and the result, not read from the IL.

**What it does not answer, and why the row stays open**: the Android and iOS heads add Mono/native AOT
and their own native SQLCipher packages (`SQLitePCLRaw.lib.e_sqlcipher.android`; on iOS, the `__Internal`
static-linking provider), and no machine here builds or runs them. The explicit `Init()` call stays: it
is now shown to be unnecessary for the trimmer, but it costs nothing and is still a static reference on a
toolchain nobody has measured. The probe lives in the session scratchpad and is not committed; it is
described here precisely enough to rebuild.

#### The review pass

**The independent review was commissioned and died on an API session limit** before reporting, the same
way item 1.4's did. What stands in its place is the author's own adversarial pass over the checklist
the review had been given, which found four defects in this item's own code — each reproduced first:

- **MAJOR — a value set on a section was accepted silently.** `"Pine:Ai": 5` or `PINE_Pine=5` names a key
  that exists — the section — so a check on names accepted it, and the binder ignored the value: TD-117's
  defect in a different shape. Refused now, naming a setting inside the section.
- **MAJOR — an ambiguous receipt date would have been read in the display language's convention.**
  `ReceiptDateParser` chooses day-first or month-first from the ambient culture, which the follower had
  just turned into "the chosen language". An Ecuadorian with an English screen would have had
  `05/03/2026` read as 5 March. A receipt is printed where the user shops, so the parser now reads
  `MachineCulture.RegionalFormat`, captured once before the follower runs. The test forces the current
  culture to the convention *opposite* the machine's, so it means the same thing on any machine; the
  mutation back to `CurrentCulture` kills it.
- **MINOR — an unconvertible value** (`"abc"` for a page size) escaped as the binder's own exception,
  alone, before the unknown-key and bounds checks could run. It is collected with the other problems now.
- **MINOR — the duplicate warning built its figure with `new Money` and `new CurrencyCode`**, which refuse
  an out-of-scale amount or a malformed stored code: a warning could have turned a save into a failure.
  It uses the non-throwing factories the budgets screen already uses. There is no test for this one: an
  out-of-scale amount cannot be built through the domain to reproduce it, which is also why it is minor.

**Unverified, reasoning only**: that a frame read from `VideoCapture` owns its pixels once `Read` returns,
so the session can be released while the capture loop still holds a frame. OpenCV's DirectShow backend
copies into the output `Mat`; no test can check it without a camera. `61-Manual-Test-Plan` §13B step 6.

**Measured totals:** unit 1238 → **1291**; integration 253 → **252** (one currency test deleted
with the command it tested); coverage gate green (74% overall, 90% Pine.Core); Windows, macOS and CLI heads build.

**What this does not close.**

- **TD-138's device half**, above. **TD-151**, **TD-152**, **TD-153**, registered.
- **TD-150** is down to four sentences and the non-sentence labels.
- **A page other than Settings keeps the notation it was drawn in** until it is next visited. No other
  page is visible while the language is switched, so this is recorded rather than engineered around.
- **Translation quality** is still unchecked by any test.

## 6. Stage 3 — Breadth: the features the documents promise

Only after Stages 0–2. Each of these is a subsystem, not a fix.

| # | Work | Depends on | State |
|---|---|---|---|
| 3.0 | ✅ **Done** (2026-09-14) — **A budget can be created** (added ahead of 3.1: a screen that could not do what it exists for) | TD-152 (closed), TD-149 (closed) | Found by item 2.5's sender guard. The independent review found the form's default made TD-149's UTC month visible, so it was closed here too. See §6.1 |
| 3.1 | 🟡 **Preconditions, local merge and conflicts screen built** (2026-09-14, §6.2 — TD-146 closed; 2026-09-18, §6.3 — the change set and the merge between two vault files, TD-144 and TD-145 narrowed further; 2026-09-18, §6.4 — a screen that lists recorded conflicts and resolves them; 2026-09-19, §6.5 — §5's envelope and the sync key, TD-154 registered). **Sync** | TD-144 (logical clock), TD-145 (tombstones), TD-146 (natural keys) | Its persistence preconditions were built in iteration 35 and its delivery mechanism was un-blocked in 36, when ADR-0021 moved `ISyncProvider` out of the withdrawn plugin SDK. The three rows above are what remain between this schema and a merge. **Do not start with the transport** |
| 3.2 | Rule engine, second half | TD-142 (retroactive application, with a preview), TD-143 (multi-condition UI) | The engine already evaluates up to eight conditions; the form writes one |
| 3.3 | Attachments: the manual path | TD-141 | FR-TXN-02 asks for attaching a document to a transaction by hand; only the scanner path exists |
| 3.4 | Notifications, forecasting, investments | — | Specified, unbuilt, and unblocked by nothing. Schedule deliberately or narrow the documents that promise them |

### 6.1 Item 3.0, as built (2026-09-14, iteration 49)

**TD-152 went ahead of 3.1 for the reason §9 gave: a screen that cannot do what it exists for.** The
budgets page measured spend against budgets, and nothing in the product could create one —
`CreateBudgetCommand` had no sender, which item 2.5's `EveryRequestHasASenderTests` found. On every real
vault the page was an empty list with no sentence explaining it.

**What was built.**

- **A "New budget" form on the budgets page**: category (the same free label a transaction's split
  carries — TD-132 made the label the model), limit, currency, period, and two dates when the period is
  custom.
  - **The currencies offered are those of the user's accounts**, and only codes the command accepts. A
    budget is measured only against splits in its own currency, so a budget in a currency no account
    holds could only ever report zero — a figure that looks measured and is not. With no accounts the
    form says so instead of opening.
  - **"Every two weeks" is not offered.** A fortnight has no calendar anchor, so a dateless bi-weekly
    budget can never resolve its window and would say "not known" for ever; one with dates is what
    "custom dates" already creates.
  - **The last day picked is inside the budget.** The model's window is half-open, so the page sends
    local midnight of the day *after* the last day. Sending the picked day would have silently dropped
    every custom budget's last day.
- **`CreateBudgetCommandValidator`**, which the command never had. Category required; limit greater than
  zero and within its currency's decimal places; ISO currency; custom and bi-weekly budgets need both
  dates, in order; a calendar budget may not carry one date alone; and no duplicate (category trimmed and
  case intact, currency, period, dates). Before, a blank category reached `Budget.Create`'s English
  `ArgumentException` inside an open database transaction.
- **Each row names its period**, and the over-limit warning no longer says "monthly" whatever the period.
- **The page shows its empty state and its load error**, both of which were silent.
- The sender guard's ratchet fired exactly as designed: `CreateBudgetCommand` came off its allow-list.

**The independent review found two major and ten minor problems, and two of its mutations survived the
suite the first commit claimed had killed twenty of twenty.** Every finding was reproduced by the review
before it was reported; the fixes are in the second commit.

- **MAJOR — the screen said "Nothing was saved" about a budget that was.** The reload after a create sat
  inside the create's `try`, so a failed reload was reported as a failed create; the list emptied, and a
  second attempt was refused as a duplicate. A reload failure is now reported as a load failure, after
  the form has closed.
- **MAJOR — the form's default made TD-149 visible, so TD-149 is closed here.** `Budget.ResolveWindow`
  built monthly, weekly and yearly windows in UTC. That was unreachable while no budget could be created,
  and became the default the moment one could: at 22:00 on 30 September an Ecuadorian's monthly budget
  measured October, so that evening's dinner counted as October and the rest of September vanished — and a
  *custom* budget over the same September, which uses local midnight, showed a different total on the same
  page. The window is now the user's calendar period, each edge at the offset in force at that edge (a
  month across a daylight-saving change starts at +01:00 and ends at +02:00), the rule
  `GetMonthlyCashFlowQueryHandler.CurrentLocalMonth` already applied. `ResolveWindow` takes the zone, so a
  caller cannot fall back to UTC by omission. The test that had pinned the defect — `TheWindowIsComputedInUtc`
  — is replaced by ones in Ecuador, Madrid across a DST change, and Tokyo at New Year, and the query is
  tested on a clock whose zone is not UTC, which no test had been.
- **MINOR, and the two survivors.** Nothing tested that the page's custom dates are *local* midnight (the
  test compared the clock reading, not the offset; building them at UTC passed everything); nothing
  tested that the page announces it is empty after a load that ends with nothing (removing that
  notification passed everything). Both are tested now, and a custom budget is measured end to end
  through the real query with transactions at 23:30 the evening before, 00:30 on the first day, 23:30 on
  the last and 00:30 the morning after — only the middle two count.
- **MINOR** — clearing the limit box silently kept the last number (bound as a non-nullable `decimal`;
  now nullable and sent as zero, which the validator refuses); no accessible names on the new inputs and no
  live region on the refusal (added, matching `RulesView` and `ScanReceiptView`); a language switch left
  rows and the period picker in the old language (re-worded, the choice kept); a calendar budget sent with
  one date was accepted and the date silently ignored (refused); an account in a stored non-ISO code offered
  a currency every save would refuse (filtered); the empty sentence sat under an open form (hidden); the
  Spanish bi-weekly sentence said *quincena*, which in Latin American usage is half a month, not fourteen
  days (reworded); the form's title and its button both read "New budget" (the title is now "Budget
  details").
- **MINOR — the rendering tests' comment misdiagnosed a harness limit.** The windows are hidden rather than
  closed, and the comment blamed closing; the review showed that *any* change that re-applies a theme
  transition throws, because the render clock belongs to a thread other than the headless session's. So a
  rendered test here cannot exercise save-then-close or reopening the form; the view-model tests cover
  those. The full unit suite passed twice in a row with the hidden windows, so they do not leak into other
  tests.

**Falsified.** Twenty mutations on the first commit, all killed — and the review's two that were not.
Sixteen on the second, including those two: sixteen killed. The first run of the second set missed one —
passing UTC from the query instead of the clock's zone survived, because every query test ran on a UTC
clock — which is what the Ecuador query test was added for.

**Measured totals:** unit 1291 → **1342**; integration 252 → **256**; coverage gate green (overall 75%,
`Pine.Ui.Avalonia` 49% → 51%); Windows, macOS and CLI heads build.

**What this does not close, recorded rather than fixed.**

- **Creating a budget broadcasts no `DataChangedMessage`.** It is the only write path that does not. No
  screen goes stale today — the shell rebuilds a page on navigation and only one is alive — and the page
  receiving its own broadcast would reload twice concurrently. Worth doing when a second live listener
  exists.
- **A monthly budget with explicit dates and a custom budget with the same dates are the same window and
  are not refused as duplicates**, because the period differs. The page never sends the first shape.
- **No budget can be edited or deleted** from the screen. Nothing sends an update or delete command because
  none exists; that is the next half of the budgets story, not this one.
- **Nothing here has been drawn on a real screen**; `61-Manual-Test-Plan` §13F.

### 6.2 Item 3.1a, as built (2026-09-14, iteration 50): what a merge will need, before any merge

**The row said "do not start with the transport", and nothing here goes near one.** It names three
preconditions — TD-144, TD-145, TD-146 — and this is those three, built into the store so that every row
written from now on carries what a merge will need. No envelope, no provider, no network: `NetworkEgressTests`
is untouched.

| Row | Outcome |
|---|---|
| TD-144 | 🟡 **Narrowed** — every written root carries `(RowClock, RowDeviceId)`; the merge-side half of a Lamport clock, and restore, remain |
| TD-145 | 🟡 **Narrowed** — deletes leave tombstones; no retention policy and no tombstones for owned children |
| TD-146 | ✅ **Closed** — every aggregate has a written, tested natural-key decision |

#### TD-144 — which device, and when in logical time

- **A device id per installation**, in Pine's data directory (`device-id`), not in the vault file — the vault
  file is exactly what travels between machines, and an id inside it would be shared by every machine it
  reached. Created once, with a flushed temporary file moved into place without overwriting, so racing
  processes agree on one id; a damaged file is **refused, never silently regenerated**, because a new id
  would make this installation a second device to every vault it has written.
- **A counter in the vault**, one `SyncClock` row. Every save that writes or deletes a root takes one number
  and stamps it, with the device, on every root it writes: shadow columns `RowClock` and `RowDeviceId` beside
  the row version, which still answers a different question. Rows from before the migration read 0 and the
  empty id — "never stamped" and "not known" — for the reason the audit columns read `0001-01-01`.
- The container passes the installation's id; a context built without one stamps the empty id rather than
  borrowing one.

#### TD-145 — a delete leaves a tombstone, and nothing else

The row is still physically deleted. A `Tombstones` row keeps the type, the id, the clock, the device and the
time — and a test pins its columns, so it cannot grow a copy of what the user deleted. The alternative, an
`IsDeleted` flag with a query filter on every table, would have kept every deleted payee and note in the
vault for ever, readable by anything that forgot the filter; the register itself argued a hard delete is
what a privacy-first ledger should mean by the word.

#### TD-146 — a natural key, decided per aggregate

`NaturalKeys` writes the decision down where a test holds it, and does not manufacture a key from a name a
user can edit, as the register required:

| Aggregate | Decision | Key |
|---|---|---|
| Setting | Derived | its key — chosen by code, unique in the schema |
| Budget | Derived | category, currency, period, dates — **the same tuple the create validator refuses a duplicate on, now computed by the same function** |
| Attachment | Derived | its transaction and its content hash |
| Transaction | Partial | its account and its import key, when it has one; none when typed by hand |
| Account, Rule, ImportProfile | None | a merge must ask (§4's third layer) |

A test reads the real model and fails for any mapped aggregate without a decision.

#### The independent review

**It found two major problems and six minor ones, and four of its mutations survived.** Every finding was
reproduced with a probe or a mutation before it was reported.

- **MAJOR — the transaction key was not a key.** Most import keys leave the account out (the import's own
  duplicate check is per account for that reason), so the same CSV row imported into two accounts — two
  legitimate transactions — got one natural key, and a merge keyed on it would have collapsed them. The key
  is `transaction|{account}|{import key}` now, and the decision table says why.
- **MAJOR — the race test raced nothing.** Each writer spent ~150 ms opening its SQLCipher connection, so the
  twelve saves finished one after another, and a counter read and written in two statements passed three runs
  out of three. The connections are opened before a barrier now, and a deterministic test holds one writer's
  lock while another waits. **Then a further measurement corrected the review and the first write-up alike**:
  once every save runs in a transaction (next item), Microsoft.Data.Sqlite begins it `IMMEDIATE`, which takes
  the write lock before the counter is read — so uniqueness comes from the lock, and the single `UPDATE …
  RETURNING` statement is defence in depth. The two-statement mutation is equivalent under that design, and
  the code's comment now says the lock, not the statement, is what guarantees it.
- **MAJOR, recorded rather than fixed — this is half a Lamport clock.** There is no receive step
  (`max(local, remote) + 1`); it belongs to the merge, and until one exists two vaults' clocks cannot be
  compared. **Restoring an older copy of the vault file puts the counter back**, so the next write can reuse a
  `(clock, device)` pair a discarded change already carried; migrating down and up reseeds it. A
  per-installation high-water mark beside the device id is the likely remedy and the merge's to build, because
  the merge is what would be deceived. **TD-144 stays open for these**, which is why it is narrowed and not
  closed.
- **MINOR, fixed** — the clock was taken outside the save's transaction when the caller opened none, so commit
  order and clock order could disagree and a failed save still spent its number (every save that writes now
  runs in a transaction, opened by the context if the caller did not); the composition-root unit tests wrote a
  real `device-id` into the developer's `%LOCALAPPDATA%\Pine` (they build against a temporary data directory
  now); the device id's `Lazy` cached a refusal for the life of the process, so deleting the damaged file as
  instructed did not help until a restart (`PublicationOnly` now, and the file is flushed before the move);
  the synchronous save, the tombstone's time, a no-op save, the budget key's start date and the container
  honouring the configured data directory were each untested — each mutation passed everything — and each is
  tested now.
- **MINOR, recorded** — **no retention policy** for tombstones (TD-145's Fix column asks for one; a retention
  without a sync cursor would be a guess about when every device has seen a deletion, so it waits for the
  cursor); **removing a split leaves no tombstone**, and §7 merges splits by set union, so a merge would need
  child tombstones — pinned by a test that says so; SQLite without `secure_delete` keeps deleted rows in free
  pages until they are reused, readable with the key, which qualifies "a hard delete is still a hard delete";
  and `Budget.UpdateLimit` exists, can change the currency, and has no caller — if one is added, the budget's
  key moves with it.

**Falsified.** Eighteen mutations on the first commit: fifteen killed, three shown equivalent and each replaced
by one that was killed. The review then ran eleven of its own, four of which survived. Nine on the fixes,
including those four: eight killed, and one — the review's two-statement counter — shown equivalent by the
`IMMEDIATE` transaction as described above.

**Measured totals:** unit 1342 → **1359**; integration 256 → **270**; coverage gate green (overall 75% → 76%,
`Pine.Infrastructure` 83% → 84%); Windows, macOS and CLI heads build.

**What this does not close.** TD-144's receive step and restore regression; TD-145's retention and child
tombstones; everything after the preconditions — the envelope, the provider, the offline queue, the conflict
matrix of §7 — which is the rest of item 3.1.

### 6.3 Item 3.1b, as built (2026-09-18, iteration 51): the change set and the merge, locally, between two vault files

**Still no transport.** Both vaults are open in one process; nothing is sent, received, encrypted or written to
disk as a change set. `NetworkEgressTests` is untouched and green. What exists now is the part a transport would
carry things *between*: an export, an apply, and the rules §4, §6 and §7 set for what an apply may do.

| Row | Outcome |
|---|---|
| TD-144 | 🟡 **Narrowed further** — the receive step and the restore high-water mark are built; a rewind by any path other than the restore service, and byte copies of a vault, remain |
| TD-145 | 🟡 **Narrowed further** — tombstones travel and are applied (or refused as a conflict); child tombstones are no longer needed by this merge; retention and `secure_delete` remain |

#### What was built

- **`VaultMerger`** (`src/Pine.Infrastructure/Sync/`): `ExportAsync` cuts a `SyncChangeSet` — the `pine.sync.v1`
  document, every root row column by column with its owned children, plus tombstones, ordered by `(clock,
  device)` then table and id so two exports of one state are the same bytes; `ApplyAsync` applies one in **one
  transaction per vault**; `MergeAsync` does both ways with both write locks held from the first export to the
  last commit. Rows, not aggregates: the domain types have no public way to be set from outside, and a merge
  must not invent one, so a change set from a different schema version is refused.
- **Bookkeeping** (migration `AddSyncMergeState`, with `Down`): a vault id on the `SyncClock` row (a vault, not a
  device, is what a merge meets — two vault files on one machine share a device id); `SyncPeers` (a cursor per
  peer vault); `SyncConflicts` (the remote change as it arrived, unique per peer, entity, clock and device, so
  merging the same change set twice records it once); `SyncIdAliases` (natural-key matches). And a second
  number on every root and tombstone, **`RowLocalClock`** — this vault's clock when the row was last written
  *here*. An applied row keeps the `(RowClock, RowDeviceId)` of the vault that wrote it, so a third vault orders
  it faithfully; but a relayed row's Lamport stamp can sit below a peer's cursor, and cut by `RowClock` it would
  never travel on. The cut reads `RowLocalClock`. Existing rows are backfilled with their own clock.
- **"Changed on both sides" is decided by what the peer had seen**, carried in the change set as
  `SourceKnowsTargetUpTo`, not by when this vault last merged: a row edited here just before a merge that only
  *pulled* is older than that merge, the peer never saw it, and the tempting rule would let the peer overwrite it.
- **§7, as an allow-list** (`SyncMergePolicy`, pure and unit-tested). Last-writer-wins by `(clock, device)` —
  never wall-clock time — only for account name, currency, opening balance and a setting's value; ties broken
  by vault id, the same way round on both sides. **Every other difference is a conflict**, including a column
  added later that nobody classified. Consent is never granted by a peer. Balances are **re-derived** from the
  merged ledger (opening balance plus transactions in force), so spends on both sides both count.
- **A deliberate departure from §7: splits are not set-unioned.** A transaction's splits must sum to its amount;
  the union of two valid split sets is in general not one, and a union would also resurrect removed splits.
  Owned children travel as a whole set with their root, and a both-sides difference in them is a conflict.
- **Natural keys (TD-146) at work**: a row absent here whose key matches exactly one local row is that row — an
  alias is recorded and the peer's later edits, deletes and references land on the local id; matching two is
  `AmbiguousNaturalKey`, never a guess. Nothing is renamed, because a primary key change would cascade through
  restrictive foreign keys.
- **The clock's receive step (TD-144)**: before anything is written, the target's counter is lifted to at least
  the highest clock in the change set, inside the apply's transaction; the merge's own writes and every later
  save take a number above it.
- **Restore no longer rewinds the clock (TD-144)**: `VaultRestoreService` records the discarded file's counter in
  `sync-clock-floor` beside the database before replacing it — written to a neighbour and moved into place,
  never lowering an earlier floor — and every increment takes `MAX(Value, floor) + 1`. A damaged floor refuses
  writes rather than reading as zero. It sits beside the database rather than beside the device id, as the 3.1a
  review suggested, because a vault directory is what a restore replaces and the device id is shared by every
  vault on the installation.
- **Refusals, before anything is written**: a change set for another vault, from this vault or from no vault,
  from another schema version, starting after this vault's cursor (it would skip changes), in another format,
  naming a table or column this vault does not have — and, as defence in depth, **any table or column name not
  shaped `^[A-Za-z_][A-Za-z0-9_]*$`**, checked where each name is interpolated into SQL as well as up front; a
  byte copy of a vault merged with its original (both claim one vault id); a context with unsaved changes or an
  open transaction.

| Situation | What the merge does |
|---|---|
| Row only on one side | Inserted on the other, keeping its writer's stamp |
| Changed only on the peer since it saw ours | Peer's version taken |
| Changed on both, LWW columns only (account name, currency, opening balance; setting value) | Later `(clock, device)` wins, on both sides alike |
| Changed on both, anything else (amount, payee, date, splits, budget, rule, …) | `ConcurrentEdit` conflict; neither side overwritten |
| Peer deleted, not changed here since | Deleted (children first); tombstone kept so the deletion travels on |
| Peer deleted, changed here since | `RemoteDeleteOfLocalEdit` |
| Peer edited, deleted here | `RemoteEditOfLocalDelete` — never resurrected |
| Peer deleted, rows here still point at it | `DeleteBlockedByDependents` |
| Row points at something not here | `MissingParent` |
| Natural key matches one local row / two | Alias recorded / `AmbiguousNaturalKey` |
| Setting would grant a consent | `ConsentNotImplicitlyGranted` |
| Same change set applied twice | Nothing the second time; no duplicate conflict |
| Apply fails midway | Nothing kept — rows, conflicts, lifted clock, cursor |

**Falsified.** Twenty-seven mutations on the first pass, each applied by a script, built, run against the sync
tests and reverted: twenty-five killed, two survived — an applied row taking this vault's clock instead of its
writer's (no test compared the two stamps), and removing `MergeAsync`'s byte-copy check (`ApplyAsync`'s own
"source is this vault" refusal still threw the same exception type). Both were answered with assertions — the
stamps are compared, and the refusal must name the byte copy — and then killed. Four more on the identifier
guard (the pattern accepting anything, in the unit and the integration suite; the up-front table check and the
up-front column check removed): all killed. **Thirty-one run, thirty-one killed in the end; none shown
equivalent.** Every mutated line was confirmed restored afterwards.

**Measured totals:** unit 1359 → **1406**; integration 270 → **292**; coverage gate green (overall 76.0% →
77.7%, `Pine.Infrastructure` 84.3% → 85.8% — measured on both commits). **Before one more unit test, the merge
had dropped `Pine.Infrastructure` to 82.4%, 0.4 above its floor**: the merge's thousand lines were exercised only
by the integration suite, which the gate does not measure. One round trip through two real vaults in the unit
suite (`VaultMergerRoundTripTests`) answered it with a test, not a lower floor. Windows, macOS and CLI heads build.

**What this does not close.** §5's envelope — a change set is plaintext and in memory only; the provider, the
transport and the offline queue (§8, §9); **any screen**: conflicts are recorded in `SyncConflicts` and nothing
shows or resolves them yet, and nothing in the application calls the merge. TD-144: a rewind by copying an old
`pine.db` over the vault by hand, or by migrating down past the clock table and up (which now also forgets the
vault id and the peer cursors); and a byte copy of a vault carries its id, so a third vault that merges with both
cannot tell them apart. TD-145: tombstone retention (a cursor per peer exists now, but a retention is a claim
about every peer, including ones never met) and `secure_delete`. Rows from before `AddSyncClockDeviceAndTombstones`
carry clock 0 and the empty device, so between two upgraded vaults their "both changed" order falls to the vault
id — deterministic, not meaningful.

#### The independent review

**It found two major problems, both data loss through the natural key, and five mutations survived the suite.**
Every finding was reproduced with a probe test that failed on the reviewed commit before it was reported, and each
probe is now a regression test that passes.

- **MAJOR, fixed — a row deleted and created again with the same key vanished from the other vault.** Deleting a
  budget (or a setting) and creating the same one again puts the new row *and* the old row's tombstone in one change
  set. Rows are applied before tombstones, so the new row found the old one by natural key, was folded into it
  under an alias, and the tombstone then deleted it: the other vault ended with **no budget at all** (probe: 0 rows
  where 1 was expected; the setting variant likewise lacked the new row). The apply now asks whether the one match is a
  row this same change set deletes; if so it applies that deletion first, and the new row is inserted as itself. If
  the deletion is refused (changed here, or still pointed at), the old row stays and is matched as before, so a
  setting's unique key is never inserted twice. A tombstone applied early is not applied, or counted, again.
- **MAJOR, fixed — two rows sharing a key on the sending side were collapsed into one.** The receipt flow lets a
  person save the same receipt twice on purpose (`CheckReceiptDuplicateQuery` warns and lets them go on), and the
  `(AccountId, ImportKey)` index is deliberately not unique, so one vault can hold two transactions with one natural
  key. The second was matched to the first on the other side: **one transaction instead of two** (probe: 1 row where
  2 were expected; the tests now also hold the two vaults' balances equal). This happened whether both came in one merge or
  the second came a merge later. A key two rows in the source share now identifies neither: the export sends no key
  for such a row (`SyncChange.NaturalKey` is null), and the apply matches by a key only when one is sent. The key
  still does its job when it is unique on the sending side — the same import made on both devices is one
  transaction, and a test holds that too.
- **Five tests that were not there, found by mutation.** Refusing a busy *right* context was never tried (only the
  left); a change to a transaction's splits alone — the children, not the root — was never merged, so treating
  children as never different passed everything; the balance of an account with no opening balance (TD-02) being
  moved by the change in its ledger was never checked, so leaving it unmoved passed; a tombstone kept for a row this
  vault never had — the thing that stops a third vault resurrecting it — could be dropped without a failure; and the
  early-tombstone guard added above survived until the counts were asserted. Each has a test now, and each mutation
  was re-run and killed.
- **Recorded, not reproduced — the early deletion can still be blocked.** A transaction deleted and re-imported
  with its attachments deleted too would have its early deletion refused (the attachments' tombstones come later in
  the tombstone pass), fall back to the match, and record `DeleteBlockedByDependents`: a conflict for a person, not
  a loss. No command deletes a transaction today, so this is by reading only.

**Read and found sound, without a finding:** "changed on both sides" against `SourceKnowsTargetUpTo` (including after
a restore, which the floor protects, and after a left-side failure in `MergeAsync`); the receive step; the cursor
arithmetic in `MergeAsync`; ledger sums as scaled integers with voided transactions excluded; every identifier
interpolated into SQL passing `Ident`, and every value a parameter; the migration's `Up`, backfill and `Down`.

**Falsified.** Twenty mutations, each applied by a script, built, run against the merge's integration tests and
reverted: fifteen killed first time (among them removing the receive step, ignoring the floor, counting voided
transactions, cutting the export by `RowClock`, resurrecting a remote edit of a local delete, guessing an ambiguous
key, and both fixes above removed or weakened), five survived — the five tests above — and all five were killed
after. None shown equivalent. The script's own first run wrote a byte-order mark into two untouched files on
restore (no content change); it was caught by `git status` and the files reverted to `HEAD` before the suites below
were run.

**Measured totals (after the review):** unit **1406** (unchanged: the review's tests are integration tests);
integration 292 → **300**; coverage gate green (overall 77.7%, `Pine.Infrastructure` 85.8% — unchanged from the
first commit); Windows, macOS and CLI heads build with no warnings.

### 6.4 Item 3.1c, as built (2026-09-18, iteration 52): a screen that lists recorded conflicts and resolves them

**Still no transport, and still nothing in the application runs a merge.** What 3.1b recorded in `SyncConflicts`
can now be read and decided: a page lists every open conflict, and each can be settled one way or the other through
the merge's own write path. On a real vault the page is empty and says what it is for — the only merges are still
the ones the tests run between two vault files. `NetworkEgressTests` is untouched and green.

| Row | Outcome |
|---|---|
| TD-144 | Unchanged — a resolution takes its number from the same clock, floor included |
| TD-145 | Unchanged — a restore removes this vault's tombstones for the row it brings back; retention and `secure_delete` remain |

#### What was built

- **A port and two requests.** `ISyncConflictStore` (`Pine.Core.Sync`, beside the conflict kind, which moved there
  from `Pine.Infrastructure` so the application can name it); `GetSyncConflictsQuery` and
  `ResolveSyncConflictCommand(ConflictId, Resolution, ConfirmConsentGrant)`, transactional, with a validator. Both
  have a sender (the page), so item 2.5's `EveryRequestHasASenderTests` stays green without an allow-list entry.
- **The store is the merge.** `SqliteSyncConflictStore` delegates to a new part of `VaultMerger`
  (`VaultMerger.Conflicts.cs`) that builds the merge's own `ApplyRun`, pointed at the peer the conflict came from:
  ids translated through the same aliases, every table and column name checked against this vault and against the
  identifier shape, every value a parameter, one transaction (the request's, or its own), balances re-derived from
  the ledger. The write halves of the merge's insert, update and delete were extracted into methods both paths
  call; nothing was copied. Reading a conflict writes nothing — not even a clock number (tested).
- **A resolution is a change this vault makes, so it is stamped by this vault**: a fresh number from its clock
  and this installation's device, not the peer's stamp from the payload. That is what makes it travel.
- **What each decision writes.**

| Kind | Keep this device's side | Take the other device's side |
|---|---|---|
| `ConcurrentEdit` | The row stamped again, unchanged; it replaces the peer's on the next merge | The peer's row written here, stamped here |
| `RemoteDeleteOfLocalEdit` | The row stamped again; the peer, which holds a tombstone, is asked whether to restore it | Deleted here, children first, with this vault's tombstone |
| `RemoteEditOfLocalDelete` | A fresh tombstone; the peer deletes it on the next merge | Restored: the peer's row inserted, this vault's tombstones for it removed |
| `DeleteBlockedByDependents` | The row stamped again (as above) | **Not offered**: rows here still point at it |
| `MissingParent` | Nothing written here; the peer keeps its row | **Offered only once the parent is here** (checked each time the list is read, and again inside the transaction) |
| `AmbiguousNaturalKey` | Nothing written here; the peer keeps its row | **Never offered**: it would be a guess |
| `ConsentNotImplicitlyGranted` | **Nothing written**, on either side: withdrawing the peer's consent is not what "keep mine" asked for | Only with `ConfirmConsentGrant`; the page's button stays disabled until a box that says so is ticked |

  Taking the peer's row is also refused when a row with its natural key was created here since (it would be a
  duplicate, or for a setting a unique-key failure) — `SyncConflictBlock.AlreadyExistsHere`.
- **Resolved, not deleted** (migration `AddSyncConflictResolution`, generated, with `Down`): `ResolvedAtUtc` and
  `Resolution` on `SyncConflicts`. The row's unique index is what stops the same change set applied again from
  recording the same conflict again; deleting a decided conflict would bring it back.
- **The resolution closes the peer's record of the same conflict** — the one change to 3.1b's apply. When a merge
  applies a change to a row, it marks resolved (`Superseded`) every open conflict on that row from the same peer
  whose `(RemoteClock, RemoteDeviceId)` is *earlier* than the change's. A later change from the peer is its newer
  word on the row — its person's decision, or an edit since — and offering the old version would bring back one
  nobody holds. The same change applied twice carries the same stamp, which is not later, so idempotence stands
  (tested, and mutation M1 below is the reason it is tested). `SyncApplyResult.ConflictsSuperseded` counts them.
- **A restore still needs a person on the vault that deleted.** Keeping a row the peer deleted stamps it; on
  arrival the peer's merge records `RemoteEditOfLocalDelete` (superseding its earlier record) rather than bring it
  back. One newer question, on purpose — "a deleted row is not brought back without a person" holds per vault.
- **The page** (`SyncConflictsViewModel`, `SyncConflictsView`, "Sync conflicts" in the sidebar after Rules): each
  conflict says what happened, to what ("Transaction · Grocer"), from which device and when, and both sides field
  by field — money as an amount, dates as dates, flags as yes/no, a reference by the name of what it points at,
  owned children as a count, a missing or deleted side said in words. Only the safe choices are buttons; where the
  other side is not offered, a sentence says why. An empty sentence, a load failure and a refused decision are each
  said, in the user's language (85 keys, English and Spanish); every button carries an accessible name. After a
  decision the list reloads and the other pages are told the data changed.
- **Refusals** (`SyncConflictRefusedException`, user-facing, one key per reason, no row data): not found, already
  resolved (by a person or superseded), consent not confirmed, and each block. The validator refuses the same
  cases before the transaction opens; the store re-checks all of them inside it.

#### Falsified

**Twenty-two mutations, applied by a script that restores each file's original bytes and asserts it did, each
built and run against the conflict tests (unit first, then the merge's and the resolution's integration tests).**
First pass: fourteen killed, three survived, five did not compile (`if (false)` is CS0162, an error here) and were
re-run with a condition false only at run time — all five killed.

- **Survived, answered with a test: M1**, superseding a conflict on a change with the *same* stamp (`<=` for `<`).
  Nothing applied a change set a second time while its conflict was open; now the integration and unit suites both
  do, and assert it stays open with nothing superseded. Killed.
- **Survived, answered with a test: M10**, removing the store's "already resolved" check. The final
  `UPDATE … WHERE ResolvedAtUtc IS NULL` still refused and the transaction rolled back, so the only difference was
  that the resolution's writes ran before being discarded. The unit round trip now asserts the refusal comes from
  the check ahead of the writes (its message names the earlier decision). Killed.
- **Survived, answered by deleting the code: M20**, a branch writing a tombstone when a peer's deletion was taken
  for a row neither here nor tombstoned. A deletion is recorded as a conflict only while the row is here, and every
  later delete through a save leaves a tombstone, so no path reaches it; it was removed rather than kept untested.

**Twenty-one run to the end, twenty-one killed; none shown equivalent.** Every mutated file was confirmed restored
byte for byte, and `git status` and a first-bytes comparison against `HEAD` show no stray byte-order mark.

**Measured totals:** unit 1406 → **1453**; integration 300 → **317**; coverage gate green (overall 77.7% → 79.1%, `Pine.Infrastructure` 85.8% → 87.0%, `Pine.Application` → 68.5%, `Pine.Ui.Avalonia` → 53.6%; no floor changed). Windows, macOS
and CLI heads build individually, with no warnings (`Pine.slnx` is not built locally: the Android and iOS workloads are not installed).

**What this does not close.** §5's envelope, the provider, the transport and the offline queue; and **nothing in
the application runs a merge**, so on a real vault this page has nothing to show — its first real conflict needs
the next item. ~~A conflict recorded under an older schema whose payload names a column this vault no longer has
fails the whole list's load~~ (fixed by the review below). Keeping a refused consent writes nothing, so the two
devices keep different values until someone changes the setting. Restoring a row the peer deleted asks the peer's
person again rather than restoring it there. ~~A 3.1b behaviour this item makes visible, and does not change: if
the peer edits a row again while its conflict is open, the next merge takes the peer's edit here~~ — the review
below found this was data loss under §7, and fixed it.

#### The independent review

**It found one major problem — the gap the first write-up recorded, which is data loss — and three minor ones,
and three of its mutations survived.** Every finding was reproduced with a probe test that failed on the reviewed
commit before it was reported; each probe is now a regression test that passes.

- **MAJOR, fixed — a row with an open conflict was overwritten by the peer's next change, and the question closed
  with it.** Both cursors move past a recorded conflict, so the peer's next edit to the row looked like a change only
  the peer made: it was taken here, and the superseding rule closed this vault's record. Nobody had decided. Probes:
  a budget limit in question (310 here, 320 there) became 330 here when the peer edited again (310 expected); when
  each side edited in turn, 320 — a value nobody chose to drop — was overwritten on its own vault and survived only
  in a record the next edit would close too; and the peer *deleting* a row whose edit was in question deleted it here
  unasked. A §7 user-prompt field is not last-writer-wins because the conflict is a merge old. The apply now asks
  first (`HeldByOpenConflictAsync`) whether this peer has an open `ConcurrentEdit` or `RemoteDeleteOfLocalEdit` on
  the row. If it does, the change goes through only when it is the peer's **decision**: the very content the open
  question offers, stamped again (its person kept its side), or a deletion again where the question is a deletion
  — and taking ours arrives equal to this row, which was already moot. Anything else supersedes the old question
  with a newer one about what the peer holds now; this vault's side stays, and "use the other device's version"
  offers the peer's current version rather than one it no longer has. A peer that decided and then edited again is
  asked about once more: an extra question, never a lost value. The superseding rule now runs only where no row of
  this vault's is at stake (none here, or both sides equal) or through that check.
- **MINOR, fixed — one old-schema conflict failed the whole list** (the gap the first write-up recorded; probe: the
  merge's `SyncMergeRefusedException` out of `GetOpenAsync`). The column check is now a question when a conflict is
  read (`HasColumnsAsync`): such a conflict is listed with both sides, its other side is not offered
  (`SyncConflictBlock.SchemaChanged`, a sentence in English and Spanish), and keeping this device's side — which
  writes only stamps — still works. A conflict whose *table* a migration dropped would still fail the load; no
  migration drops a synced table, and one that does must deal with its conflicts.
- **MINOR, fixed — a setting row sent without its key could grant a consent** (a malformed change set; 3.1b's apply
  and this item's reading shared the check). The consent test read the key from the peer's row only; an update writes
  only the columns sent, so a keyless row kept this vault's `TelemetryConsent` key and set its value to `true`
  (probe: `"true"` where `"false"` was expected, no conflict). Either row's key now counts, in the merge and in the
  conflict's "grants a consent" flag alike.
- **MINOR, fixed — every row's buttons had the same accessible name.** "Keep this device's version" on every conflict
  told a screen reader nothing about which one it decides; the names now carry the item ("… · Transaction · Grocer").

**Read and found sound, without a finding:** resolving by the conflict's *local* id, with the payload's ids and
references translated through the same peer's aliases; the restore removing tombstones only for the row it brings
back; balances re-derived after a take-remote through the merge's ledger bookkeeping; `ConfirmConsentGrant` required
by the validator, the store (inside the transaction) and the page's button alike, and no merge after a resolution
granting one; the store's refusal ahead of any write and the final `UPDATE … WHERE ResolvedAtUtc IS NULL` behind it;
every identifier through `Ident`, every value a parameter; the migration's `Up` and `Down`; the new keys in both
languages. **Recorded, not reproduced:** a conflict recorded under a peer's raw id before an alias for that id
existed is not found by a later change that the alias translates, so that change does not supersede it.

**Falsified.** Thirteen mutations of the review's own on the fixes, each applied by a script that restores the
file's original bytes and asserts it did, built and run against the resolution and merge integration tests (the
accessible name against the unit tests): ten killed first time. **Three survived**, and each was answered with a
test and killed after: R1, not holding a change with the *same* stamp as the open question (the conflict's own
change re-sent by a peer that has since seen this side matched the question's payload, read as a decision, and
overwrote this side); R5, ignoring the owned children when deciding whether a change is the peer's decision (a
further change to a transaction's splits alone); and R7, not closing a peer's older non-contested questions when its
later change goes through (a consent question kept offering a consent the peer had withdrawn). The script's first
run stopped at R2 on its own error handling (restoring the file first); it was re-run from R1. None shown equivalent. `git
status` and a first-bytes comparison against `HEAD` show no mutation left and no byte-order mark.

**Measured totals (after the review):** unit 1453 → **1455**; integration 317 → **325**; coverage gate green (unit
suite: overall 79.0%, `Pine.Infrastructure` 86.8% — the new code is exercised by the integration suite, which the
gate does not measure — `Pine.Application` 68.5%, `Pine.Ui.Avalonia` 53.6%; no floor changed). Windows, macOS and
CLI heads build individually with no warnings.

### 6.5 Item 3.1d, as built (2026-09-19, iteration 53): §5's envelope, still between two vault files

**Still no transport.** What two vaults exchange can now be sealed, and a vault now accepts only something sealed:
`VaultMerger.ExportSealedAsync` returns an envelope and nothing else, `ApplySealedAsync` takes one and nothing else,
and the plaintext change set is never written to disk. Both vaults are still open in one process. `NetworkEgressTests`
is untouched and green.

| Row | Outcome |
|---|---|
| TD-144, TD-145 | Unchanged — the envelope carries the cursor; it does not change what a cursor means |
| TD-154 | 🔴 **Registered** — nothing gives a second vault the sync key (pairing), and the tag authenticates a key, not a device |

#### What was built

- **The user's sync key** (`SyncKey`, `src/Pine.Infrastructure/Sync/`): 32 bytes from the CSPRNG; a public 16-byte id,
  `HKDF-SHA256(key, info "pine:sync-key-id:v1")`; its buffer zeroed on dispose and every use afterwards refused;
  `ToString` names the id, never the material. All-zero material is refused as a cleared buffer.
- **Where a vault keeps it** (`VaultSyncKeyStore`): `sync.key` beside `pine.db`, **only as what `IVaultFileCipher`
  seals** under the new purpose `sync.key` — AES-256-GCM under `HKDF-SHA256(data key, "pine:file-cipher:v1:sync.key")`,
  the boundary the categoriser's model already crosses. Written to a temporary file, flushed, moved into place without
  overwriting; never replaced by a different key (`SyncKeyConflictException`); a damaged file, one sealed by another
  vault, or an authentic file that is not a key is refused rather than read as absent. Not in the container — nothing
  in the application runs a merge.
- **The envelope** (`SyncEnvelope`, `pine.sync-envelope.v1`): a fixed 126-byte header — magic `PINESYNC`, version,
  source and target vault ids, since clock and source clock, creation time (ms), key id, a 32-byte salt, a 12-byte
  nonce — then the 16-byte tag and the ciphertext of the `pine.sync.v1` JSON. Each envelope is sealed under
  `HKDF-SHA256(sync key, salt, "pine:sync-envelope:v1")`, so no two share an AES-GCM key; **the whole header is the
  associated data**. No compression. The layout and every decision are in `14-Synchronization` §5.
- **Refusals, typed, before any write** (`SyncEnvelopeRefusedException.Reason`): `Malformed` (short, not ours, a header
  no sealer writes — equal or empty vault ids, a cursor running backwards, a time that is not one), `UnsupportedVersion`,
  `WrongKey` (key id), `NotAuthentic` (any tag failure — one reason, no inner exception), `WrongTarget`,
  `PayloadMismatch` (an authentic payload that is not a change set, or disagrees with its header), and in the apply
  `Replayed` (source clock not beyond this vault's cursor for its source: a replay, or an older envelope) and `Gap`
  (starts after that cursor). A newer envelope that overlaps is accepted, as a change set is. The envelope is opened
  **before the apply's transaction exists**, reading the vault id without creating one; the cursor rules run inside it.
- **The change set's plaintext** is serialised to and parsed from UTF-8 bytes for the envelope (`ToUtf8Json`,
  `FromUtf8Json`), and the byte buffers — the serialiser's working buffer, the plaintext and each envelope key — are
  zeroed. `ToJson` keeps its old output exactly.

#### Decisions, and why

- **The sync key is generated, not derived from the data key** — a departure from `15-Security` §7, amended there.
  Every vault's data key is its own random 32 bytes, so a key derived from one is a key no other vault can compute,
  and it would change on a real data-key rotation (TD-86). What §7 protects is kept: reaching the sync key needs a vault
  unlocked, because each vault holds it only sealed under its own data key.
- **§5's "signature over the ciphertext" is the GCM tag.** It authenticates ciphertext and header; it proves a holder
  of the sync key sealed it, not which device. A per-device signature would need device key pairs and a trust list —
  new cryptography this item does not invent. TD-154.
- **A per-envelope key (HKDF with a random salt) on top of a random nonce**, not a random nonce alone under the sync
  key: the salt costs 32 bytes and removes the question of how many envelopes one key may seal.
- **Replay is refused, not tolerated.** An in-process merge re-applies an overlap idempotently; an envelope that brings
  nothing new has come from somewhere else and could only raise stale conflicts.

#### Tests

Unit: `SyncEnvelopeTests` (round trip; no plaintext in the envelope; 500 seals, 500 distinct salts, nonces and
ciphertexts; **every byte flipped with two masks, each refused for the reason its region gives**; truncation at every
length; a byte appended; the wrong key, and a forged key id that the tag still catches; a header spliced onto a sibling's
body and a rewritten cursor; another target and a vault with no id; payloads disagreeing with their header; versions
0 and 99; every malformed header shape; the seal's own refusals; the documented construction reproduced with .NET's
HKDF and AES-GCM without the class; and **a known-answer vector computed independently** — Python's HMAC for HKDF,
OpenSSL's AES blocks, GCM's counter and GHASH by hand — which agreed with the implementation byte for byte). `SyncKeyTests`
(key id, lengths, copy-in and zeroing on dispose, the store's sealing, sameness, conflict, damage, another vault's file,
another purpose). `SealedMergeRoundTripTests` (two real vaults through envelopes, a wrong key and a replay).
Integration: `SealedMergeTests` — two SQLCipher vaults merge both ways through envelopes only and converge; the
export writes no file; every refusal (wrong key both ways, wrong target, flipped bits in every region and across the
ciphertext, truncation, extension, a spliced header, replay, an older envelope, a gap) leaves the target's **every
table, every row** as it was; a vault with no id is not given one; an authentic envelope the merge itself refuses (a
schema from another build, a busy context) writes nothing either.

#### Falsified

**Twenty-one mutations, applied by a script that restores each file's original bytes and asserts it did, each built
and run against the sync unit tests and `SealedMergeTests`.** First pass: nineteen killed, two survived.

- **M12, `Dispose` not zeroing the key** — the tests checked only that a disposed key refuses to be used. Answered with
  an assertion that the key's own buffer is all zeros afterwards. Killed.
- **M15, the apply creating the vault id instead of reading it** — survived because the refusal rolled the id back
  inside the transaction: equivalent as written. Answered in the code, not only the tests: the envelope is now opened
  before any transaction exists, so a write there would persist, and the no-id test catches it. Killed.

Killed on the first pass: skipping the key-id check; no associated data; associated data covering only the vault ids;
skipping the target check; not comparing the payload's since clock, or its source; accepting equal vault ids or a
backwards cursor in the header; a fixed salt; a fixed nonce; an envelope key that ignores the salt; accepting an exact
replay (`<=` to `<`); removing the gap check; the store replacing a different key; an authentic non-key read as absent;
version 0 accepted; the key id derived with the envelope's info; an all-zero key accepted; a tag failure escaping as a
raw `CryptographicException`. **After the restructure all twenty-one were run again against the final code: all twenty-one killed, none shown equivalent.**
`git status` and a first-bytes check show no mutation left and no byte-order mark.

**Measured totals:** unit 1455 → **1481**; integration 325 → **330**; coverage gate green (unit suite: overall 79%, `Pine.Core` 91%, `Pine.Infrastructure` 86%, `Pine.Application` 68%, `Pine.Ui.Avalonia` 53% — the new code is exercised mostly by the integration suite, which the gate does not measure; no floor changed). Windows, macOS
and CLI heads build individually with no warnings (`Pine.slnx` is not built locally: the Android and iOS workloads are
not installed).

#### The independent review

**It found no defect in the cryptography, and three minor problems, all in what the tests prove rather than in what
the code does — and five of its mutations survived, four of them shown equivalent or unobservable.** Every finding was
reproduced with a mutation or a probe the reviewed commit did not answer before it was reported; each is now a
regression test that passes. **The known-answer vector was re-derived a second time, independently of both this
implementation and the first derivation** — AES-256, GCM's counter mode and GHASH, and HKDF-SHA256 written from
scratch in Python against the layout in `SyncEnvelope`'s remarks — and it reproduced the pinned key id and the pinned
174-byte envelope byte for byte.

- **MINOR, fixed — a refusal message could have quoted the key or the rows, and no test would have noticed.** The
  suite checked every refusal's *reason*, and that it carried no inner exception, and never its sentence. A mutation
  that appended the per-envelope key to the "not authentic" message, and a second that appended the decrypted
  plaintext to `PayloadMismatch`, both survived the suite as it stood.
  `NoRefusalMessage_QuotesThePayload_OrAnyKeyMaterial` now drives eight refusals — the wrong key, a flipped tag byte,
  a payload disagreeing with its header, a payload that is not a change set, another target, another version, a
  truncation and a broken magic — and asserts that no message holds the payee, the sync key, the envelope key, the
  key id, or any run of twenty-four hexadecimal digits from anywhere.
- **MINOR, fixed — `SealCore` ignored the key id in the header it was handed.** It always wrote the sealing key's own
  id, so a header naming another key was silently corrected rather than refused: a test that built one believing it
  had forged a key id would have been exercising the ordinary path and asserting nothing, which is the failure this
  whole section is arranged against. The check is now `key.HasId(header.KeyId)` before anything is written, with
  `SealCore_RefusesAHeaderThatNamesAnotherKey` on it. (The suite's real forged-key-id test edits the sealed bytes and
  was never affected.)
- **MINOR, fixed — what the replay refusal is, and what it is not, was neither written down nor tested.** It is a
  fact about this vault's `SyncPeers` cursor, not a record of envelopes ever seen, so a restore that brings back an
  older `pine.db` brings back an older cursor and an already-applied envelope applies again — which is the right
  answer, because the restored vault genuinely no longer holds those rows, and the local Lamport counter is still
  protected by the restore clock floor (item 3.1b).
  `ARestoreThatRewindsTheCursor_MakesAnAppliedEnvelopeApplyAgain` states it. The neighbouring question was untested
  too and is now asserted in place: after an apply the merge itself refuses, the cursor has not moved inside the
  rolled-back transaction, so the same envelope still applies.

**Read and found sound, without a finding:** the key generation (32 bytes straight from `RandomNumberGenerator`,
all-zero material refused, the key id `HKDF-SHA256(key, no salt, "pine:sync-key-id:v1")` truncated to 16 bytes and
revealing nothing a guess of the key would not); the two HKDF uses separated by `info` rather than by salt, and
neither using the sync key as a cipher key; the departure from `15-Security` §7 — a key derived from one vault's data
key is unreachable by a second vault and dies on TD-86's rotation, so generating it is the only construction that can
work, and §7's protection is kept by sealing it under each vault's data key through the same `IVaultFileCipher`
purpose boundary the categoriser's model crosses; the whole 126-byte header as associated data (narrowing it to the
vault ids, or to nothing, is caught, and the two narrowings that are *not* caught remove only fields GCM binds
anyway — below); a fresh 32-byte salt and 12-byte nonce per envelope from the CSPRNG, so no GCM key seals twice and
the random-nonce bound is unreachable; `FixedTimeEquals` on the key id; the refusal order — shape and version, then
the key id, then the tag, and only then the target and the payload's agreement with its header, with the byte-by-byte
flip test proving the target is never the reason before the tag has spoken; `ApplySealedAsync` opening the envelope
before any transaction exists and reading the vault id without creating one, so a refusal cannot leave a vault id, a
lifted clock or a cursor behind; the gap rule; the merge path through the envelope still refusing everything 3.1b and
3.1c refuse, checked against a snapshot of every table and every row; and `JsonDocument.Parse(ReadOnlyMemory<byte>)`
materialising every value, so nothing survives into the change set pointing at the plaintext buffer the caller zeroes.
**Recorded, not reproduced:** two processes calling `VaultSyncKeyStore.GetOrCreate` on one vault at the same moment —
the loser's `File.Move` race handler refuses with `SyncKeyConflictException` instead of returning the winner's key;
the race is not injectable through any seam this class has, so it is written down rather than claimed fixed.

**Falsified.** Thirty-six mutations of the review's own, applied by a script that restores each file's original bytes
and asserts it did, each built and run against the sync unit tests and `SealedMergeTests`. Thirty-one killed; **five
survived**, and each is answered rather than left open:

- **M11 (a refusal quoting the envelope key) and M27 (a refusal quoting the plaintext)** survived and are killed by
  the message test above. **M25 (`SealCore` ignoring the header's key id)** and **M26 (a seeded generator)** likewise
  survived the suite as it stood and are killed by the two new tests.
- **M7, the key id compared with `SequenceEqual` instead of `FixedTimeEquals`** — behaviourally identical, and the key
  id is a public routing value that travels in the clear in every header, so the timing it leaks is timing about
  something an observer already has. Shown equivalent; the constant-time call stays, because it costs nothing.
- **M8, `Generate` drawing from `Random.Shared`** — no black-box test distinguishes a decent non-cryptographic
  generator from the CSPRNG, and none in this suite ever will. It is answered as far as a test can answer it:
  `GeneratedKeys_NeverRepeat_AndSpanTheWholeByteRange` kills the narrow or seeded generator (M26, `new Random(42)`,
  dies on it), and that the source is the CSPRNG is held by reading one line. Said plainly rather than covered over.
- **M9, M10 and M12, the zeroing of the plaintext, of the per-envelope key and of the serialiser's working buffer** —
  all three are method-local buffers with no observation point through any public or internal API, so no test in this
  repository can distinguish them. Unobservable rather than equivalent: the zeroing is defence in depth, and is kept.

`git status` and a byte comparison against `HEAD` show no mutation left in `src`, no byte-order mark and no line
ending changed.

**Measured totals (after the review):** unit 1481 → **1484**; integration 330 → **331**; coverage gate green (unit
suite: overall 79%, `Pine.Core` 91%, `Pine.Infrastructure` 86%, `Pine.Application` 68%, `Pine.Ui.Avalonia` 53%; no
floor changed). Windows, macOS and CLI heads build individually with no warnings.

**What this does not close.** **Pairing** — no path gives a second vault the key; the tests use
`VaultSyncKeyStore.Save` in its place (TD-154). **Device authentication** — the tag proves the key, not the device
(TD-154). The provider, the transport and the offline queue (§8, §9), and **nothing in the application runs a merge**.
The plaintext still exists in memory as the `SyncChangeSet` object's strings, which cannot be zeroed; only the byte
buffers are. A real data-key rotation (TD-86) will have to re-seal `sync.key`. The creation time in the header is
informative and is not checked.

### 6.6 Item 3.1e, as built (2026-10-04): pairing — giving a second vault the sync key

**Still no transport.** Pairing moves `sync.key` from one vault to another through a file the user carries between
them (a USB stick, a shared folder — the channel is the user's, not this item's); `NetworkEgressTests` is untouched
and green.

| Row | Outcome |
|---|---|
| TD-154 | 🟡 **Narrowed** — a second vault can now be given the key over a channel the user confirms (a short-lived code, verified afterward by a fingerprint shown on both screens); the tag still authenticates a key, not a device |

#### What was built

- **`SyncPairingCode`** (`src/Pine.Core/Sync/`): 25 characters, Crockford base32, drawn one character at a time from
  `RandomNumberGenerator.GetInt32` rather than by encoding random bytes — 125 bits, not padded or biased. Not a PAKE:
  the code is the whole of the authentication, and its entropy is the argument for why that is enough for a code read
  aloud between two devices in front of one person.
- **`SyncPairingOffer`** (`src/Pine.Infrastructure/Sync/`): a 166-byte sealed file — a 118-byte header, all of it
  associated data, a 16-byte GCM tag, and the 32-byte ciphertext of the sync key. The wrap key is
  `HKDF-SHA256(the code's 25 ASCII characters, a per-offer random 32-byte salt, info "pine:sync-pairing:v1")`. Refusals
  are ordered the way `SyncEnvelope`'s are: shape and version, then the code, then the tag, then expiry, then same-vault,
  then payload.
- **`SyncKey.Fingerprint`**: an 80-bit value, HKDF-derived with its own `info` string (not `KeyId`'s), shown in four
  groups of four characters on both devices after a pairing succeeds — the check that the two people, not just the two
  files, agree on which key just moved.
- **`VaultSyncPairing`** (`ISyncPairing`): the vault-side port. `CreateOfferAsync` gets-or-creates this vault's key and
  writes the sealed offer; `AcceptOfferAsync` opens it, refuses a vault that already holds a *different* key
  (`SyncPairingRefusal.DifferentKey`, including the race caught as `SyncKeyConflictException` from `VaultSyncKeyStore`),
  saves the key, and only then deletes the offer file — a refusal writes nothing, and a save that fails leaves the
  offer live. A copy of an already-used offer is refused as `Replayed` because the accepting vault now holds that
  key id.
- **Application layer**: `CreateSyncPairingOfferCommand`, `AcceptSyncPairingOfferCommand`, `GetSyncPairingStatusQuery`,
  each validated, each with a sender. **UI**: `SyncPairingViewModel`/`SyncPairingView`, "Pair a device" in the sidebar,
  following 3.1c's `SyncConflicts` page shape — a code and an expiry shown on the offering side, a code box and the
  fingerprint shown on the accepting side, every string a key in both `en.json` and `es.json`.

**What this does not close, said the way the offer's own remarks say it.** The same offer file, copied *before* it is
used, can pair a third vault inside its lifetime window — there is no state shared between vault files to record a
single use against, so only the (short, user-set) expiry bounds it; the user is told to keep the code to the two
devices in front of them. And the second half of TD-154 is untouched: the envelope's tag proves a holder of the sync
key sealed it, not which device — pairing hands over a key, not a device identity, and that remains a different
cryptographic problem this item does not invent. The provider, the transport and the offline queue (§8, §9), and
**nothing in the application runs a merge**, are all still ahead.

**Verified.** Built clean across `Pine.Core`, `Pine.Infrastructure`, `Pine.Application`, `Pine.Ui.Avalonia`, 0
warnings. Unit **1484 → 1585**, integration **331 → 335**, both green — the integration suite's new test pairs two
real encrypted vault files and then seals an envelope on one and opens it on the other, which is the test 3.1d could
not write because nothing gave the second vault the key yet. An independent audit (not the author) read every new and
changed file, re-ran both suites, and separately verified localisation-key parity (38 `SyncPairing` keys in each of
`en.json`/`es.json`) and the refusal/delete ordering in `VaultSyncPairing` above; it found no defect and made no
change. It checked five adversarial scenarios by hand rather than by argument alone: code brute force (bounded by the
125-bit entropy, pinned by a test), a copied offer replayed against the same vault (refused, pinned by both unit and
integration tests), an offer that never expires (bounded in both `Create` and `ReadHeader`, with a rewritten expiry
caught as `NotAuthentic` rather than honoured), the accept-race above, and the copied-offer-pairs-a-third-vault gap —
confirmed as a real, documented limit rather than an oversight.

### 6.7 Item 3.1f, as built (2026-10-05): the provider, the transport and the offline queue — the first thing that runs a merge

**The gap every item since 3.1b named is closed: something in the application now runs a merge.** A shared folder
carries sealed envelopes and vault announcements between two paired vaults; nothing is network-connected,
`NetworkEgressTests` is untouched and green.

| Row | Outcome |
|---|---|
| §6's recurring "the provider, the transport and the offline queue (§8, §9)" / "nothing in the application runs a merge" | Narrowed to one honest slice: a folder, not a network |

#### What was built

- **`ISyncProvider`** (`Pine.Core.Sync`): health, announce, list peers, upload/download/delete one envelope by
  address. Deliberately smaller than `14-Synchronization` §8's full table — no `Auth`/`Capabilities`, because no
  honest implementation of either exists for a folder; the sync cursor lives in the envelope header, not in the
  provider (decided "unified" per the design doc's SY-D-1).
- **`FolderSyncProvider`**: one directory holds two file kinds, both named only by hex GUIDs so no stray file is
  ever mistaken for a peer's — `{vault}.pine-vault` announcements and `{source}.{target}.pine-envelope` envelopes.
  Every write is temp-file-then-flush-then-atomic-`File.Move`, so a reader only ever sees a prior complete file or
  a new one, never a partial write. A 64 MiB cap is enforced before the write and, on download, before any byte is
  read.
- **`VaultSyncRunner`**, the first caller of `VaultMerger`'s merge machinery from inside the application: applies
  every peer's waiting envelope first, each in its own transaction, then exports and uploads what a peer still
  lacks. **No separate offline-queue table** — a peer's own acknowledged cursor, read back out of that peer's own
  authenticated envelope header, is the queue; re-deriving it each run is what made a dedicated structure
  unnecessary. Idempotent by construction: the apply and the cursor advance share one transaction, so a crash
  between them cannot happen, and a re-sent envelope is refused as `Replayed` and treated as a no-op, not an error.
- **`RunSyncCommand`**, validated (refuses an empty or non-rooted path before touching disk) and sent from
  `SyncRunViewModel`'s "Sync now" page, wired into the shell and both localizations the same way every prior sync
  screen was.

**What this does not close.** No real network transport — a folder is the whole of it, and that is ADR-0012's
decision rather than a shortfall of this item. The folder path is asked for on every run and never saved as a
setting. And pairing's own open gaps (§6.6: a copied offer can still pair a third vault inside its expiry window;
the envelope authenticates a key, not a device) are unchanged by this item.

**Verified.** Built clean across all four desktop-buildable projects. Unit **1585 → 1668**, integration
**335 → 340**, both green. Ten mutations against the new provider/runner/validator code, ten killed, no survivors —
among them the replay guard, the gap guard, the idempotency comparison in `IsAlreadyLeft`, the peer-identity filter,
both size checks, the source/target refusal, the path validator, and the check that stops a stranger's envelope
header from being read as a peer's acknowledgement. An independent verification pass (not the author) re-read every
file, rebuilt, re-ran both suites with the same counts, re-ran all ten mutations independently with the same result,
and answered five scrutiny questions directly against the code and its tests: an interrupted run leaves no partial
state (each apply its own transaction, each folder write temp-then-atomic-rename, demonstrated by a test that closes
and reopens contexts mid-scenario); double-apply cannot happen because there is no separate mark-applied step to
race, apply and cursor-advance being one transaction; the folder transport cannot read a partial write, by
construction; no plaintext key or vault data is ever written to the folder, checked by a test that greps every byte
written there for every plaintext secret in both UTF-8 and UTF-16; and localisation key parity and the
validator/sender convention both hold (confirmed independently: 27 `SyncRun`-prefixed keys in each of
`en.json`/`es.json`). No defect found; no fix needed.

### 6.8 Item 3.1g, as built (2026-10-05): §12's audit log

**Still no telemetry, still no network.** `14-Synchronization` §12 asks that every sync attempt be logged locally —
timestamp, provider, envelopes up and down, conflicts detected and resolved — and that a person can read that log
from Settings. Neither existed; `VaultSyncRunner`'s report (3.1f) died with the caller that read it.

| Row | Outcome |
|---|---|
| `14-Synchronization` §12 | Closed, narrowly: a persisted, read-only log of every run, inspectable from Settings |

#### What was built

- **`SyncRunAuditLogRow`**, an ordinary table inside `pine.db` (migration `AddSyncRunAuditLog`, indexed on
  `RanAtUtc`) — "encrypted at rest" is SQLCipher's, already true of every table here, so no second encryption layer
  was built. **`ISyncAuditLog`/`SqliteSyncAuditLog`** write one row per attempt and read them back newest-first.
- **`RunSyncCommandHandler` logs every path**, not only success: a refusal by its typed reason's name, an
  unexpected failure by its **exception type name, never its message** (TD-24 — a message can quote a path or a
  key), and the counts from `SyncRunReport` on success. A cancelled run is not logged, deliberately — cancellation
  is the user stopping something, not an attempt that happened.
- **`GetSyncAuditLogQuery`** and a read-only list in Settings, newest-first, with the empty state and every string
  keyed in both languages (647 keys, parity confirmed by diffing both files key by key, not by a line count).
- `ConflictsResolved` is always 0 and said so in the code: a sync run records conflicts, it does not resolve them
  (3.1c's screen does that), so the column is honest about measuring nothing yet.

**What this does not close.** §11's User Controls — pause/resume, enable or disable per provider, force a full
re-sync, change provider, delete all envelopes — are untouched; this item is §12 only. Telemetry transmission is
not addressed because none exists to address: ADR-0012 and `NetworkEgressTests` already forbid any network call,
so "opt-in only" is satisfied by there being nothing to opt into.

**Verified.** Built clean across all four desktop-buildable projects. Unit **1668 → 1679**, integration
**340 → 344**, both green (one cross-test flake in `SyncConflictsPageTests`, pre-existing and tied to a shared
messenger singleton, reproduced as a pass in isolation and on a full rerun — not this item's). Eight mutations
against the logging path and the read order, eight caught: skipping the log on a refusal, skipping it on success,
swapping the up/down counts, inventing a non-zero `ConflictsResolved`, logging `null` instead of the exception
type, logging a cancelled run, reading oldest-first, and removing the folder-path validator guard. A second pass —
not the author — read every file, independently re-confirmed localisation parity (647/647 keys, zero diff either
direction) and the exception-type-not-message rule by grepping the handler directly, filled in a missing migration
round-trip test and a few maintenance updates the new migration/entity forced in five other tests' own tripwire
lists (a new migration or mapped entity is expected to move those, by design), and found no defect in the feature
itself.

### 6.9 Item 3.1h, as built (2026-10-05): §11's User Controls

**One provider, so the clauses that assumed several were narrowed, and the one clause that didn't need several was
built for real.** `14-Synchronization` §11 was written for a plugin world ADR-0021 killed; `FolderSyncProvider` is
the only provider Pine compiles in, and that stays true here.

| Row | Outcome |
|---|---|
| `14-Synchronization` §11 | Closed, narrowly: one on/off setting (pause folded into it honestly), a forced full resync, and real erasure |

#### What was built, and the narrowing decided for each clause

- **"Enable/disable per provider" and "change the provider"** narrow to one setting, `SyncSettingKeys.Enabled`, in
  the existing generic `Setting` table — no new migration, because there is nothing to choose between.
  `RunSyncCommandHandler` checks it first and refuses as `SyncRunRefusal.Disabled`, logged to the audit log like
  any other refusal.
- **"Pause and resume" collapsed into the same setting, on purpose.** There is no background sync — a run happens
  only when the user presses the button — so a shorter-lived "pause" would be behaviourally identical to
  "disable": two controls, one effect. Said plainly in the view model's own remarks rather than built as two
  controls that do the same thing.
- **"Force a full re-sync"** (`RunThroughFolderAsync(folder, forceFullResync, …)`) re-derives the export from
  `SyncChangeSet.Never` instead of the peer's acknowledged cursor, so every row and tombstone goes again regardless
  of what the peer claims to have. **Checked, not assumed, that this cannot duplicate or lose anything**: the
  merge's own matching (3.1b) is by id and content, so a unit and an integration test both force a resync against
  an already-settled pair and assert zero rows changed. The confirmation dialog names that mechanism rather than
  giving a generic warning, in the same scrim-and-panel shape the restore dialog already uses.
- **"Delete all envelopes from a provider" (right to erasure)** is the one clause fully real with one provider:
  `EraseSyncDataCommand` removes every envelope and announcement this vault has left in the folder — never a
  peer's files, never anything already merged into this vault — behind the same confirmation shape, naming the
  folder and the actual consequence.

**What this does not close.** No plugin host and no provider-selection UI — not a gap, ADR-0021's decision. A
disabled or erased vault is not told to re-pair; pairing (3.1e) is untouched.

**Verified.** Built clean across all four desktop-buildable projects. Unit **1679 → 1727**, integration
**344 → 349**, both green. Ten mutations against the disable check, the force-resync cursor override, the erasure
loop, and both confirmation guards: nine killed, one shown equivalent rather than hidden — removing the
already-left short-circuit's `forceFullResync` guard only matters on a *second consecutive* forced resync with
no change in between, where re-sending produces the same bytes as not re-sending, so no test can tell the
difference and none was contrived to pretend otherwise. Localisation parity confirmed key by key (673/673, no
diff either direction) rather than by line count, independently by the verifying pass.

## 7. Stage 4 — The documents

| # | Work | Closes |
|---|---|---|
| 4.1 | ✅ **Done** (2026-10-05) — Revise the non-normative documents that still promise a plugin ecosystem, see §7.1 | TD-147 (closed) |
| 4.2 | ✅ **Done** (2026-10-05) — Re-scope `TD-84` (the Diagnostic Information Bundle), whose Fix column waits on a plugin host that will now never exist, see §7.2 | TD-84 (narrowed, not closed) |
| 4.3 | ✅ **Done** (2026-10-05) — Sweep the register for rows whose *mitigation* has been overtaken, as TD-67's was, see §7.3 | TD-128 (closed) |

**4.3 is the most valuable item in this section and the least obvious.** TD-67 sat at 🟡 P2 because
"neither is reachable from the wizard", and TD-28 made it reachable five iterations later while
fixing something else. Nobody re-read the row. **That is a class, not an incident**: any row whose
severity rests on a mitigation rather than on the defect can be silently promoted by unrelated work.
Search the register for Impact columns that argue "this is not reachable / not used / not exposed"
and re-verify each one against the current code.

### 7.1 Item 4.1, as built (2026-10-05): the non-normative documents, corrected rather than swept

**TD-147's own Impact column had already explained why these six were left alone the first time**: none of
`00-Vision`, `01-Business-Requirements`, `23-Roadmap`, `17-Risk-Analysis`, `55-Future-Vision` or
`05-Personas`/`04-Stakeholders` specifies software behaviour, so none could mislead a reader about what the code
does — unlike `02-Product-Scope` and `07-Functional-Requirements`, which were amended with ADR-0021 immediately
because they do. **A product's stated direction still deserves a deliberate correction, not silence**, which is
the distinction this item acts on.

**What was done.** Every sentence promising a plugin ecosystem in the six documents was struck through in place —
nothing deleted — with a dated correction beside it citing ADR-0021, following the exact pattern
`14-Synchronization` §8 already used for its own amendment. The Fix column's two specific instructions were both
taken literally rather than paraphrased: **`17-Risk-Analysis` R-10 is marked Answered, not Mitigated** — the
ecosystem will not form because there will not be one, which is a different claim than "the risk is managed" —
and **PERS-07 (the plugin-author persona) and `SH-PLUGIN-AUTHOR` are retired**, not re-aimed: no compiled-in
extension seam serves her original goal, so inventing one to redirect her at would have been the inventive move
this item was not asked to make. `STATUS.md`'s Phase 5 line was corrected too, found by the same sweep rather than
named in the row.

**What was checked and left alone.** `12-Plugin-Architecture` and `33-Plugin-SDK` were already `Withdrawn` and
cite ADR-0021 correctly; `02-Product-Scope` and `07-Functional-Requirements` were already amended, as the row
itself says. `56-IMPLEMENTATION-PLAN.md` still names plugins throughout and was left untouched **by design** — its
own header already states it is a historical plan, and editing it to match the outcome would destroy the record
of what was intended. About a dozen other documents reference `33-Plugin-SDK` only as a cross-reference, not as a
standalone promise, and TD-147's row did not name them.

**Verified.** All sixteen `Documentation`-filtered unit tests pass (`EveryDocumentDeclaresAStatus`,
`EveryWithdrawnDocumentCitesAnAdrThatExists`, `TheDocumentSetHasNotShrunk`, and the rest), re-run directly rather
than taken on the word of the pass that made the edits. The full unit suite is unaffected (still 1727 green); this
item touched no source file, so no build or integration run was needed. TD-147's own register row is marked
Closed, citing this section, with its correction summarized inline rather than left for a reader to reconstruct
from the diff.

### 7.2 Item 4.2, as built (2026-10-05): TD-84 narrowed, not closed

**TD-84's own Fix column already named the move: "build the DIB once metrics, SLOs and a plugin host exist, or
narrow §9 to what a local-first app can honestly produce."** A third of the "build" branch — the plugin host — is
now permanently foreclosed by ADR-0021, not merely unbuilt, so that third is removed from the condition rather than
left waiting on something that will never happen. The other two, metrics and SLOs, still genuinely do not exist
and are not this item's to build.

**What the Diagnostic Information Bundle is** (`30-Observability` §3, §9.2): a redacted, signed artefact a user
could send to maintainers, carrying app version, OS/runtime versions, a log tail, sync and import summaries, an
SLO budget summary, **a plugin inventory (id, version, source, permissions, risk score)**, and reproduction hints.
Nothing in it has ever shipped — only two of the eight contents are on the Diagnostics panel today, outside any
bundle — and §8/§9 were already annotated `Status: Aspirational`.

**What was done.** The plugin-inventory content is struck from `30-Observability` §8 and §9.2, and from TD-84's
own Impact and Fix columns in the register, each correction citing ADR-0021 and dated. **The row stays open,
narrowed rather than closed**: the DIB is still not honestly buildable, because metrics and SLOs are still
missing, and the register says so in the same breath rather than implying ADR-0021 finished the job. No code
changed — this is a documentation-only re-scoping, the same shape as item 4.1.

**What was deliberately left alone.** `30-Observability` §5.1/§7/§9.3/§9.4 (the telemetry switch, uploading to an
endpoint, signing, EU retention) are a separate, already-flagged contradiction with ADR-0012 and are not this
item's — touching them would have been a different TD's scope. Every dated narrative mention of TD-84 elsewhere in
the register was left as a historical record, per this project's own rule against editing the past to match the
present; only the live register row and the normative document were corrected.

**Verified.** All sixteen `Documentation`-filtered unit tests pass, re-run directly rather than taken on trust. No
source file was touched, so no build or integration run applied.

### 7.3 Item 4.3, as built (2026-10-05): the register sweep, and its one real find

**The class TD-67 named happened again, in the other direction.** Every row in the register whose Impact column
argued "not reachable / not used / not exposed / no caller / dead code / behind a gate that's always off" was
listed before any was re-verified — the same discipline as searching broadly before fixing narrowly — and each
was checked against the current code, not the code as of when it was written.

**Six rows checked and found still accurate, not touched:** TD-116 (`PortableAiProvider`'s vision refusal —
traced the real DI registration and the factory's capability refusal; still no caller on any head), TD-141
(attachments still only arrive from the receipt scanner — manual entry has no file picker), TD-143 (the rules
screen still writes exactly one condition), TD-151 (import profiles are still built and discarded, never saved),
and TD-100 (checked and explicitly **not** promoted, because its account-default line predates the row itself —
a row that slightly overstated its own gap from the start is a different problem than one overtaken by later
work, and out of this item's scope on purpose).

**One row promoted, and it closes rather than worsens.** TD-128 (the shell's disabled "Rules" nav button, 🟢,
"honest because it says Coming soon") was mooted by iteration 34's own rule-engine amendment (TD-37, §12.70),
which wired that exact button to `NavigateToRulesCommand` and replaced its tooltip — years before this sweep
re-read the row. Confirmed directly in `ShellView.axaml`'s own comment ("Enabled as of the Phase 3 rule-engine
work...") and in `RulesViewModel`'s real create/list/enable/disable/delete against `IRuleRepository`. Corrected
in place: the Impact and Fix columns struck through with a dated note citing the commit and the amendment that
mooted them, Status changed to Closed, nothing deleted.

**Verified.** All sixteen `Documentation`-filtered unit tests pass, re-run directly. No source file was touched.

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

**2026-09-10: Stage 0 is closed, and that sentence is struck through in §11.1 rather than deleted.**
Three items, three commits, three guards, each falsified before being believed. What replaced the
sentence is narrower and honest: CI is armed, derives what it builds from the solution, and every
gate proves it ran — and **no run has yet happened**, because opening a pull request against the
real remote is the one step none of this can do for itself (`61-Manual-Test-Plan` §3.2). Until that
is done, Stage 0 is complete in the repository and unproven on the server, which is a smaller claim
than "finished" and the only one the evidence supports.

~~The next item is 1.1, restore from a backup in the application.~~ Done 2026-09-10 (§4.1), and
1.2, the navigation defect, on 2026-09-11 (§4.2), and 1.3, the dashboard, the same day (§4.3). The
1.4, the dead "Scan Receipt" button, and 1.5, the invented camera list, the same day (§4.4, §4.5).
**Stage 1 is closed.** The next item is Stage 2's 2.1: the intermittent SQLite handle disposal
(TD-140), which this run met twice more — once in item 1.3's integration run and once in an import
test — and which costs a re-run every time a number in this document is measured.

~~The next item is Stage 2's 2.1.~~ Done 2026-09-11 (§5.1), then 2.2 (§5.2), 2.3 (§5.3), 2.4 (§5.4) and,
on 2026-09-14, 2.5 (§5.5). **Stage 2 is closed as far as any commit can close it**: what remains of it is
TD-108's certificate, which is a purchase, and TD-138's device half, which needs hardware and workloads
this project does not have. Two rows TD-148's guard registered belong to Stage 3's kind of work rather
than Stage 2's — TD-152 (no screen creates a budget) and TD-151 (import profiles are never saved) — and
TD-152, being a screen that cannot do what it exists for, is the first candidate to go ahead of 3.1.
**The next item is Stage 3.**

~~The next item is Stage 3.~~ Item 3.0 done 2026-09-14 (§6.1): **a budget can be created**, and budgets
measure the user's own month (TD-149). What the budgets page still cannot do is edit or delete one. **The
next item is 3.1, sync** — starting, as its row says, with TD-144, TD-145 and TD-146, not the transport.

~~The next item is 3.1, sync — starting with TD-144, TD-145 and TD-146.~~ Those three done 2026-09-14 (§6.2):
every written row says which device wrote it and when in the vault's logical time, deletes leave tombstones
that keep nothing the user deleted, and every aggregate has a natural-key decision. What is left of 3.1 is
the sync itself — and the first thing it owes, before any transport, is the merge-side half of the clock
(receive, and a high-water mark that survives a restore), because the preconditions built here are only as
trustworthy as that step. **The next item is 3.1b: the change set and the merge, locally, between two vault
files — still no transport.**

~~The next item is 3.1b: the change set and the merge, locally, between two vault files — still no
transport.~~ Done 2026-09-18 (§6.3): two vault files merge both ways in one transaction each, §7's
last-writer-wins only where §7 allows it and a recorded conflict everywhere else, balances re-derived, the
clock's receive step taken, and a restore no longer able to rewind the clock. Conflicts are recorded and nothing
shows them, and nothing in the application calls the merge. The independent review of 3.1b is done too (§6.3, two
data-loss defects in the natural-key match found and fixed). **The next item is 3.1c: a screen that lists recorded
conflicts and resolves them** — before §5's envelope and any provider,
because a merge whose conflicts nobody can see is one that silently keeps the local side.

~~The next item is 3.1c: a screen that lists recorded conflicts and resolves them.~~ Done 2026-09-18 (§6.4): a
"Sync conflicts" page lists every conflict a merge recorded, both sides field by field, and offers only the choices
that are safe; a decision is written through the merge's own path, stamped by this vault so it travels, and closes
the peer's record of the same conflict on the next merge. Consent is never granted without a ticked box. Nothing in
the application runs a merge yet, so on a real vault the page is empty. Its independent review is done too (§6.4:
a row with an open conflict was overwritten by the peer's next change — data loss, fixed — and three minor
findings, fixed). **The next
item is 3.1d: §5's envelope — the change set encrypted with the user's sync key, still between two vault files and
still without a transport** — so that the first thing that ever leaves the process is ciphertext.

~~The next item is 3.1d: §5's envelope — the change set encrypted with the user's sync key, still between two vault
files and still without a transport.~~ Done 2026-09-19 (§6.5): what one vault has for another is sealed with AES-256-GCM
under a per-envelope key derived from the user's sync key, with the whole header bound, and an envelope that is
damaged, under another key, for another vault, a replay, older, or out of order is refused before anything is written.
The sync key is generated and kept beside each vault sealed under that vault's data key — a recorded departure from
`15-Security` §7's "derived from the data key", which cannot work between two vaults. Nothing gives a second vault the
key yet. Its independent review is done too (§6.5: no defect found in the cryptography, and three minor
findings — a refusal message that could have quoted the key or the rows with no test to notice, `SealCore`
silently ignoring the key id in the header it was given, and the replay refusal's dependence on a cursor a
restore rewinds — all fixed, and the known-answer vector re-derived independently a second time). ~~The next
item is 3.1e: pairing — giving a second vault the user's sync key over a channel the user confirms
(TD-154).~~

Done 2026-10-04 (§6.6): a short-lived, high-entropy code seals the sync key into an offer file one vault writes and
the other opens, with a fingerprint shown on both screens afterward so the two people confirm the same key moved, not
just the two files. An independent audit (not the author) re-ran both suites (unit 1484 → 1585, integration 331 →
335, both green), checked five adversarial scenarios by hand — brute force, replay, a non-expiring offer, an
accept-race, a copied offer pairing a third vault — and found no defect. TD-154 is narrowed, not closed: the envelope
still authenticates a key, not a device, and the same offer copied before use can still pair a third vault inside its
expiry window (bounded, not prevented). ~~The next item is the provider, the transport and the offline queue (§8,
§9).~~

Done 2026-10-05 (§6.7): a shared folder carries sealed envelopes and vault announcements, and `VaultSyncRunner` is
the first thing in the application that actually runs a merge — applying what a peer left, then sending what a peer
lacks, each its own transaction, with no separate offline-queue table because a peer's own acknowledged cursor is
the queue. Verified: unit 1585 → 1668, integration 335 → 340, both green; ten mutations, ten killed; an independent
pass re-ran all of it and answered five scrutiny questions (interrupted runs, double-apply, partial reads, plaintext
leakage, localisation/CQRS convention) with no defect found. No real network transport exists — a folder is the
whole of it, by design (ADR-0012) — and the folder path is not yet a saved setting. ~~The next item is giving this
runner a way to run on its own, or network transport if the design doc scopes that next.~~

`14-Synchronization` has no roadmap section to check — it is a normative spec, not a punch list — so the next item
was read off its own unmet normative clauses rather than guessed: §12 (an audit log) was still unmet and small
enough to be one slice. Done 2026-10-05 (§6.8): every sync attempt, successful or not, is now logged inside the
vault's own encrypted storage and readable from Settings, newest-first. Verified: unit 1668 → 1679, integration
340 → 344, both green; eight mutations, eight caught; an independent pass found no defect, only tests to add.
~~The next item is `14-Synchronization` §11, User Controls.~~

Done 2026-10-05 (§6.9): sync can be turned off (pause folded into the same setting, honestly, since nothing runs in
the background to pause), forced to resend everything to a peer regardless of its cursor (checked safe against the
merge's own idempotency, not assumed), and erased — every envelope and announcement this vault left in the folder,
gone, behind a confirmation naming the real consequence. One provider, so "change provider" and "per-provider"
narrowed to "on or off"; no plugin host was built (ADR-0021 still holds). Verified: unit 1679 → 1727, integration
344 → 349, both green; ten mutations, nine killed, one shown equivalent rather than papered over.

**Every normative clause in `14-Synchronization` now has a resolution** — §4 (the merge policy, 3.1b), §5 (the
envelope, 3.1d), §6 (change-set semantics, 3.1a/3.1b), §7 (the conflict table, 3.1b/3.1c), §8 (the provider, 3.1f),
§9 (the offline behaviour, 3.1f), §11 (user controls, 3.1h) and §12 (the audit log, 3.1g) each have a narrow,
honest slice built against them, and **§10, Cloud Only Mode, is marked out of scope rather than built** — it
assumes a cloud provider, which ADR-0012's no-network rule rules out the same way ADR-0021 ruled out a plugin
host, and `14-Synchronization` §10 now says so in place rather than being silently skipped. Sync, as a document, is
done the way a decision can close a section: nothing in it is still merely unaddressed. ~~The next item is
whatever Stage 4 (§7 of this plan) or the open TD rows ahead of it name.~~

Done 2026-10-05, item 4.1 (§7.1): the six non-normative documents still promising a plugin ecosystem are corrected
in place — struck through, not deleted — with `17-Risk-Analysis` R-10 marked Answered and PERS-07/`SH-PLUGIN-AUTHOR`
retired, the Fix column's two instructions taken literally rather than paraphrased. TD-147 is closed. ~~The next
item is 4.2: re-scope TD-84, or 4.3's sweep for a mitigation overtaken by later work — whichever the register
shows is still live.~~

Done 2026-10-05, item 4.2 (§7.2): TD-84's plugin-inventory third is struck from both the register and
`30-Observability`, citing ADR-0021; the row stays open and narrowed, not closed, because metrics and SLOs — the
other two preconditions — still don't exist. No code changed. ~~The next item is 4.3: sweep the register for a
row whose severity rests on a mitigation that later work has overtaken, the way TD-67's was — the last item in
Stage 4, and the last item this plan currently names at all.~~

Done 2026-10-05, item 4.3 (§7.3): six rows checked against current code and found still accurate; one, TD-128
(the shell's disabled "Rules" button), found mooted the same way TD-67 was — the rule engine §6.2's own history
closed in iteration 34 had already wired it to a real screen, and nobody had re-read the row since. Closed, not
promoted. **This was the last item this plan names.** Every item in §3 through §7's tables now reads ✅, or
narrowed-and-explained where a full close was never honest to claim. Further work is the register's to name, not
this plan's: an open TD row (TD-108's certificate, TD-138's device half, TD-144/TD-145's retention, TD-154's
device-authentication half, or whatever `60-Production-Readiness-Audit` names next) or ordinary product work —
which is the destination this plan's own §9 named from the start.

**Post-closure, 2026-10-05: TD-145's `secure_delete` half, closed from the register directly.** With this plan's
own items exhausted, the next work was picked from `60-Production-Readiness-Audit` as §9 said it would be:
TD-145's row named two still-open things, and one — SQLite `secure_delete`, so a hard delete overwrites freed
bytes rather than leaving them until a page is reused — was concretely buildable without a design decision, unlike
the retention policy beside it (a claim about peers this vault has never met). An EF Core interceptor now forces it
on every connection, sync and async, not configurable; verified on a plain connection because SQLCipher already
defaults it ON on a keyed one, which the work states plainly rather than claiming credit for a vendor default.
Retention stays open. This correction lives in TD-145's own row, not a new numbered item here — the register, not
this plan, is where it belongs now.

**Post-closure, 2026-10-06: TD-154's pairing half corrected, and TD-150's four remaining sentences closed.**
Re-reading TD-154's row found it still said pairing did not exist, three weeks after item 3.1e shipped it — the
same staleness class as item 4.3, just not phrased as "not reachable", so that sweep's own pattern match missed
it; corrected in place. TD-150's four allow-listed English sentences (the scan screen's confidence hints, stuck on
a static converter with no localizer; the diagnostics panel's empty-log text, pinned to literal constants, not
actually a reference-equality problem as the row's wording suggested) are now keyed in both languages, and
`NoSentenceLiteralsInUiCodeTests`'s allow-list is empty. The row's other, non-sentence findings — the unlock
screen's title, diagnostics status words, enum lists — stay open, untouched.

**Post-closure, 2026-10-06: TD-97's thirteen-observation flake, narrowed.** Every vault-backed integration class
shares one parent temp directory, and xUnit's default per-class parallelism (44 classes, 82% of the integration
suite, measured not guessed) races `Directory.CreateDirectory` against it — the diagnosis unchanged since
iteration 15. Serialising all 44 into one collection was rejected on a measured cost: that trades a roughly
1-in-4 race for a fully single-threaded suite. `TempVault`'s directory creation now retries, bounded, 20
attempts. **Honestly reported as a mechanism fix, not a caught-then-fixed one**: five consecutive full runs on
each side of the fix were all green, so the original flake was never reproduced here either way. Narrowed, not
closed.

**Post-closure, 2026-10-06: TD-150 fully closed.** Its five remaining non-sentence findings — the unlock title,
diagnostics status words, "Copied!", three file-picker titles, and four enum lists showing raw `.ToString()`
output (account type, rule field, rule operator, adjustment kind) — all speak the user's language now, through
the same slot/converter patterns the sentence-level pass already established. The guard that catches English
sentences in C# couldn't see short labels or raw enum names, so `NoSentenceLiteralsInUiCodeTests` gained two more
patterns (title-case literals, exclaimed words) rather than leaving this class of gap able to recur silently. One
real bug found mid-fix and corrected: an adjustment's description still read the raw enum member name after
everything around it had been converted.

**Post-closure, 2026-10-06: TD-153 attempted and abandoned, flagged for review.** A Media Foundation/DirectShow
camera enumerator was drafted but two agents working the same files concurrently (a resumed background agent and
a second one dispatched without knowing the first was still alive) forced the attempt to be killed before the
real risk was ever checked: whether Media Foundation's enumeration order reliably corresponds to OpenCV's
`VideoCapture(index)` ordering. A wrong mapping there is a silent wrong-camera bug, not a missing feature, so
nothing was kept rather than ship it unverified. The register row is flagged for a dedicated review before any
retry, not left to look like ordinary open work.

**Post-closure, 2026-10-07: TD-151 built, TD-66 fixed alongside it.** The project owner decided to build real
save/reuse rather than delete the three dead requests. The import wizard now offers saved profiles at the mapping
step, pre-fills one on Apply, and saves or updates one through the two commands that previously compiled and were
sent by nothing. Doing so forced a real fix: the import key's source identifier moved from the profile's
user-editable name to its immutable id, because the name was only stable before this item as a throwaway constant
— a real, reusable, user-named profile would have broken duplicate detection the moment a name changed. **One
real bug caught in review**: the agent finishing this item found a validation check (profile must map a Date and
an Amount column) disabled behind a stray `if (false && ...)` — its own predecessor's mutation-testing pass, left
live in production code when the agent died before reverting it. Fixed before this landed.

**Post-closure, 2026-10-07: TD-122's Status column corrected, and TD-123 built on top of it.** TD-122's row read
"Done" in its Impact column and "Open" in Status for ten iterations — re-verified directly (real `ExcelImporter`,
no `PdfImporter`, the picker offers exactly csv/xlsx/xls) and the column fixed to match. That decision unblocked
TD-123, whose own Fix column had waited on it: the wizard's column-mapping step read headers only for CSV, so
`AvailableColumns` stayed empty for a spreadsheet even though `ExcelImporter` itself (landed by TD-151/TD-66)
already honoured `ColumnMappings` by name. A new `IImportHeaderReader`, segregated from `IImporter` so test
doubles that only import don't need it, lets the wizard ask whichever importer the factory hands it for headers —
no per-format dispatch left in the view model, and any future importer gets this for free. Verified end-to-end: a
real `.xlsx` with non-default header names, mapped and imported through a real vault, lands the right rows.

**Post-closure, 2026-10-07: TD-106, a guard against a defect class rather than a fix to one instance.** Eleven
known sites of TD-24's "raw exception message bound to a screen" defect were already fixed; nothing stopped a
twelfth. A source-text scan — this repo's established technique for inside-a-method-body checks NetArchTest can't
express, following `NoSentenceLiteralsInUiCodeTests`' own precedent — walks every catch block in
`Pine.Ui.Avalonia` and flags an `ex.Message`/`.ToString()` (direct, or through one level of local-variable alias)
landing on a bound, user-facing property. Ran clean against the real tree. **Falsified with five mutation
shapes, three caught and two honestly not**: direct assignment, aliasing, interpolation and a wrapped-line
right-hand side are all caught; helper-method indirection and a true multi-line right-hand side are stated as
real limits of a text-scan technique, not papered over.
