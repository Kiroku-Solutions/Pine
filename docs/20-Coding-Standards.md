# 20 — Coding Standards

| Field            | Value                                                                                |
|------------------|--------------------------------------------------------------------------------------|
| Document ID      | `PINE-DOC-20-CODING-STANDARDS`                                                       |
| Status           | Draft — pending review                                                               |
| Owner            | Engineering Review Board                                                             |
| Created          | 2026-07-08                                                                           |
| Last Updated     | 2026-07-08                                                                           |
| Specification Standard | Internal (coding standards)                                                    |
| Glossary         | `PINE-DOC-03-GLOSSARY`                                                               |
| Prerequisites    | `00-Vision`..`19-Traceability-Matrix`                                                |
| Related Documents| `21-Definition-of-Done`, `27-Development-Guidelines`, `35-Git-Workflow`, `37-Code-Review` |

---

## 1. Purpose

Define the coding standards of Pine: language baseline, formatting,
naming, file structure, error handling, logging, concurrency, IO,
testing rules, dependency rules, lint, static analysis, code review
expectations, and the *banned* patterns.

## 2. Scope

In scope: C# (primary), F# (allowed for analytical components);
formatting; naming; file structure; error handling; logging;
concurrency; IO; testing; dependency rules; lint; static analysis;
banned patterns.

Out of scope: high-level architecture → `09-System-Architecture`;
plugin SDK details → `33-Plugin-SDK`; CI pipeline → `28-CI-CD`.

## 3. Definitions

| Term                | Working definition                                                                                |
|---------------------|------------------------------------------------------------------------------------------------------|
| SAST                | Static Application Security Testing.                                                                |
| DTO                 | Data Transfer Object.                                                                                 |
| DFA                 | Data Flow Analysis.                                                                                   |
| Pure function       | A function with no side effects, deterministic for the same input.                                   |
| Side effect         | Any observable change outside the function (IO, mutation, time, randomness).                         |
| Immutability        | An object whose state cannot change after construction.                                              |
| Nullable reference type| A C# nullable reference type annotation that expresses nullability at the type level.             |

> **Assumption CS-A-1.** "Coding standards" mean the *behavioural* and
> *visual* rules enforced by tooling. Anything not enforced is a
> *guideline*.

## 4. Language Baseline

- **Primary language.** C# 12 (or the version required by the chosen
  .NET LTS in `09-System-Architecture` §4).
- **Secondary language.** F# (for analytical components only).
- **Nullable reference types.** Enabled project-wide.
- **Warnings as errors.** Enabled project-wide.
- **Implicit usings.** Disabled; explicit `using` only.
- **File-scoped namespaces.** Required.
- **Global usings.** Discouraged; allow-list only.

## 5. Formatting

- **Formatter.** `dotnet format` with the project's `editorconfig`.
- **Indentation.** 4 spaces; no tabs.
- **Line length.** 120 columns.
- **Braces.** Allman style.
- **`var` vs explicit type.** `var` for locals when the type is
  obvious; explicit type for public APIs and for clarity.
- **Expression-bodied members.** Allowed for trivial members only.
- **File ordering.** `using` → `namespace` → types.

## 6. Naming

| Element            | Style                          | Example                              |
|--------------------|--------------------------------|---------------------------------------|
| Namespace          | `Pine.<Context>.<Layer>`       | `Pine.Accounts.Domain`                |
| Class              | PascalCase                     | `Account`                             |
| Interface          | `I` + PascalCase               | `IAccountRepository`                  |
| Method             | PascalCase, verb                | `RecordTransaction`                   |
| Property           | PascalCase                      | `PostedAt`                            |
| Field (private)    | `_camelCase`                   | `_clock`                              |
| Constant           | PascalCase                     | `MaxRetryAttempts`                    |
| Enum               | PascalCase (type and members)  | `AccountStatus.Open`                  |
| Async method       | suffix `Async`                 | `LoadAsync`                           |
| Generic parameter  | `T` + PascalCase suffix        | `TEntity`                             |
| Test class         | `<Subject>Tests`               | `AccountTests`                        |
| Test method        | `<Method>_<Scenario>_<Expected>`| `RecordTransaction_WhenAmountNegative_Throws` |

## 7. File Structure

- One public type per file.
- File name matches the public type name.
- A file has at most 500 lines; longer files are a defect and
  trigger a refactor in the next sprint.
- Partial classes are discouraged; if used, every partial is in the
  same folder.

## 8. Project Structure

```text
src/
  Pine.<Context>/
    Pine.<Context>.Domain/        # aggregates, value objects, events
    Pine.<Context>.Application/   # commands, queries, handlers
    Pine.<Context>.Infrastructure/# adapters (DB, sync, AI, plugins)
    Pine.<Context>.Api/           # SDK ports, public surface
tests/
  Pine.<Context>.Domain.Tests/
  Pine.<Context>.Application.Tests/
  Pine.<Context>.Infrastructure.Tests/
  Pine.<Context>.Api.Tests/
```

- `Domain` has zero references to `Application`, `Infrastructure` or
  `Api`. Architecture tests enforce this.
- `Application` depends on `Domain`. It MUST NOT depend on
  `Infrastructure` directly.
- `Infrastructure` depends on `Application` and implements ports
  defined in `Application` (or `Api` for SDK ports).
- `Api` exposes the SDK ports to plugins and other clients.

## 9. Error Handling

- Errors are *values* at the application boundary; exceptions are
  reserved for *exceptional* situations.
- A `Result<T, E>` type is used throughout the application core
  (success / failure, with a typed error).
- An exception is allowed when the failure is *unrecoverable* (e.g.
  database corruption, encryption key missing).
- A caught exception MUST be logged with a correlation id; it MUST
  NOT be silently swallowed.

## 10. Logging

- Structured logging (Serilog or the chosen provider).
- Logs are *local* by default; telemetry is opt-in only.
- A log line MUST include a correlation id when emitted in the
  context of a request, a sync operation, an AI inference or a
  rule firing.
- A log line MUST NOT include a secret, a credential, a recovery
  phrase, a sync key or a data key.
- A log line MUST NOT include raw financial data.

## 11. Concurrency

- Concurrency is opt-in per aggregate; the default is
  single-writer.
- `async`/`await` is used for IO; CPU-bound work is *not* async.
- `ConfigureAwait(false)` is used in library code.
- `CancellationToken` is plumbed through every async method.
- Time is abstracted behind `IClock`.
- Randomness is abstracted behind `IRandom`.

## 12. Input / Output

- File IO is *async*.
- All file paths are validated against a base directory; path
  traversal is rejected.
- All text IO uses UTF-8 with no BOM.
- All binary IO uses an explicit encoding.
- All network IO uses TLS 1.3 or stronger; certificate validation
  is on; pinning is opt-in per provider.

## 13. Money and Currency

- Money is a *value object* (decimal). Never `double`, never `float`.
- Money arithmetic is performed in the *minor unit* (cents, etc.)
  whenever possible.
- Currency conversion uses a *port*; the implementation is swappable.
- All rounding rules are explicit; the default is banker's rounding.

## 14. Testing Rules

- Every public type has at least one unit test.
- Every aggregate has at least one invariant test.
- Every public method has at least one test that exercises the happy
  path and one that exercises the failure path.
- Test names follow the `<Method>_<Scenario>_<Expected>` pattern.
- Tests are *deterministic*; seeded random is used.
- Tests are *isolated*; no leakage between tests.
- Tests are *fast*; unit tests < 100 ms, integration tests < 5 s.
- Architecture tests assert the *direction* of dependencies.

## 15. Dependency Rules

- A domain package has zero third-party dependencies.
- An application package depends only on standard library and on the
  domain packages it orchestrates.
- An infrastructure package may depend on third-party packages, but
  only those in the *approved dependencies* list.
- GPL-incompatible dependencies are *rejected* in core code; in
  isolated adapter code they require an ADR.
- A dependency added without a Dependabot baseline requires an ADR.

## 16. Static Analysis and Lint

- **Compiler.** Warnings as errors.
- **dotnet format.** Enforced in CI.
- **StyleCop.** Enforced in CI; the project's ruleset overrides
  the defaults only with an ADR.
- **Roslyn analyzers.** Enabled project-wide.
- **CodeQL.** Run in CI on every PR.
- **Dependabot.** Enabled.
- **Semgrep.** Run in CI on every PR with the project's ruleset.
- **SonarQube.** Run in CI; the project's quality gate is the
  release gate.

## 17. Banned Patterns

| Pattern                                        | Why                                                                                  |
|------------------------------------------------|--------------------------------------------------------------------------------------|
| `dynamic`                                      | Type safety, SAST.                                                                   |
| `BinaryFormatter`                              | RCE, deprecated.                                                                      |
| `System.Reflection.Assembly.LoadFrom` for plugin loading in core | Sandbox bypass; use the host.                                       |
| `Thread.Sleep` in async code                   | Deadlock and performance.                                                              |
| `Task.Run` to "make it async"                  | Hides blocking work; measure first.                                                    |
| `async void` (except event handlers)            | Unobservable exceptions.                                                              |
| `DateTime.Now` in domain code                  | Breaks testability; use `IClock`.                                                     |
| `new Random()` in domain code                  | Breaks testability; use `IRandom`.                                                    |
| `Environment.GetEnvironmentVariable("PASSWORD")` or similar | Secrets in env vars are logged by accident.                     |
| `Convert.ToDouble` for money                   | Floating-point bugs; use decimal.                                                     |
| Public mutable statics                         | Hidden global state; testability.                                                     |
| `catch { }`                                    | Silent failure.                                                                       |
| Suppressing warnings without justification    | Audit trail.                                                                          |
| `Console.WriteLine` in production code         | Unstructured logging; use the logger.                                                  |
| Disabling architecture tests                   | Bypasses the layer rules.                                                            |

## 18. Code Review Expectations

- Every PR is reviewed by at least one maintainer.
- Every architectural change is reviewed by at least two maintainers
  and recorded as an ADR.
- Every PR has a clean CI before review.
- Every PR has a self-review checkbox list (template in
  `27-Development-Guidelines`).
- The detailed review process is in `37-Code-Review`.

## 19. Style Decisions that are *Not* Enforced

- Whether to use `record` or a class for a value object (project
  convention is to use `readonly record struct` for value objects,
  `record` for DTOs, classes for entities).
- Whether to use `IReadOnlyList<T>` or `IEnumerable<T>` for read-only
  collections (project convention is `IReadOnlyList<T>` when the
  count is meaningful, `IReadOnlyCollection<T>` when only the count
  is meaningful).
- Naming of test fixtures (project convention is `Given_<context>`).

## 20. Style Checklist

- [x] Language baseline, formatting, naming, file structure, project
      structure, error handling, logging, concurrency, IO, money,
      testing, dependencies, static analysis, banned patterns are
      all present.
- [x] Style decisions that are not enforced are listed.
- [x] No TODO placeholders.
- [x] No contradiction with `00-Vision`..`19-Traceability-Matrix`.

## 21. Revision History

| Version | Date       | Author                | Change                                                       |
|---------|------------|-----------------------|--------------------------------------------------------------|
| 0.1     | 2026-07-08 | Engineering Review Board | Initial coding standards.                                  |
