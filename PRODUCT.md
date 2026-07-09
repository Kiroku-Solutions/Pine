# 🌲 Pine - Product Definition

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
- **100% Local & Encrypted:** Your data never leaves your device. It is stored in a local SQLite database encrypted with SQLCipher (using military-grade AES-256) and an Argon2id key derivation function.
- **No Subscriptions, No Clouds:** You own the software and the data. If you want to sync it across devices, you can manually share the encrypted vault file using your own cloud provider (Drive, Dropbox, iCloud) without exposing the contents.
- **Write Once, Run Everywhere:** Built with Avalonia UI and .NET 10, Pine runs natively on Windows, macOS, Linux, iOS, Android, and the Web.
- **Clean & Fast:** A lightning-fast, S-Tier UI with glassmorphism and modern aesthetics that makes managing money enjoyable.

## 4. Target Audience
- **Privacy Advocates:** People who refuse to use cloud-based financial trackers.
- **Tech-Savvy Users:** Developers and power users who appreciate local-first software and encryption.
- **Frugal Budgeters:** Users who don't want to pay a $10/month subscription to manage their money.
- **Digital Nomads:** People who need an offline-capable app when traveling without internet access.

## 5. Core Features (Phase 1 & 2)
### ✅ Phase 1: Foundation (Current)
- **Vault Security:** Master password creation, Argon2id key derivation, and SQLCipher integration.
- **Accounts System:** Create and track different account types (Cash, Checking, Savings, Credit).
- **Transactions Engine:** Double-entry accounting system to register Income and Expenses, automatically updating account balances.
- **S-Tier UI:** A premium, dark-mode glassmorphic interface.

### 🚧 Phase 2: Intelligence (Upcoming)
- **Categorization & Tagging:** Organize transactions with custom colors and icons.
- **Budgeting:** Set monthly limits per category and track progress.
- **Analytics & Charts:** Interactive graphs showing net worth growth, cash flow, and spending habits over time.
- **Import/Export:** Support for CSV and QIF files to migrate from other apps.

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
