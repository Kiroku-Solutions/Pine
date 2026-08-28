# 61 · Manual Test Plan — Phase 0 items 1–8

| Field | Value |
|---|---|
| Document ID | `PINE-DOC-61-MANUALTEST` |
| Purpose | Verify by hand what the automated suite cannot prove about the remediation in `60-Production-Readiness-Audit.md` §12. |
| Covers | Phase 0 items **1–8** → TD-01, TD-02, TD-03, TD-04, TD-05, TD-10, TD-12, TD-13, TD-23 |
| Automated baseline | 141 unit + 99 integration = **240 passing** |
| Created | 2026-08-27 · updated 2026-08-28 (item 8) |
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

## 9. What a failure means

| Where it fails | Severity |
|---|---|
| §1.2, §7.6 | **Stop everything.** Users cannot open their vault. |
| §2.2 | **Stop everything.** Backups are plaintext; the privacy claim is false. |
| §4.x, §5.1 | Data loss on a write path. Report with exact steps before continuing. |
| §6.1, §7.1, §7.2, §7.4 | The remediation did not hold. Reopen the TD in §8 of the audit. |
| §7.3 | Cosmetic to structural. Describe precisely what looks wrong. |
| §8.1, §8.2, §8.5 | A correction that half-happened. **Stop.** This is the defect the feature exists to prevent. |
| §8.6 | A repair tool that made a balance worse. **Stop everything.** |
| any **[G]** behaving *better* than documented | Good news — verify it, then correct the audit. |

---

## 10. Not covered here

- **Android and iOS.** Not compilable locally; workloads absent. Nothing about those heads has ever
  been compiled, let alone run.
- **macOS, Web.** They build, but no head other than Windows has been exercised by hand.
- **Load and scale.** No test here uses a realistic vault size. `GetPagedAsync` sorts by `Date` and
  `GetByAccountIdAsync` filters on an unindexed `AccountId` (TD-30), and SQLite stores `decimal` as
  TEXT so any comparison reaching SQL is lexicographic (TD-31). Both are open.
- **Editing a transaction.** Still does not exist (TD-68). Voiding does — see §8 — so a bad record
  is no longer permanent, but correcting one means voiding it and entering it again.
- **Undoing a void** (TD-69). Once voided, a transaction stays voided.
- **Accounts created before 2026-08-28.** Their first balance check anchors rather than checks, and
  says so on screen (TD-70). §8.7 covers exactly that.
