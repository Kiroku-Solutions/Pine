# 62 — Post-Audit Implementation Plan

| Field            | Value                                                                                |
|------------------|--------------------------------------------------------------------------------------|
| Document ID      | `PINE-DOC-62-POST-AUDIT-PLAN`                                                        |
| Status           | Non-normative — working plan, superseded by its own execution                        |
| Reconciled       | 2026-09-09 · Written at the close of iteration 37, when all four phases of `60-Production-Readiness-Audit` §10 were ticked (37/37) and 68 debt rows remained open. It plans what comes after the audit's roadmap, not the roadmap itself. Every item names the row it closes, so this document goes stale the moment those rows do — read the register, not this, for current state. |
| Owner            | Engineering Review Board                                                             |
| Created          | 2026-09-09                                                                           |
| Last Updated     | 2026-09-11 — Stage 0 closed (§3.1–§3.3); Stage 1 items 1.1 and 1.2 closed (§4.1, §4.2) |
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

Ordered by what a user loses if it is wrong.

| # | Work | Closes | Why here |
|---|---|---|---|
| 1.1 | ✅ **Done** (2026-09-10) — **Restore from a backup, in the application** | TD-56 (closed) | Restore refuses anything it cannot verify first, copies what it is about to replace, and locks the vault afterwards. See §4.1 |
| 1.2 | ✅ **Done** (2026-09-11) — **Fix the navigation defect that replaces the whole shell** | TD-121 (closed) | The shell's navigator is now a type of its own that the container cannot supply, so a page can only get it from the shell it is shown in. The defect was also a lock leak the row did not name. See §4.2 |
| 1.3 | **Dashboard: make the ten inert widgets real, or take them down** | — | Net worth, expenses, income, savings, goals, investments, charts, calendar, AI suggestions and the budget tile render "No data yet" permanently. They are honest — no fabricated numbers — but ten of twelve tiles are furniture. Build the three or four that the existing queries already support (net worth, income, expenses are derivable today) and delete the rest until their subsystem exists. **This is TD-99's lesson applied before it becomes TD-99 again** |
| 1.4 | **The dashboard's "Scan Receipt" button** | — | It has no `Command` binding at all: it renders, it is clickable, it does nothing. One line |
| 1.5 | **Live scanner camera list** | — | Enumerates "Camera 0".."Camera 3" regardless of hardware. Either enumerate real devices or drop the picker |

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

**2026-09-10: Stage 0 is closed, and that sentence is struck through in §11.1 rather than deleted.**
Three items, three commits, three guards, each falsified before being believed. What replaced the
sentence is narrower and honest: CI is armed, derives what it builds from the solution, and every
gate proves it ran — and **no run has yet happened**, because opening a pull request against the
real remote is the one step none of this can do for itself (`61-Manual-Test-Plan` §3.2). Until that
is done, Stage 0 is complete in the repository and unproven on the server, which is a smaller claim
than "finished" and the only one the evidence supports.

~~The next item is 1.1, restore from a backup in the application.~~ Done 2026-09-10 (§4.1), and
1.2, the navigation defect, on 2026-09-11 (§4.2). The next item is 1.3: the dashboard's ten inert
widgets.
