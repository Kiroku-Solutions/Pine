# 🌲 Pine - Product Definition

| | |
|---|---|
| **Status** | Non-normative — product definition and positioning |
| **Reconciled** | 2026-08-31 · Checked against the code at remediation iteration 21 (TD-39). Positioning and monetisation are intent; the feature and platform claims in §3 and §5 were corrected to what exists |

## 1. What is Pine?
**Pine** is an offline-first, privacy-focused personal finance manager designed for users who want total control over their financial data. It allows users to track their cash, bank accounts, credit cards, investments, and cryptocurrencies without relying on third-party cloud services that harvest or sell user data.

## 2. The Problem
Modern budgeting and financial applications (like Mint, YNAB, or Monarch) suffer from a fundamental flaw: **they require you to upload your sensitive financial data to their servers.**
- **Privacy Risks:** Financial data is often anonymized and sold to advertisers or credit bureaus.
- **Security Breaches:** Centralized databases are honeypots for hackers.
- **Subscription Fatigue:** Users are forced to pay monthly subscriptions just to access their own data.
- **Platform Lock-in:** Exporting data is often intentionally difficult.

## 3. The Solution (Pine's Value Proposition)
Pine solves these problems by flipping the architecture. 
- **100% Local & Encrypted:** Your data never leaves your device — literally: there is no HTTP client, socket or analytics SDK anywhere in `src/` (ADR-0012). It is stored in a local SQLite database encrypted with SQLCipher under AES-256, whose key is a random data key wrapped with AES-256-GCM under an Argon2id-derived auth key (ADR-0013). There is no recovery path if the master password is lost.
- **No Subscriptions, No Clouds:** You own the software and the data. If you want to sync it across devices, you can manually share the encrypted vault file using your own cloud provider (Drive, Dropbox, iCloud) without exposing the contents.
- **Rules, and they outrank the model.** Write "payee contains STARBUCKS → Coffee" once and every future import files itself. A matching rule wins outright — the classifier is not even asked — because an instruction you wrote is not a statistical guess to be weighed against it, and "why is this in Coffee?" should have one answer. Rules never overrule a category you typed, and never touch transactions already recorded.
- **Receipts are kept, not just read.** Scanning a receipt used to produce a transaction and throw the document away. The image is now stored in the encrypted vault beside its transaction, with a SHA-256 recorded when it is attached and checked whenever it is opened — so the picture is evidence for the figure, and a tampered one says so instead of being displayed. It is written in the same database transaction as the ledger row and is included in every backup.
- **English and Spanish, switched live.** Every user-visible string flows through a JSON string table keyed by stable IDs, per `41-Localization` §5; picking a language in Settings re-renders the whole application with no restart, and the choice is stored outside the vault so the pre-unlock screens follow it too. The glossary that governs the Spanish financial terms is `41-Localization` §18, agreed before the strings were written rather than discovered across them.
- **One codebase, several heads — Windows is the one that runs.** Built with Avalonia UI and .NET 10. The shared UI is OS-agnostic and heads exist for macOS, Android and iOS, but only Windows is built, packaged and used (TD-109). The browser head was deleted in the Phase 3 platform review: it had no AES-GCM to open a vault with and no WebAssembly build of OpenCV or PaddleInference to scan a receipt with, and browser-only distribution was already a documented non-goal. Narrowing was the decision; investing in per-RID desktop runtimes remains open and is blocked on a macOS or Linux test machine, not on a package (TD-44).
- **A considered UI:** a glassmorphic Fluent interface with a light and dark palette. No performance work has been measured, so "fast" is an aspiration and `39-Performance` is marked Aspirational accordingly.

## 4. Target Audience
- **Privacy Advocates:** People who refuse to use cloud-based financial trackers.
- **Tech-Savvy Users:** Developers and power users who appreciate local-first software and encryption.
- **Frugal Budgeters:** Users who don't want to pay a $10/month subscription to manage their money.
- **Digital Nomads:** People who need an offline-capable app when traveling without internet access.

## 5. Core Features — what exists, at 2026-08-31
### ✅ Built and covered by tests
- **Vault Security:** master password creation held to an entropy floor, Argon2id key derivation, an
  AES-256-GCM key envelope, SQLCipher, idle auto-lock, and a working password change.
- **Accounts System:** cash, bank, card, investment, crypto and custom account types, with a recorded
  opening balance so a balance can be recomputed from its own ledger.
- **Transactions Engine:** income, expense, splits and transfers, written inside one database
  transaction with the balance, plus a void path and a balance recalculation that reports drift.
  **There is no edit path** — correction is void-and-re-enter (TD-68).
- **Imports:** CSV and Excel, with stable-key deduplication, in-batch duplicate detection and a
  review step that shows every row's disposition and the reason for it. **QIF is not supported.**
- **Receipt scanning:** on-device OCR and receipt detection, per-field confidence taken from the
  engine's own word scores, locale-driven date parsing that flags an ambiguous date instead of
  guessing, and duplicate-receipt warning. Nothing reaches the ledger without explicit confirmation.
  **The receipt image itself is not stored yet.**
- **Categorisation:** an offline ML.NET categoriser whose model is encrypted per vault.

### 🚧 Not built, or built and not working
- **Budgeting:** the screen exists and computes nothing — the progress bar is hard-coded to zero
  (TD-99). This is the sharpest open defect in the product surface.
- **Categorisation & Tagging in the UI:** `Category`, `Tag` and `Rule` exist as entities and tables;
  there is no rule engine and no tag UI (TD-38, `31-Rule-Engine` is marked Aspirational).
- **Analytics & Charts:** not started.
- **Export:** an encrypted vault export command exists but has no UI entry point, and there is no
  restore path (TD-56).

## 6. Technical Pillars
- **Clean Architecture:** Strict separation of Domain, Application (CQRS), Infrastructure, and UI.
- **Domain-Driven Design (DDD):** Rich domain models (`AggregateRoot`, `ValueObject`) ensure business logic is pure and testable.
- **CQRS via MediatR:** All operations are segregated into Commands (writes) and Queries (reads).
- **Avalonia UI:** A single, unified XAML codebase that compiles to native UI on all platforms.

## 7. Monetization Strategy (Optional Future)
Since Pine is open-source and local-first, the core app is **free forever**.
Future monetization could include:
- **Pine Sync (Optional):** A paid end-to-end encrypted sync service for users who don't want to manually move their vault file between devices.
- **Premium Themes:** Additional UI themes and icon packs.
- **Donations/Sponsorships:** GitHub Sponsors or Patreon for ongoing development.
