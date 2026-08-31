# 58 · Tech Debt — `WeakReferenceMessenger` Recipient Lifecycle

| | |
|---|---|
| **Status** | Non-normative — technical-debt record |
| **Reconciled** | 2026-08-31 · A working record of one defect (TD-34/TD-35). The audit's §6.12 assessed it as accurate. |
| **Type** | Technical debt / correctness |
| **Area** | `Pine.Ui.Avalonia` (shared UI, all OS heads) |
| **Severity** | Medium (correctness bug hiding behind weak references, not a hard leak) |
| **Owner** | UI / Frontend |
| **Related** | `27-Development-Guidelines.md`, `39-Performance.md`, `20-Coding-Standards.md` |

---

## 1. TL;DR

The app uses `CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default` to broadcast
domain events (`DataChangedMessage`, `ReceiptCapturedMessage`, `RetakeRequestedMessage`)
between view-models. Three problems compound:

1. **Some recipients register with a lambda that captures `this`.** This defeats the
   *weak* reference guarantee — the messenger ends up keeping those view-models alive.
2. **No recipient ever deregisters.** Because most view-models are registered as
   `AddTransient`, every navigation creates a **new** instance that registers **again**,
   while the previous instances stay subscribed (until GC, or forever for the captured
   ones). A single broadcast is then delivered to **N stale instances**, causing duplicate
   side effects (e.g. a receipt processed several times, list reloaded multiple times).
3. **One handler is `async void`**, so exceptions thrown while handling a message are
   unobserved and can crash the process.

None of this is a *hard* memory leak in the classic sense (weak refs mostly save us), but
it is a latent **correctness** and **resource** bug that must be fixed before we scale the
number of screens and messages.

---

## 2. Background — how messaging is used today

### 2.1 Messages

- `ViewModels/Messages/DataChangedMessage.cs` — "the data store changed, refresh".
- `ViewModels/Messages/ReceiptCapturedMessage.cs` — carries `Stream`s of a captured receipt.
- `ViewModels/Messages/RetakeRequestedMessage.cs` — user wants to retake a photo.

### 2.2 Senders (`.Send(...)`)

| File | Message |
|---|---|
| `AddTransactionViewModel.cs:173` | `DataChangedMessage` |
| `CreateAccountViewModel.cs:69` | `DataChangedMessage` |
| `ScanReceiptViewModel.cs:190` | `DataChangedMessage` |
| `DiagnosticsViewModel.cs:99` | `DataChangedMessage` |
| `ScanReceiptViewModel.cs:213` | `RetakeRequestedMessage` |
| `CropReceiptViewModel.cs:102` | `RetakeRequestedMessage` |
| `CropReceiptViewModel.cs:86` | `ReceiptCapturedMessage` |
| `LiveScannerViewModel.cs:326, 362` | `ReceiptCapturedMessage` |

### 2.3 Recipients (`.Register<T>(...)`)

| File | Message | Pattern | Verdict |
|---|---|---|---|
| `DashboardViewModel.cs:45` | `DataChangedMessage` | `(r, m) => ((DashboardViewModel)r).Receive(m)` | ✅ correct (no capture) |
| `TransactionsViewModel.cs:38` | `DataChangedMessage` | `(r, m) => ((TransactionsViewModel)r).Receive(m)` | ✅ correct |
| `Widgets/DashboardWidgetsPart1.cs:32` (Balance) | `DataChangedMessage` | `(r, m) => ((BalanceWidgetViewModel)r).Receive(m)` | ✅ correct |
| `Widgets/DashboardWidgetsPart3.cs:52` (RecentTx) | `DataChangedMessage` | `(r, m) => ((RecentTransactionsWidgetViewModel)r).Receive(m)` | ✅ correct |
| `ScanReceiptViewModel.cs:66` | `ReceiptCapturedMessage` | `async (r, m) => await ProcessReceiptImageAsync(...)` | ❌ **captures `this` + `async void`** |
| `CropReceiptViewModel.cs:31` | `ReceiptCapturedMessage` | `(r, m) => { _originalStream = ...; PreviewImage = ... }` | ❌ **captures `this`** |
| `LiveScannerViewModel.cs:60` | `RetakeRequestedMessage` | `(r, m) => StartScanning()` | ❌ **captures `this`** |
| `BudgetsViewModel.cs:47` | declares `IRecipient<DataChangedMessage>` | **no `Register` call found** | ⚠️ dead/incomplete — either never receives, or should be wired |

> Note: view-model DI lifetimes are declared in each OS head's composition root, e.g.
> `src/Pine.Ui.Windows/Program.cs:68-80` — all page/dialog VMs are `AddTransient`
> (`MainViewModel` is the only `AddSingleton`). Every head duplicates this list, so any
> fix must live in the **shared** `Pine.Ui.Avalonia` project and require **no** per-head
> change.

---

## 3. The problems in detail

### P1 — Closures capturing `this` defeat the weak reference

`WeakReferenceMessenger.Register<TMessage>(recipient, handler)` holds the **recipient**
(`this`) weakly, **but it stores the `handler` delegate strongly**. If the handler is a
closure that captures `this` (directly, or by calling an instance method / touching an
instance field), the messenger transitively holds a **strong** reference to the
view-model. The "weak" guarantee is gone and the instance can no longer be collected while
the messenger lives (and `WeakReferenceMessenger.Default` lives for the whole app).

Affected: `ScanReceiptViewModel`, `CropReceiptViewModel`, `LiveScannerViewModel`.

The **correct** pattern (already used by Dashboard/Transactions/widgets) never captures:
it uses only the `r` (recipient) and `m` (message) parameters and casts `r` back to the
concrete type.

### P2 — No deregistration → duplicate live recipients & duplicate side effects

Because the affected VMs are `AddTransient`, each navigation to Scan/Crop/LiveScanner
creates a **new** instance that registers again. Nothing ever calls `Unregister` /
`UnregisterAll`. Consequences:

- For the captured ones (P1), instances are **immortal** and accumulate: navigate to the
  scanner 5 times → 5 live `ScanReceiptViewModel`s, all subscribed. One
  `ReceiptCapturedMessage` → the receipt is **processed 5 times** (5× OCR calls, 5× image
  decodes, 5× `Amount`/`LineItems` writes to whichever instance the UI is bound to may
  race). Same class of bug for `DataChangedMessage` fan-out to stale Dashboard/Transactions
  instances that happen to still be alive before GC.
- Even for the "correct" ones, there is a window between "navigated away" and "GC'd" where
  a stale instance still handles messages and does wasted work (extra `IMediator` queries,
  DB reads).

### P3 — `async void` message handler

`ScanReceiptViewModel.cs:66` registers `async (r, m) => await ProcessReceiptImageAsync(...)`.
The messenger invokes the handler synchronously; an `async` lambda with no return target is
effectively `async void`. Any exception inside `ProcessReceiptImageAsync` (OCR failure,
corrupt stream, cancelled task) is **unobserved** and will tear down the process instead of
surfacing as an in-app error.

### P4 — Lifetime mismatch

`AddTransient` for screen VMs + a process-wide singleton messenger + no deactivation =
unbounded subscription growth. The lifetime of "who should receive this message" is really
"the currently-visible screen", which nothing models today.

---

## 4. Impact / symptoms to expect in the wild

- Receipt captured once but processed multiple times after visiting the scanner repeatedly.
- Transaction / dashboard lists reloading several times per change (flicker, extra DB load).
- Growing managed heap while navigating (visible in a memory profiler as retained
  `ScanReceiptViewModel` / `CropReceiptViewModel` / `LiveScannerViewModel` instances).
- Rare hard crashes from an exception in the receipt handler (P3).

---

## 5. Remediation options

### Option A — `ObservableRecipient` + activation (recommended)

Migrate message-consuming VMs from `ObservableObject` to
`CommunityToolkit.Mvvm.ComponentModel.ObservableRecipient` and implement `IRecipient<T>`.

- `ObservableRecipient` exposes `IsActive`. Setting `IsActive = true` calls `OnActivated()`,
  which **auto-registers every `IRecipient<T>` the VM implements**; setting `IsActive = false`
  calls `OnDeactivated()` → `Messenger.UnregisterAll(this)`.
- Drive `IsActive` from the **view lifecycle**: set it in `Control.Loaded` / clear it in
  `Control.Unloaded` (via a tiny attached behavior), or from the navigation service when a
  page becomes current / is replaced.
- Registration is generated by the toolkit and **never captures `this`** incorrectly, so P1
  disappears. Deactivation gives us deterministic P2/P4 cleanup. Handlers become
  `Receive(T)` methods (P3: make an `async Task ReceiveAsync` invoked via a tracked,
  try/catch-wrapped fire-and-forget, never `async void`).

**Pros:** idiomatic, centralized, deterministic, minimal per-VM code.
**Cons:** requires a base-class change + one small attached behavior for `IsActive`.

### Option B — `IDisposable` VMs + navigation-owned disposal

Make consuming VMs `IDisposable`; call `WeakReferenceMessenger.Default.UnregisterAll(this)`
in `Dispose()`. Extend `MainViewModel`/navigation (`INavigationService`,
`MainViewModel.NavigateTo<T>`) to **dispose the previously current page VM** when replacing
`CurrentPage`.

**Pros:** explicit, no base-class swap.
**Cons:** must thread disposal through navigation and every overlay/dialog host
(`DashboardViewModel` shows dialogs, `TransactionsView` overlay, etc.); easy to miss a path.

### Option C — Minimal hotfix (do at least this if A/B are deferred)

1. Remove `this`-capturing closures — rewrite all three bad handlers to the
   `(r, m) => ((T)r).Receive(m)` form.
2. Convert the `async void` receipt handler to a sync shim that starts a tracked task with
   try/catch (surface errors via `ErrorMessage`, never crash).
3. Wire `BudgetsViewModel`'s `IRecipient<DataChangedMessage>` (register it, or drop the
   interface) so its intent is unambiguous.

This kills P1/P3 immediately and shrinks P2 to "weakly-held, GC-eventually" (tolerable),
buying time for A.

---

## 6. Recommended plan (Option A) — step by step

1. **Add an activation behavior** in `Pine.Ui.Avalonia` (e.g.
   `Behaviors/RecipientActivationBehavior.cs` or an attached property) that sets
   `((IObservableRecipientLike)DataContext).IsActive = true` on `Loaded` and `false` on
   `Unloaded`. Attach it in the root element of each message-consuming view. (Alternatively,
   toggle `IsActive` from the navigation service when `CurrentPage` changes.)
2. **Migrate consumers** to `ObservableRecipient` + `IRecipient<T>`:
   - `DashboardViewModel`, `TransactionsViewModel`, `BalanceWidgetViewModel`,
     `RecentTransactionsWidgetViewModel` — already have `Receive`; delete the manual
     `Register` calls, add `IsActive` control.
   - `ScanReceiptViewModel` — implement `IRecipient<ReceiptCapturedMessage>` with a
     `Receive` that calls a `_ = SafeProcessAsync(m)` shim (try/catch); remove the async
     void lambda.
   - `CropReceiptViewModel` — `IRecipient<ReceiptCapturedMessage>`.
   - `LiveScannerViewModel` — `IRecipient<RetakeRequestedMessage>`.
   - `BudgetsViewModel` — finish or remove its `IRecipient` implementation.
3. **Remove** all direct `WeakReferenceMessenger.Default.Register<...>` calls from VM
   constructors. Keep `.Send(...)` calls (senders are fine).
4. **Verify** no handler captures `this` and none is `async void`.
5. **Confirm** widgets on the dashboard activate/deactivate with the dashboard (nested
   activation): `ObservableRecipient` children need their `IsActive` driven too — either
   propagate from `DashboardViewModel` or attach the behavior to the widget views.

### Before / after sketch

```csharp
// BEFORE — ScanReceiptViewModel (defeats weak ref + async void)
WeakReferenceMessenger.Default.Register<ReceiptCapturedMessage>(this, async (r, m) =>
{
    await ProcessReceiptImageAsync(m.ImageStream, m.OcrImageStream); // captures `this`
});

// AFTER — ObservableRecipient + IRecipient<T>, no capture, no async void
public partial class ScanReceiptViewModel : ObservableRecipient,
                                            IRecipient<ReceiptCapturedMessage>
{
    public void Receive(ReceiptCapturedMessage m) => _ = SafeProcessAsync(m);

    private async Task SafeProcessAsync(ReceiptCapturedMessage m)
    {
        try   { await ProcessReceiptImageAsync(m.ImageStream, m.OcrImageStream); }
        catch (Exception ex) { ErrorMessage = "Could not process the receipt."; /* log ex */ }
    }
}
// Registration/unregistration handled automatically by IsActive (view Loaded/Unloaded).
```

---

## 7. Verification

- **Unit/behavioral:** send `ReceiptCapturedMessage` after navigating to the scanner 3×;
  assert the receipt is processed **exactly once** (mock `IMediator`, count `ScanReceiptCommand`).
- **Memory:** with a profiler (dotMemory / VS Diagnostics), navigate Scanner↔Dashboard 10×,
  force GC, assert **0** retained `ScanReceiptViewModel` / `CropReceiptViewModel` /
  `LiveScannerViewModel` instances.
- **Deactivation:** assert `WeakReferenceMessenger.Default.IsRegistered<T>(vm)` is `false`
  after the view is unloaded.
- **Exceptions:** throw inside `ProcessReceiptImageAsync`; assert the app stays up and
  `ErrorMessage` is set (no process crash).

## 8. Acceptance criteria (Definition of Done)

- [ ] No message handler captures `this` (all use the `r` parameter or `ObservableRecipient`).
- [ ] No `async void` message handlers.
- [ ] Every recipient deregisters when its view is no longer visible.
- [ ] A single broadcast is handled exactly once per logically-active screen.
- [ ] `BudgetsViewModel`'s `IRecipient<DataChangedMessage>` is either wired or removed.
- [ ] Memory + "process once" tests above pass.
- [ ] No changes required in any OS head composition root.

## 9. Effort & risk

- **Effort:** ~0.5–1 day for Option A (base-class swap + one behavior + migrate ~7 VMs),
  or ~1–2 h for the Option C hotfix.
- **Risk:** low–medium. Main risk is nested activation of dashboard widgets (make sure
  their `IsActive` is driven). Mitigate by testing dashboard refresh after a
  `DataChangedMessage` still updates all widgets.

## 10. References

- CommunityToolkit.Mvvm — [Messenger](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/messenger)
  and [ObservableRecipient](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/observablerecipient).
- Key: *"do not capture the recipient in the registration delegate"* — use the `recipient`
  parameter; that is what keeps `WeakReferenceMessenger` actually weak.
