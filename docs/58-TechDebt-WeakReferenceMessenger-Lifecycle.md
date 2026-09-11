# 58 · Tech Debt — `WeakReferenceMessenger` Recipient Lifecycle

| | |
|---|---|
| **Status** | Non-normative — technical-debt record |
| **Reconciled** | 2026-09-07 · Rewritten after TD-34 was implemented. The defect described in §3 no longer exists; §2 and §5 are now a record of what was done and why, and §8 is ticked against `tests/Pine.Tests.Unit/ViewModels/MessengerLifecycleTests.cs`. TD-35 (fire-and-forget initialisation) is still open and is tracked separately. |
| **Type** | Technical debt / correctness |
| **Area** | `Pine.Ui.Avalonia` (shared UI, all OS heads) |
| **Severity** | Was medium; the correctness half is fixed |
| **Owner** | UI / Frontend |
| **Related** | `27-Development-Guidelines.md`, `39-Performance.md`, `20-Coding-Standards.md` |

> **A note on citations.** Every file reference in the original version of this document was a
> `File.cs:NN` line number, and by the time anyone read it again not one of them was right — the
> §2.2 table pointed at a sender in `DiagnosticsViewModel` that had been deleted, and its §2.3
> table's line numbers were off by hundreds. Iteration 21 settled the convention this rewrite
> follows: **cite by quotation**. A quoted line survives every edit above it and fails loudly, by
> not being findable, when the thing it describes is actually gone.

---

## 1. TL;DR

The app broadcasts three domain events (`DataChangedMessage`, `ReceiptCapturedMessage`,
`RetakeRequestedMessage`) between view models over
`CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default`.

**Until TD-34 it had three compounding defects**, all of them consequences of one omission —
nothing ever unregistered:

1. Three recipients registered with a lambda that captured `this`, which defeats the *weak* in
   `WeakReferenceMessenger` and made those view models immortal.
2. No recipient ever deregistered. Page view models are transient, so every visit to a screen left
   another live recipient behind. One broadcast was then handled *N* times, where *N* was the number
   of times the user had ever opened that screen.
3. One handler was `async void` in everything but name, so a failure inside it was an unobserved
   task fault rather than an in-app error.

**What TD-34 changed.** Every consumer is now an `ObservableRecipient` implementing
`IRecipient<T>`, there is not one `Register` call left in `src/`, and registration is driven by
`IsActive`, which the *navigator* flips as a page becomes and stops being the current one. Point 3's
handler is a `Receive` method that starts its work behind a local try/catch.

**What TD-34 did not change.** The fire-and-forget `_ = SomethingAsync()` calls in view-model
constructors are TD-35's subject and are untouched here, apart from the one this work created
(`ProcessCapturedReceiptAsync`, which is wrapped precisely so it is not one of them).

---

## 2. How messaging is used

### 2.1 Messages

- `ViewModels/Messages/DataChangedMessage.cs` — "the data store changed, refresh".
- `ViewModels/Messages/ReceiptCapturedMessage.cs` — carries `MemoryStream`s of a captured receipt.
- `ViewModels/Messages/RetakeRequestedMessage.cs` — the user wants to retake a photo.

### 2.2 Senders (`.Send(...)`) — unchanged by TD-34

Senders were never the problem and none of them was touched. **The table below corrects two errors
in the previous version**: it listed a `DiagnosticsViewModel` sender that does not exist (that file
contains no messenger reference of any kind — `grep -c Messenger DiagnosticsViewModel.cs` returns
`0`), and it omitted `SettingsViewModel` and `TransactionsViewModel` entirely.

| File | Message | Quote the send sits under |
|---|---|---|
| `AddTransactionViewModel.cs` | `DataChangedMessage` | followed by `_onComplete?.Invoke();` |
| `CreateAccountViewModel.cs` | `DataChangedMessage` | after `await _mediator.Send(new CreateAccountCommand(` |
| `ScanReceiptViewModel.cs` | `DataChangedMessage` | followed by `_navigationService.NavigateTo(_viewModels.CreateTransactions());` |
| `SettingsViewModel.cs` | `DataChangedMessage` | `// The dashboard and the balance widget are showing the old figure.` |
| `TransactionsViewModel.cs` | `DataChangedMessage` | in `ConfirmVoidAsync`, under `// THE RELOAD USED TO BE HERE TOO, AND IT RAN TWICE (TD-34).` |
| `ScanReceiptViewModel.cs` | `RetakeRequestedMessage` | in `private void Retake()` |
| `CropReceiptViewModel.cs` | `RetakeRequestedMessage` | in `private void Cancel()` |
| `CropReceiptViewModel.cs` | `ReceiptCapturedMessage` | `// Navigate to ScanReceiptViewModel and send the modified stream` |
| `LiveScannerViewModel.cs` | `ReceiptCapturedMessage` | ×2, in `CaptureAndNavigateAsync` and `CaptureAndNavigateCroppedAsync` |

**Four of these nine are a navigate-then-send pair**, and that turned out to be the single most
constraining fact in the whole remediation. See §6.2.

### 2.3 Recipients — before and after

| Type | Message | Before TD-34 | After |
|---|---|---|---|
| `DashboardViewModel` | `DataChangedMessage` | `Register` with `(r, m) => ((DashboardViewModel)r).Receive(m)` — no capture, but never unregistered | `ObservableRecipient`, also cascades `IsActive` to its widgets |
| `TransactionsViewModel` | `DataChangedMessage` | same correct pattern, same missing unregistration | `ObservableRecipient` |
| `BalanceWidgetViewModel` | `DataChangedMessage` | same | via `DashboardWidgetViewModelBase`, driven by the dashboard |
| `RecentTransactionsWidgetViewModel` | `DataChangedMessage` | same, and it did not even declare `IRecipient<T>` | now declares it; driven by the dashboard |
| `ScanReceiptViewModel` | `ReceiptCapturedMessage` | **captured `this`, and `async void`** | `Receive` → `ProcessCapturedReceiptAsync` (try/catch) |
| `CropReceiptViewModel` | `ReceiptCapturedMessage` | **captured `this`** (wrote `_originalStream` and `PreviewImage`) | `Receive` |
| `LiveScannerViewModel` | `RetakeRequestedMessage` | **captured `this`** (called `StartScanning()`) | `Receive`; also unregisters in `Dispose` |
| `BudgetsViewModel` | `DataChangedMessage` | declared `IRecipient<DataChangedMessage>`, had a `Receive`, and **nothing registered it** — dead code | `ObservableRecipient`, so the interface it always declared now does something |

> **A correction to the previous version's note.** It said "view-model DI lifetimes are declared in
> each OS head's composition root, e.g. `src/Pine.Ui.Windows/Program.cs:68-80` … Every head
> duplicates this list". That has not been true since iteration 22. All five heads now call
> `PineCompositionRoot.Build(...)` and the registrations live once, in
> `src/Pine.Ui.Avalonia/Composition/PineCompositionRoot.cs`. The *requirement* the note was
> protecting still holds and is still met: TD-34 changed no head. The reason is simply better than
> it was — there is now only one list to not change.

---

## 3. The problems, and what closed each one

### P1 — Closures capturing `this` defeat the weak reference

`Register<TMessage>(recipient, handler)` holds the **recipient** weakly but stores the **handler**
strongly. A handler that closes over `this` therefore gives the process-wide messenger a strong,
transitive reference to the view model, and the "weak" guarantee is gone. `LiveScannerViewModel`'s
was the worst of the three: the immortal object owned a camera.

**Closed by** deleting every manual registration. An `IRecipient<T>` method cannot capture the
recipient in a delegate, because there is no delegate — the toolkit registers the interface.

### P2 — No deregistration → duplicate recipients and duplicate side effects

Page view models are `AddTransient`, so navigation created a new instance that registered again
while the old one stayed subscribed. Navigate to the scanner five times, capture one receipt, run
the OCR pipeline five times.

**Closed by** `PageActivation`, called from the `CurrentPage` property hooks of both navigators. The
outgoing page is deactivated (`IsActive = false` → `UnregisterAll`) and disposed if it is
`IDisposable`; the incoming page is activated.

### P3 — `async void` message handler

`ScanReceiptViewModel` registered `async (r, m) => await ProcessReceiptImageAsync(...)`. The
messenger invokes handlers synchronously and discards what they return, so the `Task` was never
observed.

**Closed by** `Receive(ReceiptCapturedMessage)`, which starts `ProcessCapturedReceiptAsync` — a
private `async Task` whose own `try`/`catch` reports through `UserFacingError.Report` and sets
`ErrorMessage`. The wrapper exists rather than a bare discard because `ProcessReceiptImageAsync`
assigns half a dozen properties *before* its own `try` opens; a throw from those would otherwise
still escape.

### P4 — Lifetime mismatch

"Who should receive this message" is really "the currently-visible screen", and nothing modelled
that. It does now: `IsActive` **is** that model, and navigation is what sets it.

---

## 4. Symptoms this removed

- A receipt captured once and processed once, not once per past visit to the scanner.
- One list reload per change instead of several. This also caught a second, unrelated instance of
  the same waste: `TransactionsViewModel.ConfirmVoidAsync` broadcast `DataChangedMessage` and then
  immediately `await LoadDataAsync()`, while being a recipient of that very message — so voiding a
  transaction reloaded the list twice. The broadcast stays (other screens need it); the
  self-refresh is gone.
- No retained `ScanReceiptViewModel` / `CropReceiptViewModel` / `LiveScannerViewModel` after
  navigating away — and, for the scanner, the camera is released at that moment rather than
  whenever the view happens to unload.
- Budget bars that refresh when a transaction is added, which they never did before.

---

## 5. The options as they were weighed

### Option A — `ObservableRecipient` + activation — **chosen and implemented**

Consumers derive from `ObservableRecipient` and implement `IRecipient<T>`; `IsActive` registers and
unregisters them. See §6 for what was actually built, which differs from the sketch below in one
important respect.

### Option B — `IDisposable` view models + navigation-owned disposal

Make consumers `IDisposable` and call `UnregisterAll(this)` in `Dispose()`, then have navigation
dispose the outgoing page.

Rejected as the primary mechanism, but **half of it was kept**: navigation does dispose the outgoing
page, because `LiveScannerViewModel` owns a camera and unregistering it is not enough. (The previous
version described this option in terms of `MainViewModel.NavigateTo<T>`. That generic overload was
replaced in iteration 24 by `INavigationService.NavigateTo(ObservableObject page)`, which is what
the implemented design hooks into.)

### Option C — minimal hotfix

Rewrite the three capturing handlers, de-`async void` the fourth, wire or delete
`BudgetsViewModel`'s orphan interface. Not needed; A was done instead, and it subsumes all three.

---

## 6. What was actually built

### 6.1 The pieces

- **`Services/PageActivation.cs`** (new) — `Deactivate(page)` and `Activate(page)`, the two halves of
  a page change. Deactivate unregisters *then* disposes, in that order, so a broadcast arriving
  mid-teardown cannot reach a half-disposed recipient.
- **`MainViewModel` and `ShellViewModel`** — both implemented `INavigationService` when this was
  written, and both got the same pair of hooks on their `CurrentPage` property. *(Since TD-121,
  2026-09-11, the shell implements a separate `IShellNavigator` instead: sharing one interface is what
  let the container hand every in-shell page the window's navigator. The hooks are unchanged.)* They are on the *property*, not inside
  `NavigateTo`, because the vault-lock path assigns `shell.CurrentPage = null!` directly and never
  calls `NavigateTo`; a retirement step living in the navigator would simply be skipped there.
- **`ShellViewModel.OnDeactivated`** — the shell is both a page of `MainViewModel` and a navigator of
  its own, so being deactivated has to reach the page it is showing. It assigns
  `CurrentPage = null!`, which runs the same hook every other page change uses.
- **`DashboardViewModel.OnActivated` / `OnDeactivated`** — the twelve widgets are built with `new`
  inside `InitializeWidgets()` and are reachable from nowhere else, so only the dashboard can drive
  their `IsActive`.
- **`DashboardWidgetViewModelBase`** now derives from `ObservableRecipient` rather than
  `ObservableObject`. Only two of the twelve widgets consume messages; moving the shared base was
  chosen over a second base class or over those two losing the six layout properties. A widget that
  implements no `IRecipient<T>` registers nothing, so the other ten are unaffected.

### 6.2 The constraint that decided *where* activation happens

This document's original Option A proposed driving `IsActive` from the view lifecycle — an attached
behaviour on `Loaded` / `Unloaded`. **That would have silently broken the receipt pipeline.**

Four of the nine send sites navigate and then send, on the next statement, to the page they have
just navigated to. `LiveScannerViewModel` even says so in a comment:

> `// Navigate first so CropReceiptViewModel is created and registers the message handler`

A recipient activated when its *view* loads is not registered yet at the moment of that send, and
the message is dropped with no error anywhere. Activation therefore happens **inside the
`CurrentPage` setter**, so the guarantee those four senders already rely on still holds: when
`NavigateTo` returns, the new page is registered.
`A_message_sent_on_the_statement_after_NavigateTo_reaches_the_new_page` is the test that fails if
anyone relaxes this.

### 6.3 One generator detail worth recording

CommunityToolkit.Mvvm 8.4.2 emits **both** `OnCurrentPageChanging(value)` and
`OnCurrentPageChanging(oldValue, newValue)` for an `[ObservableProperty]` partial property — but the
generated setter invokes the two-parameter form as `OnCurrentPageChanging(default, value)`. The
"old value" it offers is always `null`. The implementation therefore uses the **one-parameter**
hook, which runs before the backing field is assigned, and reads the outgoing page from
`CurrentPage` itself.

### 6.4 `LiveScannerView.OnUnloaded` was kept

It still calls `vm.Dispose()`. That is now belt-and-braces — navigation disposes too, and `Dispose`
is idempotent via `_isDisposed` — but unloading is not always a navigation (the window closing, a
`ContentControl` re-template, anything that does not go through `INavigationService`), and the call
is cheap.

---

## 7. Verification

`tests/Pine.Tests.Unit/ViewModels/MessengerLifecycleTests.cs` — seven tests, all driving the real
`ShellViewModel.NavigateTo` rather than setting `IsActive` by hand, because what is new here is not
that CommunityToolkit works but that *something flips the flag at the right moment*:

| Test | What it pins |
|---|---|
| `A_page_navigated_away_from_is_no_longer_a_messenger_recipient` | §8's third criterion, directly |
| `Three_visits_to_the_scanner_process_one_captured_receipt_exactly_once` | the headline defect; before TD-34 the count was three |
| `A_message_sent_on_the_statement_after_NavigateTo_reaches_the_new_page` | the §6.2 ordering constraint |
| `Navigating_away_from_the_live_scanner_disposes_it` | asserted through both unregistration *and* `StartScanning` becoming inert, so it says something about the camera and not only the messenger |
| `The_dashboard_widgets_activate_and_deactivate_with_the_dashboard` | the nested-activation risk §9 named |
| `The_budgets_page_now_actually_receives_DataChangedMessage` | a behaviour the app never had |
| `The_real_container_graph_activates_and_retires_pages_the_same_way` | the same guarantee through `PineCompositionRoot.Build(PineHeadProfile.Windows)`, so lifetimes and activation are checked together |

Not automated: the profiler measurement below. It is a statement about the GC, and the assertion
that replaces it — that the recipient is unregistered the instant the user navigates away — is
strictly stronger for the behaviour anyone cares about.

---

## 8. Acceptance criteria (Definition of Done)

- [x] No message handler captures `this` — there is no handler delegate left at all;
      `grep -rn "WeakReferenceMessenger.Default.Register" src/ --include=*.cs` returns nothing.
- [x] No `async void` message handlers.
- [x] Every recipient deregisters when its page is no longer current.
- [x] A single broadcast is handled exactly once per logically-active screen.
- [x] `BudgetsViewModel`'s `IRecipient<DataChangedMessage>` is wired.
- [x] The "process once" test passes.
- [ ] The **memory** test. Not done, and deliberately not: it is a dotMemory/VS-Diagnostics
      procedure, not something the xunit suite can assert, and what it was there to detect —
      recipients surviving navigation — is now asserted directly and synchronously by
      `A_page_navigated_away_from_is_no_longer_a_messenger_recipient`. Worth running once by hand
      before a release that adds screens.
- [x] No changes required in any OS head composition root — and none was made. (The registrations
      have since moved into `PineCompositionRoot` anyway; see the note in §2.3.)

---

## 9. Effort & risk, in hindsight

The estimate was 0.5–1 day for Option A and it was about right. The risk it named — nested
activation of the dashboard widgets — was the real one, and is the reason
`DashboardViewModel.OnActivated` exists and is tested in both directions. The risk it did **not**
name is the one that would have shipped a silent bug: §6.2's ordering constraint, which is invisible
unless you read the senders before choosing where activation lives.

---

## 10. References

- CommunityToolkit.Mvvm — [Messenger](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/messenger)
  and [ObservableRecipient](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/observablerecipient).
- Key: *"do not capture the recipient in the registration delegate"* — use the `recipient`
  parameter; that is what keeps `WeakReferenceMessenger` actually weak. The stronger conclusion of
  TD-34 is that a codebase is better off with no registration delegates to get wrong.
