# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

| | |
|---|---|
| **Status** | Non-normative — release record |
| **Reconciled** | 2026-09-08 · Extended to cover iteration 30's coverage gate. Previously extended to cover iteration 29's categoriser fix. Previously extended to cover iteration 28's build and artefact changes. Previously extended to cover remediation iterations 14–21, which had shipped user-visible changes with no entry here (TD-39) |

## [Unreleased]

Nothing has been released. The build is stamped `0.1.0-alpha.1`; there is no installer, no
signed artefact and no update mechanism, so "unreleased" is literal.

### Added
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
- **Automatic transaction categorisation now works at all.** The feature shipped unable to run:
  the component that learns from your existing categories discarded every example before training,
  so no model was ever built; and any suggestion it did produce was thrown away before it reached
  the screen. Both ends were reading a category as an identifier when it is a name you typed.
  Nothing about your data changes — the categories on your transactions were always stored
  correctly.

### Changed
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
- The encrypted vault export has **no UI entry point and no restore path** (TD-56).
- **CI runs only on manual dispatch**, so no commit is verified automatically and the Android and
  iOS heads are compiled by nobody (TD-07).
- Changing your password rotates the key that protects your data key, **not** the data key itself.
  Someone who had already extracted the data key would still hold a working key. Rotating it
  requires re-encrypting the whole vault and is not yet implemented (TD-86).
- Biometric and platform-keychain unlock remain unavailable. The new envelope makes them possible;
  nothing implements them yet (TD-87).
- The experimental browser build cannot open vaults at all: its runtime does not provide AES-GCM
  (TD-88).
