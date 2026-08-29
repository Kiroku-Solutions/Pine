# 60 · Production Readiness Audit

| Field | Value |
|---|---|
| Document ID | `PINE-DOC-60-AUDIT` |
| Status | **Living document** — §1–§11 are the original audit (baseline, unaltered). §8/§9/§10 now carry status markers and §12 is the remediation log. |
| Created | 2026-08-26 |
| Audited commit | `c1c8844` on `feat-ui-production-theming` |
| Method | Full source read + `dotnet build` + `dotnet test` execution. No claim below is taken from documentation. |
| Scope | 154 `.cs` / 26 `.axaml` · ~11.7k LOC `src/` · 741 LOC `tests/` · 17,292 lines `docs/` |
| Overall rating | **2.3 / 5 ★★☆☆☆** |
| Classification | **Not Production Ready** |
| Remediation | Iterations 1–6 closed Phase 0 items 1–8. Iteration 7 **built** item 9 (TD-11) without executing it — see §12.16. Iteration 8 closed the TD-18 half of item 10 — see §12.18. Iteration 9 closed the TD-19 half, ported the TD-11 harness into `tests/`, and reclassified TD-74 — see §12.20. **Every Phase 0 item has now been worked, and items 1, 4–11 are ✅. Phase 0 is NOT clean: item 2's export is still unreachable and item 3 regressed — CI has had its triggers removed since `79c5364` and has never run.** Test suite 28 → **432** (313 unit + 119 integration), both suites green at this commit. See **§12**. §12.12 is a reconciliation pass correcting stale §8/§9 claims; §12.14, §12.16, §12.18, §12.20 and §12.22 repeat it as standing practice. |
| Last updated | 2026-08-28 |
| Related | `56-IMPLEMENTATION-PLAN`, `58-TechDebt-WeakReferenceMessenger-Lifecycle`, `59-Current-Status`, `STATUS.md` |

> **Purpose of this document.** This is the working backlog for a dedicated
> remediation cycle. Findings are evidence-based and each carries a file
> reference, an impact statement and a debt ID (`TD-nn`). Work Phase 0 top to
> bottom; it is ordered by irreversibility, not by effort.

---

## 1. Executive summary

Pine is an offline-first personal finance vault: an encrypted SQLite (SQLCipher)
database per vault, a hexagonal .NET 10 solution, an Avalonia UI, and an entirely
on-device receipt pipeline (YOLO → OpenCV → PaddleOCR → heuristic extraction →
ML.NET categorisation). The intent is coherent and the domain vocabulary is right.
The engineering is not yet at the level the domain demands.

Three things dominate the assessment.

**First, there is no transactional boundary anywhere in the write path.**
`PineDbContext` is registered `Transient` and each repository is `Transient`, so
the two repositories inside a single command handler hold *different* `DbContext`
instances over *different* SQLite connections, and each `SaveAsync` calls
`SaveChangesAsync` on its own. A transaction and the account balance it changes
are two independent, uncoordinated writes. The code says so itself, three times,
in comments: *"this should be inside a transaction scope ideally"*.

**Second, the ledger is write-only and unrepairable.** There is no
update-transaction and no delete-transaction command anywhere in
`Pine.Application`, and no UI affordance for either. Once an OCR misread or an
interrupted write lands, the user cannot correct it, and balances — stored as a
mutable field on `Account`, never derived from the transaction stream — can never
be recomputed. Balance drift is permanent.

**Third, verification is thin and CI is pointed the wrong way.** The suite is 28
passing tests (23 unit, 5 integration) for ~11.7k lines. Three of the five test
projects are `UnitTest1.cs` stubs that CI dutifully builds and "runs". CI never
builds the application itself, only the test projects — which is exactly why
`Pine.Ui.Web` (and, by the same `using Pine.Infrastructure.Persistence;`, Android
and iOS) references a namespace that does not exist and fails to compile,
undetected. The tests folder is also commented out of `Pine.slnx` behind the
marker `<!-- asdasda`.

Against that: the privacy posture is not marketing. There is no `HttpClient`, no
`System.Net`, no socket, no telemetry sink anywhere in `src/`. The OCR pipeline
runs locally and its output is presented as editable suggestions requiring
explicit user confirmation before persistence — the correct pattern, and a real
strength. `decimal` is used consistently for money, never `double`. Those
foundations are worth building on; **the recommendation is remediation, not
rewrite.**

---

## 2. Verified build & test state

> **This table is the audit baseline at commit `c1c8844` and is deliberately left
> unaltered.** For the state after remediation work, see **§12**. As of iteration 1
> the suite is 94 passing tests and `Pine.Ui.Web` compiles.

| Project | Result |
|---|---|
| `Pine.Core`, `Pine.Application`, `Pine.Infrastructure`, `Pine.Ui.Avalonia` | ✅ build clean, 0 warnings |
| `Pine.Ui.Windows` | ✅ build clean, 0 warnings |
| `Pine.Ui.Macos` | ✅ build clean |
| `Pine.Cli` | ✅ build clean (20 LOC stub) |
| `Pine.Ui.Web` | ❌ **`CS0234: 'Persistence' does not exist in namespace 'Pine.Infrastructure'`** (`Program.cs:8`) |
| `Pine.Ui.Android` | ❌ same `using`; also `typeof(Pine.Application.DependencyInjection)` — type does not exist. Workload not installed locally. |
| `Pine.Ui.Ios` | ❌ same as Android |
| `Pine.slnx` (whole solution) | ❌ fails — Android/iOS workloads |
| `Pine.Tests.Unit` | ✅ **23 passed** |
| `Pine.Tests.Integration` | ✅ **5 passed** |
| `Pine.Tests.Accessibility` / `.Performance` / `.Mutation` | ⚠️ one 10-line `UnitTest1.cs` template each |

Empty projects (0 source files): `Pine.Ai.Default`, `Pine.Ai.Explainer`,
`Pine.Plugins.Host`, `Pine.Plugins.Sdk`.

---

## 3. Scorecard

| Area | Weight | Rating | Basis |
|---|---:|---:|---|
| Architecture | 2.0 | 3.0 | Correct style and direction; composition root broken and duplicated |
| .NET Engineering | 1.0 | 2.5 | Modern language use; no config, no logging, reflection hack |
| Avalonia Architecture | 1.0 | 3.0 | Disciplined MVVM; service locator, fake diagnostics, async void |
| Domain Model | 1.5 | 2.5 | Real DDD; `AddSplit` defect, dual category model, dead entities |
| **Financial Data Integrity** | **3.0** | **1.5** | `decimal` correct; no atomicity, no correction, no rounding, silent data loss |
| OCR / Invoice Pipeline | 1.5 | 3.0 | Human-in-the-loop and swappable; fake confidence, no dedup, dead model |
| Persistence | 2.0 | 2.0 | Migrations correct; no UoW, no concurrency, no indexes, no backup |
| Security | 2.0 | 2.5 | Argon2id + SQLCipher real; no key architecture, policy or auto-lock |
| Privacy | 1.0 | 3.5 | Verified no egress; plaintext cross-vault ML model, unredacted logs |
| Testing | 2.0 | 1.5 | 28 tests; no financial invariant covered; CI never builds the app |
| Error Handling | 1.5 | 2.0 | Consistent UI catches; no taxonomy, swallowed failures, misleading messages |
| Reliability | 2.0 | 2.0 | Partial writes; no backup, rollback, retry or recovery |
| Performance | 1.0 | 3.0 | Adequate now; import and paging degrade predictably |
| Maintainability | 1.0 | 3.0 | Small files, clear naming; dead code, stubs, draft comments |
| Documentation | 1.0 | 2.0 | Extensive and well written; substantially contradicted by the code |
| Dependencies | 1.0 | 2.5 | Sound choices; quadruple OCR models, Windows-only runtimes, no CPM |
| Cloud / External Services | 0.5 | 3.5 | Correctly absent; one setting promises sync that does not exist |
| Production Readiness | 2.0 | 1.0 | No packaging, config, backup, recovery or usable observability |

**Weighting rationale.** Financial Data Integrity carries 3× because silent money
corruption is the failure mode with no recovery and no user-visible symptom.
Architecture, Persistence, Security, Testing, Reliability and Production Readiness
carry 2× because each gates whether the others can be trusted. Cloud carries 0.5×
because there is almost no surface to get wrong.

`Σ(rating × weight) = 61.0` · `Σ(weight) = 27.0` · **`61.0 / 27.0 = 2.26 → 2.3`**

---

## 4. Top 10 problems

### 1. No transactional boundary between a transaction and its account balance 🔴 P0
- **Evidence** — `src/Pine.Ui.Windows/Program.cs` registers `AddTransient<PineDbContext>` and all five repositories `AddTransient`. `AddTransactionCommandHandler` injects `ITransactionRepository` + `IAccountRepository`; each resolves its own context. Both `SqliteTransactionRepository.SaveAsync` and `SqliteAccountRepository.SaveAsync` call `SaveChangesAsync` internally.
- **Impact** — Ledger and balances diverge permanently on any mid-operation failure. Data-corruption class, not a robustness nicety.
- **Action** — Scope-per-request DI, `IUnitOfWork`, one `SaveChanges` inside one `BeginTransactionAsync` per command. (TD-01)

### 2. Transactions cannot be edited or deleted; balances cannot be recomputed 🔴 P0
- **Evidence** — `Pine.Application/Transactions/Commands/` has Add, Import, Record, ScanReceipt, TransferFunds. No Update, no Delete. `ITransactionRepository.DeleteAsync` exists and is called by nothing. `Account.Balance` is stored, never derived.
- **Impact** — Converts problem #1 from recoverable to terminal. Every error is permanent.
- **Action** — `UpdateTransactionCommand` + `VoidTransactionCommand` (prefer a reversing entry over hard delete) + `RecalculateAccountBalanceCommand`. (TD-02)

### 3. Import deduplication silently discards legitimate transactions 🔴 P0
- **Evidence** — `ImportTransactionsCommandHandler`: `existing.Date.Date == tx.Date.Date && existing.TotalAmount.Amount == tx.TotalAmount.Amount`, over all existing transactions loaded into memory. No payee, no reference, no in-batch check, no report.
- **Impact** — Two genuine €3.50 coffees on the same day collapse into one. The same file imported twice duplicates every row. Both directions wrong, user never told.
- **Action** — Stable import key (bank ref, else hash of date + amount + normalised payee + source + row index). Never drop silently — return a review list. (TD-03)

### 4. KDF has no version marker; key architecture contradicts `15-Security` 🔴 P0
- **Evidence** — `Argon2KeyDerivation` holds cost parameters as `private const`. `UnlockViewModel` passes the password-derived key straight to SQLCipher. `docs/15-Security.md` §4 specifies a wrapped data key in platform secure storage with rotation and a versioned format, and declares OD-03 **closed** by it.
- **Impact** — Three consequences: (a) no password change is possible without full re-encryption; (b) biometric/keychain unlock is architecturally impossible; (c) **any future change to the Argon2 parameters makes every existing vault permanently undecryptable.** (c) is cheap now and unfixable later.
- **Action** — Ship a versioned vault header (KDF id + parameters) beside `pine.salt` **before the first real vault exists**. Then envelope encryption. (TD-05, TD-06)

### 5. CI does not build the application; three platform heads are broken 🔴 P0
- **Evidence** — `.github/workflows/dotnet-ci.yml` restores/builds/tests only the five test `.csproj`. No solution build, no format check, no analyzer gate, no coverage threshold (`coverlet.collector` referenced but never given one), no dependency scan. `Pine.slnx` has `/tests/` commented out behind `<!-- asdasda`.
- **Impact** — Root cause of the broken Web/Android/iOS heads. Three of five "test suites" in the green badge assert nothing.
- **Action** — Build the full solution in CI; restore `/tests/` to the solution; fix or delete the broken heads. (TD-07, TD-08)

### 6. No financial invariant is covered by a test 🔴 P0
- **Evidence** — Nothing exercises: transaction+balance atomicity; the currency-mismatch partial write; `ValidateSplits` with >1 split; `TransferFundsCommandHandler` at all (including its reflection call); dedup behaviour; line-item vs total reconciliation; `Money` precision/rounding; `Budget` period boundaries.
- **Impact** — Two of the three critical domain defects here would have been caught by one characterisation test each.
- **Action** — Money-path suite, written alongside the Phase 0 fixes, not after. (TD-09)

### 7. No backup before migration; no recovery path at all 🟠 P0
- **Evidence** — `UnlockViewModel` calls `PineDbInitializer.Migrate(dbContext)` on every unlock with no preceding file copy. No export, no backup command, no audit/history columns.
- **Impact** — A migration that fails partway on the user's only encrypted copy of their financial history is unrecoverable. Local-first means there is no server-side fallback.
- **Action** — Copy to `pine.db.pre-<migration>.bak` before migrating; add encrypted export/import. (TD-10)

### 8. `AddSplit` makes multi-split transactions structurally impossible 🔴 P0
- **Evidence** — `Transaction.AddSplit` appends then immediately calls `ValidateSplits()`, which throws unless the running sum already equals `TotalAmount`.
- **Impact** — Adding the first of two €50 splits to a €100 transaction throws. The splitting feature can only ever hold one split covering the whole amount — which is exactly how every call site uses it today. The invariant is correct; enforcing it eagerly is the bug.
- **Action** — Validate at the aggregate boundary; add `ReplaceSplits(IEnumerable<…>)` that validates the whole set. (TD-04)

### 9. User-data-trained ML model written plaintext outside the vault, shared across vaults 🟠 P1
- **Evidence** — `MlNetCategorizer.ModelPath = "Assets/Ai/categorizer_model.zip"` — relative to CWD, next to the exe, unencrypted. `CategorizationTrainingBackgroundService` trains it from real payees and amounts. `IAIProvider` / `ICategorizationTrainer` are process singletons.
- **Impact** — Derived personal data outside the encryption boundary. One model file shared by all vaults, so "Personal" and "Business" — the reason multi-vault exists — cross-contaminate. The one finding that actively contradicts the product's central privacy promise.
- **Action** — Per-vault, inside the vault directory, encrypted with the vault key; scope the categoriser to the vault session. (TD-16)

### 10. The Diagnostics screen reports fabricated system health 🟠 P1
- **Evidence** — `DiagnosticsViewModel` ctor, under `// Mock Logs`, hardcodes seven lines including `"[INFO] Migrations are up to date."` and `"[WARN] No active internet connection detected."`. `DatabaseStatus = "Connected (Healthy)"` is a literal. `ExportDib()` appends a log line and has the body comment `// To be implemented`. `STATUS.md` describes this as *"real-time log tailing, environment metrics, and DIB export hooks."*
- **Impact** — Actively misleads during incident diagnosis — the one moment the screen exists for. Combined with the absence of real logging, an incident has no evidence trail.
- **Action** — Wire to a real `ILogger` sink and real probes, or remove the screen. (TD-15)

---

## 5. Top 10 strengths

Each verified in the implementation, not inferred from documentation.

1. **The privacy claim is literally true.** `grep -rn "HttpClient\|WebClient\|Socket\|System.Net" src/` returns nothing. No telemetry, no crash reporting, no update pinger.
2. **OCR output requires explicit human confirmation.** `ScanReceiptCommandHandler` returns a DTO; `ScanReceiptViewModel` binds it to editable fields; only `SaveTransactionCommand` persists. The extraction/validation separation is architecturally enforced.
3. **`decimal` everywhere, with a real `Money` value object.** No floating point in any financial path. `Money` is immutable and throws on cross-currency arithmetic rather than coercing.
4. **`TimeProvider` injected consistently** through every aggregate factory and handler. No ambient `DateTime.UtcNow`.
5. **Dependency direction is correct and asserted.** `Pine.Core` has no project reference outward; `ArchitectureTests` guards it with NetArchTest.
6. **The AI port is clean enough to actually swap.** `IAIProvider`, `IImageProcessor`, `OcrWord`, `Quadrilateral` live in `Pine.Core` and expose only `Stream` and primitives. No ONNX/OpenCV type crosses the boundary.
7. **`TreatWarningsAsErrors` + nullable reference types, and the build is clean.** Zero warnings on the Windows head.
8. **Cancellation threaded end to end**, including the full OCR pipeline; the live scanner drives a real `CancellationTokenSource`.
9. **Argon2id above the OWASP baseline** (64 MB / t=3 / p=4, CSPRNG salt) and correctly run off the UI thread via `Task.Run`.
10. **Two documents describe reality precisely** — `58-TechDebt-WeakReferenceMessenger-Lifecycle.md` and `59-Current-Status.md`. Both were written *after* the work. That is the model the other 58 should follow.

---

## 6. Findings by area

### 6.1 Architecture — 3.0

The style (hexagonal + thin CQRS over MediatR) is appropriate, not overkill.
Dependency direction is genuinely correct at the assembly level. Ports live in
`Pine.Core`, adapters in `Pine.Infrastructure`. That part holds up. The failures
are all at composition and cross-cutting.

- 🔴 **P0** No unit of work (TD-01) — see §4.1.
- 🔴 **P0** Five hand-copied composition roots; three do not compile (TD-08, TD-29). Windows uses `AddTransient` for the DbContext; macOS/iOS/Web/Android use `AddScoped`. macOS registers no `IAIProvider`, so any receipt screen throws on resolve. Windows registers `ShellViewModel` transient, macOS singleton. → One shared `AddPineApplication()` / `AddPineInfrastructure()` pair.
- 🟠 **P1** No configuration system at all (TD-27). No `IConfiguration`, no `appsettings.json`, no `IOptions<T>`. Argon2 costs, model paths, the `0.3` AI confidence gate (duplicated in two files) and page size `50` are all compile-time constants.
- 🟠 **P1** Service locator in the UI (TD-28). `MainViewModel`, `ShellViewModel`, `SetupViewModel`, `UnlockViewModel` inject raw `IServiceProvider`. `SetupViewModel.cs:115` and `UnlockViewModel.cs:86` call `GetRequiredService<PineDbContext>()` — the UI reaching for EF Core and triggering `Migrate`.
- 🟡 **P1** Transient `IDisposable`s resolved from the root provider are never released. No DI scope is ever created; the root provider is never disposed. Every command leaks a `DbContext` + connection for the session. Fixed by the same change as TD-01.
- 🟢 Dependency direction enforced by `ArchitectureTests` — **caveat**: written as `ShouldNot().HaveDependencyOn(a).And().HaveDependencyOn(b)`, which composes as "not *all* of" rather than "not *any* of". Prefer `NotHaveDependencyOnAny(...)`.

### 6.2 .NET engineering — 2.5

**Modern and correct:** `net10.0`, `Nullable=enable`, `ImplicitUsings`,
`TreatWarningsAsErrors=true` in `Directory.Build.props` with a clean build.
`TimeProvider` injected everywhere. `[GeneratedRegex]` throughout the extractor and
`CurrencyCode`. `CancellationToken` on essentially every async signature. Records
for commands/DTOs, sealed classes for aggregates.

- 🟠 **P1** No logging abstraction outside one class (TD-14). `ILogger` appears only in `CategorizationTrainingBackgroundService`. Infrastructure logs via `Console.WriteLine` (`OnnxNlpFieldExtractor.cs:125`, `PaddleOcrEngine.cs:107,113`, `CropReceiptViewModel.cs:90`) and `File.AppendAllText(AppContext.BaseDirectory + "ocr_init_error.log")`. The registered provider is `AddConsole()` in a **WinExe** — no console, records go nowhere.
- 🟠 **P1** Reflection bypasses domain encapsulation (TD-26). `TransferFundsCommandHandler`: `typeof(Transaction).GetProperty(nameof(Transaction.LinkedTransactionId))!.SetValue(sourceTx, destTx.Id);`, preceded by the comment *"Wait, source needs to link to destination as well."* → Add `Transaction.LinkTo(...)`, or model the transfer as one aggregate with two legs.
- 🟡 **P2** No central package management; versions drifting (TD-42). `Microsoft.Extensions.DependencyInjection` pinned at both `10.0.11` and `10.0.9`; `EFCore.Sqlite.Core 10.0.9` vs `EFCore.Design 10.0.11`.
- 🟡 **P2** Relative model paths resolved against CWD (TD-46). `MlNetCategorizer` and `HeuristicFieldExtractor` use relative paths while `ReceiptDetector` correctly uses `AppContext.BaseDirectory`. Launching from another directory silently disables categorisation and writes the retrained model wherever the shell was.

### 6.3 Avalonia — 3.0

The most polished layer. `CommunityToolkit.Mvvm` with modern
`[ObservableProperty] public partial` syntax, `[RelayCommand]`, a ViewLocator-style
navigation service, five runtime accent palettes as swappable merged dictionaries,
thin code-behind. Argon2 correctly pushed off the UI thread. No god objects — the
largest VM (`LiveScannerViewModel`, 384 lines) is doing camera work that belongs to it.

- 🟠 **P1** Diagnostics panel displays fabricated data (TD-15) — see §4.10.
- 🟡 **P1** Fire-and-forget async in constructors and messenger handlers (TD-35). `ScanReceiptViewModel` ctor: `_ = LoadAccountsAsync();` and `WeakReferenceMessenger.Default.Register<ReceiptCapturedMessage>(this, async (r,m) => …)` — `async void` semantics. Four `async void` handlers in code-behind (`ScanReceiptView`, `CropReceiptView`, `ImportWizardView`, `DiagnosticsView`). An exception in the messenger handler takes the process down.
- 🟡 **P2** Messenger subscriptions never unregistered (TD-34) — already documented in `58-TechDebt-…`. Correctly diagnosed and triaged; `WeakReferenceMessenger` doesn't root the recipient, so the symptom is stale VMs acting on messages, not a classic leak.
- 🔵 **P2** Accessibility and localisation documented but not implemented (TD-45). All UI strings are hardcoded English literals across 26 `.axaml`; no `.resx`, no `IStringLocalizer`. The Spanish-language extractor regexes suggest a Spanish-speaking target market.

### 6.4 Domain & financial model — 2.5 / integrity 1.5

> **Observed fact.** Every monetary value is `decimal`. No `float`/`double` in any
> financial path — the only `Math.Round` calls in `src/` are on image dimensions in
> `ReceiptDetector`. `Money` is immutable and refuses cross-currency arithmetic.
> That is the most important thing to get right, and it is right. What is missing
> is everything around it.

- 🔴 **P0** No edit/delete/recompute (TD-02) — see §4.2.
- 🔴 **P0** `AddSplit` eager validation (TD-04) — see §4.8.
- 🔴 **P0** Import dedup discards real transactions (TD-03) — see §4.3.
- ✅ **P0 — CLOSED in iteration 4, and reclassified on the way; see §12.9.** Transaction currency never checked against account currency (TD-12). `AddTransactionCommandHandler` builds `Money` from the request, **saves the transaction**, then calls `account.Deposit(...)` which throws on mismatch inside `Money.Add`. Result: an orphaned ledger entry with no matching balance movement. `TransferFundsCommandHandler` is the only handler that validates currency up front. **[Correction, iteration 4: the orphaned-entry claim in the preceding sentence was tested against a real vault and could NOT be reproduced — closing TD-01 had already made it impossible. The residual defect was a wrong-layer throw with a leaky message. Baseline text left intact per this document’s convention; see §12.9.]**
- 🟠 **P1** No currency scale or rounding policy on `Money` (TD-18). No normalisation to minor units, no `MidpointRounding`, no `Multiply`, no `Allocate`. `CurrencyCode` validates the ISO-4217 *shape* (`^[A-Z]{3}$`) but not membership — `"ZZZ"` is accepted — and carries no minor-unit metadata. A total of `10.005` is storable.
- 🟠 **P1** Line items not reconciled against the total (TD-19). `AddLineItem` checks currency only. Nothing compares `Σ TotalPrice` to `TotalAmount` in the domain or in `ScanReceiptViewModel.SaveTransactionAsync` — even though the extractor already computes that ratio, and uses it only to nudge a confidence score.
- 🟡 **P2** Categories modelled twice (TD-32). `Category` is a full aggregate with `Id` and `ParentCategoryId`; `TransactionSplit.Category` is a `string`, and `CategorizeTransactionQueryHandler` resolves the predicted `CategoryId` back to `category.Name` to store as text. `ParentCategoryId` has no FK configured — no referential integrity, cycles possible.
- 🔵 **P2** Inconsistent aggregate modelling (TD-33). `Account`/`Transaction`/`Category` derive from `AggregateRoot`; `Budget` derives from nothing and redeclares its own `Guid Id`. `Budget`'s `Money` uses a value converter while the others use nested `ComplexProperty` — two mapping strategies for one value object. Domain events are raised on two aggregates and **dispatched nowhere**; `DomainEvents` is read by no code.
- 🔵 **P1** New vaults seeded with a fictional $1,000 account (TD-47). `SetupViewModel`: `mediator.Send(new CreateAccountCommand("My First Bank", "USD", AccountType.Bank, 1000m))`. In a tool whose value is an accurate picture of the user's money, seeding invented data is a correctness problem.

### 6.5 OCR / invoice pipeline — 3.0

> **Observed fact — the pipeline's biggest strength.** OCR output is never written
> to the ledger automatically. `ScanReceiptCommandHandler` returns a
> `ReceiptExtractionResult`; the VM binds it to editable fields; persistence
> happens only on explicit `SaveTransactionCommand`.

Fully on-device, cancellation-aware end to end, engine swappable behind
`IAIProvider`. Image work is competent: YOLO segmentation, `ApproxPolyDP` quad
extraction, separate display (sharpen) and OCR (adaptive threshold) streams,
explicit `WriteableBitmap.Lock()` deep copies to avoid GC races in the preview.

- 🟠 **P1** "Confidence" is an arbitrary tally presented as a score, and gates nothing (TD-22). `ComputeConfidence`: base `0.1`, `+0.2` if merchant ≠ `"Unknown Merchant"`, `+0.25` if total > 0, `+0.2` if a date parsed, `+0.15` if any line items, `+0.1` if the item-sum ratio is 0.8–1.3 (commented as "within 20%"). PaddleOCR's per-word confidences are discarded. A fully-populated but entirely wrong extraction scores ~0.9. Nothing refuses to proceed on low confidence.
- 🟠 **P1** No duplicate detection on scanned receipts (TD-20). Dedup exists only in the import handler; `AddTransactionCommandHandler` — the path every receipt takes — has none.
- 🟠 **P1** Ambiguous date parsing (TD-21). `DateRegex` matches `\d{2}[/-]\d{2}[/-]\d{4}`; `ParseDateFromText` tries `InvariantCulture, es-EC, en-US` and takes the first success. Invariant resolves `dd/MM/yyyy` input as `MM/dd/yyyy`. `05/03/2026` on an Ecuadorian receipt (the `es-EC` culture and the `FACTURA`/`RUC` keyword list make the market clear) is stored as 5 March, not 3 May. Never fails, lands in the wrong month's budget.
- 🟡 **P2** Dead ONNX session (TD-36). `Ai/Extraction/OnnxNlpFieldExtractor.cs` defines `HeuristicFieldExtractor`. `LoadModel()` builds an `InferenceSession` over `Assets/Ai/ner_minilm_int8.onnx` — **a file not present in the repository** — and `_session` is referenced only at declaration, null-check, assignment and dispose. Never used for inference. Failure swallowed to `Console.WriteLine`.
- 🟡 **P2** Swallowed exceptions; receipt image never kept. `DefaultLocalAiProvider.ExtractReceiptDataAsync` has a bare `catch { }`. `PaddleOcrEngine` catches init failures, writes `ocr_init_error.log`, sets `_engine = null` → OCR degrades to silence. The `Attachment` entity and table exist but the scan flow never stores the source image — removing the audit value of scanning receipts at all.

### 6.6 Persistence — 2.0

EF Core 10 over SQLCipher, migration-managed (a recent, correct fix —
`PineDbInitializer.Migrate` replaced an `EnsureCreated` that had produced a live
`no such table: TransactionLineItems` bug). Value objects via `ComplexProperty`,
enums as strings, unique index on `Setting.Key`. Right calls.

- 🔴 **P0** Owned collections silently dropped on update (TD-13). `SqliteTransactionRepository.SaveAsync` on an existing row does `_dbContext.Entry(existing).CurrentValues.SetValues(transaction);`. `SetValues` copies scalars and complex properties only — it does **not** touch `OwnsMany` collections, and `Splits` and `LineItems` are exactly that. Same pattern in the account, budget, category and setting repositories. Latent only because no update path exists yet.
- 🟠 **P1** No optimistic concurrency token anywhere (TD-17). No `IsRowVersion()`, no `IsConcurrencyToken()`. The background trainer contends with user saves; last-write-wins, undetected.
- 🟠 **P1** SQLite has no native decimal (TD-31). All `Money.Amount` columns are `decimal` with no explicit column type; the EF SQLite provider maps to **TEXT**, so any server-evaluated comparison or `ORDER BY` is lexicographic. Nothing is broken today because filtering happens in memory — the first `.Where(t => t.Amount > 100)` or `.OrderBy(t => t.Amount)` that reaches SQL returns wrong results, and it will look right on small test data.
- 🟠 **P2** Missing indexes on the actual query paths (TD-30). Only `Transaction.LinkedTransactionId` and unique `Setting.Key` are indexed. `GetPagedAsync` sorts by `Date` descending; `GetByAccountIdAsync` filters on an unindexed `AccountId` with **no FK to `Account.Id`** — deleting an account orphans its entire ledger.
- 🟠 **P0** No backup before migration, no recovery, no audit trail (TD-10) — see §4.7.
- 🔵 **P3** `PineDbContextFactory` uses a fixed all-`0x01` key and writes `design_time.db` into the CWD. Correctly scoped to design time and clearly commented — but worth a `.gitignore` entry and a guard.

### 6.7 Security & privacy — 2.5 / 3.5

> **Observed fact.** `grep -rn "HttpClient\|WebClient\|Socket\|System.Net" src/`
> returns **nothing**. No network code, no telemetry SDK, no crash reporter, no
> analytics. The README's offline-first claim is literally true of the code.

Encryption at rest is real: SQLCipher via `SQLitePCLRaw.bundle_e_sqlcipher`, keyed
with Argon2id at 64 MB / t=3 / p=4 over a CSPRNG per-vault salt. Unlock failures
are deliberately collapsed into one generic message. The gap is between that
primitive and a key-management *architecture*.

- 🔴 **P0** Password-derived key used directly; no envelope, no version marker (TD-05, TD-06) — see §4.4.
- 🟠 **P0** No password policy, no auto-lock, no re-authentication (TD-11). `SetupViewModel` rejects only whitespace-empty passwords — a one-character master password is accepted. `VaultSession.Lock()` is called from exactly one place: the `catch` of a failed unlock. No idle timer, no lock-on-suspend, no lock button. The 32-byte key sits in a managed `byte[]` in a singleton for the entire process lifetime. Argon2id at 64 MB is meaningless against a one-character password.
- 🟠 **P1** ML model plaintext, cross-vault, outside the vault (TD-16) — see §4.9.
- 🟡 **P1** Unredacted diagnostic files (TD-25). `Program.Main` writes the full exception + stack to `%LOCALAPPDATA%/Pine/crash.log`; `PaddleOcrEngine` writes `ocr_init_error.log` next to the exe; `PaddleOcrEngine.cs:107` does `Console.WriteLine($"[OCR] Extracted: '{region.Text}'…")` — the full recognised text of every receipt. `docs/30-Observability.md` mandates a redaction posture no code implements.
- 🟡 **P2** Raw `ex.Message` surfaced to the user (TD-24) in `SetupViewModel`, `ScanReceiptViewModel`, `AddTransactionViewModel` and others. `UnlockViewModel` is the sole exception and does it correctly — apply that pattern everywhere.
- 🔵 **P2** No file-type or size validation on imports (TD-50). Picked files go straight to `CsvImporter`/`ExcelImporter`/`PdfImporter`/OCR. No magic-byte check, no size cap, no row/page limit. PdfPig, ExcelDataReader and OpenCV all have real CVE histories.

### 6.8 Testing — 1.5

**28 tests pass** (23 unit + 5 integration); 741 lines of test code against ~11.7k
lines of source. The tests that exist are decent — xUnit, FluentAssertions,
NSubstitute, a real SQLite `DatabaseFixture`, behaviour-named methods. Not a padded
suite, just a very small one.

Against `docs/18-Test-Strategy.md` (ISTQB levels with FsCheck, Stryker.NET,
Testcontainers, Appium, SpecFlow, k6, OWASP ZAP, axe): **none** of those tools
appear in any `.csproj`.

- 🔴 **P0** No financial invariant covered (TD-09) — see §4.6.
- 🔴 **P0** CI never builds the app; three empty suites "run" (TD-07) — see §4.5.
- 🟡 **P2** The architecture tests cannot currently fail. They assert on `Pine.Core` and `Pine.Application`, neither of which references the named assemblies — satisfied by the project file, not the code. They also would not catch this codebase's actual layering violation (UI → `PineDbContext`), because that direction is not asserted. → Add a `Pine.Ui.Avalonia` must-not-depend-on `Pine.Infrastructure` rule. **It would fail today, which is the point.**

### 6.9 Reliability & error handling — 2.0

The pattern is catch-at-the-view-model, set a string, continue. Reasonable as a UI
convention, but it is the *only* convention: no exception hierarchy (no
`PineDomainException`, no `VaultLockedException`), so callers cannot distinguish
transient failure from programming error from domain rule violation.

| Dependency | If it fails today | Should be |
|---|---|---|
| SQLCipher / disk | Message in UI; partial state left behind; no retry | Rollback; typed error; retry on `SQLITE_BUSY` |
| PaddleOCR init | Logged next to the exe, engine `null`, pipeline silently returns nothing | Explicit "OCR unavailable" state with a reason |
| YOLO detector | Model missing → `_session` null → detection skipped, no signal | Degrade to manual crop with a visible notice |
| ML.NET model | Missing file → no categorisation, no message | Acceptable to degrade, but say so |
| Camera / DirectShow | Background `Task.Run` loop; unobserved faults | Surface device errors; file-picker fallback |
| Migration on unlock | Reported as "Incorrect password or corrupted vault" | Separate phases; offer backup restore |

- 🟠 **P1** A failed migration is reported as a wrong password (TD-23). `UnlockViewModel.UnlockVaultAsync` wraps key derivation, `CanConnect`, the probe query **and** `Migrate` in one `Task.Run` under one `catch (Exception)`. Collapsing the error is right for authentication and wrong for everything after it — a user with a failing migration will retype a correct password forever.
- 🟡 **P2** No retry, timeout or idempotency anywhere. No Polly, no `SQLITE_BUSY` handling, no OCR inference deadline, no idempotency key on any command.

### 6.10 Performance — 3.0

No profiling was run; graded by confidence.

**Probable, with a clear technical basis**
- Import is O(n) memory and O(n·m) time. `ImportTransactionsCommandHandler` loads every existing transaction for the account (`GetByAccountIdAsync`, with `Include(t => t.Splits)`, tracked) and runs a LINQ `Any` per imported row. 5,000 rows against 50,000 existing = 250M comparisons over a fully-materialised tracked graph.
- Every paged query scans and sorts — no index on `Date` or `AccountId`.
- Leaked `DbContext` instances accumulate in the root provider for the process lifetime.
- Dashboard widgets each issue their own queries and **all** reload on every `DataChangedMessage` — one saved transaction fans out to a dozen round trips against a serialised SQLite connection.

**Theoretical / watch**
- `IAIProvider` is a singleton whose constructor eagerly builds the YOLO session, the Paddle engine, the unused ONNX session and the ML.NET context. First resolution stalls; if on the UI thread, it stalls the UI.
- The live scanner runs OpenCV per frame with no frame-rate governor.

**Well handled**
- Argon2 (deliberately slow) correctly off the UI thread in both setup and unlock.
- The `WriteableBitmap.Lock()` deep-copy fix for GC races — a real, non-obvious problem correctly solved.
- Pagination exists on the transactions query rather than loading everything. Right instinct, missing its index.

### 6.11 Maintainability — 3.0

Better than the rest of the audit suggests. Mean file ~76 lines; largest source
file is the 773-line extractor, which is cohesive. Naming is consistent and
domain-accurate, folder structure mirrors the architecture, no god classes. Zero
`TODO`/`FIXME`/`HACK` markers — which reflects *unmarked* debt, not absent debt.

**"This will hurt later"**
- **Stream-of-consciousness comments shipped as code** (TD-51). `TransferFundsCommandHandler`: *"Wait, source needs to link to destination as well."* `AddTransactionCommandHandler`: *"If the transaction is an expense, should the line item totals be negative?"* — an **open design question, unresolved, in a money path.** `Pine.slnx`: `<!-- asdasda`.
- **Three `UnitTest1.cs` stub projects** built and run by CI, creating false green signal (TD-38).
- **Four empty projects** backed by 646 lines of plugin documentation. Note `Pine.Ai.Default` is empty while the actual default AI provider lives in `Pine.Infrastructure` — the intended modularisation was documented, then not done.
- **Three dead entities** (`Attachment`, `Rule`, `Tag`) carried in the schema and migrations, referenced by no application or UI code (TD-37). The rule engine has a 312-line spec and one unused entity.
- **ONNX binaries tracked in git** without LFS (TD-43): `PP-OCRv4-det`, `PP-OCRv4-rec`, `yolo-receipt-nano`. Every clone pays forever.
- **Mixed-language comments.** `STATUS.md` and several code comments switch between English and Spanish mid-paragraph. Fine solo, friction for a team; `docs/20-Coding-Standards.md` should pick one.

### 6.12 Documentation — 2.0

60 documents, 17,292 lines — **1.5 lines of specification per line of source.** The
writing is careful and the structure consistent. It is also the single largest
source of misleading signal in the project.

| Document | Claim | Reality | Verdict |
|---|---|---|---|
| `15-Security` | OD-03 "closed": wrapped data key in keychain, rotation, versioned format | Password-derived key straight to SQLCipher; no rotation, no versioning | **Contradictory** |
| `18-Test-Strategy` | ISTQB levels; FsCheck, Stryker, Testcontainers, Appium, SpecFlow, k6, ZAP, axe | 28 tests, xUnit + NSubstitute; three stub projects | **Contradictory** |
| `30-Observability` | Structured JSON logs, trace/span ids, redaction, signed DIB | `Console.WriteLine`; `AddConsole()` in a WinExe; DIB export is a stub | **Contradictory** |
| `12-Plugin-Architecture` / `33-Plugin-SDK` | 646 lines of host, trust model, SDK contract | Both projects contain zero source files | Aspirational |
| `14-Synchronization` | Multi-device sync with conflict resolution | No sync code; a `System.StorageMode = CloudSync` setting nothing reads | Aspirational |
| `31-Rule-Engine` | 312-line auto-categorisation engine | `Rule` entity + table; no engine | Aspirational |
| `25-ADR` + `docs/adr/` | Code cites ADR-0013 (KDF); docs cite ADR-0012 (telemetry) | Only ADR-0001 exists; `INDEX.md` says "auto-generated by the CI pipeline" — CI has no such step | Broken references |
| `STATUS.md` | Diagnostics has "real-time log tailing, environment metrics, DIB export" | Hardcoded strings under `// Mock Logs` | Inaccurate |
| `STATUS.md` | Phase 1 and Phase 2 "✅ 100% Completed" | Phase 1's transaction management has no edit or delete | Overstated |
| `59-Current-Status` | UI pass + `EnsureCreated`→`Migrate` root cause | Matches the code precisely | **Accurate** |
| `58-TechDebt-WeakReference…` | Messenger lifecycle debt | Real, correctly diagnosed, honestly recorded | **Accurate** |

**Process finding, not a writing-quality finding:** the two accurate documents were
written *after* the code. The inaccurate ones were written before and never
revisited. There is no step in the workflow that reconciles a specification with
its implementation. Marking every unimplemented document
`Status: Aspirational — not implemented` in its header would recover most of the
value in an afternoon. (TD-39, TD-40)

### 6.13 Dependencies — 2.5

Choices are sound and current. Argon2/Konscious, SQLCipher/SQLitePCLRaw,
CommunityToolkit.Mvvm, MediatR, CsvHelper, PdfPig, ExcelDataReader — each the
conventional answer to a real need. **Nothing here needs replacing.**

- 🟡 **P2** Four overlapping PaddleOCR model packages referenced simultaneously (TD-41): `Models.Local` 3.3.1, `.LocalV3` 2.7.0.1, `.LocalV4` 2.7.0.1, `.LocalV5` 3.3.1 — plus three `.onnx` files committed separately. Each embeds full det+rec weights. Tens to hundreds of MB duplicated, and the effective model version is ambiguous to a reader. Keep one.
- 🟡 **P2** Windows-only native runtimes on a cross-platform product (TD-44). `OpenCvSharp4.runtime.win` and `Sdcb.PaddleInference.runtime.win64.mkl` are the only native runtimes; the camera scanner is DirectShow/COM. Receipt scanning cannot function on macOS, Linux, Android, iOS or WASM as configured — and OpenCV/PaddleInference have no WASM story at all, so the Browser head could never run this pipeline even once it compiles.

> **Not a finding.** MediatR at 12 handlers is on the edge of earning its keep. I'm
> not calling it overengineering: it's applied consistently, keeps VMs thin, and is
> the natural insertion point for the validation and unit-of-work behaviours this
> codebase needs next. Keep it and use it for that.

### 6.14 Cloud / external services — 3.5

No cloud surface at all. For a local-first product this is correct and correctly
implemented — nothing to isolate behind an abstraction because nothing is there.

Two loose threads:
- `SetupViewModel` persists `System.StorageMode = "CloudSync"` when the user picks cloud storage on the first-run screen. **No code reads it and no sync exists** — onboarding offers a capability the product does not have (TD-48).
- `System.Telemetry` opt-in is stored and unused. Harmless today, but the flag must be honoured at the *collection site* before any telemetry ships.

When sync arrives it will collide with the persistence gaps above: no concurrency
tokens, no audit columns, no logical clock, stored rather than derived balances, no
stable natural key. Fix those first; adding sync on the current schema would not go
well.

---

## 7. Over- vs under-engineering

### 7.1 Overengineering — less than the shape suggests

| Construct | Verdict | Reasoning |
|---|---|---|
| Hexagonal layering (4 assemblies) | Justified | Real engine substitution; domain testable without EF or Avalonia |
| `IAIProvider` / `IImageProcessor` | Justified | Genuine variation point; no ONNX/OpenCV leakage |
| MediatR CQRS | Borderline, keep | Thin at 12 handlers, but the right home for validation + UoW behaviours |
| `ValueObject` base | Justified | Two implementers, correct semantics, standard pattern |
| `AggregateRoot` + `IDomainEvent` | **Unearned** | Events raised in two aggregates, dispatched nowhere. Either dispatch them (a natural home for the balance update) or remove them |
| Six platform heads | **Premature** | Three don't compile; the AI stack is Windows-only |
| Five test projects | **Premature** | Three are empty templates |
| Four empty AI/plugin projects | **Premature** | Structure built to match a documented plan, not a built feature |
| Per-aggregate repository interfaces | Justified | Hand-written, aggregate-shaped, not a generic `IRepository<T>`. Correct restraint |

The distinctive pattern is not excess abstraction — it is **premature structure**.
Each empty project, stub suite and non-compiling head is a place where the solution
asserts a capability that is not there.

### 7.2 Underengineering — where the real deficit is

- **No transactional boundary.** No UoW, no `BeginTransaction`, no scope.
- **No validation layer.** No FluentValidation, no MediatR validation behaviour. Currency compatibility, positive amounts and account existence are each checked in *some* handlers and not others.
- **No mutation path.** No update, delete, reversal or balance recomputation.
- **No configuration.** Every threshold, path and cost is a compile-time constant.
- **No logging.** One `ILogger` in the entire solution.
- **No exception taxonomy.** `ArgumentException`/`InvalidOperationException` for domain rules; callers cannot discriminate.
- **No audit trail.** No history, no soft delete, no who/when on financial records.
- **No concurrency control.** No row version anywhere.
- **Direct infrastructure access from the UI.** `PineDbContext` resolved and migrated from two view models.
- **Unsafe assumptions in money handling.** Sign-inferred transaction type, unvalidated currency, unreconciled line items, unrounded decimals.

**Through-line: the domain layer is modelled with care and the write path around it
is not defended at all.** Rich aggregates protect invariants within a single
object, then the handler layer writes those objects to two databases in two commits
with no validation stage and no rollback. Closing that gap — one unit of work, one
validation behaviour, one update/delete path — moves more of this scorecard than
any other single change.

---

## 8. Technical debt register

> **Status legend.** ✅ **Done** — implemented end-to-end, wired into DI, reachable from
> the real code path and covered by passing tests. 🟡 **Partial** — some of the debt is
> genuinely closed and the rest is named explicitly in §12.3; never used to mean
> "mostly done". 🔴 **Open** — not started. A ticket is never marked ✅ on the strength
> of code existing; it is marked ✅ only when it has been exercised.
> Rows **TD-52 … TD-67** were discovered *during* remediation and were not in the
> original audit.

| ID | Debt | Sev | Impact | Action | Pri | Status |
|---|---|---|---|---|---|:---:|
| TD-01 | No unit of work; transient DbContext per repository | 🔴 | Non-atomic financial writes | Scope-per-request + `IUnitOfWork` | P0 | ✅ **Done** |
| TD-02 | No update/delete/void for transactions | 🔴 | Errors permanent; balances unrepairable | Void + balance recalculation (done); edit deferred as TD-68 | P0 | 🟡 **Partial** — a transaction can now be **voided** with a recorded reason and its balance effect undone atomically (transfers as a unit), and any account's balance can be **recomputed from its ledger** and repaired. The **edit** path is deliberately not built — a ledger voids rather than edits, and an update command would make TD-59/TD-60 load-bearing. See §12.14 |
| TD-03 | Dedup on (date, amount) only, silent | 🔴 | Legitimate transactions lost | Stable import key + review list | P0 | ✅ **Done** |
| TD-04 | `AddSplit` validates eagerly | 🔴 | Multi-split throws | Validate at aggregate boundary | P0 | ✅ **Done** |
| TD-05 | No KDF version marker beside the salt | 🔴 | Parameter change bricks all vaults | Versioned vault header | P0 | ✅ **Done** |
| TD-06 | Key = password-derived key; no envelope | 🔴 | No password change, no biometric | Wrapped data key | P0 | 🔴 **Open** |
| TD-07 | CI workflow is **manual-only** (`workflow_dispatch:`). The `push`/`pull_request` triggers iteration 1 wrote were deleted in `79c5364`; iteration 6 took the second of the two options this row offered — **CI is opt-in by an explicit project decision** (compute-minute cost), not by accident | 🟠 | The regression net exists and is real, but nothing arms it: a broken head reaches `main` unless a human dispatches the workflow. Severity dropped 🔴→🟠 because the gap is now a known, chosen operating mode rather than an unnoticed regression — the *risk* is unchanged | Dispatch the workflow by hand before any release; re-arm `pull_request:` when compute budget allows | P1 | 🟡 **Accepted** — documented decision, iteration 6; see §12.12 and §12.14 |
| TD-08 | Web/Android/iOS reference a non-existent namespace | 🔴 | 3 of 6 heads uncompilable | Fix or remove; add to CI | P0 | 🟡 **Partial** |
| TD-09 | Money-path tests cover atomicity, split-sum, owned-collection round trip, currency match, import dedup, void + balance recalculation, and — since iterations 8 and 9 — `Money` scale/rounding/`Allocate` (TD-18) and line-item reconciliation (TD-19) | 🟠 | The regression net now covers every closed P0 and both remaining money-path invariants; eleven falsification mutations across the two iterations, all killed | — | P1 | ✅ **Closed** — iteration 9, §12.20 |
| TD-10 | No backup before migration | 🟠 | Unrecoverable data loss | Pre-migration copy + export | P0 | 🟡 **Partial** |
| TD-11 | No password policy; no auto-lock | 🟠 | Key exposed on idle machine | Entropy floor + idle lock | P0 | 🟡 **Partial** — a 60-bit entropy floor derived from the shipped Argon2id parameters now gates vault creation, and a `TimeProvider`-driven idle watchdog locks the session and returns the user to the vault picker. **Verified out-of-suite**: a 25-check harness (not committed) watched the floor reject a one-character password, the idle lock fire on a fake clock, and the key zero; no test in `tests/` covers it, so nothing guards against regression. Re-authentication, lock-on-suspend and a persisted timeout remain unbuilt. See §12.16 |
| TD-12 | Currency not validated against account | 🟠 | Orphaned ledger entry — **measured: no longer reachable once TD-01 closed; the residual defect was a domain exception leaking to the UI** | Validation behaviour | P0 | ✅ **Done** |
| TD-13 | `CurrentValues.SetValues` drops owned collections | 🔴 | Latent silent data loss on edit | Tracked-graph updates | P0 | ✅ **Done** |
| TD-14 | No `ILogger`; `AddConsole` in a WinExe — in **two** heads (Windows and macOS), not one | 🟠 | No incident evidence | Structured file logging + redaction | P1 | 🔴 **Open** |
| TD-15 | Diagnostics screen shows mock data | 🟠 | Misleads during incidents | Wire to real sinks or remove | P1 | 🔴 **Open** |
| TD-16 | ML model plaintext, cross-vault, outside vault | 🟠 | Privacy-claim violation | Per-vault encrypted model | P1 | 🔴 **Open** |
| TD-17 | No optimistic concurrency token | 🟠 | Lost updates | Row version on aggregates | P1 | 🔴 **Open** |
| TD-18 | No `Money` scale or rounding policy | 🟠 | Sub-cent drift; undefined split remainders | Normalise + `Allocate` | P1 | ✅ **Closed** — iteration 8, §12.18. `Money` normalises in its constructor to the currency's ISO 4217 minor units (`ToEven`), rejects a fractional amount in a zero-minor-unit currency, and gains `Allocate`/`Multiply`/`Negate`/`Round`/`Scale`. `CurrencyCode` validates membership and offers `TryCreate`. Six falsification mutations, all killed |
| TD-19 | Line items unreconciled against total | 🟠 | Internally contradictory records | `LineItemKind` represents the difference instead of tolerating it; the invariant is enforced at the persistence boundary and the receipt confirmation UI offers a one-click adjustment | P1 | ✅ **Closed** — iteration 9, §12.20 |
| TD-20 | No receipt duplicate detection | 🟠 | Double-counted expenses | Content-hash check | P1 | 🔴 **Open** |
| TD-21 | Ambiguous dd/MM vs MM/dd parsing — and the OCR "multi-culture cascade" tries `InvariantCulture` (MM/dd) **first**, so its `es-EC` leg is unreachable for any ambiguous date while its comment claims dd/MM is tried first | 🟠 | Silently misdated transactions, now behind code that reads as if it handled them | Locale-driven order; flag ambiguity | P1 | 🔴 **Open** — severity understated, see §12.12 |
| TD-22 | Confidence score is an arbitrary tally | 🟠 | Unearned user trust in OCR | Propagate real confidence or relabel | P1 | 🔴 **Open** |
| TD-23 | Migration failure reported as wrong password | 🟠 | Undiagnosable support cases | Split auth and schema phases | P1 | ✅ **Done** |
| TD-24 | Raw `ex.Message` surfaced in UI | 🟡 | Path/internals leakage | Map to user-facing messages | P2 | 🔴 **Open** |
| TD-25 | Unredacted crash.log / OCR text to console | 🟡 | Sensitive data outside vault | Redact and relocate | P1 | 🔴 **Open** |
| TD-26 | Reflection sets `LinkedTransactionId` | 🟠 | Silent runtime break on rename | Domain method or single aggregate | P1 | 🔴 **Open** |
| TD-27 | No configuration system | 🟠 | Nothing tunable; no env separation | `IOptions` + appsettings | P1 | 🔴 **Open** |
| TD-28 | Service locator in view models | 🟠 | Hidden deps; UI touches EF | Typed VM factories | P1 | 🔴 **Open** |
| TD-29 | Five duplicated composition roots | 🟠 | Divergent lifetimes per platform | `AddPine*()` extensions | P1 | 🔴 **Open** |
| TD-30 | No index on `Date`; no FK transaction→account. `AccountId` gained a leading-column index in iteration 5's `(AccountId, ImportKey)`; a Budget→Category FK **already exists** | 🟠 | Scans at scale; orphan risk | Add the `Date` index + the transaction FK | P2 | 🔴 **Open** — scope narrowed, see §12.12 |
| TD-31 | Decimal stored as TEXT in SQLite | 🟠 | Wrong results when comparison reaches SQL | Scaled-integer converter | P1 | 🔴 **Open** |
| TD-32 | `TransactionSplit.Category` is a free string, not a FK (`Budget` already uses a real `CategoryId` FK) | 🟡 | Rename orphans history | Store `CategoryId` on the split | P2 | 🔴 **Open** — scope narrowed, see §12.12 |
| TD-33 | Domain events raised, never dispatched | 🟡 | Misleading scaffolding | Dispatch or remove | P2 | 🔴 **Open** |
| TD-34 | Messenger subscriptions never unregistered | 🟡 | Stale VMs act on messages | `IDisposable` view models | P2 | 🔴 **Open** |
| TD-35 | Fire-and-forget async in ctor / messenger | 🟡 | Unobserved exceptions; init races | Awaited `InitializeAsync` | P1 | 🔴 **Open** |
| TD-36 | Dead `InferenceSession`; missing model file | 🟡 | Startup cost; misleads readers | Delete | P2 | 🔴 **Open** |
| TD-37 | Dead entities: Attachment, Rule, Tag | 🟡 | Schema noise; implies features | Implement or drop | P2 | 🔴 **Open** |
| TD-38 | Four empty projects; three stub test projects | 🟡 | False signal in solution and CI | Remove until needed | P2 | 🔴 **Open** |
| TD-39 | Docs contradict implementation (§6.12) | 🟠 | Misleads every new contributor | Mark aspirational; reconcile | P1 | 🔴 **Open** |
| TD-40 | Code cites ADR-0013/0012; only ADR-0001 exists | 🟡 | Dangling decision references | Write them or drop the citations | P2 | 🔴 **Open** |
| TD-41 | Four PaddleOCR model packages referenced | 🟡 | Large duplicated payload | Keep one | P2 | 🔴 **Open** |
| TD-42 | No central package management; version drift | 🟡 | Silent unification; EF mismatch | `Directory.Packages.props` | P2 | 🔴 **Open** |
| TD-43 | ONNX binaries are LFS-tracked since `bec0887` (all three blobs are ~133-byte pointers at HEAD); the residual is history predating the conversion, which LFS does not rewrite | 🔵 | Residual clone cost only | Rewrite history, or accept it | P3 | 🟡 **Partial** — was 🔴, see §12.12 |
| TD-44 | Windows-only native runtimes | 🟡 | Scanning impossible off Windows | Per-RID runtimes; state support | P2 | 🔴 **Open** |
| TD-45 | Hardcoded English UI; no localisation | 🟡 | Costly retrofit across 26 views | `.resx` + `IStringLocalizer` | P2 | 🔴 **Open** |
| TD-46 | Relative model paths in `MlNetCategorizer` and `OnnxNlpFieldExtractor`; `ReceiptDetector` already anchors to `AppContext.BaseDirectory` | 🟡 | Feature silently off; stray files | Anchor the remaining two | P2 | 🔴 **Open** — scope narrowed, see §12.12 |
| TD-47 | New vaults seeded with a fictional $1,000 account | 🔵 | In a tool whose value is an accurate picture of the user's money, seeding invented data is a correctness problem | Removed from `SetupViewModel`; a fresh vault now has zero accounts, and the dashboard's always-visible `+ New Account` button is the path in | P1 | ✅ **Closed** — iteration 10, §12.22 |
| TD-48 | `CloudSync` setting read by nothing | 🔵 | Onboarding promises an absent feature | The disabled radio, the `IsCloudSync` property and the `System.StorageMode` write are all gone; the step keeps the local option, whose privacy claim is true | P2 | ✅ **Closed** — iteration 10, §12.22 |
| TD-49 | No packaging, signing or update channel | 🟠 | No way to ship or patch | Installer + signing + updater | P1 | 🔴 **Open** |
| TD-50 | No file-type or size validation on imports | 🔵 | Parser DoS / hang | Magic-byte + size cap | P2 | 🔴 **Open** |
| TD-51 | Draft comments and `<!-- asdasda` in source | 🔵 | Signals unreviewed code | Clean up; enforce in review | P2 | 🟡 **Partial** |
| TD-52 | Web/Android/iOS roots omit `TimeProvider.System` + 9 other registrations Windows has | 🔴 | Heads compile, then throw on the first command dispatched | Shared `AddPine*()` roots (TD-29) | P1 | 🔴 **Open** |
| TD-53 | `Pine.Ui.Web` now passes `":memory:"`, but `PineDbContext` unconditionally builds a **keyed SQLCipher** connection, whose native lib WASM does not link | 🔴 | Web head still fails at first DB access — an in-memory *SQLCipher* database needs the same native library | Decide the Browser story or wire a real provider | P1 | 🔴 **Open** — wording corrected, see §12.12 |
| TD-54 | CI enumerates projects instead of building `Pine.slnx` | 🟠 | A newly added project is silently outside CI — the TD-07 drift class again | Build the solution, or assert slnx coverage in CI | P1 | 🔴 **Open** |
| TD-55 | `TreatWarningsAsErrors` promotes NuGet audit warnings (NU19xx) to build errors | 🟡 | A new CVE advisory turns CI red with no code change | Set `NuGetAuditMode`; triage advisories separately | P2 | 🔴 **Open** |
| TD-56 | No restore-from-backup path; `ExportVaultBackupCommand` has no UI entry point | 🟠 | Recovery requires the user to copy files by hand | Restore command + UI affordance | P1 | 🔴 **Open** |
| TD-57 | `IVaultMigrator` lives in `Pine.Core` because Infrastructure cannot see Application | 🟡 | A persistence-orchestration port sits in the domain project | Add the ProjectReference, then move the port | P2 | 🔴 **Open** |
| TD-58 | `AddTransactionViewModel` / `CreateAccountViewModel` take an unregistered `Action onComplete` ctor parameter | 🟡 | Blocks `ValidateOnBuild`; the container cannot self-check at startup | Typed VM factories (TD-28) or a registered callback | P2 | 🔴 **Open** |
| TD-59 | `ReconcileOwnedChildren` infers owned-child state from EF's Modified/Added guess | 🟡 | A client-keyed owned child is promoted Modified→Added; if either owned type ever gains a public setter, a real update is silently rewritten as an insert | Set owned-child state explicitly, or key them server-side | P2 | 🟡 **Partial** — assumption pinned by `OwnedTransactionChildren_ShouldNot_ExposePublicSetters` |
| TD-61 | `CurrencyCodeText.TryNormalise` restates `CurrencyCode`'s shape rule outside the domain | 🟡 | Two copies of one rule can drift — most concretely when `CurrencyCode` gains ISO 4217 *membership* validation (TD-18), after which the validator would still wave `"ZZZ"` through | Expose a non-throwing `CurrencyCode.TryCreate` and delete the copy | P2 | ✅ **Closed** — iteration 8, §12.18. The helper now delegates to `CurrencyCode.TryCreate`; the copy is deleted. Pinned by a theory asserting `TryCreate` and the constructor agree on every input, and by falsification M6 |
| TD-62 | Validators cover currency only; other pre-conditions still fail inside the domain | 🟡 | `AddTransactionCommand` with `Amount == 0` still reaches `account.Deposit(...)` and surfaces "Deposit amount must be strictly positive."; `ImportTransactionsCommandHandler` still throws a bare `ArgumentException` for a missing account. Same wrong-layer class TD-12 fixed, different fields | Extend the existing validators; the seam is already in place | P2 | 🔴 **Open** |
| TD-63 | `CreateBudgetCommand` carries a currency anchored to nothing | 🟡 | A budget's `CurrencyCode` is validated for shape only — it has no account to be checked against, so a EUR budget can sit against a USD-only vault and no total is comparable | Decide whether a budget is per-account or per-vault, then anchor it | P2 | 🔴 **Open** |
| TD-60 | `ReplaceSplits` / `ReplaceLineItems` mint fresh `Guid` ids on every detached save | 🔵 | Owned children are deleted and re-inserted with new ids on each update; any future reference to a split id (rules, attachments, audit trail) would dangle | Diff by identity instead of replacing wholesale | P2 | 🔴 **Open** |
| TD-64 | The import currency guard inspects only the rows it is about to save | 🟡 | A skipped duplicate in the wrong currency is never reported, so a bad file looks clean on re-import while its first import was rejected | Validate parsed rows before the dedup split, or report currency per review row | P2 | 🔴 **Open** |
| TD-65 | Import de-duplication has a check-then-insert race; the `(AccountId, ImportKey)` index is deliberately non-unique | 🟡 | Two concurrent imports of one file can both pass the existence probe before either inserts, and both write | Unique index + conflict handling, or serialise imports per account | P2 | 🔴 **Open** |
| TD-66 | The derived import key's `source` field is `ImportProfile.Name`, the only source identifier reachable inside `ImportAsync` | 🟡 | Renaming or duplicating a profile changes every derived key, so the next import of an already-imported file reports every row as new | Key the source to something stable (profile id, or the file's own identity) | P2 | 🔴 **Open** |
| TD-67 | `ExcelImporter` and `PdfImporter` parse nothing — both open the file, loop, and return an empty list | 🟡 | Two of three advertised import formats are stubs; neither is reachable from the wizard, which hardcodes `CsvImporter` | Implement or delete; they are documented as stubs as of iteration 5 | P2 | 🔴 **Open** |
| TD-68 | No update/edit path for a transaction; correction is void-then-re-enter | 🟡 | Two rows where the user made one mistake, and the re-entered row carries no link back to the one it replaces | Update command — but close TD-59 and TD-60 first, which it makes load-bearing | P2 | 🔴 **Open** — deliberate scope decision, iteration 6 |
| TD-69 | A void cannot be undone | 🟡 | A mis-voided transaction can only be re-entered as a new one, losing its id and its import key | Unvoid command, or fold into TD-68 | P2 | 🔴 **Open** |
| TD-70 | Accounts created before `20260828005910` have no recorded opening balance, so their FIRST recalculation anchors instead of checking | 🟡 | Pre-existing drift on those accounts is undetectable and unrecoverable — reported honestly (`OpeningBalanceWasInferred`) but not fixable | An explicit "set opening balance" gesture, so the user supplies the figure instead of the system inferring it | P2 | 🔴 **Open** |
| TD-71 | The transactions list cannot filter or hide voided rows | 🔵 | Correct at today's volumes; a vault with many corrections reads badly | A filter on the list, once there is a read model to filter (TD-28's DTOs) | P3 | 🔴 **Open** |
| TD-72 | `ShellViewModel` lifetime diverges: `Transient` on Windows, `Singleton` on the other four heads | 🟡 | One view model means two different things depending on platform; a singleton shell outlives the vault it was opened for | Pick one lifetime, in the shared registration TD-29 already calls for | P2 | 🔴 **Open** — iteration 7 |
| TD-73 | `PasswordStrengthPolicy` has no natural-language word list, so multi-word passphrases are over-estimated | 🟡 | The estimator accepts a passphrase weaker than the bits it reports; the 60-bit floor's `2^6` margin is what covers this | Embed a word list in `Pine.Core` — needs the BCL-only rule relaxed to permit an embedded resource | P2 | 🔴 **Open** — iteration 7 |
| TD-74 | ~~One integration test is red at `19a1877`~~ **The whole integration project stopped compiling at `f691ba8` and nobody noticed for two iterations** | 🟠 | The reported symptom was wrong. `Pine.Tests.Integration` was not 98/99; it was **0 tests executed**, 8 × CS0535. A suite that does not build reports no colour at all | Fixed in iteration 9 — the two hand-written `IVaultSession` fakes now track the interface; see §12.20 | P1 | ✅ **Closed — reclassified**, iteration 9 |
| TD-75 | Every money figure in the UI is formatted `{0:N2}` — `BudgetsView.axaml:57,59`, `RecentTransactionsWidgetView.axaml:30`, `BalanceWidgetView.axaml:29` | 🟡 | Two decimals is now provably wrong for a JPY account (0 minor units) and a KWD one (3). The domain knows the right number since TD-18; the view hardcodes the wrong one. There are no Avalonia converters in the project, so there is nowhere to put the rule today | A value converter reading `Money.Scale`, or a formatted string on the view model | P2 | 🔴 **Open** — opened by iteration 8 |
| TD-76 | The account-creation currency picker is a seven-item hardcoded list (`CreateAccountViewModel.Currencies`) | 🔵 | `"BTC"` was in it and could not have succeeded once TD-18 landed; it was removed. The remaining list is arbitrary and excludes ~170 real currencies a user may hold | A searchable picker over `IsoCurrencies.Codes` | P3 | 🔴 **Open** — opened by iteration 8 |
| TD-77 | `Account.OpeningBalanceAmount` is a bare `decimal?` column with no `Money` around it (`PineDbContext:77`) | 🟡 | TD-18's scale policy lives in `Money`'s constructor, so a column that does not pass through `Money` on the way out is outside it. Writes are covered transitively (`AdoptOpeningBalance` takes a `Money`); a raw SQL update or a legacy row is not, and `Account.OpeningBalance` now rounds on projection rather than throwing | Store it as a `Money` complex property, or keep the projection and document it (done) | P2 | 🔴 **Open** — opened by iteration 8 |
| TD-78 | `CreateAccountCommand` and `CreateBudgetCommand` still have no validator, so an unknown currency now throws `ArgumentException` from inside the handler | 🟡 | TD-18 made `CurrencyCode` reject `"ZZZ"`, which turned two previously-silent paths into wrong-layer throws rendered verbatim by `catch (Exception)` view models. Same class as TD-12, and a direct widening of TD-24's blast radius. The validator seam already exists; nothing was added because the rows are TD-62/TD-63 | An `IRequestValidator<T>` for each, using `CurrencyCodeText.TryNormalise` | P2 | 🔴 **Open** — opened by iteration 8 |
| TD-79 | `PineDbContext` declares no `HasPrecision()` and no `HasColumnType()` anywhere; every `decimal` is stored as SQLite `TEXT` | 🟡 | The store has no scale of its own, so TD-18's policy is enforced only by the domain. Anything writing that file outside Pine — a migration, a repair script, another tool — can plant an out-of-scale amount, and the read path is deliberately tolerant of exactly that | Related to TD-31; at minimum a `CHECK` constraint or an explicit column type | P2 | 🔴 **Open** — opened by iteration 8 |
| TD-80 | Onboarding asked consent for telemetry that does not exist | 🟠 | Worse than TD-48: the cloud radio was disabled, this checkbox was **enabled** and asked for real consent. No telemetry code exists anywhere in the repository and nothing read `System.Telemetry`. In a product whose argument is local privacy, this is the worst place to be inaccurate | Checkbox, `TelemetryOptIn` and the setting write removed | P1 | ✅ **Opened and closed** — iteration 10, §12.22 |
| TD-81 | `SetupViewModel`'s happy path cannot be tested | 🟠 | `VaultManager` is `sealed`, has no interface, hard-codes `LocalApplicationData/Pine` in its constructor and writes `vaults.json` to the real disk, so `InitializeVaultAsync` cannot run under test without mutating the developer's machine. Every existing test of this view model stops at an early validation failure; iteration 10's tests had to assert type-level dependencies instead of behaviour | Extract `IVaultManager`, or inject the data directory | P2 | 🔴 **Open** — iteration 10 |

---

## 9. Production blockers

Each of the following should prevent a release to users who will store real
financial data. Strict but not manufactured — every one has an identified file and
a concrete failure mode.

1. ✅ **TD-01** — ~~No transactional boundary.~~ **Closed.** Scope-per-request DI, `IUnitOfWork`, and an opt-in `TransactionBehavior`; repositories no longer self-commit. Rollback proven by tests that flush real rows inside the transaction and assert the database comes back empty.
2. 🟡 **TD-02** — ~~No correction mechanism.~~ **Blocker closed; the row stays 🟡 for a narrower reason.** A transaction can be **voided** with a recorded reason, its effect on the balance undone in the same database transaction, and a transfer voided as a unit across both legs and both accounts. Any account's balance can be **recomputed from its own ledger** and repaired, exactly — the sum is done in .NET `decimal` because SQLite would coerce a pushed-down `SUM` to REAL (TD-31). Proven by four falsification mutations (§12.14). What is *not* built is the **edit** path: correcting a transaction means voiding it and re-entering it, which is the right default for a ledger and is tracked as TD-68. The blocker said "any bad record is permanent" — that is no longer true, and it is the sentence that made this a blocker.
3. ✅ **TD-03** — ~~Silent import data loss.~~ **Closed.** De-duplication keys on a stable per-row import key (`ImportKeyFactory`, `r1:`/`h1:` schemes) looked up through an indexed `(AccountId, ImportKey)` probe, never again on `(date, amount)`. Two same-day same-amount rows both survive. The command now returns every parsed row with its disposition and a reason, and the wizard shows them on a review step — so a row is never discarded without the user being told which and why. Proven from a fresh context and raw SQLCipher `SELECT`s, and by three falsification mutations (see §12.11).
4. ✅ **TD-04** — ~~`AddSplit` throws.~~ **Closed.** `AddSplit` no longer validates the sum eagerly, `ReplaceSplits`/`ReplaceLineItems` validate a whole set atomically, and the invariant is enforced at the persistence boundary by `Transaction.EnsureValid()` in `SqliteTransactionRepository.SaveAsync`. A two-way €50/€50 split on a €100 transaction now persists; the boundary check is proven by falsification (removing it turns the test red).
5. 🟡 **TD-05 ✅ / TD-06 🔴** — ~~Unversioned KDF~~ and no envelope key. The versioned header now records the KDF id and every parameter, so the irreversible half of this blocker is closed; envelope encryption (TD-06) is not built, so password change and biometric unlock remain impossible.
6. 🔴 **TD-07 / TD-08** — CI builds the product and the three heads no longer reference a non-existent namespace, but the workflow has never executed, **and as of `79c5364` it can no longer execute on its own**: the `push` and `pull_request` triggers were deleted, leaving `workflow_dispatch:` alone. Android/iOS were never compiled (workloads absent locally). Regression protection is written, disarmed, and still not demonstrated. Iterations 3 and 4 both restated "never executed" without noticing the triggers had gone — see §12.12.
7. 🟡 **TD-09** — ~~No financial-invariant tests. Every fix above would ship unverified.~~ **Untrue since iteration 2, and the gap has narrowed twice more since.** Atomicity, split-sum, owned-collection round trip, currency match, import dedup and now **void + balance recalculation** are all covered, several falsification-proven; the suite is 240 tests. The remaining gaps are rounding/allocation (TD-18) and line-item reconciliation (TD-19) — both P1, and both invariants the domain does not express yet, so they are work to *design* rather than tests to back-fill. See §12.12 and §12.14.
8. 🟡 **TD-10** — Encrypted pre-migration backup is implemented and verified, so a failed upgrade no longer destroys the only copy. **Recovery is still manual**: no restore path and no UI entry point for the export command.
9. 🟡 **TD-11** — ~~No password policy, no auto-lock. The encryption is defeatable by the setup screen.~~ **The setup screen no longer accepts a one-character password**: a 60-bit entropy floor, derived from the KDF Pine actually ships, gates vault creation, and an idle watchdog locks the session after ten minutes. **This blocker is not closed, because nothing in the suite guards it.** The behaviour has been executed and observed once, by hand, outside the repository; the code compiles on three of five heads and is unreachable on the other two for unrelated reasons (TD-08). It downgrades from 🔴 to 🟡 on structure alone, and returns to 🔴 if the tests in §12.17 are not written. See §12.16.
10. ✅ **TD-12 ✅ / TD-13 ✅** — ~~Owned collections dropped on update~~ and ~~currency never validated against the account~~. **Blocker closed.** TD-13: `Transaction.Splits` and `LineItems` round-trip through the tracked graph, proven from a fresh context and a raw `SELECT COUNT(*)` across grow/shrink/empty. TD-12: a validation pipeline behaviour rejects a currency mismatch before any write, proven by falsification. **The honest correction this blocker needs:** it claimed *two* corruption paths. Iteration 4 measured the second one and found it was no longer a corruption path — closing TD-01 had already contained it (a mismatch rolled back cleanly: zero rows, balance untouched, `Begin, Rollback`). What remained was a domain exception (`"Cannot add money of different currencies: USD and EUR"`) leaking verbatim to the user from the wrong layer. That is a real defect and it is now fixed, but it was not data corruption, and this blocker overstated it from iteration 2 onward. See §12.9.
11. 🔴 **TD-49** — No packaging, signing or update channel. No mechanism to deliver any of the above fixes to a user who already installed.

**Not blockers, but ship with eyes open:** TD-15 (mock diagnostics — remove the
screen rather than ship it), TD-16 (plaintext ML model — contradicts the marketing
claim), TD-21 / TD-22 (date ambiguity and unearned confidence — mitigated by the
human confirmation step, which is why they are not P0).

---

## 10. Remediation roadmap

Incremental. **No rewrite is warranted:** the layering is correct, the domain is
properly modelled, and the defects are concentrated in the write path and the
composition root — both replaceable without touching the domain or the UI.

### Phase 0 — Before any real user data exists

Ordered by **irreversibility**, not by size. The KDF header is first because it is
a day of work now and an impossible migration later.

- [x] ✅ **1.** Versioned vault header recording KDF id + parameters (TD-05) — **DONE.** `pine.vault.json`, legacy `pine.salt` adoption, known-answer regression vector proving existing vaults still open.
- [x] 🟡 **2.** Pre-migration database backup + encrypted export command (TD-10) — **BACKUP DONE, EXPORT UNREACHABLE.** `VACUUM INTO` on the keyed connection, encryption proven by test. `ExportVaultBackupCommand` exists but no UI invokes it; there is no restore path.
- [x] 🔴 **3.** CI: build the whole solution; restore `/tests/` to `Pine.slnx`; fix or delete the broken heads (TD-07, TD-08) — **CODE DONE, THEN DISARMED.** `Pine.slnx` clean, heads fixed, three-job workflow written — but `79c5364` removed the `push`/`pull_request` triggers, so the workflow now runs only when someone dispatches it by hand. Never run; Android/iOS never compiled. **This item regressed after it was ticked.**
- [x] ✅ **4.** Scope-per-request DI + `IUnitOfWork`; strip `SaveChangesAsync` from repositories; one DB transaction per command (TD-01 — also fixes the transient-disposable leak) — **DONE.** All 11 repository `SaveChangesAsync` calls removed, 4 MediatR-bypassing write paths converted, `ValidateScopes` on in all five roots.
- [x] ✅ **5.** Fix `AddSplit`; fix `CurrentValues.SetValues` to update tracked graphs (TD-04, TD-13) — **DONE.** Deferred split validation with an atomic `ReplaceSplits`/`ReplaceLineItems`, the invariant enforced at the persistence boundary, and owned collections reconciled on the tracked graph. Round-trip proven from a fresh context; 124 → 138 tests.
- [x] ✅ **6.** MediatR validation behaviour; validate currency against account (TD-12) — **DONE.** Hand-rolled `IRequestValidator<T>` seam + `ValidationBehavior` registered *before* `TransactionBehavior`, so a rejected command never opens a transaction. Four validators; `TransferFundsCommandHandler`'s up-front checks folded in so there is one idiom. Proven by four falsification mutations; 138 → 152 tests.
- [x] ✅ **7.** Rewrite import dedup on a stable key with a user-facing review list (TD-03) — **DONE.** `ImportKeyFactory` (BCL-only, invariant-culture, versioned `r1:`/`h1:` prefixes), a nullable `ImportKey` column with an `(AccountId, ImportKey)` index, an indexed existence probe replacing the full-account load, in-batch duplicate detection, and an `ImportTransactionsResult` carrying every row's disposition + reason, surfaced on a new wizard review step. 152 → 170 tests.
- [x] 🟡 **8.** Update / void transaction commands + balance recalculation (TD-02) — **VOID AND RECALCULATION DONE; EDIT DELIBERATELY DEFERRED.** `VoidTransactionCommand` (both transfer legs, both balances, one database transaction, reason required) and `RecalculateAccountBalanceCommand` (exact .NET `decimal` sum, voided rows excluded, drift reported). `Account` gained a recorded opening balance — without which recomputing a balance from its ledger is arithmetically impossible; the migration deliberately does not backfill it, see §12.14. Four falsification mutations; 170 → 240 tests. The **edit** path is TD-68.
- [x] ✅ **9.** Password entropy floor and idle auto-lock (TD-11) — **DONE.** `PasswordStrengthPolicy` (BCL-only shortest-path entropy estimator; the 60-bit floor is derived from `KdfParameters.Argon2idV1`, arithmetic in the constant's doc comment) gates `SetupViewModel`, which also gained a confirm-password field. `VaultAutoLockService` polls an injected `TimeProvider`, arms on resolution, and raises `IVaultSession.Locked`; `MainViewModel` marshals it to the UI thread, drops the shell's inner page and says why. Wired on all five heads. Behaviour was verified by an out-of-suite harness (25 checks, transcribed in §12.16). **[Updated, iteration 9: that harness is now in `tests/Pine.Tests.Unit/Security/` as 44 tests, and every value §12.16 transcribed reproduced exactly. The item is ✅ for everything a test can reach; the strength meter's rendering and the Android/iOS wiring remain manual rows in `61-Manual-Test-Plan` §9. See §12.20.]**
- [x] ✅ **10.** Money-path test suite covering all of the above — written *alongside*, not after (TD-09) — **DONE.** `Money` scale/rounding/`Allocate` and ISO-4217 membership (iteration 8, §12.18); line-item reconciliation via `LineItemKind` (iteration 9, §12.20). 240 → 429 tests, eleven falsification mutations across the two iterations, all killed.
- [x] ✅ **11.** Remove the seeded fictional account; remove or wire the `CloudSync` option (TD-47, TD-48) — **DONE.** Both removed, plus the telemetry consent checkbox found alongside them (TD-80). Three falsification mutations, all killed. 310 → 313 tests. See §12.22.

### Phase 1 — Stabilisation

- [ ] Structured file logging with redaction at the boundary; delete the OCR content trace (TD-14, TD-25)
- [ ] Real diagnostics or no diagnostics (TD-15)
- [ ] Envelope encryption: random data key wrapped by the Argon2 key → password change and biometric unlock become ordinary features (TD-06)
- [ ] Per-vault encrypted ML model; scope the categoriser to the vault session (TD-16)
- [ ] Row versions; indexes on `Date` and `AccountId`; FK transaction→account; scaled-integer money converter (TD-17, TD-30, TD-31)
- [ ] Money scale + rounding + `Allocate`; line-item reconciliation in the confirmation UI (TD-18, TD-19)
- [ ] Receipt duplicate detection; locale-driven date parsing; honest confidence labelling (TD-20, TD-21, TD-22)
- [ ] Exception taxonomy; split unlock auth and schema phases; stop surfacing `ex.Message` (TD-23, TD-24)
- [ ] Packaging, code signing, update channel (TD-49)
- [ ] Reconcile the docs: mark every unimplemented spec `Status: Aspirational`; write ADR-0012/0013 or drop the citations (TD-39, TD-40)

### Phase 2 — Engineering quality

- [ ] One shared `AddPineApplication()` / `AddPineInfrastructure()`; delete the duplicated roots (TD-29)
- [ ] `IOptions` configuration for thresholds, paths, KDF parameters (TD-27)
- [ ] Typed view-model factories; remove `IServiceProvider` and `PineDbContext` from the UI; add an architecture test asserting UI ↛ Infrastructure (TD-28)
- [ ] Replace the reflection call with a domain method or a single transfer aggregate (TD-26)
- [ ] `IDisposable` view models with deterministic messenger unregistration (TD-34); awaited initialisation (TD-35)
- [ ] Delete dead code: unused `InferenceSession`, empty projects, stub test projects, Attachment/Rule/Tag if not being built (TD-36, TD-37, TD-38)
- [ ] Central package management; one PaddleOCR package; Git LFS for the ONNX files (TD-41, TD-42, TD-43)
- [ ] `CategoryId` as an FK on splits; dispatch or remove domain events (TD-32, TD-33)
- [ ] Coverage floor enforced in CI; rewrite the architecture-test assertions with `NotHaveDependencyOnAny`

### Phase 3 — Long-term

- [ ] Decide the platform story honestly. Either invest in per-RID native runtimes and a non-DirectShow camera abstraction, or narrow the README to the platforms that actually ship. The Browser head cannot run OpenCV or PaddleInference and should probably be dropped.
- [ ] Localisation infrastructure before the view count grows (TD-45)
- [ ] Attachment storage — persist the receipt image encrypted in the vault, linked to its transaction. The missing half of the OCR feature's audit value.
- [ ] Rule engine, on top of the now-real `CategoryId` relationship
- [ ] Sync — **only** after row versions, audit columns and stable natural keys exist
- [ ] Plugin host, or formally retire the two plugin documents and delete the empty projects

---

## 11. Final verdict

| | |
|---|---|
| **Current rating** | **2.3 / 5 ★★☆☆☆** |
| **Production status** | **Not Production Ready** |

**Why not "Prototype / Early Development":** the architecture is coherent, the
domain is properly modelled, the privacy posture is genuine and the Windows head
builds clean and runs. This is well past prototype.

**Why not "Production Ready with Conditions":** the blockers are not a bounded
punch list. They include a data-integrity defect in the core write path, the
absence of any correction mechanism for bad data, a security-architecture decision
that becomes irreversible the moment real vaults exist, and a verification pipeline
that cannot detect regressions in any of it.

- **Biggest strength** — The privacy and trust architecture is real where it counts: no network egress anywhere in the codebase, and OCR output that cannot reach the ledger without explicit human confirmation.
- **Biggest weakness** — No transactional boundary in the write path, and no mechanism to correct the result.
- **Biggest production risk** — Silent, permanent financial data corruption: partial writes, silently dropped imports and misdated receipts all fail invisibly, and nothing in the application can detect or repair them afterwards.
- **Most important next action** — Register `PineDbContext` as scoped, open a DI scope per MediatR request, remove `SaveChangesAsync` from every repository, and commit once per command inside an explicit database transaction — with tests for the transaction-plus-balance path written at the same time.

### Architecture verdict

The architecture is fundamentally sound and does not need restructuring. The
hexagonal decomposition is real rather than nominal: `Pine.Core` holds no external
dependency, ports are defined in the domain and implemented in infrastructure, the
AI engine is genuinely swappable, and the UI dispatches through MediatR rather than
reaching for repositories. That skeleton is worth keeping, and a rewrite would
discard a correct structure to fix defects that live inside it.

What has not been built is the layer between the architecture and the data: no unit
of work, no validation stage, no configuration, no logging, no concurrency control,
no correction path. These are all cross-cutting concerns, all insertable at the
composition root and the MediatR pipeline, and all addressable without touching the
domain model or the views. That is why the recommendation is remediation rather
than restructuring.

The honest summary is that this project has the shape of a mature system and the
substance of an early one. Sixty specification documents, six platform heads, five
test projects, four empty modules and a plugin SDK sit on top of 11.7k lines of
source, 28 tests, and a write path that cannot guarantee a transaction and its
balance are saved together. The scaffolding was built to the plan; the load-bearing
work was not. Nothing in that ordering is fatal — the foundations are good and the
fixes are well-defined — but the project is considerably further from production
than `STATUS.md` ("Phase 1 and 2: 100% Completed") represents, and closing that gap
starts with making the write path atomic and the CI pipeline honest.

---

## 12. Remediation log

> This section is append-only. Each iteration records what was **actually
> exercised**, what was merely written, and what was found along the way. The rule
> the audit itself demands applies here first: nothing is called complete unless it
> is implemented end-to-end, registered in DI, reachable from the real code path and
> covered by a passing test. Where that is not true, it says so.

### 12.1 Iteration 1 — Phase 0, items 1–3

| Field | Value |
|---|---|
| Date | 2026-08-26 |
| Branch | `feat-ui-production-theming` |
| Baseline commit | `c1c8844` |
| Scope | Phase 0 items **1**, **2**, **3** → TD-05, TD-10, TD-07, TD-08 |
| Outcome | 1 closed ✅ · 3 partial 🟡 · 2 debts closed as a side effect · 6 new debts opened |
| State | Working tree only — **not committed, not pushed** |

#### Verification actually executed

Run by the integrating agent, not self-reported by the implementers.

| Check | Result |
|---|---|
| `dotnet build src/Pine.Ui.Windows -c Release` | ✅ 0 warnings, 0 errors |
| `dotnet build src/Pine.Ui.Macos -c Release` | ✅ 0 warnings, 0 errors |
| `dotnet build src/Pine.Cli -c Release` | ✅ 0 warnings, 0 errors |
| `dotnet build src/Pine.Ui.Web -c Release` | ✅ 0 errors (1 pre-existing SDK `.targets` warning, not a compiler diagnostic) |
| `dotnet test tests/Pine.Tests.Unit` | ✅ **51 passed, 0 failed** (was 23) |
| `dotnet test tests/Pine.Tests.Integration` | ✅ **43 passed, 0 failed** (was 5) |
| `Pine.Core.csproj` dependency count | ✅ zero `PackageReference`, zero `ProjectReference` — the domain stayed clean |
| `TODO` / `FIXME` / dead code in new source | ✅ none |
| `Pine.Infrastructure.Persistence` references remaining | ✅ none |

**Test suite: 28 → 94 passing.**

#### TD-05 — Versioned vault header · ✅ Done

`pine.vault.json` records a magic marker, `headerVersion`, `kdfId` and all five
Argon2id parameters beside the salt. Key derivation is driven by the header, never
by constants, so changing the KDF profile becomes an additive change instead of an
event that bricks every existing vault.

- **New ports** in `src/Pine.Core/Security/`: `VaultHeader`, `KdfParameters`,
  `IVaultHeaderStore`, `IKeyDerivationService`, and typed
  `UnsupportedVaultHeaderException` / `VaultHeaderCorruptException` /
  `VaultHeaderNotFoundException`. `Pine.Core` gained no package reference.
- **Adapters**: `FileVaultHeaderStore`, `Argon2idKeyDerivationService`.
- **Refusal over guessing.** An unknown `kdfId` or a future `headerVersion` throws;
  corrupt JSON, bad Base64 or an out-of-range value throws. The store never falls
  back to defaults — deriving with guessed parameters is precisely the silent
  corruption this ticket exists to eliminate.
- **Atomic write.** Temp file → `Flush(flushToDisk: true)` → `File.Replace`. A
  crash mid-write can never leave a truncated, unopenable header.
- **Legacy vaults.** A `pine.salt`-only vault is adopted into a v1 header on first
  read, idempotently. **`pine.salt` is deliberately left on disk** as a fallback.
- **The proof that matters.** A known-answer vector was computed from the old
  implementation *before* it was deleted, and is asserted both directly and through
  legacy adoption: password `correct horse battery staple`, salt `000102…0f` → key
  `853b272a44db1421c02962669a55eb0994f3cab385ed1c4c79253eee19bab49e`. Without this
  test the ticket would be worthless, because a header that derives a *different*
  key opens no existing vault.
- **Deleted, not shimmed**: `Argon2KeyDerivation.cs`. No second derivation path.

#### TD-10 — Pre-migration backup + encrypted export · 🟡 Partial

- **`VACUUM INTO` on the already-keyed SQLCipher connection**, so the copy is
  encrypted with the same key and vault contents never touch disk in plaintext.
  Unlike `File.Copy`, it also cannot capture a torn page.
- **Encryption verified empirically, not assumed** — on
  `SQLitePCLRaw.bundle_e_sqlcipher` 2.1.11 a single test asserts three things: the
  backup's first 16 bytes are not `SQLite format 3`; the correct key reads the
  seeded row; a wrong key throws `SqliteException` with `SqliteErrorCode == 26`
  (`SQLITE_NOTADB`). No plaintext fallback was needed or added.
- **`EfCoreVaultMigrator`** replaces `PineDbInitializer.Migrate`. Sequence: read
  pending migrations → **return early if none** (unlock happens constantly and must
  stay write-free) → back up → migrate → prune. On failure it throws
  `VaultMigrationException` carrying the pending list *and* the backup path.
- A first-run vault is correctly *not* backed up: no applied migrations means no
  user data to lose.
- **What is missing:** see §12.3 items 1 and 2 — the export command is unreachable
  and there is no restore path. Tracked as **TD-56**.

#### TD-07 / TD-08 — CI and the broken heads · 🟡 Partial

- `Pine.slnx`: the `<!-- asdasda` marker is gone and `/tests/` is restored, so the
  five test projects are part of the solution again.
- All three heads: the non-existent `using Pine.Infrastructure.Persistence;`
  removed, and `typeof(Pine.Application.DependencyInjection)` — a type that does not
  exist — replaced with the same anchor the Windows head uses.
- `.github/workflows/dotnet-ci.yml` rewritten into three jobs: `desktop`
  (ubuntu + windows), `platform-heads` (android/ubuntu, wasm/ubuntu, ios/macos, each
  installing its workload — gated, not excluded), and `test` (unit + integration,
  TRX uploaded). The three empty test projects are compile-checked under a step name
  that says they assert nothing, instead of being reported as green coverage.
- **What is missing:** see §12.3 items 3 and 4.

#### Closed as a side effect

- **TD-23** ✅ — because migration failures now throw a typed
  `VaultMigrationException`, `UnlockViewModel` no longer reports them as
  "Incorrect password or corrupted vault". Auth and schema phases are separated; the
  generic collapsed message is retained for genuine auth failures, which is correct
  security practice and must not regress.
- **TD-51** 🟡 — the `<!-- asdasda` marker is gone from `Pine.slnx`. The broader
  draft-comment cleanup across the codebase is untouched.

#### DI wiring completed across all six composition roots

`IVaultHeaderStore` and `IKeyDerivationService` (singleton, stateless) plus
`IVaultBackupService` and `IVaultMigrator` (matching each root's `PineDbContext`
lifetime) are registered in Windows, macOS, Web, Android and iOS. This was
mandatory, not cosmetic: `SetupViewModel` and `UnlockViewModel` now take the two
ports by **constructor injection**, so any root missing them would throw at
resolution.

### 12.2 Findings discovered during remediation

Not present in the original audit. Filed as **TD-52 … TD-57** in §8.

1. **The platform heads compile but do not work (TD-52).** None of Web, Android or
   iOS registers `TimeProvider.System`, which `CreateAccountCommandHandler`
   requires — verified by grep: Windows and macOS have it, the other three have zero
   references. They also omit `IAIProvider`, `IImageProcessor`,
   `ICategorizationTrainer`, `AddLogging` and six ViewModels. Fixing TD-08 made them
   *compile*; it did not make them *run*. This is TD-29 (five duplicated composition
   roots) showing its real cost.
2. **The Web head's own comment is false (TD-53).** It claims an in-memory database
   for WebAssembly, but constructs a real SQLCipher `PineDbContext` whose WASM
   native library is explicitly not linked. It will fail at first database access.
3. **The new CI workflow reintroduces the TD-07 drift class (TD-54).** It enumerates
   projects individually rather than building `Pine.slnx`, so a project added to the
   solution tomorrow is silently outside CI — the exact failure mode TD-07 existed
   to eliminate.
4. **`TreatWarningsAsErrors` extends to NuGet audit warnings (TD-55).** A newly
   published CVE advisory can turn CI red with no code change.
5. **`ListBackups` cannot report `SchemaVersionBefore`.** That value lives inside the
   encrypted file; it is populated only on the `VaultBackupInfo` returned by
   `CreateBackupAsync`. Documented in the port.
6. **`IVaultMigrator` had to land in `Pine.Core` (TD-57)** rather than
   `Pine.Application`, because `Pine.Infrastructure` has no `ProjectReference` to
   `Pine.Application`. `Pine.Core` stays package-free and EF-free either way, but a
   persistence-orchestration port in the domain project is a compromise, not a
   design choice.

### 12.3 Explicitly NOT done

Stated plainly, per the document's own standard.

1. **No restore-from-backup path.** Recovering a backup requires the user to copy
   the file over `pine.db` by hand. (TD-56)
2. **`ExportVaultBackupCommand` has no UI entry point.** It is registered and
   reachable via `IMediator`, but no menu, button or ViewModel invokes it, so the
   "encrypted export" half of Phase 0 item 2 does not exist for a user. (TD-56)
3. **Android and iOS were never compiled.** The workloads are not installed on the
   development machine, so the build dies at workload resolution and the C# compiler
   is never invoked. The fixes are correct by inspection against the working Windows
   head; **they are not compiler-proven.** The first CI run is what will actually
   verify them.
4. **The CI workflow has never executed.** It parses as valid YAML and its job graph
   is correct on paper. Nothing beyond that is demonstrated: not that the ubuntu leg
   builds, not that `dotnet workload install` succeeds on hosted runners, not that
   the integration tests pass on Linux.
5. **Avalonia 12.0.5 base-class shapes are unverified** for
   `AvaloniaMainActivity` / `AvaloniaAppDelegate<T>` (packages absent from the local
   cache, network unavailable). A latent `CS0534` on the mobile heads is possible.
6. **The three stub test projects remain empty** (TD-38 unchanged). CI no longer
   pretends otherwise, which is the honest interim position, not a fix.
7. **Nothing was committed or pushed.** All work sits in the working tree.

### 12.4 Next (after iteration 1)

Phase 0 item **4 — TD-01**: scope-per-request DI plus `IUnitOfWork`, strip
`SaveChangesAsync` from every repository, one database transaction per command.
This is the document's own "most important next action" and the highest-weighted
failure in the scorecard. Note that it will touch the lifetimes of
`IVaultBackupService` and `IVaultMigrator` introduced above, which currently follow
`PineDbContext`.

### 12.5 Iteration 2 — Phase 0, item 4 (TD-01)

| Field | Value |
|---|---|
| Date | 2026-08-26 |
| Scope | Phase 0 item **4** → TD-01, the audit's own "most important next action" |
| Outcome | **TD-01 ✅ closed** · 2 pre-existing defects fixed incidentally · 1 new debt opened (TD-58) |
| State | Working tree only — **not committed, not pushed** |

#### Verification actually executed

| Check | Result |
|---|---|
| `dotnet build src/Pine.Ui.Windows -c Release` | ✅ 0 warnings, 0 errors |
| `dotnet build src/Pine.Ui.Macos -c Release` | ✅ 0 warnings, 0 errors |
| `dotnet build src/Pine.Ui.Web -c Release` | ✅ 0 errors (1 pre-existing SDK `.targets` warning) |
| `dotnet build src/Pine.Cli -c Release` | ✅ 0 warnings, 0 errors |
| `dotnet test tests/Pine.Tests.Unit` | ✅ **64 passed, 0 failed** |
| `dotnet test tests/Pine.Tests.Integration` | ✅ **60 passed, 0 failed** |
| `SaveChangesAsync` remaining in any repository | ✅ zero (only `EfCoreUnitOfWork` and doc comments) |
| `AddTransient` on `PineDbContext` or any repository | ✅ zero across all five roots |
| Any `I*Repository` injected into `Pine.Ui.Avalonia` | ✅ zero |

**Test suite: 94 → 124 passing** (28 at the audit baseline).

#### What was built

- **`IUnitOfWork`** (`src/Pine.Core/Common/`) + **`EfCoreUnitOfWork`** (Infrastructure), owning one
  `IDbContextTransaction` per request. `Pine.Core` still has zero package and project references.
- **Lifetimes → `Scoped`** for `PineDbContext` and all six repositories in all five composition
  roots. This alone ends the "two repositories, two SQLite connections" defect and the
  transient-`IDisposable` leak.
- **`ScopedMediator`** — a singleton `IMediator`/`ISender`/`IPublisher` over `IServiceScopeFactory`
  that opens one DI scope per dispatch. Without it, scoping the `DbContext` would have achieved
  nothing, because MediatR handlers were resolved from the root provider.
- **`ITransactionalRequest` + `TransactionBehavior`** — begin → `next()` → `SaveChangesAsync` →
  commit; rollback and rethrow on any exception, cancellation included.
- **All 11 repository `SaveChangesAsync` calls removed.** Repositories are now pure change-trackers.

#### Opt-in, not opt-out — and why it is load-bearing

`ExportVaultBackupCommand` runs `VACUUM INTO`, which SQLite **refuses to execute inside a
transaction**. Had the behaviour been opt-out, it would have silently broken the encrypted backup
built in iteration 1. The exclusion is not a preference and is not defended by vigilance: a test
(`VaultBackup_InsideAnOpenTransaction_Fails`) opens a transaction, attempts the backup, and asserts
the resulting `SqliteException` — proving the exclusion is a *necessity*. A second test asserts
`ExportVaultBackupCommand` is not assignable to `ITransactionalRequest`, so re-marking it breaks CI.

Marked transactional: `CreateAccount`, `RecordTransaction`, `AddTransaction`, `TransferFunds`,
`ImportTransactions`, `CreateBudget`, `Create`/`UpdateImportProfile`, `SaveSetting`,
`CreateCategory`. Deliberately not marked: `ExportVaultBackupCommand` (above), `ScanReceiptCommand`
(depends only on `IAIProvider`, performs zero repository calls — verified, not assumed), and all six
queries.

#### The trap that made this ticket dangerous — four write paths, not two

Stripping `SaveChangesAsync` from repositories silently deletes any write that does not pass through
the new transaction boundary. Two such paths were predicted before the work started; **four were
found**:

| Path | Disposition |
|---|---|
| `SetupViewModel.cs:145-147` — `ISettingRepository.SaveAsync` ×2 during first-run vault creation | → `SaveSettingCommand` |
| `SettingsViewModel.cs` — 3 reads + 1 write | → `GetSettingQuery` / `SaveSettingCommand`; the repository field is gone |
| `DiagnosticsViewModel.cs:94` — `ICategoryRepository.SaveAsync` | → `CreateCategoryCommand`. **Not predicted; a genuine data-loss path.** |
| `CategorizationTrainingBackgroundService.cs:115` — `ISettingRepository.SaveAsync` | → `ISender.Send(SaveSettingCommand)`. **Not predicted; would have vanished silently.** |

Captive-dependency and root-resolution fixes: `UnlockViewModel:93,110,115` and `SetupViewModel:135`
no longer resolve scoped services from the root provider. `Pine.Cli`, `Pine.Plugins.Host`,
`Pine.Plugins.Sdk`, `Pine.Ai.Default` and `Pine.Ai.Explainer` were checked and hold no repository or
`PineDbContext` reference at all.

`ValidateScopes = true` is now enabled in all five roots, so a captive dependency becomes a startup
failure rather than a silent leak.

#### How rollback is actually proven

The decisive test does not merely assert that nothing was staged. It **flushes real rows inside the
open transaction**, asserts they are visible there (2 transfer legs present, source balance already
reduced), then fails and asserts the database comes back with zero transactions and both opening
balances intact — plus a unit-of-work call log of exactly
`Begin, SaveChanges, SaveChanges, Rollback` with no `Commit`. A test that only proved "nothing was
written" would pass even if the transaction boundary did not exist.

#### Pre-existing defects fixed incidentally

- **`IImportProfileRepository` was registered in zero composition roots.** All three import-profile
  handlers would have thrown on resolution. Now registered in all five.
- **MediatR 14 hard-requires `ILoggerFactory`, and Web/Android/iOS never called `AddLogging()`** —
  any `Send` on those heads already threw. Fixed in all five. This narrows TD-52 but does **not**
  close it: `TimeProvider.System` is still missing from Web, Android and iOS (verified by grep:
  Windows and macOS have it, the other three have zero references), and macOS still lacks
  `IAIProvider`.

#### New debt opened

- **TD-58** — `AddTransactionViewModel` and `CreateAccountViewModel` are registered `AddTransient`
  but take an unregistered `Action onComplete` constructor parameter. This is why `ValidateOnBuild`
  had to be left off: it aborts startup on this pre-existing defect. Reported rather than papered
  over; it belongs with TD-28 (typed view-model factories).

#### INCOMPLETE / NOT DONE

1. **Android and iOS still cannot be compiled locally** (`NETSDK1147`, workloads absent). Their
   edits were re-read by hand and use only constructs the Web root proved compile, but they remain
   **not compiler-proven**. Unchanged from iteration 1.
2. **`ValidateOnBuild` is off**, so the container is not fully self-checking at startup. Blocked by
   TD-58, which is outside TD-01.
3. Still untouched and still P0: TD-02 (no correction path), TD-03 (silent import dedup), TD-04
   (`AddSplit`), TD-13 (`CurrentValues.SetValues` drops owned collections). Deliberately out of
   scope — this iteration stayed on the transaction boundary.
4. **Nothing was committed or pushed.**

### 12.6 Next (after iteration 2) — superseded

Phase 0 item **5 — TD-04 + TD-13**. Closed by iteration 3; see §12.7.

### 12.7 Iteration 3 — Phase 0, item 5 (TD-04 + TD-13)

| Field | Value |
|---|---|
| Date | 2026-08-27 |
| Scope | Phase 0 item **5** → TD-04 (eager split validation) and TD-13 (owned collections dropped on update) |
| Outcome | **TD-04 ✅ closed · TD-13 ✅ closed** · 1 pre-existing latent corruption fixed incidentally · 2 new debts opened (TD-59, TD-60) |
| State | Working tree only — **not committed, not pushed** |

#### Verification actually executed

| Check | Result |
|---|---|
| `dotnet build src/Pine.Ui.Windows -c Release` | ✅ 0 warnings, 0 errors |
| `dotnet build src/Pine.Ui.Macos -c Release` | ✅ 0 warnings, 0 errors |
| `dotnet build src/Pine.Ui.Web -c Release` | ✅ 0 errors (1 pre-existing SDK `.targets` warning, unchanged from iteration 2) |
| `dotnet build src/Pine.Cli -c Release` | ✅ 0 warnings, 0 errors |
| `dotnet build Pine.slnx -c Release` | ✅ 0 warnings; only 2 × `NETSDK1147` (Android/iOS workloads absent) |
| `dotnet test tests/Pine.Tests.Unit` | ✅ **73 passed, 0 failed, 0 skipped** |
| `dotnet test tests/Pine.Tests.Integration` | ✅ **65 passed, 0 failed, 0 skipped** |
| Falsification: comment out `EnsureValid()` in `SaveAsync` | ✅ boundary test turns **red**, green again on restore |
| `SaveChangesAsync` remaining in any repository | ✅ zero (TD-01 not regressed) |
| `Pine.Core.csproj` package + project references | ✅ zero |
| `CurrentValues.SetValues` remaining in repositories | ⚠️ **4 remain — deliberate, see below** |

**Test suite: 124 → 138 passing** (28 at the audit baseline).

#### What was built

- **`SplitDefinition`** and **`LineItemDefinition`** (`Pine.Core/Transactions/`) — public `readonly record
  struct` transports. They exist because `TransactionSplit`/`TransactionLineItem` have `internal`
  constructors, so Application and UI could not otherwise hand a set of children to the aggregate.
- **`AddSplit` no longer calls `ValidateSplits()`.** Signature unchanged; the currency check stays. This is
  the whole of TD-04: the invariant was correct, enforcing it after every single append was the bug.
- **`ReplaceSplits(IEnumerable<SplitDefinition>)`** and **`ReplaceLineItems(IEnumerable<LineItemDefinition>)`**
  — build a staging list, validate it whole, and swap in **only on success**, so a rejected call leaves the
  aggregate byte-identical. An empty set clears the collection, which `ValidateSplits` already treats as
  valid. `ReplaceLineItems` deliberately carries **no** sum check: reconciling line items against the total
  is TD-19 and stays out of scope.
- **`Transaction.EnsureValid()`** — the aggregate-boundary entry point, called as the first statement of
  `SqliteTransactionRepository.SaveAsync` on both the insert and the update path.
- **`SqliteTransactionRepository.SaveAsync` rewritten** — handles three cases: insert; already-tracked
  same-reference (the common path under scope-per-request DI, where `SetValues` is not merely incomplete but
  a no-op with side effects); and detached, where scalars go through `CurrentValues.SetValues` and the two
  owned collections are merged **through the domain** via `ReplaceSplits`/`ReplaceLineItems`.
- **`ReconcileOwnedChildren`** — EF Core infers an owned child's state from whether its key is set. Both
  owned types assign a `Guid` in their constructor, so EF always guesses `Modified` and emits an `UPDATE`
  against a row that does not exist, failing the flush with a zero-rows-affected concurrency error. This was
  the true root cause of the update path being unusable, and it is **not** what the audit predicted.

#### The trap that made this ticket dangerous — removing the throw is the easy half

Deleting the eager `ValidateSplits()` call takes one line and immediately trades a false throw for silent
corruption: after that change, **nothing in the codebase called `ValidateSplits()` at all**, and every write
path — `AddTransactionCommand`, `CsvImporter`, any future split editor — would have happily persisted a €100
transaction allocated €50. The invariant had to be re-homed *before* the eager check was removed, not after.
It now lives at the persistence boundary, the only choke point every write actually passes through.

Two call sites were also **already latently wrong**: `ImportTransactionsCommand` and `ImportWizardViewModel`
apply an AI category and were guarded only by `if (tx.Splits.Count == 0)`. Weaken or remove that guard under
the old code and each would append a *second* full-amount split, doubling the sum. Both now use
`ReplaceSplits` and are idempotent regardless of the guard.

#### How the fix is *proven*, not asserted

- **Falsification.** `SaveAsync_RefusesATransactionWhoseSplitsDoNotSumToTheTotal` was run with
  `EnsureValid()` commented out and **failed**, then passed again on restore. A test that cannot fail proves
  nothing; this one demonstrably can.
- **Round-trip from a different connection.** `TransactionOwnedCollectionRoundTripTests` drives splits
  **2 → 3 → 1 → 0** and line items **2 → 3**, asserting after each step from a brand-new `PineDbContext`
  *and* from a raw SQLCipher `SELECT COUNT(*)` that bypasses EF entirely. Asserting through the writing
  context would pass even for a repository that dropped the collections, because EF's identity map returns
  the in-memory graph regardless of what SQL ran. The shrink direction is the one a naive clear-and-re-add
  gets wrong, which is why 3 → 1 → 0 is tested and not merely growth.
- **A real file vault, not `":memory:"`** — an in-memory database dies with its connection, so a second
  connection would see an empty database and every assertion would pass for the wrong reason.
- **Production-shaped writes** — through the repository plus `EfCoreUnitOfWork` (begin / flush / commit),
  never `SaveChangesAsync` behind the repository's back, which would exercise a path that does not exist.
- **Atomicity of rejection** is asserted positively: a rejected `ReplaceSplits` leaves count, amounts,
  categories, notes *and* split ids unchanged.

#### Pre-existing defects fixed incidentally

- **Owned-entity instance aliasing — a live, reachable corruption path.** `Money` and `CurrencyCode` are both
  sealed *reference* types. Two splits built from the same `Money` instance were one owned entity to EF,
  which wrote the currency column for a single owner and `NULL` for the rest, tripping the `NOT NULL`
  constraint. This was reachable today: `ImportTransactionsCommand` and `ImportWizardViewModel` both pass
  `tx.TotalAmount` straight in as the split amount. Fixed at the correct layer — `TransactionSplit` and
  `TransactionLineItem` now deep-copy incoming `Money` (including its nested `CurrencyCode`) in their
  constructors, so no two owned children can share an instance. A regression test pins the aliased shape.
- **A `PineDbContext` mapping change was made and then reverted.** Mid-iteration the split/line-item currency
  was remapped from a nested `OwnsOne` to a `HasConversion` value converter to tolerate the aliasing above.
  Once the defensive copy landed, that mapping change became redundant, and it was **reverted after testing
  showed the full integration suite passes without it**. This was deliberate: the converter left
  `PineDbContextModelSnapshot.cs` describing a model the context no longer built, and
  `EfCoreVaultMigrator.MigrateAsync` calls `Database.Migrate()` — snapshot drift there is a vault-open risk
  for every existing user. Reverting removed that risk class entirely rather than mitigating it, and required
  no migration. **`PineDbContext.cs` is unmodified in the final tree.** The counter-argument on record: the
  copy only protects children created *through* the aggregate, and EF's load path bypasses constructors. That
  was judged not to apply — EF materialises each owned child per row, so loaded children are already distinct
  instances, and the 2 → 3 → 1 → 0 round-trips exercise exactly that load-mutate-save path.

#### New debt opened

- **TD-59** — `ReconcileOwnedChildren` promotes owned children from `Modified` to `Added` on the reasoning
  that neither owned type exposes a mutator, so a `Modified` child can only be a mis-classified insert. That
  reasoning is **load-bearing**: the day either type gains a public setter, a genuine update becomes a silent
  insert. It was found **unpinned** and is now pinned by
  `OwnedTransactionChildren_ShouldNot_ExposePublicSetters`, so such a change breaks a test instead of the
  database. The underlying reliance on EF's state inference remains debt.
- **TD-60** — `ReplaceSplits`/`ReplaceLineItems` mint fresh `Guid` ids, so a detached save deletes and
  re-inserts every owned child with new ids. Harmless today because nothing references a split id; it becomes
  a real problem the moment rules, attachments or an audit trail do.

#### Why `CurrentValues.SetValues` is still present in four places

The literal checklist item "zero `CurrentValues.SetValues` in repositories" **does not hold**, and saying
otherwise would be false. It remains in the account, budget and category repositories, and on the detached
branch of the transaction repository. The defect it names is nonetheless closed:
`EfModel_DeclaresOwnedCollectionsOnTransactionOnly` asserts against the live EF model that
`Transaction.Splits` and `Transaction.LineItems` are the **only** owned collections in the entire model, so
on the other three aggregates `SetValues` is provably dropping nothing — `Account.Balance` and `Budget.Limit`
are `ComplexProperty`, and `Category`/`Setting` have neither. On the transaction path it now copies only
scalars and the `TotalAmount` complex property, with the owned collections merged explicitly alongside it.
The call was left in place rather than hand-rolled per property because that is precisely what it is correct
for; the model-metadata test is what stops the day someone adds a fourth owned collection from being silent.
No fix was invented for repositories that did not have the defect.

#### INCOMPLETE / NOT DONE

1. **Android and iOS still cannot be compiled locally** (`NETSDK1147`, workloads absent). Unchanged from
   iterations 1 and 2. Nothing in this iteration touched their code, but they remain **not compiler-proven**.
2. **Line-item shrink/clear is covered only for growth in the committed suite** (2 → 3). `ReplaceLineItems`
   supports clearing and was exercised shrinking in a scratchpad harness, but **a scratchpad harness is not
   the committed test suite and does not count toward a ✅**. Committed coverage for the shrink direction is
   on `Splits` only.
3. **TD-19 remains untouched** — nothing reconciles `Σ LineItem.TotalPrice` against `TotalAmount`. A
   transaction can still hold internally contradictory line items; only the *split* sum is enforced.
4. **The invariant is enforced at the persistence boundary, not at every mutation.** An aggregate mutated and
   never saved is never validated, and a caller that interleaves `AddSplit` calls relies entirely on the
   repository check. This is the deliberate trade of TD-04, and it is sound only while `SaveAsync` calls
   `EnsureValid()` — now falsification-tested, but a single point of enforcement.
5. Still untouched and still P0: TD-02 (no correction path), TD-03 (silent import dedup), TD-12 (currency not
   validated against account). Deliberately out of scope.
6. **Nothing was committed or pushed.**

### 12.8 Next (after iteration 3) — superseded

> Closed by iteration 4; see §12.9.


Phase 0 item **6 — TD-12**: a MediatR validation behaviour, and validating transaction currency against the
account's currency. `AddTransactionCommandHandler` today builds a `Money` from the request, **saves the
transaction**, and only then calls `account.Deposit(...)`, which throws on mismatch inside `Money.Add` —
leaving an orphaned ledger entry with no matching balance movement. `TransferFundsCommandHandler` is the only
handler that validates currency up front. With TD-01 closed the write is now rolled back rather than
half-committed, so the damage is contained, but the command still fails at the wrong layer and reports the
wrong thing to the user. TD-12 is the last of the two corruption paths named in blocker 10; TD-13, the other,
was closed in this iteration.

---

### 12.9 Iteration 4 — Phase 0, item 6 (TD-12)

| Field | Value |
|---|---|
| Date | 2026-08-27 |
| Scope | Phase 0 item **6** → TD-12 (transaction currency never validated against the account) |
| Outcome | **TD-12 ✅ closed — and reclassified: it was no longer a corruption path.** Blocker 10 closes entirely · 1 duplication removed incidentally · 3 new debts opened (TD-61, TD-62, TD-63) |
| State | Working tree only — **not committed, not pushed** |

#### Verification actually executed

| Check | Result |
|---|---|
| Characterisation of the PRE-fix defect on a real vault | ✅ measured — see "the trap" below |
| `dotnet build src/Pine.Ui.Windows -c Release` | ✅ 0 warnings, 0 errors |
| `dotnet build src/Pine.Ui.Macos -c Release` | ✅ 0 warnings, 0 errors |
| `dotnet build src/Pine.Ui.Web -c Release` | ✅ 0 errors (1 pre-existing SDK `.targets` warning, unchanged from iterations 2–3) |
| `dotnet build src/Pine.Cli -c Release` | ✅ 0 warnings, 0 errors |
| `dotnet build Pine.slnx -c Release` | ✅ 0 warnings; only 2 × `NETSDK1147` (Android/iOS workloads absent) |
| `dotnet test tests/Pine.Tests.Unit` | ✅ **81 passed, 0 failed, 0 skipped** |
| `dotnet test tests/Pine.Tests.Integration` | ✅ **71 passed, 0 failed, 0 skipped** |
| Falsification M1 — neuter the currency comparison | ✅ `AddTransaction_WithAMismatchedCurrency_…` turns **red**, with the pre-fix `InvalidOperationException` verbatim |
| Falsification M2 — swap the two `AddOpenBehavior` calls | ✅ **5 tests red** (1 architecture + 4 integration, all on `Calls.Should().BeEmpty()`) |
| Falsification M3a — drop normalisation in the validator | ✅ `AddTransaction_WithALowercaseCurrency_…` turns **red** |
| Falsification M3b — drop normalisation in `CurrencyCode` | ✅ same test **red**, with `ArgumentException` from the constructor |
| Falsification M4 — hide one validator from the DI scan | ✅ `EveryValidator_…ShouldBeRegisteredAsScoped` **red**, plus 2 integration tests |
| Green restored after every mutation | ✅ 81 + 71 re-run clean |
| `SaveChangesAsync` outside `EfCoreUnitOfWork` | ✅ zero (TD-01 not regressed) |
| `Pine.Core.csproj` package + project references | ✅ zero |
| Android / iOS compiled | ❌ **impossible locally** — workloads absent, unchanged since iteration 1 |

**Test suite: 138 → 152 passing.**

#### What was built

- **The validator seam** (`Pine.Application/Common/`) — `IRequestValidator<TRequest>`,
  `ValidationError(PropertyName, Message)`, `RequestValidationException` carrying the structured `Errors`,
  and the shared `CurrencyCodeText.TryNormalise`.
- **`ValidationBehavior<TRequest, TResponse>`** (`Common/Behaviors/`, beside `TransactionBehavior`) —
  resolves `IEnumerable<IRequestValidator<TRequest>>`, runs every validator **sequentially** (they share one
  scoped `PineDbContext`, which is not thread-safe, so `Task.WhenAll` here would be a latent concurrency
  bug), aggregates all errors into one exception, and calls the handler only when there are none. A request
  with no registered validator sees an empty enumerable and passes through.
- **Opt-in with no second source of truth.** Unlike `ITransactionalRequest`, validation has **no marker
  interface**: the opt-in signal *is* the registered validator. A marker would allow a request to be labelled
  "validated" with nothing behind it — silently unvalidated, which is precisely the failure this seam exists
  to prevent.
- **Four validators**, each in the same file as its command, matching the existing one-file convention:
  `AddTransactionCommandValidator`, `RecordTransactionCommandValidator`, `TransferFundsCommandValidator`,
  `ImportTransactionsCommandValidator`.
- **Reflection registration** in `AddPineApplication` — every `IRequestValidator<>` in the assembly is
  registered Scoped via `TryAddEnumerable`, once, in the single extension all five composition roots call.
- **`TransferFundsCommandHandler`'s up-front checks folded into its validator**, removing the second idiom.

#### Why the seam is hand-rolled and not FluentValidation

Decided explicitly, not by default. FluentValidation's value is *declarative input rules* — `NotEmpty`,
`Length`, `Matches`. The rule this ticket needed is none of those: "the currency must equal the account's
currency" is a **database read**, and the only part of the library that would have been used is its
`IValidator<T>` abstraction plus assembly scanning — roughly forty lines, which is what was written. Against
that: TD-42 records that this solution has **no central package management**, so a new dependency means
hand-pinning a version across nineteen project files, and `TreatWarningsAsErrors` makes any new analyzer
surface a build-breaking risk. Every comparable seam here — `ITransactionalRequest`, `ScopedMediator`,
`EfCoreUnitOfWork` — is hand-rolled and documented in prose, and this audit repeatedly penalises *two idioms
for one job*. The counter-argument on record: once validation grows past a handful of rules, re-implementing
rule composition, conditional rules and message templating would be a bad trade, and the seam should be
replaced rather than extended. `IRequestValidator<T>` is deliberately narrow enough that a FluentValidation
adapter is a drop-in later.

#### Pipeline ordering, and what it costs

`AddOpenBehavior` order is execution order. `ValidationBehavior` is registered **first**, so it wraps
`TransactionBehavior` and every validator's read happens **outside** the write transaction.

The consequence is observable and asserted: a rejected command's unit-of-work log is **empty**, not
`Begin, Rollback`. That is the difference between a refusal and a failure, and M2 shows nothing else would
catch the reversal — the exception type and message are identical either way.

The price is a time-of-check/time-of-use window, and it is provably empty *for this rule*. The only fact read
outside the transaction is an account's **currency**, and an account's currency cannot change after
`Account.Create`: `Balance` has a private setter and the only mutators, `Deposit` and `Withdraw`, route
through `Money.Add`/`Money.Subtract`, which throw on any currency but the one already held. That assumption
is not left to prose — `Account_ShouldNot_ExposeAnyPublicMeansOfChangingItsCurrency` pins `Account`'s public
surface to exactly `{Create, Deposit, Withdraw}` with no public setters, so the day an account gains a
re-denomination path, this ordering decision breaks a test instead of the ledger. **That test is a tripwire
on an assumption, not a proof there is no window**, and it is recorded as such rather than sold as more.

#### Failure mode: a typed exception, and its precise relationship to TD-24

Validation failures throw `RequestValidationException`, not a result type. A `Result<T>` is the better shape
in the abstract and is not available at an honest price: Pine's requests return `Guid`,
`IReadOnlyList<Transaction>`, `ImportProfile`, `string?` and `void`, and threading an envelope through them
would rewrite every handler, every `Send` call site and every view model. That is TD-24, and it is **not**
done here.

What *did* change is what the existing indiscriminate `catch (Exception ex) { ErrorMessage = $"Error: {ex.Message}"; }`
now catches. Before: `Cannot add money of different currencies: USD and EUR` — a domain internal, thrown from
inside `Money.Add`, shown verbatim to a user. After: `Transaction currency EUR does not match account currency USD.`
TD-24's blast radius narrows on the covered paths; TD-24 itself stays open, and no view model was touched.

#### The trap in this ticket — the defect was not what the audit said it was

§6.4 and blocker 10 both described TD-12 as a live corruption path: transaction saved, then
`account.Deposit(...)` throws, leaving *an orphaned ledger entry with no matching balance movement*. The
first thing iteration 4 did was try to reproduce that, on a real SQLCipher file vault through the real
container and the real pipeline. **It does not happen.** Measured, pre-fix:

```
EXCEPTION = System.InvalidOperationException: Cannot add money of different currencies: USD and EUR
TXROWS    = 0
BALANCE   = 1000  (unchanged)
UOW       = Begin, Rollback
```

Closing TD-01 in iteration 2 had already contained it. No repository calls `SaveChangesAsync`, so the
"saved" transaction was only staged on the scoped `DbContext`, and `TransactionBehavior` rolled the whole
command back. **TD-12 had silently degraded from data corruption to a wrong-layer throw with a leaky message
— and the audit went on asserting the corruption for two more iterations.** Writing the ✅ without saying
that would have been the convenient thing rather than the true one.

Two further things the ticket's framing got wrong, both found by enumerating rather than trusting the list:

- **The mismatch is not reachable from the shipped UI at all.** `AddTransactionViewModel` and
  `ScanReceiptViewModel` both derive the currency from `SelectedAccount.Balance.Currency.Value`;
  `DiagnosticsViewModel` hardcodes `"USD"`; `CsvImporter` builds every `Money` from
  `targetAccount.Balance.Currency`. There is today no user gesture that produces a mismatched
  `AddTransactionCommand`. The validators are therefore a **boundary guard against future and pluggable
  callers**, not a fix for a bug a user can hit — the honest severity is 🟡, not 🟠.
- **`ImportTransactionsCommand` cannot be validated up front, and pretending otherwise would be worse.**
  The imported transactions do not exist until `Importer.ImportAsync` runs *inside* the handler, and reading
  the stream in a validator would consume a forward-only stream the handler needs. So the import validator
  checks only that the account exists, and the currency check sits in the one window that does exist: after
  the import and dedup, **strictly before the first `SaveAsync`**. It throws the same
  `RequestValidationException` so callers see one failure shape. `IImporter` is a port and the command
  carries the importer *instance* as a payload field, so a plugin can supply any currency it likes — that
  guard is the boundary check on the port, and it is the one place TD-12's fix is genuinely load-bearing
  today.

#### How the fix is *proven*, not asserted

- **Four falsification mutations, all executed, all restored.** M1 (neuter the comparison) makes the pre-fix
  `InvalidOperationException` reappear verbatim — the same string the test asserts is *absent*, so the
  mutation is caught twice. M2 (swap the behaviour order) reddens five tests. M3a/M3b (drop the
  normalisation, in the validator and in the domain) redden the lowercase test. M4 (hide a validator from the
  reflection scan) reddens the architecture test plus two integration tests — the nastiest production
  analogue, a validator that exists, reads correctly and is never called.
- **Rejection asserted from outside EF entirely.** `AtomicityHost.CountRowsRaw` issues
  `SELECT COUNT(*)` over a raw keyed SQLCipher connection, so "zero transactions, one untouched account" is a
  claim no part of EF's model, translation or identity map can explain away.
- **The empty unit-of-work log** is the assertion that distinguishes *refused* from *rolled back*. Without it
  M2 would be invisible.
- **Normalisation was previously unpinned.** Before this iteration no test anywhere passed a lowercase
  currency code, so `CurrencyCode`'s `Trim().ToUpperInvariant()` could have been deleted against the 138-test
  baseline with nothing going red. It is now pinned.
- **Every money write goes through the pipeline.** No view model or service touches
  `ITransactionRepository`/`IAccountRepository` directly; the only non-handler consumer,
  `CategorizationTrainingBackgroundService`, reads. So validation has no bypass.

#### Pre-existing defects fixed incidentally

- **`TransferFundsCommandHandler` compared raw strings.** Its old check was
  `sourceAccount.Balance.Currency.Value != request.CurrencyCode`, which rejected `"usd"` outright even though
  `CurrencyCode` trims and upper-cases and the domain accepts it. Folding the check into the validator
  normalises first, so a legitimate input stopped being rejected. This is a **behavioural widening**, not a
  relocation, and is called out rather than buried.
- **A shape check triplicated across three validators** was promoted to the single
  `CurrencyCodeText.TryNormalise`. Filed as TD-61, because one shared copy of a domain rule living outside the
  domain is still a copy.

#### On modifying `AtomicityHost`, the harness that proves TD-01

`AtomicityHost` exists to prove a closed P0 blocker, so changing it deserves scrutiny. The change is
**purely additive**: one new method, `CountRowsRaw(string table)`, which opens a raw keyed SQLCipher
connection via the pre-existing `TempVault.OpenRaw` and runs `SELECT COUNT(*)`. No existing member was
altered or removed, no registration in the container changed, and **no existing test file under
`tests/Pine.Tests.Integration/Atomicity/` was touched** — `TransferFundsAtomicityTests`,
`RecordTransactionAtomicityTests`, `ImportAtomicityTests`, `RequestScopeAndRepositoryBoundaryTests` and
`VaultBackupIsNotTransactionalTests` are byte-identical to iteration 3 and all 65 of their assertions still
pass unchanged inside the new 71.

#### New debt opened

- **TD-61** — `CurrencyCodeText.TryNormalise` restates `CurrencyCode`'s `^[A-Z]{3}$` outside the domain,
  because the constructor reports failure by throwing and a validator cannot use it. They drift the moment
  `CurrencyCode` gains ISO 4217 *membership* validation (TD-18). The real fix is a non-throwing
  `CurrencyCode.TryCreate`.
- **TD-62** — the validators cover **currency and nothing else**. `AddTransactionCommand` with `Amount == 0`
  still reaches `account.Deposit(...)` and surfaces "Deposit amount must be strictly positive."; the import
  handler still throws a bare `ArgumentException` for a missing account. Same wrong-layer class this ticket
  fixed, different fields. Deliberately out of scope; the seam now exists to fix it cheaply.
- **TD-63** — `CreateBudgetCommand` carries a currency with no account to check it against. Nothing anchors a
  budget's currency to anything.

#### INCOMPLETE / NOT DONE

1. **Android and iOS still cannot be compiled locally** (`NETSDK1147`, workloads absent). Unchanged from
   iterations 1–3. Nothing here touched their code; they remain **not compiler-proven**.
2. **TD-12's severity in §8 was overstated for two iterations and is only now corrected.** The register row
   and blocker 10 both said "orphaned ledger entry" long after TD-01 made that impossible. The row is fixed;
   the lesson — that closing one debt silently changes another's severity, and nothing re-checks — is not
   fixed by anything in this iteration.
3. **`CreateAccountCommand` has no validator.** It *defines* an account's currency, so there is nothing to
   validate it against, but it also means a vault can be seeded with `"ZZZ"` — `CurrencyCode` validates shape,
   not membership. That is TD-18 and stays open.
4. **The TOCTOU argument rests on a tripwire, not a proof.** Reading account currency outside the transaction
   is sound only while an account's currency is immutable. The architecture test detects a change to
   `Account`'s public surface; it cannot detect one made through EF, reflection or a raw SQL update.
5. **The transfer cross-currency message is not pinned.** That test asserts the exception type and a non-empty
   `Errors`, not the message text.
6. **TD-24 is not fixed.** View models still catch `Exception` and render `ex.Message` raw. The message they
   render on the covered paths is now deliberate; the mechanism is still wrong.
7. Still untouched and still P0: TD-02 (no correction path), TD-03 (silent import dedup), TD-09
   (money-path suite), TD-06/TD-11 (key envelope, password policy). Deliberately out of scope.
8. **Nothing was committed or pushed.**

### 12.10 Next (after iteration 4) — superseded

Phase 0 item **7 — TD-03**: rewrite import deduplication on a stable key, with a user-facing review list.
`ImportTransactionsCommandHandler` currently discards any parsed transaction matching an existing one on
`(Date.Date, TotalAmount.Amount)` alone, silently and with no record. Two genuine £4.50 coffees on the same
day are one transaction as far as Pine is concerned, and the user is never told. This is the last of the
original P0 data-loss findings still open — TD-01, TD-04 and TD-13 are closed, and TD-12 closed and was
reclassified — and unlike TD-12 it is **reachable from the shipped UI today**, through
`ImportWizardViewModel`. It needs a stable import key (source row identity, not a value tuple), a
quarantine/review path for suspected duplicates rather than a silent drop, and the dedup decision surfaced
in the wizard. Note that the currency guard added in iteration 4 sits immediately after the dedup loop in
that same handler, so both changes land in the same method and the guard must not be displaced ahead of the
new key logic.

### 12.11 Iteration 5 — Phase 0, item 7 (TD-03)

| Field | Value |
|---|---|
| Date | 2026-08-27 |
| Scope | Phase 0 item **7** → TD-03 (import de-duplication silently discards legitimate transactions) |
| Outcome | **TD-03 ✅ closed.** Blocker 3 closes entirely · a test-coverage hole found and closed during integration · 4 new debts opened (TD-64 … TD-67) |
| State | Working tree only — **not committed, not pushed** |

#### Verification actually executed

| Check | Result |
|---|---|
| `dotnet build src/Pine.Core -c Release` | ✅ 0 warnings, 0 errors |
| `dotnet build src/Pine.Application -c Release` | ✅ 0 warnings, 0 errors |
| `dotnet build src/Pine.Infrastructure -c Release` | ✅ 0 warnings, 0 errors |
| `dotnet build src/Pine.Ui.Avalonia -c Release` | ✅ 0 warnings, 0 errors |
| `dotnet build src/Pine.Ui.Windows -c Release` | ✅ 0 warnings, 0 errors |
| `dotnet build src/Pine.Ui.Macos -c Release` | ✅ 0 warnings, 0 errors |
| `dotnet build src/Pine.Ui.Web -c Release` | ✅ 0 errors (1 pre-existing SDK `.targets` warning, unchanged since iteration 2) |
| `dotnet build src/Pine.Cli -c Release` | ✅ 0 warnings, 0 errors |
| `dotnet build tests/Pine.Tests.Unit` + `tests/Pine.Tests.Integration` | ✅ 0 warnings, 0 errors |
| `dotnet build Pine.slnx -c Release` | ❌ **2 errors, restore aborts** — see the correction below; this command has never proven anything about the code |
| `dotnet test tests/Pine.Tests.Unit` | ✅ **90 passed, 0 failed, 0 skipped** |
| `dotnet test tests/Pine.Tests.Integration` | ✅ **80 passed, 0 failed, 0 skipped** |
| Falsification M1 — key lookup returns nothing | ✅ 2 unit + 3 integration tests **red** |
| Falsification M2 — neuter the in-batch `HashSet` guard | ✅ 1 unit + 1 integration test **red** |
| Falsification M3 — drop `t.AccountId == accountId` from the repository probe | ⚠️ **survived 79 tests**; gap closed, then **red**, then restored — see the trap |
| Falsification M4 — constant import key in `CsvImporter` | ✅ 2 importer tests **red** |
| Green restored after every mutation | ✅ 90 + 80 re-run clean; mutated files SHA-256-identical to their pre-mutation copies |
| `SaveChangesAsync` outside `EfCoreUnitOfWork` | ✅ zero (TD-01 not regressed) |
| `Pine.Core.csproj` package + project references | ✅ zero |
| Currency guard still ahead of the first `SaveAsync` | ✅ verified by line order after the rewrite (dedup → guard → save) |
| Android / iOS compiled | ❌ **impossible locally** — workloads absent, unchanged since iteration 1 |
| The wizard review screen rendered | ❌ **not observed** — no GUI was launched; see INCOMPLETE |

**Test suite: 152 → 170 passing.**

#### A correction to the previous iterations' verification tables

§12.9 recorded `dotnet build Pine.slnx -c Release` as "✅ 0 warnings; only 2 × `NETSDK1147`". That
reading is wrong, and it was re-measured here: `NETSDK1147` is an **error**, not a warning, and it
aborts *restore*, so the solution build terminates in under a second having compiled **nothing**. Every
solution-wide "clean build" claim from iterations 1–4 rests on a command that never built the product.
The per-project builds above are what actually carry the evidence, and they are listed individually for
that reason. Nothing about the code changes; what changes is which command is allowed to be cited.

#### What was built

- **`ImportKeyFactory`** (`Pine.Core/Imports/`, BCL-only so `Pine.Core` keeps zero references) —
  `FromReference(source, reference)` → `r1:` + SHA-256, for sources carrying their own stable identifier;
  `Derive(date, amount, currency, payee, source, rowIndex)` → `h1:` + SHA-256, for those that do not.
  Invariant-culture throughout, no `GetHashCode`, no randomness. **Versioned prefixes**: if the
  derivation ever changes it takes a new prefix, so old rows fail to match the new keys rather than
  matching them by accident — a silent re-match would re-create exactly the data loss being removed.
- **`Transaction.ImportKey`** (nullable) + `AssignImportKey` + `MaxImportKeyLength = 128`, a nullable
  `TEXT(128)` column and an `(AccountId, ImportKey)` index (migration `20260827141109`, reversible `Down`).
- **`ITransactionRepository.GetExistingImportKeysAsync`** — answers only "which of these keys do you
  already have?", `AsNoTracking`, `IN` push-down, empty-set short circuit. This **replaces a full
  `GetByAccountIdAsync` load on every import**, which grew linearly with the account's history.
- **The handler's step 3, rewritten.** Three outcomes, all reported: key already on the account, key
  already seen earlier in this batch, and no key at all. The last is the one worth stating plainly — a
  keyless row is **still imported**, because refusing it would be new data loss, but it is reported as
  `ImportedWithoutKeyCheck` rather than quietly treated as if it had passed a check it never took.
- **`ImportTransactionsResult`** replaces `IReadOnlyList<Transaction>`. The old contract could only
  express what survived; a dropped row left no trace in the return value, in a log, or on screen. The
  new one carries every parsed row in file order with its disposition and a finished user-facing reason.
- **`CsvImporter` assigns a key to every row it produces**, preferring `FromReference` when the profile
  maps an optional `"Reference"` column (a plain extra entry in the existing free-form `ColumnMappings`,
  so no schema change and no migration) and falling back to `Derive`. The row index counts the row's
  position **in the file**, incremented for every record the reader yields including ones later
  rejected — an index into the output list would shift whenever a row ahead of it was rejected,
  renumbering every later row and breaking the match against already-stored keys.
- **A wizard review step** (`IsStep3`, matching the existing `IsStepN` idiom) listing every row with its
  outcome and the handler's reason **passed through verbatim** — the UI derives no prose of its own, so
  there is one source of truth for what happened to a row. It reuses existing design-system resources
  (`PineSurface`, `PineSurfaceElevated`, `PineBorder`, `PineText`, `PineTextMuted`, `PineSuccess`,
  `PineWarning`) and adds none. The wizard auto-closes only when nothing was skipped **and** nothing was
  imported unchecked; anything else stops on the review step and waits for the user.

#### The trap in this ticket — a passing suite that did not test what it appeared to

Two things the ticket's framing assumed turned out not to hold, and both were found by checking rather
than by trusting.

**1. The de-duplication lookup's account scoping was untested, and nothing noticed.** After the four
child workstreams integrated and all 169 tests passed, one further mutation was run that no workstream
had tried: deleting `t.AccountId == accountId` from
`SqliteTransactionRepository.GetExistingImportKeysAsync`. **All 79 integration tests stayed green.**
That mutation is silent cross-account data loss of precisely the TD-03 kind, reached through a
different door — importing a joint statement into a second account would skip every row on the strength
of the *first* account's history, and the new review list would confidently explain why each one was
"already imported". A suite that proves de-duplication works while permitting it to work against the
wrong account is not proving what it claims.
`Import_OfTheSameStatementIntoASecondAccount_IsNotBlockedByTheFirstAccountsKeys` was added, the mutation
re-run (**red**, as it now must be), and the file restored byte-for-byte. That test is test 170.

**2. "The importers must assign the keys" presumed importers that exist.** `CsvImporter` does the real
work. `ExcelImporter` and `PdfImporter` **parse nothing at all** — each opens the file, walks it, and
returns an empty list without ever constructing a `Transaction`. There was no key to assign because
there is no row to key, and no Excel or PDF parser was invented to manufacture the appearance of one.
Both are now documented as stubs, with what a future implementation must do and what happens meanwhile
to any keyless row that escaped. Neither is reachable from the wizard, which hardcodes `CsvImporter`.
Filed as **TD-67** rather than left as an implied capability.

#### How the fix is *proven*, not asserted

- **Four falsification mutations executed and restored**, each verified byte-identical afterwards by
  SHA-256 and `cmp`. M1 (key lookup returns nothing) reddens 5 tests; M2 (in-batch guard) reddens 2;
  M4 (constant key in `CsvImporter`) reddens 2; M3 (account scoping) reddened **nothing until a test was
  written for it**, which is the finding above.
- **Row counts and persisted keys are read from outside EF.** `AtomicityHost.CountRowsRaw` and the new
  additive `ReadImportKeysRaw` issue raw SQL over a keyed SQLCipher connection, so "two rows survived"
  and "the legacy key is NULL" are claims no identity map or change tracker can explain away.
- **The headline regression is asserted the other way round.** Two same-day, same-amount, same-payee
  rows from one file both reach the file — the exact pair the old rule destroyed.
- **Legacy rows are proven inert**, not assumed: a populated pre-`ImportKey` vault migrates, reopens,
  keeps its rows, and their keys read raw-NULL; those NULLs then match no candidate and block nothing.
- **The old test that encoded the bug was deleted deliberately, not quietly.**
  `Handle_WithDuplicates_SkipsDuplicates` asserted that a same-date same-amount row *is* discarded.
  Keeping it green would have meant keeping the defect; it is removed with a header comment recording
  exactly what it used to assert.

#### Fixes made by the coordinator, not by a child workstream

Two breaks left by the previous tree's mid-integration crash were repaired directly, and are recorded
here so the log does not credit them to the workstreams: the `FaultInjectingTransactionRepository`
pass-through for the new repository member (`tests/Pine.Tests.Integration/Atomicity/FaultInjection.cs`),
and the head-migration tripwire in `VaultBackupServiceTests`, retargeted to
`20260827141109_AddTransactionImportKey`. The account-scoping test above was also written at
integration, not by a workstream.

#### Pre-existing defects fixed incidentally

- **`ImportAtomicityTests`' stub importer carried a stale comment** describing "date+amount
  de-duplication" as the behaviour under test. Corrected; the test itself was only updated for the new
  return type.
- **The full-account load is gone from the import path.** Not a correctness fix, but the old dedup read
  every transaction on the account into memory on every import; the replacement is an indexed probe over
  the candidate keys only.

#### New debt opened

- **TD-64** — the currency guard iterates only the rows about to be saved, so a *skipped* row in the
  wrong currency is never reported. Correct in that nothing bad is written, but a file whose first
  import was rejected for currency can look clean on re-import.
- **TD-65** — check-then-insert race. Two concurrent imports of one file can both pass the existence
  probe before either inserts. The `(AccountId, ImportKey)` index is deliberately non-unique, so nothing
  stops the second write. Asserted nowhere; accepted debt, now written down.
- **TD-66** — the derived key's `source` is `ImportProfile.Name`, the only source identifier reachable
  inside `ImportAsync`. Renaming or duplicating a profile therefore changes every derived key, and the
  next import of an already-imported file reports every row as new. Renaming a profile is a data event.
- **TD-67** — `ExcelImporter` and `PdfImporter` parse nothing (above).

#### INCOMPLETE / NOT DONE

1. **The review screen has never been seen.** Its bindings are compiler-checked (compiled bindings are
   on; a deliberately misspelled binding path was confirmed to produce `AVLN2000`, then reverted), but
   `DynamicResource` keys are not compile-checked, and layout, spacing and the skipped-row tint are
   **reasoned, not observed**. No GUI was launched. Nothing here should be read as a claim that the
   screen looks right.
2. **Android and iOS still cannot be compiled locally** (`NETSDK1147`, workloads absent). Unchanged
   since iteration 1. They remain **not compiler-proven**, and TD-52's runtime consequence remains
   inferred rather than observed.
3. **`dotnet build Pine.slnx` still does not build the solution**, and now never did — see the
   correction above. Solution-wide build coverage does not exist locally and, with CI disarmed (§12.12),
   does not exist remotely either.
4. **Concurrency is untested and unhandled** (TD-65). The index is non-unique by choice, and nothing
   asserts what two simultaneous imports do.
5. **No importer other than CSV produces anything** (TD-67), so the "prefer `FromReference`" branch is
   exercised only by CSV's optional `Reference` mapping and by test stubs — no real bank format has been
   run through it.
6. **The key's stability rests on the profile name** (TD-66), which the wizard mints per import as a
   throwaway `ImportProfile.Create("Final Profile", …)`. That happens to be stable today; it is not
   stable by construction.
7. **TD-24 is not fixed.** The wizard still catches `Exception` and renders `ex.Message` raw on the
   import path.
8. **Nothing was committed or pushed.**

### 12.12 Reconciliation pass — re-checking the register against the code

Iteration 4 found that this audit had asserted a corruption path (TD-12) that its **own iteration-2
work had already closed**, and that the claim stood wrong for two further iterations. The lesson
recorded there was that *closing one debt silently changes another's severity, and nothing re-checks*.
This section is that re-check, run deliberately for the first time: every 🔴/🟠 row in §8 and every
§9 blocker still marked open was compared against what the code does today.

**Method.** Source read, with a file and line for every claim. Where a claim could not be checked
cheaply it is listed as unverified rather than guessed — a guess in this register is what created the
TD-12 problem in the first place. Rows already scheduled for imminent work (TD-02, TD-18, TD-19, TD-24,
TD-47, TD-61, TD-62, TD-63) were left alone, as was TD-03, which was changing under this very iteration.

#### Findings that changed a row

| Row | Audit's claim | Verdict | What the code actually does |
|---|---|---|:---|
| **TD-07 / Blocker 6 / roadmap item 3** | CI "now builds the product"; the workflow "has never executed" | **Regressed — materially worse** | `.github/workflows/dotnet-ci.yml` now triggers on `workflow_dispatch:` **only**. Commit `79c5364` deleted `push: branches:[main]` and `pull_request: branches:[main]`, with the comment *"CI automatizado deshabilitado temporalmente para ahorrar minutos de cómputo"*. The three jobs are intact and real; nothing fires them. Iterations 3 and 4 both restated "never executed" without noticing it also can no longer execute on its own. **This item regressed after it was ticked ✅ in iteration 1.** |
| **TD-09 / Blocker 7** | "No financial-invariant tests. Every fix above would ship unverified." | **Now false** | Money-path tests have existed since iteration 2 and several are falsification-proven: `tests/Pine.Tests.Integration/Atomicity/*`, `Repositories/TransactionOwnedCollectionRoundTripTests.cs`, `tests/Pine.Tests.Unit/Domain/MoneyTests.cs`, `AccountTests.cs`, `TransactionSplitTests.cs`, and now the import-dedup suite. **Invalidated by iterations 2, 3 and 4** — each wrote money-path tests while TD-09 stayed 🔴. Now 🟡: what is genuinely missing is coverage for the P0s still open (TD-18, TD-19, TD-02). |
| **TD-43** | "ONNX binaries tracked in git without LFS" | **Now largely false** | `.gitattributes` line 1 tracks `*.onnx` through LFS since commit `bec0887`; all three blobs at HEAD are ~133-byte pointers. Residual: LFS does not rewrite history, so pre-`bec0887` blobs remain in the pack. Now 🟡. |
| **TD-30** | "Missing indexes on `Date` / `AccountId`; **no FK**" | **Severity wrong** | A FK does exist — Budget→Category with `DeleteBehavior.Restrict` (`PineDbContext.cs:173-176`) — and `LinkedTransactionId` is indexed (`:77`). `AccountId` gained a leading-column index via iteration 5's `(AccountId, ImportKey)` (`:98`). What remains: no index on `Date`, no transaction→account FK. |
| **TD-32** | "Category as free string, not FK" | **Severity wrong** | `Budget.CategoryId` is already a real FK. The free string survives only on `TransactionSplit.Category`. |
| **TD-46** | "Relative model paths (CWD-dependent)" | **Severity wrong** | Two of three remain relative (`MlNetCategorizer.cs:34`, `OnnxNlpFieldExtractor.cs:28`); `ReceiptDetector.cs:21` already anchors to `AppContext.BaseDirectory`. Invalidated by the vision work in `a1a440b`/`b6be65c`. |
| **TD-53** | Web head "builds a real SQLCipher context"; "its own comment claims in-memory" | **Wording stale, defect intact** | `Pine.Ui.Web/Program.cs:32` now passes `":memory:"`, so the comment contradiction is gone. The defect survives for a different reason: `PineDbContext.cs:41-52` unconditionally builds a **keyed SQLCipher** connection, and an in-memory *SQLCipher* database still needs the native library WASM does not link. Independently corroborated during this iteration's build: `Pine.Ui.Web`'s one warning names `e_sqlcipher.a` among native references that "won't be linked in". |
| **TD-21** | "Ambiguous dd/MM vs MM/dd parsing" | **Severity understated** | `OnnxNlpFieldExtractor.cs:490-509` added a "SOTA multi-culture cascade" whose comment claims it tries "ISO, then LatAm (dd/MM), then US (MM/dd)". It tries `InvariantCulture` **first**, and `InvariantCulture` *is* MM/dd — so the `es-EC` leg is unreachable for any ambiguous date, and there is still no ambiguity flag. The debt is now hidden behind code that reads as though it were handled. The CSV path is unaffected (`CsvImporter.cs` parses with `TryParseExact` against `profile.DateFormat`). |
| **TD-14** | "`AddConsole` in a WinExe" | **Understated by one** | Two heads, not one: `Pine.Ui.Windows/Program.cs:101` and `Pine.Ui.Macos/Program.cs:72`. |

#### Rows re-checked and confirmed exactly as written

TD-06, TD-11, TD-15, TD-16, TD-17, TD-20, TD-22, TD-25, TD-26, TD-27, TD-28, TD-29, TD-31, TD-33,
TD-34, TD-35, TD-36, TD-37, TD-38, TD-39, TD-40, TD-41, TD-42, TD-44, TD-45, TD-48, TD-49, TD-50,
TD-51, TD-52, TD-54, TD-55, TD-56, TD-57, TD-58, TD-59, TD-60 — all still accurate. Spot evidence:
TD-33's domain events are raised in exactly two places (`Account.cs:32`, `Transaction.cs:91`) and no
dispatcher exists anywhere; TD-54's workflow still enumerates eleven `.csproj` in a bash loop rather
than building `Pine.slnx`; TD-22's confidence score is still a literal `0.1 + 0.2 + 0.25 + 0.2 + 0.15`
tally (`OnnxNlpFieldExtractor.cs:745-764`).

#### No overstated closes

Every ✅ row was checked against the code and each one is real: TD-01 (no `SaveChangesAsync` outside
`EfCoreUnitOfWork.cs:46`), TD-04 (`Transaction.cs:137` no longer validates eagerly; `EnsureValid()` is
the first statement of `SqliteTransactionRepository.SaveAsync`, `:77`), TD-05 (`VaultHeader` carries
`HeaderVersion` plus every KDF parameter), TD-12 (four validators, all wired), TD-13
(`ReconcileOwnedChildren`, `:123-145`), TD-23 (`UnlockViewModel` catches five distinct vault
exceptions). **This is worth stating explicitly**: the failure mode being hunted was an overstated
close, and none was found.

#### §12.7 and §12.9 verified against disk

Every checkable claim in the iteration-3 and iteration-4 logs holds: `EnsureValid()` at the persistence
boundary ✓ · `ReplaceSplits`/`ReplaceLineItems` staging-then-swap (`Transaction.cs:150-172,189`) ✓ ·
`ReconcileOwnedChildren` on the tracked graph ✓ · `ValidationBehavior` registered **before**
`TransactionBehavior` (`PineApplicationServiceCollectionExtensions.cs:47-48`) ✓ · four
`IRequestValidator<T>` registered by reflection via `TryAddEnumerable` ✓ · `CurrencyCodeText.TryNormalise`
used by exactly three validators ✓ · no repository calls `SaveChangesAsync` ✓ · `Pine.Core.csproj` has
zero references ✓.

One drift, cosmetic but worth fixing when those files are next touched: the comments at
`PineApplicationServiceCollectionExtensions.cs:42` and `:55` name tests
`ValidationBehavior_IsRegistered_BeforeTransactionBehavior` and
`EveryRequestValidator_IsRegistered_ByAddPineApplication`; the actual tests are
`ValidationBehavior_ShouldBeRegistered_BeforeTransactionBehavior` (`ArchitectureTests.cs:125`) and
`EveryValidator_InTheApplicationAssembly_ShouldBeRegisteredAsScoped` (`:160`). A reader chasing the
cited names finds nothing.

The one correction this pass makes to §12.9 itself is recorded in §12.11: its verification table cited
`dotnet build Pine.slnx` as passing with two warnings, when that command in fact fails at restore and
compiles nothing.

#### What could not be verified, and why

- **Android / iOS behaviour (TD-52).** Workloads are absent (`NETSDK1147`); the registration diff is
  real and readable, but the runtime consequence is inferred, not observed. Not verifiable on this
  machine at all.
- **Runtime consequences of TD-15, TD-20, TD-34, TD-35, TD-45, TD-49.** These need a running GUI.
  Only the code shape was confirmed.
- **TD-43's historical pack cost.** Measuring it needs a full-history object walk; the LFS-pointer half
  is measured, the history half is not.
- **Whether the CI jobs would actually pass.** The workflow has never run, and now cannot run without a
  manual dispatch, so this is unknown by construction — which is precisely what makes the disarmed
  triggers worse than the register recorded.

#### The process finding

TD-07 regressed **after** being marked ✅, by a deliberate commit unrelated to any remediation
iteration, and three subsequent iterations read past it. TD-09 was false for three iterations because
the work that falsified it was logged in §12 but never reflected back into §8. Both are the same
failure as TD-12's: **§8 is only ever updated by the ticket that owns a row, and no ticket owns the
side effects it has on other rows.** A reconciliation pass of this kind belongs at the end of every
iteration, not once.

### 12.13 Next (after iteration 5) — superseded

> Carried out by iteration 6 (§12.14). Its three warnings all held: the correction-versus-deletion
> question was settled as **void**; the balance-recalculation tension with `Deposit`/`Withdraw` was
> resolved by a third mutator that cannot re-denominate **by construction**, so iteration 4's TOCTOU
> argument survived intact; and the editing traps (TD-59, TD-60) were avoided rather than solved, by
> deferring the edit path — which is why it stayed deferred rather than being quietly dropped
> (TD-68). What it did **not** foresee is recorded in §12.14: the opening balance was never stored,
> so "recompute the balance from its ledger" was arithmetically impossible before this ticket
> created somewhere to put it.


Phase 0 item **8 — TD-02**: update / void transaction commands plus balance recalculation. This is now
the **last original P0 data-integrity finding still open**, and the most consequential of them: TD-01,
TD-03, TD-04, TD-12 and TD-13 are closed, which means Pine can now record transactions atomically,
import them without losing any, and split them correctly — and still cannot **correct a single one of
them**. Any bad record, from any cause, is permanent, and a balance that has drifted cannot be
recomputed from its ledger.

Three things make it harder than the tickets before it, and they should be settled before code is
written:

1. **Correction versus deletion is a domain decision, not an implementation detail.** A finance vault
   generally voids rather than deletes, so history survives an audit. Nothing in the domain currently
   expresses "this transaction was reversed"; adding a delete would be quicker and would throw away the
   property that makes a ledger trustworthy.
2. **Balance recalculation and `Account.Deposit`/`Withdraw` are in tension.** The balance is currently
   maintained incrementally by those two methods, which are also the only mutators and the guarantee
   TD-12's validator ordering rests on (`Account_ShouldNot_ExposeAnyPublicMeansOfChangingItsCurrency`).
   A recalculation path that sets a balance from the sum of its transactions is a *third* mutator, and
   it must not become a re-denomination path — otherwise iteration 4's TOCTOU argument quietly breaks.
3. **Editing a transaction re-enters every trap iteration 3 closed.** `ReplaceSplits`/`ReplaceLineItems`
   mint fresh ids on each detached save (TD-60), and `ReconcileOwnedChildren` infers owned-child state
   from EF's guess (TD-59). Both are tolerable while transactions are only ever created; an update
   command makes them load-bearing.

The seams needed are already in place: `IRequestValidator<T>` for pre-conditions, `ITransactionalRequest`
for atomicity, and `AtomicityHost` for proving rollback from raw SQL. TD-09's remaining gap — no test
covers balance recalculation — should be closed *by* this ticket rather than after it.

### 12.14 Iteration 6 — Phase 0, item 8 (TD-02)

| Field | Value |
|---|---|
| Ticket | Phase 0 item **8** — correction mechanism and balance recalculation (TD-02) |
| Scope decision | **Void, not edit, and not delete.** Taken deliberately before code was written; see "The scope decision" below. |
| Tests | 170 → **240** (141 unit + 99 integration) |
| Schema | `20260828005910_AddTransactionVoidAndAccountOpeningBalance` |
| Status | 🟡 **Partial by design** — the correction and repair halves are closed; the *edit* half is explicitly deferred, see INCOMPLETE |

#### Verification actually executed

| Claim | How it was checked | Result |
|---|---|---|
| Baseline before any change | `dotnet test` on both suites | 90 + 80 = **170 passing** |
| Windows head builds | `dotnet build src/Pine.Ui.Windows` | ✅ 0 warnings, 0 errors |
| Final unit suite | `dotnet test tests/Pine.Tests.Unit` | ✅ **141 passing**, 0 failed |
| Final integration suite | `dotnet test tests/Pine.Tests.Integration` | ✅ **99 passing**, 0 failed |
| Migration is real, not asserted | A vault migrated down to `…_AddTransactionImportKey`, seeded over a raw SQLCipher connection, migrated up through the production `EfCoreVaultMigrator` + `VaultBackupService` path, then reopened | ✅ rows survive; void columns and `OpeningBalanceAmount` come back **raw-NULL** |
| Every "was / was not saved" claim | Fresh `PineDbContext` or raw SQL, never the change tracker that wrote it | ✅ |
| The four load-bearing guards | Falsification: each one removed, suite re-run | ✅ all four turn tests red — table below |
| Android / iOS heads | **Not checked.** Workloads absent (`NETSDK1147`), as in every prior iteration | ⚠️ unknown |
| The GUI | **Not checked.** The void confirmation, the struck-through row and the Settings report were never rendered; only the view models and bindings compile | ⚠️ unverified — see `61-Manual-Test-Plan` §8 |

#### The scope decision, taken before any code

The roadmap phrases item 8 as "Update / void transaction commands + balance recalculation". Those are
three features, not one, and the audit's own §12.13 warned that the first of them re-enters every trap
iteration 3 closed (`ReplaceSplits` minting fresh ids on each detached save, TD-60; owned-child state
inferred from EF's guess, TD-59). The decision taken was **void plus recalculation, and no edit**:

- A finance vault voids rather than edits, so the record of what was originally entered survives an
  audit. An edit destroys the very evidence a correction mechanism exists to preserve.
- Voiding does not touch owned collections at all, so TD-59 and TD-60 stay latent instead of becoming
  load-bearing. An update command would have had to close both first.
- The user is not blocked: a wrong transaction is voided with a reason and re-entered correctly. The
  cost is two rows instead of one, and that cost is visible rather than silent.

The deferred half is recorded as **TD-68**, not quietly dropped.

#### What was built

**Domain.** `Transaction` gained `VoidedAt`, `VoidReason`, a derived `IsVoided`, and
`Void(reason, timeProvider)`. Voiding is deliberately *not* idempotent: a second call throws, because
silently accepting one would let a double-void move a balance twice — the same class of defect as a
double-posted transaction. A rejected void leaves the aggregate completely unmutated.
`TransactionVoidedEvent` is raised for symmetry with `TransactionRegisteredEvent` (still undispatched —
TD-33).

`Account` gained `OpeningBalanceAmount`, a derived `OpeningBalance`, `AdoptOpeningBalance(...)` and
`RecalculateBalance(...)`.

**Persistence.** Four columns and an `(AccountId, VoidedAt)` index.
`ITransactionRepository.GetAmountsInForceAsync` projects the amounts of the non-voided rows;
`GetDistinctCurrenciesInForceAsync` answers the pre-condition the sum depends on.

**Application.** `VoidTransactionCommand` and `RecalculateAccountBalanceCommand`, both
`ITransactionalRequest`, both with an `IRequestValidator<T>` — the seams iteration 4 built, used as
intended and with no new mechanism invented.

**UI.** A per-row *Void* action with a confirmation that requires a reason; voided rows stay in the
list, dimmed and struck through, with a `VOIDED` badge carrying the reason in its tooltip. A **Data
Integrity** section in Settings checks any account's balance against its own ledger and reports the
result as a sentence. Neither path renders `ex.Message`, so neither adds to TD-24.

#### The trap in this ticket — the opening balance did not exist

The ticket reads as "sum the transactions and compare". It is not, and the reason is not in the audit:

> `Account.Create(name, type, initialBalance, …)` accepted an opening balance, folded it into
> `Balance`, and **kept no record of it**. The balance is `opening + Σ transactions`, and `opening` was
> unrecoverable. "Recompute the balance from its ledger" was arithmetically impossible.

Only three things could have been done about it, and two of them are wrong:

1. **Backfill it in the migration** — `Balance_Amount − SUM(TotalAmount_Amount)`. Wrong twice over.
   *Arithmetically*: EF's own scaffold emits `type: "TEXT"` for these columns, so SQLite coerces them
   to REAL to add them, and the figure baked in as the account's origin would be a floating-point
   approximation. *Semantically*, and far worse: it derives the opening balance **from the current
   balance**, which assumes the current balance is correct — the exact assumption recalculation exists
   to stop making. An account whose balance had already drifted would have that drift written in as its
   opening figure, and every later recalculation would confirm the wrong number forever.
2. **Refuse to recalculate accounts that predate the column.** Honest, and useless: those are precisely
   the accounts most likely to have drifted.
3. **What was done.** The column is nullable and unbackfilled. The first recalculation of such an
   account infers the opening figure in exact .NET `decimal`, records it through
   `AdoptOpeningBalance` (which refuses a second call, so the anchor can never be moved to make a
   disagreement go away), and returns `OpeningBalanceWasInferred: true`. The settings screen says so in
   words: *"This first check could not detect drift; run it again from now on and it will."*

That flag is the honest part of the whole feature. Reporting that run as a clean bill of health would
have been a lie, and it is the lie a less careful implementation tells by default. The residual
limitation is **TD-70**.

#### The second trap — a tripwire that was supposed to make this hard

`ArchitectureTests.Account_ShouldNot_ExposeAnyPublicMeansOfChangingItsCurrency` pins Account's entire
public method surface to a literal list, and its own doc-comment says that a new public member
invalidates `ValidationBehavior`'s TOCTOU-free currency read and forces the behaviour-ordering decision
to be revisited. This ticket needed **two** new methods. The tripwire fired exactly as designed.

It was widened, and the widening is justified in writing beside the list. The premise survives because
neither method can re-denominate an account **by construction rather than by check**:
`RecalculateBalance` builds the new `Balance` from the *existing* `CurrencyCode` instance and discards
the argument's; `AdoptOpeningBalance` stores a bare `decimal` and never persists a currency at all.
Deleting either guard would not open the window. Two unit tests prove it with `ReferenceEquals` rather
than value equality — necessarily, because `CurrencyCode` compares structurally, so an equal-but-
distinct instance sails through both guards and only reference identity distinguishes "discarded" from
"validated". So the ordering decision did **not** have to be revisited.

#### The third trap — a transfer is two transactions

Voiding one leg of a transfer and not the other leaves money that left one account and arrived nowhere:
a worse state than the one the user was correcting. The handler voids both legs and adjusts both
balances inside the one transaction, and `VoidTransactionResult` returns both id lists so the caller
knows to refresh more than the row that was clicked. The two links are established by two *different*
mechanisms in `TransferFundsCommandHandler` — the destination leg through the constructor, the source
leg through the reflection call that is TD-26 — so each direction can break independently, and each has
its own test.

#### How the fix is *proven*, not asserted

Four mutations, each applied to green code, suite re-run, then reverted:

| # | Mutation | Expected | Actual |
|---|---|---|---|
| A | Delete the foreign-currency rule from `RecalculateAccountBalanceCommandValidator` | Red | ✅ **3 red** — 2 unit, 1 integration |
| B | Stop adding the linked leg in `VoidTransactionCommandHandler` | Red | ✅ **2 red** — both transfer directions |
| C | `Account.RecalculateBalance`: `Balance = total` instead of rebuilding from the existing currency | Red | ✅ **1 red** — `Account_RecalculateBalance_CannotRedenominateTheAccount` |
| D | Drop `&& t.VoidedAt == null` from `GetAmountsInForceAsync` | Red | ✅ **3 red** — including the void/recalculation agreement test |

The exactness of the sum is pinned separately, by a ledger built so that a `double` gets it wrong: the
test asserts the premise first (`the double sum really is wrong`) so that reshuffling the amounts into
a floating-point-friendly order makes the test say so instead of passing quietly.

#### The defect the review found, and why it was the important one

The unit-test workstream reported that `RecalculateAccountBalanceCommand` had **no currency rule**. It
was right, and it is the only defect in this feature that could have made a balance *worse* while
telling the user it had been verified:

`GetAmountsInForceAsync` returns bare decimals on the reasoning that every transaction on an account
shares the account's currency. All four transaction validators enforce that — but
`VoidTransactionCommandValidator` had *already documented*, in this same iteration, that rows written
before those validators existed can violate it, and refuses to void them. The recalculation path was
written against the optimistic reading of the same fact. A legacy EUR row on a USD account would have
had its raw number folded into a USD total; `RecalculateBalance` would have accepted it, because it
validates the currency of the *total* — which the handler builds correctly — and knows nothing about the
provenance of the numbers inside it. The balance would then be overwritten with a figure that is not a
quantity of anything, and the settings screen would have called it a clean check.

Closed by a validator rule that names every offending currency and **refuses**. Refusing rather than
skipping is deliberate: dropping the odd row from the sum would silently delete a transaction's
contribution to a balance, which is the defect class TD-03 closed on the import path. Falsification A
above is this rule.

The same review raised two other items. One — that the void handler does not call
`Transaction.EnsureValid()` — was checked and is **wrong**: `EnsureValid()` is the first statement of
`SqliteTransactionRepository.SaveAsync`, which is where iteration 3 deliberately put the aggregate
boundary, so every write path is covered including this one. The other was an inaccurate XML doc on
`VoidTransactionResult.AdjustedAccountIds`, corrected.

#### Pre-existing defects found and fixed incidentally

- **A test that had been passing for the wrong reason for two iterations.** EF Core writes a `Guid`
  into a SQLite TEXT column **upper-cased**, and SQLite compares TEXT case-sensitively.
  `ImportKeyMigrationTests` seeded its legacy row with `accountId.ToString()` — lower case. The row
  still *materialises* correctly (`Guid.Parse` ignores case), so every client-side assertion in that
  file was sound; but the **server-side** predicate in `GetExistingImportKeysAsync` could never match
  it. Its `matches.Should().BeEmpty()` — which reads as "a NULL import key is not a wildcard" — was
  therefore vacuous, and would have passed even if a NULL key *did* behave as a wildcard. Fixed: the
  seed is upper-cased, and the assertion is now preceded by a check that the row is visible to a
  server-side predicate at all. The encoding itself is pinned by a new `Data/RawGuidEncodingTests`, so
  a future EF change makes one test fail loudly instead of several going quietly vacuous. **Verified
  empirically, not assumed** — the probe reads the raw cell out of a vault EF itself wrote.
- **A stale tripwire.** `EfCoreVaultMigratorTests.CurrentSchemaVersion` still named
  `…_AddTransactionLineItems` after `…_AddTransactionImportKey` shipped in iteration 5, so for one
  iteration those tests asserted that an upgrade reached a schema that was no longer the head. Nothing
  failed, because `Should().Contain(...)` is satisfied by an earlier id in the applied set. Retargeted,
  with the history recorded on the constant. This is the same failure mode as TD-07 and TD-09: a
  constant that has to be re-examined by a human, and was not.

#### New debt opened

| ID | Debt | Sev | Pri |
|---|---|---|:---:|
| TD-68 | No update/edit path for a transaction. Correction is void-then-re-enter, which is the right default for a ledger but costs two rows and loses the original's `ImportKey` linkage to the re-entered row | 🟡 | P2 |
| TD-69 | A void cannot be undone. A mis-voided transaction can only be re-entered as a new one, losing its id and its import key | 🟡 | P2 |
| TD-70 | Accounts created before this migration have no recorded opening balance, so their FIRST recalculation anchors instead of checking and cannot detect pre-existing drift. Reported honestly, but the drift is unrecoverable | 🟡 | P2 |
| TD-71 | The transactions list has no way to filter or hide voided rows. Correct at today's volumes; a vault with many corrections will read badly | 🔵 | P3 |

#### INCOMPLETE / NOT DONE

- **The edit half of TD-02 is not built**, by decision. See "The scope decision". TD-68.
- **A void cannot be undone.** TD-69.
- **No GUI verification.** Every claim above about the UI is a claim about code that compiles and view
  models that are unit-testable, not about pixels. The void confirmation, the struck-through row, the
  badge tooltip and the Settings report have never been rendered. `61-Manual-Test-Plan` §8 covers them.
- **Android / iOS still uncompiled.** Unchanged from every prior iteration; workloads are absent.
- **TD-59 / TD-60 remain latent.** Voiding does not touch owned collections, so neither became
  load-bearing. They will the day TD-68 is built — which is half the reason it was deferred.
- **Budget "spent" figures are unaffected because they are not implemented** (`BudgetsViewModel`
  hardcodes `SpentAmount = 0m`). When they are, they must exclude voided rows. Not new debt; noted so
  the next person does not have to rediscover it.

#### Reconciliation pass

§12.12's process finding was that **no ticket owns the side effects it has on other rows**, and that a
reconciliation pass belongs at the end of every iteration rather than once. Run again here:

- **TD-02 → ✅ for the correction and repair halves**, 🟡 overall; the edit half is TD-68.
- **TD-09** — the register's remaining gap named "balance recalculation (TD-02)" as uncovered. It is now
  covered, by a falsification-proven integration suite. Rounding/allocation (TD-18) and line-item
  reconciliation (TD-19) remain the only money-path invariants with no test. Row updated.
- **TD-07** — resolved as a **decision** rather than a defect: CI is opt-in by choice, the choice and
  its cost are now written into the workflow file itself, and the row is 🟡 Accepted at P1 rather than
  🔴 Open at P0. The *risk* is unchanged; what changed is that it is no longer recorded as an
  unnoticed regression that three iterations read past.
- **TD-26** — unchanged in severity, but this iteration made it load-bearing in a new place: voiding a
  transfer depends on the source leg's `LinkedTransactionId`, which is written by the reflection call.
  Silently dropping that line would now produce a half-voided transfer, not just a missing link. Both
  directions are covered by tests.
- **TD-31** — unchanged, and this iteration is the clearest illustration of it yet: the decision to
  project amounts and add them in .NET rather than push `SUM` into SQL exists solely because of it, and
  is pinned by a test built to fail if it is ever "optimised" away.
- **Rows re-checked and confirmed unchanged:** TD-06, TD-11, TD-14, TD-15, TD-16, TD-17, TD-18, TD-19,
  TD-20, TD-21, TD-22, TD-24, TD-25, TD-27 … TD-67. Nothing this iteration touched their premises.

### 12.15 Next (after iteration 6) — superseded

Phase 0 items **9, 10 and 11** are what remain before the phase closes:

- **9 — TD-11**, password entropy floor and idle auto-lock. The largest remaining security gap, and the
  one whose absence makes the encryption defeatable at the setup screen rather than at the vault.
- **10 — TD-09's remainder**, money-path coverage for TD-18 (money scale, rounding, `Allocate`) and
  TD-19 (line-item reconciliation). These two are the last money-path invariants with no test, and
  TD-18 in particular should be *designed* rather than back-filled: `Money` has no scale policy at all
  today, so "the sum of splits equals the total" is currently exact only because nothing rounds.
- **11 — TD-47 / TD-48**, remove the seeded fictional $1,000 account and the `CloudSync` setting that
  nothing reads.

Item 9 is the one to take first: it is the only remaining Phase 0 item whose cost grows once real
vaults exist, which is the ordering principle §10 states.

---

### 12.16 Iteration 7 — Phase 0, item 9 (TD-11)

| Field | Value |
|---|---|
| Ticket | Phase 0 item **9** — password entropy floor and idle auto-lock (TD-11) |
| Scope decision | **Entropy floor, not a composition rule. Idle lock, not re-authentication.** The third noun in TD-11's title — re-authentication for sensitive operations — is deliberately not in this iteration; see INCOMPLETE. |
| Tests | 240 → **240 (unchanged)**. **No test was added to the suite and the suite was not run**, by explicit instruction. Behaviour was instead verified by a 25-check harness executed outside the repository — its full output is transcribed below. That is weaker than a committed test, because nothing re-runs it; it is much stronger than the compiler alone, which is all this entry originally rested on. |
| Schema | None. Nothing in this ticket touches persistence. |
| Status | 🟡 **Code complete, behaviour verified out-of-suite** — the floor rejects and accepts as designed, the idle lock has been observed to fire on a fake clock, and the key buffer has been observed to zero. No test in `tests/` covers any of it, so nothing will catch a regression. |

#### Verification actually executed — and what was not

The behavioural rows below were produced by a throwaway console harness referencing `Pine.Core` and
`Pine.Application`, built outside the repository because no test file could be written. **It is not
committed and nothing re-runs it.** Its output is transcribed verbatim rather than summarised, so a
later reader can see exactly what was and was not asserted.

| Claim | How it was checked | Result |
|---|---|---|
| Baseline unit suite, before any change | `dotnet test tests/Pine.Tests.Unit` | ✅ **141 passing** |
| Baseline integration suite, before any change | `dotnet test tests/Pine.Tests.Integration` | 🔴 **98 passing, 1 failing** — `ImportDeduplicationTests.Import_OfAnExtendedStatement_SavesOnlyTheNewRowAndMovesTheBalanceOnlyByIt`, thrown at `AtomicityHost.cs:201`. **Pre-existing** on the commit this iteration started from (`19a1877`) and untouched by this work. Recorded because §12.14 claimed 99/99 and that claim is now stale. Not diagnosed — see TD-74. **[Correction, iteration 9: this row is wrong and could not have been produced as written. `f691ba8` — this very iteration's commit — added four members to `IVaultSession` and touched no file under `tests/`, so `Pine.Tests.Integration` failed to *compile* (8 × CS0535) from that commit onward: not 98 passing and 1 failing, but zero tests executed. At `19a1877`, where the project still builds, the suite runs 99/99 — confirmed over four full runs in a clean worktree, and four more at HEAD after the build was repaired. Baseline text left intact per this document's convention; see §12.20.]** |
| Windows head builds | `dotnet build src/Pine.Ui.Windows` | ✅ 0 warnings, 0 errors |
| macOS head builds | `dotnet build src/Pine.Ui.Macos` | ✅ 0 warnings, 0 errors |
| Web head builds | `dotnet build src/Pine.Ui.Web` | ✅ builds; one pre-existing native-WASM link warning, unrelated |
| Android / iOS heads build | **NOT CHECKED.** The workloads are not installed on this machine (`NETSDK1147`), so `Pine.slnx` cannot be built here at all. Their edits mirror the macOS/Web pattern line for line and were reviewed by reading. | ⚠️ unverified |
| A one-character password is refused — the audit's literal finding | Harness: `"a"` 4.7 bits, `"1"` 3.3, `"x"` 4.7, `" "` 0.0, `""` 0.0 | ✅ all five refused |
| Weak-but-plausible passwords are refused | Harness: `password` 2.0 bits, `P@ssw0rd` 4.0, `aaaaaaaaaaaa` 9.3, `qwerty123456` 10.5, `Password1!` 16.1, `Sunshine!2024` 39.4 | ✅ all six refused, each with the expected `PasswordWeakness` reasons |
| The floor is not a wall — strong passwords pass | Harness: `xK9#mQ2$vL` 65.7 bits, `7bTz!qW4nR#8` 70.3, `gravel-thicket-cinder-9271` 149.6 | ✅ all three accepted |
| Monotonicity: appending a character can never lower the score | Harness: four suffixes on a 65.7-bit base → 72.3, 72.3, 72.3, 78.8 | ✅ the shortest-path property holds |
| `Evaluate(null)` does not throw | Harness | ✅ returns a failing result |
| **The idle lock actually fires** | Harness on a `FakeTimeProvider`, no `Task.Delay` anywhere: unlocked → idle 9 min (still open, 1 min remaining) → `ReportActivity()` → idle 9 min again (still open, so activity really does reset the countdown) → idle 2 min more | ✅ **locked**, `VaultLockReason.IdleTimeout` reported, `Key` null |
| The key is zeroed, not merely dropped | Harness captured the session's internal `_key` array by reflection before the lock and re-read it after | ✅ all 32 bytes zero |
| The strength meter actually renders | **NOT CHECKED.** It compiles; it has never been displayed. `61-Manual-Test-Plan` §9.2, §9.14 and §9.15 cover it. | 🔴 **unverified** |

**The honest reading of that table:** the mechanism has been watched working, but only once, by
hand, from outside the repository. Nothing in `tests/` will notice if it stops working tomorrow —
which is the difference between *verified* and *covered*, and the reason TD-11 stays 🟡 rather than
becoming ✅. The two claims still resting on the compiler alone are the Android/iOS wiring and the
strength meter's rendering. Both, and everything else a harness cannot reach — an existing weak-password
vault still opening, an accepted password still unlocking after a restart, the lock taking the
decrypted data with it — are now written up as `61-Manual-Test-Plan` §9.

#### What was built

**The floor — `src/Pine.Core/Security/`, BCL-only, no package reference.**
`PasswordStrengthPolicy.Evaluate` decomposes a candidate into the cheapest sequence of tokens an
attacker could use to produce it — a shortest-path search over the string — pricing a hit in a small
ranked common-password list at about `log2(rank)`, a repeated run at its base block plus
`log2(repeats)`, an alphabetic, numeric or QWERTY-walk run at about `log2(alphabet) + log2(length)`,
and anything left unexplained at the full per-character pool. The total is the **minimum** over all
decompositions, so adding a pattern can never raise a score — the property a composition rule fails
to have, and the reason `Password1!` cannot buy its way over the floor by appending punctuation.
`PasswordStrengthResult` carries bits, a band and a list of `PasswordWeakness` enum values; it never
quotes the candidate back, and nothing in the file writes to a field, a cache or a log
(`15-Security` §9). Wording lives in the view model, so the domain never dictates UI copy.

**The floor is 60 bits, and its arithmetic is in the constant's own doc comment.** It is derived
from `KdfParameters.Argon2idV1` — m = 64 MiB, t = 3, p = 4, which every vault in existence uses —
not chosen for looking round. One guess moves at least `2 × 64 MiB × 3 ≈ 0.375 GiB` of memory
traffic, so a 1000 GB/s GPU is bandwidth-capped near 2,700 guesses/s, rounded pessimistically **up**
to 3,000; a thousand such GPUs give roughly `2^22` guesses/s; a century is about `2^31.5` seconds,
hence `2^53.5` guesses; add `2^6` for a hundred years of hardware improvement and `2^1` because an
attacker expects to succeed at half the keyspace, giving 60.5, taken as **60**. `MinimumLength = 10`
is *derived* from that (`ceil(60 / log2(94))`), not decreed, so a nine-character password cannot
reach the floor under any arrangement. The comment also records the leverage plainly: doubling
Argon2id's memory to 128 MiB would lower this floor by exactly **one bit**. The password is the cheap
lever — which is the whole thesis of the audit's original finding.

**The lock — `src/Pine.Application/Security/`.** `IVaultSession` gained a `Locked` event,
`LastActivityUtc`, `ReportActivity()` and `Lock(VaultLockReason)`; the old parameterless `Lock()` was
kept as an overload meaning `UserRequested`, which is why `UnlockViewModel`'s six defensive `Lock()`
call sites compile unchanged. `VaultAutoLockService` polls every 15 seconds rather than re-arming a
timer on every keystroke, takes both its schedule and its clock from an injected `TimeProvider` — the
same abstraction `VoidTransactionCommandHandler` already uses, so nothing new was invented — arms
itself on **resolution** rather than requiring someone to remember to start it, and also implements
`IHostedService` so the Windows head's existing hosted-service loop starts it with no new code. One
monitor guards every composite read of `(key, path, unlocked)`, so no reader can observe a half-locked
session; `ReportActivity` is deliberately outside that gate — an `Interlocked.Exchange` on a tick
count — so the input path never contends for a lock.

**The wiring — all five composition roots.** `AddPineVaultAutoLock()` on each; `TimeProvider.System`
added to Web, Android and iOS, which had never registered one; and an explicit
`GetRequiredService<IVaultAutoLockService>()` at startup on the four heads that do not run a
hosted-service loop. `SetupViewModel` now refuses to create a vault unless `MeetsFloor` holds,
re-evaluated inside the command rather than trusted from the live-feedback state, and gained a
confirm-password field: a password mistyped at creation is unrecoverable, because this product has no
recovery phrase. `MainViewModel` handles `Locked`, marshalling through `Dispatcher.UIThread.Post`
because the event is raised on the timer thread, drops the shell's inner page so nothing holding
decrypted data survives the key, and tells the user *why* they were returned to the vault picker —
otherwise an idle lock reads as a crash. `MainWindow` reports activity from tunnelled pointer and key
handlers, so an event that a control marks handled still counts as activity.

#### Defects found while verifying, and fixed

**The derived key was left in a second, unzeroed managed array — found by the harness, and it
contradicted a claim made about this code.** `VaultSession.Unlock` *copies* the key into its own
buffer, and `Lock()` zeroes that copy. Both `UnlockViewModel` and `SetupViewModel` derived the key
into a local `byte[]`, handed it to `Unlock`, and then abandoned it — so a full second copy of the
vault key stayed live on the managed heap for the whole process, untouched by any lock, idle timeout
or vault switch. TD-11's own text is *"the 32-byte key sits in a managed `byte[]` … for the entire
process lifetime"*, so shipping the idle lock while leaving that copy behind would have closed the
finding on paper only. Both call sites now `Array.Clear` the local immediately after handing it over.
The harness check that caught this had been *asserted* to pass during implementation; it could never
have passed, because `Unlock` copies. It is recorded here because that is exactly the failure mode
§12.12 exists to catch — a verification claim nobody could reproduce.

#### A defect found while wiring, and fixed

**`UnlockViewModel` was registered only on the Windows head** (`Pine.Ui.Windows/Program.cs:83`). On
macOS, Web, Android and iOS, `GetRequiredService<UnlockViewModel>()` throws the moment the vault
picker navigates to it: the unlock screen could not be opened at all. It is now registered on the four
missing heads. **Pre-existing and unrelated to TD-11** — but it could not be left, because shipping a
lock feature onto a head with no reachable unlock screen would have converted an idle timeout into a
dead end.

#### Defects found while wiring, and deliberately not fixed

- **`ShellViewModel` is `Transient` on Windows and `Singleton` on the other four heads.** Changing a
  lifetime is not a security fix and would ripple further than this ticket. Its one dangerous
  consequence — a singleton shell retaining child view models full of decrypted data after a lock —
  is contained by nulling the shell's inner page in the lock handler. The divergence itself stands:
  new row **TD-72**.
- **`App.OnFrameworkInitializationCompleted` has no `ISingleViewApplicationLifetime` branch at all.**
  Web, Android and iOS therefore construct no root view and show nothing. That makes the missing
  `ReportActivity()` hook on those three heads moot — there is no `MainWindow` there to hook, and a
  head that never renders has no actively-working user to lock out. This is TD-08's "three platform
  heads are broken" seen from a new angle, and it is sharper evidence than the register currently
  carries.

#### INCOMPLETE — stated rather than buried

1. **No test in `tests/` covers any of this.** By instruction. The behaviour *was* verified — see the
   table above — but by a harness that is not committed and that nothing re-runs, so there is no
   regression net. The `TimeProvider` seam makes the idle-lock test a deterministic, microsecond
   assertion with no `Task.Delay`; the harness is, in effect, that test written in the wrong place.
   Porting it into `tests/Pine.Tests.Unit` is the first item of §12.17 and the reason the status
   stays 🟡.
2. **No re-authentication for sensitive operations** — the third noun in TD-11's own title. It is
   worth little before the envelope key (TD-06) exists.
3. **No lock-on-suspend.** `VaultLockReason.ApplicationSuspended` and `CheckNow()` exist for a UI
   that can observe platform lifecycle; nothing observes it yet.
4. **The idle timeout is not persisted.** `SetIdleTimeout` applies in-process only; nothing writes it
   through `SaveSettingCommand`, and Settings exposes no control for it. The shipped default of ten
   minutes is therefore the only value any user will ever have.
5. **The key is still a managed `byte[]`.** Both copies are now zeroed — the session's on `Lock()`,
   the view models' derived local immediately after `Unlock` — but a compacting GC may already have
   moved either one and the OS may have paged it. This narrows the window; it does not close it, and
   no claim to the contrary is made anywhere. Closing it needs an unmanaged or pinned buffer, which
   belongs to TD-06.
6. **No word list, so passphrases are over-estimated.** `correct horse battery staple` scores far
   above its true ~44 bits. Every limitation of the estimator errs *generous*, which is exactly why
   the floor carries a `2^6` margin rather than sitting at break-even: new row **TD-73**.

#### Rows the register gains

| ID | Debt | Sev | Impact | Fix | Priority | Status |
|---|---|---|---|---|---|---|
| TD-72 | `ShellViewModel` lifetime diverges: `Transient` on Windows, `Singleton` on the other four heads | 🟡 | One view model means two different things depending on platform; a singleton shell outlives the vault it was opened for | Pick one lifetime, in the shared registration TD-29 already calls for | P2 | 🔴 Open |
| TD-73 | `PasswordStrengthPolicy` has no natural-language word list, so multi-word passphrases are over-estimated | 🟡 | The estimator accepts a passphrase weaker than the bits it reports; the 60-bit floor's `2^6` margin is what currently covers this | Embed a word list in `Pine.Core` — needs the BCL-only rule relaxed to permit an embedded resource | P2 | 🔴 Open |
| TD-74 | One integration test is red at `19a1877` and nobody has diagnosed it | 🟠 | §12.14 recorded 99/99; the suite is 98/99. A regression net whose colour nobody reads is not a net | Diagnose `ImportDeduplicationTests.Import_OfAnExtendedStatement_…` — it is TD-03's own coverage | P1 | 🔴 Open |

### 12.17 Next (after iteration 7) — superseded

Phase 0 items **10** and **11** remain, plus a debt this iteration created:

- **First, before either: port the verification harness into `tests/Pine.Tests.Unit`.** It already
  exists and already passes — 25 checks across the estimator, the idle lock and the zeroing — but it
  lives outside the repository, so nothing re-runs it and the next change to `VaultSession` will
  break it silently. The seams were built for exactly this: an injected `TimeProvider`, a pure
  `Evaluate`, an observable `Locked`. This is a transcription job, not a design job.
- **10 — TD-09's remainder**: money-path coverage for TD-18 (money scale, rounding, `Allocate`) and
  TD-19 (line-item reconciliation). Still work to *design* rather than tests to back-fill — `Money`
  has no scale policy at all, so "the sum of the splits equals the total" is exact today only because
  nothing rounds.
- **11 — TD-47 / TD-48**: remove the seeded fictional $1,000 account — the
  `CreateAccountCommand("My First Bank", "USD", AccountType.Bank, 1000m)` call in `SetupViewModel` —
  and the `CloudSync` setting that nothing reads.
- **TD-74**, the red import test, before it is three iterations old and something starts depending on
  its silence.

### 12.18 Iteration 8 — Phase 0, item 10, first half (TD-18, TD-61)

| Field | Value |
|---|---|
| Date | 2026-08-28 |
| Scope | Phase 0 item **10**, the TD-18 half: a scale and rounding policy for `Money`, exact allocation, ISO 4217 membership for `CurrencyCode` — and TD-61, which could not be left open once membership existed |
| Outcome | **TD-18 ✅ closed · TD-61 ✅ closed · TD-09 narrowed to TD-19 only.** 5 new debts opened (TD-75 … TD-79) |
| Tests | 185 + 99 = 284 → **274 + 110 = 384** |
| Schema | **No migration.** Nothing about the storage shape changed; see below |
| State | Working tree only — not committed, not pushed |

#### Verification actually executed

| Check | Result |
|---|---|
| Baseline before any change | ✅ 185 unit, 99 integration, both green |
| `dotnet build` × 8 heads, Release (`Core`, `Application`, `Infrastructure`, `Ui.Avalonia`, `Ui.Windows`, `Ui.Macos`, `Ui.Web`, `Cli`) | ✅ 0 errors; 0 warnings except `Ui.Web`'s pre-existing SDK `.targets` warning, unchanged since iteration 2 |
| `dotnet test tests/Pine.Tests.Unit` | ✅ **274 passed, 0 failed, 0 skipped** |
| `dotnet test tests/Pine.Tests.Integration` | ✅ **110 passed, 0 failed, 0 skipped** |
| Legacy vault tolerance | ✅ proven, not assumed — rows planted by raw SQL over a keyed SQLCipher connection |
| Falsification | ✅ **six mutations, six killed**, table below; green restored after each |
| Android / iOS heads | ❌ **not compiled** — workloads absent (`NETSDK1147`), unchanged since iteration 1 |
| The GUI | ❌ **not rendered.** The currency-picker change and the `{0:N2}` finding are claims about code, not pixels |

#### The design, and where the policy lives

The policy is in **`Money`'s constructor**, and the scale it uses comes from **`CurrencyCode.MinorUnits`**,
which reads an ISO 4217 table embedded in `Pine.Core`. Putting it at the boundaries instead would mean
there is a boundary to forget; putting it in the value object means an out-of-scale amount cannot exist
inside the domain and no aggregate has to defend against one.

The table is embedded rather than read from `RegionInfo` / `NumberFormatInfo.CurrencyDecimalDigits`
deliberately: those are ICU culture data, revised between OS releases and different across platforms, so a
vault's arithmetic would otherwise depend on the machine that opened it.

**What is normalised versus what is rejected is asymmetric, and the asymmetry is the design.** For a
currency with a minor unit, digits below it are sub-unit noise — division residue, never a sum anybody
meant — and are rounded away half-to-even. For a currency with **no** minor unit there is no sub-unit for
noise to occupy, so a fractional yen is not residue but a quantity the currency cannot express, which means
the caller computed it somewhere it should not have: `new Money(10.5m, JPY)` **throws**. Silently making it
10 JPY would hide the arithmetic that produced the half.

That frontier is drawn at the **constructor** — the caller-authored boundary. Results this type computes
itself (`Multiply`, `Allocate`) are not caller-authored, so they round through `Money.Round` and never
throw. A caller that genuinely wants the nearest expressible yen says so by calling `Money.Round` or
`Money.FromRounded`.

**Half-to-even, not half-away-from-zero**: away-from-zero rounds every midpoint in the same direction and
accumulates a drift proportional to the number of operations, which in a ledger is the wrong error to have.

`Allocate` is **largest remainder** over whole minor units: each part takes the truncated units its ratio
earns, and the leftover units — always fewer than the number of parts — go one each to the largest
remainders, ties by position. `new Money(10.00m, USD).Allocate(3)` is `[3.34, 3.33, 3.33]`. Divide-and-round
is the defect this exists to prevent: three shares of 3.3333… each round to 3.33 and sum to 9.99, which
breaks `Transaction.ValidateSplits` for a number that is not a quantity of money. Negative amounts are
allocated by magnitude and re-signed, so the two directions mirror exactly; a zero ratio yields a zero part
and can never receive a leftover unit.

**`decimal` is still the type.** Scaled integers are TD-31 and were not touched. `ToMinorUnits()` is a
projection used for allocation arithmetic, not a change of representation.

#### Backward compatibility, and why there is no migration

A vault in the field may hold a currency outside the table (the old rule was `^[A-Z]{3}$` and nothing more)
or an amount outside its currency's scale. **Neither can be migrated**: there is no correct value to rewrite
an unknown currency to, and rounding stored history is destroying evidence. So the read path is deliberately
tolerant and the write path is deliberately strict.

The seam is that **EF Core binds the private parameterless constructors** of `Money` and `CurrencyCode` and
writes the backing fields directly, so materialisation executes neither rule. That is a fact about EF's
constructor selection rather than about Pine, which is exactly why it is now a test rather than an
assumption — an EF upgrade that changed the selection rule would turn "opens fine" into "throws on open"
with no other signal. `Data/LegacyMoneyToleranceTests` plants `Balance_Currency='ZZZ'`, a three-decimal USD
amount and a fractional JPY balance by raw SQL and asserts each one materialises, each test first asserting
that the validating path *would* have refused the same value so that it cannot pass vacuously.

**The one read path that does run code was a real defect.** `Budget.Limit`'s currency is the only money
column mapped with an explicit `HasConversion` (the TD-33 divergence), and its converter called the
**validating** constructor. Left alone, this ticket would have made a vault holding a legacy budget currency
unopenable. It now calls `CurrencyCode.FromStoredValue`. Falsification M5 is that line, and no other test in
the suite catches it.

No schema changed, so **no migration was generated**.

#### Call sites whose behaviour changed

Every `new Money(...)` in the solution now rounds. The ones where that is a real change of behaviour:

- **`CsvImporter:137`** — the only site whose input is genuinely arbitrary. A statement row of `12.345` used
  to be stored verbatim and is now stored as `12.34`. This is the intended fix and also the largest blast
  radius in the ticket.
- **`AddTransactionCommand:52,77,78`** and **`RecordTransactionCommand:36,42`** — a plugin or a future caller
  passing an unrounded amount now has it rounded rather than persisted at full precision.
- **`RecalculateAccountBalanceCommand:137,143`** — the ledger sum is added in exact .NET `decimal` (TD-31,
  deliberately not pushed into SQL) and *then* wrapped in a `Money`, so a legacy vault of out-of-scale rows
  now yields a **rounded** recalculated balance where it previously yielded an exact one. The sum itself is
  unchanged; only the figure written back is normalised.
- **`Account.OpeningBalance`** — now built with `Money.FromRounded`, because `OpeningBalanceAmount` is a bare
  `decimal?` column that does not pass through `Money` on the way out. Without this, a legacy JPY account
  with a fractional opening balance would have thrown *every time it was displayed*. A read-path projection
  must not be able to break a screen. Recorded as TD-77.
- **`CurrencyCode`'s membership rule** changes `CreateAccountCommand:28` and `CreateBudgetCommand:31` from
  "accepts anything three letters long" to "throws `ArgumentException` from inside the handler". Neither has
  a validator (TD-62 / TD-63), so this is a wrong-layer throw rendered verbatim by the view models — a
  widening of TD-24 that this ticket created and did not fix. Recorded as **TD-78**.
- **`CreateAccountViewModel.Currencies` offered `"BTC"`**, which is not an ISO 4217 code. After this change
  that picker entry could not have succeeded: a shipped UI affordance that always fails. Removed. Pine has no
  crypto-asset model — no divisibility, no rate source — so offering one was a promise the domain could not
  keep. TD-76.
- **Four hand-rolled negations** (`AddTransactionCommand:92`, `VoidTransactionCommand:168`, and two in
  `TransferFundsCommand`) now use unary `-`. `TransferFundsCommandHandler` additionally derives all four of
  its figures from one normalised `Money` instead of re-constructing from `request.Amount` four times, so
  they cannot disagree about scale.
- **`TransactionSplit.CopyOf` / `TransactionLineItem.CopyOf`** rebuilt the nested `CurrencyCode` with the
  validating constructor. On a legacy aggregate that would have thrown while merely *copying* a value it had
  just read. They now use `FromStoredValue`.

#### Falsification

Six mutations, each applied to green code, both suites re-run, then reverted and re-run green.

| # | Mutation | Result |
|---|---|---|
| M1 | `MidpointRounding.ToEven` → `AwayFromZero`, in the constructor and in `Round` | ✅ **10 red** — 8 unit (incl. `Money_UsesBankersRounding_NotAwayFromZero`, `Money_RespectsAThreeDigitCurrency`, `Round_IsTheExplicitWay…`) + 2 integration (`AnAccountBalance_IsPersistedAlreadyNormalised`, `ALegacyRowAndANewRow_Coexist…`) |
| M2 | Delete `Allocate`'s largest-remainder distribution loop | ✅ **11 red** — 10 unit including both property sweeps, + `ATransactionTotalAndItsSplits_ArePersistedAlreadyNormalised` |
| M3 | Disable the ISO membership check in both the constructor and `TryCreate` | ✅ **8 red** — 4 unit (`CurrencyCode_Rejects_AWellShapedStringThatIsNotACurrency`, `TryCreate_AgreesWithTheConstructor…`) + 4 integration (all three `CurrencyMembershipValidationTests` rows, plus the legacy-tolerance test's own anti-vacuity guard) |
| M4 | Remove the constructor's normalisation (`Amount = amount`) | ✅ **12 red** — 10 unit + 2 integration |
| M5 | `Budget.Limit`'s EF converter back to the validating constructor | ✅ **1 red** — `ABudgetDenominatedInAnUnknownCurrency_StillMaterialises`. The only test that catches it, and the reason it was written |
| M6 | `CurrencyCodeText.TryNormalise` back to its own copy of the shape rule | ✅ **3 red** — `AddTransaction_WithAWellShapedNonCurrency_IsRefusedByTheValidator` for all three codes. **Zero unit tests red**, which is the honest shape of TD-61: it is a wiring defect, visible only through the pipeline |

**No mutant survived.** M5 and M6 were added precisely because M1–M4 left two seams untested; without them
both would have been surviving mutants rather than killed ones.

#### INCOMPLETE / NOT DONE

1. **TD-19 (line-item reconciliation) is untouched**, by instruction. Phase 0 item 10 is therefore half
   closed; TD-09 is narrowed, not closed.
2. **`{0:N2}` in four `.axaml` files is still wrong for JPY and KWD.** Diagnosed, not fixed — there are no
   Avalonia converters in the project, so fixing it means introducing that mechanism, which is a UI change
   with no test harness behind it, inside a ticket about the domain. **TD-75.**
3. **`CreateAccountCommand` / `CreateBudgetCommand` have no validator**, so this ticket made two paths throw
   a domain exception where they previously accepted bad input silently. **TD-78.** The seam to fix it exists.
4. **Android and iOS still cannot be compiled locally** (`NETSDK1147`). Unchanged since iteration 1.
5. **No GUI was rendered.** The picker change and the formatting finding are unverified visually.
6. **The ISO table is a point-in-time transcription** (list one, 2024-06-25) with no update mechanism and no
   test that it matches the standard — only that it is internally consistent and that every member is
   constructible. A code the standard adds later will be rejected until someone edits the file.
7. **`Money` supports scales 0–4 only.** Anything else throws from `PowerOfTen` / `MinorUnit`. That covers the
   whole ISO table today and is a deliberate closed set, not an oversight.
8. **`Allocate` uses `long` minor units and `Int128` intermediates.** No test explores the overflow boundary;
   the amounts involved are far outside any plausible vault, but the claim is untested rather than proven.
9. **TD-74 was not investigated** by iteration 8. Both suites are green on this machine at this commit,
   which does not by itself resolve §12.17's claim that a test was red at `19a1877`. **[Iteration 9
   investigated it and found the claim false; see §12.20. The green suites iteration 8 observed were green
   only because iteration 9's fake-repair had already landed in the working tree.]**
10. **Nothing was committed or pushed.**

#### Reconciliation pass

- **TD-18 → ✅ closed.** TD-61 → ✅ closed; it could not have been left open, because membership validation
  would have made the validator's private copy of the rule strictly weaker than the rule it stood in for.
- **TD-09** — rounding and allocation are now covered, falsification-proven. Only TD-19 remains.
- **TD-33** — unchanged in severity but newly load-bearing: `Budget.Limit`'s divergent `HasConversion` was
  the single place where the read path executed domain validation, and it is now the single place where the
  read path executes `FromStoredValue`. The divergence is covered by a test that goes red if it is
  "unified" back onto the validating constructor.
- **TD-31** — unchanged, and reinforced: `RecalculateAccountBalanceCommand` still sums in .NET rather than
  SQL, and the new round-trip test has to read decimal columns as raw TEXT, which is only necessary because
  of it. The round-trip test also documents that EF's `decimal` encoding drops trailing zeroes, so `10.00`
  lands on disk as `10.0` — the absence of the third decimal, not the presence of the second, is what that
  assertion actually discriminates.
- **TD-24** — measurably worse on two paths; TD-78 records it rather than burying it.
- **TD-12** — its validators are strictly stronger now: `"ZZZ"` was previously accepted by them.
- **Rows re-checked and confirmed unchanged:** TD-02, TD-03, TD-06, TD-11, TD-13 … TD-17, TD-19, TD-20 …
  TD-30, TD-32, TD-34 … TD-60, TD-62 … TD-74.

### 12.20 Iteration 9 — Phase 0 item 10 closed (TD-19, TD-09), TD-74 reclassified

| Field | Value |
|---|---|
| Date | 2026-08-28 |
| Scope | Phase 0 item **10** — TD-19 (line items unreconciled) and TD-09's remainder. Plus §12.17's prerequisite (port the TD-11 harness) and TD-74. |
| Outcome | **TD-19 ✅ closed · TD-09 ✅ closed · TD-74 ✅ closed and reclassified · TD-11 covered.** Phase 0 item 10 is done; only item 11 remains. |
| Tests | 240 → **429** (310 unit + 119 integration), 0 failed, 0 skipped. |
| Schema | One migration, `20260828201103_AddTransactionLineItemKind` — a nullable `Kind` column on `TransactionLineItems`. Nullable is load-bearing, not laziness; see below. |
| State | Working tree only — **not committed, not pushed**. |

#### TD-74 was misdiagnosed, and the real defect was worse

§12.16 recorded "98 passing, 1 failing" at `19a1877`. That could not have happened. `f691ba8` — iteration
7's own commit — added `Locked`, `LastActivityUtc`, `ReportActivity()` and `Lock(VaultLockReason)` to
`IVaultSession` and touched **no file under `tests/`**. The unit suite survived because its doubles are
NSubstitute proxies that regenerate; the integration suite has two hand-written fakes — `TempVaultSession`
in `AtomicityHost.cs` and `FakeVaultSession` in `ExportVaultBackupCommandHandlerTests.cs` — which stopped
satisfying the interface. **`Pine.Tests.Integration` had not compiled since `f691ba8`: 8 × CS0535, zero
tests executed.** Not a red test — no test at all.

A clean worktree at `19a1877`, where the project still builds, runs 99/99 — confirmed over four full runs,
including the named test in isolation, and four more at HEAD after the build was repaired.
`AtomicityHost.cs:201` at that commit is `ExecuteScalar()` on a raw SQLCipher connection: the shape of a
transient file lock, not of a failed assertion, which would have thrown at the test's own line. **The flake
is not reproducible and is not resolved; what is resolved is the far larger defect hiding behind the
report.** Both fakes now mirror the real `VaultSession`'s semantics, so a double cannot pass where
production would fail. Falsification: stash the two files → 8 × CS0535; restore → 99/99.

The lesson generalises past this row: **a suite that does not build reports no colour, and three iterations
read the absence of red as green.** This is the same failure mode §12.12 exists to catch.

#### TD-11's harness is now in the suite

§12.17's first item. All 25 checks ported to `tests/Pine.Tests.Unit/Security/` (44 tests). Every value
transcribed in §12.16 reproduced **exactly** — the bit scores, the `PasswordWeakness` sets, the idle-lock
sequence on a `FakeTimeProvider` with no `Task.Delay`, the 32 zeroed bytes. Nothing needed relaxing, so
TD-11's behaviour is now covered rather than merely witnessed. The zeroing needed no reflection: the getter
on `IVaultSession.Key` returns the live buffer, so capturing it before `Lock()` observes the wipe in place.
The two rows §12.16 marked NOT CHECKED — the Android/iOS heads and the strength meter's rendering — are
still unchecked, and still belong to `61-Manual-Test-Plan` §9.

#### TD-19 — the difference is represented, not tolerated

A receipt's product lines almost never sum to what was paid: tax, tip, discounts, container deposits and
cash rounding sit in between. Enforcing an exact sum naively would have made the scanner refuse most real
receipts; tolerating a difference would not be an invariant. So every component of the total is a line, and
`LineItemKind` says which kind: `Product`, `Tax`, `Tip`, `Discount`, `Deposit`, `Rounding`, `Unattributed`.
The invariant stays **exact** and the receipt stays storable.

**There is no tolerance constant, and the derivation is that none is available.** Every `Money` is
normalised to a whole number of minor units by its own constructor (TD-18, §12.18), and the sum is taken
over stored line totals rather than recomputed from quantity × unit price. A sum of exact multiples of the
minor unit is an exact multiple of the minor unit, so the largest representable residue is **zero**. An
epsilon here would not absorb arithmetic noise — there is none — it would absorb tax, one to four orders of
magnitude larger, and an amount the user is entitled to see named. `Rounding` and `Unattributed` exist so
nobody has to lie about what a charge was.

**Sign is not part of the kind.** A `Discount` is a line whose amount runs opposite to the total's
direction; the sum invariant is what makes that work. Encoding "subtract this one" in the kind would give
two places that decide a line's contribution and let them disagree.

**`Kind` is nullable, and null is not a member of the enum.** Null means "written before this model
existed", and such a set is **not** validated. That data is incomplete, not contradictory, and the
alternatives were to invent the missing adjustment line — fabricating evidence — or to refuse it, which
would have made every pre-existing scanned receipt impossible to void or correct, silently undoing TD-02's
correction path with a new invariant. The hole is as narrow as it can be made: `AddLineItem` takes a
non-nullable kind, so the only way back into that state through the domain is to pass a null to
`ReplaceLineItems` deliberately, and the only caller that does is the repository merge path preserving what
it has just read.

**The UI half.** `ScanReceiptViewModel` recomputes the difference as the grid is edited — through `Money`
once an account is selected, so the figure the user sees is normalised to the same scale the validator
compares against, and a receipt cannot look reconciled and then be refused.
`RecordDifferenceAsAdjustment` inserts the outstanding amount as one adjustment line of a chosen kind:
**the user chooses what to call it, never how much it is.** `Product` is absent from that picker — the
difference between what was bought and what was paid is by definition not a product. Adjustment rows are
exempt from the quantity × unit price auto-calculation, which would otherwise drive a tax line to zero the
instant it was created.

#### A pre-existing defect the invariant exposed

`ScanReceiptViewModel.SaveTransactionAsync` sent line items **positive against a negative total**. That set
could never have reconciled under any rule — which is precisely why nothing had ever checked it. Line items
are now negated with the total, so they sum to it.

#### Falsification — 5 mutations, 5 killed

| # | Mutation | Red |
|---|---|---|
| M-A | Neuter the line-item sum invariant | 3 unit |
| M-B | Delete the legacy null-kind exemption | 2 unit + 2 integration |
| M-C | Delete the UI reconciliation guard | 1 unit (`ScanReceiptReconciliationTests`) |
| M-D | Send line items unnegated — the pre-existing sign defect | 1 unit |
| M8 | Drop `li.Kind` from the repository's **detached merge** | 2 integration — **survived until this iteration**; see below |

**M8 is the hole this iteration found in its own work.** `SaveAsync` branches on
`ReferenceEquals(existing, transaction)`. Every existing test loads through the repository inside the same
context and gets the **tracked** instance, so none of them ever reached the branch that rebuilds a
`LineItemDefinition` — the only place in the solution where a kind can be silently dropped, because it is
the only place a line item is reconstructed from one that already exists. Dropping it flattens every line
to `Product`, and worse, it flattens a legacy null — which asserts the transaction reconciles and turns a
merge into a throw. A correction path that worked would start failing on exactly the data the legacy seam
exists to protect. Two tests now cover it, both reading on one context and saving on another, with an
explicit `ReferenceEquals(...).Should().BeFalse()` so the test cannot silently stop exercising the branch
it names.

`CS0162` is an error in this repository, so a mutation that leaves unreachable code does not compile. M-B
and M-C delete their blocks rather than short-circuiting them. Green was restored and re-verified after
every mutation.

#### Verification actually executed

| Check | Result |
|---|---|
| `dotnet build -c Release` × `Core`, `Application`, `Infrastructure`, `Ui.Avalonia`, `Ui.Windows`, `Ui.Macos`, `Ui.Web`, `Cli` | ✅ 0 errors, 0 warnings on all eight |
| `dotnet test tests/Pine.Tests.Unit` | ✅ **310 passed**, 0 failed, 0 skipped |
| `dotnet test tests/Pine.Tests.Integration` | ✅ **119 passed**, 0 failed, 0 skipped |
| Android / iOS heads | ❌ **not compiled** — workloads absent (`NETSDK1147`), unchanged since iteration 1 |
| Any GUI rendered | ❌ **not observed.** The reconciliation panel and the adjustment picker compile and are covered by view-model tests; they have never been displayed. `61-Manual-Test-Plan` needs a §10 for them. |

#### INCOMPLETE — stated rather than buried

1. **No GUI was rendered.** The reconciliation affordance is the entire argument for why this invariant is
   liveable, and that argument currently rests on view-model tests and on reading.
2. **The uncommitted work of this iteration was destroyed mid-verification and reconstructed.**
   `git checkout -- <file>` was used to revert two falsification mutations. It restores from the index, and
   this iteration was never staged, so it discarded the working-tree versions of `Transaction.cs` and
   `ScanReceiptViewModel.cs` wholesale. Both were rebuilt by replaying the edit scripts recorded in the
   implementing agent's transcript, then reconciled by hand where an abandoned intermediate design
   (`MarkAsPredatingKinds`) had been captured instead of the final one. **The recovered files are a
   reconstruction, not the original bytes.** What backs them: all eight heads build clean, both suites
   return the exact pre-loss counts (310 / 119), and all five mutations above were run against the
   reconstruction rather than against the lost original. What does not back them: nobody has diffed the
   reconstruction against what was lost, because it no longer exists. **Falsification must use file copies,
   never `git checkout`, on an unstaged tree.**
3. **The flake behind TD-74's original report is unresolved** — not reproduced in eight full runs, and no
   product defect found behind it. If it is real it is in test infrastructure: a raw reader opened against a
   file EF still holds pooled connections on.
4. **TD-19's extractor half is partial.** The OCR extractor already computed the line-items-to-total ratio
   and spent it on a confidence score; it now feeds adjustment extraction, but no receipt corpus was run
   through it. Its accuracy is unmeasured.
5. **Nothing committed or pushed.**

#### Reconciliation pass

- **TD-19 → ✅ closed.** **TD-09 → ✅ closed** — rounding, allocation and line-item reconciliation were its
  last two gaps, and both are now falsification-proven. Phase 0 item 10 is complete.
- **TD-74 → ✅ closed and reclassified** — from "a red test" to "a suite that had not compiled for two
  iterations". §12.16's baseline row is annotated in place rather than rewritten, per this document's
  convention.
- **TD-11 → ✅** for everything a test can reach; the manual rows stay in `61-Manual-Test-Plan` §9.
- **TD-02** — newly load-bearing in a place iteration 6 could not have anticipated: the legacy null-kind
  exemption is what keeps a pre-existing scanned receipt voidable. M-B is the test that would notice if it
  were removed.
- **TD-24 / TD-78** — unchanged, and this iteration adds a third wrong-layer throw: `ReplaceLineItems`
  raises `InvalidOperationException` carrying user-facing prose in its message.
- **TD-75** — sharper now. The receipt confirmation screen shows a difference figure formatted by the same
  `{0:N2}` that ignores minor units, so a JPY receipt would display a reconciliation figure with two
  decimals that cannot exist.
- **Rows re-checked and confirmed unchanged:** TD-03, TD-06, TD-13 … TD-17, TD-20 … TD-23, TD-25 … TD-73,
  TD-76, TD-77, TD-79.

### 12.19 Next (after iteration 8) — superseded

Phase 0 items **10** (remainder) and **11** remain. Item 10 was closed by iteration 9; see §12.20.

- **10 — TD-19**: line-item reconciliation against the transaction total. Deliberately left out of
  iteration 8, which touched `Money` and did not touch `Transaction.ReplaceLineItems`/`AddLineItem`.
  It is now the ONLY money-path invariant with no test, and it lands on top of the two latent debts
  iteration 6 avoided making load-bearing (TD-59, TD-60).
- **11 — TD-47 / TD-48**: remove the seeded fictional $1,000 account in `SetupViewModel` and the
  `CloudSync` setting nothing reads.
- **TD-78, before it is a user-visible defect.** Iteration 8 created it: two commands now throw a
  domain exception on a bad currency because they have no validator. The seam to fix it has existed
  since iteration 4 and the fix is two small classes.
- **TD-75**, the `{0:N2}` formatting. Iteration 8 gave the domain a correct answer for how many
  decimals a currency has and the view still ignores it.
- ~~**TD-74**, still undiagnosed and now four iterations old.~~ **Diagnosed and closed in iteration 9 — see §12.20. It was not a red test; it was a project that had not compiled since `f691ba8`.**

### 12.21 Next (after iteration 9) — superseded

Phase 0 has **one** item left. It was closed by iteration 10; see §12.22.

- **11 — TD-47 / TD-48**: remove the seeded fictional $1,000 account — the
  `CreateAccountCommand("My First Bank", "USD", AccountType.Bank, 1000m)` call in `SetupViewModel` — and
  the `CloudSync` setting that nothing reads. Small, and the last thing standing between the tree and a
  closed Phase 0.
- **Commit this work.** Three iterations of unstaged changes now sit in one working tree, and §12.20's
  INCOMPLETE 2 is the direct cost of that.
- **TD-78**, before it is user-visible: `CreateAccountCommand` and `CreateBudgetCommand` still have no
  validator, so an invalid currency throws a domain exception the view model renders verbatim. The seam has
  existed since iteration 4 and the fix is two small classes.
- **TD-75**, the `{0:N2}` formatting. The domain has known how many decimals a currency has since
  iteration 8, and four views still do not ask.
- **Re-arm CI (TD-07).** Every claim in §12.18 and §12.20 was verified on one machine, by hand. The
  workflow that would re-run them on every push has had its triggers removed since `79c5364`.

### 12.22 Iteration 10 — Phase 0 item 11 (TD-47, TD-48), and TD-80 found alongside

| Field | Value |
|---|---|
| Date | 2026-08-28 |
| Scope | Phase 0 item **11** — TD-47 (seeded fictional account) and TD-48 (dead `CloudSync` option). TD-80 was found while reading the same screen and closed in the same pass. |
| Outcome | **TD-47 ✅ · TD-48 ✅ · TD-80 ✅ opened and closed · TD-81 🔴 opened.** The last Phase 0 item is done. |
| Tests | 310 → **313** unit; integration unchanged at **119**. Total **432**. |
| Schema | None. |
| State | Working tree only — **not committed, not pushed**. |

#### What was removed

- `SetupViewModel.cs` — the `CreateAccountCommand("My First Bank", "USD", AccountType.Bank, 1000m)` send;
  the `IsLocalOnly`, `IsCloudSync` and `TelemetryOptIn` properties; both `SaveSettingCommand` sends; and the
  four usings that died with them.
- `SetupView.axaml` — the "Local + Cloud Sync (Coming Soon)" radio with its border and caption, and the
  telemetry consent checkbox.

**The surviving "Local Only" radio became a `TextBlock`.** A radio button with a `GroupName` and no peer is
a choice control with nothing to choose: permanently checked and impossible to uncheck. The block keeps its
visual weight and its privacy sentence — which is true — without pretending a decision is being offered.

#### TD-80 — the finding this ticket did not go looking for

The audit records `CloudSync` (TD-48) but never recorded the checkbox sitting three lines below it. Pine had
an **enabled** control asking the user to consent to "sending anonymous usage telemetry", wrote
`System.Telemetry` on first run, and **contains no telemetry code at all** — nothing reads that setting and
nothing transmits anything. That is the same defect class as TD-48 and a worse instance of it: the cloud
radio was at least disabled, so it read as a roadmap promise; an enabled checkbox reads as a live choice,
and a user who ticks it believes data is leaving their machine. In a product whose entire argument is that
data never leaves the device, this was the worst possible place to be inaccurate. Removed, not deferred.

#### The zero-account question, and why nothing was added

Removing the seeded account leaves a fresh vault with **no accounts**, so the real risk in this ticket was
turning honest emptiness into a dead end. It is not one, and the evidence was checked from both directions —
independently by the reviewer before the implementing agent reported, so the conclusion is not one party's
word:

- `DashboardView.axaml:32` carries a `+ New Account` button bound to `ShowCreateAccountDialogCommand`,
  **unconditional** — not gated on `Accounts.Count`. It is the first-account path, and it was already there.
- `BalanceWidgetView.axaml:40` already renders "No accounts yet." on `IsVisible="{Binding !Accounts.Count}"`.
- `AddTransactionViewModel.cs:108` and `ScanReceiptViewModel.cs:138` both guard `Accounts.First()` behind
  `if (Accounts.Any())`; `ImportWizardViewModel.cs:146` uses `FirstOrDefault()` and null-checks before use.
- `DashboardViewModel.LoadDataAsync` accumulates with `foreach` and never divides by a count;
  `BudgetsViewModel.ProgressPercentage` guards `LimitAmount > 0`.

**So no empty state was added.** The gap between "stop inventing data" and "redesign onboarding" is real,
and this ticket stayed on the near side of it. What a fresh vault does *not* have is an empty state on the
account pickers in the receipt, add-transaction and import screens — they render an empty combo box with no
explanation. That is pre-existing, it is not a crash, and it is not TD-47; it is noted here so the next
reader does not have to rediscover it.

#### TD-81 — the reason these tests assert structure instead of behaviour

The natural test is "completing setup sends no `CreateAccountCommand` and no `SaveSettingCommand`", written
against a substituted `IMediator` with `DidNotReceive()`. It cannot be written. `InitializeVaultAsync`
resolves `VaultManager` first, which is `sealed`, has no interface, hard-codes
`LocalApplicationData/Pine` in its constructor, and writes `vaults.json` to the real disk — so the happy path
cannot execute in a test without mutating the machine running the suite. **Verified directly, not taken on
the implementing agent's word:** `VaultManager.cs:9` is `sealed class VaultManager` with no `IVaultManager`
anywhere in `src/`, and lines 17–18, 58, 60 and 74 are the constructor's path and the real writes. Every
pre-existing test of this view model stops at an early validation failure for the same reason.

The three tests therefore assert **type-level dependency** via `NetArchTest`, the idiom already used in
`ArchitectureTests.cs`. For "this command is never sent" that is strictly stronger than a `DidNotReceive()`
on one execution: it proves the send is impossible by construction rather than absent on one path. What it
does **not** prove is that setup still completes correctly — no test covers that, and none did before. The
seam is TD-81.

#### Falsification — 3 mutations, 3 killed, all re-run by the reviewer

Each mutation was applied by the reviewer independently of the implementing agent, restored with `cp` from a
backup (never `git checkout`, for the reason in §12.20 INCOMPLETE 2), and the file confirmed byte-identical
afterwards.

| # | Mutation | Red |
|---|---|---|
| M1 | Re-add the `CreateAccountCommand` send, with the usings it needs | `SetupViewModel_ShouldNot_HaveDependencyOnCreateAccountCommand` — and only it (1 failed, 312 passed) |
| M2 | Re-add `SaveSettingCommand("System.StorageMode", "LocalOnly")` | `SetupViewModel_ShouldNot_HaveDependencyOnSaveSettingCommand` — and only it (1 failed, 312 passed) |
| M3 | Re-add the `IsCloudSync` / `TelemetryOptIn` properties **alone**, with no command send | `SetupViewModel_ShouldNotExpose_CloudSyncOrTelemetryProperties` — and only it (1 failed, 312 passed) |

M3 matters because it is the one mutation M2's test cannot see: a build that re-exposes the controls without
re-adding the writes would put the promise back on screen while leaving the register looking closed.

#### Verification actually executed — every row re-run by the reviewer

| Check | Result |
|---|---|
| `"My First Bank"`, `"System.StorageMode"`, `"System.Telemetry"`, `IsCloudSync`, `TelemetryOptIn`, `"Coming Soon"` in `src/` | ✅ **0 occurrences each** |
| `dotnet test tests/Pine.Tests.Unit` | ✅ **313 passed**, 0 failed, 0 skipped |
| `dotnet test tests/Pine.Tests.Integration` | ✅ **119 passed**, 0 failed, 0 skipped |
| `dotnet build -c Release` × 8 heads | ✅ 0 errors. 0 warnings on seven; `Ui.Web` carries its one pre-existing `NativeFileReference` warning |
| M1 / M2 / M3 re-run by hand | ✅ each killed by exactly the named test, tree restored byte-identical, 313 green |
| Android / iOS heads | ❌ **not compiled** — workloads absent (`NETSDK1147`) |
| Any GUI rendered | ❌ **not observed.** The removals are claims about XAML markup, not about pixels. `61-Manual-Test-Plan` needs a first-run row: a fresh vault must reach the dashboard, show "No accounts yet.", and create an account through `+ New Account`. |

#### INCOMPLETE — stated rather than buried

1. **No GUI was rendered.** Nobody has seen the first-run screen without its cloud block and its telemetry
   checkbox, nor a freshly created vault arriving at an empty dashboard.
2. **No test covers that setup still completes** — see TD-81. The three new tests prove absence, not success.
3. **The account pickers have no empty state.** Pre-existing, deliberately out of scope, recorded above.
4. **Nothing committed or pushed.** Four iterations of work now sit unstaged in one tree.

#### Reconciliation pass

- **TD-47 → ✅. TD-48 → ✅. TD-80 → ✅ opened and closed. TD-81 → 🔴 opened.**
- **Phase 0's roadmap is now fully worked, but Phase 0 is not clean.** Item 2's encrypted export still has
  no UI and no restore path (TD-56), and **item 3 regressed after it was ticked**: `79c5364` removed the
  workflow's `push`/`pull_request` triggers, so CI has never run and Android/iOS have never compiled. Every
  claim in §12.18, §12.20 and §12.22 was verified on one machine, by hand. Ticking item 11 does not make the
  phase green; it makes item 3 the only structural thing left in it.
- **TD-24 / TD-78** — unchanged. `SetupViewModel` no longer sends the two commands whose validators were
  missing, which narrows TD-78's blast radius by exactly one caller without fixing it.
- **TD-29** — unchanged, but this ticket is a small argument for it: the settings written on first run were
  duplicated nowhere, so removing them touched one file. The composition roots are not so lucky.
- **Rows re-checked and confirmed unchanged:** TD-01 … TD-46, TD-49 … TD-79.

### 12.23 Next

Phase 0's list is worked through. What remains before this branch is defensible:

- **Commit.** Four iterations, fifty files, nothing staged. §12.20 INCOMPLETE 2 is what that costs when it
  goes wrong, and it already did once.
- **TD-07 — re-arm CI.** Now the largest single risk on the branch. Everything above was verified on one
  machine by hand; the workflow that would re-run it on every push has been disarmed since `79c5364`, and
  the Android and iOS heads have never been compiled by anything.
- **TD-74's sibling risk** — the integration suite silently stopped building for two iterations and nobody
  noticed. CI is the control that would have caught it in minutes.
- **TD-78**, then **TD-75**, then **TD-81**: a missing validator that turns a bad currency into a raw domain
  exception on screen; four views that still format money without asking the domain how many decimals it
  has; and the `VaultManager` seam that makes first-run behaviour testable.
- Then **Phase 1** proper, whose first row — structured logging with redaction (TD-14, TD-25) — is the one
  that makes everything after it debuggable.
