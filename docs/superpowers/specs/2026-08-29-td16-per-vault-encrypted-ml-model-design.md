# TD-16 — Per-vault encrypted ML model, categoriser scoped to the vault session

| Field | Value |
|---|---|
| Audit row | Phase 1, item 4 — `60-Production-Readiness-Audit` §10 |
| Debt | TD-16 |
| Date | 2026-08-29 |
| Status | Approved for implementation |

---

## 1. The defect, as verified rather than as quoted

The audit's §4.9 evidence was re-checked against the code before any of this was designed. It
holds, and two facts it does not mention were found alongside it.

| Fact | Evidence |
|---|---|
| The model path is a relative constant, resolved against the process working directory | `MlNetCategorizer.cs:34` |
| It is read and written in plaintext, with no encryption anywhere on the path | `MlNetCategorizer.cs:48,50,134` — line 127 carries the comment "transparent encryption in final phase" |
| One file serves every vault | there is no vault-derived component in the path at all |
| No model file is tracked, LFS-covered, or copied to output | `git ls-files`; `.gitattributes` covers `*.onnx` only; `Pine.Infrastructure.csproj:38-40` globs `.onnx`/`.txt` |
| No test anywhere touches the categoriser, the trainer, or the training service | grep over `tests/` returns nothing |
| **The AI stack is registered in one head only** | `Pine.Ui.Windows/Program.cs:103-106`; Macos, Android, iOS, Web and Cli register nothing |
| **Two categoriser instances exist, so training never reaches predictions** | DI registers one at `Program.cs:104`; `DefaultLocalAiProvider.cs:43` news up a second |

The last row is worth stating on its own, because it changes what this ticket is. `TrainModelAsync`
rebuilds `_predictionEngine` on the instance the container holds. Every prediction goes through
`DefaultLocalAiProvider`, which holds a *different* instance whose engine was built in its
constructor. **Training has no effect on prediction until the process restarts.** Fixing TD-16
forces the two instances into one, so this is closed as a consequence rather than as an extra.

Because no model file has ever been tracked or shipped, **there is nothing to migrate**. The
correct-from-zero situation that shaped iteration 13 applies here for a different reason.

## 2. Scope decision

Taken by the product owner, recorded here because it bounds everything below:

> The cross-head registration gap is **registered as a new debt row, not fixed**. This iteration
> stays on TD-16 proper and is verified on the Windows head.

The reasoning: `ImportTransactionsCommandHandler` ctor-injects `IAIProvider`
(`ImportTransactionsCommand.cs:117`), so import is unresolvable on four of five heads. That is a
real defect, but it is a "what is each head for" question, which is where TD-85 already lives — and
closing it would ship ML.NET registrations into Android and iOS, neither of which can be compiled
on this machine while TD-07 is open.

## 3. Architecture

### 3.1 Reaching the vault key from `Pine.Infrastructure`

`MlNetCategorizer` lives in `Pine.Infrastructure`, which references only `Pine.Core`. The data key
lives on `IVaultSession` in `Pine.Application`. The composition roots bridge this for
`PineDbContext` with a factory lambda, but that works only for a scoped object rebuilt per request;
it cannot serve a singleton that must outlive lock/unlock cycles.

`IVaultLockState` exists for precisely this problem and deliberately exposes one bit. It is widened
**by moving members, not by adding them**:

```csharp
// Pine.Core.Security
public interface IVaultDataKeyAccessor : IVaultLockState
{
    byte[]? Key { get; }     // relocated from IVaultSession, same name, same contract
    string? DbPath { get; }  // relocated from IVaultSession, same name, same contract
}
```

`IVaultSession` then extends `IVaultDataKeyAccessor` instead of `IVaultLockState` and drops its own
declarations of the two members. `VaultSession` already satisfies the result, so there is **no
implementation change and no call-site change**. The binding is one `TryAddSingleton` forward
beside the existing `IVaultLockState` forward at
`PineApplicationServiceCollectionExtensions.cs:85`.

**The cost, stated rather than buried:** this widens what `Pine.Infrastructure` can see from "is
the vault locked" to "the data key". `IVaultLockState`'s doc comment currently promises the narrow
version, and that comment is amended in the same change rather than left to read as though the
promise still held.

### 3.2 The cipher

```csharp
// Pine.Core.Security
public interface IVaultFileCipher
{
    byte[] Encrypt(ReadOnlySpan<byte> plaintext, string purpose);
    byte[] Decrypt(ReadOnlySpan<byte> sealedBlob, string purpose);
}
```

Implemented as `AesGcmVaultFileCipher` in `Pine.Infrastructure/Security/`.

- **Key separation.** The file key is `HKDF-SHA256(dataKey, info: "pine:file-cipher:v1:" + purpose)`
  via the BCL `HKDF.DeriveKey` — no new package. The model file is therefore not encrypted under
  the literal SQLCipher key, so compromise of one blob's key does not hand over the database.
  It also means a blob sealed for one purpose cannot be opened as another.
- **Blob layout:** `PINEFC1\0` (8-byte magic, version in the last character) ‖ nonce (12) ‖
  tag (16) ‖ ciphertext.
- **Associated data:** magic ‖ length-prefixed purpose. This follows `VaultHeaderBinding`'s rule —
  bind everything available as a rule, not as a per-field judgement call.
- The vault id is **not** bound into the AD. It would require reading the header, and the HKDF
  input is already the vault's own data key, so a blob from another vault fails at the tag anyway.

### 3.3 The model store

```csharp
// Pine.Core.Ai
public interface ICategorizerModelStore
{
    string? VaultFingerprint { get; }  // null when locked
    byte[]? TryRead();
    void Write(ReadOnlySpan<byte> model);
}
```

`VaultCategorizerModelStore` in `Pine.Infrastructure/Ai/Categorisation/` composes the accessor and
the cipher. The file is `{DbPath}/ai/categorizer.model`.

- Writes are temp-file-then-`File.Move(overwrite: true)`, so a crash mid-save cannot leave a
  truncated model behind for the next start to cope with.
- `TryRead` returns `null` for absent, unreadable **and undecryptable** files. See §3.4.

### 3.4 The categoriser

`MlNetCategorizer` takes `ICategorizerModelStore` and drops the `ModelPath` constant entirely.

- **Loading is lazy, not constructor-time.** The constructor runs at container-build time, when no
  vault is open — a large part of why the current design is wrong.
- At every use it compares the store's `VaultFingerprint` against the fingerprint it loaded from,
  and drops the engine on mismatch or on a locked vault. The check happens at the moment of use, so
  a stale engine cannot answer; correctness does not depend on any event being delivered.
- **A model that will not decrypt is discarded and retrained, not reported.** It is derived data,
  always reproducible from the ledger. Turning a corrupt cache into a vault-open failure would be
  strictly worse than losing it. Logged at warning level.
- The replaced `_predictionEngine` is disposed on retrain; today it leaks.
- A training run whose vault locked mid-flight aborts without writing.

### 3.5 Composition

- `DefaultLocalAiProvider` takes `IMlNetCategorizer` by injection; `new MlNetCategorizer()` is
  deleted, and the categoriser leaves its `Dispose` because the container now owns it.
- `ICategorizationTrainer` is bound by **forwarding to the same singleton** as `IMlNetCategorizer`.
  That single line is what closes the training-never-reaches-predictions defect.

## 4. Testing

Unit — `tests/Pine.Tests.Unit/Ai/`:

- Cipher: round-trip; wrong purpose rejected; tampered nonce, tag and ciphertext each rejected;
  a different vault key rejected; nonce differs across two seals of identical plaintext; the
  plaintext appears nowhere in the blob.
- Store: absent → `null`; corrupt → `null` without throwing; locked → throws; the written path is
  inside the vault directory and nowhere else.
- Categoriser: no model → `Guid.Empty` at zero confidence; train-then-predict succeeds on one
  instance; a fingerprint change drops the engine; sub-threshold training writes nothing.

Integration: train in vault A, open vault B, assert A's model is neither visible to B nor a
readable ML.NET zip on disk.

Verification discipline follows §12.27's: every build and every test is run by the orchestrating
session, no agent's claim about a compile or a test result is accepted, and each falsification
mutation is reverted and byte-compared against its backup afterwards.

## 5. Explicitly not done

1. The four heads that register no AI at all — the §2 decision. New debt row.
2. `32-AI-Training` §6.1's "ships with a default model" — no model is seeded, and none ever was.
3. The model is read whole into memory. ML.NET categoriser models are small; this is a stated
   limit, not an oversight.
4. **Train/serve skew.** Training builds its text from `Payee` alone
   (`CategorizationTrainingBackgroundService.cs:109`) while import categorises on `Payee + Notes`
   (`ImportTransactionsCommand.cs:253`). A genuine model-quality defect, unrelated to encryption.
   Registered, not fixed.
5. The key remains a managed `byte[]`, with the same caveat §12.27 item 6 records.

---

## 6. Deviations found during implementation

Recorded here rather than edited into the sections above, so the design and what actually happened
stay distinguishable.

1. **`VaultLockedException` already existed.** The design assumed a new exception type in
   `Pine.Core.Security`. `Pine.Application.Security.VaultLockedException` was already there, with the
   same meaning, and adding a second produced a compile-time ambiguity in an existing integration
   test. The existing type was **moved down to `Pine.Core.Security`** rather than the new one being
   renamed — two types with one name and one meaning is a latent trap, since a `catch` clause would
   depend on which `using` was in scope.
2. **The registration was extracted into `AddPineCategorisation()`**, which the design did not call
   for. The reason emerged from the design's own logic: the two-instance defect it closes lives in a
   composition root, and a composition root inside a `WinExe` is unreachable from a test. Without the
   extraction, the most important behavioural fix in this iteration would have had no test at all.
   It also removes four inline lines from `Program.cs`, which is a small slice of TD-29.
3. **Two falsification mutations survived**, and correctly so. Purpose separation is enforced both by
   a distinct HKDF-derived key and by the authenticated associated data, so removing either alone
   leaves behaviour correct; removing both is caught. Reported as defence in depth rather than as a
   coverage gap — see audit §12.29.
4. **`CategorizationTrainingBackgroundService` gained a typed `catch`** for the relocated exception,
   while keeping its existing message-sniffing clause. The untyped clause is still reachable, because
   the `PineDbContext` factory in each composition root still throws a bare
   `InvalidOperationException`. Deleting it would have turned an ordinary locked vault back into an
   error logged every five minutes. That remains TD-23's problem, not this iteration's.
