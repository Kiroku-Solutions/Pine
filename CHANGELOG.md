# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

| | |
|---|---|
| **Status** | Non-normative — release record |
| **Reconciled** | 2026-09-11 · Extended to cover the dashboard (plan item 1.3) and the navigation fix (TD-121). Previously extended to cover iteration 37's import, budget and backup fixes. Previously extended to cover iteration 36's withdrawal of the plugin host (ADR-0021). Previously extended to cover iteration 35's row audit columns and stable child identity. Previously extended to cover iteration 34's rule engine. Previously extended to cover iteration 33's receipt attachments. Previously extended to cover iteration 32's localisation. Previously extended to cover iteration 30's coverage gate. Previously extended to cover iteration 29's categoriser fix. Previously extended to cover iteration 28's build and artefact changes. Previously extended to cover remediation iterations 14–21, which had shipped user-visible changes with no entry here (TD-39) |

## [Unreleased]

Nothing has been released. The build is stamped `0.1.0-alpha.1`; there is no installer, no
signed artefact and no update mechanism, so "unreleased" is literal.

### Added
- **Every record now carries when it was written and when it was last changed.** Two columns on
  every table, filled in by the database layer rather than by each feature, so nothing can forget
  them. They are not shown anywhere yet: this is the groundwork multi-device sync is gated behind,
  and rows written before this release read as "never stamped" rather than pretending to a date
  nobody chose.
- **A rule engine, and a screen to write rules in.** A rule is a title, a priority, a category and
  up to eight conditions over a transaction's payee, notes or amount — all of which must hold. Rules
  are evaluated on every import and on manual entry, and a matching rule **beats the on-device
  classifier outright**: the model is not consulted at all, because an instruction the user wrote is
  not a guess to be weighed against a confidence score. Two guardrails are deliberate: a rule never
  overwrites a category that came from the user or from the imported file, and a rule never touches
  transactions already in the ledger. Ties between equal priorities resolve by creation time and
  then by id, so the same vault answers the same way on every machine. The sidebar's "Rules" entry,
  which had been disabled with a "Coming soon" tooltip, is now a working page.
- **The receipt is kept with the transaction.** Scanning a receipt produced a transaction and
  discarded the image; the picture is now stored inside the encrypted vault database, linked to its
  transaction by a real foreign key, and shown from the transaction list. A SHA-256 is recorded when
  it is attached and verified whenever it is opened, so an image that no longer matches its
  fingerprint is refused rather than presented as evidence. The bytes live in `pine.db` rather than
  in a file beside it for three reasons: SQLCipher already encrypts them, they are written in the
  same database transaction as the ledger row, and the backup copies the database — a file beside it
  would have been missing from every restore.
- **A language you can choose, and an interface that follows it.** Pine ships English and Spanish.
  The Settings picker now changes the application instead of only remembering a number: every
  string in every view is bound to a shared string table, so switching re-renders the running UI
  without a restart. The preference lives in `%LOCALAPPDATA%/Pine/ui-preferences.json` rather than
  inside the vault, which is what lets the unlock and vault-selection screens be translated at all
  — they are drawn before any vault is open. A pre-existing in-vault choice is migrated once, so
  nobody loses a selection they had already made. Adding a third language is one JSON file (TD-45).
- **A version.** Builds are stamped `0.1.0-alpha.1` plus the commit, and `pine --version` prints it
  from a running process. Every log record and the diagnostics panel previously showed the constant
  `1.0.0`, because nothing set a version and the resolver discarded the only varying part.
- **A packaging script** (`packaging/package.sh`) that produces a runtime-identifier-pinned Windows
  artefact with a checksum for every file, a release manifest, and a signing step that refuses
  rather than pretending. There is still **no installer and no signing certificate**.
- **A duplicate-receipt warning.** A scanned receipt that matches one already in the vault is
  flagged before it is written, on a stable key that reuses the import path's index. The warning is
  non-blocking and is re-checked immediately before the write.
- **Honest OCR confidence.** Per-field confidence bands now come from the OCR engine's own word
  scores, which it had always produced and the extractor had always thrown away. "Not found" is a
  distinct outcome from "low confidence".
- **Optimistic concurrency.** Every aggregate carries a row version, so two windows editing the
  same transaction can no longer silently overwrite one another; the second write is refused with
  "Reload and try again." A change to a split or line item promotes its parent, so the aggregate
  rather than the row is the unit of concurrency.
- **A per-vault, encrypted categorisation model.** The ML model moved out of a shared plaintext file
  into the vault, sealed under a key derived from the vault's own data key, and the categoriser now
  refuses to answer with a different vault's model.
- **Structured logging with redaction** at a single boundary, with rotation and retention, and
  real diagnostics: the panel's log tail, database status and version are measured rather than
  asserted. The OCR content trace was deleted, not redacted.
- **Envelope encryption for the vault key (TD-06).** The vault's database key is now a random
  256-bit data key stored only in encrypted form, wrapped with AES-256-GCM under an auth key that
  Argon2id derives from the master password. The wrap is bound to every other field of
  `pine.vault.json`, so a wrap cannot be moved between vaults and no header field can be edited
  without breaking it.
- **Change your master password**, from Settings → Master Password. The vault is not re-encrypted
  and does not close: only the key that protects the data key is replaced. Held to the same
  password-strength floor as vault creation.
- Initial project scaffold for Phase 0.

### Fixed
- **The dashboard's "Scan Receipt" button works.** It looked like a button and did nothing at all
  when pressed. It now opens the receipt screen, and it is not shown on builds that cannot scan a
  receipt.
- **The dashboard shows your money instead of ten empty boxes.** Ten of its twelve tiles said
  "No data yet" whatever was in your vault. It now has five, and every one reads your ledger: the total
  of your account balances (one figure per currency — Pine never adds dollars to euros), what came in
  and went out this month, your accounts, your budgets with the ones over their limit first, and your
  latest transactions. Calendar, goals, investments, charts and AI suggestions are gone until Pine can
  actually fill them. A tile that could not be loaded now says so, instead of looking empty.
- **Importing a statement now updates the dashboard.** The figures used to stay as they were until you
  left the dashboard and came back.
- **Leaving the receipt screen no longer throws away the sidebar — and locking afterwards clears the
  screen.** Saving or cancelling a scanned receipt replaced the whole app with a bare transactions
  list: no sidebar, no lock button, no way back but to restart. Worse, if the vault then locked —
  after the idle timeout, or by hand — that transactions list stayed on screen. It now opens inside the
  app like every other page, and a lock clears it like every other page.
- **Importing an Excel statement now actually imports it.** Selecting a `.xlsx` or `.xls` file used
  to report success and add nothing — indistinguishable from a statement with no rows in it. Excel
  files are now read with the same column mapping, date format and duplicate detection as CSV, so
  the same statement exported either way imports identically and will not double up.
- **PDF statements are now refused instead of silently importing nothing.** The picker offered "PDF
  Statements" and the reader behind it parsed no rows at all. Rather than leave a promise that
  cannot be kept, PDF has been removed: picking one now says plainly that this screen cannot read
  it. Receipt images were removed from that picker too — they belong to receipt scanning.
- **Budgets show what you have actually spent.** Every budget used to display 0% spent, whatever you
  had spent, and named its category with a fragment of an internal identifier. Spend is now measured
  from your transactions within the budget's period, ignoring voided ones. Where Pine genuinely
  cannot match a budget to your spending it says so rather than showing you a zero.
- **The "Export Vault" button works.** It was connected to nothing at all. It now writes an
  encrypted backup and tells you where it went. The panel beside it also claimed the backup was
  *decrypted*; it never was, and the text has been corrected. "Lock Vault" in Settings was dead in
  the same way and now locks the vault.
- **Editing a transaction no longer silently rebuilds its splits.** Saving a transaction used to
  delete every one of its splits and its receipt line items and write them back as brand-new rows,
  even when nothing about them had changed — so the parts of a transaction had no lasting identity
  of their own. They keep it now. Nothing you can see changes today; what changes is that a split
  can be referred to, which is what an audit trail, and eventually multi-device merging, both need.
- **Automatic transaction categorisation now works at all.** The feature shipped unable to run:
  the component that learns from your existing categories discarded every example before training,
  so no model was ever built; and any suggestion it did produce was thrown away before it reached
  the screen. Both ends were reading a category as an identifier when it is a name you typed.
  Nothing about your data changes — the categories on your transactions were always stored
  correctly.

### Changed
- **Pine will not load third-party code, and that is now a decision on the record rather than an
  absence.** The plugin host, the plugin SDK and the plugin marketplace were specified in detail and
  never built; they are now formally withdrawn (ADR-0021). The reason is not effort: the security
  specification required that a plugin never see your vault key, and no plugin running inside the
  application can be prevented from reaching it. Extensibility continues as features built into
  Pine and shipped with it — every line of code that touches your vault is code this project wrote.
  Cloud sync, which had been specified as something only a plugin could provide, is unaffected and
  keeps its place on the roadmap.
- **Categories can no longer reference a parent that does not exist.** The parent link was an
  unconstrained column; it is now a real foreign key, so the database refuses to strand a
  sub-category. Existing vaults are migrated in place.
- **The download is roughly half the size.** The packaged artefact went from 1.08 GiB to 658 MiB
  unpacked, and from 680 MB to 292 MiB compressed. Nothing a user can do changed: two redundant
  PaddleOCR model packages (472 MB) and three model files (203 MB) were being shipped, and the
  three files had never been read by any code in this project's history. Measured by building the
  artefact, not estimated.
- **Package versions now live in one file.** `Directory.Packages.props` replaces a version literal
  in every project file. This closes a real mismatch — Entity Framework's SQLite and Design
  packages were pinned one patch apart and NuGet had been silently reconciling them — so the
  versions the build actually uses are now the versions the repository says it uses.
- **BREAKING: vaults created by earlier builds can no longer be opened.** The vault header is now
  version 2, and version-1 headers (and the older `pine.salt`-only layout) are refused with a
  message saying so rather than being upgraded in place. This was taken deliberately while no vault
  exists outside a developer machine; it removes the un-enveloped key path entirely instead of
  carrying it alongside the new one. Create a new vault and re-enter your data.
- **BREAKING: money is stored as an integer count of ten-thousandths**, not as text. The migration
  converts existing amounts with integer arithmetic only — no value passes through a floating-point
  type — and **refuses to run** on a vault holding an amount with more than four decimal places
  rather than rounding stored history.
- **Errors on screen no longer quote the exception.** An exception reaches a screen only if its
  author opted in and wrote the sentence a user should read; everything else becomes a fixed message
  and the full detail goes to the log. Eleven screens previously rendered `Error: {ex.Message}`,
  which promised to show whatever any layer happened to throw, including file paths.
- **The dashboard no longer adds different currencies together**, and amounts everywhere are
  formatted at their currency's own scale.
- An incorrect master password is now detected by the key envelope's authentication tag, before any
  database file is opened, instead of surfacing as a SQLCipher error.
- A date on a receipt that is genuinely ambiguous (03/04/2026) is now shown as ambiguous instead of
  being resolved silently in whichever direction the parser tried first.
- **The documentation now says whether it describes software that exists.** All 62 files in `docs/`,
  plus `README.md`, `STATUS.md`, `PRODUCT.md` and this file, carry a status from a closed
  vocabulary and a date; a test fails the build when one goes missing. `STATUS.md`'s "Phase 1 and
  Phase 2: 100% Completed" is corrected, and the README no longer claims six supported platforms.

### Known limitations
- There is no way to **edit** a transaction. Correcting one means voiding it and entering a
  replacement, which leaves two rows with no link between them (TD-68).
- The **budgets screen computes nothing**: its progress bar is hard-coded to zero and a budget can
  be exceeded without the screen saying so (TD-99).
- ~~The encrypted vault export has **no UI entry point and no restore path** (TD-56).~~ **Fixed:**
  the entry point shipped in iteration 37 and the restore path on 2026-09-10. Settings lists this
  vault's backups and restores the one you choose — refusing any file it cannot open with your
  password or that a newer Pine wrote, keeping a copy of the vault as it stood so the restore can be
  undone, and locking the vault afterwards so what you see next is the ledger you restored.
- ~~**CI runs only on manual dispatch**, so no commit is verified automatically and the Android and
  iOS heads are compiled by nobody (TD-07).~~ **Fixed 2026-09-10:** the workflow runs on every pull
  request into `main` and on every push to it, and the Android and iOS heads are among what it
  compiles. Nothing yet runs either head on a device.
- Changing your password rotates the key that protects your data key, **not** the data key itself.
  Someone who had already extracted the data key would still hold a working key. Rotating it
  requires re-encrypting the whole vault and is not yet implemented (TD-86).
- Biometric and platform-keychain unlock remain unavailable. The new envelope makes them possible;
  nothing implements them yet (TD-87).
- The experimental browser build has been removed (Phase 3 platform review). It could not open a
  vault at all — the browser runtime provides no AES-GCM — and OpenCV and PaddleInference publish
  no WebAssembly build, so receipt scanning could never have run there either. Browser-only
  distribution was already an explicit non-goal in `02-Product-Scope`.
