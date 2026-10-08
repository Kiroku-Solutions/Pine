# 63 · Remaining Production Tasks

| Field | Value |
|---|---|
| Document ID | `PINE-DOC-63-REMAINING-TASKS` |
| Status | Non-normative — Active punch list |
| Created | 2026-10-07 |
| Reconciled | 2026-10-07 · Rebuilt from a full row-by-row audit of `60-Production-Readiness-Audit`'s 154 TD rows, cross-referenced with `62-Post-Audit-Plan`. The previous version of this list was already stale the day it was written — it named TD-105, TD-106, TD-122, TD-123, TD-145 and TD-154 as open; all six closed or were decided the same day. This version buckets every non-closed row by what actually blocks it, which is the question that matters now: 83 rows closed, 20 narrowed, 51 open, of which only 1 needs a purchase (TD-108) and 7 need hardware/a build workload this project does not have — everything else below is pure software, already decided or waiting on nothing but being built. |

This document contains the distilled, remaining open tasks from `60-Production-Readiness-Audit` and
`62-Post-Audit-Plan`, bucketed by what blocks each one. It is rebuilt from a fresh audit rather than edited in
place, because a hand-maintained punch list goes stale exactly the way the first version of this file did.

## Blocked on a purchase (1 row)

- **TD-108** — code-signing certificate. Bought from a CA against a verified legal identity; no engineering work
  produces one.

## Blocked on hardware or a build workload this project does not have (7 rows)

- **TD-08, TD-44, TD-109, TD-114, TD-115, TD-138** — Android/iOS SDKs, a macOS/Linux build runner, or a second
  physical device, depending on the row.
- **TD-153** — needs a second physical camera to safely verify the Media Foundation enumeration order against
  OpenCV's index ordering before any UI ships on top of it (an abandoned attempt is recorded, not kept — see
  `62-Post-Audit-Plan` §7's TD-153 note).

## Decided 2026-10-07, now ordinary software work (10 rows)

Each decision is recorded in full in `62-Post-Audit-Plan` §7.4. One-line reminders:

- **TD-63** — a budget's currency is the vault's base currency.
- **TD-82** — pre-unlock logs stay plaintext, redacted of PII; amend the documents to say so.
- **TD-100** — reconciliation preview defaults to the vault's base currency's scale.
- **TD-103** — accept headless Skia as a stated CI requirement.
- **TD-110** — keep bundling OCR weights; ADR-0012 stands.
- **TD-112** — document-status rule applies to `docs/*` only; GitHub/AI meta-files are exempt.
- **TD-119** — config is pre-unlock-screen-paint needs only (theme, language); everything else is an encrypted
  Setting.
- **TD-126** — a stream in a message is owned by its recipient (ownership transfer).
- **TD-145** — ✅ **closed outright by the decision**: retention is infinite; nothing to build, the current
  behaviour (no purge ever) already matches the decision. See `60-Production-Readiness-Audit` TD-145.
- **TD-154** — per-device sync signatures are not worth building for v1; the shared sync key's authentication is
  enough.

## Pure unbuilt software, no decision and no external blocker (51 rows, now that TD-145 has closed)

Ready to implement in any order; none needs a human's input first.

- **Transactions/ledger**: TD-02 (transaction edit command), TD-59 (owned-child EF state set explicitly), TD-62
  (validators beyond currency), TD-68 (update/edit command), TD-69 (unvoid command), TD-70 (set-opening-balance
  gesture), TD-71 (hide voided rows), TD-77 (`OpeningBalanceAmount` as a real `Money`), TD-95 (`Quantity` scale +
  column type), TD-96 (`Budget` dates get `DateUtc` treatment).
- **Import**: TD-50 (file-type/magic-byte + size-cap validation), TD-64 (currency validation before dedup),
  TD-65 (dedup check-then-insert race), TD-102 (import-key scheme that doesn't leak decimal scale).
- **Sync**: TD-144 (two residual edge cases: manual migration-down/up rewind, byte-copied vault collision).
- **Rules/receipts**: TD-101 (word provenance through adjustments), TD-141 (file picker for manual attachments),
  TD-142 (preview-then-apply for retroactive rules), TD-143 (multi-condition rules UI).
- **Accounts/budgets/categoriser**: TD-78 (currency validators), TD-89 (consent setting before training), TD-91
  (one shared train/serve text function), TD-124 (read-only legacy-transfer detector), TD-125 (transfer-pair FK,
  after TD-124), TD-133 (page the training-set query).
- **Security/ops**: TD-86 (`PRAGMA rekey` on password change), TD-87's Windows/macOS half (platform-held key
  wrap for biometric unlock — the Android/iOS half is bucket-hardware), TD-110's already-decided bundling stands.
- **CI/build hygiene**: TD-46 (anchor two relative model paths), TD-51 (clean stray draft comments), TD-55
  (`NuGetAuditMode`), TD-104 (typed VM factories for 7 remaining view models), TD-107 (port `package.sh`'s guards
  into a test), TD-111 (network-deny-all harness for the packaged app), TD-113 (generate traceability matrix),
  TD-129 (`packages.lock.json` + `RestoreLockedMode`), TD-130 (harden shell-pipeline measurement scripts), TD-131
  (extend the no-unnamed-asset rule beyond `Assets/Ai`), TD-135 (branch-coverage floors).
- **Architecture**: TD-57 (move `IVaultMigrator` to its correct layer), TD-73 (natural-language word list for
  passphrase entropy), TD-76 (searchable currency picker), TD-81 (extract `IVaultManager` for testability), TD-93
  (UI-thread-blocking headless-dispatcher test harness), TD-118 (document the tuned-vision-constants boundary),
  TD-127 (give views a logger, or move file-picker/clipboard work to view models).
- **Narrowed, nothing left to build, only to keep observing**: TD-97 (mechanism fixed, flake never reproduced),
  TD-140 (diagnosed and fixed).
- **Correctly deferred, not yet actionable**: TD-116 (needs a head without vision to gain a receipt entry point
  first — nothing to build until that happens).
- **Register hygiene, flagged not fixed**: TD-10 is stale — its "what is missing" note says "Tracked as TD-56",
  and TD-56 is closed. A future pass should close TD-10 outright, the same way TD-122/TD-128/TD-154 were found
  and fixed this session.
- **TD-84** — the Diagnostic Information Bundle itself, now that its plugin-inventory third is removed (item
  4.2): still waits on metrics and SLOs, which are themselves unbuilt bucket-(d) work, not a decision or a
  purchase — large, but pure software.
