# 62 — Post-Audit Implementation Plan

| Field            | Value                                                                                |
|------------------|--------------------------------------------------------------------------------------|
| Document ID      | `PINE-DOC-62-POST-AUDIT-PLAN`                                                        |
| Status           | Non-normative — working plan, superseded by its own execution                        |
| Reconciled       | 2026-09-09 · Written at the close of iteration 37, when all four phases of `60-Production-Readiness-Audit` §10 were ticked (37/37) and 68 debt rows remained open. It plans what comes after the audit's roadmap, not the roadmap itself. Every item names the row it closes, so this document goes stale the moment those rows do — read the register, not this, for current state. |
| Owner            | Engineering Review Board                                                             |
| Created          | 2026-09-09                                                                           |
| Last Updated     | 2026-09-13 — Stage 0 closed (§3.1–§3.3); **Stage 1 closed**, items 1.1–1.5 (§4.1–§4.5); Stage 2 items 2.1 (§5.1), 2.2 (§5.2, everything but the certificate), 2.3 (§5.3) and 2.4 (§5.4) |
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
| 2.5 | `TD-148`, `TD-117`'s remaining half, `TD-138`, `TD-120`, `TD-21` | several | Small, mechanical |

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
