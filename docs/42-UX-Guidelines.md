# 42 — UX Guidelines

| Field            | Value                                                                                |
|------------------|--------------------------------------------------------------------------------------|
| Document ID      | `PINE-DOC-42-UX-GUIDELINES`                                                          |
| Status           | Aspirational — not verified against any screen                                       |
| Reconciled       | 2026-08-31 · No test, review step or manual pass checks a view against these guidelines. The manual plan (`61`) covers behaviour, not visual or interaction conformance. |
| Owner            | Design + Engineering Review Board                                                   |
| Created          | 2026-07-08                                                                           |
| Last Updated     | 2026-07-08                                                                           |
| Specification Standard | Internal (UX practice)                                                       |
| Glossary         | `PINE-DOC-03-GLOSSARY`                                                               |
| Prerequisites    | `00-Vision`..`41-Localization`                                                       |
| Related Documents| `00-Vision`, `26-Diagrams`, `40-Accessibility`, `41-Localization`, `31-Rule-Engine` |

---

## 1. Purpose

Define the user-experience (UX) principles, the visual identity,
the design tokens, the interaction patterns, and the design
discipline of Pine.

## 2. Scope

In scope: principles, visual identity, design tokens, information
architecture, interaction patterns, charts, notifications, error
states, empty states, onboarding, progressive disclosure, name
discipline.

Out of scope: accessibility principles → `40-Accessibility`;
localized text contracts → `41-Localization`; rule engine UX
surface → `31-Rule-Engine`.

## 3. Definitions

| Term             | Working definition                                                                                |
|------------------|------------------------------------------------------------------------------------------------------|
| UX               | User experience.                                                                                     |
| UI               | User interface.                                                                                     |
| Design system    | The shared set of tokens, components, and patterns.                                                  |
| Token            | A named visual or behavioural variable.                                                              |
| Component        | A reusable UI building block.                                                                       |
| Pattern          | A reusable interaction shape.                                                                       |
| Information architecture | The way content is grouped and labelled.                                                  |
| Progressive disclosure | Revealing detail on demand.                                                               |
| Sketch           | A wireframe or low-fidelity mockup.                                                                |
| Mockup            | A high-fidelity visual representation.                                                              |
| Prototype         | An interactive mockup.                                                                              |

> **Assumption UX-A-1.** Pine ships with a *minimal* first-party
> UI and an *open* design system; the design system is documented
> and the source is published.

## 4. Principles

UX principles are derived from the Vision principles (per
`00-Vision` §3) and refined for UX:

| ID  | Principle                                                                                              |
|-----|----------------------------------------------------------------------------------------------------------|
| UP-01 | **Local-first.** UI works fully offline.                                                                |
| UP-02 | **Plain language.** No jargon, no upsell, no dark patterns.                                              |
| UP-03 | **Transparency visible.** Surfaces: which rule fired, why a category, what sync state, what encryption. |
| UP-04 | **Calm by default.** Notifications are quiet, non-blocking, dismissable.                                   |
| UP-05 | **Reversible.** Destructive actions are preceded by confirm; undo where possible.                        |
| UP-06 | **Respectful of attention.** Animations are brief; no autoplay; no motion tricks.                          |
| UP-07 | **One screen, one job.** A screen does one thing well.                                                    |
| UP-08 | **Predictable.** The same action always produces the same result.                                         |
| UP-09 | **Accessible.** WCAG 2.2 AA throughout the first-party UI (per `40-Accessibility`).                       |
| UP-10 | **Forgiving.** Errors are explained, not thrown.                                                         |

## 5. Visual Identity

### 5.1 Tone

- Calm, clinical, friendly.
- Avoid gaming, achievement, streak.
- Avoid financial anxiety triggers (e.g. red color for *negative*
  without reason).

### 5.2 Logo and Marks

- Logo: defined in the design system.
- Marks follow the brand guidelines (deferred OD-09 in
  `00-Vision` §13).

### 5.3 Colour

The palette maps to the design system. The palette is consistent
with the accessibility tokens in `40-Accessibility` §6.

### 5.4 Typography

- Sans-serif family, with a serif for long-form content (optional).
- Sizes use a rem-relative scale.
- Line height 1.5 default.

## 6. Design Tokens

The design system publishes a *single source* for tokens:

| Group     | Examples                                                                                |
|-----------|-------------------------------------------------------------------------------------------|
| Colour    | `surface/default`, `surface/raised`, `text/primary`, `accent/positive`                   |
| Spacing   | `space/x1`, `space/x2`, `space/x4`, `space/x8`                                           |
| Radius    | `radius/small`, `radius/medium`, `radius/full`                                            |
| Type      | `font/body`, `font/heading`, `font/mono`                                                  |
| Elevation | `elevation/0`, `elevation/1`, `elevation/2`                                              |
| Motion    | `motion/duration/quick`, `motion/easing/standard`, `motion/reduced`                     |

The tokens are *implemented* in the code base and *themed* per
platform.

## 7. Information Architecture

Pine's top-level structure:

```
Home
├── Dashboard
├── Transactions
├── Budgets
├── Goals
├── Recurring
├── Investments
├── Reports
├── Imports
├── Sync (if configured)
├── Settings
│   ├── Profile
│   ├── Categories
│   ├── Accounts
│   ├── Privacy
│   ├── Plugins
│   ├── Notifications
│   ├── Appearance
│   ├── Locale & Region
│   ├── Backup & Restore
│   └── About
└── Diagnostics (under Settings)
```

### 7.1 Naming Discipline

- Labels are short nouns or verb phrases.
- Labels are translated (per `41-Localization`).
- Labels avoid jargon (e.g. "Recurring" instead of "Recurrence
  Set").
- Labels are consistent across the UI (same label, same thing).

## 8. Interaction Patterns

### 8.1 Forms

- Forms label every field.
- Forms validate inline.
- Forms surface errors next to fields, not at the top.
- Forms save on explicit submit; autosave for notes only.

### 8.2 Lists

- Lists are virtualised.
- Lists expose stable sort, stable filter, and stable grouping.
- Lists have a search affordance when the dataset permits.

### 8.3 Tables

- Tables sort, filter, and re-group.
- Tables are responsive.
- Tables export via the user's `archetype` (per `14-Synchronization`
  §3).

### 8.4 Charts

- Charts re-orient for RTL.
- Charts label series clearly.
- Charts expose drill-down where useful.
- Charts support colour-blind palettes.
- Charts support keyboard navigation between data points.

### 8.5 Notifications

- In-app, mobile, email (opt-in), webhook (plugin).
- Each notification has a body, an action, a dismiss.
- Snooze supported per `31-Rule-Engine` §9.

### 8.6 Empty States

- Every list has an empty state.
- The empty state explains the *next* step.

### 8.7 Error States

- Errors explain *what* happened and *what* to do.
- Errors cite a code (per `34-API-Guidelines` §8).
- Errors do not display a stack trace; the DIB does.

## 9. Onboarding

### 9.1 First Run

- First run explains: local-first, encryption, sync opt-in,
  telemetry opt-in.
- First run never asks for payment or cloud signup at the gate.
- First run is skippable for power users.

### 9.2 Nudges

- Nudges explain the *why* and link to a doc page.
- Nudges respect quiet hours.
- Nudges are off by default; users opt-in per category.

## 10. Progressive Disclosure

- Defaults hide advanced options behind a "Show advanced" toggle.
- Defaults surface the common path.
- Power users opt into more advanced detail.

## 11. Plugins and the UI

- Plugin panels live under Settings → Plugins.
- Plugin panels are clearly labelled as "by \<author\>" with the
  plugin's risk score visible.
- A plugin panel is hidden when the plugin is disabled.

## 12. Cross-Platform Consistency

- A button is a button across platforms.
- A list is a list across platforms.
- Differences are platform-native (menu bar, system tray, etc.).
- Differences are documented in the design system.

## 13. Documentation Discipline

- Every screen has a *documentation page* that describes its
  purpose and its primary actions.
- Every component has a *storybook entry* (or platform equivalent).
- Every pattern has a *rationale* recorded in the design system.

## 14. Quality Bar

- A UI PR MUST include a sketch or mockup.
- A UI PR MUST pass the visual QA in the pseudo-locale build.
- A UI PR MUST pass the accessibility gate (per
  `40-Accessibility`).
- A new screen MUST have a documentation page before merge.

## 15. Cross-Cutting Constraints

- A first-party UI MUST NOT use dark patterns.
- A first-party UI MUST NOT include auto-playing motion.
- A first-party UI MUST follow the design tokens.
- A first-party UI MUST respect the accessibility guidelines.

## 16. Deferred Decisions

| ID        | Decision                                                                                  | Owning document(s)         |
|-----------|-------------------------------------------------------------------------------------------|----------------------------|
| UX-D-1    | Default theme (light / dark / system).                                                    | `42-UX-Guidelines` (next rev.) |
| UX-D-2    | Whether to ship a "Frequent" set vs. a "Pinned" set on home.                              | `42-UX-Guidelines` (next rev.) |
| UX-D-3    | Typography face final choice.                                                              | `42-UX-Guidelines` (next rev.) |
| UX-D-4    | Whether to ship an optional "Pro" UI variant.                                             | `42-UX-Guidelines` (next rev.) |

## 17. Quality Checklist

- [x] Principles, identity, tokens, information architecture,
      patterns, charts, notifications, error states, empty
      states, onboarding, progressive disclosure, plugins,
      cross-platform consistency, docs are all present.
- [x] No TODO placeholders.
- [x] No contradiction with `00-Vision`..`41-Localization`.

## 18. Revision History

| Version | Date       | Author                | Change                                                       |
|---------|------------|-----------------------|--------------------------------------------------------------|
| 0.1     | 2026-07-08 | Engineering Review Board | Initial UX guidelines.                                       |
