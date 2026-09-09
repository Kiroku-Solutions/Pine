# 41 — Localization

| Field            | Value                                                                                |
|------------------|--------------------------------------------------------------------------------------|
| Document ID      | `PINE-DOC-41-LOCALIZATION`                                                            |
| Status           | Aspirational — no localisation infrastructure exists                                 |
| Reconciled       | 2026-09-08 · Localisation now EXISTS and TD-45 is closed. There is still not one `.resx` file, deliberately: §5 mandates JSON keyed by stable string IDs, and the audit's `.resx` suggestion was overridden by this document — see the design spec's §3.3. The `es` glossary §8.4 requires is §18 below. |
| Owner            | Engineering Review Board                                                             |
| Created          | 2026-07-08                                                                           |
| Last Updated     | 2026-07-08                                                                           |
| Specification Standard | Unicode CLDR + ICU                                             |
| Glossary         | `PINE-DOC-03-GLOSSARY`                                                               |
| Prerequisites    | `00-Vision`..`40-Accessibility`                                                       |
| Related Documents| `03-Glossary`, `24-Contributing`, `40-Accessibility`, `42-UX-Guidelines` |

---

## 1. Purpose

Define Pine's localization (l10n) and internationalisation (i18n)
practice: how strings are extracted, translated, validated, and
shipped; how currency, dates, numbers, and names are formatted;
how RTL is handled; how locale leads are organised.

## 2. Scope

In scope: i18n posture, string extraction, translation workflow,
RTL, formatting (currency, date, number, names), locale leads,
translation memory, glossary localisation, pseudo-locale build,
marketplace posture.

Out of scope: visual design tokens (lives in the design system);
accessibility interactions with translations → `40-Accessibility`;
translation community governance → `24-Contributing` §15.

## 3. Definitions

| Term             | Working definition                                                                                |
|------------------|------------------------------------------------------------------------------------------------------|
| Locale           | A language + region identifier (e.g. `en-GB`).                                                     |
| i18n             | The discipline of making a product support multiple locales.                                        |
| l10n             | The discipline of adapting a product for a specific locale.                                         |
| CLDR             | Unicode Common Locale Data Repository.                                                              |
| ICU              | International Components for Unicode.                                                               |
| PL               | Pseudo-Locale (e.g. `en-XA`, `ar-XB`).                                                            |
| RTL              | Right-to-Left text direction (e.g. Arabic, Hebrew).                                                 |
| Bidi              | Bidirectional text layout.                                                                          |
| TM               | Translation Memory.                                                                                  |

> **Assumption LOC-A-1.** Pine ships *baseline* locales (English
> plus four priority locales) at v1.0 and grows the locale set
> through the contribution model.

## 4. Supported Locale Matrix

### 4.1 First-Wave Locales (v1.0)

| Locale | Code     | Direction |
|--------|----------|------------|
| English (US)     | `en-US` | LTR        |
| English (UK)     | `en-GB` | LTR        |
| Spanish          | `es-ES` | LTR        |
| Spanish (LatAm)  | `es-419`| LTR        |
| German           | `de-DE` | LTR        |
| French           | `fr-FR` | LTR        |
| Portuguese (BR)  | `pt-BR` | LTR        |
| Italian          | `it-IT` | LTR        |
| Japanese         | `ja-JP` | LTR        |
| Arabic           | `ar-SA` | RTL        |
| Hebrew           | `he-IL` | RTL        |

The set is revisited at major releases.

### 4.2 Locale Scope

- A locale ships *complete* UI translations before claiming the
  locale badge.
- Incomplete locales are tagged *in-progress*.
- An *untranslated* locale falls back to English (US) with a
  warning on first launch.

## 5. String Extraction and Storage

- UI strings live in `.po` / `.json` resource files keyed by stable
  string IDs.
- A string ID is **never** reused for a different meaning.
- A renamable *alias* is allowed to preserve translation memory.
- Strings NEVER include markup.
- Strings are extracted by a tool on every commit; missing strings
  fail the build (for the languages tagged *complete*).

### 5.1 ICU MessageFormat

- Pluralisation follows ICU MessageFormat.
- Date/time formatting follows ICU date patterns.
- Number formatting follows ICU number patterns.

## 6. Currency, Date, Number Formatting

- Currency formatting follows the user's locale.
- A user may override the display currency independently of the
  underlying account currency.
- The application's *primary* currency for a profile is set on
  first use; the user can switch it afterwards.
- The account stores the **native currency** of the underlying
  source; reporting respects both.

### 6.1 Calendar

- Default: Gregorian.
- Hijri calendar support is opt-in per user.
- Japanese era support is opt-in per user.
- Fiscal year customisation is per user.

### 6.2 Week Start

- Default: locale-specific (Mon in EU, Sun in US).
- A user can override the week start.

## 7. RTL Posture

- The UI mirrors fully in RTL.
- Icons that imply direction (e.g. arrow) are mirrored.
- Numbers and LTR-strings inside RTL text are handled via bidi
  isolation.
- Charts reorient for RTL.

### 7.1 Bidi Discipline

- Embedding is automatic; explicit bidi overrides are reviewed.

## 8. Translation Workflow

### 8.1 Pipeline

```mermaid
graph LR
    S[Source strings (en-US)] --> TM[Translation memory]
    S --> P[Pre-translate]
    TM --> P
    P --> L[Locale lead]
    L --> R[Review]
    R --> B[Build]
    B --> V[Visual QA]
    V --> SH[Ship]
```

### 8.2 Locale Lead

- A locale lead is named per locale (per `24-Contributing` §15).
- The locale lead is the *final reviewer* for that locale.
- The locale lead may delegate but retains ownership.

### 8.3 Translation Platform

- A translation platform houses the strings and TM.
- The platform exposes a CLI for offline editing.

### 8.4 Glossary

- A locale-specific glossary extends the global glossary
  (`03-Glossary`).
- The glossary drives per-locale terminological consistency.

## 9. Pseudo-Locale Build

- A pseudo-locale build is shipped for QA.
- Pseudo-locales:
  - `en-XA` (accented English).
  - `ar-XB` (RTL English).
- The pseudo-locale build exposes: untranslated strings, layout
  breakage, RTL bugs.

## 10. Source-Language Discipline

- Source-language strings are written in **en-US**.
- Source-language strings avoid idioms and wordplay.
- Source-language strings keep sentences short and clinical.
- Source-language strings preserve the glossary terms.

## 11. Accessibility Intersection

- The translation plate respects accessibility text expansion (the
  UI allows up to 200% expansion before truncating).
- Right-to-left UI maintains focus order and tab order.
- Screen reader pronunciation respects locale; per-locale phonetic
  spellings are stored.
- A locale-aware accessibility test runs per release.

## 12. Marketplace and Plugins

- The marketplace listing is translated.
- A plugin that ships UI strings MUST provide at least the source
  locale; *additional* locales are recommended.
- A plugin's locale set is declared in the manifest.

## 13. Quality Bar

- A new UI surface MUST be extracted into the translation resource
  set before merge.
- A new locale MUST have a locale lead and a glossary before merge.
- A pseudo-locale build MUST pass before release.
- A locale marked *complete* MUST be 100% translated before
  release; *in-progress* locales may be partial with an in-app
  fallback warning.

## 14. Cross-Cutting Constraints

- A first-party UI string MUST NOT be hardcoded; it MUST flow
  through the resource pipeline.
- A new locale MUST NOT be enabled without a locale lead and a
  glossary.
- A locale marked *complete* MUST NOT contain untranslated strings
  on release.

## 15. Deferred Decisions

| ID        | Decision                                                                                  | Owning document(s)         |
|-----------|-------------------------------------------------------------------------------------------|----------------------------|
| LOC-D-1   | Translation platform final choice (open-source vs. managed).                              | `41-Localization` (next rev.) |
| LOC-D-2   | Currency conversion default (live vs. snapshot).                                          | `13-AI-Architecture`       |
| LOC-D-3   | Whether to ship Indic and SE-Asia locales in v1.0.                                        | `41-Localization` (next rev.) |
| LOC-D-4   | Whether to support non-Gregorian calendars as default for any locale.                     | `41-Localization` (next rev.) |

## 16. Quality Checklist

- [x] Locale matrix, extraction, formatting, RTL, workflow,
      pseudo-locale, source-language, accessibility intersection,
      marketplace, quality bar are all present.
- [x] CLDR + ICU named.
- [x] No TODO placeholders.
- [x] No contradiction with `00-Vision`..`40-Accessibility`.

## 18. Spanish Glossary (`es`)

> Added 2026-09-08, at the glossary gate §8.4 requires and the localisation
> design's §10.1 names. It was agreed with the product owner **before** the
> 210 UI strings were extracted, which is the whole point of the gate:
> discovering a terminology disagreement afterwards means revisiting every
> screen instead of one table.

**Register: neutral Latin American Spanish.** Chosen over peninsular
Spanish because `es-EC` is the locale the OCR field extractor already
works in, and because the design ships a neutral `es` (D7) that
`es-ES`, `es-419` and `es-MX` all resolve to. Where the two registers
differ the neutral term is used: *transacción* rather than *movimiento*,
*estado de cuenta* rather than *extracto*, *recibo* rather than *ticket*.

| English | Spanish (`es`) | Note |
|---|---|---|
| vault | bóveda | The encrypted database file, not a folder. |
| account | cuenta | A financial account, never a user login. |
| transaction | transacción | Neutral; *movimiento* is peninsular. |
| balance | saldo | Never *balance*, which reads as a balance **sheet**. |
| opening balance | saldo inicial | |
| budget | presupuesto | |
| receipt | recibo | Neutral; *ticket* is peninsular and an anglicism. |
| ledger | libro mayor | |
| statement | estado de cuenta | Neutral; *extracto* is peninsular. |
| void (a transaction) | anular | A verb. The ledger anula rather than *elimina* — nothing is deleted, which is the domain rule. |
| import | importar | |
| category | categoría | |

**These twelve are binding on every `es` string.** A translation that
uses a synonym for one of them is a defect, not a style preference:
`41-Localization` §8.4 makes the glossary the thing per-locale
consistency is measured against.


## 17. Revision History

| Version | Date       | Author                | Change                                                       |
|---------|------------|-----------------------|--------------------------------------------------------------|
| 0.1     | 2026-07-08 | Engineering Review Board | Initial localization document.                               |
