<div align="center">
  <img src="public/PineLogo.svg" alt="Pine Logo" width="128" />
  <h1>Pine</h1>
  <p><strong>A Privacy-First, Offline-First Personal Finance Engine</strong></p>
</div>

---

| | |
|---|---|
| **Status** | Partially implemented — alpha. Do not put real money in it yet |
| **Reconciled** | 2026-08-31 · Rewritten against the code at remediation iteration 21 (TD-39). Every claim below is either verified in the tree or marked as not yet true |
| **Version** | `0.1.0-alpha.1` — no release has ever been published |

> **Read this before anything else.** Pine has never been released, has no installer, and is not
> signed. An internal production-readiness audit
> ([`docs/60-Production-Readiness-Audit.md`](docs/60-Production-Readiness-Audit.md)) rates it
> **2.3 / 5 — Not Production Ready** and tracks 110 numbered defects and gaps, a remediation cycle
> against them, and exactly what is and is not true of each subsystem. It is the honest document
> about this project; this README is the short version of it.

## The Vision

**Pine** is a personal finance application for people who do not want their financial data on
someone else's computer. It is **offline-first** and stores everything in a locally encrypted vault.

Your money. Your data. Your control.

### What is actually true today

- **No network egress, anywhere.** There is no `HttpClient`, no socket, and no analytics or
  crash-reporting SDK in `src/` — a property enforced by review rather than merely intended, and
  recorded in [`docs/adr/0012-no-telemetry.md`](docs/adr/0012-no-telemetry.md). Nothing to opt out
  of, because there is nothing to opt in to.
- **Encryption at rest.** The database is SQLCipher-encrypted. The key is a random 256-bit data key
  wrapped with AES-256-GCM under an auth key that Argon2id derives from your master password, bound
  to every other field of the vault header — see
  [`docs/adr/0013-encryption-baseline.md`](docs/adr/0013-encryption-baseline.md). Pine cannot read
  your data without your password, and neither can anyone who copies the files. **There is no
  recovery path if you forget it.**
- **Accounts, transactions, imports and receipt scanning** are real and work: cash, bank, card,
  investment and crypto accounts; CSV/Excel import with stable-key deduplication and a review step;
  on-device OCR that never sends an image anywhere and cannot write to the ledger without your
  explicit confirmation.
- **.NET 10, Avalonia UI, CQRS via MediatR, hexagonal architecture.** The layering is real: the
  domain project has no external dependency and the UI dispatches through MediatR.

### What is not true yet

- **Windows is the only head that is built, packaged and used.** The shared UI is OS-agnostic and
  macOS, Linux, Android, iOS and WebAssembly heads exist as projects, but only Windows has a
  packaging path; Android and iOS still carry template identity; and the WebAssembly head cannot
  open a vault at all, because the browser platform has no AES-GCM implementation available to it
  (TD-88, TD-109). Treat the other five as source, not as supported platforms.
- **A transaction cannot be edited.** Correction is void-and-re-enter, which is the right default
  for a ledger and costs two rows with no link between them (TD-68).
- **The budgets screen does not compute anything.** Its progress bar is hard-coded to zero (TD-99).
- **No backup restore path, no installer, no code-signing certificate, and no update mechanism.**
  An update check would be Pine's first network call ever, so it is a privacy decision and not a
  packaging chore.
- **CI runs only when a human dispatches it.** Nothing verifies a commit automatically (TD-07).

---

## Architecture

Pine was engineered from the ground up using strict **Clean / Hexagonal Architecture** principles.

1. **Domain-Driven Design (DDD):** The core engine uses rich Domain Entities (`AggregateRoot`, `ValueObject`, `DomainEvent`) to eliminate primitive obsession (e.g., using a `Money` Value Object instead of raw decimals).
2. **CQRS via MediatR:** Every action in Pine is segregated into `Commands` (state mutations) and `Queries` (data retrieval), with validation and transaction behaviours in the pipeline so a rejected command never opens a database transaction.
3. **Composition Roots:** The UI layer (`Pine.Ui.Avalonia`) is OS-agnostic. The executables (`Pine.Ui.Windows`, `Pine.Ui.Macos`, `Pine.Ui.Ios`, etc.) are composition roots that bootstrap the native OS window and inject dependencies. They are **not** equivalent: each root registers its own services by hand, and several services are registered in one root only (TD-29, TD-85, TD-90).

## Getting Started

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- IDE of your choice (Visual Studio, Rider, VS Code)

### Running the app
From the repository root:

```bash
# Run the Windows Composition Root — the only head that is regularly built and used
dotnet run --project src/Pine.Ui.Windows

# Print the version, which is resolved from the build and the commit
dotnet run --project src/Pine.Cli -- --version

# Run the test suites (1054 tests: 855 unit + 199 integration)
dotnet test tests/Pine.Tests.Unit/Pine.Tests.Unit.csproj
dotnet test tests/Pine.Tests.Integration/Pine.Tests.Integration.csproj
```

The macOS, Linux, Android, iOS and Web heads build from the same shared UI but are not verified:
CI does not compile them (TD-07) and none of them has been packaged. Android, iOS and Web
additionally need their respective `dotnet` workloads installed.

---

## Project Structure

- `src/Pine.Core`: The Domain Layer. Contains entities, value objects, and business rules. No external dependencies.
- `src/Pine.Application`: The Use Case Layer. Contains CQRS Commands/Queries and interfaces.
- `src/Pine.Infrastructure`: The Persistence Layer. Implements repositories and the EF Core SQLCipher Database Context.
- `src/Pine.Ui.Avalonia`: The Cross-Platform UI Layer. Contains MVVM views, routing, and Fluent Design styles.
- `src/Pine.Ui.*`: Platform-specific bootstrappers (Windows, macOS, iOS, Android, Web).

## Contributing
Please see our [CONTRIBUTING.md](CONTRIBUTING.md) and [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) for details on how to help out.
