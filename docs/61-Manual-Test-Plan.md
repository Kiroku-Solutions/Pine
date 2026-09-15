# 61 · Manual Test Plan — Phase 0 items 1–9

| Field | Value |
|---|---|
| Document ID | `PINE-DOC-61-MANUALTEST` |
| Purpose | Verify by hand what the automated suite cannot prove about the remediation in `60-Production-Readiness-Audit.md` §12. |
| Covers | Phase 0 items **1–9** → TD-01, TD-02, TD-03, TD-04, TD-05, TD-10, TD-11, TD-12, TD-13, TD-23 |
| Automated baseline | 141 unit + 99 integration = **240**, currently **239 passing / 1 failing** (TD-74, pre-existing). **§9 has no automated coverage at all** — unlike every other section here, it is the only evidence TD-11 has. |
| Created | 2026-08-27 · updated 2026-08-28 (items 8 and 9) |
| Related | `60-Production-Readiness-Audit.md`, `50-Test-Scenarios.md`, `53-Release-Checklist.md` |
| Status | Non-normative — manual test procedures |
| Reconciled | 2026-09-14 · Procedures for the behaviour no automated test reaches. Written, and largely not yet executed - which the audit records at each iteration. Sections 13A-13C were added by post-audit plan items 1.3-1.5 and 2.1, 13E by item 2.5, 13F by item 3.0 and 13G by item 3.1a, which also rewrote §13.6 and §13.8's expected sentences: the figure and the date in them now follow the display language, where §13.8 used to say they deliberately did not. None of these has been run on a screen yet, and §3.2 is still owed a CI run on the server. |

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

**1.1 [M] A new vault writes a versioned header carrying a wrapped key.**
Create a fresh vault. Open `%LOCALAPPDATA%\Pine\<vault>\pine.vault.json`.
*Expected:* `"headerVersion": 2`, a `kdf` object naming Argon2id and every cost parameter — memory,
iterations, parallelism, key length, salt length — a `saltBase64`, and a `keyWrap` object with
`"algorithm": "AES-256-GCM"` plus `nonceBase64`, `ciphertextBase64` and `tagBase64`. Decode the
nonce and tag: they must be **12** and **16** bytes. If `keyWrap` is absent the vault has no
envelope and the database key is the password-derived key, which is the state TD-06 removed.

**1.2 [M] The wrapped key is not the password-derived key.**
Create two vaults with the *same* password. Compare their `keyWrap.ciphertextBase64` values.
*Expected:* they differ, and so do `saltBase64`, `keyWrap.nonceBase64` and `vaultId`. Two vaults
sharing a password must share nothing on disk. **If the two ciphertexts match, the data key is
being derived rather than generated and the envelope is decoration — stop and report.**

> **Superseded:** this step previously read "a pre-remediation vault still opens" and asserted that
> a `pine.salt`-only vault would be adopted into a versioned header. Iteration 13 (§12.27) removed
> legacy adoption deliberately: a pre-envelope vault is now **refused**, and that refusal is
> exercised by step 1.5 below. The change was taken while no vault existed outside a developer
> machine; see the audit's INCOMPLETE note 3 for why it is affordable exactly once.

**1.3 [M] A wrong password says one thing, always.**
Enter a wrong password on an existing vault.
*Expected:* a single generic failure message. It must **not** mention schema, migration, EF Core,
or a file path. Then enter the correct password on a vault whose schema is behind — the migration
must report itself as a migration, not as a wrong password (TD-23).

**1.4 [M] Password change — Settings → Master Password (TD-06, §12.27).**
Unlock a vault, open Settings and scroll to the "Master Password" card. Everything below is a
rendering claim that no test in the suite can make.

- *Meter.* Type into "New Password" and watch the bar and the sentence under it move together. The
  sentence must state pass or fail **in words** ("meets Pine's minimum" / "does not yet meet…"), so
  the state survives a colour-blind reader and a palette change. Confirm the advice line appears
  for a weak password and disappears for a strong one.
- *Confirmation.* Enter mismatched new passwords and press Change Password. *Expected:* it refuses
  with the mismatch sentence and **nothing is sent** — there is no recovery phrase, so a typo
  agreed to by both boxes is permanent lockout.
- *Wrong current password.* Enter a correct new password and a wrong current one. *Expected:*
  "The current password is incorrect. Your master password was not changed." Unlike the unlock
  screen this may name the cause: the vault is already open, so the typist has demonstrably passed
  authentication once and there is no oracle to protect.
- *Weak new password.* Try `hunter2`. *Expected:* refused before anything is written, and the
  message must **never** contain the password itself or its bit estimate.
- *The happy path, and the claim it makes.* Change to a strong new password. *Expected:* the three
  boxes clear, the vault **stays open** (you are not bounced to the unlock screen), and the message
  says the data was not re-encrypted. Then confirm the substance of that: the app must still list
  your transactions without re-prompting. This is the observable proof that the data key did not
  change.
- *The change actually took.* Lock the vault. Unlock with the **old** password — must fail with the
  generic message. Unlock with the **new** password — must succeed, and your data must all be
  there. This is the single most important row in §1.
- *Crash safety, if you can arrange it.* Kill the process during the change. *Expected:* the vault
  still opens with the **old** password. The header write is atomic, so the only two reachable
  states are old-header/old-password and new-header/new-password.

**1.5 [S] A pre-envelope vault is refused, and says why.**
If you still have a vault folder created before iteration 13 — one whose `pine.vault.json` says
`"headerVersion": 1`, or a folder with only `pine.salt` — attempt to unlock it.
*Expected:* a message saying the vault was created by a pre-release build that predates encrypted
key storage and cannot be opened, and that a new vault should be created. It must **not** blame the
password, must **not** say "corrupted", and must **not** print the folder path (TD-24).

**1.6 [S] Biometric unlock.**
*Expected:* **not available.** The envelope makes it architecturally possible, but no platform
secure storage is implemented (TD-87 open). Confirm the UI does not offer it.

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

## 3. CI — item 3, TD-07 / TD-08 (armed 2026-09-10)

**3.1 [G] CI is armed.**
Open `.github/workflows/dotnet-ci.yml`.
*Expected:* the `on:` block carries `pull_request: branches: [main]`, `push: branches: [main]` and
`workflow_dispatch:`. The first two were removed in `79c5364` and restored by plan item 0.1; a
regression here is not a matter of taste, and `BuildHygieneTests.CiIsArmedOnPullRequestAndPushToMain`
fails the suite if either goes missing.

**3.2 [M] Open a pull request that must fail, and watch it be rejected.**
Branch from `main`, add a test that asserts something false, push, open a pull request into `main`.
*Expected:* the `test` job goes red without anyone starting it, and the pull request shows the
failure. This is the step that distinguishes an armed workflow from a YAML diff — the trigger block
can be right while the run never happens (a disabled workflow, a fork's default permissions, a
branch-protection rule that was never configured). `package` is expected NOT to appear on the pull
request: it is held back by an `if:`, deliberately.

**3.3 [M] Then check the same commit on `main`.**
After the pull request merges, open the Actions tab.
*Expected:* a second run, from the `push` trigger, this time including `package`.

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

## 10. Per-vault categorisation model — Phase 1 item 4, TD-16

**Scope.** Windows only. `Pine.Ui.Macos`, `Web`, `Android` and `Cli` register no AI subsystem at
all (**TD-90**), so there is nothing on those heads to exercise. Record that as the scope rather
than as a failure.

**Why any of this is manual.** 62 unit tests and 6 integration tests cover the cipher, the store and
the categoriser, and none of them has ever run inside the application. What follows is the part that
only a running Pine can answer: whether the model is produced at all, where it lands, and whether
the vault it belongs to is the vault that gets it.

**Setup.** You need **two** vaults. Create `Personal` and `Business` with different passwords, and
know where each directory is. You will need a file explorer alongside Pine.

### The model is produced, and lands inside the vault

**10.1 [M] A fresh vault has no model.**
Create `Personal`, unlock it, and look in its directory.
*Expected:* no `ai` sub-directory. Pine ships no default model (`32-AI-Training` §6.1 says it does;
it does not — audit §12.29 INCOMPLETE item 4).

**10.2 [M] Training produces a model after enough categorised splits.**
Record at least **10** transactions with a category on a split, then leave Pine running and
unlocked. The trainer waits one minute after start, then polls every five, and needs ten *new*
categorised splits past its watermark.
*Expected:* within roughly six minutes, `Personal/ai/categorizer.model` appears.
*If it does not:* check the JSONL log for `ai.model_store.*` events before filing anything.

**10.3 [M] The model is not readable.**
Open `Personal/ai/categorizer.model` in a hex viewer, or just a text editor.
*Expected:* it begins with the ASCII bytes `PINEFC1` followed by a NUL. It must **not** begin with
`PK` — that would be a plaintext ML.NET zip, which is precisely the pre-TD-16 defect. Search it for
one of the payees you typed; you must not find it.

**10.4 [M] No model file appears next to the executable.**
Look in Pine's install directory for `Assets/Ai/categorizer_model.zip`.
*Expected:* it does not exist and is never created. That path was the defect.

**10.5 [M] No stray temporary file survives.**
Look in `Personal/ai/` after a training cycle.
*Expected:* exactly one file, `categorizer.model`. A leftover `.tmp` means the write-then-move path
failed halfway.

### The model belongs to one vault

**10.6 [M] Suggestions reflect what THIS vault learned.**
In `Personal`, start recording a transaction with a payee you have categorised many times.
*Expected:* the category is suggested.

**10.7 [M] The other vault does not inherit them.** *(the central row)*
Lock `Personal`. Unlock `Business`. Record a transaction with **the same payee**.
*Expected:* **no suggestion.** Business has never seen that payee. A suggestion here means one
vault's learned spending is leaking into the other through the categoriser, without either database
being decrypted — the exact failure TD-16 exists to prevent, and the most important row on this page.

**10.8 [M] Copying the file between vaults does not transplant the model.**
With Pine closed, copy `Personal/ai/categorizer.model` over `Business/ai/categorizer.model`
(create the directory if needed). Open `Business` and try 10.7 again.
*Expected:* still no suggestion, and the app behaves normally. The file is sealed under a key
derived from Personal's data key, so Business authenticates it, fails, discards it and carries on.
Check the log for `ai.model_store.authentication_failed`.

**10.9 [M] A corrupt model is survivable, not fatal.**
With Pine closed, flip a byte in `Personal/ai/categorizer.model` (or replace it with random bytes).
Open `Personal`.
*Expected:* the vault opens **normally**. Categorisation simply stops suggesting until the next
training cycle rebuilds the model. If the vault refuses to open, or an error dialog appears, that is
a real failure — a damaged derived cache must never block access to a user's own data.

**10.10 [M] Locking the vault stops categorisation without an error.**
Unlock `Personal`, wait for the ten-minute idle lock (or lock manually), and leave Pine running past
the next five-minute training tick.
*Expected:* no error dialog, no error in the log. At most a debug line saying the cycle was skipped.

### Known gaps — confirm, do not report

**10.11 [G] Training is not opt-in and cannot be turned off.**
Look in **Settings** for any control over AI training or categorisation.
*Expected:* **there is none.** Training runs unconditionally every five minutes on every unlocked
vault. `32-AI-Training` §12.2 says the fine-tune is opt-in per profile; it is not. This is **TD-89**,
registered in audit §12.29. Confirm and do not file it.

**10.12 [G] The model is not included in backups.**
Export or trigger a backup and inspect `Personal/backups/`.
*Expected:* only `pine-*.db`. The model is derived data and is deliberately not backed up; a
restored vault retrains. Audit §12.29 INCOMPLETE item 6. Do not file it.

**10.13 [G] Suggestions may be poorer than the training data implies.**
Training learns from the payee alone, while the import path asks about payee *and* notes. The model
is therefore served text shaped unlike what it was fitted on. This is **TD-91**. Do not file it.

**10.14 [G] There is no way to inspect, export or delete the model from inside Pine.**
Deleting the file by hand is the only mechanism, and it simply retrains. No export path exists, and
after TD-16 the file would be useless outside its vault anyway. Do not file it.

---

## 11. Receipt scan responsiveness — UX fix, not an audit item

**Scope.** Windows only, `Receipt Scanner` screen. This section is not tied to a `60-Audit` roadmap
row; it covers a defect reported directly by the product owner: **adding a photo froze the window
and Windows drew its "not responding" ghost over it for several seconds.**

**Why every row here is manual.** The cause was that the scan pipeline ran on the UI thread. No
automated test in this repository can observe that — detecting it needs a real Avalonia dispatcher
with a real window painting real frames, and the unit suite has none. A falsification mutation
confirmed this directly: putting the image decode back on the UI thread leaves all 540 unit tests
green. **The threading fix is a code-inspection claim until the rows below are executed.** Treat
them as the actual verification, not as a formality.

### The freeze is gone

**11.1 [M] The window keeps painting during a scan.** *(the row this whole change exists for)*
Open **Receipt Scanner**, click **Select Image...**, and choose a **large** photo — a real phone
photo, 4000×3000 or bigger, not a small test file. While it processes, drag the Pine window around
the screen and move the mouse over its buttons.
*Expected:* the window **moves normally and keeps redrawing**. The title bar must not grey out, and
Windows must never append "(No responde)" / "(Not Responding)" to the title.
*This failing is the original bug, unfixed.*

**11.2 [M] The spinner actually spins.**
During the same scan, watch the progress bar in the overlay.
*Expected:* it animates continuously. A **frozen** bar is worse than the old behaviour — it means
the UI thread is blocked again and the overlay is now lying about it.

**11.3 [M] The photo appears immediately.**
Watch the moment the file dialog closes.
*Expected:* the receipt image appears in the left panel **straight away**, before any text is
extracted. Previously nothing appeared until the whole pipeline finished.

### The overlay says what is happening

**11.4 [M] The stage messages advance in order.**
Watch the text under the spinner through a full scan.
*Expected, in this order:* `Loading the image...` → `Looking for the receipt...` →
`Reading the text...` → `Filling in the details...` → (if a merchant was found)
`Suggesting a category...`.
Each must appear **before** its slow step, never after it. `Reading the text...` is the long one.

**11.5 [M] The overlay blocks the form underneath.**
While the overlay is up, try clicking the Merchant box, the account dropdown and **Save
Transaction**.
*Expected:* nothing responds; clicks land on the scrim. The point is that an impatient second click
cannot queue up behind the first.

**11.6 [M] The overlay disappears on failure too, and says why.**
Select an image that is clearly not a receipt (a screenshot, a photo of a wall).
*Expected:* the overlay closes, the form becomes usable again, and the red error strip reads
*"Failed to process receipt: The provided image does not appear to be a receipt."* The app must not
be left permanently covered.

**11.7 [M] Saving shows the overlay too.**
Complete a scan, fill in the account, and press **Save Transaction**.
*Expected:* the overlay appears with `Saving the transaction...` — not a blank scrim with no
sentence under it.

**11.8 [M] Text, not colour alone.**
Screenshot the overlay and desaturate it.
*Expected:* the stage is still readable from the **words** (`40-Accessibility`).

### Known gaps — confirm, do not report

**11.9 [G] A scan cannot be cancelled.**
There is no Cancel button on the overlay. Once a scan starts it runs to completion. This was
scoped out deliberately: cancelling means threading a `CancellationToken` through ONNX and
PaddleOCR and proving both actually honour it between stages, which is a separate piece of work.
Do not file it.

**11.10 [G] The live camera scanner is a different code path.**
`LiveScannerViewModel` already ran its loop off the UI thread and was not changed. If the **camera**
view stutters, that is a separate defect from this one — record it as such rather than as a
regression of §11.

**11.11 [G] One status string is still in Spanish.**
`ReceiptDetector` returns `"¡Captura perfecta!"` while every other string on these screens is
English. Pre-existing, and it belongs with localisation (**TD-45**), which does not exist yet. Do
not file it.

**11.12 [G] The stage list is coarse.**
There are two reported pipeline stages, not five. The pipeline genuinely has two steps; inventing
finer granularity would be showing the user precision that does not exist. Do not file it.

---

## 12. The transaction list opens at all — Phase 1 item 5a, TD-94

**Read this before running it.** Until iteration 15, `GetPagedAsync` ordered by a `DateTimeOffset`,
which the EF SQLite provider refuses to translate: it throws rather than sorting. The transaction
list screen and the dashboard's recent-transactions widget are its only two callers, so **both threw
every time they loaded** — through an unknown number of releases, under a fully green test suite. An
automated test now executes that query against a real encrypted vault, which did not exist before,
but no automated test opens a window. These rows are the only thing that confirms the screen works.

### 12.1 The list loads

1. Open a vault with at least three transactions.
2. Navigate to **Transactions**.
3. **Expected:** the list renders. No error dialog, no blank pane, no "not responding".
4. **If it throws here, stop.** The fix did not work and nothing below is meaningful.

### 12.2 The dashboard widget loads

1. Navigate to the dashboard.
2. **Expected:** the recent-transactions widget shows rows rather than an empty or errored state.

### 12.3 Newest first, and correct across a time-zone change

This is the half an index alone would not have fixed.

1. Add a transaction and let the date default to now. Note the time shown.
2. Change the machine's time zone by several hours in a direction that moves the wall clock
   *backwards* (for example Madrid to a US zone).
3. Add a second transaction, again letting the date default to now. It is genuinely later in real
   time, but its wall clock reads earlier.
4. Open **Transactions**.
5. **Expected:** the second transaction appears **above** the first. Ordering is by instant.
6. **If the older one is on top**, ordering has fallen back to wall-clock text — the defect
   `Repositories/TransactionOrderingTests` pins.
7. Restore the machine's time zone.

### 12.4 Amounts still read correctly after the storage change

Money is now stored as an integer count of ten-thousandths rather than as decimal text, and
`Money.ToString()` was changed so that this cannot show through.

1. Look at any transaction list, the balance widget and an account's balance.
2. **Expected:** amounts show their currency's normal number of decimals — `12.00 USD`, not
   `12 USD` and not `12.3400 USD`. A JPY account shows no decimals at all.
3. **Expected:** no amount has changed value. Compare against what the same vault showed before
   upgrading, if you have it.

### 12.5 An existing vault upgrades

1. Take a copy of a vault created before this release. **Keep the copy** — the migrator makes its
   own backup, but a second one costs nothing.
2. Open it.
3. **Expected:** it opens, every balance is unchanged to the cent, and the transaction list renders.
4. **If it refuses to open with a message naming `pine_migration_refuses_...`**, that is the
   migration declining to damage data — an amount beyond four decimals, or a transaction whose
   account is missing. Report the message; do not work around it.

### Known gaps — confirm, do not report

- **Deleting an account with transactions now fails** rather than orphaning them. There is no delete
  button in the UI, so this is unreachable by hand today.
- **Sub-millisecond ordering for rows migrated from an older vault.** The backfill resolves to the
  millisecond. Two transactions in the same millisecond may tie; new rows carry full precision.

## 13. Receipt reconciliation, and money at its own scale — Phase 1 item 6, TD-19 / TD-75

**Read this first.** Iteration 9 built the reconciliation panel and wrote, in audit §12.20, that it
had *never been rendered* and needed a manual section of its own. Seven iterations later that section
did not exist. This is it. Everything below is covered by view-model tests and by nothing that draws
a pixel — and TD-94, a primary screen that threw on every load under a fully green suite, is what
that gap looks like when it goes unwatched.

### 13.1 The panel appears, and the difference is live

1. Open **Scan Receipt** and pick a photo, or add line items by hand with **+ Add Item**.
2. Select an account.
3. Enter a transaction total of `100.00` and one line item priced `60.00`.
4. **Expected:** a reconciliation panel appears showing "Items total" `60.00` and an unexplained
   difference of `40.00`.
5. Edit the line's price to `100.00`.
6. **Expected:** the difference falls to `0.00` **as you type**, and the error sub-panel disappears.

### 13.2 Saving is refused while a difference is outstanding

1. Leave a difference of `40.00` on screen.
2. Press **Save Transaction**.
3. **Expected:** it refuses, with a message naming both figures — the items total and the shortfall.
4. **If it saves**, the invariant is not being enforced at the point of intent and this is a defect.

### 13.3 The one-click adjustment closes it exactly

1. With `40.00` outstanding, choose a kind from the picker — **Tax**, **Tip**, **Discount**,
   **Deposit**, **Rounding** or **Unattributed**.
2. Press **Record difference**.
3. **Expected:** one new line appears for exactly `40.00`, described by the kind you chose, and the
   difference becomes `0.00`.
4. **Expected:** editing that line's quantity does **not** recompute its total — an adjustment is not
   a quantity times a price.
5. Save. **Expected:** it succeeds.

### 13.4 Money is written at its currency's scale

This is TD-75. Use three accounts if you can: one **USD**, one **JPY**, one **KWD**.

1. Look at the balance widget, the recent-transactions widget, the budgets screen and the dashboard
   total.
2. **Expected — USD:** two decimals, e.g. `1,234.56`.
3. **Expected — JPY:** **no** decimals at all, e.g. `1,500`. A yen has no minor unit; `1,500.00` is
   the defect.
4. **Expected — KWD:** **three** decimals, e.g. `12.345`. A trailing digit dropped is the defect.
5. **Expected everywhere:** thousands are grouped, using your machine's separators.

### 13.5 The dashboard does not invent a total

The defect this replaced showed a number that was not a quantity of anything.

1. With a USD account holding `1000` and a EUR account holding `900`, look at the dashboard's
   headline balance.
2. **Expected:** two figures, one per currency — `900.00 EUR · 1,000.00 USD`.
3. **If you see a single figure near `1900`**, currencies are being added together again.
4. **Expected:** no currency symbol that belongs to your machine's locale rather than to the money.
   On a Spanish-locale machine a dollar balance must **not** read `1.234,56 € USD`.

### Known gaps — confirm, do not report

- ~~**The budgets progress bar never moves and never flags an exceeded budget** (TD-99). Spend is
  hardcoded to zero.~~ Spend is measured from the ledger now. ~~**What remains:** no budget can be
  created from the screen (TD-99), so on a fresh vault the list is empty.~~ A budget can be created since
  plan item 3.0 (TD-152) — §13F. It still cannot be edited or deleted.
- ~~**The budgets screen shows category names like `Category 3f2a`** (TD-99) — a fragment of an
  identifier, not a name.~~ Fixed twice over: first by showing the category's name, then — since
  plan item 2.3 (TD-132) — by making the category the budget's own label. There is no colour dot
  beside it any more, deliberately.
- **The difference shown before an account is selected** may differ slightly from the one shown after
  (TD-100). Only the second decides whether a save is allowed.

### 13.6 An ambiguous date is asked about, never guessed — TD-21

Scan a receipt whose date is printed as an ambiguous `dd/MM` vs `MM/dd` string, for example
`05/03/2026`. Any value where both components are 12 or below will do.

**Expect:** the date field is filled with the reading your machine's locale implies — day-first on
`es-EC` or `en-GB`, month-first on `en-US` — and a warning row appears beneath it reading *"This could
also be Sunday, May 3, 2026. Check the receipt."* (English, US conventions) or *"También podría ser el
domingo, 3 de mayo de 2026. Revisa el recibo."* (Spanish) with a **Use this date instead** button. The
weekday is there on purpose: this is the one sentence whose job is to make you compare two dates.

**Switch the display language in Settings and come back** while the warning is showing, or before
scanning. **Expect:** the sentence, including the month name, is in the language you chose — not the
one your operating system uses. Until plan item 2.5 an English screen on a Spanish machine read *"This
could also be 3 mayo 2026"* (TD-21).

**Then press the button.** The two readings swap. The warning stays on screen, and pressing the button
again swaps them back.

**This is the point of the test:** the warning does not disappear once you have answered it. The
question stays open and the swap stays reversible, because nothing in this pipeline decides a date on
the user's behalf. If the warning vanishes after one press, that is a regression.

Now scan a receipt dated `23/03/2026` — a day above 12 has only one reading. **Expect no warning at
all.** A prompt on an unambiguous date is noise, and noise trains users to dismiss the prompt that
matters.

### 13.7 Weak OCR fields are flagged, and "not found" reads differently from "misread" — TD-22

Scan a receipt where at least one field reads poorly — a creased, faded or partly shadowed total works;
so does photographing at a steep angle.

**Expect:** a hint in the warning colour beneath the affected field. There is **no percentage anywhere
on the screen** — if you see one, the old fabricated score has come back.

Two wordings exist and they are not interchangeable:

- *"Low confidence — please check this."* — the OCR read something, badly. You are being asked to
  **verify** a value that is already in the box.
- *"Not found — please fill this in."* — no text produced this field at all. You are being asked to
  **supply** a value.

**Check both appear in the right circumstances.** Scan a receipt with no recognisable merchant line and
confirm the merchant field says *not found*, not *low confidence*. Collapsing those two into one
message is the original defect this row closed, so seeing the same sentence in both cases is a failure,
not a cosmetic difference.

A well-lit, flat receipt should show **no hints at all**.

### 13.8 A duplicate receipt is warned about, and can still be saved — TD-20

Scan a receipt and save it. Then scan the same receipt again, against the same account.

**Expect:** on pressing Save, the transaction is **not** written. A warning appears reading *"This looks
like a duplicate of 24.50 USD to ACME on Thursday, March 5, 2026. Save again to add it anyway."* in
English, or *"Parece un duplicado de 24,50 USD a ACME del jueves, 5 de marzo de 2026. Guarda de nuevo
para agregarlo de todos modos."* in Spanish.

**Press Save a second time without changing anything.** The transaction **is** written and you are
taken to the transaction list.

That second press is the requirement, not a convenience: two identical coffees on the same day are two
real purchases, and this feature is a warning, never a silent drop. If the second press is refused, the
feature is destroying data.

**Then test that an edit re-arms the check.** Scan the duplicate again, get the warning, and this time
change the date by one day before pressing Save. Expect the transaction to be written with no second
warning — it is genuinely a different transaction now.

**And the reverse.** Get the warning, change the date, change it *back*, then press Save. Expect the
warning again, because you are once more about to write the duplicate.

Finally, **switch the display language while the warning is showing.** Expect the whole sentence to
change, the figure included: `24.50` in English, `24,50` in Spanish. **This reverses what this step used
to say.** The figure was deliberately left in invariant notation while nothing else on the screen was
localised; once the total above it followed the language (plan item 2.5, TD-21), an invariant `24.50`
beside a `24,50` total would have been the one figure on the screen in a different notation.

### Known gaps for §13.6–13.8 — confirm, do not report

- **No per-line-item confidence is shown** (TD-101), only merchant, total and date. The line items
  table carries no bands at all; that is deliberate for now.
- **A correctly-read total sitting on the same printed line as a badly-read neighbour will show a low
  band** (audit §12.38). Confidence is attributed at the line level, not the word level.
- **The duplicate check compares merchant, calendar date, signed amount, currency and account.** Two
  genuinely different purchases identical in all five will warn. That is the intended trade: a false
  warning is visible and dismissible, a false "not a duplicate" double-counts money.

## 13A. The dashboard shows only what it can measure — post-audit plan item 1.3

**Read this before running it.** The tiles are rendered by an automated headless test, at their real
size, and it reads back what is visible — but a headless window is not a screen, has no real fonts,
and no user's time zone. These rows are the part only a person can do. (Lettered rather than numbered
so the sections after it keep the numbers other documents cite.)

### 13A.1 Five tiles, none of them empty furniture

1. Open a vault with at least two accounts, one budget, and a transaction this month.
2. **Expected:** exactly five tiles — Total balance, This month, Accounts, Budgets, Recent transactions.
   **No tile reads "No data yet".** There is no Calendar, Goals, Investments, Charts or AI Suggestions.
3. **Expected:** every tile heading is in the language chosen in Settings. Switch the language there
   and come back: the headings, the greeting, and the budget rows' "Not known" all follow.

### 13A.2 Two currencies stay two figures

1. Give one account a USD balance and another a EUR balance.
2. **Expected:** Total balance shows two figures, one per currency, and never their sum. The sentence
   under them says it is a sum of balances, and that a debt lowers it only if entered as negative.

### 13A.3 "This month" is your month — the case the automated suite found late

1. Set the machine's time zone to one west of UTC (for example Ecuador, UTC−5).
2. Near the end of a month, after 19:00 local time, add an expense dated today.
3. **Expected:** it counts in *this* month's "Out". Before the fix, the tile had already moved on to
   UTC's next month and showed an almost empty one.
4. ~~**Known and registered, not a failure of this row:** the *budgets* screen still measures the UTC month
   (TD-149), so for a few hours at a month boundary the two can disagree.~~ Since plan item 3.0 the budgets
   screen measures the same local month; after 19:00 on the last day of a month the two must agree (§13F.3).

### 13A.4 An import updates the tiles

1. With the dashboard open, import a statement containing this month's transactions.
2. **Expected:** when the wizard closes, "This month", "Recent transactions", "Accounts" and "Total
   balance" already show the imported rows, without leaving the dashboard. Before the fix nothing
   changed until you navigated away and back.

### 13A.5 The Scan Receipt button — post-audit plan item 1.4

1. On the Windows head, press **Scan Receipt** in the dashboard's header.
2. **Expected:** the receipt page opens *beside the sidebar*, and the sidebar's "Scan Receipt" entry
   lights. The shell does not disappear. Until this item the button did nothing at all.
3. On a macOS, Android or iOS build: **expected:** there is no Scan Receipt button in the header, exactly
   as there is none in the sidebar.

## 13B. The live scanner uses your default camera — post-audit plan item 1.5

**Read this before running it.** No automated test opens a camera; everything below is the only check
there is. The picker that used to sit here offered "Camera 0" to "Camera 3" whatever was attached.

1. Open the live scanner on the Windows head.
2. **Expected:** there is no camera picker anywhere on the screen.
3. Press **Start Camera**. **Expected:** your default camera opens and the feed appears.
4. With the camera already in use by another application, press Start again. **Expected:** the sentence
   names your *default camera* and says to check that nothing else is using it — not the old bare
   "Failed to open camera."
5. **Known limitation, deliberate:** with two cameras attached you cannot choose the second one any
   more. The camera seam now exists (plan item 2.5, TD-120), but its only implementation is OpenCV's,
   which cannot enumerate devices, so choosing a camera is still TD-153.
6. **Since plan item 2.5 the camera is opened behind `ICameraSource`.** Repeat steps 3 and 4, then press
   **Stop**, and confirm the camera's indicator light goes off; start again and confirm it comes back.
   The fake-camera tests prove the scanner releases what it opens; only this proves the real device is
   released by the real implementation.

## 13C. Pine lets go of a backup after you restore from it — post-audit plan item 2.1

**Read this before running it.** This one is automated (`ARestoreLeavesTheBackupFileDeletable`),
and it is here because the automated version proves the file handle is released, not that the
*application* behaves well when it is. Restoring opens the backup to verify it first, and Pine used
to keep that file open for the rest of the session — so it could not delete it when tidying up old
backups. A restore Pine REFUSED was enough to cause it, which is the case to run.

1. Create at least two backups from **Settings**, so there is something to tidy up.
2. Attempt a restore from a file Pine will refuse — the quickest is a backup from a *different*
   vault, or any file renamed to `.db`. **Expected:** a sentence saying nothing was restored and
   your vault is unchanged.
3. Now restore from a real backup of this vault. **Expected:** it succeeds and the vault locks.
4. Unlock, and delete a backup from the backups folder in Explorer while Pine is still running.
   **Expected:** the file deletes. Before this fix Windows refused, saying the file was in use by
   another process, and that process was Pine.
5. **What a failure here means:** the connection pool is holding a file the application has
   finished with. Report it against TD-140, which is the row that class of defect belongs to.

## 13D. The release artefact says whether it is signed — post-audit plan item 2.2

**Read this before running it.** Signing is automated end to end
(`packaging/selftest-signing.sh`, thirteen cases, run by CI in the `test` job on the windows leg,
on every pull request) and this is here for the part no script can check: whether a person looking
at what the pipeline produced can tell signed from unsigned without reading any of it. It needs a
Windows machine with a Windows SDK.

1. Run `bash packaging/selftest-signing.sh`. **Expected:** thirteen passes, nothing failed, in
   under a minute. It creates its own throwaway certificate and deletes it afterwards; it needs no
   network.
2. Run `bash packaging/package.sh --out artifacts` with no signing variables set. **Expected:** a
   block of `!! UNSIGNED BUILD` warnings naming the consequence, and a successful build.
3. Open `artifacts/*.release.json`. **Expected:** `"signed": false`, `"signature": null`, and a
   `signing_note` that says no certificate was configured.
4. Run it again with `--require-signature`. **Expected:** it refuses and exits non-zero
   IMMEDIATELY — within a second, before anything is built. If it publishes first and refuses ten
   minutes later, the fail-fast check at the top of `packaging/package.sh` has been removed.
5. **On CI, after a push to `main`:** open the Actions run and look at the `package` job's uploaded
   artefact. **Expected:** it is named `pine-win-x64-UNSIGNED`, and the job log carries a warning
   naming TD-108. If it is ever named `pine-win-x64` while the manifest says `"signed": false`, the
   two statements that are supposed to agree have stopped agreeing — report it against TD-108.
6. **What a failure here means:** an artefact that does not say what it is. That is the whole of
   TD-108's impact — a user cannot tell this build from one a third party modified — and a
   mislabelled one is worse than an honestly unsigned one.

---

## 13E. Configuration, and figures in the language you chose — post-audit plan item 2.5

**Read this before running it.** The behaviour is unit-tested against in-memory configuration and
explicit cultures. What no test does is run the real heads against a real file and a real operating
system locale.

### 13E.1 A misspelled configuration key stops Pine and says which one — TD-117

1. Copy `packaging/appsettings.example.json` to Pine's data directory as `appsettings.json`
   (`%LOCALAPPDATA%\Pine\appsettings.json` on Windows). **Expected:** Pine starts normally — the example
   is every setting at its default.
2. Change `"CategorisationConfidenceThreshold"` to `"ConfidenceThreshold"` and start Pine. **Expected:**
   Pine does not start; the crash log names `Pine:Ai:ConfidenceThreshold` and asks *"Did you mean
   Pine:Ai:CategorisationConfidenceThreshold?"* Before this item Pine started and ignored the key.
3. Put the key back and set the environment variable `PINE_Ui__TransactionsPageSize=10` (the `Pine__`
   is missing). **Expected:** refused, and the message spells out `PINE_Pine__Ui__<setting>`.
4. Remove that variable and set `PINE_SIGNTOOL=C:\anything.exe`. **Expected:** Pine starts. The packaging
   scripts share the prefix, and a machine that has built a release must still run Pine.
5. Delete `appsettings.json` afterwards.

### 13E.2 Figures and dates follow the display language — TD-21

On a machine whose regional format is **not** Spanish (English – United States is the clearest):

1. Open a vault with a transaction of more than a thousand, and choose **Español** in Settings.
2. **Expected, on the transactions list:** amounts read `1.500,00 USD` and dates `3 de mayo de 2026`.
   Before this item they read `1,500.00 USD` and `May 03, 2026` under Spanish headings.
3. **Expected, on the same Settings page, without leaving it:** the backup list's dates and sizes change
   notation the moment the language changes.
4. Open **Add transaction**. **Expected:** the amount box takes and shows a decimal comma.
5. Switch back to English. **Expected:** `1,500.00 USD` and `May 3, 2026`.

On a machine set to **Spanish (Ecuador)** with the display language **Español**: dates and figures follow
Ecuador's own conventions. With the display language **English**, they follow the United States', not
Ecuador's — a sentence in English does not carry Spanish month names.

### Known gaps for §13E — confirm, do not report

- **A page other than Settings keeps the notation it was drawn in** until you navigate to it again. Only
  the Settings page is visible when the language changes, which is why only it re-renders.
- **The import wizard's own mapping text, the confidence hints and a few labels are still English**
  (TD-150).

## 13F. Creating a budget — post-audit plan item 3.0

**Read this before running it.** The form, the validator and the window are tested, including a rendered
view in a headless window. What no test does is type into the real controls: the date pickers, the limit
box being cleared, and a screen reader reading the fields.

### 13F.1 The first budget

1. On a vault with no budgets, open **Budgets**. **Expected:** a sentence saying you have no budgets yet,
   and a **New budget** button. Before this item: an empty page and no way to add one.
2. On a vault with **no accounts**, press **New budget**. **Expected:** the form does not open; a sentence
   says to create an account first.
3. With a USD account, press **New budget**. **Expected:** Category, Limit, Currency (only your accounts'
   currencies, each once), Period (Monthly, Weekly, Yearly, Custom dates — **no "every two weeks"**). No
   date pickers until you choose Custom dates.
4. Enter *Groceries*, 200, Monthly, **Create budget**. **Expected:** the form closes and a *Groceries —
   Monthly* row appears, measuring this month's Groceries spending.
5. Try the same again. **Expected:** refused — there is already a budget for 'Groceries' in USD over that
   period — and the form stays open with what you typed.

### 13F.2 Refusals, and the limit box

1. Leave Category empty and press Create. **Expected:** a sentence asking you to name the category.
2. Type a limit, then **clear the box** and press Create. **Expected:** refused as not greater than zero —
   not saved with the number you deleted.
3. Choose Custom dates with only a start date. **Expected:** a sentence asking for both dates.
4. Switch the language to Español with a refusal showing. **Expected:** the sentence, the row's period and
   the period list all change language, and your chosen period stays chosen.

### 13F.3 The month is yours — TD-149

1. On a machine in a time zone west of Greenwich (Ecuador, UTC−5), on the **last day of a month after
   19:00**, add an expense in a category with a monthly budget.
2. **Expected:** it counts in **this** month's budget, and the dashboard's "This month" agrees. Before this
   item the budget had already moved to UTC's next month.
3. Create a **Custom dates** budget for this month, first to last day. **Expected:** it shows the same spend
   as the monthly budget for the same category.

### 13F.4 Accessibility

With a screen reader, tab through the form. **Expected:** each field announces its label, and a refusal is
announced when it appears.

### Known gaps for §13F — confirm, do not report

- **A budget cannot be edited or deleted** from the screen.
- **"Every two weeks" budgets created elsewhere still show "Not known"** without explicit dates.

## 13G. Every write is stamped for a future merge — post-audit plan item 3.1a

**Read this before running it.** Nothing here is visible in the application; it is checked with a file
browser and by opening an existing vault. The stamping itself is covered by integration tests against real
encrypted vaults.

1. On a machine that has run an older build, open an **existing vault**. **Expected:** it opens and every
   screen shows the same data. The upgrade adds columns and two tables and changes no value.
2. **Expected:** a file `device-id` exists in Pine's data directory (`%LOCALAPPDATA%\Pine` on Windows) holding
   one GUID, and it is the same after restarting Pine.
3. Replace its contents with `nonsense` and start Pine, then open a vault. **Expected:** the vault does not
   open and the log names the file; Pine does **not** write a new id over it. **What the screen says in this
   case has not been observed** — the refusal is tested below the UI only; record what appears. Delete the file: **expected** the
   vault opens without restarting Pine, and a new id is written.
4. Delete a transaction. **Expected:** it is gone from every screen, exactly as before this item.

### Known gaps for §13G — confirm, do not report

- **Restoring an older backup rewinds the vault's logical clock** (TD-144). Harmless until sync exists.
- **Tombstones are never pruned** (TD-145).

## 14. What a failure means

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

## 15. Not covered here

- **Concurrency conflicts (TD-17).** Every aggregate now carries a version, and a write against a row
  that changed after it was read is refused rather than silently overwriting. There is deliberately
  **no manual row for it**: producing a conflict needs two overlapping writes to the same row, and
  the UI offers no way to arrange that by hand. It is covered by `Data/ConcurrencyTokenTests`, which
  drives two contexts over one vault file. What a user would see if one ever happened is **not**
  covered anywhere — nothing catches the exception yet (TD-98).


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
