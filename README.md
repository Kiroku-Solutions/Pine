<div align="center">
  <h1>🌲 Pine</h1>
  <p><strong>A Privacy-First, Offline-First Personal Finance Engine</strong></p>
</div>

---

## 💡 The Vision

**Pine** is a modern, enterprise-grade personal finance application built for users who refuse to compromise on privacy. 
Unlike cloud-based budgeting apps that harvest your financial data, Pine is **offline-first** and stores everything in a military-grade encrypted local vault (SQLCipher). 

Your money. Your data. Your control.

### Key Features
- **Zero-Knowledge Architecture:** Your database is fully encrypted with `Argon2id` key derivation. Not even Pine can read your data without your master password.
- **True Cross-Platform:** Write once, run everywhere. Powered by **Avalonia UI**, Pine runs natively on Windows, macOS, Linux, iOS, Android, and WebAssembly.
- **Comprehensive Tracking:** Track cash, bank accounts, credit cards, investments, and crypto all in one place.
- **Lightning Fast:** Built on .NET 10, utilizing a high-performance CQRS pipeline.

---

##  Architecture (Enterprise-Grade)

Pine was engineered from the ground up using strict **Clean / Hexagonal Architecture** principles.

1. **Domain-Driven Design (DDD):** The core engine uses rich Domain Entities (`AggregateRoot`, `ValueObject`, `DomainEvent`) to eliminate primitive obsession (e.g., using a `Money` Value Object instead of raw decimals).
2. **CQRS via MediatR:** Every action in Pine is strictly segregated into `Commands` (state mutations) and `Queries` (data retrieval), ensuring a perfectly scalable and testable logic flow.
3. **Composition Roots:** The UI layer (`Pine.Ui.Avalonia`) is 100% OS-agnostic. The actual executables (`Pine.Ui.Windows`, `Pine.Ui.Macos`, `Pine.Ui.Ios`, etc.) act as simple composition roots that bootstrap the native OS window and inject dependencies.

##  Getting Started

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- IDE of your choice (Visual Studio, Rider, VS Code)

### Running the App
To run the Desktop version (Windows/macOS/Linux), use the .NET CLI from the root folder:

```bash
# Run the Windows Composition Root
dotnet run --project src/Pine.Ui.Windows

# Run the macOS Composition Root
dotnet run --project src/Pine.Ui.Macos
```

*(Note: Running Android, iOS, or Web versions requires installing their respective `dotnet workloads`).*

---

##  Project Structure

- `src/Pine.Core`: The Domain Layer. Contains entities, value objects, and business rules. No external dependencies.
- `src/Pine.Application`: The Use Case Layer. Contains CQRS Commands/Queries and interfaces.
- `src/Pine.Infrastructure`: The Persistence Layer. Implements repositories and the EF Core SQLCipher Database Context.
- `src/Pine.Ui.Avalonia`: The Cross-Platform UI Layer. Contains MVVM views, routing, and Fluent Design styles.
- `src/Pine.Ui.*`: Platform-specific bootstrappers (Windows, macOS, iOS, Android, Web).

##  Contributing
Please see our [CONTRIBUTING.md](CONTRIBUTING.md) and [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) for details on how to help out.
