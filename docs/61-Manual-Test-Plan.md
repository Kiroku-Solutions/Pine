# 61 · Manual Test Plan — Phase 0 items 1–9

| Field | Value |
|---|---|
| Document ID | `PINE-DOC-61-MANUALTEST` |
| Purpose | Verify by hand what the automated suite cannot prove about the remediation in `60-Production-Readiness-Audit.md` §12. |
| Covers | Phase 0 items **1–9** → TD-01, TD-02, TD-03, TD-04, TD-05, TD-10, TD-11, TD-12, TD-13, TD-23 |
| Automated baseline | 141 unit + 99 integration = **240**, currently **239 passing / 1 failing** (TD-74, pre-existing). **§9 has no automated coverage at all** — unlike every other section here, it is the only evidence TD-11 has. |
| Created | 2026-08-27 · updated 2026-08-28 (items 8 and 9) |
| Related | `60-Production-Readiness-Audit.md`, `50-Test-Scenarios.md`, `53-Release-Checklist.md` |

> **How to read the markers.**
> **[M]** — do this by hand; no automated test covers it.
> **[S]** — spot-check only; already machine-proven, listed so a UI-level regression is still caught.
> **[G]** — **known gap.** The expected result *is* the broken behaviour. Confirm it still behaves as documented and do **not** file a bug — it already has a TD number.

---

## 0. Preflight

**Back up your real vault before running anything in §1 or §7.** Two of these tests apply
schema migrations to a live vault.

Locations (Windows):

```
%LOCALAPPDATA%\Pine\vaults.json          registry of vaults
%LOCALAPPDATA%\Pine\<vault-name>\        one directory per vault
%LOCALAPPDATA%\Pine\<vault-name>\backups\  pre-migration backups
%LOCALAPPDATA%\Pine\crash.log            last unhandled exception
```

Run the app:

```
dotnet run --project src/Pine.Ui.Windows
```

Confirm the automated baseline first — if this is not green, stop and fix that before
trusting anything below:

```
dotnet test tests/Pine.Tests.Unit          -> 141 passed
dotnet test tests/Pine.Tests.Integration   -> 99 passed
```

**Note on database inspection.** The vault is SQLCipher-encrypted with a key derived from the
master password, so you cannot open it with a plain `sqlite3` shell. Everything below is
verified through the UI, except the backup-encryption test in §2, which only needs the file to
*fail* to open.

---

## 1. Vault header and KDF — item 1, TD-05 / TD-23

The most irreversible work in the whole remediation. Test it first.

**1.1 [M] A new vault writes a versioned header.**
Create a fresh vault. Open `%LOCALAPPDATA%\Pine\<vault>\pine.vault.json`.
*Expected:* a JSON header naming the KDF (Argon2id) and every cost parameter — memory, iterations,
parallelism, salt. If this file is missing, the vault is unversioned and a future parameter change
would brick it.

**1.2 [M] A pre-remediation vault still opens.**
If you have a vault created before this work, open it. If you do not, simulate one: close the app,
rename `pine.vault.json` to `pine.vault.json.bak`, and confirm `pine.salt` is still present.
*Expected:* the vault unlocks normally with its existing password, and a `pine.vault.json` is
adopted for it. **A failure here means every existing vault is locked out — stop and report.**

**1.3 [M] A wrong password says one thing, always.**
Enter a wrong password on an existing vault.
*Expected:* a single generic failure message. It must **not** mention schema, migration, EF Core,
or a file path. Then enter the correct password on a vault whose schema is behind — the migration
must report itself as a migration, not as a wrong password (TD-23).

**1.4 [S] Password change / biometric unlock.**
*Expected:* **not available.** There is no envelope key yet (TD-06 open). Confirm the UI does not
offer either.

---

## 2. Backup before migration — item 2, TD-10 (partial)

**2.1 [M] A migration writes a backup first.**
Open a vault whose schema is behind the current model — the `ImportKey` migration from item 7 gives
you one for free if the vault predates it.
*Expected:* `<vault>\backups\<timestamp>-PreMigration.db` appears **before** the migration runs.

**2.2 [M] That backup is encrypted.**
Run `sqlite3 "<backup>.db" ".tables"` against it.
*Expected:* it fails — `file is not a database`. If it lists tables, the backup is plaintext and
that is a privacy failure, not a minor bug.

**2.3 [S] Old backups are pruned.**
Trigger several migrations.
*Expected:* only the most recent N are kept.

**2.4 [G] There is no way to restore, and no way to export.**
Search the whole UI for a restore option or a "back up now" action.
*Expected:* **neither exists.** `ExportVaultBackupCommand` is implemented but no screen invokes it,
and there is no restore path at all — recovery means copying files by hand. This is TD-56 and the
open half of TD-10.

---

## 3. CI — item 3, TD-07 / TD-08 (regressed)

**3.1 [G] CI is manual-only.**
Open `.github/workflows/dotnet-ci.yml`.
*Expected:* `on: workflow_dispatch:` and nothing else. The `push` / `pull_request` triggers were
removed in `79c5364` to save compute minutes, so the regression net exists but is never armed.
This item was ticked in iteration 1 and regressed afterwards — see §12.12 of the audit.

**3.2 [M] Dispatch it once by hand.**
Run the workflow manually from the Actions tab.
*Expected:* unknown — **it has never executed.** Whatever happens is new information worth
recording in the audit.

---

## 4. Atomicity — item 4, TD-01

**4.1 [S] A transfer moves both legs or neither.**
Create two accounts in the same currency. Transfer between them.
*Expected:* exactly two transactions, both balances moved, no orphan leg.

**4.2 [M] Kill the app mid-write.**
Start an import of a large CSV and kill the process from Task Manager while it runs.
*Expected:* on restart, either every row of that import is present or none is. Never a partial file.

**4.3 [M] Settings survive a restart.**
Change theme, language and any vault-security setting. Close the app fully. Reopen.
*Expected:* all of them persisted. This path used to write through a repository that bypassed the
transaction boundary, so it is a genuine regression risk.

**4.4 [M] A category created from Diagnostics persists.**
Open Diagnostics, create a category, restart the app.
*Expected:* the category is still there. This was an unpredicted silent data-loss path found during
iteration 2.

**4.5 [M] First-run choices persist.**
Create a new vault and make deliberate choices on the setup screen — storage mode, telemetry opt-in.
Restart.
*Expected:* both are honoured, not reset to defaults.

---

## 5. Splits and owned collections — item 5, TD-04 / TD-13

**5.1 [M] Receipt line items round-trip.**
Scan or load a receipt that produces several line items. Save the transaction. Close the app,
reopen, and open that transaction.
*Expected:* every line item is still present with its description, quantity and prices. Before this
fix, an update silently dropped the whole owned collection. **This is the single most important
manual test in §5** — the automated proof exists, but only this exercises the real UI path.

**5.2 [M] Re-previewing an import does not duplicate the category.**
In the import wizard, go to the preview step, go back, and return to the preview so AI
categorization runs twice on the same rows.
*Expected:* each transaction ends with exactly **one** split, not two. `AddSplit` was replaced with
`ReplaceSplits` at both call sites for this reason.

**5.3 [G] Multi-split transactions have no UI.**
*Expected:* you cannot create a two-way split from any screen. The domain now supports it and it is
covered by tests, but nothing surfaces it. Not a regression — it was never built.

---

## 6. Validation and currency — item 6, TD-12

**6.1 [M] A mismatched currency is refused cleanly.**
Create a USD account. Try to add a transaction denominated in EUR.
*Expected:* a readable message naming both currencies and the account. **No transaction row is
created and the balance does not move.** Before this fix the transaction was saved and *then* the
balance update threw.

**6.2 [M] A cross-currency transfer is refused up front.**
Create a USD account and a EUR account. Attempt a transfer.
*Expected:* refused before anything moves, with both currencies named.

**6.3 [M] Lowercase currency codes are accepted.**
Enter `usd` in lowercase anywhere a currency is typed.
*Expected:* accepted and normalised to `USD`. The domain always accepted it; the transfer handler
used to reject it by comparing raw strings.

**6.4 [G] Non-currency errors still come from the domain.**
Add a transaction with an amount of `0`.
*Expected:* the message is `Deposit amount must be strictly positive.` — a domain exception, not a
clean validation failure. Same wrong-layer class TD-12 fixed, different field. This is TD-62.

---

## 7. Import de-duplication and review list — item 7, TD-03

The largest surface in this plan, and the one with the most never-observed UI.

Prepare a CSV with a date column, a payee column and an amount column. Include **two identical
rows** — same date, same payee, same amount.

**7.1 [M] Two genuine identical purchases both import.**
Import that file.
*Expected:* **both** rows import. This is the original bug: two €3.50 coffees on the same day used
to collapse into one, silently.

**7.2 [M] Re-importing the same file imports nothing.**
Import the exact same file a second time.
*Expected:* zero rows imported, and **every** row listed on the review step as a duplicate with a
reason. Nothing is discarded without telling you.

**7.3 [M] The review step actually renders.**
Look at that review step carefully — column alignment, counts, the reason text, scrolling with many
rows, and the Finish button.
*Expected:* it looks like the rest of Pine. **Nobody has ever seen this screen.** Its bindings are
compiler-checked and its brushes were reasoned from the existing theme, but no GUI was ever
launched. Anything visually wrong here is expected and worth reporting precisely.

**7.4 [M] The same file imports fully into a second account.**
Import your file into account A, then import the *same file* into account B.
*Expected:* every row imports into B. During iteration 5, removing the account filter from the
duplicate probe left **all 79 integration tests green** while causing silent cross-account data
loss. This test is the human version of the one that closed that hole.

**7.5 [M] Pre-existing transactions do not interfere.**
Use a vault that already had transactions before this work — their import key is `NULL`.
*Expected:* they neither match incoming rows nor block them. Import a file whose rows resemble them
and confirm the rows still import.

**7.6 [M] An existing vault survives the migration.**
Open a vault created before iteration 5.
*Expected:* it opens, a pre-migration backup appears (§2.1), and imports work afterwards. **A
failure here locks users out of their vault — stop and report.**

**7.7 [G] Renaming the import profile invalidates every key.**
Note the profile name, import a file, rename the profile, re-import the same file.
*Expected:* every row reports as **new**, not duplicate. The derived key uses `ImportProfile.Name`
as its source field, so a rename is a data event. This is TD-66 and it is a real fragility, not a
cosmetic one.

**7.8 [M] A file with a reference column dedups regardless of row order.**
Map a `Reference` column in the wizard. Import, then shuffle the rows and import again.
*Expected:* all rows reported as duplicates. With a stable reference the key uses the `r1:` scheme
and does not depend on position.

**7.9 [G] Without a reference column, reordering defeats dedup.**
Import a file with no `Reference` mapping, shuffle its rows, import again.
*Expected:* rows report as **new**. This is a deliberate trade recorded in `ImportKeyFactory`: the
row index is part of the derived key so two genuinely identical rows stay distinct. A false "new
transaction" is visible and correctable; a false "duplicate" destroys data silently.

**7.10 [G] Excel and PDF import nothing.**
*Expected:* `ExcelImporter` and `PdfImporter` return an empty list. Both are stubs and neither is
reachable from the wizard, which hardcodes `CsvImporter`. This is TD-67.

**7.11 [G] A duplicate row in the wrong currency is not reported.**
Re-import a file whose rows are in a currency the account does not use.
*Expected:* the skipped duplicates bypass the currency guard, so the file looks clean on re-import
even though its first import was rejected. This is TD-64.

**7.12 [G] Concurrent imports can both write.**
Two simultaneous imports of the same file into one account.
*Expected:* both may pass the existence probe before either inserts. The `(AccountId, ImportKey)`
index is deliberately non-unique. This is TD-65 and it is untested.


---

## 8. Voiding and balance repair — item 8, TD-02

The correction mechanism. Everything here moves money, so **use a throwaway vault**.

Automated coverage is deep (`VoidTransactionAtomicityTests`, `BalanceRecalculationTests`,
`VoidAndOpeningBalanceMigrationTests`, and four falsification mutations recorded in audit §12.14), so
most of what follows is **[S]**. The **[M]** items are the ones no test can reach: what the screen
actually shows.

**8.1 [M] A transaction can be voided, and the balance comes back.**
Note an account's balance. Add an expense of 25.00. The balance drops by 25.00. On the transactions
list, press **Void** on that row, type a reason, confirm.
*Expected:* the row is **still there**, dimmed and struck through, with a red **VOIDED** badge. The
balance is back to what it was. Hovering the badge shows the reason you typed.
*If the row disappears instead:* stop — a void must never delete. That is the entire point of the
feature over a delete.

**8.2 [M] Voiding one leg of a transfer voids the other.**
Create two accounts. Transfer 100.00 from A to B. Both balances move. Now void **either** leg.
*Expected:* **both** rows show VOIDED, and **both** balances return to their starting values. Then
repeat the whole test voiding the *other* leg first — the two links are established by two different
mechanisms in the code, so each direction can break on its own.
*If only one leg voids:* stop. Money has left one account and arrived nowhere.

**8.3 [M] A reason is required.**
Open the void confirmation and press **Void transaction** with the reason box empty.
*Expected:* a refusal in red inside the dialog — "A reason is required to void a transaction." —
nothing is voided, and the dialog stays open. The message must be a sentence aimed at you, not an
exception type or a file path.

**8.4 [S] A transaction cannot be voided twice.**
The **Void** button is hidden on an already-voided row. There is no supported way to reach this from
the UI; if you find one, the command still refuses and nothing changes.

**8.5 [M] Voided rows survive a restart and a re-import.**
Void an imported transaction. Close and reopen the vault — it is still voided, with its reason. Now
import the same statement file again.
*Expected:* the row is reported as an **already-imported duplicate**, not re-created. A void does not
make a row importable again; otherwise the next import would silently undo your correction.

**8.6 [M] Balance repair reports rather than reassures.**
Go to **Settings → Data Integrity**. Press **Check Balance** on an account you have used.
*Expected:* a sentence naming the balance and how many transactions it was computed from, e.g.
*"'Main' agrees with its ledger: 875.00 USD across 3 transactions."*
*If it says the balance was repaired:* that is a real finding — record the exact figures and the drift
before doing anything else. It means a balance and its transactions had disagreed.

**8.7 [G] An account created before 2026-08-28 cannot be checked the first time.**
On a vault that existed before this build, the first **Check Balance** on any account reports that no
opening balance was on record, that one was inferred, and that *"This first check could not detect
drift; run it again from now on and it will."*
*Expected:* exactly that wording, and a second press then reports a normal agreement.
**This is documented behaviour, not a bug** (TD-70). The opening balance of an existing account is
genuinely unrecoverable — any figure derived from the current balance assumes that balance is
correct, which is the assumption the check exists to question. Do not file it.

**8.8 [S] A newly created account checks cleanly straight away.**
Create an account with an opening balance of 500.00, add nothing, and press **Check Balance**.
*Expected:* agreement, 0 transactions, and **no** "inferred" wording — a new account records its
opening balance at creation.

**8.9 [M] Nothing in this section ever shows you an exception.**
Every refusal above must read as a sentence written for a person. If any of them shows a type name, a
stack trace, a file path or the phrase "money of different currencies", that is TD-24 leaking on a new
path — report it with the exact text.

---

## 9. Password floor and idle auto-lock — item 9, TD-11

**Read this before you start.** Every other section in this plan sits on top of automated coverage
and asks you to check what a test cannot see. This one does not: **no test in `tests/` covers any of
TD-11**. The behaviour was verified once, by hand, with a throwaway harness that was never
committed (audit §12.16). So here almost everything is **[M]**, and a failure below is not
necessarily a regression — it may be the first time anyone has looked.

Use a **throwaway vault** for §9.1–§9.5. §9.12 needs a vault that already existed before this build;
if you do not have one, say so and skip it rather than faking it — it is the most important test in
the section.

**§9.6 costs eleven real minutes of not touching the machine.** There is no setting to shorten the
timeout (it is fixed at ten minutes and not persisted — see §9.13). Plan for it; do not fake it by
changing the system clock, because the watchdog reads a monotonic `TimeProvider` and a clock jump
does not exercise the same path.

### The floor, at vault creation

**9.1 [M] A one-character password is refused.**
Start a new vault. In the master-password box type `a`. Try to continue.
*Expected:* the vault is **not** created, and the screen says the password does not meet Pine's
minimum strength, followed by advice on what to change.
*This is the audit's literal finding* — "a one-character master password is accepted". If a vault is
created, item 9 has not been done.

**9.2 [M] The strength meter renders, and it moves as you type.**
Type slowly into the password box: `p`, `pa`, `pas`, … up to `password`, then keep going with random
characters until the meter turns positive.
*Expected:* a bar and a sentence that update on **every keystroke** with no lag, an estimated
strength that **never goes down when you add a character**, and a clear statement of whether it
passes.
*This has never been rendered.* It compiles; nobody has seen it. Screenshot whatever appears, even
if it looks right.

**9.3 [M] The advice is written for a person.**
Try each of these and read what comes back: `password` · `P@ssw0rd` · `qwerty123456` ·
`aaaaaaaaaaaa` · `Password1!` · `Sunshine!2024`. All six must be refused.
*Expected:* plain sentences — "Use at least 10 characters", "…pick something less guessable", and
similar. Each refusal should tell you something you can act on.
*If you see `BelowEntropyFloor`, `LimitedCharacterVariety`, `PasswordWeakness` or any other enum
name, type name or bit count presented as an error:* that is a real defect. The domain deliberately
returns only enums so the UI can choose the words; an enum reaching the screen means it did not.

**9.4 [M] Adding punctuation cannot buy a pass.**
Type `Password1!` (refused), then keep appending: `Password1!!`, `Password1!!!`.
*Expected:* it stays refused. The estimate may rise slightly but must not cross the floor from
appended junk alone.
*If padding a known-bad password gets you in*, the estimator's monotonicity is broken and the floor
is decorative.

**9.5 [M] The floor is not a wall — and an accepted password really works.**
Create the vault with `gravel-thicket-cinder-9271`.
*Expected:* accepted. Then **close the app completely, reopen it, and unlock with the same
password.** It must open.
*This second half is the point:* it catches a password being trimmed, case-folded, re-encoded or
otherwise mangled between the setup screen and the KDF. A vault you can create but not reopen is
total data loss, and it would look like success at creation time.

**9.6 [M] A mistyped password is refused, not accepted.**
Type a strong password in the first box and a different one in the confirm box.
*Expected:* refusal, naming the mismatch, and the vault is not created.
*Why this matters more than it looks:* Pine has no recovery phrase. A typo here is not an
inconvenience, it is a vault nobody can ever open again.

### The idle lock

**9.7 [M] It fires. — eleven minutes, hands off.**
Unlock a vault, land on the dashboard, then **leave the machine completely alone for eleven
minutes.** Do not move the mouse; a pointer move counts as activity. Do not let the screensaver or
sleep kick in, or you are testing the OS, not Pine.
*Expected:* Pine returns to the vault picker on its own, with a message saying it locked the vault
after ten minutes of inactivity.
*If nothing happens after ~12 minutes:* the watchdog is not armed on this head. Record it — the
whole item hangs on this.
*If it locks much sooner than ten minutes:* activity reporting is not reaching the session.

**9.8 [M] Working continuously does not get you locked out.**
Unlock, then use the app **continuously for fifteen minutes** — click between screens, type into a
form, scroll a list. Do not idle for more than a minute at a stretch.
*Expected:* no lock, ever.
*If it locks while you are actively working*, that is the worst possible outcome of this feature and
it is worse than not shipping it. Stop and report it with what you were doing at the moment it fired.

**9.9 [M] The lock explains itself.**
Read the message on the vault picker after §9.7 fires.
*Expected:* a sentence that tells you Pine locked the vault because you were away, and how long it
waited. It must not look like a crash, a logout, or an error.
*If you are simply dumped on the picker with no explanation:* users will read that as the app
crashing and losing their work. Report it.

**9.10 [M] Nothing decrypted survives the lock.**
Before the lock, navigate to **Transactions** so real rows are on screen. Let §9.7 fire. After it
does, look at the screen carefully, then unlock again.
*Expected:* no transaction rows, balances or account names remain visible anywhere behind or during
the picker, not even for a frame. After unlocking, the app starts you fresh rather than restoring the
screen you had.
*The key is gone at that point*, so anything still on screen is data that outlived the key that
protected it.

**9.11 [M] The manual lock button works.**
Unlock, find **Lock Vault** in the sidebar, press it.
*Expected:* immediate return to the vault picker. The message must read as *your* action, not as an
idle timeout — the two reasons are distinguished in code and the wording must reflect that.

**9.12 [M] An existing vault with a weak password still opens. — do not skip this.**
Take a vault created **before this build**, whose password would not pass §9.1–§9.3 today (a short
one, or `Password1!`). Unlock it.
*Expected:* **it opens normally.** The floor applies at **creation only**.
*If an existing vault is refused at the unlock screen, stop everything and report it immediately.*
That is not a strict security policy, it is every existing user permanently locked out of their own
data by an upgrade, with no recovery phrase to fall back on. This is the single highest-consequence
failure mode in the whole item.

**9.13 [M] The password never reaches disk.**
After doing §9.1–§9.6, open `%LOCALAPPDATA%\Pine\crash.log` if it exists, and look at any log or
trace file in the vault directory.
*Expected:* none of the passwords you typed appear anywhere, in any form — not the accepted one, not
the rejected ones.

### Presentation

**9.14 [M] The meter survives a theme change.**
With the password box showing a refusal, switch the app between light and dark (and any accent).
*Expected:* the bar and its text stay legible in both. Pine themes at runtime, so a hardcoded colour
shows up here as text that vanishes into its background.

**9.15 [M] Pass and fail are not signalled by colour alone.**
Squint, or take a screenshot and desaturate it.
*Expected:* you can still tell a passing password from a failing one from the **words**, not just
from red versus green (`40-Accessibility`).

### Known gaps — confirm, do not report

**9.16 [G] The timeout is ten minutes and cannot be changed.**
Look in **Settings** for an auto-lock or timeout control.
*Expected:* **there is none.** `SetIdleTimeout` exists in code but nothing writes the choice through
`SaveSettingCommand`, so ten minutes is the only value any user will ever have. Documented in audit
§12.16 INCOMPLETE item 4. Do not file it.

**9.17 [G] Minimising, suspending or locking the OS does not lock the vault.**
Unlock, then minimise Pine, or lock Windows itself, and come back inside ten minutes.
*Expected:* still unlocked. Only elapsed idle time locks it; `VaultLockReason.ApplicationSuspended`
exists but nothing raises it. Audit §12.16 INCOMPLETE item 3. Do not file it.

**9.18 [G] Nothing re-authenticates before a sensitive action.**
An unlocked session will export a backup or void a transaction without asking for the password
again. That is the third noun in TD-11's title and it is deliberately not built — it is worth little
before the envelope key (TD-06). Do not file it.

**9.19 [G] A long passphrase scores higher than it deserves.**
Try `correct horse battery staple` at the setup screen.
*Expected:* accepted, with a very high strength reading. Its true strength is roughly 44 bits, well
under the 60-bit floor — there is no word list, so the estimator reads it as random letters. Every
limitation of the estimator errs *generous*, which is why the floor carries margin. This is **TD-73**;
confirm the behaviour and do not file it.

**9.20 [G] None of §9 can be tested on macOS, Web, Android or iOS.**
Web, Android and iOS construct no root view at all, so they show nothing to lock (TD-08, sharpened in
audit §12.16). macOS builds and is wired identically to Windows but has never been run by hand.
Android and iOS have never been compiled. Test on Windows and record that as the scope.

---

## 10. What a failure means

| Where it fails | Severity |
|---|---|
| §1.2, §7.6 | **Stop everything.** Users cannot open their vault. |
| §2.2 | **Stop everything.** Backups are plaintext; the privacy claim is false. |
| §4.x, §5.1 | Data loss on a write path. Report with exact steps before continuing. |
| §6.1, §7.1, §7.2, §7.4 | The remediation did not hold. Reopen the TD in §8 of the audit. |
| §7.3 | Cosmetic to structural. Describe precisely what looks wrong. |
| §8.1, §8.2, §8.5 | A correction that half-happened. **Stop.** This is the defect the feature exists to prevent. |
| §8.6 | A repair tool that made a balance worse. **Stop everything.** |
| §9.12 | **Stop everything.** An existing user is locked out of their own vault by an upgrade, and there is no recovery phrase. The worst outcome in this document. |
| §9.5 (second half) | **Stop everything.** A vault that can be created but not reopened is total data loss that looks like success. |
| §9.1, §9.4 | The floor is decorative. Item 9's stated purpose is unmet — reopen TD-11. |
| §9.8 | Locking an actively-working user. Worse than not shipping the feature; disable the watchdog until fixed. |
| §9.7, §9.10, §9.11 | The lock does not fire, or does not take the data with it. Reopen TD-11. |
| §9.2, §9.3, §9.9, §9.14, §9.15 | Presentation defects on a screen nobody has ever rendered. Report with a screenshot. |
| any **[G]** behaving *better* than documented | Good news — verify it, then correct the audit. |

---

## 11. Not covered here

- **Android and iOS.** Not compilable locally; workloads absent. Nothing about those heads has ever
  been compiled, let alone run.
- **macOS, Web.** They build, but no head other than Windows has been exercised by hand.
- **Load and scale.** No test here uses a realistic vault size. `GetPagedAsync` sorts by `Date` and
  `GetByAccountIdAsync` filters on an unindexed `AccountId` (TD-30), and SQLite stores `decimal` as
  TEXT so any comparison reaching SQL is lexicographic (TD-31). Both are open.
- **Editing a transaction.** Still does not exist (TD-68). Voiding does — see §8 — so a bad record
  is no longer permanent, but correcting one means voiding it and entering it again.
- **Undoing a void** (TD-69). Once voided, a transaction stays voided.
- **Any automated proof of item 9.** §9 is it. The behaviour passed a 25-check harness once
  (audit §12.16) but that harness was never committed, so nothing re-runs it and nothing will
  catch a regression in the entropy estimator, the watchdog or the key zeroing. Porting it into
  `tests/Pine.Tests.Unit` is the first item of audit §12.17.
- **A configurable auto-lock timeout** (§9.16), **lock on suspend or minimise** (§9.17), and
  **re-authentication before sensitive actions** (§9.18). None exist; all three are recorded in
  audit §12.16.
- **Accounts created before 2026-08-28.** Their first balance check anchors rather than checks, and
  says so on screen (TD-70). §8.7 covers exactly that.
