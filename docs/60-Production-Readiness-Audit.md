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
| Remediation | Iterations 1–2 closed Phase 0 items 1–4. Test suite 28 → **124**. See **§12**. |
| Last updated | 2026-08-26 |
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
- 🟠 **P0** Transaction currency never checked against account currency (TD-12). `AddTransactionCommandHandler` builds `Money` from the request, **saves the transaction**, then calls `account.Deposit(...)` which throws on mismatch inside `Money.Add`. Result: an orphaned ledger entry with no matching balance movement. `TransferFundsCommandHandler` is the only handler that validates currency up front.
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
> Rows **TD-52 … TD-58** were discovered *during* remediation and were not in the
> original audit.

| ID | Debt | Sev | Impact | Action | Pri | Status |
|---|---|---|---|---|---|:---:|
| TD-01 | No unit of work; transient DbContext per repository | 🔴 | Non-atomic financial writes | Scope-per-request + `IUnitOfWork` | P0 | ✅ **Done** |
| TD-02 | No update/delete/void for transactions | 🔴 | Errors permanent; balances unrepairable | Add mutation + recalculation commands | P0 | 🔴 **Open** |
| TD-03 | Dedup on (date, amount) only, silent | 🔴 | Legitimate transactions lost | Stable import key + review list | P0 | 🔴 **Open** |
| TD-04 | `AddSplit` validates eagerly | 🔴 | Multi-split throws | Validate at aggregate boundary | P0 | 🔴 **Open** |
| TD-05 | No KDF version marker beside the salt | 🔴 | Parameter change bricks all vaults | Versioned vault header | P0 | ✅ **Done** |
| TD-06 | Key = password-derived key; no envelope | 🔴 | No password change, no biometric | Wrapped data key | P0 | 🔴 **Open** |
| TD-07 | CI builds only test projects | 🔴 | Broken heads ship undetected | Full-solution build matrix | P0 | 🟡 **Partial** |
| TD-08 | Web/Android/iOS reference a non-existent namespace | 🔴 | 3 of 6 heads uncompilable | Fix or remove; add to CI | P0 | 🟡 **Partial** |
| TD-09 | No financial-invariant tests | 🔴 | No regression net on money | Money-path test suite | P0 | 🔴 **Open** |
| TD-10 | No backup before migration | 🟠 | Unrecoverable data loss | Pre-migration copy + export | P0 | 🟡 **Partial** |
| TD-11 | No password policy; no auto-lock | 🟠 | Key exposed on idle machine | Entropy floor + idle lock | P0 | 🔴 **Open** |
| TD-12 | Currency not validated against account | 🟠 | Orphaned ledger entry | Validation behaviour | P0 | 🔴 **Open** |
| TD-13 | `CurrentValues.SetValues` drops owned collections | 🔴 | Latent silent data loss on edit | Tracked-graph updates | P0 | 🔴 **Open** |
| TD-14 | No `ILogger`; `AddConsole` in a WinExe | 🟠 | No incident evidence | Structured file logging + redaction | P1 | 🔴 **Open** |
| TD-15 | Diagnostics screen shows mock data | 🟠 | Misleads during incidents | Wire to real sinks or remove | P1 | 🔴 **Open** |
| TD-16 | ML model plaintext, cross-vault, outside vault | 🟠 | Privacy-claim violation | Per-vault encrypted model | P1 | 🔴 **Open** |
| TD-17 | No optimistic concurrency token | 🟠 | Lost updates | Row version on aggregates | P1 | 🔴 **Open** |
| TD-18 | No `Money` scale or rounding policy | 🟠 | Sub-cent drift; undefined split remainders | Normalise + `Allocate` | P1 | 🔴 **Open** |
| TD-19 | Line items unreconciled against total | 🟠 | Internally contradictory records | Reconcile in confirmation UI | P1 | 🔴 **Open** |
| TD-20 | No receipt duplicate detection | 🟠 | Double-counted expenses | Content-hash check | P1 | 🔴 **Open** |
| TD-21 | Ambiguous dd/MM vs MM/dd parsing | 🟠 | Silently misdated transactions | Locale-driven order; flag ambiguity | P1 | 🔴 **Open** |
| TD-22 | Confidence score is an arbitrary tally | 🟠 | Unearned user trust in OCR | Propagate real confidence or relabel | P1 | 🔴 **Open** |
| TD-23 | Migration failure reported as wrong password | 🟠 | Undiagnosable support cases | Split auth and schema phases | P1 | ✅ **Done** |
| TD-24 | Raw `ex.Message` surfaced in UI | 🟡 | Path/internals leakage | Map to user-facing messages | P2 | 🔴 **Open** |
| TD-25 | Unredacted crash.log / OCR text to console | 🟡 | Sensitive data outside vault | Redact and relocate | P1 | 🔴 **Open** |
| TD-26 | Reflection sets `LinkedTransactionId` | 🟠 | Silent runtime break on rename | Domain method or single aggregate | P1 | 🔴 **Open** |
| TD-27 | No configuration system | 🟠 | Nothing tunable; no env separation | `IOptions` + appsettings | P1 | 🔴 **Open** |
| TD-28 | Service locator in view models | 🟠 | Hidden deps; UI touches EF | Typed VM factories | P1 | 🔴 **Open** |
| TD-29 | Five duplicated composition roots | 🟠 | Divergent lifetimes per platform | `AddPine*()` extensions | P1 | 🔴 **Open** |
| TD-30 | Missing indexes on `Date` / `AccountId`; no FK | 🟠 | Scans at scale; orphan risk | Add indexes + FK | P2 | 🔴 **Open** |
| TD-31 | Decimal stored as TEXT in SQLite | 🟠 | Wrong results when comparison reaches SQL | Scaled-integer converter | P1 | 🔴 **Open** |
| TD-32 | Category as free string, not FK | 🟡 | Rename orphans history | Store `CategoryId` | P2 | 🔴 **Open** |
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
| TD-43 | ONNX binaries tracked in git without LFS | 🔵 | Permanent clone cost | Git LFS or fetch-on-build | P3 | 🔴 **Open** |
| TD-44 | Windows-only native runtimes | 🟡 | Scanning impossible off Windows | Per-RID runtimes; state support | P2 | 🔴 **Open** |
| TD-45 | Hardcoded English UI; no localisation | 🟡 | Costly retrofit across 26 views | `.resx` + `IStringLocalizer` | P2 | 🔴 **Open** |
| TD-46 | Relative model paths (CWD-dependent) | 🟡 | Feature silently off; stray files | Anchor to BaseDirectory / vault | P2 | 🔴 **Open** |
| TD-47 | Seeded fictional $1,000 account | 🔵 | Fabricated data in a finance app | Real onboarding step | P1 | 🔴 **Open** |
| TD-48 | `CloudSync` setting read by nothing | 🔵 | Onboarding promises absent feature | Remove until sync exists | P2 | 🔴 **Open** |
| TD-49 | No packaging, signing or update channel | 🟠 | No way to ship or patch | Installer + signing + updater | P1 | 🔴 **Open** |
| TD-50 | No file-type or size validation on imports | 🔵 | Parser DoS / hang | Magic-byte + size cap | P2 | 🔴 **Open** |
| TD-51 | Draft comments and `<!-- asdasda` in source | 🔵 | Signals unreviewed code | Clean up; enforce in review | P2 | 🟡 **Partial** |
| TD-52 | Web/Android/iOS roots omit `TimeProvider.System` + 9 other registrations Windows has | 🔴 | Heads compile, then throw on the first command dispatched | Shared `AddPine*()` roots (TD-29) | P1 | 🔴 **Open** |
| TD-53 | `Pine.Ui.Web` builds a real SQLCipher context whose WASM native lib is not linked | 🔴 | Web head fails at first DB access; its own comment claims in-memory | Decide the Browser story or wire a real provider | P1 | 🔴 **Open** |
| TD-54 | CI enumerates projects instead of building `Pine.slnx` | 🟠 | A newly added project is silently outside CI — the TD-07 drift class again | Build the solution, or assert slnx coverage in CI | P1 | 🔴 **Open** |
| TD-55 | `TreatWarningsAsErrors` promotes NuGet audit warnings (NU19xx) to build errors | 🟡 | A new CVE advisory turns CI red with no code change | Set `NuGetAuditMode`; triage advisories separately | P2 | 🔴 **Open** |
| TD-56 | No restore-from-backup path; `ExportVaultBackupCommand` has no UI entry point | 🟠 | Recovery requires the user to copy files by hand | Restore command + UI affordance | P1 | 🔴 **Open** |
| TD-57 | `IVaultMigrator` lives in `Pine.Core` because Infrastructure cannot see Application | 🟡 | A persistence-orchestration port sits in the domain project | Add the ProjectReference, then move the port | P2 | 🔴 **Open** |
| TD-58 | `AddTransactionViewModel` / `CreateAccountViewModel` take an unregistered `Action onComplete` ctor parameter | 🟡 | Blocks `ValidateOnBuild`; the container cannot self-check at startup | Typed VM factories (TD-28) or a registered callback | P2 | 🔴 **Open** |

---

## 9. Production blockers

Each of the following should prevent a release to users who will store real
financial data. Strict but not manufactured — every one has an identified file and
a concrete failure mode.

1. ✅ **TD-01** — ~~No transactional boundary.~~ **Closed.** Scope-per-request DI, `IUnitOfWork`, and an opt-in `TransactionBehavior`; repositories no longer self-commit. Rollback proven by tests that flush real rows inside the transaction and assert the database comes back empty.
2. 🔴 **TD-02** — No correction mechanism. Any bad record, from any cause, is permanent.
3. 🔴 **TD-03** — Silent import data loss. Real transactions vanish without notice.
4. 🔴 **TD-04** — `AddSplit` throws. A shipped feature is broken on its intended use.
5. 🟡 **TD-05 ✅ / TD-06 🔴** — ~~Unversioned KDF~~ and no envelope key. The versioned header now records the KDF id and every parameter, so the irreversible half of this blocker is closed; envelope encryption (TD-06) is not built, so password change and biometric unlock remain impossible.
6. 🟡 **TD-07 / TD-08** — CI now builds the product and the three heads no longer reference a non-existent namespace, but **the workflow has never executed** and **Android/iOS were never compiled** (workloads absent locally). Regression protection is written, not yet demonstrated.
7. 🔴 **TD-09** — No financial-invariant tests. Every fix above would ship unverified.
8. 🟡 **TD-10** — Encrypted pre-migration backup is implemented and verified, so a failed upgrade no longer destroys the only copy. **Recovery is still manual**: no restore path and no UI entry point for the export command.
9. 🔴 **TD-11** — No password policy, no auto-lock. The encryption is defeatable by the setup screen.
10. 🔴 **TD-12 / TD-13** — Currency unvalidated; owned collections dropped on update. Two concrete corruption paths reachable from the UI.
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
- [x] 🟡 **3.** CI: build the whole solution; restore `/tests/` to `Pine.slnx`; fix or delete the broken heads (TD-07, TD-08) — **CODE DONE, NOT DEMONSTRATED.** `Pine.slnx` clean, heads fixed, three-job workflow written. Workflow never run; Android/iOS never compiled.
- [x] ✅ **4.** Scope-per-request DI + `IUnitOfWork`; strip `SaveChangesAsync` from repositories; one DB transaction per command (TD-01 — also fixes the transient-disposable leak) — **DONE.** All 11 repository `SaveChangesAsync` calls removed, 4 MediatR-bypassing write paths converted, `ValidateScopes` on in all five roots.
- [ ] 🔴 **5.** Fix `AddSplit`; fix `CurrentValues.SetValues` to update tracked graphs (TD-04, TD-13)
- [ ] 🔴 **6.** MediatR validation behaviour; validate currency against account (TD-12)
- [ ] 🔴 **7.** Rewrite import dedup on a stable key with a user-facing review list (TD-03)
- [ ] 🔴 **8.** Update / void transaction commands + balance recalculation (TD-02)
- [ ] 🔴 **9.** Password entropy floor and idle auto-lock (TD-11)
- [ ] 🔴 **10.** Money-path test suite covering all of the above — written *alongside*, not after (TD-09)
- [ ] 🔴 **11.** Remove the seeded fictional account; remove or wire the `CloudSync` option (TD-47, TD-48)

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

### 12.6 Next

Phase 0 item **5 — TD-04 + TD-13**: fix `AddSplit`, and fix `CurrentValues.SetValues` so updates
write the tracked graph instead of silently dropping the `OwnsMany` `Splits` and `LineItems`
collections. TD-13 is latent only because no update path exists yet; item 8 (TD-02, update/void
commands) is what makes it reachable, so TD-13 must be fixed before it.
