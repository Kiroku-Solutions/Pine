# Receipt trust: duplicates, dates and confidence

| Field | Value |
|---|---|
| Ticket | `60-Production-Readiness-Audit` Phase 1, item 7 |
| Debt rows | TD-20, TD-21, TD-22 |
| Status | Approved design |
| Date | 2026-08-30 |

## 1. Why these three are one ticket

All three are the same defect class: the receipt pipeline tells the user something it
has not earned the right to say. It says "this is a new transaction" without having
looked (TD-20), it says "5 March" when the paper says 3 May (TD-21), and it says
"90% confident" from a tally that never touched the OCR engine's own confidences
(TD-22). Each one is a claim, and none of them is measured.

The pipeline's one real strength, recorded in the audit's executive summary, is that
its output is *presented as editable suggestions requiring explicit user confirmation*.
That is the design this ticket extends: every new signal here goes to the confirmation
screen, and none of them decides anything on the user's behalf.

## 2. Verified starting state

Read from the code at this commit, not from the register.

| Fact | Evidence |
|---|---|
| The receipt write path performs no duplicate lookup | `Pine.Application/Transactions/Commands/AddTransactionCommand.cs:63` — the handler loads the account and writes |
| Import de-duplication already exists and is indexed | `ITransactionRepository.GetExistingImportKeysAsync`; index `IX_Transactions_AccountId_ImportKey` (migration `20260827141109`) |
| The date cascade tries `InvariantCulture` first, and `InvariantCulture` is MM/dd | `OnnxNlpFieldExtractor.cs:566-590`. It also uses `TryParse`, not `TryParseExact`, so the `es-EC` leg is unreachable for any ambiguous date |
| The date regex requires two-digit day and month | `OnnxNlpFieldExtractor.cs:67` — `\d{2}[/-]\d{2}[/-]\d{4}` |
| The confidence score is a literal tally | `OnnxNlpFieldExtractor.cs:825-846` |
| Real per-word OCR confidence exists and is discarded | `Pine.Core/Ai/OcrWord.cs:19` (`float Confidence`), never read by the extractor |

## 3. TD-21 — locale-driven date parsing

New type `Pine.Core/Ai/ReceiptDateParser` (BCL only, no OCR types in its signature, so it
is testable without a pipeline).

- The regex widens to accept `d/M/yyyy`, `dd/MM/yy` and `.` as a separator, in addition to
  what it accepts today.
- Parsing uses `TryParseExact` against an explicit format list. Never `TryParse` with a
  culture: `TryParse` is what made the `es-EC` leg unreachable, because a culture accepts
  more than its own short pattern.
- Format order: ISO (`yyyy-MM-dd`, `yyyy/MM/dd`) first because it is unambiguous, then the
  day/month order implied by `CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern`,
  then the opposite order.
- Return type `ReceiptDateCandidate(DateTimeOffset Value, bool IsAmbiguous, DateTimeOffset? Alternative)`.
  `IsAmbiguous` is true only when both components are <= 12 **and** both orders yield a
  distinct, plausible date. A two-digit year that resolves the same under both orders is
  not ambiguous.
- The plausibility window (`Year > 2000 && Year <= now.Year + 1`) applies per candidate,
  not to whichever string parsed first.
- `ScanReceiptViewModel` surfaces the ambiguity with the alternative reading and a one-click
  swap. It does not silently pick.

## 4. TD-22 — real, per-field confidence in bands

`ComputeConfidence` is deleted, not recalibrated. `ReceiptExtractionResult.ConfidenceScore`
is removed from the record and from the UI.

- `ReceiptExtractionResult` carries per-field confidence: merchant, total, date, and each
  line item.
- A field's confidence is the **minimum** confidence of the `OcrWord`s that produced it. A
  field is worth what its worst-read word is worth; a mean would let one crisp word carry a
  misread one.
- Bands live in `Pine.Core`: `High >= 0.90`, `Medium >= 0.70`, `Low` below that, plus a
  distinct `NotFound` for a field no OCR word produced. `NotFound` is not a low score — today
  the "Unknown Merchant" fallback and a badly-read merchant are indistinguishable in the tally.
- The item-sum-versus-total bonus is dropped. The TD-19 reconciliation panel already states
  that relationship in a form the user can check.
- The confirmation form highlights `Low` and `NotFound` fields. That is the whole user-facing
  behaviour: no global percentage anywhere.

## 5. TD-20 — receipt duplicate detection

No new persistence infrastructure. The import path's lookup and index are reused.

- `ImportKeyFactory.FromReceipt(...)` with a new scheme prefix `s1:`, derived from the
  normalised merchant, the calendar date, the signed amount, the currency and the account id.
  A new prefix is mandatory: the type's own documentation requires that a changed derivation
  take a new prefix so old rows fail to match rather than match by accident.
- New query `CheckReceiptDuplicateQuery` in `Pine.Application`, returning the matching
  transaction's date, amount and payee, or nothing.
- `ScanReceiptViewModel` runs it twice: when extraction completes, and again immediately
  before saving, because the user may have edited the date or the amount in between. The
  second run is the one that matters and is the one a mutation will target.
- The warning is non-blocking. `SaveTransactionAsync` proceeds when the user confirms —
  two identical coffees on one day are two transactions, the trade `ImportKeyFactory.Derive`
  already documents.
- `AddTransactionCommand` gains an optional `ImportKey`, so the saved transaction carries
  its `s1:` key and a later re-scan recognises it. Without this the detection fires once and
  never again.

## 6. Testing

TDD per sub-item, plus falsification mutations in the standing practice of iterations 8-17:
break the code deliberately, confirm a test dies.

Planned mutations:

1. Swap the day/month order the culture implies.
2. Return `IsAmbiguous = false` unconditionally.
3. Use `max` instead of `min` across a field's word confidences.
4. Return `High` for a `NotFound` field.
5. Drop the `s1:` prefix so receipt keys share the import namespace.
6. Skip the pre-save duplicate re-check after the user edits the date.
7. Keep `TryParse` instead of `TryParseExact` (asserts the TD-21 cause itself is covered).

Every agent-reported result is re-verified by running `dotnet test` locally. Reported counts
are checked against that run, not accepted.

## 7. Out of scope, explicitly

TD-99 (the budgets screen is a mock), TD-97 (the flaky suite) and TD-98 (unhandled
concurrency conflict) are separate rows; TD-98 belongs to Phase 1 item 8. Log encryption,
image-level perceptual hashing and a persisted per-vault date-format setting (which needs the
Phase 2 `IOptions` work, TD-27) are not built here.
