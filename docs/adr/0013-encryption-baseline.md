# 0013 — Encryption baseline: SQLCipher at rest, Argon2id auth key, AES-256-GCM key envelope

| Field          | Value                                                       |
|----------------|-------------------------------------------------------------|
| ADR            | `0013`                                                      |
| Title          | Encryption baseline                                          |
| Status         | Accepted                                                     |
| Date           | 2026-08-31                                                   |
| Authors        | Written retrospectively from the implementation (TD-40)      |
| Reviewers      | n/a — this repository has one contributor                    |
| Supersedes     | —                                                            |
| Superseded by  | —                                                            |
| Closes OD      | `OD-03` (encryption at rest standard)                        |
| Deciders       | The implementation. See the note below.                      |

> **This ADR was written after the fact, and that is the finding it records.** For eight months
> `ADR-0013` existed only as an eight-word row in `25-ADR.md` §11 marked *Accepted*, while
> `Argon2idKeyDerivationService.cs:8` cited it as the authority for the key derivation and four other
> documents cited it as the authority for encryption at rest. The decision was real and was
> implemented; the record of it was a dangling reference (audit TD-40). This file states what was
> actually decided and built, verified against the code rather than against the register — including
> the places where the built thing differs from what the register implied.

## Context

Pine holds a household's complete financial history in a local file. That file is the whole asset: a
copy of it taken from a backup drive, a synced folder or a stolen laptop must be worthless without
the user's passphrase. There is no server to authenticate against and no second factor to fall back
on, so every property has to come from the file format itself.

`15-Security` §4 fixed the requirement — encryption at rest, a memory-hard KDF, and a key that can be
changed without re-encrypting the data. `56-IMPLEMENTATION-PLAN.md:338-341` explicitly left the
mechanism open: *"Decision between SQLCipher and an application-level envelope is owned by
`29-Deployment`; Phase 0 picks the default."* That decision was never recorded anywhere. It was,
however, taken — in code.

## Decision

**Both mechanisms, at two different layers, for two different reasons.**

1. **The database is encrypted by SQLCipher.** `PineDbContext` opens a keyed connection; the page
   cipher is SQLCipher's. Nothing in the application encrypts rows or columns itself, so there is no
   second, hand-rolled cryptosystem in the write path to get wrong.

2. **The key that opens it is a random data key, wrapped by a passphrase-derived key.** The
   passphrase runs through **Argon2id** (`Argon2idKeyDerivationService`) with the parameters stored in
   the vault header — not with constants in the code, so a vault written under one cost can still be
   opened after the cost is raised. The output is the **auth key**. The auth key wraps a randomly
   generated **data key** with **AES-256-GCM**, and it is the data key that opens SQLCipher.

3. **The wrap's associated data binds every other header field**, so a `keyWrap` spliced from another
   vault, or a vault id edited in place, fails the authentication tag rather than producing a wrong
   key and a corrupt-looking database.

4. **Header version 2 is the only format this build opens.** Pre-envelope vaults (v1, `pine.salt`)
   are refused with a distinct exception, not migrated.

## Consequences

**Positive.**
- Changing the master password re-derives the auth key under a fresh salt and re-wraps the same data
  key. No data is re-encrypted, the database is not touched and the session is not closed
  (`ChangeVaultPasswordCommand`). Before the envelope this feature was impossible without rewriting
  every page.
- A wrong passphrase is detected by an authentication tag rather than inferred from "SQLCipher could
  not read a page", which is what let `UnlockViewModel` stop reporting failed migrations as wrong
  passwords (ADR-adjacent; audit TD-23).
- Biometric unlock becomes an ordinary feature — a second wrap of the same data key — rather than a
  redesign.

**Negative, and load-bearing.**
- **The data key is re-wrapped, never rotated.** Changing the password does not change the key that
  protects the data, so anyone who already knew the data key still knows it afterwards. Real rotation
  requires re-encrypting the database. Registered as **TD-86**, and `15-Security` §4 is annotated in
  place rather than left to read as satisfied.
- **There is no platform secure storage.** `15-Security` §4 offers "keychain OR passphrase-derived";
  only the second half exists, so there is no biometric unlock (**TD-87**).
- **The browser head cannot open any vault at all**, because the WASM runtime provides no AES-GCM and
  therefore cannot unwrap the key (**TD-88**). It fails with a distinct exception that says so rather
  than reporting a wrong password.
- Refusing v1 vaults is a breaking change. It was taken deliberately, while no vault existed outside
  a developer machine, because carrying a second key path forever is a larger cost than deleting one
  now.

**Neutral.**
- The Argon2id derivation is byte-for-byte unchanged from the pre-envelope build; its output simply
  changed role from *database key* to *auth key*. The pre-envelope known-answer vector still passes,
  which is how that was verified rather than asserted.

## Alternatives Considered

- **Passphrase-derived key straight into SQLCipher** (what the code did before). Rejected: it makes
  password change equivalent to re-encrypting the entire database, and it conflates authentication
  with decryption, so the only signal of a wrong password is a read failure — indistinguishable from
  a corrupt file or a failed migration.
- **Application-level envelope only, with an unencrypted SQLite file.** Rejected: it would require
  every repository to encrypt and decrypt individual values, which puts a hand-rolled cryptosystem in
  the money write path and makes indexing and ordering impossible.
- **PBKDF2 or scrypt instead of Argon2id.** Rejected: Argon2id is the current recommendation for
  passphrase hashing and is memory-hard against the GPU attack this file format invites. The header
  records the KDF id, so a future ADR can supersede this one without breaking existing vaults.
- **Storing the data key in the OS keychain.** Not rejected — not built. It is TD-87, and this ADR
  deliberately does not claim it.

## Risks

| Risk | Mitigation |
|---|---|
| A raised Argon2id cost locks users out of existing vaults | Parameters are read from each vault's own header, never from constants in the code |
| A wrapped key is spliced between vaults | The GCM associated data binds every other header field, so the tag check fails |
| The unlock screen becomes a password oracle | Every distinguishable vault failure that could confirm a candidate passphrase is collapsed to one message (audit §12.40) |
| The data key is never rotated | **Unmitigated.** TD-86 is open and `15-Security` §4 says so in place |
| A managed byte array holding the key is copied by a compacting GC | Narrowed, not closed: the key is cleared the moment it is handed to the session. Stated in `UnlockViewModel` rather than claimed as solved |

## Compliance

- `15-Security` §4 — implemented in part; the platform-secure-storage and rotation clauses are not.
  That document carries the clause-by-clause table.
- `38-Threat-Model` TM-10, TM-40 — both name ADR-0013 as their mitigation. TM-40 (encryption at rest)
  holds. TM-10 (a sync provider exfiltrating plaintext) is vacuous today: there is no sync.
- `08-NonFunctional-Requirements` — the security class.

## Traceability

- Closes `OD-03` (encryption at rest standard).
- Cited by `src/Pine.Infrastructure/Security/Argon2idKeyDerivationService.cs:8` — the one ADR citation
  in the entire source tree, and the reason this file had to be written rather than the citation
  dropped.
- Affects `15-Security`, `38-Threat-Model`, `27-Development-Guidelines`, `56-IMPLEMENTATION-PLAN`.
- Implemented by audit §12.27 (TD-06). Open consequences: **TD-86**, **TD-87**, **TD-88**.
