# 14 — Synchronization

| Field            | Value                                                                                |
|------------------|--------------------------------------------------------------------------------------|
| Document ID      | `PINE-DOC-14-SYNCHRONIZATION`                                                        |
| Status           | Aspirational — no synchronisation code exists; its persistence preconditions now do  |
| Reconciled       | 2026-09-19 · **Update, post-audit plan item 3.1d: §5's envelope exists, still between two vault files and still with nothing sent anywhere.** `VaultMerger.ExportSealedAsync` hands out only a `pine.sync-envelope.v1` envelope and `ApplySealedAsync` accepts only one; the plaintext change set is never written. Two decisions recorded in §5 below: the sync key is **generated, not derived from a data key** (a departure from `15-Security` §7, which cannot work between two vaults whose data keys are independent), and §5's "signature" is **the AES-GCM tag**, not a per-device signature. Pairing — giving the second vault the key — is not built (TD-154). Earlier: 2026-09-18 · **Update, post-audit plan item 3.1b: a merge exists, between two vault files in one process, and still nothing is sent anywhere.** `VaultMerger` exports a §6 change set (`pine.sync.v1`, ordered by `(clock, device)`, tombstones included, plaintext and in memory only — §5's envelope is not built) and applies one in a single transaction per vault: §4's three layers, with §7's last-writer-wins by `(clock, device)` for account name/currency/opening balance and settings only, **everything else a recorded conflict** (a `SyncConflicts` row, once per change), consent never granted by a peer, totals re-derived from the merged ledger, and the Lamport receive step (TD-144). **Deliberate departure from §7:** splits are not set-unioned — a union of two valid split sets need not sum to the amount — so a split difference on both sides is a conflict. No envelope, provider, transport or conflict-resolution screen. Earlier: 2026-09-14 · **Update, post-audit plan item 3.1a:** the three missing persistence pieces this line lists below now exist in part — every written row carries `(RowClock, RowDeviceId)`, deletes leave tombstones, and `NaturalKeys` records a decision per aggregate (TD-146 closed). TD-144 stays open for the merge-side receive step and for restore rewinding the clock; TD-145 for retention and child tombstones. Still no envelope, provider or merge. Earlier: 2026-09-09 · **Still no sync code, and that is now a decision rather than an absence.** A repo-wide grep for sync services, conflict resolution or a sync envelope still returns nothing, and the `CloudSync` setting this document implied was removed by TD-48. What changed is underneath: `60-Production-Readiness-Audit` §10 Phase 3 gates sync behind "row versions, audit columns and stable natural keys", and all three now exist — row versions since TD-17, audit columns and stable owned-child identity as of §12.72 (TD-60 closed). **Three things this document specifies are still missing and are registered rather than glossed:** §6's Lamport clock and the `device_id` §7 pairs with it (TD-144 — the row version is a random GUID, which detects a change but cannot order two of them), §6's tombstones (TD-145 — every delete in this codebase is physical), and natural keys (TD-146 — identity is stable now, but it is a surrogate; nothing recognises "the same account" across two vaults). **The plugin gate is gone as of ADR-0021** (§12.74): §8's `ISyncProvider` was owned by `33-Plugin-SDK`, which is now Withdrawn, and the contract is this document's own — sync providers are compiled in rather than loaded, so this document no longer waits on a plugin host that will never be built. Any network transport would also trip `NetworkEgressTests`, which per ADR-0012 is deleted deliberately or not at all. |
| Owner            | Engineering Review Board                                                             |
| Created          | 2026-07-08                                                                           |
| Last Updated     | 2026-07-08                                                                           |
| Specification Standard | Internal (sync algorithms)                                                    |
| Glossary         | `PINE-DOC-03-GLOSSARY`                                                               |
| Prerequisites    | `00-Vision`..`13-AI-Architecture`                                                    |
| Decisions closed | OD-02 (sync conflict resolution strategy baseline)                                   |
| Related Documents| `09-System-Architecture`, `11-Data-Model`, `12-Plugin-Architecture`, `15-Security`   |

---

## 1. Purpose

Define the synchronisation architecture of Pine: the data envelope,
the providers (as plugins), the conflict-resolution strategy, the
offline behaviour, the encryption guarantees, the user controls, and
the audit trail.

## 2. Scope

In scope: sync protocol, envelope, conflict resolution, providers,
offline queue, encryption, UX surface, audit, telemetry, plugin
contract.

Out of scope: storage envelope → `15-Security`; data shapes →
`11-Data-Model`; plugin SDK details → `33-Plugin-SDK`.

## 3. Definitions

| Term                  | Working definition                                                                                |
|-----------------------|------------------------------------------------------------------------------------------------------|
| Provider              | A built-in implementation of `ISyncProvider`, shipped with Pine (ADR-0021).                          |
| Envelope              | The encrypted payload exchanged with a provider.                                                     |
| Change set            | The set of changes since the last successful sync.                                                  |
| Cursor                | A provider-specific token identifying the last synchronised point.                                  |
| Conflict              | A pair of changes to the same entity on different devices that cannot be auto-merged.                 |
| Resolution            | The action taken to reconcile a conflict (e.g. keep local, keep remote, merge, prompt user).       |
| Tombstone             | A delete marker.                                                                                    |
| CRDT                  | Conflict-free Replicated Data Type.                                                                  |
| LWW                   | Last-writer-wins; a simple conflict resolution that picks the latest timestamp.                     |

> **Assumption SY-A-1.** Encryption is applied *before* the envelope
> leaves the device. The provider sees only ciphertext and an opaque
> cursor.

## 4. Closing OD-02 — Sync Conflict Resolution Strategy

OD-02 from `00-Vision` §14 is **closed by this document** as follows:

Pine uses a **domain-aware, deterministic, auditable merge** with a
**user prompt for irreconcilable cases**.

The strategy has three layers, applied in order:

1. **Idempotent CRDT-friendly primitives** for fields that are safe
   to merge set-wise (tags, attachments, splits within a transaction).
2. **Domain-aware auto-merge** for fields where the domain has a
   well-defined rule (e.g. adding a contribution to a savings goal is
   commutative; changing an account name is last-writer-wins by
   `(device_id, lamport_clock)`).
3. **User prompt** for any change that is *not* covered by (1) or
   (2), with a side-by-side view and a clear "keep local / keep
   remote / merge manually" choice.

**Rationale.** A pure LWW strategy would silently lose user data on
concurrent edits. A pure CRDT strategy would force the entire
domain into a CRDT shape, which is not always appropriate. A pure
user-prompt strategy would be unusable for common cases. The
three-layer strategy balances safety, predictability and UX.

## 5. Sync Envelope (normative)

The envelope is defined in `11-Data-Model` §6.1. The normative
properties of the envelope are:

- The plaintext is a JSON document conforming to
  `pine.sync.v<MAJOR>`.
- The plaintext is encrypted client-side with the user's
  sync key (see `15-Security`).
- The provider sees the ciphertext, the cursor, the size, and the
  creation timestamp.
- The signature is over the ciphertext.

```mermaid
sequenceDiagram
    participant A as Device A
    participant P as Sync Provider (built in)
    participant B as Device B
    A->>P: PUT envelope (ciphertext + cursor + sig)
    P-->>A: ack
    B->>P: GET since(cursor)
    P-->>B: envelopes
    B->>B: decrypt + merge + resolve conflicts
    B->>P: PUT envelope (new change set)
    P-->>B: ack
```

> **As built, post-audit plan item 3.1d (2026-09-19).** `SyncEnvelope` (`src/Pine.Infrastructure/Sync/`). Every
> choice this section left open was taken the conservative way, and the two places it departs from the letter of
> `15-Security` §7 and of this section are stated rather than glossed.
>
> | Property | As built |
> |---|---|
> | Plaintext | The `pine.sync.v1` JSON of `SyncChangeSet`, UTF-8. **No compression**: nothing here asks for it, and compressing before encrypting makes the ciphertext's length depend on its content. |
> | Cipher | AES-256-GCM, 12-byte random nonce, 16-byte tag — the primitive the key wrap and the vault file cipher already use. |
> | Key per envelope | `HKDF-SHA256(sync key, salt = 32 random bytes carried in the header, info = "pine:sync-envelope:v1")`. No two envelopes share a GCM key, so the random-nonce limit on one key never applies. |
> | Header (what the provider sees) | Magic `PINESYNC`, format version 1, source and target vault ids, the cursor (since clock and source clock), the creation time in milliseconds, the key id, the salt and the nonce — 126 bytes, fixed length. The ciphertext's size is its own length. |
> | Binding | The **whole header is the AEAD's associated data**, so no field can be edited and no header moved onto another envelope's body. |
> | "The signature is over the ciphertext" | **The GCM tag**, which authenticates ciphertext and header under a key only holders of the sync key can derive. It proves "sealed by a holder of the user's sync key", not "sealed by device X": a per-device signature needs device key pairs and a trust model, which do not exist (TD-154). |
> | The sync key | **Generated (32 bytes, CSPRNG), not derived from a data key.** `15-Security` §7 says it "is derived from the data key"; two vaults' data keys are independent random values, so a key derived from one could never be computed by the other, and it would change when the data key is rotated (TD-86). What §7 protects is kept: each vault holds the key only **sealed under its own data key** (`sync.key` beside `pine.db`, through the same `IVaultFileCipher` boundary as the categoriser's model), so reaching it still needs that vault unlocked. It is never replaced silently. Its public id is `HKDF-SHA256(sync key, info = "pine:sync-key-id:v1")`, 16 bytes. **Pairing — how a second vault is given it — is not built** (TD-154). |
> | Refusals | Typed (`SyncEnvelopeRefusedException.Reason`), each before anything is written: malformed or truncated, another version, the wrong key (by key id), not authentic (any tag failure — one reason, no detail), sealed for another vault, a payload that disagrees with its header, **a replay or an older envelope** (its source clock is not beyond this vault's cursor for its source), and a gap (it starts after that cursor). A newer envelope that overlaps what was already applied is accepted, as a change set is. |
>
> A known-answer vector for the format is pinned in `SyncEnvelopeTests`; it was computed independently of the
> implementation (Python's HMAC for HKDF, OpenSSL for the AES blocks, GCM by hand) and agreed byte for byte.

## 6. Change Set Semantics

- A change set is *ordered* by a Lamport clock keyed on the device.
- A change set is *idempotent*: applying the same change set twice
  has the same effect as applying it once.
- A change set MAY contain tombstones.
- A change set MUST NOT contain secrets in plaintext.

## 7. Conflict Resolution Rules

| Field class                                  | Strategy                                                                |
|----------------------------------------------|--------------------------------------------------------------------------|
| Tags, attachments, splits                    | Set-union (idempotent CRDT-style).                                       |
| Numeric contributions, quantities            | Additive merge; the totals are re-derived from the merged set.            |
| Account name, currency, opening balance      | Last-writer-wins by `(device_id, lamport_clock)` with explicit timestamp. |
| Transaction amount, payee, posted_at         | User prompt.                                                              |
| Budget threshold, period                     | User prompt (the user knows their budget).                                |
| Savings goal target, target date             | User prompt.                                                              |
| Holding cost basis, quantity                 | User prompt.                                                              |
| Holding price                                | Most recent valuation wins; older is kept as historical.                   |
| Categorisation                               | Most recent wins with the per-user override flag preserved.               |
| Rule definitions                             | User prompt.                                                              |
| Plugin installations                         | Most recent wins; permissions are re-granted.                             |
| Settings                                     | Most recent wins; telemetry consent is *not* implicitly granted.          |

**Rationale.** The table is normative. A new field class is added
explicitly; "everything is LWW" is rejected.

## 8. Provider Contract (high level)

> **Amended by ADR-0021.** This section previously handed `ISyncProvider` to `33-Plugin-SDK`, which
> is withdrawn. The interface is unchanged and becomes Pine's own: providers are compiled in and
> shipped with the application, not loaded from outside it. Nothing else in this section changes,
> because nothing else in it depended on who wrote the implementation.

A provider implements `ISyncProvider` with the following surface:

| Method                                | Purpose                                                                 |
|---------------------------------------|-------------------------------------------------------------------------|
| `Capabilities()`                      | Returns what the provider can do (e.g. partial read, append-only).      |
| `Auth()`                              | Acquires/refreshes credentials; returns a provider session.             |
| `Upload(envelope, cursor)`            | Uploads an envelope and returns the new cursor.                          |
| `Download(sinceCursor, limit)`        | Downloads envelopes since a cursor.                                      |
| `Delete(envelopeId)`                  | Deletes a previously uploaded envelope (GDPR support).                   |
| `Health()`                            | Returns the provider health (reachability, quota).                       |

~~The contract is owned by `33-Plugin-SDK`~~ — the contract is owned by this
document (ADR-0021); the security rules are owned by `15-Security`.

## 9. Offline Behaviour

- All sync operations are queued locally.
- The queue is persistent and survives crashes.
- The queue has a configurable size limit; when full, the user is
  asked to free space or change provider.
- The app is fully usable while the queue is non-empty.
- The queue is encrypted at rest.

```mermaid
stateDiagram-v2
    [*] --> Online
    Online --> Offline: network lost
    Offline --> Online: network restored
    Online --> Syncing: queue not empty
    Syncing --> Online: queue empty
    Offline --> Queued: changes made
    Queued --> Syncing: Online reached
```

## 10. Cloud Only Mode

> **Out of scope, decided rather than deferred (post-audit plan item 3.1h, 2026-10-05).** This section assumes a
> cloud provider — "the source of truth is the provider" is only meaningful when the provider is a remote service
> a device can be offline from. ADR-0012 forbids Pine from making any network connection, and §8's one compiled-in
> provider is a shared folder, which has no "Cloud Only" state to be in: a folder is either reachable or it is not,
> and §9 already covers that. Building a Cloud Only mode would mean building a cloud provider first, which ADR-0012
> rules out the same way ADR-0021 ruled out a plugin host. This section is kept for the record and is not acted on,
> the way §8's withdrawn plugin ownership is kept rather than deleted.

Per `00-Vision` §10 and the user-approved rule: **Cloud Only
requires an encrypted local cache** so the app stays usable
offline.

- The local cache is a subset of the data the user has accessed in
  the last N days, configurable.
- The cache is encrypted with the user's data key.
- The cache is purged on logout or on the user's request.
- The cache is *not* the source of truth; the source of truth is the
  provider. The cache exists to make the app usable offline.

## 11. User Controls

- The user can enable or disable sync per provider.
- The user can pause and resume sync.
- The user can force a full re-sync (destructive; explicit
  confirmation required).
- The user can change the provider at any time; the new provider
  receives the same envelopes.
- The user can delete all envelopes from a provider (right to
  erasure).

## 12. Audit and Telemetry

- Every sync attempt is logged locally with: timestamp, provider,
  envelopes up, envelopes down, conflicts detected, conflicts
  resolved.
- Telemetry about sync is opt-in only.
- The user can inspect the audit log from the Settings module.

## 13. Cross-Cutting Sync Constraints

- A provider MUST NOT require the user to grant the host root
  credentials.
- A provider MUST support deletion of envelopes (right to erasure).
- A provider MUST NOT log plaintext user data.
- A provider MUST be sandboxed; it MUST NOT modify the local store.
- A provider MUST be tested in CI for the conflict matrix of §7.

## 14. Deferred Decisions

| ID     | Decision                                                                                  | Owning document(s)         |
|--------|-------------------------------------------------------------------------------------------|----------------------------|
| SY-D-1 | Default cursor implementation (provider-specific vs. unified).                            | `33-Plugin-SDK`            |
| SY-D-2 | Conflict resolution UI details.                                                           | `42-UX-Guidelines`         |
| SY-D-3 | Cloud Only default cache size and policy.                                                  | `42-UX-Guidelines`         |

## 15. Quality Checklist

- [x] Envelope, providers, conflict resolution, offline behaviour,
      encryption, audit, user controls are all present.
- [x] OD-02 is closed with a three-layer strategy.
- [x] Field-class resolution table is normative.
- [x] Cloud Only keeps an encrypted local cache.
- [x] No TODO placeholders.
- [x] No contradiction with `00-Vision`..`13-AI-Architecture`.

## 16. Revision History

| Version | Date       | Author                | Change                                                       |
|---------|------------|-----------------------|--------------------------------------------------------------|
| 0.1     | 2026-07-08 | Engineering Review Board | Initial sync architecture; closes OD-02.                  |
