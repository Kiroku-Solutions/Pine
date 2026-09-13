# 60 · Production Readiness Audit

| Field | Value |
|---|---|
| Document ID | `PINE-DOC-60-AUDIT` |
| Status | Non-normative — audit and remediation log                                                                                                     |
| Reconciled| 2026-09-09 · A record of findings and of the work done against them, through **iteration 37**, which closed the three findings of the audit that was run across all four phases once Phase 3 was ticked. **The phases were genuinely closed — 37 of 37 rows — and the app was not, and the audit's own conclusion was the last thing still saying otherwise.** **(1) The worst defect still shipping was import.** `ExcelImporter` and `PdfImporter` opened the file, walked it, and returned an empty list **reporting success**, while the picker offered "Spreadsheets" and "PDF Statements" by name — a user could select their bank statement, be told it worked, and receive nothing. **The register had it at 🟡 P2 because of a mitigation a later iteration had removed**: TD-67 said the stubs were unreachable "because the wizard hardcodes `CsvImporter`", and TD-28 wired them up five iterations later while fixing something else. Both of TD-67's branches were taken — **Excel implemented** against the same profile mapping and key derivation CSV uses, **PDF deleted** along with its `PdfPig` reference, because a PDF statement has no columns and no two banks agree on layout. **(2) TD-99 closed after nineteen deferrals**: budget spend is measured rather than hard-coded to zero, and where the `Guid`-versus-string category join cannot be resolved the screen says **not known** instead of inventing a number — which is the whole point of the row. **(3) TD-56's export half closed**, and beside it a shipped sentence that was false: the panel called the backup "a **decrypted** backup" when `VACUUM INTO` on the keyed connection encrypts it with the same key, as an existing test already proved. The restore half stays open and keeps the row P1. **(4) §11 was rewritten.** It still read 2.3/5, "11.7k lines, 28 tests", "no unit of work, no concurrency control", and named as the most important next action the work TD-01 finished in Phase 1. It is now **3.4/5, still not production ready** — the difference being that the blockers are a bounded punch list, which is exactly what the original said they were not. **The original verdict is preserved verbatim as §11.2** rather than overwritten. Three mutations, three killed; M1's first attempt patched the wrong `return` and looked like a survivor, and "the tests are vacuous" would have been the wrong conclusion. `BuildHygieneTests` caught this iteration's own orphaned `PdfPig` version. 1004 → **1049** unit, **234** integration; floors raised to overall 68, `Pine.Application` 59, `Pine.Ui.Avalonia` 38. New row: TD-148. See **§12.76**. **TD-07 is now the largest single gap in the project: nothing arms any of this automatically.** Through **iteration 36**, which closed **Phase 3 item 6 and with it PHASE 3**. **The row had two defective premises, and one of them was this document's own.** The empty projects it asks to delete were already gone — commit `9771a85`, iteration 27, verified in git — and **TD-38 had already recorded the retirement branch as satisfied**, on the argument that deleting them made the two plugin documents "more precisely `Aspirational`". That does not hold: `Aspirational` is defined as "nothing in the tree does this — **it may still be a good plan**", a claim about scaffolding plus an open question about the plan. Deletion sharpened the first and left the second untouched, which is exactly why this checkbox sat unticked for nine iterations while a debt row called it done. **The decision is ADR-0021, Accepted: Pine will not load third-party code.** The deciding argument is not effort but impossibility: `12-Plugin-Architecture` §5 loads a plugin "in an isolated load context", which in .NET is an `AssemblyLoadContext` and **is not a security boundary**, while `15-Security` §10 requires that "a plugin has no access to the data key" — and `VaultSession.Key` returns the live key array by deliberate design. §5's mechanism cannot deliver §10's requirement, and the design that could is out-of-process isolation, a subsystem larger than this application. Two invisible collisions surfaced on the way: **a plugin could violate ADR-0012 while `NetworkEgressTests` stayed green**, because that test scans `src/` and a plugin is not there; and **nothing in this repository can verify a signature**, so the mandatory manifest signature described a check no code could perform. **A fifth document status, `Withdrawn`, was added — the one iteration 27 declined to invent — and it carries a price**: a document may only claim it if its Status row names an ADR that resolves to a file. **Sync was re-homed rather than retired alongside it**, since §12.72 had left it gated on this row; `ISyncProvider` becomes `14-Synchronization`'s own and providers are compiled in. Three mutations: **M1 survived its first run and exposed a real hole in the new rule** — it accepted an incidental ADR-0012 mention elsewhere in the header — and was killed once the rule was narrowed to the Status row. **No source file changed; there was nothing left to remove.** 1003 → **1004** unit, **234** integration. New row: TD-147. See **§12.74**. **Phase 3 is closed: six rows, five of which had a false or stale premise, and the sixth had two.** Through **iteration 35**, which closed **Phase 3 item 5** — the gate in front of sync. **The row is a precondition, not a feature**: it forbids sync until row versions, audit columns and stable natural keys exist, so the work was to establish all three and build the two that did not. Row versions were already real and enforced (TD-17). **Audit columns were not, and unevenly**: `Transaction` had a creation stamp and no update stamp, `Category`, `Setting` and `Budget` had neither, and the five roots that had any disagreed on both name and type — they are now shadow columns on all eight roots stamped by the context, so no future write path can forget them. **The `Row` prefix is load-bearing**, because two roots already own a `CreatedAtUtc` and a colliding shadow name binds silently to the domain property and creates nothing; that exact collision is mutation M6 and it kills seven of eight tests. **TD-60 is closed, and the row understated where the loss happened**: the repository never passed a child id at all, so loading a transaction and saving it back unchanged moved every child's primary key — the defect needed no edit to fire. Closing it forced the retraction of a load-bearing comment: `ReconcileOwnedChildren` promoted every `Modified` child because "neither type exposes a mutator", and both types have one now. **The third precondition is still not true as the row words it** — what exists is stable SURROGATE keys, and the only unique constraint in the schema is `Settings.Key` (TD-146), so the gate is not recorded as fully open when a third of it is only mostly open. **Nothing beyond the gate was built, and that is the row obeying itself**: `02-Product-Scope` §7.2 scopes sync to plugins only, `14-Synchronization` §8 hands the provider contract to `33-Plugin-SDK`, and the plugin host is the next row in this phase and does not exist — so item 6 now gates sync outright rather than being an independent choice. Also missing and registered: a logical clock (TD-144) and tombstones (TD-145). **Nine mutations, nine killed.** The coverage gate blocked again — `Pine.Core` fell below its floor because the new domain code was covered only by integration tests — and was answered with thirteen unit tests. 990 → **1003** unit, 219 → **234** integration; floors raised to overall 67 and `Pine.Infrastructure` 78. See **§12.72**. Through **iteration 34**, which closed **Phase 3 item 4** — the rule engine. **The row's premise was false, and this document had already recorded why, twice**: there is no "now-real `CategoryId` relationship" to build on — `TransactionSplit.Category` is a `string`, iteration 29 rejected changing that in writing (TD-32, TD-132) because nothing in production writes a category, and iteration 27 flagged the same premise when it deleted the old `Rule` shell. **Fourth consecutive Phase 3 row whose stated premise did not survive contact with the code.** The engine is built on the name, because that is the model with instances. **TD-132 decided the scope before any code was written**: a `Rule` aggregate with no way to write a rule would have been that row's defect again — the third time, after `Attachment`, `Rule` and `Tag` were deleted for it under TD-37 — so the rules screen is not an extra, it is why the engine is allowed to exist, and it makes the sidebar's disabled "Coming soon" Rules button real. **A matching rule beats the classifier outright and the model is not consulted at all**, because a user's priority and a softmax confidence share no units and mixing them makes "why is this in Coffee?" unanswerable; a rule never overwrites a category the user typed or the file supplied. Ordering is **total** — §6.2 settles nothing when two rules share a priority, so ties break by creation time then id, and the repository applies the same order in SQL. Six of §5.2's fourteen operators, conditions ANDed rather than a boolean tree, one action — all declared deviations. **Seven mutations, six killed and one survived**: nothing tested the regex timeout, so a user-authored pattern could hang an import; closed with a test that races the match against a deadline, because asserting on it directly would hang the suite rather than fail it. **The coverage gate blocked this iteration too** and was obeyed rather than lowered — 937 → **990** unit, 212 → **219** integration, floors raised to overall 66, `Pine.Core` 88, `Pine.Infrastructure` 77, `Pine.Application` 57, `Pine.Ui.Avalonia` 37. New rows: TD-142, TD-143. See **§12.70**. Through **iteration 33**, which closed **Phase 3 item 3** — attachment storage. **The row's phrasing was exact**: scanning a receipt produced a transaction and destroyed the document it came from, because the image was decoded for the screen and then dropped. **The storage decision went against the plan this document itself recorded.** TD-37 wrote it down as "an encrypted blob written through `IVaultFileCipher`"; that cipher's own contract says it is "**the wrong shape for a large attachment. Say so before reusing it for one**", and `VaultBackupService` runs `VACUUM INTO` and **copies `pine.db` and nothing else** — so a receipt beside the database would have been absent from every backup, for the one feature whose purpose is an audit trail. The bytes are a BLOB in the vault, encrypted by SQLCipher and committed in the same unit of work as the ledger row. The SHA-256 §7 demands is computed by the aggregate and **verified on every read**. **Seven mutations, five killed and two survived — and both survivors were real holes**: nothing tested that the scanner attaches anything (reverting to discarding the image broke no test), and nothing tested that the read path refuses a tampered file. Both closed; both mutations now die. **The coverage gate blocked this iteration for the first time**, at 36% against a floor of 37%, and was obeyed rather than lowered. Suite 903 → **937** unit, 205 → **212** integration; floors raised to overall 65, `Pine.Core` 87, `Pine.Infrastructure` 75. New row: TD-141. See **§12.68**. Through **iteration 32**, which closed **Phase 3 item 2** — localisation. **The row read as infrastructure and the defect underneath it was a shipped control that did nothing**: a "Display Language" picker offering English and Spanish, persisting a number into the vault and changing not one character on screen, because nothing in `src/` ever read that key. Its two siblings on the same panel each applied AND persisted; it only persisted. Implemented against an already-approved design spec, which **overrode this document's own `.resx` prescription** in favour of `41-Localization` §5 — TD-45's Fix column is corrected. **260 keys**, `en` and `es`, switching live with no restart; the preference stored OUTSIDE the vault so the pre-unlock screens follow it; the glossary agreed BEFORE the strings rather than across them. **Three risks were declared unverified and none of them was the one that bit**: dotted keys work, and the real finding was that **no binding in this test suite had ever been evaluated** — `SetupWithoutStarting()` starts no dispatcher, so `Bind` succeeds and the property reads back `null`, in silence. Also **Avalonia does not listen for `Item[]`**, the BCL convention the design assumed. **Four of five parallel agents died mid-edit on a rate limit**, leaving keys with no values; recovered from `git HEAD` after the first recovery pass produced wrong English and was discarded — 205 exact, 6 reported, 0 guessed. The §14 guard test then found **nine more literals the manual count had missed**. Seven mutations, **7 killed**. Suite 865 → **903** unit, **205** integration; coverage ratchet turned, `Pine.Ui.Avalonia` 28 → **37**. New rows: TD-139, TD-140. See **§12.66**. Through **iteration 31**, which opened **Phase 3** by closing its first row: the platform story. **The decision is to narrow.** The Browser head is deleted — project, solution entry, CI leg, package pin, enum member, capability row and test rows. Three technical grounds hold, and **the decisive one was in no row at all**: `02-Product-Scope` has listed browser-only distribution as an explicit non-goal since before that head was scaffolded, so it contradicted this project's own scope document from day one. Two register entries were corrected on the way — **TD-53's stated cause was wrong** (`SQLitePCLRaw` does ship a `browser-wasm` asset; the build was never asking for it), and **§12.63's "it has never compiled on any machine" was false** — it was built here, 0 errors, before deletion, which is exactly why deleting beat leaving it as source. The row's *invest* branch was priced instead of dismissed: the macOS and Linux runtime packages for OpenCV and PaddleInference **exist at the exact versions already pinned**, four lines in total, and were still not taken — **the blocker is a runner, not a package**, and an untested `true` in the capability table is worse than an honest `false`. Android and iOS gained real application identities, closing half of TD-109. **The iteration's loudest finding was wrong, and the falsification pass is what caught it**: a missing `Batteries_V2.Init()` on three heads was written up as a vault-breaking defect, one mutation refused to die, and a clean-process probe showed `Microsoft.Data.Sqlite` arms the bundle by reflection. It was relabelled rather than patched into looking right. **TD-117's abort is closed** as an enabler — the first two full runs here aborted, and without the fix no number in this iteration could have been measured. Seven mutations, 5 killed, 1 survived deliberately, 1 needed three runs to kill. Suite 874 → **865** unit (the count fell with the deleted head's profile), **205** integration, both green; coverage gate re-run and passed. New rows: TD-137, TD-138. See **§12.64**. Through **iteration 30**, which closed **Phase 2 item 9** and with it **Phase 2**. The row read as an afternoon — pick a number, add a flag — and neither half existed. **The suite went red the moment coverage was measured**: coverlet injects a tracker type into each instrumented assembly and NetArchTest threw `TypeLoadException` materialising it, so iteration 25's reflection allow-list failed under `--collect` and passed without it — a failure visible only in the configuration about to be made mandatory. The architecture rules are now scoped to the `Pine` namespace and the suite is 873/873 under instrumentation. And no threshold flag was available: `coverlet.collector`'s own documentation states the VSTest integration lacks "threshold validation", and `coverlet.msbuild` is neither referenced nor cached — so `scripts/coverage-gate.sh` parses the cobertura report itself. **No exclusions** — EF migrations, code-behind and generated properties all count, making every figure lower than a tuned configuration would report. Floors are a ratchet set just below measured: overall 61, `Pine.Core` 86, `Pine.Infrastructure` 74, `Pine.Application` 55, `Pine.Ui.Avalonia` 28. The script was **executed end to end**, not written and assumed. **Its honest limit is TD-07**: nothing triggers the workflow, so every regression net Phase 2 built into that file is inert (TD-136). Five mutations, 4 killed, **1 survived and exposed a real hole** — the guard test was reading the gate's name out of a CI *comment* — and its first re-run was a false pass caught only by reading the build output. Suite 873 → **874** unit, **205** integration, both green. **Phase 2 is closed: two of its nine rows were closed by finding the row's own premise wrong, and the phase's largest finding was in no row at all.** New rows: TD-135, TD-136. See **§12.62**. Through **iteration 29**, which closed **Phase 2 item 8** (TD-32, TD-33) and found that the row filed as a modelling complaint was hiding **a feature that had never once run**. The categoriser's training projection parsed a split's category NAME as a `Guid` and discarded everything that failed, so the training set was empty on every poll, `TrainModelAsync` was never reached and the watermark was never written — with no exception, no log line and no red test. The same mismatch broke the other end: both the suggestion and the import paths resolved a predicted `Guid` through a `Categories` table **nothing in this application has ever inserted a row into**, so every prediction was discarded. **TD-32's prescribed remedy was rejected in writing** — storing a `CategoryId` on the split needs categories to exist, and nothing creates one (TD-132) — so the string won, because it is the model with instances. The row's second clause was taken verbatim: `ParentCategoryId` now has a real `Restrict` foreign key with a migration and six integration tests, written as integration tests because a SQLite foreign key does nothing unless `PRAGMA foreign_keys` is on. TD-33 was closed by **removal**: no dispatcher ever existed on any branch, the two mutations that raised events already commit their effects synchronously, and **the deleted types did not implement `10-Domain-Model` §5 anyway**. Eight mutations, 7 killed, 1 survived and was fixed — and the decisive one restored the exact shipped projection and turned 6 of 7 tests red, which is the difference between claiming the defect and demonstrating it. Suite 863 → **873** unit, 199 → **205** integration, both green. New rows: TD-132, TD-133, TD-134. See **§12.60**. Through **iteration 28**, which closed **Phase 2 item 7** (TD-41, TD-42, TD-43) and found that **all three of that row’s sub-claims needed correcting** — one was already fixed, one was impossible as written, and one was false in a way that took three attempts to measure. Central package management moved every version into `Directory.Packages.props`, closing a real `EFCore.Sqlite.Core` 10.0.9 / `Design` 10.0.11 split that NuGet had been unifying silently. TD-41’s “keep one” could not be done — `Models.Local` is a facade whose own `.nuspec` requires `Models.LocalV5` — so the answer is two, with `LocalV3` and `LocalV4` (472 MB) removed. **The largest finding is not in the row at all:** `PP-OCRv4-det.onnx` (113 MB), `PP-OCRv4-rec.onnx` (90 MB) and `dict.txt` were shipped in every artefact by a glob and named by **no commit on any branch in the repository’s history**, having been superseded by the PP-OCRv5 weights embedded in the NuGet assembly before they were ever wired. The artefact went **1.08 GiB → 658 MiB unpacked, 680 MB → 292 MiB compressed**, measured by running `package.sh`. **And an agent sent to verify TD-43 returned a confident refutation that was wrong** — caught only because a second run of the same pipeline produced an impossible result, exposing a silently broken `awk` that had reported “no blob over 500 KB” while three blobs of 113 MB, 90 MB and 13 MB sat in `.git/objects`; TD-130 registers the class. Ten mutations, 9 killed, 1 survived by design, 1 discarded as invalid — and one kill only counts because the mutation exposed a real gap in the test, which was then fixed and re-falsified. Suite 855 → **863** unit, **199** integration, both green. New rows: TD-129, TD-130, TD-131. See **§12.58**. Through **iteration 27**, which closed **Phase 2 item 6** (TD-36, TD-37, TD-38) by deleting seven projects, three entities and one dead inference session — and found that two of that row’s own claims were wrong, and that the compiler already refused two of the three ways the deleted code could return. Through **iteration 26**, which closed **Phase 2 item 5** (TD-34, TD-35), found and reproduced the cause of **TD-117**, and watched a mutation survive all 853 tests — proving the suite had never checked that navigation actually initialises a page. Through **iteration 25**, which closed **Phase 2 item 4** (TD-26) by deleting the reflection call that wrote half of every transfer’s link — and which found, in doing so, that `Transaction` had never carried the public-surface tripwire `Account` has had since iteration 6. Through **iteration 24**, which closed **Phase 2 item 3** (TD-28) and TD-58 with it — and which began by discovering, with a throwaway experiment, that the two layer-dependency assertions this repository has relied on since early on were **conjunctive and therefore near-vacuous**. Through **iteration 23**, which closed **Phase 2 item 2** (TD-27) and half of TD-81, after iteration 22 closed **Phase 2 item 1** and, with it, TD-29, TD-52, TD-72, TD-83, TD-85 and TD-90 — six rows removed by removing one cause. It is the document that required all the others to carry a status, and §12.44 is where it was turned on the four root documents — `README.md`, `STATUS.md`, `PRODUCT.md`, `CHANGELOG.md` — that the first pass could not see. |
| Created | 2026-08-26 |
| Audited commit | `c1c8844` on `feat-ui-production-theming` |
| Method | Full source read + `dotnet build` + `dotnet test` execution. No claim below is taken from documentation. |
| Scope | 154 `.cs` / 26 `.axaml` · ~11.7k LOC `src/` · 741 LOC `tests/` · 17,292 lines `docs/` |
| Overall rating | **2.3 / 5 ★★☆☆☆** |
| Classification | **Not Production Ready** |
| Remediation | Iterations 1–6 closed Phase 0 items 1–8. Iteration 7 **built** item 9 (TD-11) without executing it — see §12.16. Iteration 8 closed the TD-18 half of item 10 — see §12.18. Iteration 9 closed the TD-19 half, ported the TD-11 harness into `tests/`, and reclassified TD-74 — see §12.20. **Every Phase 0 item has now been worked, and items 1, 4–11 are ✅. Phase 0 is NOT clean: item 2's export is still unreachable and item 3 regressed — CI has had its triggers removed since `79c5364` and has never run.** **Phase 1 items 5 and 6 are closed**: 5a (TD-30, TD-31, TD-79) by iteration 15, 5b (TD-17) by iteration 16, and item 6 was found **already closed** by iterations 8 and 9 — it duplicated Phase 0 item 10. Iteration 17 did what item 6 had actually left undone: carry the money scale policy into the UI, and stop the dashboard adding different currencies together. See §12.32, §12.34 and §12.36. That iteration also found **TD-94**, a shipped crash on the transaction list screen, under a fully green suite. Test suite 28 → **769** (582 unit + 187 integration), both suites green at this commit. See **§12**. §12.12 is a reconciliation pass correcting stale §8/§9 claims; §12.14, §12.16, §12.18, §12.20, §12.22, §12.32, §12.34 and §12.36 repeat it as standing practice. **Iteration 18 closed Phase 1 item 7 (TD-20, TD-21, TD-22)** — receipt duplicate detection on an `s1:` key reusing the import index, locale-ordered date parsing that flags ambiguity instead of guessing, and per-field confidence bands taken from the OCR engine's own word scores in place of a literal tally. Eleven mutations, 10 killed, 1 survived by genuine redundancy. **Two `NaN` defects were found in that iteration's own new code, one of which crashed the scan and lived in the seam between two tasks under two green suites** — recorded in §12.38 as the clearest evidence yet that a green suite is not evidence of correctness. Suite 769 → **833** (643 unit + 190 integration), both green. **Phase 1 now has three rows left: items 8, 9 and 10.** New rows: TD-101, TD-102, TD-103. See **§12.38**. **Iteration 19 closed Phase 1 item 8 (TD-24), and TD-98 with it.** An exception now reaches a screen only if its author opted in via `IUserFacingFailure` and wrote the sentence — an allow-list, because the set of exceptions a request can throw is open and a deny-list is stale the moment anyone adds a `throw`. Eleven `ex.Message` renderings replaced; the detail moves to the TD-14 log rather than being deleted. TD-23 was re-verified in code and `UnlockViewModel` deliberately left alone, because its identical wordings are an anti-oracle property, not duplication. Eleven mutations, 11 killed — **two of which first failed to compile and were recorded as not-a-kill until rewritten**, §12.40. Suite 833 → **863** (673 unit + 190 integration), both green. **Phase 1 now has two rows left: items 9 and 10.** New rows: TD-104, TD-105, TD-106. See **§12.40**. **Iteration 20 worked Phase 1 item 9 (TD-49) and got half of it.** The row's premise was under it: no version was set anywhere, so every build stamped `1.0.0` and the resolver discarded the commit — a constant in the panel and in every log record. There is now a real version, a `pine --version` that proves it in a running process, and a packaging script that was **executed**: 1.78 GiB across 30 runtime-identifier folders became a 1.08 GiB RID-pinned artefact with 317/317 checksums verified, a release manifest, and a signing seam that refuses rather than pretends. CI gained a `package` job and the fix for a latent defect — no checkout set `lfs: true`, so it would have shipped 133-byte model pointers. **No installer, no certificate, no updater**, the last deliberately: it would be Pine's first network call. Seven mutations, 7 killed, two only after the tests were sharpened. Suite 863 → **876** (686 unit + 190 integration). **TD-97 was observed a fourth time, in a fourth different test.** **Phase 1 now has one row left: item 10.** New rows: TD-107, TD-108, TD-109, TD-110. See **§12.42**. **Iteration 21 closed Phase 1 item 10 (TD-39, TD-40), and with it Phase 1.** It began by *checking* the previous commit rather than trusting it: all 62 documents in `docs/` really are classified (25 Aspirational, **20** — not 19 — Non-normative, 17 Partially implemented, and **zero `Implemented`** — after twenty iterations not one specification fully matches the code), both ADRs really do match the code, and both suites really are green. **Five things underneath that were not.** `STATUS.md` — the file §6.12's contradiction table names *twice* — sat at the repository root outside the docs-only scan, seven iterations stale, still reporting 542 tests and still naming item 4 as next; ADR-0012's eight line-number citations were **every one of them wrong inside the commit that wrote them**, because that same commit's `Reconciled` row shifted each cited line down by one; ADR-0013 was silent about the second AEAD layer (`AesGcmVaultFileCipher`, HKDF-SHA256, purpose-separated); ADR-0001 had never carried the front matter `25-ADR` §4.3 makes mandatory, so its status was unreadable by any rule; and `18-Test-Strategy` cited 686 unit tests against 691. `README.md`, `STATUS.md`, `PRODUCT.md` and `CHANGELOG.md` are now inside the enforced rule and rewritten to what is true; ADRs cite by quotation instead of by line number, with a test that keeps the quotation honest; and **ADR-0012's own risk row is closed** by `NetworkEgressTests`, so "no network I/O at all" is a property the build holds rather than a claim three documents repeat. Eleven mutations, **11 killed**, including the two that would otherwise have let the suite pass *vacuously*. Suite **881** — not the 876 §12.42 recorded, which undercounted its own iteration by five — → **891** (701 unit + 190 integration), both green. **Phase 1 is closed — and every count in this document is still a local claim, because TD-07 is still open.** New rows: TD-111, TD-112, TD-113. See **§12.44**. **Iteration 22 closed Phase 2 item 1 (TD-29) and five rows with it — TD-52, TD-72, TD-83, TD-85 and TD-90 — none of which was worked directly.** The five composition roots were never five platform configurations; they were five copies of one, each edited at a different time by whoever was closing a different ticket, and the diff between them was the record of which edits had been forgotten where. Windows registered `PineDbContext` **transient** against scoped on the other four, which is **TD-01 re-entering through the composition root** and had been true since iteration 1. `AddPineInfrastructure()` and `PineCompositionRoot` replace all five; the heads went from 733 lines carrying ~120 registrations to **206 lines carrying none**. The enabling change is one lambda: the `PineDbContext` factory now reads `IVaultDataKeyAccessor` (Pine.Core) instead of `IVaultSession` (Pine.Application), which is the only reason it had to be written in the heads at all. **TD-90 was deliberately not fixed the way its row prescribed** — registering the AI stack per head would have moved the failure from a managed exception to a P/Invoke into win-x64 binaries four heads do not ship, so `PortableAiProvider` categorises and refuses vision by name on the TD-24 taxonomy. `ValidateOnBuild` is now on in all five, having been off in all five because of one dead registration. The root lives in `Pine.Ui.Avalonia` **because that is the only place a test can reach it**, which is what closes TD-83. Ten mutations, **10 killed** — after the first run of them was discarded outright, because the harness did not preserve mtimes and MSBuild was silently skipping the rebuild, so each run was partly measuring the previous mutation. Suite 891 → **952** (762 unit + 190 integration), both green. **Android and iOS still compile on no machine**, and TD-97 was observed a fifth time. New rows: TD-114, TD-115, TD-116. See **§12.46**. **Iteration 23 closed Phase 2 item 2 (TD-27) and half of TD-81.** The row names thresholds, paths and KDF parameters, and they turned out to be three different problems: a threshold is a number read on every run, a path is a per-installation fact, and a KDF cost is read **once per vault, ever**, and then written into that vault's own header. `PineOptions` — four sections in `Pine.Core`, still with zero package references — binds from an optional `appsettings.json` and `PINE_*` environment variables, validates once at startup with **every** problem in one exception, and is delivered as `IOptions<T>`. **The KDF is configurable for new vaults only, and nothing on the unlock path may read it**: a configured cost consulted at unlock would derive a different key for every existing vault, with a symptom that is *by design* indistinguishable from a wrong password (TD-23). That separation is pinned by a reflection test over the four unlock-path types and by a real-Argon2id integration test that mints under one profile and unlocks after the configuration changes. Ten literals moved, and the two that mattered were duplicates nobody had noticed: the `0.3` confidence gate existed in **two** files where only one of them writes to the ledger, and `%LOCALAPPDATA%/Pine` was computed independently in `VaultManager` and in `PineLoggingOptions`. `PineLoggingOptions` and `VaultAutoLockOptions` were deliberately NOT converted — each carries a written reason not to be an `IOptions` binding and both still hold; configuration seeds them instead. The config file lives in Pine's data directory and **not** in the artefact, because `package.sh` checksums every file it ships. Thirteen mutations, **13 killed — one of them only after a survivor exposed a real gap**: the tests asserted that both handlers *took* the options and not that they *used* them, so reverting one to its literal left the suite green. An agent also corrected this document: §6.2's "page size 50" was **unreachable** — the UI paged at 30 — so the dead default was deleted rather than made configurable. Suite 952 → **991** (798 unit + 193 integration), both green; the unit suite **aborted once at 717 and was re-run three times** before that number was stated. New rows: TD-117, TD-118, TD-119. See **§12.48**. **Iteration 24 closed Phase 2 item 3 (TD-28), and TD-58 with it — after first having to repair the test the row asked it to extend.** `ArchitectureTests` has carried two layer-dependency assertions since early on, both written as `ShouldNot().HaveDependencyOn(A).And().HaveDependencyOn(B)`. That chain is **conjunctive** in NetArchTest: it fails only for a type depending on every listed namespace at once, so `ApplicationLayer_ShouldNot_DependOnInfrastructureOrUI` passed while `Pine.Application` genuinely depended on a listed namespace. An agent flagged it as suspicious and correctly marked it UNVERIFIED; it was settled by a throwaway experiment, not by reading. Both are rewritten as `NotHaveDependencyOnAny`, and the anti-vacuity guard that one test carried is now on all of them. The row proper: `IViewModelFactory` names all fifteen pages in methods and `INavigationService` takes the page rather than a type argument, so an unregistered page fails where it is *created* instead of on the user's click; `IServiceProvider` is gone from all four view models and from `MainWindow`; `IVaultDatabaseOpener` took the DI scope, the `PineDbContext` probe and the migration out of two view models; and `Microsoft.EntityFrameworkCore.Sqlite.Core` left the UI `.csproj` because nothing there needed it. **The rule is scoped to the view models and views rather than the assembly, which is the decision §12.47 and §12.49 both said to make deliberately** — with the rejected alternative written into the test's own remarks, and an explicit one-entry allow-list naming the only type permitted to touch infrastructure. Two defects were found on the way that this row does not mention: the import wizard handed **every** file type the picker offers to `CsvImporter`, and `DashboardViewModel` hard-coded `TimeProvider.System` over the registered clock. Ten mutations, **10 killed** — but one was recorded a not-a-kill until it compiled, one was discarded as a no-op that changed no behaviour, and the one true survivor exposed that `ScopedVaultDatabaseOpener` **was executed by no test at all**, which four new integration tests now fix. Suite 810 → **824** unit, 193 → **197** integration, both green. **TD-117's abort was observed ten times in one day and now has a signature.** Android and iOS still compile on no machine. New rows: TD-120, TD-121, TD-122, TD-123. See **§12.50**. **Iteration 25 closed Phase 2 item 4 (TD-26).** `TransferFundsCommandHandler` linked a transfer’s two legs by two different mechanisms — the destination through a constructor argument, the source through `GetProperty(...).SetValue(...)` against an already-`private` setter, with the author’s thinking-aloud comments still in the file. The cost was not the rename hazard the row names: `VoidTransactionCommandHandler` follows the link outward from whichever leg the user clicked and never does a reverse lookup, so **each direction could be lost independently and either loss leaves money withdrawn from one account and never returned to the other**. Neither remedy §6.2 proposed was taken — a `LinkTo` mutator would have made the one-way link easier to write, and a single aggregate needs a migration. `Transaction.CreateTransferPair` generates both identifiers up front so each leg is constructed already carrying the other’s, and `Transaction.Create` lost its link parameter, so a half-linked transfer has no production expression at all. `Transaction` also gained the public-surface tripwire it never had, and the set of `Pine.Application` types allowed to touch `System.Reflection` is now an explicit one-entry allow-list. Ten mutations, **10 killed** — one only after being rewritten, because its first form discarded its `GetProperty` result and therefore emitted no reflection dependency to catch, making it invalid rather than a survivor; that is the **second iteration running** in which a mutation had to be thrown out for changing no behaviour. Suite 824 → **837** unit, integration unchanged at **197**. **TD-97 was observed a seventh time, in a seventh different test.** New rows: TD-124, TD-125. See **§12.52**. **Iteration 26 closed Phase 2 item 5 (TD-34, TD-35).** The dedicated document for this defect, `58-TechDebt-WeakReferenceMessenger-Lifecycle`, turned out to have **nine wrong factual claims** — a sender that no longer exists, two it never listed, and a prescribed remedy referencing a method iteration 24 deleted; it has been rewritten to cite by quotation rather than by line number. Its recommended mechanism was also wrong for a reason that postdates it: activation is driven from the `CurrentPage` property hook, not from `Loaded`/`Unloaded`, because **`shell.CurrentPage = null!` in the vault-lock path bypasses `NavigateTo` entirely** — the one path where leaving a recipient subscribed matters most. An agent found the trap inside that decision by emitting the generated source: for partial properties CommunityToolkit calls the two-parameter `OnCurrentPageChanging` with `default` as the old value, so the obvious overload would have deactivated nothing while compiling and passing every test. Eleven types moved to `ObservableRecipient`, **zero manual `Register` calls remain**, `ObservedTask` ended twenty-five silent discarded tasks, `IAsyncInitializable` took I/O out of nine constructors, and all four `async void` handlers are gone. Two unrecorded defects were fixed on the way: the settings page **silently stopped saving anything for the rest of the session** after one failed read, and the transaction list reloaded twice on every void. Nine mutations, **9 killed — but one survived the entire 853-test suite first**: deleting the initialisation call from `PageActivation` left everything green, because the suite proved `InitializeAsync` worked and proved activation registered, and never that the two were connected. **TD-117 is no longer unexplained** — a native access violation in PaddleOCR predictor creation under xunit parallelism, reproduced three times, diagnosed but deliberately not fixed. Suite 837 → **854** unit, integration unchanged at **197**. TD-97 was observed three more times, in three more tests. New rows: TD-126, TD-127. See **§12.54**. **Iteration 27 closed Phase 2 item 6 (TD-36, TD-37, TD-38)** — the first destructive item, so it began by establishing that every candidate file was tracked and unmodified and therefore recoverable. **Two of the row’s own claims were wrong:** the `Microsoft.ML.OnnxRuntime` package is not removable, because `ReceiptDetector` genuinely runs inference over a model that is present; and the extractor class is not dead, only its session was — four references, never once used for inference. A third was half wrong: the stub test projects were built by CI and **never run**, as the workflow’s own comment explained. The decisive argument for dropping the three entities was not that they were unused but that **`Attachment` had no `Hash` property while `10-Domain-Model` says "Hash MUST be set"** — the entity already contradicted its own specification, so keeping it preserved a wrong guess rather than a head start. §10 Phase 3 had made the plugin deletion conditional on "formally retiring the two plugin documents"; that was satisfied without inventing a status value, because `Aspirational` already means "nothing in the tree does this". Six mutations, **4 killed, 2 invalid** — and the invalid ones taught the most: `CS0169` and `CA1001` are promoted to errors here, so the build itself now refuses two of the three ways the dead session could return, and only the third needed a new test. **A mistake here truncated the new migration file to zero bytes** — the one artefact git could not have restored — found by checking rather than by a failing test, rewritten and re-verified. Suite 854 → **855** unit, 197 → **199** integration. Ten documents reconciled. TD-97 observed twice more. New rows: TD-128. See **§12.56**. **Iteration 28 closed Phase 2 item 7 (TD-41, TD-42, TD-43), and every one of that row’s three sub-claims needed correcting.** `Microsoft.Extensions.DependencyInjection` was no longer drifting; the EF Core half was, and `Directory.Packages.props` closes it at 10.0.11 across the family, with `NU1008` turning a version in a `.csproj` into a build error and eight `BuildHygieneTests` covering what CPM itself cannot see. **TD-41’s “keep one” is not achievable**: `Sdcb.PaddleOCR.Models.Local` is a 36 MB facade holding the one type the OCR names, and its own `.nuspec` requires `Models.LocalV5 >= 3.0.0`, so removing the weights package would only float it to that floor — two is the graph’s minimum, and `LocalV3` + `LocalV4` (472 MB) were the genuine duplication. **The row’s smallest-sounding clause hid its largest finding:** `PP-OCRv4-det.onnx` (113 MB), `PP-OCRv4-rec.onnx` (90 MB) and `dict.txt` were copied into every build output by a glob and named by no commit on any branch, ever — superseded by the PP-OCRv5 weights embedded in the NuGet assembly before a line was written to load them. An agent given the explicit job of refuting the deletion failed to refute it. The artefact, re-measured by running `package.sh`, went **1.08 GiB → 658 MiB unpacked and 680 MB → 292 MiB compressed**. **TD-43 produced the iteration’s real lesson:** a subagent returned a confident “FALSE, refuted” that was wrong, and so did this document’s own first scan, because a silently broken `awk` pipeline reported no large blobs while three of 113 MB, 90 MB and 13 MB sat loose in `.git/objects`, reachable only from the reflog — so the conversion did leave residue, and “every clone pays forever” was still never true. TD-130 registers that failure mode against every pipeline-derived number in this file. Ten mutations, **9 killed, 1 survived by design, 1 discarded as invalid**, and one kill counts only because the mutation exposed a real gap that was then fixed and re-falsified. Suite 855 → **863** unit, integration unchanged at **199**. TD-117 observed a fifteenth time. New rows: TD-129, TD-130, TD-131. See **§12.58**. **Iteration 29 closed Phase 2 item 8 (TD-32, TD-33), and TD-32 turned out to be the most consequential row in the phase.** It is filed as "categories modelled twice", a tidiness complaint about renames. What it actually described was a seam where the ledger spoke a string and the categoriser spoke a `Guid`, with `Guid.TryParse` on a human-typed category name as the adapter — a parse that cannot succeed. **The training set was therefore empty on every single poll since the feature was written**: `TrainModelAsync` was never called, the watermark was never advanced, and nothing anywhere reported it. The other end failed identically — the suggestion path and the import path both resolved the model's predicted id through a `Categories` table that **no code path in this application has ever written a row to**, so every prediction resolved to `null` and was dropped, however confident. The two halves concealed each other. **The row's prescribed direction was not taken**: storing a `CategoryId` on the split requires categories to exist, and `CreateCategoryCommand` has no caller, no query lists categories, and every category input in the UI is a free `TextBox` (TD-132) — so the string won, being the model with instances, and the reasoning is in the code rather than only here. The row's second clause was taken as written: `Category.ParentCategoryId` had no foreign key in either the fluent config or the original `CreateTable` and now has a `Restrict` one, with a migration and six integration tests — written as integration tests deliberately, because **a SQLite foreign key is inert unless `PRAGMA foreign_keys` is on**, which no mapping assertion could detect. Two behaviours there were measured rather than assumed: EF nulls a tracked child's link and deletes the parent where the database would refuse (TD-134), and a cycle remains possible because a foreign key is a per-row guarantee. TD-33 was closed by **removal** — no dispatcher exists on any branch, the two mutations that raised events already commit their effects in the same handler and transaction, and the deleted records did not implement `10-Domain-Model` §5's specified events anyway. `AggregateRoot` survives as an empty marker with tests that give it meaning. **One test was deleted and that cost is recorded.** Eight mutations, **7 killed, 1 survived and exposed a real hole that was then fixed** — a blank category could have been offered to the user at full confidence. Mutation 1 restored the exact shipped projection and turned 6 of 7 tests red. Suite 863 → **873** unit, 199 → **205** integration. TD-97 observed in a fourteenth distinct test; one background run was killed by the OS for low memory and produced no result, which is recorded so it is not mistaken for a failure. New rows: TD-132, TD-133, TD-134. See **§12.60**. **Iteration 30 closed Phase 2 item 9 — the coverage floor — and with it Phase 2.** The row's remaining half read as an afternoon's work and turned out to contain two obstacles, neither of them the number. **First, the suite goes red the moment coverage is measured**: coverlet instruments by rewriting IL and injects a `Coverlet.Core.Instrumentation.Tracker` type into each assembly, and NetArchTest's `GetTypes()` throws `TypeLoadException` materialising it — so iteration 25's reflection allow-list failed under `--collect:"XPlat Code Coverage"` and passed without it, which is the worst shape a failure can have: **it appears only in the configuration you are about to make mandatory**. Scoping the whole-assembly queries to the `Pine` namespace makes instrumentation invisible to the rules without weakening any of them, and the suite is 873/873 under coverage. **Second, no threshold mechanism exists here**: an agent found the decisive line in `coverlet.collector`'s own bundled documentation — the VSTest integration does not support "threshold validation" — and `coverlet.msbuild`, which does, is neither referenced nor in the package cache. So `scripts/coverage-gate.sh` reads the cobertura report itself. **It carries no exclusions by decision**: EF migrations, Avalonia code-behind and source-generated properties all count, so every figure is lower than a tuned configuration would report, which is the honest direction to be wrong in. The floors are a ratchet set just below measured — overall 61 (61.93), `Pine.Core` 86 (87.27), `Pine.Infrastructure` 74 (74.44), `Pine.Application` 55 (55.83), `Pine.Ui.Avalonia` 28 (28.51) — with the domain carrying the highest because it carries the money, and the per-assembly split existing because a single overall floor would let 8,284 lines of UI absorb a regression in 2,624 lines of domain. The script was **executed end to end**, per §12.42's standard. **Its value is nonetheless deferred entirely by TD-07**: nothing triggers the workflow, so the coverage gate, the packaging job, the platform-head builds and the suite itself are all inert there (TD-136). Five mutations, **4 killed, 1 survived and exposed a real hole** — the guard test matched the gate's filename inside a CI *comment*, so neutering the step left it green — and that mutation's first re-run was a **false pass**, because a `CA1865` analyzer error failed the build and `--no-build` re-ran the previous binary; it was caught only by reading the build output rather than the test result. Suite 873 → **874** unit, integration unchanged at **205**. TD-117 observed a seventeenth time. **Phase 2 is closed**, and the phase's own record is worth stating: two of nine rows were closed by discovering the row's premise was wrong, a third found its prescribed remedy impossible, and the largest finding of the phase — a categoriser that had never been trained — was in no row at all. New rows: TD-135, TD-136. See **§12.62**. |
| Last updated | 2026-09-08 |
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
- ✅ **P1** Service locator in the UI (TD-28) — **CLOSED, iteration 24, §12.50.** `MainViewModel`, `ShellViewModel`, `SetupViewModel` and `UnlockViewModel` injected raw `IServiceProvider`, and two of them resolved `PineDbContext` out of a scope they created themselves — the UI reaching for EF Core and triggering `Migrate`. All four now take `IViewModelFactory`; the probe and the migration live behind `IVaultDatabaseOpener`; the UI project no longer references EF Core at all. `grep -rn "IServiceProvider\|GetRequiredService\|GetService\|CreateScope" src/Pine.Ui.Avalonia` returns hits only under `Composition/`, and three architecture tests hold it there.
- 🟡 **P1** Transient `IDisposable`s resolved from the root provider are never released. No DI scope is ever created; the root provider is never disposed. Every command leaks a `DbContext` + connection for the session. Fixed by the same change as TD-01.
- 🟢 Dependency direction enforced by `ArchitectureTests` — **caveat**: written as `ShouldNot().HaveDependencyOn(a).And().HaveDependencyOn(b)`, which composes as "not *all* of" rather than "not *any* of". Prefer `NotHaveDependencyOnAny(...)`.

### 6.2 .NET engineering — 2.5

**Modern and correct:** `net10.0`, `Nullable=enable`, `ImplicitUsings`,
`TreatWarningsAsErrors=true` in `Directory.Build.props` with a clean build.
`TimeProvider` injected everywhere. `[GeneratedRegex]` throughout the extractor and
`CurrencyCode`. `CancellationToken` on essentially every async signature. Records
for commands/DTOs, sealed classes for aggregates.

- 🟠 **P1** No logging abstraction outside one class (TD-14). `ILogger` appears only in `CategorizationTrainingBackgroundService`. Infrastructure logs via `Console.WriteLine` (`OnnxNlpFieldExtractor.cs:125`, `PaddleOcrEngine.cs:107,113`, `CropReceiptViewModel.cs:90`) and `File.AppendAllText(AppContext.BaseDirectory + "ocr_init_error.log")`. The registered provider is `AddConsole()` in a **WinExe** — no console, records go nowhere.
- ✅ **P1** Reflection bypasses domain encapsulation (TD-26) — **CLOSED, iteration 25, §12.52.** `TransferFundsCommandHandler` wrote the second half of a transfer’s link with `typeof(Transaction).GetProperty(nameof(Transaction.LinkedTransactionId))!.SetValue(sourceTx, destTx.Id);`, preceded by the comment *"Wait, source needs to link to destination as well."* — the handler thinking aloud, left in the file. Neither remedy this bullet proposed was taken literally: a `LinkTo` mutator would have made a one-way link *easier* to write, and a single aggregate needs a schema change. `Transaction.CreateTransferPair` instead makes both legs together, already pointing at each other, and `Create` lost its link parameter — so a half-linked transfer is now unconstructible rather than merely discouraged.
- 🟡 **P2** No central package management; versions drifting (TD-42) — **closed, iteration 28.** Half of this bullet was already stale when it was reached: `Microsoft.Extensions.DependencyInjection` sat at `10.0.11` in every project. The EF half was real — `EFCore.Sqlite.Core 10.0.9` against `EFCore.Design 10.0.11`, both pulling the same `Relational`, unified silently by NuGet so that no file a reader could open said what was actually built. `Directory.Packages.props` now holds every version, and the family resolves at 10.0.11.
- 🟡 **P2** Relative model paths resolved against CWD (TD-46). `MlNetCategorizer` and `HeuristicFieldExtractor` use relative paths while `ReceiptDetector` correctly uses `AppContext.BaseDirectory`. Launching from another directory silently disables categorisation and writes the retrained model wherever the shell was.

### 6.3 Avalonia — 3.0

The most polished layer. `CommunityToolkit.Mvvm` with modern
`[ObservableProperty] public partial` syntax, `[RelayCommand]`, a ViewLocator-style
navigation service, five runtime accent palettes as swappable merged dictionaries,
thin code-behind. Argon2 correctly pushed off the UI thread. No god objects — the
largest VM (`LiveScannerViewModel`, 384 lines) is doing camera work that belongs to it.

- 🟠 **P1** Diagnostics panel displays fabricated data (TD-15) — see §4.10.
- ✅ **P1** Fire-and-forget async in constructors and messenger handlers (TD-35) — **CLOSED, iteration 26, §12.54.** The inventory found **25** discarded tasks, nine of them started from a constructor, and nine whose callee had no `catch` at all. Nothing in the process observed any of them: there is no `TaskScheduler.UnobservedTaskException` handler and no `<ThrowUnobservedTaskExceptions>`, so a faulted discarded task was collected in silence. `ObservedTask` now routes every one through `UserFacingError`, `IAsyncInitializable` moved the constructor loads onto a lifecycle the navigator drives, and the four `async void` handlers are sync methods that observe their work. Original finding:  `ScanReceiptViewModel` ctor: `_ = LoadAccountsAsync();` and `WeakReferenceMessenger.Default.Register<ReceiptCapturedMessage>(this, async (r,m) => …)` — `async void` semantics. Four `async void` handlers in code-behind (`ScanReceiptView`, `CropReceiptView`, `ImportWizardView`, `DiagnosticsView`). An exception in the messenger handler takes the process down.
- ✅ **P2** Messenger subscriptions never unregistered (TD-34) — **CLOSED, iteration 26, §12.54.** Already documented in `58-TechDebt-…`, and correctly diagnosed there: `WeakReferenceMessenger` does not root the recipient, so the symptom is stale VMs acting on messages rather than a classic leak. What that triage understated is that **three of the seven handlers captured `this`**, which defeats the weak reference entirely and made those three genuinely immortal. All seven are now `IRecipient<T>` registered by `ObservableRecipient`, which cannot capture, and deregistration is deterministic.
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
- ✅ **P0 — CLOSED in iteration 4, and reclassified on the way; see §12.9.** Transaction currency never checked against account currency (TD-12). `AddTransactionCommandHandler` builds `Money` from the request, **saves the transaction**, then calls `account.Deposit(...)` which throws on mismatch inside `Money.Add`. Result: an orphaned ledger entry with no matching balance movement. `TransferFundsCommandHandler` is the only handler that validates currency up front. **[Correction, iteration 4: the orphaned-entry claim in the preceding sentence was tested against a real vault and could NOT be reproduced — closing TD-01 had already made it impossible. The residual defect was a wrong-layer throw with a leaky message. Baseline text left intact per this document’s convention; see §12.9.]**
- 🟠 **P1** No currency scale or rounding policy on `Money` (TD-18). No normalisation to minor units, no `MidpointRounding`, no `Multiply`, no `Allocate`. `CurrencyCode` validates the ISO-4217 *shape* (`^[A-Z]{3}$`) but not membership — `"ZZZ"` is accepted — and carries no minor-unit metadata. A total of `10.005` is storable.
- 🟠 **P1** Line items not reconciled against the total (TD-19). `AddLineItem` checks currency only. Nothing compares `Σ TotalPrice` to `TotalAmount` in the domain or in `ScanReceiptViewModel.SaveTransactionAsync` — even though the extractor already computes that ratio, and uses it only to nudge a confidence score.
- 🟡 **P2** Categories modelled twice (TD-32). `Category` is a full aggregate with `Id` and `ParentCategoryId`; `TransactionSplit.Category` is a `string`, and `CategorizeTransactionQueryHandler` resolves the predicted `CategoryId` back to `category.Name` to store as text. `ParentCategoryId` has no FK configured — no referential integrity, cycles possible. — **Closed by iteration 29, and this bullet understated it.** The round trip it describes ran against a table with no writer, so it always produced `null`; and the reverse direction, `Guid.TryParse` on a category name in the training projection, meant **the categoriser was never trained at all**. The split still holds a string, deliberately (§12.60). `ParentCategoryId` now has a real `Restrict` FK; cycles are still possible and there is now a test that says so.
- 🔵 **P2** Inconsistent aggregate modelling (TD-33). `Account`/`Transaction`/`Category` derive from `AggregateRoot`; `Budget` derives from nothing and redeclares its own `Guid Id`. `Budget`'s `Money` uses a value converter while the others use nested `ComplexProperty` — two mapping strategies for one value object. Domain events are raised on two aggregates and **dispatched nowhere**; `DomainEvents` is read by no code. — **The event half is closed by iteration 29 (removed, §12.60).** The other two clauses are NOT closed and are not the same problem: `Budget`'s separate shape is untouched, and its converter divergence is **deliberate** — it is the one place the read path tolerates a currency the current code would reject, pinned by `LegacyMoneyToleranceTests`. Unifying it would make a legacy vault unopenable. Three concerns should not share one id.
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
- ✅ **P2** Dead ONNX session (TD-36) — **CLOSED, iteration 27, §12.56.** `Ai/Extraction/OnnxNlpFieldExtractor.cs` defines `HeuristicFieldExtractor`; `LoadModel()` built an `InferenceSession` over `Assets/Ai/ner_minilm_int8.onnx` — **a file that has never been present in the repository** — and `_session` was referenced only at declaration, null-check, assignment and dispose. Never used for inference. (The `Console.WriteLine` this bullet mentions had already gone in iteration 11’s TD-14 work; by the time the row was worked the failure went to `ILogger` at warn.) The session, `LoadModel()` and `IDisposable` are gone, and `TheOnlyTypeUsingTheOnnxRuntime_IsTheOneThatRunsInference` holds the line. **The package reference stays** — see the roadmap row.
- 🟡 **P2** Swallowed exceptions; receipt image never kept. `DefaultLocalAiProvider.ExtractReceiptDataAsync` has a bare `catch { }`. `PaddleOcrEngine` catches init failures, writes `ocr_init_error.log`, sets `_engine = null` → OCR degrades to silence. The `Attachment` entity and table exist but the scan flow never stores the source image — removing the audit value of scanning receipts at all.

### 6.6 Persistence — 2.0

EF Core 10 over SQLCipher, migration-managed (a recent, correct fix —
`PineDbInitializer.Migrate` replaced an `EnsureCreated` that had produced a live
`no such table: TransactionLineItems` bug). Value objects via `ComplexProperty`,
enums as strings, unique index on `Setting.Key`. Right calls.

- 🔴 **P0** Owned collections silently dropped on update (TD-13). `SqliteTransactionRepository.SaveAsync` on an existing row does `_dbContext.Entry(existing).CurrentValues.SetValues(transaction);`. `SetValues` copies scalars and complex properties only — it does **not** touch `OwnsMany` collections, and `Splits` and `LineItems` are exactly that. Same pattern in the account, budget, category and setting repositories. Latent only because no update path exists yet.
- 🟢 ~~**P1** No optimistic concurrency token anywhere (TD-17).~~ **Fixed, §12.34.** Every aggregate root carries one. The reason this finding originally gave — the background trainer — was false, and is corrected there.
- 🟢 ~~**P1** SQLite has no native decimal (TD-31).~~ **Fixed, §12.32.** Money is a scaled integer; the comparison hazard is removed rather than documented.
- 🟢 ~~**P2** Missing indexes on the actual query paths (TD-30).~~ **Fixed, §12.32** — with a correction the original finding missed entirely. `GetPagedAsync` did not merely sort by an unindexed `Date`: the provider **refuses** to translate an `ORDER BY` over a `DateTimeOffset`, so that method threw every time the transaction list opened (TD-94).
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

- ✅ ~~**P0** Password-derived key used directly; no envelope, no version marker (TD-05, TD-06)~~ — both closed; see §4.4 and §12.27.
- 🟠 **P0** No password policy, no auto-lock, no re-authentication (TD-11). `SetupViewModel` rejects only whitespace-empty passwords — a one-character master password is accepted. `VaultSession.Lock()` is called from exactly one place: the `catch` of a failed unlock. No idle timer, no lock-on-suspend, no lock button. The 32-byte key sits in a managed `byte[]` in a singleton for the entire process lifetime. Argon2id at 64 MB is meaningless against a one-character password.
- 🟠 **P1** ML model plaintext, cross-vault, outside the vault (TD-16) — see §4.9.
- 🟡 **P1** Unredacted diagnostic files (TD-25). `Program.Main` writes the full exception + stack to `%LOCALAPPDATA%/Pine/crash.log`; `PaddleOcrEngine` writes `ocr_init_error.log` next to the exe; `PaddleOcrEngine.cs:107` does `Console.WriteLine($"[OCR] Extracted: '{region.Text}'…")` — the full recognised text of every receipt. `docs/30-Observability.md` mandates a redaction posture no code implements.
- ~~🟡 **P2** Raw `ex.Message` surfaced to the user (TD-24) in `SetupViewModel`, `ScanReceiptViewModel`, `AddTransactionViewModel` and others. `UnlockViewModel` is the sole exception and does it correctly — apply that pattern everywhere.~~ **✅ Closed by iteration 19 (§12.40).** The count was eleven renderings, not "and others". The prescription — *apply `UnlockViewModel`'s pattern everywhere* — was **not** followed and should not have been: that screen's per-type cascade is correct precisely because several of its arms must say the SAME thing, which is a security decision that does not generalise. What generalised was the rule underneath it.
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
- **Three `UnitTest1.cs` stub projects** (TD-38) — **deleted, iteration 27.** This bullet said they were "built and run by CI, creating false green signal". Half wrong, and the workflow said so itself: it compiled them and deliberately did **not** run them, with a comment explaining that running an empty file "would be a green light over an empty file". The cost was build time and a false signal in the solution listing, not a green tick. A project that must be excluded from the thing it is named after carries a promise, not a test.
- **Four empty projects** backed by 646 lines of plugin documentation. Note `Pine.Ai.Default` is empty while the actual default AI provider lives in `Pine.Infrastructure` — the intended modularisation was documented, then not done.
- **Three dead entities** (`Attachment`, `Rule`, `Tag`) carried in the schema and migrations, referenced by no application or UI code (TD-37) — **dropped, iteration 27**, by `DropAttachmentRuleAndTagTables`. There were no foreign keys in either direction, so it was pure subtraction. The rule engine still has its 313-line specification and now has no entity at all, which is the honest state: `31-Rule-Engine` is retained and says so.
- **ONNX binaries tracked in git** without LFS (TD-43): `PP-OCRv4-det`, `PP-OCRv4-rec`, `yolo-receipt-nano`. Every clone pays forever. — **False as written, and measured false in iteration 28.** All three were LFS pointers from their first commit, and the reachable object graph holds **10.3 MiB of blobs in total**. No clone has ever paid. The raw blobs do exist loose in `.git/objects` — 190 MB of them — but only reachable from the reflog of the machine that made the conversion. Two of the three files were then deleted outright, for a reason this bullet never suspected: nothing had ever read them (TD-131).
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

- 🟡 **P2** Four overlapping PaddleOCR model packages referenced simultaneously (TD-41): `Models.Local` 3.3.1, `.LocalV3` 2.7.0.1, `.LocalV4` 2.7.0.1, `.LocalV5` 3.3.1 — plus three `.onnx` files committed separately. Each embeds full det+rec weights. Tens to hundreds of MB duplicated, and the effective model version is ambiguous to a reader. Keep one. — **Closed by iteration 28, at two packages rather than one**, because `Models.Local`'s own `.nuspec` requires `Models.LocalV5`. The diagnosis of ambiguity was exactly right and the answer is now legible: the OCR runs PP-OCRv5 Latin weights embedded in the `LocalV5` assembly. The `.onnx` half of this bullet was the bigger half — two of those three files were dead weight nothing had ever read.
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

**AMENDED, iteration 35 (§12.72).** Three of those five have since been fixed and this
paragraph is kept above as written rather than edited, because what it got right and what
it got wrong are both worth keeping. Concurrency tokens arrived with TD-17 (§12.34); audit
columns and stable owned-child identity arrived with Phase 3 item 5 (TD-60 closed). **Two
remain, and a third has been added by being looked at properly:** there is still no logical
clock (**TD-144** — the row version is a random GUID, which cannot order anything), balances
are still stored rather than derived, and "stable natural key" turns out to name something
this schema still does not have — identity is stable now, but surrogate, and the only unique
constraint anywhere is `Settings.Key` (**TD-146**). Deletion is the gap this paragraph never
mentioned: every delete is physical, so a row deleted on one device would be invisible to a
merge (**TD-145**). The sentence's advice was right and has been followed.

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
> Rows **TD-52 … TD-67** were discovered *during* remediation and were not in the
> original audit.

| ID | Debt | Sev | Impact | Action | Pri | Status |
|---|---|---|---|---|---|:---:|
| TD-01 | No unit of work; transient DbContext per repository | 🔴 | Non-atomic financial writes | Scope-per-request + `IUnitOfWork` | P0 | ✅ **Done** |
| TD-02 | No update/delete/void for transactions | 🔴 | Errors permanent; balances unrepairable | Void + balance recalculation (done); edit deferred as TD-68 | P0 | 🟡 **Partial** — a transaction can now be **voided** with a recorded reason and its balance effect undone atomically (transfers as a unit), and any account's balance can be **recomputed from its ledger** and repaired. The **edit** path is deliberately not built — a ledger voids rather than edits, and an update command would make TD-59/TD-60 load-bearing. See §12.14 |
| TD-03 | Dedup on (date, amount) only, silent | 🔴 | Legitimate transactions lost | Stable import key + review list | P0 | ✅ **Done** |
| TD-04 | `AddSplit` validates eagerly | 🔴 | Multi-split throws | Validate at aggregate boundary | P0 | ✅ **Done** |
| TD-05 | No KDF version marker beside the salt | 🔴 | Parameter change bricks all vaults | Versioned vault header | P0 | ✅ **Done** |
| TD-06 | ~~Key = password-derived key; no envelope~~ | 🔴 | ~~No password change, no biometric~~ | Wrapped data key | P0 | ✅ **Closed (iteration 13, §12.27)** — random data key wrapped with AES-256-GCM under the Argon2id auth key, bound to the header; password change shipped. Biometric is architecturally possible and **not built** (TD-87); the data key itself is still never rotated (TD-86) |
| TD-07 | CI workflow was **manual-only** (`workflow_dispatch:`). The `push`/`pull_request` triggers iteration 1 wrote were deleted in `79c5364`; iteration 6 took the second of the two options this row offered and recorded **CI is opt-in by an explicit project decision** (compute-minute cost). The post-audit plan reversed that decision: leverage, not cost, was the deciding term once the workflow gated five things it had not gated in `79c5364` | 🟠 | The regression net existed and was real, but nothing armed it: a broken head reached `main` unless a human dispatched the workflow. That was true for thirty-one iterations | **Done** (2026-09-10, plan item 0.1): `on:` now carries `pull_request: branches: [main]` and `push: branches: [main]`, keeping `workflow_dispatch`. `package` alone is held back from pull requests (a ~680 MB artefact no reviewer reads); every gate a reviewer needs runs. `BuildHygieneTests.CiIsArmedOnPullRequestAndPushToMain` reddens the suite if either trigger is deleted again — verified by deleting them and watching it fail | P1 | ✅ **Closed** — iteration 38; see §12.12, §12.14 and the note in `62-Post-Audit-Plan` §3 |
| TD-08 | Web/Android/iOS reference a non-existent namespace | 🔴 | 3 of 6 heads uncompilable | Fix or remove; add to CI | P0 | 🟡 **Partial** |
| TD-09 | Money-path tests cover atomicity, split-sum, owned-collection round trip, currency match, import dedup, void + balance recalculation, and — since iterations 8 and 9 — `Money` scale/rounding/`Allocate` (TD-18) and line-item reconciliation (TD-19) | 🟠 | The regression net now covers every closed P0 and both remaining money-path invariants; eleven falsification mutations across the two iterations, all killed | — | P1 | ✅ **Closed** — iteration 9, §12.20 |
| TD-10 | No backup before migration | 🟠 | Unrecoverable data loss | Pre-migration copy + export | P0 | 🟡 **Partial** |
| TD-11 | No password policy; no auto-lock | 🟠 | Key exposed on idle machine | Entropy floor + idle lock | P0 | 🟡 **Partial** — a 60-bit entropy floor derived from the shipped Argon2id parameters now gates vault creation, and a `TimeProvider`-driven idle watchdog locks the session and returns the user to the vault picker. **Verified out-of-suite**: a 25-check harness (not committed) watched the floor reject a one-character password, the idle lock fire on a fake clock, and the key zero; no test in `tests/` covers it, so nothing guards against regression. Re-authentication, lock-on-suspend and a persisted timeout remain unbuilt. See §12.16 |
| TD-12 | Currency not validated against account | 🟠 | Orphaned ledger entry — **measured: no longer reachable once TD-01 closed; the residual defect was a domain exception leaking to the UI** | Validation behaviour | P0 | ✅ **Done** |
| TD-13 | `CurrentValues.SetValues` drops owned collections | 🔴 | Latent silent data loss on edit | Tracked-graph updates | P0 | ✅ **Done** |
| TD-14 | No `ILogger`; `AddConsole` in a WinExe — in **two** heads (Windows and macOS), not one | 🟠 | No incident evidence | Structured file logging + redaction | P1 | ✅ **Done** — iteration 11, §12.24. One `AddPineLogging()` in all five roots writing JSONL to `%LOCALAPPDATA%/Pine/logs`; every `Console.WriteLine` and `File.AppendAllText` outside `Pine.Cli` is gone, and a NetArchTest forbids `Pine.Infrastructure` from depending on `System.Console` so they cannot come back |
| TD-15 | Diagnostics screen shows mock data | 🟠 | Misleads during incidents | Wire to real sinks or remove | P1 | ✅ **Done** — iteration 12, §12.25. Every value on the page is now measured: `ILogTailReader` over the TD-14 JSONL files, `IDatabaseHealthProbe` over the keyed connection, `IRuntimeInfoProvider` for version/OS/heap. The two dishonest buttons (`Export DIB`, `Seed Demo Data`) are removed rather than reimplemented. Seven falsification mutations, all killed |
| TD-16 | ML model plaintext, cross-vault, outside vault | 🟠 | Privacy-claim violation | Per-vault encrypted model | P1 | 🟢 **Closed** (§12.29) |
| TD-17 | No optimistic concurrency token | 🟠 | Lost updates | Row version on aggregates | P1 | 🟢 **Fixed** (§12.34). **AMENDED, iteration 35 (§12.72): this row's mechanism was reused wholesale, and its own forward-looking comment was checked and found correct.** The audit columns Phase 3 gates sync behind are shadow properties stamped in the same `SaveChanges` override, excluded from owned types by the same rule, and reaching the owner through the same promotion — because a column nothing in the domain can write is a column nobody can forget to write. The token itself is unchanged, and its limit is now registered: a random GUID detects a change and cannot order two of them, which is what `14-Synchronization` §7 needs (**TD-144**). Originally: — a `Version` shadow property on all nine non-owned entities, assigned a fresh GUID by `PineDbContext` on every insert and update, and translated at the persistence boundary into `ConcurrencyConflictException` so EF does not leak upwards. A change to an owned collection promotes its owner, because the aggregate is the unit of concurrency. **The cause this row used to give was false** and was corrected in iteration 15: the background trainer only reads, and its one write is a `Setting` watermark. The real contention is two `ScopedMediator` sends read-modify-writing the same row. No automatic retry — see §12.34 |
| TD-18 | No `Money` scale or rounding policy | 🟠 | Sub-cent drift; undefined split remainders | Normalise + `Allocate` | P1 | ✅ **Closed** — iteration 8, §12.18. `Money` normalises in its constructor to the currency's ISO 4217 minor units (`ToEven`), rejects a fractional amount in a zero-minor-unit currency, and gains `Allocate`/`Multiply`/`Negate`/`Round`/`Scale`. `CurrencyCode` validates membership and offers `TryCreate`. Six falsification mutations, all killed |
| TD-19 | Line items unreconciled against total | 🟠 | Internally contradictory records | `LineItemKind` represents the difference instead of tolerating it; the invariant is enforced at the persistence boundary and the receipt confirmation UI offers a one-click adjustment | P1 | ✅ **Closed** — iteration 9, §12.20 |
| TD-20 | No receipt duplicate detection | 🟠 | Double-counted expenses | Content-hash check | P1 | ✅ **Closed** — `s1:` receipt key on the existing `(AccountId, ImportKey)` index; non-blocking warning re-checked immediately before the write. See §12.38 |
| TD-21 | Ambiguous dd/MM vs MM/dd parsing — and the OCR "multi-culture cascade" tries `InvariantCulture` (MM/dd) **first**, so its `es-EC` leg is unreachable for any ambiguous date while its comment claims dd/MM is tried first | 🟠 | Silently misdated transactions, now behind code that reads as if it handled them | Locale-driven order; flag ambiguity | P1 | ✅ **Closed** — `ReceiptDateParser` matches explicit formats with `TryParseExact`, ISO first, then the culture's own day/month order; an ambiguous date is flagged and both readings are shown. The real cause was `TryParse`, not the ordering. See §12.38 |
| TD-22 | Confidence score is an arbitrary tally | 🟠 | Unearned user trust in OCR | Propagate real confidence or relabel | P1 | ✅ **Closed** — the tally is deleted; per-field bands are built from the OCR engine's own word confidences, with `NotFound` distinct from `Low`. No aggregate score. See §12.38 |
| TD-23 | Migration failure reported as wrong password | 🟠 | Undiagnosable support cases | Split auth and schema phases | P1 | ✅ **Done** |
| TD-24 | Raw `ex.Message` surfaced in UI | 🟡 | Path/internals leakage | Map to user-facing messages | P2 | ✅ **Done** — iteration 19, §12.40 |
| TD-25 | Unredacted crash.log / OCR text to console | 🟡 | Sensitive data outside vault | Redact and relocate | P1 | ✅ **Done** — iteration 11, §12.24. The OCR content trace is deleted outright (a receipt cannot be usefully redacted); `crash.log` and `ocr_init_error.log` are replaced by redacted JSONL records. The log files are still **plaintext on disk** — see TD-82 |
| TD-26 | Reflection sets `LinkedTransactionId` | 🟠 | Silent runtime break on rename | Domain method or single aggregate | P1 | ✅ **Done** — iteration 25, §12.52. `Transaction.CreateTransferPair` builds both legs already linked in both directions; `Transaction.Create` no longer accepts a link, so the one-way shape has no production expression. Held by `TheOnlyTypeInTheApplicationLayer_ThatUsesReflection_IsTheValidatorRegistration` and `Transaction_ShouldNot_WidenItsPublicSurfaceSilently`, plus four mutations against the real void path |
| TD-27 | No configuration system | 🟠 | Nothing tunable; no env separation | `IOptions` + appsettings | P1 | ✅ **Done** — iteration 23, §12.48. `PineOptions` bound from an optional file plus `PINE_*` env vars, validated at startup with every problem in one exception, delivered as `IOptions<T>`. The file lives in Pine's data directory, **not** inside the packaged artefact, because `package.sh` checksums every file it ships |
| TD-28 | Service locator in view models | 🟠 | Hidden deps; UI touches EF | Typed VM factories | P1 | ✅ **Done** — iteration 24, §12.50, in the shape this row prescribed. `IViewModelFactory` has one named method per page; `INavigationService` takes the page, not a type argument. `IServiceProvider` survives in exactly one view-model-facing type, `PineViewModelFactory`, which is composition and says so. Held by `NoViewModelOrView_ShouldTakeTheContainer`, `ViewModelsAndViews_ShouldNot_DependOnInfrastructureOrEfCore` and `TheOnlyTypeInTheUi_ThatTouchesInfrastructure_IsTheCompositionRoot` |
| TD-29 | Five duplicated composition roots | 🟠 | Divergent lifetimes per platform | `AddPine*()` extensions | P1 | ✅ **Done** — iteration 22, §12.46. `AddPineInfrastructure()` + `PineCompositionRoot`; the five heads register nothing of their own, and a test reads their source to keep it that way |
| TD-30 | No index on `Date`; no FK transaction→account | 🟠 | Scans at scale; orphan risk | The FK, and an index on a column that can actually be ordered | P2 | 🟢 **Fixed** (§12.32) — FK `Restrict`, no navigation property, matching Budget→Category. **The "index on `Date`" half could not be done as written**: ordering that column is wrong (TD-94), so the index is on the new `DateUtc`. Indexing `Date` would have made a wrong order fast |
| TD-31 | Decimal stored as TEXT in SQLite | 🟠 | Wrong results when comparison reaches SQL | Scaled-integer converter | P1 | 🟢 **Fixed** (§12.32) — every money column is an `INTEGER` count of ten-thousandths. The scale is **fixed at 4, not per-currency**: a value converter sees one column and cannot read the sibling currency column, so a per-currency scale was unavailable, not merely awkward. 4 is free — it is the ISO 4217 maximum (`CLF`, `UYW`) and already `Money.PowerOfTen`'s ceiling |
| TD-32 | `TransactionSplit.Category` is a free string, not a FK (`Budget` already uses a real `CategoryId` FK) | 🟡 | Rename orphans history | Store `CategoryId` on the split | P2 | ✅ **Done** — iteration 29 (§12.60), **and the severity was understated, not overstated.** The cost was not renames: the ledger spoke a string and the categoriser spoke a `Guid`, and the adapter between them was a `Guid.TryParse` on a human-typed name. It could not succeed, so **the model was never trained and no prediction was ever applied**. The fix unifies on the name; the prescribed direction (`CategoryId` on the split) was rejected in writing because the `Categories` table has no writer (TD-132). The row's second clause was taken as written: `ParentCategoryId` now has a real `Restrict` FK, a migration, and six tests proving it is enforced. **AMENDED, iteration 34 (§12.70): the decision this row took is now load-bearing in a second subsystem.** The rule engine's action is a category NAME, for the same reason the split's is — so §10 Phase 3's "rule engine, on top of the now-real `CategoryId` relationship" was built on a relationship that does not exist, and says so in the code. TD-132's "not both, and not neither" is closer to being decided by default with every subsystem that adopts the string |
| TD-33 | Domain events raised, never dispatched | 🟡 | Misleading scaffolding | Dispatch or remove | P2 | ✅ **Done** — iteration 29 (§12.60), by **remove**. Three event types, `IDomainEvent` and the list on `AggregateRoot` are gone. No dispatcher ever existed (`git log -S "IDomainEventDispatcher"` returns no commit on any branch), and the two mutations that raised events already update the balance synchronously in the same handler and the same transaction, with a written argument for why they must. `10-Domain-Model` §5's specified events are **not** what was deleted — the spec names `TransactionRecorded` carrying a category, the code had `TransactionRegisteredEvent` without one, and every subscriber the spec lists is a context with no source files. `AggregateRoot` survives as a marker with tests. **Note the naming collision**: §5's "Budget's `Money` uses a value converter" clause is a *different* concern filed under the same id, is deliberate, and was left alone |
| TD-34 | Messenger subscriptions never unregistered | 🟡 | Stale VMs act on messages | `IDisposable` view models | P2 | ✅ **Done** — iteration 26, §12.54. Not by the remedy this row names: `IDisposable` alone would have left the three `this`-capturing handlers immortal regardless. `ObservableRecipient` + `IRecipient<T>` removes the capture by construction, and `PageActivation` — driven by the navigator's `CurrentPage` hook — deactivates and then disposes. Disposal is still there, and it is what makes the camera release deterministic |
| TD-35 | Fire-and-forget async in ctor / messenger | 🟡 | Unobserved exceptions; init races | Awaited `InitializeAsync` | P1 | ✅ **Done** — iteration 26, §12.54. Both halves, and they needed different fixes: `ObservedTask` for the unobserved exceptions (25 sites), `IAsyncInitializable` for the init races (nine constructors that returned while their object was still filling itself in). Held by `ObservedTaskTests`, `AsyncInitialisationTests` and `Navigating_to_a_page_starts_its_initialisation` — the last written only because a mutation proved the other two did not connect |
| TD-36 | Dead `InferenceSession`; missing model file | 🟡 | Startup cost; misleads readers | Delete | P2 | ✅ **Done** — iteration 27, §12.56. The session, the `ModelPath` constant, the model-load log record, `LoadModel()` and `IDisposable` are all gone; the extraction was always heuristic and is unchanged. **The `Microsoft.ML.OnnxRuntime` package reference stays**, because `ReceiptDetector` really does run inference — this row implied otherwise. Held by `TheOnlyTypeUsingTheOnnxRuntime_IsTheOneThatRunsInference`, an allow-list of one |
| TD-37 | Dead entities: Attachment, Rule, Tag | 🟡 | Schema noise; implies features | Implement or drop | P2 | ✅ **Done** — dropped. `Attachment`, `Rule` and `Tag` and their three tables are gone (migration `DropAttachmentRuleAndTagTables`). Pure subtraction: there were no foreign keys in either direction, so `Attachment.TransactionId`, `Rule.TargetCategoryId` and `Rule.TargetTagId` were unconstrained `Guid` columns and nothing was orphaned. **Not saved work**: `Attachment` had no `Hash` despite `10-Domain-Model` §7 requiring one, and Phase 3's designs — an encrypted blob through `IVaultFileCipher`, a rule engine over a real `CategoryId` relationship — are different shapes. `10-Domain-Model` §6.14 and §7 now mark the design *(not built)*; `31-Rule-Engine` is kept, because nine documents cross-reference it. **AMENDED, iteration 34 (§12.70): `Rule` is built too, and — like `Attachment` — the type that came back is not the one that was dropped.** The shell had a `TargetCategoryId` and a `TargetTagId` the database knew nothing about, no conditions and no evaluator. The replacement has conditions, a pure evaluator with a total ordering, a rules screen, and a place in the import and manual-entry pipelines. `Tag` is still nothing but a name in a document. **AMENDED, iteration 33 (§12.68): `Attachment` is built, and this row's own reasoning is why it could be built cleanly.** The replacement has the hash this row identified as missing, and it is verified on read rather than merely stored; it has a real foreign key where the old one had a loose `Guid`; and it holds the file, which the old one never did. **One clause of this row turned out to be wrong**: the Phase 3 design is NOT "an encrypted blob through `IVaultFileCipher`" — that cipher's contract explicitly refuses large attachments, and a file outside `pine.db` would be excluded from every backup. `Rule` and `Tag` remain deleted |
| TD-38 | Four empty projects; three stub test projects | 🟡 | False signal in solution and CI | Remove until needed | P2 | ✅ **Done** — iteration 27, §12.56. All seven deleted; zero functionality, zero assertions and zero packaged output were lost, because there were none. §10 Phase 3 had made this conditional on "formally retiring the two plugin documents" — satisfied without inventing a new status value, since `Aspirational` already means "nothing in the tree does this", which an absent project states more precisely than an empty one. **CORRECTED, iteration 36 (§12.74): that second sentence was wrong, and the deletion this row records was only ever one third of the instruction.** `Aspirational` is defined by `DocumentStatusTests` as "nothing in the tree does this. **It may still be a good plan**" — a claim about the scaffolding plus an open question about the plan. Deleting the projects sharpened the first and left the second untouched, so nothing had been retired and a reader was still entitled to expect a plugin host. The fifth status value this row says was unnecessary turned out to be exactly what retirement required. The deletion itself stands and was correct; only the claim to have closed the Phase 3 row is withdrawn. Retired for real by **ADR-0021** |
| TD-39 | Docs contradict implementation (§6.12) | 🟠 | Misleads every new contributor | Mark aspirational; reconcile | P1 | ✅ **Done** — iterations 20 and 21 (§12.44). All 62 files in `docs/` plus `README.md`, `STATUS.md`, `PRODUCT.md` and `CHANGELOG.md` carry a status from a closed vocabulary and a `Reconciled` date, and `DocumentStatusTests` is the workflow step that keeps it true. **What it does not do is judge accuracy** — no test can — so TD-113 records the residue |
| TD-40 | Code cites ADR-0013/0012; only ADR-0001 exists | 🟡 | Dangling decision references | Write them or drop the citations | P2 | ✅ **Done** — iterations 20 and 21 (§12.44). Both ADRs were written from the code, `EveryAdrCitedInSourceHasAFile` fails the build on a dangling citation from source, and ADR-0012's citations were converted from line numbers to quotations after every one of them was found to be wrong **within the same commit that wrote them** |
| TD-41 | Four PaddleOCR model packages referenced | 🟡 | Large duplicated payload | Keep one | P2 | ✅ **Done** — iteration 28 (§12.58). Down to two, not one, and **"keep one" was not achievable**: `Models.Local` is the 36 MB facade holding `LocalFullModels`, and its own `.nuspec` requires `Models.LocalV5 >= 3.0.0`, so dropping the weights package would only float it to that floor. `LocalV3` (238 MB) and `LocalV4` (234 MB) removed; neither defines `LocalFullModels` and no source file named either. The row's "three `.onnx` files committed separately" half turned out to be the larger finding — see TD-131 |
| TD-42 | No central package management; version drift | 🟡 | Silent unification; EF mismatch | `Directory.Packages.props` | P2 | ✅ **Done** — iteration 28 (§12.58). `Directory.Packages.props` with `ManagePackageVersionsCentrally`, so `NU1008` makes a version in a `.csproj` a build error. The EF split this row named was real — `Sqlite.Core` 10.0.9 against `Design` 10.0.11 — and now resolves as one family at 10.0.11, confirmed in `project.assets.json` rather than by reading. The **`Microsoft.Extensions.DependencyInjection` 10.0.11/10.0.9 drift this row also named was already gone** before the work started. `BuildHygieneTests` carries six of the eight guards |
| TD-43 | ONNX binaries are LFS-tracked since `bec0887`; the LFS conversion **did** leave the three raw blobs behind, but only in a working copy's reflog — never in reachable history, so no clone has ever received one | 🔵 | No clone cost at all; ~190 MB of local `.git` in this working copy only | Nothing to rewrite. `git gc --prune` reclaims the local copy | P3 | ✅ **Done** — iteration 28 (§12.58), by measurement rather than by change. Reachable history is **10.3 MiB of blobs in total**, largest 372,666 bytes (this document). The three raw blobs — 113,352,104 / 90,530,732 / 13,287,636 bytes, matching the pointers' own `size` fields — sit loose in `.git/objects`, reachable only via `--reflog`, which is why `fsck` is silent about them. **Both this row and the agent sent to check it were wrong, in opposite directions** |
| TD-44 | Windows-only native runtimes | 🟡 | Scanning impossible off Windows | Per-RID runtimes; state support | P2 | 🟡 **Partial** — iteration 31, §12.64. **"State support" is done and the row's own framing was too pessimistic.** The per-RID packages are not unavailable: `OpenCvSharp4.runtime.osx.arm64` and `OpenCvSharp4.official.runtime.linux-x64` at 4.13.0.20260627, and `Sdcb.PaddleInference.runtime.osx-arm64` and `Sdcb.PaddleInference.runtime.linux-x64.mkl` at 3.3.1.70, all exist — **the exact versions this repository already pins**. The desktop half of this row is four `PackageReference` lines. It was deliberately not done because **the blocker is a runner, not a package**: nothing in this project can execute a macOS or Linux binary, so the only reachable outcome is `PineHeadCapabilities` moving from an honest `false` to an untested `true`. The mobile half is genuinely impossible — `sdcb.paddleinference.runtime.android` is a 404 and `opencvsharp4.runtime.android` returns zero hits. The WASM clause is closed by deletion. **What remains: acquire a macOS or Linux runner, then add four lines and re-derive the table** |
| TD-45 | Hardcoded English UI; no localisation | 🟡 | Costly retrofit across 26 views | ~~`.resx` + `IStringLocalizer`~~ → **JSON keyed by stable string IDs, per `41-Localization` §5**. **This column was wrong and is corrected here**: §5 is the normative specification and a *Fix* column is a reviewer's suggestion. `.resx` would also have shipped translations as satellite assemblies, the known silent casualty of trimming on the two AOT heads nothing here can build (TD-07) | P2 | ✅ **Done** — iteration 32, §12.66. **The row understated it: this was not a missing feature, it was a shipped control that did nothing.** A "Display Language" picker with English and Spanish has been on the Settings screen since the multi-vault work; it persisted `Appearance.Language` into the vault and changed not one character, because nothing in `src/` read that key and nothing ever assigned `CurrentUICulture`. Its two siblings on the same panel each applied AND persisted; it only persisted. Now: **260 keys**, `en` and `es`, live-switching with no restart through a binding to the localizer's indexer; the preference stored OUTSIDE the vault so the pre-unlock screens are translated too, with a one-way migration of any existing in-vault choice; and the `es` glossary `41-Localization` §8.4 requires agreed BEFORE the strings were written, as §18 of that document. **The guard test is the part that keeps it closed**: `41-Localization` §14 as an executable rule, which immediately found nine more literals the manual count had missed — `AutomationProperties.Name` values a screen reader reads aloud. Seven mutations, seven killed. Coverage of `Pine.Ui.Avalonia` 28% → 37%. **Not closed by this row: the sentences below `ViewModels/` (TD-139) and locale-driven number and date formatting (TD-21)** |
| TD-46 | Relative model paths in `MlNetCategorizer` and `OnnxNlpFieldExtractor`; `ReceiptDetector` already anchors to `AppContext.BaseDirectory` | 🟡 | Feature silently off; stray files | Anchor the remaining two | P2 | 🔴 **Open** — scope narrowed, see §12.12 |
| TD-47 | New vaults seeded with a fictional $1,000 account | 🔵 | In a tool whose value is an accurate picture of the user's money, seeding invented data is a correctness problem | Removed from `SetupViewModel`; a fresh vault now has zero accounts, and the dashboard's always-visible `+ New Account` button is the path in | P1 | ✅ **Closed** — iteration 10, §12.22 |
| TD-48 | `CloudSync` setting read by nothing | 🔵 | Onboarding promises an absent feature | The disabled radio, the `IsCloudSync` property and the `System.StorageMode` write are all gone; the step keeps the local option, whose privacy claim is true | P2 | ✅ **Closed** — iteration 10, §12.22 |
| TD-49 | No packaging, signing or update channel | 🟠 | No way to ship or patch | Installer + signing + updater | P1 | 🟡 **Partial** — iteration 20 (§12.42). Version identity, a reproducible RID-pinned checksummed artefact, a release manifest and a signing seam exist. **An installer, a certificate and an update channel do not** — tracked as TD-108 (certificate), TD-109 (per-platform bundles) and, for the updater, the network-egress decision §12.42 records |
| TD-50 | No file-type or size validation on imports | 🔵 | Parser DoS / hang | Magic-byte + size cap | P2 | 🔴 **Open** |
| TD-51 | Draft comments and `<!-- asdasda` in source | 🔵 | Signals unreviewed code | Clean up; enforce in review | P2 | 🟡 **Partial** |
| TD-52 | Web/Android/iOS roots omit `TimeProvider.System` + 9 other registrations Windows has | 🔴 | Heads compile, then throw on the first command dispatched | Shared `AddPine*()` roots (TD-29) | P1 | ✅ **Done** — iteration 22, §12.46. There is one list of registrations and every head gets all of it; `ValidateOnBuild` now turns an omission into a startup failure in front of a test rather than a throw on the screen that needed it |
| TD-53 | `Pine.Ui.Web` now passes `":memory:"`, but `PineDbContext` unconditionally builds a **keyed SQLCipher** connection, whose native lib the WASM build does not link | 🔴 | Web head still fails at first DB access — an in-memory *SQLCipher* database needs the same native library | Decide the Browser story or wire a real provider | P1 | 🔴 **Open** — wording corrected, see §12.12. Iteration 22 deleted the false comment when the Web head lost its private composition root (§12.46): it now builds the same `PineDbContext` every head builds, from the real vault directory. **The defect is untouched** — the head still cannot open a vault, for the separate reason recorded as TD-88. **Closed by deletion, iteration 31 (§12.64), and this row's stated cause was wrong**: `SQLitePCLRaw.lib.e_sqlcipher` does ship a `browser-wasm` native asset. Every build of that head printed `the native references won't be linked in, because neither $(WasmBuildNative), nor $(RunAOTCompilation) are 'true'` and named `e_sqlcipher.a`. It was not that WASM could not link it; it was that nothing asked. The outcome the row describes was real; the reason was not — ✅ **Done (by decision)** |
| TD-54 | CI enumerated projects instead of building `Pine.slnx` | 🟠 | A newly added project was silently outside CI — the TD-07 drift class again | **Done** (2026-09-10, plan item 0.2), by the second of the two options this column offered. The first is not available: `dotnet build Pine.slnx` aborts at restore with two NETSDK1147 errors on any runner without the android and ios workloads, which this audit has measured three times. So `scripts/ci-projects.sh` derives each leg from `Pine.slnx` — platform suffix, then test SDK, then desktop — and refuses to classify a TFM it has no leg for rather than dropping it. The workflow names no project at all. `BuildHygieneTests.EveryProjectInTheSolutionIsBuiltBySomeCiLeg` runs that script and asserts the legs cover the solution exactly, with nothing in two legs and nothing in none | P1 | ✅ **Closed** — iteration 38 |
| TD-55 | `TreatWarningsAsErrors` promotes NuGet audit warnings (NU19xx) to build errors | 🟡 | A new CVE advisory turns CI red with no code change | Set `NuGetAuditMode`; triage advisories separately | P2 | 🔴 **Open** |
| TD-56 | No restore-from-backup path; `ExportVaultBackupCommand` had no UI entry point | 🟠 | Recovery required the user to copy files by hand | Restore command + UI affordance. **HALF DONE — iteration 37 (§12.76).** The export half is closed: the Settings button was an empty method bound to a visible control while `ExportVaultBackupCommand` and `VaultBackupService` sat complete and unused; it now runs the backup and names the file it wrote. `LockVault()` was the same shape and also bound to a visible button, so it was wired rather than deleted. **A false security sentence was found beside it**: the panel described the result as "a **decrypted** backup", when `VACUUM INTO` on the keyed connection encrypts the copy with the very same key — proven by `CreateBackupAsync_ShouldProduceBackupEncryptedWithSameKey`. Corrected. **The restore half is now closed too — 2026-09-10, plan item 1.1.** `IVaultRestoreService` replaces `pine.db` with one of the vault's own backups, in a fixed order that is the whole design: refuse a path outside the backups directory; refuse a file that will not open with this vault's key or is not a Pine vault; refuse a backup carrying a migration this build has never heard of, because EF applies only *pending* migrations and is silent about unknown ones; take a `PreRestore` copy of what is about to be replaced; and only then replace it. Every refusal leaves the vault untouched, and the `PreRestore` copy is what makes the one destructive step reversible — proven end to end by `RestoreAsync_ShouldRestoreARestore_SoTheOperationIsReversible`. The handler locks the vault afterwards, because every context and every screen was built against the file that has just been replaced. Settings offers a list rather than a file picker, for the mirror image of the reason the export half has no picker: the source is not the user's to choose either | P1 | ✅ **Closed** — iteration 39 |
| TD-57 | `IVaultMigrator` lives in `Pine.Core` because Infrastructure cannot see Application | 🟡 | A persistence-orchestration port sits in the domain project | Add the ProjectReference, then move the port | P2 | 🔴 **Open** |
| TD-58 | `AddTransactionViewModel` / `CreateAccountViewModel` take an unregistered `Action onComplete` ctor parameter | 🟡 | Blocks `ValidateOnBuild`; the container cannot self-check at startup | Typed VM factories (TD-28) or a registered callback | P2 | ✅ **Done** — iteration 24, §12.50. The premise had already been half-dissolved by iteration 22, which turned `ValidateOnBuild` on by deleting the dead registration; what remained was that these three view models (`ImportWizardViewModel` is the third) could only be built by hand, which is why their `ILogger` was `null` on every real run. `IViewModelFactory` builds all three through `ActivatorUtilities`, so the callback is supplied by the caller and everything else comes from the container — **without** a registration, so the flag is not re-endangered |
| TD-59 | `ReconcileOwnedChildren` infers owned-child state from EF's Modified/Added guess | 🟡 | A client-keyed owned child is promoted Modified→Added; if either owned type ever gains a public setter, a real update is silently rewritten as an insert | Set owned-child state explicitly, or key them server-side | P2 | 🟡 **Partial** — assumption pinned by `OwnedTransactionChildren_ShouldNot_ExposePublicSetters` |
| TD-61 | `CurrencyCodeText.TryNormalise` restates `CurrencyCode`'s shape rule outside the domain | 🟡 | Two copies of one rule can drift — most concretely when `CurrencyCode` gains ISO 4217 *membership* validation (TD-18), after which the validator would still wave `"ZZZ"` through | Expose a non-throwing `CurrencyCode.TryCreate` and delete the copy | P2 | ✅ **Closed** — iteration 8, §12.18. The helper now delegates to `CurrencyCode.TryCreate`; the copy is deleted. Pinned by a theory asserting `TryCreate` and the constructor agree on every input, and by falsification M6 |
| TD-62 | Validators cover currency only; other pre-conditions still fail inside the domain | 🟡 | `AddTransactionCommand` with `Amount == 0` still reaches `account.Deposit(...)` and surfaces "Deposit amount must be strictly positive."; `ImportTransactionsCommandHandler` still throws a bare `ArgumentException` for a missing account. Same wrong-layer class TD-12 fixed, different fields | Extend the existing validators; the seam is already in place | P2 | 🔴 **Open** |
| TD-63 | `CreateBudgetCommand` carries a currency anchored to nothing | 🟡 | A budget's `CurrencyCode` is validated for shape only — it has no account to be checked against, so a EUR budget can sit against a USD-only vault and no total is comparable | Decide whether a budget is per-account or per-vault, then anchor it | P2 | 🔴 **Open** |
| TD-60 | `ReplaceSplits` / `ReplaceLineItems` mint fresh `Guid` ids on every detached save | 🔵 | Owned children are deleted and re-inserted with new ids on each update; any future reference to a split id (rules, attachments, audit trail) would dangle | Diff by identity instead of replacing wholesale | P2 | ✅ **Done** — iteration 35 (§12.72), **and the row understated where the loss happened.** The aggregate re-minted ids, but the repository never passed one in the first place: it rebuilt `SplitDefinition` from three fields and dropped the key at the boundary. **Loading a transaction and saving it back unchanged moved every child's primary key** — the defect needed no edit at all to fire. The prescribed fix was taken as written: a diff by identity, with a definition carrying a known id updating that child in place. Doing so required the first mutator either owned child has ever had, which **invalidated the stated reasoning of `ReconcileOwnedChildren`** ("neither type exposes a mutator, so every Modified child is a new one EF mis-classified"); it now asks the database which ids exist, once per collection, on the update path only. The severity was right to be 🔵: nothing outside the aggregate referenced a split id, so this was latent rather than shipped-broken. It stopped being latent the moment Phase 3 gated sync behind stable identity |
| TD-64 | The import currency guard inspects only the rows it is about to save | 🟡 | A skipped duplicate in the wrong currency is never reported, so a bad file looks clean on re-import while its first import was rejected | Validate parsed rows before the dedup split, or report currency per review row | P2 | 🔴 **Open** |
| TD-65 | Import de-duplication has a check-then-insert race; the `(AccountId, ImportKey)` index is deliberately non-unique | 🟡 | Two concurrent imports of one file can both pass the existence probe before either inserts, and both write | Unique index + conflict handling, or serialise imports per account | P2 | 🔴 **Open** |
| TD-66 | The derived import key's `source` field is `ImportProfile.Name`, the only source identifier reachable inside `ImportAsync` | 🟡 | Renaming or duplicating a profile changes every derived key, so the next import of an already-imported file reports every row as new | Key the source to something stable (profile id, or the file's own identity) | P2 | 🔴 **Open** |
| TD-67 | `ExcelImporter` and `PdfImporter` parse nothing — both open the file, loop, and return an empty list | 🟠 | ~~Two of three advertised import formats are stubs; neither is reachable from the wizard, which hardcodes `CsvImporter`~~ **THE SEVERITY WAS SET ON A MITIGATION THAT A LATER ITERATION REMOVED, AND NOBODY RE-READ THE ROW.** 🟡 was justified by the second clause: the stubs could not be reached. **TD-28 (iteration 24) made them reachable** — it replaced the wizard's hard-coded `CsvImporter` with `ExtensionImporterFactory.ForFile`, which routes `.xlsx`/`.xls` and `.pdf` straight to them — and the file picker offered "Spreadsheets" and "PDF Statements" as named categories. From that iteration until iteration 37 a user could select their bank's Excel or PDF statement, be told the import SUCCEEDED, and receive zero transactions, indistinguishable from a statement that genuinely held none. **Fixing one row silently promoted another.** Raised 🟡 → 🟠: this is the defect class the whole audit exists to remove — a shipped affordance that does nothing — and it is the only instance of it that touches whether a user believes their financial records were saved | Implement or delete; they are documented as stubs as of iteration 5 | P1 | ✅ **Done** — iteration 37 (§12.76), taking BOTH branches: `ExcelImporter` implemented against the same `ImportProfile` column mapping CSV uses, `PdfImporter` **deleted** along with the `PdfPig` package reference, because a PDF statement has no columns, no delimiter and no two banks agreeing on layout — parsing one is a project, not a missing method body. A `.pdf` now reaches `UnsupportedImportFormatException`, which says so in a sentence |
| TD-68 | No update/edit path for a transaction; correction is void-then-re-enter | 🟡 | Two rows where the user made one mistake, and the re-entered row carries no link back to the one it replaces | Update command — but close TD-59 and TD-60 first, which it makes load-bearing | P2 | 🔴 **Open** — deliberate scope decision, iteration 6 |
| TD-69 | A void cannot be undone | 🟡 | A mis-voided transaction can only be re-entered as a new one, losing its id and its import key | Unvoid command, or fold into TD-68 | P2 | 🔴 **Open** |
| TD-70 | Accounts created before `20260828005910` have no recorded opening balance, so their FIRST recalculation anchors instead of checking | 🟡 | Pre-existing drift on those accounts is undetectable and unrecoverable — reported honestly (`OpeningBalanceWasInferred`) but not fixable | An explicit "set opening balance" gesture, so the user supplies the figure instead of the system inferring it | P2 | 🔴 **Open** |
| TD-71 | The transactions list cannot filter or hide voided rows | 🔵 | Correct at today's volumes; a vault with many corrections reads badly | A filter on the list, once there is a read model to filter (TD-28's DTOs) | P3 | 🔴 **Open** |
| TD-72 | `ShellViewModel` lifetime diverges: `Transient` on Windows, `Singleton` on the other four heads | 🟡 | One view model means two different things depending on platform; a singleton shell outlives the vault it was opened for | Pick one lifetime, in the shared registration TD-29 already calls for | P2 | ✅ **Done** — iteration 22, §12.46. **Transient**, on the reasoning this row gave: the type is a view of one open vault. Pinned by a descriptor assertion across all five profiles |
| TD-73 | `PasswordStrengthPolicy` has no natural-language word list, so multi-word passphrases are over-estimated | 🟡 | The estimator accepts a passphrase weaker than the bits it reports; the 60-bit floor's `2^6` margin is what covers this | Embed a word list in `Pine.Core` — needs the BCL-only rule relaxed to permit an embedded resource | P2 | 🔴 **Open** — iteration 7 |
| TD-74 | ~~One integration test is red at `19a1877`~~ **The whole integration project stopped compiling at `f691ba8` and nobody noticed for two iterations** | 🟠 | The reported symptom was wrong. `Pine.Tests.Integration` was not 98/99; it was **0 tests executed**, 8 × CS0535. A suite that does not build reports no colour at all | Fixed in iteration 9 — the two hand-written `IVaultSession` fakes now track the interface; see §12.20 | P1 | ✅ **Closed — reclassified**, iteration 9 |
| TD-75 | Every money figure in the UI was formatted `{0:N2}` | 🟡 | Two decimals is provably wrong for a JPY account (0 minor units) and a KWD one (3). The domain has known the right number since TD-18; the view hardcoded the wrong one | `Money.ToAmountString()` plus a shared converter | P2 | 🟢 **Fixed** (§12.36) — the rule lives once, in `Money`, and the four bindings now go through it. **This row's own reasoning was wrong**: it said "there are no Avalonia converters in the project, so there is nowhere to put the rule today". `Utils/Converters.cs:9,16` already held an `IValueConverter`. The obstacle it described did not exist |
| TD-76 | The account-creation currency picker is a seven-item hardcoded list (`CreateAccountViewModel.Currencies`) | 🔵 | `"BTC"` was in it and could not have succeeded once TD-18 landed; it was removed. The remaining list is arbitrary and excludes ~170 real currencies a user may hold | A searchable picker over `IsoCurrencies.Codes` | P3 | 🔴 **Open** — opened by iteration 8 |
| TD-77 | `Account.OpeningBalanceAmount` is a bare `decimal?` column with no `Money` around it (`PineDbContext:77`) | 🟡 | TD-18's scale policy lives in `Money`'s constructor, so a column that does not pass through `Money` on the way out is outside it. Writes are covered transitively (`AdoptOpeningBalance` takes a `Money`); a raw SQL update or a legacy row is not, and `Account.OpeningBalance` now rounds on projection rather than throwing | Store it as a `Money` complex property, or keep the projection and document it (done) | P2 | 🔴 **Open** — opened by iteration 8 |
| TD-78 | `CreateAccountCommand` and `CreateBudgetCommand` still have no validator, so an unknown currency now throws `ArgumentException` from inside the handler | 🟡 | TD-18 made `CurrencyCode` reject `"ZZZ"`, which turned two previously-silent paths into wrong-layer throws rendered verbatim by `catch (Exception)` view models. Same class as TD-12, and a direct widening of TD-24's blast radius. The validator seam already exists; nothing was added because the rows are TD-62/TD-63 | An `IRequestValidator<T>` for each, using `CurrencyCodeText.TryNormalise` | P2 | 🔴 **Open** — opened by iteration 8 |
| TD-79 | `PineDbContext` declared no `HasPrecision()` and no `HasColumnType()`; every `decimal` was stored as SQLite `TEXT` | 🟡 | The store had no scale of its own, so TD-18's policy was enforced only by the domain | Closed by TD-31's converter | P2 | 🟡 **Mostly fixed** (§12.32) — the store now has a scale of its own: an `INTEGER` of ten-thousandths cannot hold an out-of-scale amount, so the read path's tolerance became automatic rather than deliberate. **`TransactionLineItems.Quantity` is still `TEXT`** and is deliberately untouched — it is not money and the domain gives it no scale policy to enforce. Carried as TD-95 |
| TD-80 | Onboarding asked consent for telemetry that does not exist | 🟠 | Worse than TD-48: the cloud radio was disabled, this checkbox was **enabled** and asked for real consent. No telemetry code exists anywhere in the repository and nothing read `System.Telemetry`. In a product whose argument is local privacy, this is the worst place to be inaccurate | Checkbox, `TelemetryOptIn` and the setting write removed | P1 | ✅ **Opened and closed** — iteration 10, §12.22 |
| TD-81 | `SetupViewModel`'s happy path cannot be tested | 🟠 | `VaultManager` is `sealed`, has no interface, hard-codes `LocalApplicationData/Pine` in its constructor and writes `vaults.json` to the real disk, so `InitializeVaultAsync` cannot run under test without mutating the developer's machine. Every existing test of this view model stops at an early validation failure; iteration 10's tests had to assert type-level dependencies instead of behaviour | Extract `IVaultManager`, or inject the data directory | P2 | 🟡 **Half done** — iteration 23, §12.48. The **second** half of the stated fix is in: `VaultManager` takes `PineStorageOptions`, so a test can point it at a temporary directory instead of the developer's real `%LOCALAPPDATA%`, and one does. The interface is still missing, so `SetupViewModel` still cannot be substituted at that seam and its happy path is still uncovered |
| TD-82 | Log files are not encrypted at rest | 🟡 | `30-Observability` §4.4 requires logs to be encrypted with the data-store key. They are not: iteration 11 writes plaintext JSONL to `%LOCALAPPDATA%/Pine/logs`. The reason is structural — the records that matter most (startup, KDF failure, crash, every pre-unlock error) exist precisely when no vault key is available — so the honest posture is redaction plus a location outside the vault, not a half-encrypted log. Residual exposure is whatever `LogRedactor` misses | Either a post-unlock encrypted sink alongside the plaintext pre-unlock one, or amend §4.4 to match reality | P2 | 🔴 **Open** — iteration 11 |
| TD-83 | Nothing proves a head still registers logging | 🟠 | `AddPineLogging()` is verified by a unit test that builds one `ServiceCollection` by hand. No test builds any head's real container, because `tests/Pine.Tests.Unit` references `Pine.Ui.Avalonia` but none of the five heads. Deleting the call from `Pine.Ui.Web/Program.cs` would leave 367 tests green — the same class of hole as TD-14 itself, one layer up. The identical gap applies to `AddPineApplication` and `AddPineVaultAutoLock` | A composition-root smoke test per head, or a single shared `AddPineHost()` the heads cannot bypass (folds into TD-29) | P2 | ✅ **Done** — iteration 22, §12.46. Both halves of the suggested fix, because neither alone is enough: `PineCompositionRootTests` builds the real container for all five profiles, and one test **reads the five head source files** and fails if any of them names a registration call or omits the shared root. The second half is a text scan and says so; it is the only instrument available, because three of the heads target `net10.0-android`, `-ios` and `-browser` and cannot be referenced from a `net10.0` test project |
| TD-84 | No Diagnostic Information Bundle exists | 🟡 | `30-Observability` §9 specifies a **signed** ZIP carrying sync run summaries, an SLO budget summary and a plugin inventory. None of those subsystems exist, and there is no signing key, no distribution and no verification path. Iteration 12 removed the `Export DIB` button — whose body was one log line and a `// To be implemented` — rather than shipping something else under that name, and annotated §8 and §9 `Status: Aspirational` | Build the DIB once metrics, SLOs and a plugin host exist, or narrow §9 to what a local-first app can honestly produce | P2 | 🔴 **Open** — iteration 12 |
| TD-85 | Four of five heads never register their navigation targets | 🔴 | `ShellViewModel.NavigateTo` resolves through `GetRequiredService`, so an unregistered view model is not a blank page but a **throw on click**. Only `Pine.Ui.Windows` registers all seven nav targets. macOS is missing `Budgets`, `Settings`, `ScanReceipt` and `LiveScanner`; Web is missing `Settings`, `ScanReceipt` and `LiveScanner`; Android and iOS are missing all four. `Diagnostics` was a fifth instance and was fixed under TD-15 in iteration 12 — the rest were left alone deliberately, because registering `ScanReceipt`/`LiveScanner` on the Browser head would wire up a page that cannot run OpenCV or PaddleInference at all (§10, Phase 3). It compiles on every head, which is why nothing caught it | Decide per head which pages exist, hide the nav entries the head cannot serve, and register the rest — ideally once, via TD-29's shared root | P1 | ✅ **Done** — iteration 22, §12.46, in exactly the shape this row prescribed. `PineHeadCapabilities` is the per-head page inventory, in one table; the sidebar's two receipt buttons bind to the same object that decides whether their view models are registered, so a visible button always has a view model behind it and an absent one has no button. The other five pages are now registered on **all five** heads |

---

## 9. Production blockers

Each of the following should prevent a release to users who will store real
financial data. Strict but not manufactured — every one has an identified file and
a concrete failure mode.

1. ✅ **TD-01** — ~~No transactional boundary.~~ **Closed.** Scope-per-request DI, `IUnitOfWork`, and an opt-in `TransactionBehavior`; repositories no longer self-commit. Rollback proven by tests that flush real rows inside the transaction and assert the database comes back empty.
2. 🟡 **TD-02** — ~~No correction mechanism.~~ **Blocker closed; the row stays 🟡 for a narrower reason.** A transaction can be **voided** with a recorded reason, its effect on the balance undone in the same database transaction, and a transfer voided as a unit across both legs and both accounts. Any account's balance can be **recomputed from its own ledger** and repaired, exactly — the sum is done in .NET `decimal` because SQLite would coerce a pushed-down `SUM` to REAL (TD-31). Proven by four falsification mutations (§12.14). What is *not* built is the **edit** path: correcting a transaction means voiding it and re-entering it, which is the right default for a ledger and is tracked as TD-68. The blocker said "any bad record is permanent" — that is no longer true, and it is the sentence that made this a blocker.
3. ✅ **TD-03** — ~~Silent import data loss.~~ **Closed.** De-duplication keys on a stable per-row import key (`ImportKeyFactory`, `r1:`/`h1:` schemes) looked up through an indexed `(AccountId, ImportKey)` probe, never again on `(date, amount)`. Two same-day same-amount rows both survive. The command now returns every parsed row with its disposition and a reason, and the wizard shows them on a review step — so a row is never discarded without the user being told which and why. Proven from a fresh context and raw SQLCipher `SELECT`s, and by three falsification mutations (see §12.11).
4. ✅ **TD-04** — ~~`AddSplit` throws.~~ **Closed.** `AddSplit` no longer validates the sum eagerly, `ReplaceSplits`/`ReplaceLineItems` validate a whole set atomically, and the invariant is enforced at the persistence boundary by `Transaction.EnsureValid()` in `SqliteTransactionRepository.SaveAsync`. A two-way €50/€50 split on a €100 transaction now persists; the boundary check is proven by falsification (removing it turns the test red).
5. ✅ **TD-05 ✅ / TD-06 ✅** — ~~Unversioned KDF and no envelope key.~~ The versioned header records the KDF id and every parameter, and header version 2 now carries the data key wrapped under the Argon2id-derived auth key (§12.27). Password change is an ordinary feature. Biometric unlock is *possible* and still unbuilt (TD-87), and the data key is re-wrapped rather than rotated (TD-86).
6. 🔴 **TD-07 / TD-08** — CI builds the product and the three heads no longer reference a non-existent namespace, but the workflow has never executed, **and as of `79c5364` it can no longer execute on its own**: the `push` and `pull_request` triggers were deleted, leaving `workflow_dispatch:` alone. Android/iOS were never compiled (workloads absent locally). Regression protection is written, disarmed, and still not demonstrated. Iterations 3 and 4 both restated "never executed" without noticing the triggers had gone — see §12.12.
7. 🟡 **TD-09** — ~~No financial-invariant tests. Every fix above would ship unverified.~~ **Untrue since iteration 2, and the gap has narrowed twice more since.** Atomicity, split-sum, owned-collection round trip, currency match, import dedup and now **void + balance recalculation** are all covered, several falsification-proven; the suite is 240 tests. The remaining gaps are rounding/allocation (TD-18) and line-item reconciliation (TD-19) — both P1, and both invariants the domain does not express yet, so they are work to *design* rather than tests to back-fill. See §12.12 and §12.14.
8. 🟡 **TD-10** — Encrypted pre-migration backup is implemented and verified, so a failed upgrade no longer destroys the only copy. **Recovery is still manual**: no restore path and no UI entry point for the export command.
9. 🟡 **TD-11** — ~~No password policy, no auto-lock. The encryption is defeatable by the setup screen.~~ **The setup screen no longer accepts a one-character password**: a 60-bit entropy floor, derived from the KDF Pine actually ships, gates vault creation, and an idle watchdog locks the session after ten minutes. **This blocker is not closed, because nothing in the suite guards it.** The behaviour has been executed and observed once, by hand, outside the repository; the code compiles on three of five heads and is unreachable on the other two for unrelated reasons (TD-08). It downgrades from 🔴 to 🟡 on structure alone, and returns to 🔴 if the tests in §12.17 are not written. See §12.16.
10. ✅ **TD-12 ✅ / TD-13 ✅** — ~~Owned collections dropped on update~~ and ~~currency never validated against the account~~. **Blocker closed.** TD-13: `Transaction.Splits` and `LineItems` round-trip through the tracked graph, proven from a fresh context and a raw `SELECT COUNT(*)` across grow/shrink/empty. TD-12: a validation pipeline behaviour rejects a currency mismatch before any write, proven by falsification. **The honest correction this blocker needs:** it claimed *two* corruption paths. Iteration 4 measured the second one and found it was no longer a corruption path — closing TD-01 had already contained it (a mismatch rolled back cleanly: zero rows, balance untouched, `Begin, Rollback`). What remained was a domain exception (`"Cannot add money of different currencies: USD and EUR"`) leaking verbatim to the user from the wrong layer. That is a real defect and it is now fixed, but it was not data corruption, and this blocker overstated it from iteration 2 onward. See §12.9.
11. 🟡 **TD-49** — No packaging, signing or update channel. No mechanism to deliver any of the above fixes to a user who already installed. **[Updated, iteration 20: Pine can now produce a versioned, checksummed win-x64 artefact that states its own build id (§12.42). It still cannot be *installed* and still cannot be *updated*, so the blocker stands — what changed is that a fix can now be handed to someone as a named, verifiable file rather than as a git clone.]**

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
- [x] 🔴 **3.** CI: build the whole solution; restore `/tests/` to `Pine.slnx`; fix or delete the broken heads (TD-07, TD-08) — **CODE DONE, THEN DISARMED.** `Pine.slnx` clean, heads fixed, three-job workflow written — but `79c5364` removed the `push`/`pull_request` triggers, so the workflow now runs only when someone dispatches it by hand. Never run; Android/iOS never compiled. **This item regressed after it was ticked.**
- [x] ✅ **4.** Scope-per-request DI + `IUnitOfWork`; strip `SaveChangesAsync` from repositories; one DB transaction per command (TD-01 — also fixes the transient-disposable leak) — **DONE.** All 11 repository `SaveChangesAsync` calls removed, 4 MediatR-bypassing write paths converted, `ValidateScopes` on in all five roots.
- [x] ✅ **5.** Fix `AddSplit`; fix `CurrentValues.SetValues` to update tracked graphs (TD-04, TD-13) — **DONE.** Deferred split validation with an atomic `ReplaceSplits`/`ReplaceLineItems`, the invariant enforced at the persistence boundary, and owned collections reconciled on the tracked graph. Round-trip proven from a fresh context; 124 → 138 tests.
- [x] ✅ **6.** MediatR validation behaviour; validate currency against account (TD-12) — **DONE.** Hand-rolled `IRequestValidator<T>` seam + `ValidationBehavior` registered *before* `TransactionBehavior`, so a rejected command never opens a transaction. Four validators; `TransferFundsCommandHandler`'s up-front checks folded in so there is one idiom. Proven by four falsification mutations; 138 → 152 tests.
- [x] ✅ **7.** Rewrite import dedup on a stable key with a user-facing review list (TD-03) — **DONE.** `ImportKeyFactory` (BCL-only, invariant-culture, versioned `r1:`/`h1:` prefixes), a nullable `ImportKey` column with an `(AccountId, ImportKey)` index, an indexed existence probe replacing the full-account load, in-batch duplicate detection, and an `ImportTransactionsResult` carrying every row's disposition + reason, surfaced on a new wizard review step. 152 → 170 tests.
- [x] 🟡 **8.** Update / void transaction commands + balance recalculation (TD-02) — **VOID AND RECALCULATION DONE; EDIT DELIBERATELY DEFERRED.** `VoidTransactionCommand` (both transfer legs, both balances, one database transaction, reason required) and `RecalculateAccountBalanceCommand` (exact .NET `decimal` sum, voided rows excluded, drift reported). `Account` gained a recorded opening balance — without which recomputing a balance from its ledger is arithmetically impossible; the migration deliberately does not backfill it, see §12.14. Four falsification mutations; 170 → 240 tests. The **edit** path is TD-68.
- [x] ✅ **9.** Password entropy floor and idle auto-lock (TD-11) — **DONE.** `PasswordStrengthPolicy` (BCL-only shortest-path entropy estimator; the 60-bit floor is derived from `KdfParameters.Argon2idV1`, arithmetic in the constant's doc comment) gates `SetupViewModel`, which also gained a confirm-password field. `VaultAutoLockService` polls an injected `TimeProvider`, arms on resolution, and raises `IVaultSession.Locked`; `MainViewModel` marshals it to the UI thread, drops the shell's inner page and says why. Wired on all five heads. Behaviour was verified by an out-of-suite harness (25 checks, transcribed in §12.16). **[Updated, iteration 9: that harness is now in `tests/Pine.Tests.Unit/Security/` as 44 tests, and every value §12.16 transcribed reproduced exactly. The item is ✅ for everything a test can reach; the strength meter's rendering and the Android/iOS wiring remain manual rows in `61-Manual-Test-Plan` §9. See §12.20.]**
- [x] ✅ **10.** Money-path test suite covering all of the above — written *alongside*, not after (TD-09) — **DONE.** `Money` scale/rounding/`Allocate` and ISO-4217 membership (iteration 8, §12.18); line-item reconciliation via `LineItemKind` (iteration 9, §12.20). 240 → 429 tests, eleven falsification mutations across the two iterations, all killed.
- [x] ✅ **11.** Remove the seeded fictional account; remove or wire the `CloudSync` option (TD-47, TD-48) — **DONE.** Both removed, plus the telemetry consent checkbox found alongside them (TD-80). Three falsification mutations, all killed. 310 → 313 tests. See §12.22.

### Phase 1 — Stabilisation

- [x] ✅ **1.** Structured file logging with redaction at the boundary; delete the OCR content trace (TD-14, TD-25) — **DONE.** A hand-rolled JSONL sink (`Pine.Infrastructure/Diagnostics/`, zero new NuGet packages) with `LogRedactor` as the single boundary every string crosses, size rotation and retention per `30-Observability` §4.4, one `AddPineLogging()` replacing five different ways of logging nothing, and a container-free `PineCrashLogger` for the two `Main` catch blocks. The OCR content trace is **deleted, not redacted**. Four falsification mutations, all killed. 313 → 367 tests. Log **encryption at rest** is deliberately out of scope (TD-82) and §4.4 is annotated accordingly. See §12.24.
- [x] ✅ **2.** Real diagnostics or no diagnostics (TD-15) — **DONE, THE "REAL" HALF.** The panel keeps its place and every value on it is now measured rather than asserted: a `ILogTailReader` reading the TD-14 JSONL files back (bounded 200-record tail, `FileShare.ReadWrite` so the live file the sink holds open is still readable), a singleton `IDatabaseHealthProbe` that answers `Locked` without touching the database and otherwise reports `Healthy`/`PendingMigrations`/`Unreachable`/`Faulted` as a closed status set carrying no exception text (TD-23), and an `IRuntimeInfoProvider` whose `AppVersion` is *the same resolution* the sink stamps into every record. The `Export DIB` and `Seed Demo Data` buttons are **removed, not reimplemented** (TD-84; the second was TD-47's defect class writing fiction into a real vault). Seven falsification mutations, all killed. 367 → 423 tests. See §12.25.
- [x] ✅ **3.** Envelope encryption: random data key wrapped by the Argon2 key → password change and biometric unlock become ordinary features (TD-06) — **DONE FOR PASSWORD CHANGE; BIOMETRIC IS POSSIBLE, NOT BUILT.** Header version 2 carries a `keyWrap` (AES-256-GCM, 12-byte nonce, 16-byte tag) whose associated data binds every other header field, so splicing a wrap between vaults or editing the vault id fails the tag check. The Argon2id derivation is byte-for-byte unchanged — its output is now the *auth* key — and the pre-TD-05 known-answer vector still passes. `ChangeVaultPasswordCommand` re-wraps under a fresh salt without touching the database or closing the session, gated by the same entropy floor setup enforces. Pre-envelope vaults (v1, `pine.salt`) are **refused, not adopted**: a deliberate breaking change taken while no vault exists outside a developer machine, which deletes the second key path instead of carrying it. Nine falsification mutations, all killed. 423 → 474 unit, 119 → 150 integration. See §12.27. Three rows registered: **TD-86** (the data key is re-wrapped, never rotated), **TD-87** (no platform secure storage, so no biometric), **TD-88** (WASM has no AES-GCM, so the Browser head can open nothing).
- [x] ✅ **4.** Per-vault encrypted ML model; scope the categoriser to the vault session (TD-16) — **DONE.** The model moved from a relative plaintext path shared by every vault to `{vault}/ai/categorizer.model`, sealed with AES-256-GCM under `HKDF-SHA256(data key, "pine:file-cipher:v1:ai.categorizer.model")` — a purpose-separated key, deliberately not the database key. `IVaultFileCipher` is a general seam Phase 3's encrypted attachments can reuse. The categoriser stopped loading in its constructor (which runs before any vault is open) and now compares the store's vault fingerprint at every use, so a previous vault's model cannot answer. A second defect TD-16 does not mention was closed as a consequence: **two categoriser instances existed, so training never reached predictions until the process restarted**. 11 falsification mutations, 9 killed, 2 surviving individually by genuine redundancy (see §12.29). 474 → 536 unit, 150 → 156 integration. Three rows registered: **TD-89** (training is not the opt-in `32-AI-Training` §12.2 promises), **TD-90** (the AI stack is registered in one head of five), **TD-91** (train/serve text skew).
- [x] 🟡 **5a.** Indexes and ordering; FK transaction→account; scaled-integer money converter (TD-30, TD-31, TD-79) — **DONE.** Every money column is a SQLite `INTEGER` count of ten-thousandths, at a scale fixed at 4 because a value converter cannot see the sibling currency column and 4 is the ISO maximum anyway. The migration converts existing amounts by splitting the stored text at the decimal point and doing integer arithmetic on the halves, never letting a value touch IEEE-754, and it **refuses** a vault holding an amount beyond four decimals rather than rounding stored history. The FK is `Restrict` with no navigation property. The "index on `Date`" could not be built as written — see **TD-94**, the shipped crash this ticket walked into. 540 → 564 unit, 156 → 179 integration. Eight falsification mutations, 7 killed, **1 survived and is recorded** (§12.32). Rows registered: **TD-94**, **TD-95**, **TD-96**, **TD-97**.
- [x] ✅ **5b.** Row versions on the aggregates (TD-17) — **DONE.** A `Version` shadow property on all nine non-owned entities — shadow, because optimistic concurrency is a fact about the store and not about money — assigned a fresh GUID by `PineDbContext` on every insert and update, with the store's `DbUpdateConcurrencyException` translated into `ConcurrencyConflictException` at the persistence boundary. A change confined to an owned collection promotes its owner, so the aggregate rather than the row is the unit of concurrency; that promotion is the one thing a surviving mutation proved was untested, and it is now covered by the case that would otherwise corrupt a transaction. No automatic retry, deliberately. 564 → 567 unit, 179 → 187 integration. Five mutations, 4 killed, **1 survived and led to a real correction** (§12.34). Row registered: **TD-98**.
- [x] ✅ **6.** Money scale + rounding + `Allocate`; line-item reconciliation in the confirmation UI (TD-18, TD-19) — **ALREADY DONE WHEN THIS ROW WAS REACHED, AND THAT IS THE FINDING.** This row duplicates Phase 0 item 10: iteration 8 closed TD-18 and iteration 9 closed TD-19, the UI half included. Verified against the code rather than against the register — `Money`'s constructor rounds half-to-even (`:102`), rejects a fractional amount in a zero-minor-unit currency (`:94`), and `Allocate` distributes by largest remainder in `Int128` (`:236-247`); the reconciliation panel and its one-click adjustment are live at `ScanReceiptView.axaml:152-191` with 9 view-model tests. **What had NOT happened is that the scale policy never reached the rest of the UI**, and iteration 17 did that: TD-75 closed, plus a dashboard that was adding different currencies together. 567 → 582 unit. Five falsification mutations, all killed. See §12.36. Rows registered: **TD-99**, **TD-100**.
- [x] ✅ **7.** Receipt duplicate detection; locale-driven date parsing; honest confidence labelling (TD-20, TD-21, TD-22) — **DONE.** `ReceiptDateParser` replaces a cascade whose real defect was `TryParse` (which accepts far more than a culture's short pattern, making the `es-EC` leg unreachable by construction, not merely by ordering); ambiguous dates are carried and shown, never resolved on the user's behalf. `ComputeConfidence`'s literal tally is deleted and replaced by per-field bands taken from `OcrWord.Confidence`, which the engine had always produced and the extractor had always discarded; `NotFound` is a distinct band from `Low`. An `s1:` receipt key reuses the import path's lookup and index — no new migration — and the duplicate warning is non-blocking and re-checked immediately before the write. Eleven falsification mutations, **10 killed, 1 survived by genuine redundancy** (§12.38). Two `NaN` defects were found in this iteration's *own* new code, one of which crashed the scan and lived in the seam between two tasks under two green suites. 582 → **643** unit, 187 → **190** integration. Rows registered: **TD-101**, **TD-102**, **TD-103**.
- [x] ✅ **8.** Exception taxonomy; split unlock auth and schema phases; stop surfacing `ex.Message` (TD-23, TD-24) — **DONE.** `IUserFacingFailure` (Core) makes showing an exception an **opt-in the author takes while writing the sentence**, which reverses the default that made TD-24 unfixable: the set of exceptions a request can throw is open, so a deny-list is stale the moment anyone adds a `throw`. `UserFacingError` (UI) is the single place a caught exception becomes screen text, and it writes the whole exception to the TD-14 log first — the detail **moves to the log, it is not deleted**. Eleven `ex.Message` renderings replaced; three pairs of hand-rolled catch blocks collapsed into it. Three types opted in: `RequestValidationException`, `ConcurrencyConflictException` (**this is TD-98, closed here**) and `VaultLockedException` (with a fixed sentence, *not* its `Message`). The nine vault exceptions deliberately stay OUT, because several must produce wording identical to a genuine auth failure or the unlock screen becomes a password oracle — TD-23 was already closed by iteration 1 and was re-verified in code, not in the register. Eleven falsification mutations, all killed. 643 → **673** unit; integration unchanged at 190. Rows registered: **TD-104**, **TD-105**, **TD-106**. See §12.40.
- [x] 🟡 **9.** Packaging, code signing, update channel (TD-49) — **VERSIONING AND THE ARTEFACT DONE; INSTALLER AND UPDATER DELIBERATELY NOT.** The row's premise turned out to sit under it: nothing set a version, so every build stamped `1.0.0` and the resolver *trimmed off the commit* — the only varying part — leaving a constant in the diagnostics panel and in every log record. `Directory.Build.props` now sets `0.1.0-alpha.1` per `22-Release-Plan` §5, `ApplicationVersionText` keeps twelve characters of commit, and `pine --version` makes the whole chain observable in a real process (measured: `0.1.0-alpha.1+9f42153820e4`). `packaging/package.sh` was **run**, not written and assumed: an unconstrained publish emitted 1.78 GiB across **30 runtime-identifier folders**; the RID-pinned artefact is 1.08 GiB, 318 files, with 317 of 317 checksums verified, and it refuses to build if the published binary's own version disagrees with its filename. Signing is a real seam with two outcomes and no third — `--require-signature` exits non-zero when no certificate is configured, which is the current state. CI gained a `package` job and, with it, the fix for a **latent defect nothing had noticed**: no `actions/checkout` set `lfs: true`, so a runner would have packaged 133-byte LFS pointers instead of the OCR models. **No installer and no updater**: an in-app update check would be Pine's first network call ever, which is a change to the privacy posture and not a packaging chore. Seven mutations, 7 killed — **two only after the tests were sharpened, one of them a survivor that was unfalsifiable rather than untested**. 673 → **686** unit. Rows registered: **TD-107**, **TD-108**, **TD-109**, **TD-110**. See §12.42.
- [x] ✅ **10.** Reconcile the docs: mark every unimplemented spec `Status: Aspirational`; write ADR-0012/0013 or drop the citations (TD-39, TD-40) — **DONE, AND THE REMEDY IS NOT THE ONE THIS ROW ASKED FOR.** §6.12 prescribed "an afternoon" of editing; the finding in its own next sentence was that *no step in the workflow reconciles a specification with its implementation*, and an afternoon is not a step in a workflow. All 62 files in `docs/` carry a status from a closed four-value vocabulary (**25 Aspirational, 20 Non-normative, 17 Partially implemented, and zero `Implemented` — after twenty iterations not one specification fully matches the code**) with a dated `Reconciled` line, and `DocumentStatusTests` fails the build when one goes missing or reverts. ADR-0012 and ADR-0013 were written **from the code**; ADR-0012 does not record the decision its seven citations assume, because what was built is stronger than what was cited. Iteration 21 finished the row: the docs-only pass had left `STATUS.md` — the file §6.12's own contradiction table names **twice** — unreconciled and seven iterations stale, along with `README.md`, `PRODUCT.md` and `CHANGELOG.md`; those four are now inside the same enforced rule. It also closed ADR-0012's own risk row by building `NetworkEgressTests`, so "no network I/O at all" is a property the build holds rather than a claim three documents repeat. Eleven falsification mutations, **11 killed**. 691 → **701** unit; integration unchanged at 190. Rows registered: **TD-111**, **TD-112**, **TD-113**. See §12.44.

### Phase 2 — Engineering quality

> **Where this phase stands: 9 of 9 rows closed** (iterations 22–30; items 1 and 2 by iterations 22
> and 23, items 3–9 in one session). **Phase 2 is closed.** What that does and does not mean is in
> §12.62: every row is worked, and two of the nine were closed by discovering the row's own premise
> was wrong. The coverage floor's second half was already done — iteration 24 rewrote the architecture
> assertions with `NotHaveDependencyOnAny` after proving the old `.And()` chain was conjunctive and
> therefore near-vacuous. Iteration 27's warning was carried correctly into item 7: the
> `Microsoft.ML.OnnxRuntime` reference **was kept**, because `ReceiptDetector` runs real inference
> over a model that is present. What iteration 28 found instead is that the *weights beside it* were
> the dead ones — 203 MB of PP-OCRv4 files that no commit in this repository's history has ever
> named.


- [x] ✅ **1.** One shared `AddPineApplication()` / `AddPineInfrastructure()`; delete the duplicated roots (TD-29) — **DONE, AND IT DISSOLVED THE FAMILY §12.45 SAID IT WOULD.** `AddPineInfrastructure()` (Pine.Infrastructure) and `PineCompositionRoot` (Pine.Ui.Avalonia) replace five hand-copied roots; the heads went from 127–183 lines with ~25 registrations each to 27–60 lines with **zero**. The change that made it possible is small and specific: the `PineDbContext` factory now reads `IVaultDataKeyAccessor` (Pine.Core) instead of `IVaultSession` (Pine.Application), which is the only reason that lambda had to be written in the heads at all. Five divergences closed with it — a transient DbContext on Windows against scoped on four (TD-29 proper), a transient `ShellViewModel` against singleton on four (**TD-72**), `TimeProvider` absent on three (**TD-52**), three to four nav targets missing on four heads (**TD-85**), and `IAIProvider` registered on one head of five (**TD-90**), whose real cost was that `ImportTransactionsCommandHandler` could not be *constructed* on the other four. `ValidateOnBuild` is now on in all five, which was blocked in all five by one dead registration. The root lives in `Pine.Ui.Avalonia` rather than in a head **because that is the only place a test can reach it** — which closes **TD-83**. Ten falsification mutations, **10 killed**. 701 → **762** unit; integration unchanged at 190. Rows registered: **TD-114**, **TD-115**, **TD-116**. See §12.46.
- [x] ✅ **2.** `IOptions` configuration for thresholds, paths, KDF parameters (TD-27) — **DONE, AND THE KDF HALF IS NARROWER THAN THE ROW READS.** `PineOptions` (four sections, in `Pine.Core`, BCL-only) is bound from an optional `appsettings.json` and `PINE_*` environment variables, validated once at startup, and delivered as `IOptions<T>`. Ten literals moved into it, including the `0.3` confidence gate that was **written twice** and could drift — the import path WRITES a category to the ledger where the suggestion path only shows one, so a drift would have filed transactions under a category the app considered too weak to display. **The KDF is configurable for NEW vaults only, and nothing on the unlock path may read it**: a vault's Argon2id parameters live in its own header, and a configured cost consulted at unlock would derive a different key for every existing vault with a symptom indistinguishable from a wrong password. `PineSecurityOptions.ToKdfParameters()` is the single bridge, and both a reflection test and a real-Argon2id integration test pin the separation. `PineLoggingOptions` and `VaultAutoLockOptions` were **deliberately not converted** — each carries a written reason not to be an `IOptions` binding, and both reasons still hold; configuration seeds them instead. Thirteen falsification mutations, **13 killed — one only after a survivor exposed a real gap in the tests** (§12.48). 762 → **798** unit, 190 → **193** integration. Rows registered: **TD-117**, **TD-118**, **TD-119**. See §12.48.
- [x] ✅ **3.** Typed view-model factories; remove `IServiceProvider` and `PineDbContext` from the UI; add an architecture test asserting UI ↛ Infrastructure (TD-28) — **DONE, AND THE TEST THAT WAS ALREADY THERE WAS THE FIRST THING THAT HAD TO BE FIXED.** `IViewModelFactory` names all fifteen pages in methods, so a page a head cannot serve fails where it is *created* rather than inside a generic `GetRequiredService<T>` in the navigator; `INavigationService` takes the page instead of a type argument. `IServiceProvider` is gone from all four view models and from `MainWindow`, and `App.Services` is a typed two-field record rather than the container. Two seams carry what the UI used to reach for: `IVaultDatabaseOpener` (the DI scope, the `PineDbContext` probe and the migration) and `IImporterFactory`. `Microsoft.EntityFrameworkCore.Sqlite.Core` left the UI `.csproj` — nothing there needed it. **The two existing layer assertions were near-vacuous and were measured to be so before anything was built**: `ShouldNot().HaveDependencyOn(A).And().HaveDependencyOn(B)` is *conjunctive* in NetArchTest, so `ApplicationLayer_ShouldNot_DependOnInfrastructureOrUI` passed while `Pine.Application` genuinely depended on a listed namespace — proven with a throwaway probe, then rewritten as `NotHaveDependencyOnAny` with anti-vacuity guards. **The rule is scoped to the view models and the views, not the assembly, and that is the decision §12.47 and §12.49 asked for**; moving composition into its own project was considered and rejected in writing, in the test's own remarks. Ten falsification mutations, **10 killed — but one was recorded as a not-a-kill until it compiled, and one was discarded outright as a no-op that changed no behaviour**. The one genuine survivor found that `ScopedVaultDatabaseOpener` had no test at all. 810 → **824** unit, 193 → **197** integration. Rows registered: **TD-120**, **TD-121**, **TD-122**, **TD-123**. See §12.50.
- [x] ✅ **4.** Replace the reflection call with a domain method or a single transfer aggregate (TD-26) — **DONE, AND THE FIX IS THE SECOND OPTION IN SPIRIT WITHOUT THE MIGRATION.** `Transaction.CreateTransferPair` generates both identifiers up front and constructs each leg already carrying the other’s, so the two directions are established by **one** mechanism instead of two — which is what the reflection call existed to work around, and what made each direction losable independently. `Transaction.Create` no longer accepts a `linkedTransactionId` at all, so a one-way link is not something production code can express. Two guards that were previously only in the validator now also hold in the domain (distinct accounts, positive amount). The durable half is an architecture test: the set of types in `Pine.Application` with an IL dependency on `System.Reflection` must equal an explicit allow-list, whose one entry is the validator registration that legitimately scans the assembly. `Transaction` also gained the public-surface tripwire it never had — `Account` has had one since iteration 6, so a new public mutator on the *other* aggregate root was previously free. Ten falsification mutations, **10 killed**, one of them only after being rewritten: the first form discarded its `GetProperty` result and so emitted no `System.Reflection` dependency at all, making it an invalid mutation rather than a survivor. 824 → **837** unit, integration unchanged at **197**. Rows registered: **TD-124**, **TD-125**. See §12.52.
- [x] ✅ **5.** `IDisposable` view models with deterministic messenger unregistration (TD-34); awaited initialisation (TD-35) — **DONE, AND THE MECHANISM IS NAVIGATION, NOT THE VIEW LIFECYCLE.** `docs/58-TechDebt-WeakReferenceMessenger-Lifecycle` had prescribed driving `IsActive` from `Loaded`/`Unloaded`; that document predates iteration 24, which collapsed navigation to a single `NavigateTo(ObservableObject)`. Activation is therefore driven from the `CurrentPage` hook — which also covers `shell.CurrentPage = null!` in the vault-lock path, a route the document never mentions and `NavigateTo` never sees. Eleven types moved to `ObservableRecipient`; **zero manual `Register` calls remain anywhere in `src/`**; `PageActivation` unregisters *then* disposes, which is what makes `LiveScannerViewModel`'s camera release deterministic instead of dependent on a view being unloaded. For TD-35: `ObservedTask` ends twenty-five silent discarded tasks, `IAsyncInitializable` takes I/O out of nine constructors, and **all four `async void` code-behind handlers are gone**. Two real defects were fixed on the way that the row does not name: the settings page **silently stopped saving anything for the rest of the session** if a settings read threw, and the transaction list reloaded itself twice on every void. Nine mutations, **9 killed — but one survived the entire 853-test suite first**, and that survivor is the most useful thing in the iteration. 837 → **854** unit, integration unchanged at **197**. **TD-117's cause was found and reproduced.** Rows registered: **TD-126**, **TD-127**. See §12.54.
- [x] ✅ **6.** Delete dead code: unused `InferenceSession`, empty projects, stub test projects, Attachment/Rule/Tag if not being built (TD-36, TD-37, TD-38) — **DONE, AND TWO OF THIS ROW’S OWN CLAIMS WERE WRONG.** The `Microsoft.ML.OnnxRuntime` package is **not** removable: `ReceiptDetector` genuinely calls `session.Run` over `yolo-receipt-nano.onnx`, which is present. And the extractor class is not dead — `HeuristicFieldExtractor` is live and used by `DefaultLocalAiProvider`; only its `InferenceSession` was dead, touched in exactly four places and never once used for inference. Deleting it took `LoadModel()` and `IDisposable` off `INlpFieldExtractor` with it, because both existed only to serve that session. Seven projects deleted (four empty, three holding a single empty `UnitTest1.Test1()`), with the solution, CI and six documents reconciled to match. Three entities dropped by a real EF migration — **the decisive argument was not that they were unused but that `Attachment` had no `Hash` property while `10-Domain-Model` says "Hash MUST be set", so the shipped entity already contradicted its own specification**; keeping it preserved a guess known to be wrong, not a head start. Six mutations, **4 killed, 2 invalid** — and the two invalid ones taught more than the four kills, because the analyzers turned out to be the guard: `CS0169` and `CA1001` refuse two of the three ways the deleted session could return, and the third needed a new architecture test. 854 → **855** unit, 197 → **199** integration. Rows registered: **TD-128**. See §12.56.
- [x] ✅ **7.** Central package management; one PaddleOCR package; Git LFS for the ONNX files (TD-41, TD-42, TD-43) — **DONE, AND ALL THREE OF THIS ROW'S SUB-CLAIMS NEEDED CORRECTING.** `Directory.Packages.props` moves every version out of every `.csproj`, so `NU1008` makes a second opinion about a package a build error rather than a thing to notice. The EF Core split TD-42 named was real and is closed: `Sqlite.Core` 10.0.9 against `Design` 10.0.11, both pulling the same `Relational`, now one family at 10.0.11 — verified by a live restore, not by reading. **TD-41's "keep one" is not achievable**: `Sdcb.PaddleOCR.Models.Local` is a 36 MB facade holding the `LocalFullModels` type the OCR names, and its own `.nuspec` declares `Models.LocalV5 >= 3.0.0`, so removing the weights package would only float it down to that floor. Two is the graph's minimum; `LocalV3` (238 MB) and `LocalV4` (234 MB) were the genuine duplication and are gone. **TD-43's residual claim was wrong, and so was the agent that "refuted" it** — see §12.58: there really was an LFS conversion and it really did leave the three raw blobs behind, 190 MB of them, but they are reachable only from this working copy's reflog, so no clone has ever received one. Reachable history is **10.3 MiB of blobs in total**. The largest finding is not in the row at all: `PP-OCRv4-det.onnx` (113 MB), `PP-OCRv4-rec.onnx` (90 MB) and `dict.txt` were copied into every build output by a glob and **named by no commit on any branch, ever** — superseded by the PP-OCR**v5** weights embedded in the NuGet assembly before they were ever wired. The artefact went **1.08 GiB → 658 MiB unpacked, 680 MB → 292 MiB compressed**, measured by running `package.sh`. Ten falsification mutations, **9 killed, 1 survived by design** — and one of the kills only counts because the mutation exposed a real gap in the test that was then fixed. 855 → **863** unit, integration unchanged at **199**. Rows registered: **TD-129**, **TD-130**, **TD-131**. See §12.58.
- [x] ✅ **8.** `CategoryId` as an FK on splits; dispatch or remove domain events (TD-32, TD-33) — **DONE, AND THIS ROW WAS NOT A TIDINESS ROW.** TD-32 reads as a modelling complaint about renames. Underneath it was a **shipped feature that had never once run**: the categoriser's training projection read `Guid.TryParse(split.Category, out var g) ? g : Guid.Empty` and then filtered out everything that failed to parse — but a split's category is a NAME a person typed or a CSV column supplied, so the parse never succeeded, the training set was empty on every poll, `TrainModelAsync` was never reached, and the watermark was never written. No exception, no log line, no red test. The same mismatch broke the other end: both the suggestion path and the import path resolved a predicted `Guid` through `ICategoryRepository` against a `Categories` table **nothing in this application has ever inserted a row into**, so every prediction resolved to `null` and was discarded. **The row's prescribed direction was not taken and the reason is written into the code**: storing a `CategoryId` on the split needs categories to exist, and `CreateCategoryCommand` has no caller, no query lists categories, and every category input in the UI is a free `TextBox` (TD-132). The string won because it is the model with instances. TD-32's *second* half was taken verbatim: `Category.ParentCategoryId` had no FK in either the fluent config or the original `CreateTable`, and now has one, `Restrict`, with a migration and six integration tests that prove it is **enforced** rather than declared — which matters because a SQLite foreign key does nothing unless `PRAGMA foreign_keys` is on. For TD-33 the answer was **remove**: the three events were dispatched nowhere, no dispatcher was ever built (`git log -S` finds none on any branch), and the two mutations that raised them already do their work synchronously in the same handler and the same transaction. `AggregateRoot` survives as an empty marker **with tests that give it meaning**. Eight mutations, **7 killed, 1 survived and was then fixed** — the decisive one restored the exact shipped projection and turned 6 of 7 tests red. 863 → **873** unit, 199 → **205** integration. Rows registered: **TD-132**, **TD-133**, **TD-134**. See §12.60.
- [x] ✅ **9.** Coverage floor enforced in CI — **DONE, AND THE FLOOR WAS THE EASY HALF.** The first obstacle was that **the suite goes red the moment coverage is measured**: coverlet instruments by rewriting IL and injects a `Coverlet.Core.Instrumentation.Tracker.<assembly>_<guid>` type, and NetArchTest's `GetTypes()` throws `TypeLoadException` materialising it — so iteration 25's reflection allow-list failed under `--collect:"XPlat Code Coverage"` and passed without it. A coverage gate cannot be enforced on a suite that coverage breaks, so the architecture rules are now scoped to the `Pine` namespace and instrumentation is invisible to them, weakening nothing. `scripts/coverage-gate.sh` is the gate, and it **parses the cobertura report itself** because `coverlet.collector` cannot enforce a threshold — its own `VSTestIntegration.md` says the VSTest integration lacks "threshold validation", and `coverlet.msbuild` is neither referenced nor cached. **No exclusions**: no runsettings, no `ExcludeByFile`, no `ExcludeByAttribute`. EF migrations, Avalonia code-behind and source-generated properties all count, which makes every number lower than a tuned configuration would report and is the honest direction to be wrong in. Floors are a **ratchet set just below measured**: overall 61 (61.93), `Pine.Core` 86 (87.27), `Pine.Infrastructure` 74 (74.44), `Pine.Application` 55 (55.83), `Pine.Ui.Avalonia` 28 (28.51). The script was **executed, not written and assumed** — end to end, 873 tests, report produced, gate passed. **Its honest limit is TD-07**: nothing triggers the workflow, so the CI step is a gate nobody passes through; the one-command local script is the part that works today. The row's second half was already done by iteration 24. Five mutations, **4 killed, 1 survived and exposed a real hole** — the guard test matched the gate's name inside a CI *comment*, so neutering the step left it green. 873 → **874** unit, integration unchanged at **205**. Rows registered: **TD-135**, **TD-136**. See §12.62.

### Phase 3 — Long-term

- [x] ✅ **1.** Decide the platform story honestly — **DONE, AND THE DECISION IS TO NARROW.** The Browser head is **deleted**: nine files, the `Pine.slnx` entry, the `browser-wasm` CI leg, the `Avalonia.Browser` pin, the `PineHeadProfile.Browser` member and its test rows. Three technical grounds hold (no AES-GCM, so no vault; no WebAssembly build of OpenCV or PaddleInference, so no receipt pipeline; and its SQLCipher native asset was never being linked — which **corrects TD-53's stated cause**, the `browser-wasm` asset exists and the build simply was not asking for it). **But the decisive argument was in no row**: `02-Product-Scope` has listed browser-only distribution as an explicit non-goal the whole time, so the head contradicted this project's own scope document from the day it was scaffolded. **§12.63's claim that it "has never compiled on any machine" was false** — it was built here, 0 errors, before being deleted, which is exactly why leaving it as source was the worse option. The row's *invest* branch was priced rather than dismissed: `OpenCvSharp4.runtime.osx.arm64`, `OpenCvSharp4.official.runtime.linux-x64`, `Sdcb.PaddleInference.runtime.osx-arm64` and `Sdcb.PaddleInference.runtime.linux-x64.mkl` **all exist at the exact versions already pinned** — four lines — and were still not taken, because **the blocker is a runner, not a package**: no machine here can execute a macOS or Linux binary, so the capability table would go from an honest `false` to an untested `true`. Android and iOS have no such package at any price. Android and iOS gained **real application identities** (`org.pine.app`, display name `Pine`), closing the identity half of TD-109. **The iteration's loudest finding was wrong and the falsification pass is what caught it**: a mutation refused to die, and a clean-process probe proved `Microsoft.Data.Sqlite` arms the SQLCipher bundle by reflection, so the missing `Batteries_V2.Init()` on three heads was never the vault-breaking defect it was written up as. **TD-117's abort is closed** as an enabler — without it no count in this iteration could be measured. Seven mutations, 5 killed, 1 survived deliberately, 1 needed three runs. 874 → **865** unit (the count fell because a deleted head removes a profile from four theories), integration unchanged at **205**. Rows registered: **TD-137**, **TD-138**. See **§12.64**.
- [x] ✅ **2.** Localisation infrastructure before the view count grows (TD-45) — **DONE, AND THE ROW WAS NOT ABOUT INFRASTRUCTURE.** Underneath it was a **shipped control that did nothing**: the Settings language picker persisted a number into the vault and changed no string on screen, because nothing ever read it. Implemented against the already-approved design spec, which **overrode this document's own `.resx` suggestion** in favour of `41-Localization` §5's JSON — corrected in TD-45's Fix column. **260 keys across `en` and `es`**, live switching with no restart, preference stored outside the vault so the pre-unlock screens follow it, glossary agreed before the strings rather than across them. **Three declared risks, and none of them was the one that bit**: dotted keys work; the real finding was that **no binding in this test suite had ever been evaluated** — `SetupWithoutStarting()` starts no dispatcher, so `Bind` succeeds and the property reads back `null`, silently. `Avalonia.Headless.XUnit` could not be used (it needs xunit v3 against this suite's 2.9.3), so `HeadlessUnitTestSession` was used directly. And **Avalonia does not listen for `Item[]`**, the BCL indexer convention the design assumed — only `"Item"` works, measured. **Four of five parallel agents died mid-edit on a rate limit**, leaving keys with no values; recovered from `git HEAD` mechanically after the first recovery pass produced wrong English and was thrown away — 205 exact, 6 reported, 0 guessed. The §14 guard test then found **nine more literals the count had missed**. Seven mutations, **7 killed**. 865 → **903** unit, **205** integration. Coverage ratchet turned: overall 61 → 63, UI 28 → 37. Rows registered: **TD-139**, **TD-140**. See **§12.66**.
- [x] ✅ **3.** Attachment storage — **DONE, AND THE ROW'S PHRASING WAS EXACT.** Scanning worked and then destroyed the document: the image was decoded for the screen and dropped, and `AddTransactionCommand` had no image parameter at all. **The storage decision went AGAINST the plan this document recorded.** TD-37 wrote it down as "an encrypted blob written through `IVaultFileCipher`"; that cipher's own contract says "it is **the wrong shape for a large attachment. Say so before reusing it for one**", and — decisively — `VaultBackupService` runs `VACUUM INTO` on the keyed connection and **copies `pine.db` and nothing else**, so a receipt written beside the database would have been missing from every backup and every restore. The bytes are therefore a `BLOB` in the vault database: SQLCipher already encrypts them (§6.2, FR-TXN-02), and they commit in the SAME unit of work as the ledger row. `Attachment` is an aggregate root rather than a child of `Transaction`, so a ledger query never reads a photograph; the repository splits listing from content, and the transaction list's marker comes from a projection that never names `Content`. The SHA-256 `10-Domain-Model` §7 demands is computed by the aggregate and **verified on every read**, with a typed refusal rather than a warning beside a picture. **Seven mutations, five killed and two survived — and both survivors were real holes**: nothing tested that the scanner attaches anything (reverting to throwing the image away broke no test), and nothing tested that the read path refuses a tampered file. Both are closed and both mutations now die. **The coverage gate refused the first attempt** at 36% against a floor of 37% and was obeyed rather than lowered. 903 → **937** unit, 205 → **212** integration. Rows registered: **TD-141**. See **§12.68**.
- [x] ✅ **4.** Rule engine — **DONE, AND THE PREMISE IN THIS LINE IS FALSE.** There is no "now-real `CategoryId` relationship": `TransactionSplit.Category` is a `string` with no foreign key, iteration 29 rejected changing that **in writing** (TD-32, §12.60) because the `Categories` table has no production writer (TD-132), and iteration 27 flagged the same premise when it deleted the old `Rule` (§12.56). **Fourth consecutive Phase 3 row whose stated premise did not survive contact with the code.** The engine is built on the name, for §12.60's reason: it is the model that has instances. **TD-132 decided the scope before any code was written**: its Fix column says "either build category management ... but not both, and not neither", and a `Rule` aggregate with no way to create a rule would have been that defect again — the third time, after `Attachment`, `Rule` and `Tag` were deleted for it under TD-37. So the rules screen is not an extra; it is why the engine is allowed to exist. It also makes the sidebar's disabled "Coming soon" Rules button real. **A matching rule beats the classifier outright and the model is not consulted at all** — comparing a user's priority with a softmax confidence mixes two scales that share no units and makes "why is this in Coffee?" unanswerable. A rule never overwrites a category the user typed or the file supplied. Ordering is TOTAL, not just by priority: §6.2 settles nothing when two rules share one, so ties break by creation time then id, and the repository applies the same order in SQL. Six of §5.2's fourteen operators, conditions ANDed rather than a boolean tree, one action — all declared deviations. **Seven mutations, six killed and one survived**: nothing tested the regex timeout, so a user-authored pattern could hang an import. Closed with a test that races the match against a deadline, because asserting on it directly would hang the suite rather than fail it. **The coverage gate blocked this iteration too** and was obeyed rather than lowered. 937 → **990** unit, 212 → **219** integration. Rows registered: **TD-142**, **TD-143**. See **§12.70**.
- [x] ✅ **5.** Sync — **THE GATE IS DONE; SYNC IS DELIBERATELY NOT.** This row is a precondition, not a feature: it forbids sync until three things exist, so the work was to establish all three and build the two that did not. **Row versions were already real** and genuinely enforced (TD-17, §12.34) — and `ConfigureConcurrencyTokens` had said in a comment that the Phase 3 sync work would need them on everything, which turned out to be right. **Audit columns did not exist**, unevenly: `Transaction` had a creation stamp and no update stamp, `Category`, `Setting` and `Budget` had neither, and the five that did disagreed on both the name and the type. They are now `RowCreatedAtUtc`/`RowUpdatedAtUtc` shadow columns on all eight roots, stamped by the context so no future write path can forget them — the `Row` prefix is load-bearing, because two roots already own a `CreatedAtUtc` and a colliding shadow name binds silently to the domain property instead of creating anything. **Stable identity did not exist for owned children**: TD-60, closed here — a split kept no id across its own edit, and the repository discarded the id before the aggregate could. Closing it forced the retraction of a load-bearing comment in `ReconcileOwnedChildren`, which promoted every `Modified` child because "neither type exposes a mutator"; both types have one now. **The third precondition is still not true as this line words it** — what exists is stable SURROGATE keys; there are no natural keys and the only unique constraint in the schema is `Settings.Key` (**TD-146**). **Nothing on the far side of the gate was built, and that is the row obeying itself**: `02-Product-Scope` §7.2 scopes sync to plugins only, `14-Synchronization` §8 hands the provider contract to `33-Plugin-SDK`, and the plugin host is the NEXT row in this phase and does not exist. Also missing and registered rather than glossed: a logical clock (**TD-144**) and tombstones (**TD-145**). **Nine mutations, nine killed.** The coverage gate blocked this iteration too and was answered with thirteen unit tests. 990 → **1003** unit, 219 → **234** integration. See **§12.72**.
- [x] ✅ **6.** Plugin host, or formally retire the two plugin documents and delete the empty projects — **RETIRED, AND THIS ROW HAD TWO DEFECTIVE PREMISES, ONE OF THEM THIS DOCUMENT'S OWN.** The empty projects were already gone: commit `9771a85` deleted both in iteration 27, verified in git. And **TD-38 already recorded the retirement branch as satisfied**, arguing that deleting the projects made the two documents "more precisely `Aspirational`". That argument does not hold: `Aspirational` is defined as "nothing in the tree does this — **it may still be a good plan**", which is a claim about scaffolding plus an open question about the plan. Deletion sharpened the first half and left the second untouched, which is why this checkbox stayed unticked for nine iterations while a debt row called it done. **The decision is ADR-0021, Accepted: Pine will not load third-party code**, and the deciding argument is not effort — it is that `12-Plugin-Architecture` §5's mechanism cannot satisfy `15-Security` §10's requirement. An `AssemblyLoadContext` is not a security boundary, and `VaultSession.Key` returns the live key array, so "a plugin has no access to the data key" is unsatisfiable in-process. Two invisible collisions were found on the way: a plugin could break ADR-0012 while `NetworkEgressTests` stayed green, because it scans `src/` and a plugin is not in it; and nothing in this repository can verify a signature, so the mandatory manifest signature described a check no code could perform. **A fifth document status, `Withdrawn`, was added — the one iteration 27 declined to invent — and it carries a price**: `EveryWithdrawnDocumentCitesAnAdrThatExists` fails the build unless the Status row names an ADR with a file. **Sync was re-homed rather than retired with it** (`ISyncProvider` moves to `14-Synchronization`, providers compiled in), because §12.72 had left sync gated on this row. Three mutations: **M1 survived its first run and exposed a real hole** — the rule accepted an incidental ADR-0012 mention — then was killed after the rule was tightened to read the Status row only. No source file changed; there was nothing left to remove. New row: **TD-147**. See **§12.74**. **This closes Phase 3.**

---

## 11. Final verdict

> **This section was written at iteration 0 and was never amended through thirty-six iterations of
> remediation.** It is preserved verbatim below the line, because an audit that silently rewrites its
> own conclusion is worth less than one that shows what it got right and what it outgrew. Everything
> above the line is the current verdict; everything below it is the original, and the gap between
> them is the record.

### 11.1 Current verdict (iteration 36)

| | |
|---|---|
| **Current rating** | **3.4 / 5 ★★★☆☆** |
| **Production status** | **Not production ready — but the blockers are now a bounded punch list, which is the thing that changed** |

**What the original verdict named as disqualifying no longer holds.** Its four blockers were: a
data-integrity defect in the core write path, no correction mechanism for bad data, an irreversible
security decision, and a pipeline that could not detect regressions in any of it. Three are closed
and verified. Every repository stages and commits nothing (TD-01); a `TransactionBehavior` opens one
database transaction per command; every aggregate root carries a concurrency token that is bumped on
write and surfaced as a typed failure (TD-17); money is stored as scaled integers rather than text
(TD-31); a transaction is voided rather than deleted, with the balance effect reversed in the same
unit of work (TD-02); balances can be recalculated from the ledger from a real button (TD-30). The
suite went from **28 tests to 1,238**, source from 11.7k to ~39.9k lines, and a coverage ratchet
refuses to fall.

**Why it is still not "production ready".** Three reasons, in order of weight:

1. ~~**Nothing arms the regression net (TD-07).** The CI workflow is `workflow_dispatch:` only, by an
   explicit and documented decision about compute cost. Every guard built across three phases —
   architecture rules, the coverage gate, the egress scan, the documentation status tests — is real
   and runs only when a human asks it to. This is the single largest gap and it is not technical.~~
   **Closed 2026-09-10** (plan items 0.1 and 0.2). The workflow fires on `pull_request` into `main`
   and on `push` to `main`, and it now asks `scripts/ci-projects.sh` which projects to build rather
   than carrying a hand-written list, so a project added to `Pine.slnx` tomorrow is built by CI
   without anyone remembering to add it. Item 0.3 closed the same day: every gate records a receipt
   at the moment it succeeds, and each job verifies unconditionally that the receipts it was
   supposed to produce exist, so a skipped coverage floor or a loop that built nothing fails the job
   instead of reporting green. **What is left of this reason is not in the repository**: no run has
   happened on the server yet, which needs a real pull request (`61-Manual-Test-Plan` §3.2).
2. **Open debt rows**, of which two are 🟠 P1: an unsigned release artefact (TD-108) — whose
   signing path was built, executed for the first time and found broken on 2026-09-12, leaving only
   the certificate itself outstanding (`62-Post-Audit-Plan` §5.2) — and the
   intermittent SQLite handle disposal (TD-140) — **diagnosed and fixed in the test harness on
   2026-09-11, and deliberately not yet counted as closed.** The cause is a process-wide
   `ClearAllPools()` in the harnesses' own teardown, argued from a captured stack and from removing
   the fix and watching the failure return; the run counts on their own are not significant
   (`62-Post-Audit-Plan` §5.1). The row stays P1 until the suite has run clean for longer than one
   iteration, because "it stopped happening" is what this row said twice before. TD-07, TD-54, TD-56
   and TD-121, which were on this list, are closed — TD-121 on 2026-09-11, when it also turned out to
   have been a lock leak: a vault lock after leaving the receipt page left the ledger on screen.
3. **The product is narrower than the document set.** Budgets, sync, notifications, forecasting and
   investments are specified and unbuilt. That is a scope statement, not a defect.

**What is genuinely good, and load-bearing.** The privacy posture is real rather than asserted: no
network egress anywhere in `src/`, enforced by a test that fails on the mere mention of a networking
type; a vault encrypted by SQLCipher with an Argon2id-wrapped key; OCR output that cannot reach the
ledger without explicit human confirmation; and receipts stored inside the vault so they survive a
backup. The core loop — create a vault, add accounts, enter or import transactions, scan a receipt,
categorise by rule or classifier, review a paginated ledger with attachments and an audit trail —
works end to end against the encrypted store.

- **Biggest strength** — The write path is now atomic and correctable, and the privacy claims are
  enforced by tests rather than by intention.
- **Biggest weakness** — ~~**Nothing runs the tests automatically.** A regression reaches `main`
  unless a human dispatches the workflow.~~ **Closed 2026-09-10** (TD-07, TD-136; plan item 0.1):
  the workflow fires on `pull_request` into `main` and on `push` to `main`. This is struck through
  rather than deleted because the sentence is the one `62-Post-Audit-Plan` §9 named as the plan's
  definition of done, and a definition of done that quietly disappears proves nothing. **The
  weakness that replaces it** was narrower, and was narrowed twice more on the same day: the project
  list drift (TD-54) by plan item 0.2, and the skipped-gate hole by item 0.3, which makes every gate
  record a receipt and every job verify the receipts it owed. **The whole of Stage 0 is now closed,
  and the honest residue is that none of it has run on the server yet.**
- **Biggest production risk** — ~~No restore path. `VaultBackupService` produces backups and, until
  iteration 37, the Settings button that should trigger one did nothing; restoring one still means
  copying files by hand (TD-56).~~ **Closed 2026-09-10** (plan item 1.1): a backup can be restored
  from Settings, the restore refuses anything it cannot verify first, and it leaves a copy of what
  it replaced so it can be undone. **What replaces it as the largest risk** is TD-108, the unsigned
  release artefact: there is no way for a user to tell this build from anyone else's. Narrowed on
  2026-09-12 and not removed — the pipeline now signs, verifies what it signed, and labels the
  unsigned case everywhere a person can see it, but there is still no certificate, so every artefact
  it produces is still one a user cannot distinguish from anyone else's.
- **Most important next action** — ~~Re-arm `pull_request:` on the CI workflow.~~ Done, 2026-09-10.
  ~~Next: restore from a backup, in the application (TD-56, plan item 1.1).~~ Done the same day.
  ~~Next: the navigation defect that replaces the whole shell (TD-121, plan item 1.2).~~ Done
  2026-09-11. ~~Next: the dashboard's ten inert widgets (plan item 1.3).~~ Done the same day: five
  tiles, each reading the ledger, five deleted, and TD-149 registered for the budgets' UTC window. Next:
  ~~the dashboard's "Scan Receipt" button, which has no command (plan item 1.4).~~ Done the same day,
  and it went out without an independent review — the one commissioned for it died on an API session
  limit — which was recorded rather than left implied, and **paid back the same day**: the review that
  covered item 1.5 covered 1.4 too, and found a guard that read the wrong capability flag
  (`62-Post-Audit-Plan` §4.4). ~~Next: the live scanner's camera list (plan item 1.5).~~ Done the same
  day, and **Stage 1 of the plan is closed**. ~~Next: Stage 2's 2.1, the intermittent SQLite handle
  disposal (TD-140), which cost a re-run twice during Stage 1 and taxes every number this document
  states.~~ Done 2026-09-11: the cause was the test harnesses' own `ClearAllPools()`, which is
  process-wide while xUnit runs classes in parallel, and it is fixed in the harness rather than in
  the product — **and the independent review found that the fix had leaked encrypted vault
  directories into `%TEMP%` on every run and left every in-memory fixture's database resident for
  the whole test process, both of them the same defect shape as the row being closed**
  (`62-Post-Audit-Plan` §5.1). ~~Next: TD-108, the unsigned release artefact (plan item 2.2) — the
  one thing that blocks giving this build to anyone.~~ Done as far as a commit can take it,
  2026-09-12: the signing path was executed for the first time since it was written and **did not
  work** — Git Bash rewrote `/fd` into a path, so signtool rejected the flag it had been given —
  and it now signs, verifies every loadable file afterwards, refuses six distinct ways, and is
  falsified in CI on every push by a self-test that needs no certificate. **What is left of the row
  is the purchase** (`62-Post-Audit-Plan` §5.2). **Next: TD-132 (plan item 2.3)**, the deferral that
  is now load-bearing in three subsystems and that every further subsystem decides by default.

**On the method, because it is the most transferable output.** Thirty-six iterations were run as
implement → mutate → verify: after each change, deliberate defects were introduced to check that
some test died. It repeatedly found what review did not — a survived mutation exposed that nothing
tested the regex timeout, that the scanner attached no image, that a read path accepted a tampered
file, and that a new documentation rule accepted an unrelated citation. **More consequentially, five
of Phase 3's six roadmap rows turned out to rest on a false or stale premise, and one of those was
this document's own.** Checking a ticket's claim before obeying it caught more defects than any
other single practice here.

### 11.2 Original verdict, iteration 0 — superseded, kept as the record

> | | |
> |---|---|
> | **Current rating** | **2.3 / 5 ★★☆☆☆** |
> | **Production status** | **Not Production Ready** |
>
> **Why not "Prototype / Early Development":** the architecture is coherent, the
> domain is properly modelled, the privacy posture is genuine and the Windows head
> builds clean and runs. This is well past prototype.
>
> **Why not "Production Ready with Conditions":** the blockers are not a bounded
> punch list. They include a data-integrity defect in the core write path, the
> absence of any correction mechanism for bad data, a security-architecture decision
> that becomes irreversible the moment real vaults exist, and a verification pipeline
> that cannot detect regressions in any of it.
>
> - **Biggest strength** — The privacy and trust architecture is real where it counts: no network egress anywhere in the codebase, and OCR output that cannot reach the ledger without explicit human confirmation.
> - **Biggest weakness** — No transactional boundary in the write path, and no mechanism to correct the result.
> - **Biggest production risk** — Silent, permanent financial data corruption: partial writes, silently dropped imports and misdated receipts all fail invisibly, and nothing in the application can detect or repair them afterwards.
> - **Most important next action** — Register `PineDbContext` as scoped, open a DI scope per MediatR request, remove `SaveChangesAsync` from every repository, and commit once per command inside an explicit database transaction — with tests for the transaction-plus-balance path written at the same time.
>
> ### Architecture verdict
>
> The architecture is fundamentally sound and does not need restructuring. The
> hexagonal decomposition is real rather than nominal: `Pine.Core` holds no external
> dependency, ports are defined in the domain and implemented in infrastructure, the
> AI engine is genuinely swappable, and the UI dispatches through MediatR rather than
> reaching for repositories. That skeleton is worth keeping, and a rewrite would
> discard a correct structure to fix defects that live inside it.
>
> What has not been built is the layer between the architecture and the data: no unit
> of work, no validation stage, no configuration, no logging, no concurrency control,
> no correction path. These are all cross-cutting concerns, all insertable at the
> composition root and the MediatR pipeline, and all addressable without touching the
> domain model or the views. That is why the recommendation is remediation rather
> than restructuring.
>
> The honest summary is that this project has the shape of a mature system and the
> substance of an early one. Sixty specification documents, six platform heads, five
> test projects, four empty modules and a plugin SDK sit on top of 11.7k lines of
> source, 28 tests, and a write path that cannot guarantee a transaction and its
> balance are saved together. The scaffolding was built to the plan; the load-bearing
> work was not. Nothing in that ordering is fatal — the foundations are good and the
> fixes are well-defined — but the project is considerably further from production
> than `STATUS.md` ("Phase 1 and 2: 100% Completed") represents, and closing that gap
> starts with making the write path atomic and the CI pipeline honest.
>
> ---
>
>

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

### 12.6 Next (after iteration 2) — superseded

Phase 0 item **5 — TD-04 + TD-13**. Closed by iteration 3; see §12.7.

### 12.7 Iteration 3 — Phase 0, item 5 (TD-04 + TD-13)

| Field | Value |
|---|---|
| Date | 2026-08-27 |
| Scope | Phase 0 item **5** → TD-04 (eager split validation) and TD-13 (owned collections dropped on update) |
| Outcome | **TD-04 ✅ closed · TD-13 ✅ closed** · 1 pre-existing latent corruption fixed incidentally · 2 new debts opened (TD-59, TD-60) |
| State | Working tree only — **not committed, not pushed** |

#### Verification actually executed

| Check | Result |
|---|---|
| `dotnet build src/Pine.Ui.Windows -c Release` | ✅ 0 warnings, 0 errors |
| `dotnet build src/Pine.Ui.Macos -c Release` | ✅ 0 warnings, 0 errors |
| `dotnet build src/Pine.Ui.Web -c Release` | ✅ 0 errors (1 pre-existing SDK `.targets` warning, unchanged from iteration 2) |
| `dotnet build src/Pine.Cli -c Release` | ✅ 0 warnings, 0 errors |
| `dotnet build Pine.slnx -c Release` | ✅ 0 warnings; only 2 × `NETSDK1147` (Android/iOS workloads absent) |
| `dotnet test tests/Pine.Tests.Unit` | ✅ **73 passed, 0 failed, 0 skipped** |
| `dotnet test tests/Pine.Tests.Integration` | ✅ **65 passed, 0 failed, 0 skipped** |
| Falsification: comment out `EnsureValid()` in `SaveAsync` | ✅ boundary test turns **red**, green again on restore |
| `SaveChangesAsync` remaining in any repository | ✅ zero (TD-01 not regressed) |
| `Pine.Core.csproj` package + project references | ✅ zero |
| `CurrentValues.SetValues` remaining in repositories | ⚠️ **4 remain — deliberate, see below** |

**Test suite: 124 → 138 passing** (28 at the audit baseline).

#### What was built

- **`SplitDefinition`** and **`LineItemDefinition`** (`Pine.Core/Transactions/`) — public `readonly record
  struct` transports. They exist because `TransactionSplit`/`TransactionLineItem` have `internal`
  constructors, so Application and UI could not otherwise hand a set of children to the aggregate.
- **`AddSplit` no longer calls `ValidateSplits()`.** Signature unchanged; the currency check stays. This is
  the whole of TD-04: the invariant was correct, enforcing it after every single append was the bug.
- **`ReplaceSplits(IEnumerable<SplitDefinition>)`** and **`ReplaceLineItems(IEnumerable<LineItemDefinition>)`**
  — build a staging list, validate it whole, and swap in **only on success**, so a rejected call leaves the
  aggregate byte-identical. An empty set clears the collection, which `ValidateSplits` already treats as
  valid. `ReplaceLineItems` deliberately carries **no** sum check: reconciling line items against the total
  is TD-19 and stays out of scope.
- **`Transaction.EnsureValid()`** — the aggregate-boundary entry point, called as the first statement of
  `SqliteTransactionRepository.SaveAsync` on both the insert and the update path.
- **`SqliteTransactionRepository.SaveAsync` rewritten** — handles three cases: insert; already-tracked
  same-reference (the common path under scope-per-request DI, where `SetValues` is not merely incomplete but
  a no-op with side effects); and detached, where scalars go through `CurrentValues.SetValues` and the two
  owned collections are merged **through the domain** via `ReplaceSplits`/`ReplaceLineItems`.
- **`ReconcileOwnedChildren`** — EF Core infers an owned child's state from whether its key is set. Both
  owned types assign a `Guid` in their constructor, so EF always guesses `Modified` and emits an `UPDATE`
  against a row that does not exist, failing the flush with a zero-rows-affected concurrency error. This was
  the true root cause of the update path being unusable, and it is **not** what the audit predicted.

#### The trap that made this ticket dangerous — removing the throw is the easy half

Deleting the eager `ValidateSplits()` call takes one line and immediately trades a false throw for silent
corruption: after that change, **nothing in the codebase called `ValidateSplits()` at all**, and every write
path — `AddTransactionCommand`, `CsvImporter`, any future split editor — would have happily persisted a €100
transaction allocated €50. The invariant had to be re-homed *before* the eager check was removed, not after.
It now lives at the persistence boundary, the only choke point every write actually passes through.

Two call sites were also **already latently wrong**: `ImportTransactionsCommand` and `ImportWizardViewModel`
apply an AI category and were guarded only by `if (tx.Splits.Count == 0)`. Weaken or remove that guard under
the old code and each would append a *second* full-amount split, doubling the sum. Both now use
`ReplaceSplits` and are idempotent regardless of the guard.

#### How the fix is *proven*, not asserted

- **Falsification.** `SaveAsync_RefusesATransactionWhoseSplitsDoNotSumToTheTotal` was run with
  `EnsureValid()` commented out and **failed**, then passed again on restore. A test that cannot fail proves
  nothing; this one demonstrably can.
- **Round-trip from a different connection.** `TransactionOwnedCollectionRoundTripTests` drives splits
  **2 → 3 → 1 → 0** and line items **2 → 3**, asserting after each step from a brand-new `PineDbContext`
  *and* from a raw SQLCipher `SELECT COUNT(*)` that bypasses EF entirely. Asserting through the writing
  context would pass even for a repository that dropped the collections, because EF's identity map returns
  the in-memory graph regardless of what SQL ran. The shrink direction is the one a naive clear-and-re-add
  gets wrong, which is why 3 → 1 → 0 is tested and not merely growth.
- **A real file vault, not `":memory:"`** — an in-memory database dies with its connection, so a second
  connection would see an empty database and every assertion would pass for the wrong reason.
- **Production-shaped writes** — through the repository plus `EfCoreUnitOfWork` (begin / flush / commit),
  never `SaveChangesAsync` behind the repository's back, which would exercise a path that does not exist.
- **Atomicity of rejection** is asserted positively: a rejected `ReplaceSplits` leaves count, amounts,
  categories, notes *and* split ids unchanged.

#### Pre-existing defects fixed incidentally

- **Owned-entity instance aliasing — a live, reachable corruption path.** `Money` and `CurrencyCode` are both
  sealed *reference* types. Two splits built from the same `Money` instance were one owned entity to EF,
  which wrote the currency column for a single owner and `NULL` for the rest, tripping the `NOT NULL`
  constraint. This was reachable today: `ImportTransactionsCommand` and `ImportWizardViewModel` both pass
  `tx.TotalAmount` straight in as the split amount. Fixed at the correct layer — `TransactionSplit` and
  `TransactionLineItem` now deep-copy incoming `Money` (including its nested `CurrencyCode`) in their
  constructors, so no two owned children can share an instance. A regression test pins the aliased shape.
- **A `PineDbContext` mapping change was made and then reverted.** Mid-iteration the split/line-item currency
  was remapped from a nested `OwnsOne` to a `HasConversion` value converter to tolerate the aliasing above.
  Once the defensive copy landed, that mapping change became redundant, and it was **reverted after testing
  showed the full integration suite passes without it**. This was deliberate: the converter left
  `PineDbContextModelSnapshot.cs` describing a model the context no longer built, and
  `EfCoreVaultMigrator.MigrateAsync` calls `Database.Migrate()` — snapshot drift there is a vault-open risk
  for every existing user. Reverting removed that risk class entirely rather than mitigating it, and required
  no migration. **`PineDbContext.cs` is unmodified in the final tree.** The counter-argument on record: the
  copy only protects children created *through* the aggregate, and EF's load path bypasses constructors. That
  was judged not to apply — EF materialises each owned child per row, so loaded children are already distinct
  instances, and the 2 → 3 → 1 → 0 round-trips exercise exactly that load-mutate-save path.

#### New debt opened

- **TD-59** — `ReconcileOwnedChildren` promotes owned children from `Modified` to `Added` on the reasoning
  that neither owned type exposes a mutator, so a `Modified` child can only be a mis-classified insert. That
  reasoning is **load-bearing**: the day either type gains a public setter, a genuine update becomes a silent
  insert. It was found **unpinned** and is now pinned by
  `OwnedTransactionChildren_ShouldNot_ExposePublicSetters`, so such a change breaks a test instead of the
  database. The underlying reliance on EF's state inference remains debt.
- **TD-60** — `ReplaceSplits`/`ReplaceLineItems` mint fresh `Guid` ids, so a detached save deletes and
  re-inserts every owned child with new ids. Harmless today because nothing references a split id; it becomes
  a real problem the moment rules, attachments or an audit trail do.

#### Why `CurrentValues.SetValues` is still present in four places

The literal checklist item "zero `CurrentValues.SetValues` in repositories" **does not hold**, and saying
otherwise would be false. It remains in the account, budget and category repositories, and on the detached
branch of the transaction repository. The defect it names is nonetheless closed:
`EfModel_DeclaresOwnedCollectionsOnTransactionOnly` asserts against the live EF model that
`Transaction.Splits` and `Transaction.LineItems` are the **only** owned collections in the entire model, so
on the other three aggregates `SetValues` is provably dropping nothing — `Account.Balance` and `Budget.Limit`
are `ComplexProperty`, and `Category`/`Setting` have neither. On the transaction path it now copies only
scalars and the `TotalAmount` complex property, with the owned collections merged explicitly alongside it.
The call was left in place rather than hand-rolled per property because that is precisely what it is correct
for; the model-metadata test is what stops the day someone adds a fourth owned collection from being silent.
No fix was invented for repositories that did not have the defect.

#### INCOMPLETE / NOT DONE

1. **Android and iOS still cannot be compiled locally** (`NETSDK1147`, workloads absent). Unchanged from
   iterations 1 and 2. Nothing in this iteration touched their code, but they remain **not compiler-proven**.
2. **Line-item shrink/clear is covered only for growth in the committed suite** (2 → 3). `ReplaceLineItems`
   supports clearing and was exercised shrinking in a scratchpad harness, but **a scratchpad harness is not
   the committed test suite and does not count toward a ✅**. Committed coverage for the shrink direction is
   on `Splits` only.
3. **TD-19 remains untouched** — nothing reconciles `Σ LineItem.TotalPrice` against `TotalAmount`. A
   transaction can still hold internally contradictory line items; only the *split* sum is enforced.
4. **The invariant is enforced at the persistence boundary, not at every mutation.** An aggregate mutated and
   never saved is never validated, and a caller that interleaves `AddSplit` calls relies entirely on the
   repository check. This is the deliberate trade of TD-04, and it is sound only while `SaveAsync` calls
   `EnsureValid()` — now falsification-tested, but a single point of enforcement.
5. Still untouched and still P0: TD-02 (no correction path), TD-03 (silent import dedup), TD-12 (currency not
   validated against account). Deliberately out of scope.
6. **Nothing was committed or pushed.**

### 12.8 Next (after iteration 3) — superseded

> Closed by iteration 4; see §12.9.


Phase 0 item **6 — TD-12**: a MediatR validation behaviour, and validating transaction currency against the
account's currency. `AddTransactionCommandHandler` today builds a `Money` from the request, **saves the
transaction**, and only then calls `account.Deposit(...)`, which throws on mismatch inside `Money.Add` —
leaving an orphaned ledger entry with no matching balance movement. `TransferFundsCommandHandler` is the only
handler that validates currency up front. With TD-01 closed the write is now rolled back rather than
half-committed, so the damage is contained, but the command still fails at the wrong layer and reports the
wrong thing to the user. TD-12 is the last of the two corruption paths named in blocker 10; TD-13, the other,
was closed in this iteration.

---

### 12.9 Iteration 4 — Phase 0, item 6 (TD-12)

| Field | Value |
|---|---|
| Date | 2026-08-27 |
| Scope | Phase 0 item **6** → TD-12 (transaction currency never validated against the account) |
| Outcome | **TD-12 ✅ closed — and reclassified: it was no longer a corruption path.** Blocker 10 closes entirely · 1 duplication removed incidentally · 3 new debts opened (TD-61, TD-62, TD-63) |
| State | Working tree only — **not committed, not pushed** |

#### Verification actually executed

| Check | Result |
|---|---|
| Characterisation of the PRE-fix defect on a real vault | ✅ measured — see "the trap" below |
| `dotnet build src/Pine.Ui.Windows -c Release` | ✅ 0 warnings, 0 errors |
| `dotnet build src/Pine.Ui.Macos -c Release` | ✅ 0 warnings, 0 errors |
| `dotnet build src/Pine.Ui.Web -c Release` | ✅ 0 errors (1 pre-existing SDK `.targets` warning, unchanged from iterations 2–3) |
| `dotnet build src/Pine.Cli -c Release` | ✅ 0 warnings, 0 errors |
| `dotnet build Pine.slnx -c Release` | ✅ 0 warnings; only 2 × `NETSDK1147` (Android/iOS workloads absent) |
| `dotnet test tests/Pine.Tests.Unit` | ✅ **81 passed, 0 failed, 0 skipped** |
| `dotnet test tests/Pine.Tests.Integration` | ✅ **71 passed, 0 failed, 0 skipped** |
| Falsification M1 — neuter the currency comparison | ✅ `AddTransaction_WithAMismatchedCurrency_…` turns **red**, with the pre-fix `InvalidOperationException` verbatim |
| Falsification M2 — swap the two `AddOpenBehavior` calls | ✅ **5 tests red** (1 architecture + 4 integration, all on `Calls.Should().BeEmpty()`) |
| Falsification M3a — drop normalisation in the validator | ✅ `AddTransaction_WithALowercaseCurrency_…` turns **red** |
| Falsification M3b — drop normalisation in `CurrencyCode` | ✅ same test **red**, with `ArgumentException` from the constructor |
| Falsification M4 — hide one validator from the DI scan | ✅ `EveryValidator_…ShouldBeRegisteredAsScoped` **red**, plus 2 integration tests |
| Green restored after every mutation | ✅ 81 + 71 re-run clean |
| `SaveChangesAsync` outside `EfCoreUnitOfWork` | ✅ zero (TD-01 not regressed) |
| `Pine.Core.csproj` package + project references | ✅ zero |
| Android / iOS compiled | ❌ **impossible locally** — workloads absent, unchanged since iteration 1 |

**Test suite: 138 → 152 passing.**

#### What was built

- **The validator seam** (`Pine.Application/Common/`) — `IRequestValidator<TRequest>`,
  `ValidationError(PropertyName, Message)`, `RequestValidationException` carrying the structured `Errors`,
  and the shared `CurrencyCodeText.TryNormalise`.
- **`ValidationBehavior<TRequest, TResponse>`** (`Common/Behaviors/`, beside `TransactionBehavior`) —
  resolves `IEnumerable<IRequestValidator<TRequest>>`, runs every validator **sequentially** (they share one
  scoped `PineDbContext`, which is not thread-safe, so `Task.WhenAll` here would be a latent concurrency
  bug), aggregates all errors into one exception, and calls the handler only when there are none. A request
  with no registered validator sees an empty enumerable and passes through.
- **Opt-in with no second source of truth.** Unlike `ITransactionalRequest`, validation has **no marker
  interface**: the opt-in signal *is* the registered validator. A marker would allow a request to be labelled
  "validated" with nothing behind it — silently unvalidated, which is precisely the failure this seam exists
  to prevent.
- **Four validators**, each in the same file as its command, matching the existing one-file convention:
  `AddTransactionCommandValidator`, `RecordTransactionCommandValidator`, `TransferFundsCommandValidator`,
  `ImportTransactionsCommandValidator`.
- **Reflection registration** in `AddPineApplication` — every `IRequestValidator<>` in the assembly is
  registered Scoped via `TryAddEnumerable`, once, in the single extension all five composition roots call.
- **`TransferFundsCommandHandler`'s up-front checks folded into its validator**, removing the second idiom.

#### Why the seam is hand-rolled and not FluentValidation

Decided explicitly, not by default. FluentValidation's value is *declarative input rules* — `NotEmpty`,
`Length`, `Matches`. The rule this ticket needed is none of those: "the currency must equal the account's
currency" is a **database read**, and the only part of the library that would have been used is its
`IValidator<T>` abstraction plus assembly scanning — roughly forty lines, which is what was written. Against
that: TD-42 recorded that this solution had **no central package management**, so a new dependency meant
hand-pinning a version across every project file that used it (nineteen at the time; twelve now, and
one `Directory.Packages.props` entry since iteration 28), and `TreatWarningsAsErrors` makes any new analyzer
surface a build-breaking risk. Every comparable seam here — `ITransactionalRequest`, `ScopedMediator`,
`EfCoreUnitOfWork` — is hand-rolled and documented in prose, and this audit repeatedly penalises *two idioms
for one job*. The counter-argument on record: once validation grows past a handful of rules, re-implementing
rule composition, conditional rules and message templating would be a bad trade, and the seam should be
replaced rather than extended. `IRequestValidator<T>` is deliberately narrow enough that a FluentValidation
adapter is a drop-in later.

#### Pipeline ordering, and what it costs

`AddOpenBehavior` order is execution order. `ValidationBehavior` is registered **first**, so it wraps
`TransactionBehavior` and every validator's read happens **outside** the write transaction.

The consequence is observable and asserted: a rejected command's unit-of-work log is **empty**, not
`Begin, Rollback`. That is the difference between a refusal and a failure, and M2 shows nothing else would
catch the reversal — the exception type and message are identical either way.

The price is a time-of-check/time-of-use window, and it is provably empty *for this rule*. The only fact read
outside the transaction is an account's **currency**, and an account's currency cannot change after
`Account.Create`: `Balance` has a private setter and the only mutators, `Deposit` and `Withdraw`, route
through `Money.Add`/`Money.Subtract`, which throw on any currency but the one already held. That assumption
is not left to prose — `Account_ShouldNot_ExposeAnyPublicMeansOfChangingItsCurrency` pins `Account`'s public
surface to exactly `{Create, Deposit, Withdraw}` with no public setters, so the day an account gains a
re-denomination path, this ordering decision breaks a test instead of the ledger. **That test is a tripwire
on an assumption, not a proof there is no window**, and it is recorded as such rather than sold as more.

#### Failure mode: a typed exception, and its precise relationship to TD-24

Validation failures throw `RequestValidationException`, not a result type. A `Result<T>` is the better shape
in the abstract and is not available at an honest price: Pine's requests return `Guid`,
`IReadOnlyList<Transaction>`, `ImportProfile`, `string?` and `void`, and threading an envelope through them
would rewrite every handler, every `Send` call site and every view model. That is TD-24, and it is **not**
done here.

What *did* change is what the existing indiscriminate `catch (Exception ex) { ErrorMessage = $"Error: {ex.Message}"; }`
now catches. Before: `Cannot add money of different currencies: USD and EUR` — a domain internal, thrown from
inside `Money.Add`, shown verbatim to a user. After: `Transaction currency EUR does not match account currency USD.`
TD-24's blast radius narrows on the covered paths; TD-24 itself stays open, and no view model was touched.

#### The trap in this ticket — the defect was not what the audit said it was

§6.4 and blocker 10 both described TD-12 as a live corruption path: transaction saved, then
`account.Deposit(...)` throws, leaving *an orphaned ledger entry with no matching balance movement*. The
first thing iteration 4 did was try to reproduce that, on a real SQLCipher file vault through the real
container and the real pipeline. **It does not happen.** Measured, pre-fix:

```
EXCEPTION = System.InvalidOperationException: Cannot add money of different currencies: USD and EUR
TXROWS    = 0
BALANCE   = 1000  (unchanged)
UOW       = Begin, Rollback
```

Closing TD-01 in iteration 2 had already contained it. No repository calls `SaveChangesAsync`, so the
"saved" transaction was only staged on the scoped `DbContext`, and `TransactionBehavior` rolled the whole
command back. **TD-12 had silently degraded from data corruption to a wrong-layer throw with a leaky message
— and the audit went on asserting the corruption for two more iterations.** Writing the ✅ without saying
that would have been the convenient thing rather than the true one.

Two further things the ticket's framing got wrong, both found by enumerating rather than trusting the list:

- **The mismatch is not reachable from the shipped UI at all.** `AddTransactionViewModel` and
  `ScanReceiptViewModel` both derive the currency from `SelectedAccount.Balance.Currency.Value`;
  `DiagnosticsViewModel` hardcodes `"USD"`; `CsvImporter` builds every `Money` from
  `targetAccount.Balance.Currency`. There is today no user gesture that produces a mismatched
  `AddTransactionCommand`. The validators are therefore a **boundary guard against future and pluggable
  callers**, not a fix for a bug a user can hit — the honest severity is 🟡, not 🟠.
- **`ImportTransactionsCommand` cannot be validated up front, and pretending otherwise would be worse.**
  The imported transactions do not exist until `Importer.ImportAsync` runs *inside* the handler, and reading
  the stream in a validator would consume a forward-only stream the handler needs. So the import validator
  checks only that the account exists, and the currency check sits in the one window that does exist: after
  the import and dedup, **strictly before the first `SaveAsync`**. It throws the same
  `RequestValidationException` so callers see one failure shape. `IImporter` is a port and the command
  carries the importer *instance* as a payload field, so a plugin can supply any currency it likes — that
  guard is the boundary check on the port, and it is the one place TD-12's fix is genuinely load-bearing
  today.

#### How the fix is *proven*, not asserted

- **Four falsification mutations, all executed, all restored.** M1 (neuter the comparison) makes the pre-fix
  `InvalidOperationException` reappear verbatim — the same string the test asserts is *absent*, so the
  mutation is caught twice. M2 (swap the behaviour order) reddens five tests. M3a/M3b (drop the
  normalisation, in the validator and in the domain) redden the lowercase test. M4 (hide a validator from the
  reflection scan) reddens the architecture test plus two integration tests — the nastiest production
  analogue, a validator that exists, reads correctly and is never called.
- **Rejection asserted from outside EF entirely.** `AtomicityHost.CountRowsRaw` issues
  `SELECT COUNT(*)` over a raw keyed SQLCipher connection, so "zero transactions, one untouched account" is a
  claim no part of EF's model, translation or identity map can explain away.
- **The empty unit-of-work log** is the assertion that distinguishes *refused* from *rolled back*. Without it
  M2 would be invisible.
- **Normalisation was previously unpinned.** Before this iteration no test anywhere passed a lowercase
  currency code, so `CurrencyCode`'s `Trim().ToUpperInvariant()` could have been deleted against the 138-test
  baseline with nothing going red. It is now pinned.
- **Every money write goes through the pipeline.** No view model or service touches
  `ITransactionRepository`/`IAccountRepository` directly; the only non-handler consumer,
  `CategorizationTrainingBackgroundService`, reads. So validation has no bypass.

#### Pre-existing defects fixed incidentally

- **`TransferFundsCommandHandler` compared raw strings.** Its old check was
  `sourceAccount.Balance.Currency.Value != request.CurrencyCode`, which rejected `"usd"` outright even though
  `CurrencyCode` trims and upper-cases and the domain accepts it. Folding the check into the validator
  normalises first, so a legitimate input stopped being rejected. This is a **behavioural widening**, not a
  relocation, and is called out rather than buried.
- **A shape check triplicated across three validators** was promoted to the single
  `CurrencyCodeText.TryNormalise`. Filed as TD-61, because one shared copy of a domain rule living outside the
  domain is still a copy.

#### On modifying `AtomicityHost`, the harness that proves TD-01

`AtomicityHost` exists to prove a closed P0 blocker, so changing it deserves scrutiny. The change is
**purely additive**: one new method, `CountRowsRaw(string table)`, which opens a raw keyed SQLCipher
connection via the pre-existing `TempVault.OpenRaw` and runs `SELECT COUNT(*)`. No existing member was
altered or removed, no registration in the container changed, and **no existing test file under
`tests/Pine.Tests.Integration/Atomicity/` was touched** — `TransferFundsAtomicityTests`,
`RecordTransactionAtomicityTests`, `ImportAtomicityTests`, `RequestScopeAndRepositoryBoundaryTests` and
`VaultBackupIsNotTransactionalTests` are byte-identical to iteration 3 and all 65 of their assertions still
pass unchanged inside the new 71.

#### New debt opened

- **TD-61** — `CurrencyCodeText.TryNormalise` restates `CurrencyCode`'s `^[A-Z]{3}$` outside the domain,
  because the constructor reports failure by throwing and a validator cannot use it. They drift the moment
  `CurrencyCode` gains ISO 4217 *membership* validation (TD-18). The real fix is a non-throwing
  `CurrencyCode.TryCreate`.
- **TD-62** — the validators cover **currency and nothing else**. `AddTransactionCommand` with `Amount == 0`
  still reaches `account.Deposit(...)` and surfaces "Deposit amount must be strictly positive."; the import
  handler still throws a bare `ArgumentException` for a missing account. Same wrong-layer class this ticket
  fixed, different fields. Deliberately out of scope; the seam now exists to fix it cheaply.
- **TD-63** — `CreateBudgetCommand` carries a currency with no account to check it against. Nothing anchors a
  budget's currency to anything.

#### INCOMPLETE / NOT DONE

1. **Android and iOS still cannot be compiled locally** (`NETSDK1147`, workloads absent). Unchanged from
   iterations 1–3. Nothing here touched their code; they remain **not compiler-proven**.
2. **TD-12's severity in §8 was overstated for two iterations and is only now corrected.** The register row
   and blocker 10 both said "orphaned ledger entry" long after TD-01 made that impossible. The row is fixed;
   the lesson — that closing one debt silently changes another's severity, and nothing re-checks — is not
   fixed by anything in this iteration.
3. **`CreateAccountCommand` has no validator.** It *defines* an account's currency, so there is nothing to
   validate it against, but it also means a vault can be seeded with `"ZZZ"` — `CurrencyCode` validates shape,
   not membership. That is TD-18 and stays open.
4. **The TOCTOU argument rests on a tripwire, not a proof.** Reading account currency outside the transaction
   is sound only while an account's currency is immutable. The architecture test detects a change to
   `Account`'s public surface; it cannot detect one made through EF, reflection or a raw SQL update.
5. **The transfer cross-currency message is not pinned.** That test asserts the exception type and a non-empty
   `Errors`, not the message text.
6. **TD-24 is not fixed.** View models still catch `Exception` and render `ex.Message` raw. The message they
   render on the covered paths is now deliberate; the mechanism is still wrong.
7. Still untouched and still P0: TD-02 (no correction path), TD-03 (silent import dedup), TD-09
   (money-path suite), TD-06/TD-11 (key envelope, password policy). Deliberately out of scope.
8. **Nothing was committed or pushed.**

### 12.10 Next (after iteration 4) — superseded

Phase 0 item **7 — TD-03**: rewrite import deduplication on a stable key, with a user-facing review list.
`ImportTransactionsCommandHandler` currently discards any parsed transaction matching an existing one on
`(Date.Date, TotalAmount.Amount)` alone, silently and with no record. Two genuine £4.50 coffees on the same
day are one transaction as far as Pine is concerned, and the user is never told. This is the last of the
original P0 data-loss findings still open — TD-01, TD-04 and TD-13 are closed, and TD-12 closed and was
reclassified — and unlike TD-12 it is **reachable from the shipped UI today**, through
`ImportWizardViewModel`. It needs a stable import key (source row identity, not a value tuple), a
quarantine/review path for suspected duplicates rather than a silent drop, and the dedup decision surfaced
in the wizard. Note that the currency guard added in iteration 4 sits immediately after the dedup loop in
that same handler, so both changes land in the same method and the guard must not be displaced ahead of the
new key logic.

### 12.11 Iteration 5 — Phase 0, item 7 (TD-03)

| Field | Value |
|---|---|
| Date | 2026-08-27 |
| Scope | Phase 0 item **7** → TD-03 (import de-duplication silently discards legitimate transactions) |
| Outcome | **TD-03 ✅ closed.** Blocker 3 closes entirely · a test-coverage hole found and closed during integration · 4 new debts opened (TD-64 … TD-67) |
| State | Working tree only — **not committed, not pushed** |

#### Verification actually executed

| Check | Result |
|---|---|
| `dotnet build src/Pine.Core -c Release` | ✅ 0 warnings, 0 errors |
| `dotnet build src/Pine.Application -c Release` | ✅ 0 warnings, 0 errors |
| `dotnet build src/Pine.Infrastructure -c Release` | ✅ 0 warnings, 0 errors |
| `dotnet build src/Pine.Ui.Avalonia -c Release` | ✅ 0 warnings, 0 errors |
| `dotnet build src/Pine.Ui.Windows -c Release` | ✅ 0 warnings, 0 errors |
| `dotnet build src/Pine.Ui.Macos -c Release` | ✅ 0 warnings, 0 errors |
| `dotnet build src/Pine.Ui.Web -c Release` | ✅ 0 errors (1 pre-existing SDK `.targets` warning, unchanged since iteration 2) |
| `dotnet build src/Pine.Cli -c Release` | ✅ 0 warnings, 0 errors |
| `dotnet build tests/Pine.Tests.Unit` + `tests/Pine.Tests.Integration` | ✅ 0 warnings, 0 errors |
| `dotnet build Pine.slnx -c Release` | ❌ **2 errors, restore aborts** — see the correction below; this command has never proven anything about the code |
| `dotnet test tests/Pine.Tests.Unit` | ✅ **90 passed, 0 failed, 0 skipped** |
| `dotnet test tests/Pine.Tests.Integration` | ✅ **80 passed, 0 failed, 0 skipped** |
| Falsification M1 — key lookup returns nothing | ✅ 2 unit + 3 integration tests **red** |
| Falsification M2 — neuter the in-batch `HashSet` guard | ✅ 1 unit + 1 integration test **red** |
| Falsification M3 — drop `t.AccountId == accountId` from the repository probe | ⚠️ **survived 79 tests**; gap closed, then **red**, then restored — see the trap |
| Falsification M4 — constant import key in `CsvImporter` | ✅ 2 importer tests **red** |
| Green restored after every mutation | ✅ 90 + 80 re-run clean; mutated files SHA-256-identical to their pre-mutation copies |
| `SaveChangesAsync` outside `EfCoreUnitOfWork` | ✅ zero (TD-01 not regressed) |
| `Pine.Core.csproj` package + project references | ✅ zero |
| Currency guard still ahead of the first `SaveAsync` | ✅ verified by line order after the rewrite (dedup → guard → save) |
| Android / iOS compiled | ❌ **impossible locally** — workloads absent, unchanged since iteration 1 |
| The wizard review screen rendered | ❌ **not observed** — no GUI was launched; see INCOMPLETE |

**Test suite: 152 → 170 passing.**

#### A correction to the previous iterations' verification tables

§12.9 recorded `dotnet build Pine.slnx -c Release` as "✅ 0 warnings; only 2 × `NETSDK1147`". That
reading is wrong, and it was re-measured here: `NETSDK1147` is an **error**, not a warning, and it
aborts *restore*, so the solution build terminates in under a second having compiled **nothing**. Every
solution-wide "clean build" claim from iterations 1–4 rests on a command that never built the product.
The per-project builds above are what actually carry the evidence, and they are listed individually for
that reason. Nothing about the code changes; what changes is which command is allowed to be cited.

#### What was built

- **`ImportKeyFactory`** (`Pine.Core/Imports/`, BCL-only so `Pine.Core` keeps zero references) —
  `FromReference(source, reference)` → `r1:` + SHA-256, for sources carrying their own stable identifier;
  `Derive(date, amount, currency, payee, source, rowIndex)` → `h1:` + SHA-256, for those that do not.
  Invariant-culture throughout, no `GetHashCode`, no randomness. **Versioned prefixes**: if the
  derivation ever changes it takes a new prefix, so old rows fail to match the new keys rather than
  matching them by accident — a silent re-match would re-create exactly the data loss being removed.
- **`Transaction.ImportKey`** (nullable) + `AssignImportKey` + `MaxImportKeyLength = 128`, a nullable
  `TEXT(128)` column and an `(AccountId, ImportKey)` index (migration `20260827141109`, reversible `Down`).
- **`ITransactionRepository.GetExistingImportKeysAsync`** — answers only "which of these keys do you
  already have?", `AsNoTracking`, `IN` push-down, empty-set short circuit. This **replaces a full
  `GetByAccountIdAsync` load on every import**, which grew linearly with the account's history.
- **The handler's step 3, rewritten.** Three outcomes, all reported: key already on the account, key
  already seen earlier in this batch, and no key at all. The last is the one worth stating plainly — a
  keyless row is **still imported**, because refusing it would be new data loss, but it is reported as
  `ImportedWithoutKeyCheck` rather than quietly treated as if it had passed a check it never took.
- **`ImportTransactionsResult`** replaces `IReadOnlyList<Transaction>`. The old contract could only
  express what survived; a dropped row left no trace in the return value, in a log, or on screen. The
  new one carries every parsed row in file order with its disposition and a finished user-facing reason.
- **`CsvImporter` assigns a key to every row it produces**, preferring `FromReference` when the profile
  maps an optional `"Reference"` column (a plain extra entry in the existing free-form `ColumnMappings`,
  so no schema change and no migration) and falling back to `Derive`. The row index counts the row's
  position **in the file**, incremented for every record the reader yields including ones later
  rejected — an index into the output list would shift whenever a row ahead of it was rejected,
  renumbering every later row and breaking the match against already-stored keys.
- **A wizard review step** (`IsStep3`, matching the existing `IsStepN` idiom) listing every row with its
  outcome and the handler's reason **passed through verbatim** — the UI derives no prose of its own, so
  there is one source of truth for what happened to a row. It reuses existing design-system resources
  (`PineSurface`, `PineSurfaceElevated`, `PineBorder`, `PineText`, `PineTextMuted`, `PineSuccess`,
  `PineWarning`) and adds none. The wizard auto-closes only when nothing was skipped **and** nothing was
  imported unchecked; anything else stops on the review step and waits for the user.

#### The trap in this ticket — a passing suite that did not test what it appeared to

Two things the ticket's framing assumed turned out not to hold, and both were found by checking rather
than by trusting.

**1. The de-duplication lookup's account scoping was untested, and nothing noticed.** After the four
child workstreams integrated and all 169 tests passed, one further mutation was run that no workstream
had tried: deleting `t.AccountId == accountId` from
`SqliteTransactionRepository.GetExistingImportKeysAsync`. **All 79 integration tests stayed green.**
That mutation is silent cross-account data loss of precisely the TD-03 kind, reached through a
different door — importing a joint statement into a second account would skip every row on the strength
of the *first* account's history, and the new review list would confidently explain why each one was
"already imported". A suite that proves de-duplication works while permitting it to work against the
wrong account is not proving what it claims.
`Import_OfTheSameStatementIntoASecondAccount_IsNotBlockedByTheFirstAccountsKeys` was added, the mutation
re-run (**red**, as it now must be), and the file restored byte-for-byte. That test is test 170.

**2. "The importers must assign the keys" presumed importers that exist.** `CsvImporter` does the real
work. `ExcelImporter` and `PdfImporter` **parse nothing at all** — each opens the file, walks it, and
returns an empty list without ever constructing a `Transaction`. There was no key to assign because
there is no row to key, and no Excel or PDF parser was invented to manufacture the appearance of one.
Both are now documented as stubs, with what a future implementation must do and what happens meanwhile
to any keyless row that escaped. Neither is reachable from the wizard, which hardcodes `CsvImporter`.
Filed as **TD-67** rather than left as an implied capability.

#### How the fix is *proven*, not asserted

- **Four falsification mutations executed and restored**, each verified byte-identical afterwards by
  SHA-256 and `cmp`. M1 (key lookup returns nothing) reddens 5 tests; M2 (in-batch guard) reddens 2;
  M4 (constant key in `CsvImporter`) reddens 2; M3 (account scoping) reddened **nothing until a test was
  written for it**, which is the finding above.
- **Row counts and persisted keys are read from outside EF.** `AtomicityHost.CountRowsRaw` and the new
  additive `ReadImportKeysRaw` issue raw SQL over a keyed SQLCipher connection, so "two rows survived"
  and "the legacy key is NULL" are claims no identity map or change tracker can explain away.
- **The headline regression is asserted the other way round.** Two same-day, same-amount, same-payee
  rows from one file both reach the file — the exact pair the old rule destroyed.
- **Legacy rows are proven inert**, not assumed: a populated pre-`ImportKey` vault migrates, reopens,
  keeps its rows, and their keys read raw-NULL; those NULLs then match no candidate and block nothing.
- **The old test that encoded the bug was deleted deliberately, not quietly.**
  `Handle_WithDuplicates_SkipsDuplicates` asserted that a same-date same-amount row *is* discarded.
  Keeping it green would have meant keeping the defect; it is removed with a header comment recording
  exactly what it used to assert.

#### Fixes made by the coordinator, not by a child workstream

Two breaks left by the previous tree's mid-integration crash were repaired directly, and are recorded
here so the log does not credit them to the workstreams: the `FaultInjectingTransactionRepository`
pass-through for the new repository member (`tests/Pine.Tests.Integration/Atomicity/FaultInjection.cs`),
and the head-migration tripwire in `VaultBackupServiceTests`, retargeted to
`20260827141109_AddTransactionImportKey`. The account-scoping test above was also written at
integration, not by a workstream.

#### Pre-existing defects fixed incidentally

- **`ImportAtomicityTests`' stub importer carried a stale comment** describing "date+amount
  de-duplication" as the behaviour under test. Corrected; the test itself was only updated for the new
  return type.
- **The full-account load is gone from the import path.** Not a correctness fix, but the old dedup read
  every transaction on the account into memory on every import; the replacement is an indexed probe over
  the candidate keys only.

#### New debt opened

- **TD-64** — the currency guard iterates only the rows about to be saved, so a *skipped* row in the
  wrong currency is never reported. Correct in that nothing bad is written, but a file whose first
  import was rejected for currency can look clean on re-import.
- **TD-65** — check-then-insert race. Two concurrent imports of one file can both pass the existence
  probe before either inserts. The `(AccountId, ImportKey)` index is deliberately non-unique, so nothing
  stops the second write. Asserted nowhere; accepted debt, now written down.
- **TD-66** — the derived key's `source` is `ImportProfile.Name`, the only source identifier reachable
  inside `ImportAsync`. Renaming or duplicating a profile therefore changes every derived key, and the
  next import of an already-imported file reports every row as new. Renaming a profile is a data event.
- **TD-67** — `ExcelImporter` and `PdfImporter` parse nothing (above).

#### INCOMPLETE / NOT DONE

1. **The review screen has never been seen.** Its bindings are compiler-checked (compiled bindings are
   on; a deliberately misspelled binding path was confirmed to produce `AVLN2000`, then reverted), but
   `DynamicResource` keys are not compile-checked, and layout, spacing and the skipped-row tint are
   **reasoned, not observed**. No GUI was launched. Nothing here should be read as a claim that the
   screen looks right.
2. **Android and iOS still cannot be compiled locally** (`NETSDK1147`, workloads absent). Unchanged
   since iteration 1. They remain **not compiler-proven**, and TD-52's runtime consequence remains
   inferred rather than observed.
3. **`dotnet build Pine.slnx` still does not build the solution**, and now never did — see the
   correction above. Solution-wide build coverage does not exist locally and, with CI disarmed (§12.12),
   does not exist remotely either.
4. **Concurrency is untested and unhandled** (TD-65). The index is non-unique by choice, and nothing
   asserts what two simultaneous imports do.
5. **No importer other than CSV produces anything** (TD-67), so the "prefer `FromReference`" branch is
   exercised only by CSV's optional `Reference` mapping and by test stubs — no real bank format has been
   run through it.
6. **The key's stability rests on the profile name** (TD-66), which the wizard mints per import as a
   throwaway `ImportProfile.Create("Final Profile", …)`. That happens to be stable today; it is not
   stable by construction.
7. **TD-24 is not fixed.** The wizard still catches `Exception` and renders `ex.Message` raw on the
   import path.
8. **Nothing was committed or pushed.**

### 12.12 Reconciliation pass — re-checking the register against the code

Iteration 4 found that this audit had asserted a corruption path (TD-12) that its **own iteration-2
work had already closed**, and that the claim stood wrong for two further iterations. The lesson
recorded there was that *closing one debt silently changes another's severity, and nothing re-checks*.
This section is that re-check, run deliberately for the first time: every 🔴/🟠 row in §8 and every
§9 blocker still marked open was compared against what the code does today.

**Method.** Source read, with a file and line for every claim. Where a claim could not be checked
cheaply it is listed as unverified rather than guessed — a guess in this register is what created the
TD-12 problem in the first place. Rows already scheduled for imminent work (TD-02, TD-18, TD-19, TD-24,
TD-47, TD-61, TD-62, TD-63) were left alone, as was TD-03, which was changing under this very iteration.

#### Findings that changed a row

| Row | Audit's claim | Verdict | What the code actually does |
|---|---|---|:---|
| **TD-07 / Blocker 6 / roadmap item 3** | CI "now builds the product"; the workflow "has never executed" | **Regressed — materially worse** | `.github/workflows/dotnet-ci.yml` now triggers on `workflow_dispatch:` **only**. Commit `79c5364` deleted `push: branches:[main]` and `pull_request: branches:[main]`, with the comment *"CI automatizado deshabilitado temporalmente para ahorrar minutos de cómputo"*. The three jobs are intact and real; nothing fires them. Iterations 3 and 4 both restated "never executed" without noticing it also can no longer execute on its own. **This item regressed after it was ticked ✅ in iteration 1.** |
| **TD-09 / Blocker 7** | "No financial-invariant tests. Every fix above would ship unverified." | **Now false** | Money-path tests have existed since iteration 2 and several are falsification-proven: `tests/Pine.Tests.Integration/Atomicity/*`, `Repositories/TransactionOwnedCollectionRoundTripTests.cs`, `tests/Pine.Tests.Unit/Domain/MoneyTests.cs`, `AccountTests.cs`, `TransactionSplitTests.cs`, and now the import-dedup suite. **Invalidated by iterations 2, 3 and 4** — each wrote money-path tests while TD-09 stayed 🔴. Now 🟡: what is genuinely missing is coverage for the P0s still open (TD-18, TD-19, TD-02). |
| **TD-43** | "ONNX binaries tracked in git without LFS" | **Now largely false** | `.gitattributes` line 1 tracks `*.onnx` through LFS since commit `bec0887`; all three blobs at HEAD are ~133-byte pointers. Residual: LFS does not rewrite history, so pre-`bec0887` blobs remain in the pack. Now 🟡. |
| **TD-30** | "Missing indexes on `Date` / `AccountId`; **no FK**" | **Severity wrong** | A FK does exist — Budget→Category with `DeleteBehavior.Restrict` (`PineDbContext.cs:173-176`) — and `LinkedTransactionId` is indexed (`:77`). `AccountId` gained a leading-column index via iteration 5's `(AccountId, ImportKey)` (`:98`). What remains: no index on `Date`, no transaction→account FK. |
| **TD-32** | "Category as free string, not FK" | **Severity wrong** | `Budget.CategoryId` is already a real FK. The free string survives only on `TransactionSplit.Category`. |
| **TD-46** | "Relative model paths (CWD-dependent)" | **Severity wrong** | Two of three remain relative (`MlNetCategorizer.cs:34`, `OnnxNlpFieldExtractor.cs:28`); `ReceiptDetector.cs:21` already anchors to `AppContext.BaseDirectory`. Invalidated by the vision work in `a1a440b`/`b6be65c`. |
| **TD-53** | Web head "builds a real SQLCipher context"; "its own comment claims in-memory" | **Wording stale, defect intact** | `Pine.Ui.Web/Program.cs:32` now passes `":memory:"`, so the comment contradiction is gone. The defect survives for a different reason: `PineDbContext.cs:41-52` unconditionally builds a **keyed SQLCipher** connection, and an in-memory *SQLCipher* database still needs the native library WASM does not link. Independently corroborated during this iteration's build: `Pine.Ui.Web`'s one warning names `e_sqlcipher.a` among native references that "won't be linked in". |
| **TD-21** | "Ambiguous dd/MM vs MM/dd parsing" | **Severity understated** | `OnnxNlpFieldExtractor.cs:490-509` added a "SOTA multi-culture cascade" whose comment claims it tries "ISO, then LatAm (dd/MM), then US (MM/dd)". It tries `InvariantCulture` **first**, and `InvariantCulture` *is* MM/dd — so the `es-EC` leg is unreachable for any ambiguous date, and there is still no ambiguity flag. The debt is now hidden behind code that reads as though it were handled. The CSV path is unaffected (`CsvImporter.cs` parses with `TryParseExact` against `profile.DateFormat`). |
| **TD-14** | "`AddConsole` in a WinExe" | **Understated by one** | Two heads, not one: `Pine.Ui.Windows/Program.cs:101` and `Pine.Ui.Macos/Program.cs:72`. |

#### Rows re-checked and confirmed exactly as written

TD-06, TD-11, TD-15, TD-16, TD-17, TD-20, TD-22, TD-25, TD-26, TD-27, TD-28, TD-29, TD-31, TD-33,
TD-34, TD-35, TD-36, TD-37, TD-38, TD-39, TD-40, TD-41, TD-42, TD-44, TD-45, TD-48, TD-49, TD-50,
TD-51, TD-52, TD-54, TD-55, TD-56, TD-57, TD-58, TD-59, TD-60 — all still accurate. Spot evidence:
TD-33's domain events are raised in exactly two places (`Account.cs:32`, `Transaction.cs:91`) and no
dispatcher exists anywhere; TD-54's workflow still enumerates eleven `.csproj` in a bash loop rather
than building `Pine.slnx`; TD-22's confidence score is still a literal `0.1 + 0.2 + 0.25 + 0.2 + 0.15`
tally (`OnnxNlpFieldExtractor.cs:745-764`).

#### No overstated closes

Every ✅ row was checked against the code and each one is real: TD-01 (no `SaveChangesAsync` outside
`EfCoreUnitOfWork.cs:46`), TD-04 (`Transaction.cs:137` no longer validates eagerly; `EnsureValid()` is
the first statement of `SqliteTransactionRepository.SaveAsync`, `:77`), TD-05 (`VaultHeader` carries
`HeaderVersion` plus every KDF parameter), TD-12 (four validators, all wired), TD-13
(`ReconcileOwnedChildren`, `:123-145`), TD-23 (`UnlockViewModel` catches five distinct vault
exceptions). **This is worth stating explicitly**: the failure mode being hunted was an overstated
close, and none was found.

#### §12.7 and §12.9 verified against disk

Every checkable claim in the iteration-3 and iteration-4 logs holds: `EnsureValid()` at the persistence
boundary ✓ · `ReplaceSplits`/`ReplaceLineItems` staging-then-swap (`Transaction.cs:150-172,189`) ✓ ·
`ReconcileOwnedChildren` on the tracked graph ✓ · `ValidationBehavior` registered **before**
`TransactionBehavior` (`PineApplicationServiceCollectionExtensions.cs:47-48`) ✓ · four
`IRequestValidator<T>` registered by reflection via `TryAddEnumerable` ✓ · `CurrencyCodeText.TryNormalise`
used by exactly three validators ✓ · no repository calls `SaveChangesAsync` ✓ · `Pine.Core.csproj` has
zero references ✓.

One drift, cosmetic but worth fixing when those files are next touched: the comments at
`PineApplicationServiceCollectionExtensions.cs:42` and `:55` name tests
`ValidationBehavior_IsRegistered_BeforeTransactionBehavior` and
`EveryRequestValidator_IsRegistered_ByAddPineApplication`; the actual tests are
`ValidationBehavior_ShouldBeRegistered_BeforeTransactionBehavior` (`ArchitectureTests.cs:125`) and
`EveryValidator_InTheApplicationAssembly_ShouldBeRegisteredAsScoped` (`:160`). A reader chasing the
cited names finds nothing.

The one correction this pass makes to §12.9 itself is recorded in §12.11: its verification table cited
`dotnet build Pine.slnx` as passing with two warnings, when that command in fact fails at restore and
compiles nothing.

#### What could not be verified, and why

- **Android / iOS behaviour (TD-52).** Workloads are absent (`NETSDK1147`); the registration diff is
  real and readable, but the runtime consequence is inferred, not observed. Not verifiable on this
  machine at all.
- **Runtime consequences of TD-15, TD-20, TD-34, TD-35, TD-45, TD-49.** These need a running GUI.
  Only the code shape was confirmed.
- **TD-43's historical pack cost.** Measuring it needs a full-history object walk; the LFS-pointer half
  is measured, the history half is not.
- **Whether the CI jobs would actually pass.** The workflow has never run, and now cannot run without a
  manual dispatch, so this is unknown by construction — which is precisely what makes the disarmed
  triggers worse than the register recorded.

#### The process finding

TD-07 regressed **after** being marked ✅, by a deliberate commit unrelated to any remediation
iteration, and three subsequent iterations read past it. TD-09 was false for three iterations because
the work that falsified it was logged in §12 but never reflected back into §8. Both are the same
failure as TD-12's: **§8 is only ever updated by the ticket that owns a row, and no ticket owns the
side effects it has on other rows.** A reconciliation pass of this kind belongs at the end of every
iteration, not once.

### 12.13 Next (after iteration 5) — superseded

> Carried out by iteration 6 (§12.14). Its three warnings all held: the correction-versus-deletion
> question was settled as **void**; the balance-recalculation tension with `Deposit`/`Withdraw` was
> resolved by a third mutator that cannot re-denominate **by construction**, so iteration 4's TOCTOU
> argument survived intact; and the editing traps (TD-59, TD-60) were avoided rather than solved, by
> deferring the edit path — which is why it stayed deferred rather than being quietly dropped
> (TD-68). What it did **not** foresee is recorded in §12.14: the opening balance was never stored,
> so "recompute the balance from its ledger" was arithmetically impossible before this ticket
> created somewhere to put it.


Phase 0 item **8 — TD-02**: update / void transaction commands plus balance recalculation. This is now
the **last original P0 data-integrity finding still open**, and the most consequential of them: TD-01,
TD-03, TD-04, TD-12 and TD-13 are closed, which means Pine can now record transactions atomically,
import them without losing any, and split them correctly — and still cannot **correct a single one of
them**. Any bad record, from any cause, is permanent, and a balance that has drifted cannot be
recomputed from its ledger.

Three things make it harder than the tickets before it, and they should be settled before code is
written:

1. **Correction versus deletion is a domain decision, not an implementation detail.** A finance vault
   generally voids rather than deletes, so history survives an audit. Nothing in the domain currently
   expresses "this transaction was reversed"; adding a delete would be quicker and would throw away the
   property that makes a ledger trustworthy.
2. **Balance recalculation and `Account.Deposit`/`Withdraw` are in tension.** The balance is currently
   maintained incrementally by those two methods, which are also the only mutators and the guarantee
   TD-12's validator ordering rests on (`Account_ShouldNot_ExposeAnyPublicMeansOfChangingItsCurrency`).
   A recalculation path that sets a balance from the sum of its transactions is a *third* mutator, and
   it must not become a re-denomination path — otherwise iteration 4's TOCTOU argument quietly breaks.
3. **Editing a transaction re-enters every trap iteration 3 closed.** `ReplaceSplits`/`ReplaceLineItems`
   mint fresh ids on each detached save (TD-60), and `ReconcileOwnedChildren` infers owned-child state
   from EF's guess (TD-59). Both are tolerable while transactions are only ever created; an update
   command makes them load-bearing.

The seams needed are already in place: `IRequestValidator<T>` for pre-conditions, `ITransactionalRequest`
for atomicity, and `AtomicityHost` for proving rollback from raw SQL. TD-09's remaining gap — no test
covers balance recalculation — should be closed *by* this ticket rather than after it.

### 12.14 Iteration 6 — Phase 0, item 8 (TD-02)

| Field | Value |
|---|---|
| Ticket | Phase 0 item **8** — correction mechanism and balance recalculation (TD-02) |
| Scope decision | **Void, not edit, and not delete.** Taken deliberately before code was written; see "The scope decision" below. |
| Tests | 170 → **240** (141 unit + 99 integration) |
| Schema | `20260828005910_AddTransactionVoidAndAccountOpeningBalance` |
| Status | 🟡 **Partial by design** — the correction and repair halves are closed; the *edit* half is explicitly deferred, see INCOMPLETE |

#### Verification actually executed

| Claim | How it was checked | Result |
|---|---|---|
| Baseline before any change | `dotnet test` on both suites | 90 + 80 = **170 passing** |
| Windows head builds | `dotnet build src/Pine.Ui.Windows` | ✅ 0 warnings, 0 errors |
| Final unit suite | `dotnet test tests/Pine.Tests.Unit` | ✅ **141 passing**, 0 failed |
| Final integration suite | `dotnet test tests/Pine.Tests.Integration` | ✅ **99 passing**, 0 failed |
| Migration is real, not asserted | A vault migrated down to `…_AddTransactionImportKey`, seeded over a raw SQLCipher connection, migrated up through the production `EfCoreVaultMigrator` + `VaultBackupService` path, then reopened | ✅ rows survive; void columns and `OpeningBalanceAmount` come back **raw-NULL** |
| Every "was / was not saved" claim | Fresh `PineDbContext` or raw SQL, never the change tracker that wrote it | ✅ |
| The four load-bearing guards | Falsification: each one removed, suite re-run | ✅ all four turn tests red — table below |
| Android / iOS heads | **Not checked.** Workloads absent (`NETSDK1147`), as in every prior iteration | ⚠️ unknown |
| The GUI | **Not checked.** The void confirmation, the struck-through row and the Settings report were never rendered; only the view models and bindings compile | ⚠️ unverified — see `61-Manual-Test-Plan` §8 |

#### The scope decision, taken before any code

The roadmap phrases item 8 as "Update / void transaction commands + balance recalculation". Those are
three features, not one, and the audit's own §12.13 warned that the first of them re-enters every trap
iteration 3 closed (`ReplaceSplits` minting fresh ids on each detached save, TD-60; owned-child state
inferred from EF's guess, TD-59). The decision taken was **void plus recalculation, and no edit**:

- A finance vault voids rather than edits, so the record of what was originally entered survives an
  audit. An edit destroys the very evidence a correction mechanism exists to preserve.
- Voiding does not touch owned collections at all, so TD-59 and TD-60 stay latent instead of becoming
  load-bearing. An update command would have had to close both first.
- The user is not blocked: a wrong transaction is voided with a reason and re-entered correctly. The
  cost is two rows instead of one, and that cost is visible rather than silent.

The deferred half is recorded as **TD-68**, not quietly dropped.

#### What was built

**Domain.** `Transaction` gained `VoidedAt`, `VoidReason`, a derived `IsVoided`, and
`Void(reason, timeProvider)`. Voiding is deliberately *not* idempotent: a second call throws, because
silently accepting one would let a double-void move a balance twice — the same class of defect as a
double-posted transaction. A rejected void leaves the aggregate completely unmutated.
`TransactionVoidedEvent` is raised for symmetry with `TransactionRegisteredEvent` (still undispatched —
TD-33).

`Account` gained `OpeningBalanceAmount`, a derived `OpeningBalance`, `AdoptOpeningBalance(...)` and
`RecalculateBalance(...)`.

**Persistence.** Four columns and an `(AccountId, VoidedAt)` index.
`ITransactionRepository.GetAmountsInForceAsync` projects the amounts of the non-voided rows;
`GetDistinctCurrenciesInForceAsync` answers the pre-condition the sum depends on.

**Application.** `VoidTransactionCommand` and `RecalculateAccountBalanceCommand`, both
`ITransactionalRequest`, both with an `IRequestValidator<T>` — the seams iteration 4 built, used as
intended and with no new mechanism invented.

**UI.** A per-row *Void* action with a confirmation that requires a reason; voided rows stay in the
list, dimmed and struck through, with a `VOIDED` badge carrying the reason in its tooltip. A **Data
Integrity** section in Settings checks any account's balance against its own ledger and reports the
result as a sentence. Neither path renders `ex.Message`, so neither adds to TD-24.

#### The trap in this ticket — the opening balance did not exist

The ticket reads as "sum the transactions and compare". It is not, and the reason is not in the audit:

> `Account.Create(name, type, initialBalance, …)` accepted an opening balance, folded it into
> `Balance`, and **kept no record of it**. The balance is `opening + Σ transactions`, and `opening` was
> unrecoverable. "Recompute the balance from its ledger" was arithmetically impossible.

Only three things could have been done about it, and two of them are wrong:

1. **Backfill it in the migration** — `Balance_Amount − SUM(TotalAmount_Amount)`. Wrong twice over.
   *Arithmetically*: EF's own scaffold emits `type: "TEXT"` for these columns, so SQLite coerces them
   to REAL to add them, and the figure baked in as the account's origin would be a floating-point
   approximation. *Semantically*, and far worse: it derives the opening balance **from the current
   balance**, which assumes the current balance is correct — the exact assumption recalculation exists
   to stop making. An account whose balance had already drifted would have that drift written in as its
   opening figure, and every later recalculation would confirm the wrong number forever.
2. **Refuse to recalculate accounts that predate the column.** Honest, and useless: those are precisely
   the accounts most likely to have drifted.
3. **What was done.** The column is nullable and unbackfilled. The first recalculation of such an
   account infers the opening figure in exact .NET `decimal`, records it through
   `AdoptOpeningBalance` (which refuses a second call, so the anchor can never be moved to make a
   disagreement go away), and returns `OpeningBalanceWasInferred: true`. The settings screen says so in
   words: *"This first check could not detect drift; run it again from now on and it will."*

That flag is the honest part of the whole feature. Reporting that run as a clean bill of health would
have been a lie, and it is the lie a less careful implementation tells by default. The residual
limitation is **TD-70**.

#### The second trap — a tripwire that was supposed to make this hard

`ArchitectureTests.Account_ShouldNot_ExposeAnyPublicMeansOfChangingItsCurrency` pins Account's entire
public method surface to a literal list, and its own doc-comment says that a new public member
invalidates `ValidationBehavior`'s TOCTOU-free currency read and forces the behaviour-ordering decision
to be revisited. This ticket needed **two** new methods. The tripwire fired exactly as designed.

It was widened, and the widening is justified in writing beside the list. The premise survives because
neither method can re-denominate an account **by construction rather than by check**:
`RecalculateBalance` builds the new `Balance` from the *existing* `CurrencyCode` instance and discards
the argument's; `AdoptOpeningBalance` stores a bare `decimal` and never persists a currency at all.
Deleting either guard would not open the window. Two unit tests prove it with `ReferenceEquals` rather
than value equality — necessarily, because `CurrencyCode` compares structurally, so an equal-but-
distinct instance sails through both guards and only reference identity distinguishes "discarded" from
"validated". So the ordering decision did **not** have to be revisited.

#### The third trap — a transfer is two transactions

Voiding one leg of a transfer and not the other leaves money that left one account and arrived nowhere:
a worse state than the one the user was correcting. The handler voids both legs and adjusts both
balances inside the one transaction, and `VoidTransactionResult` returns both id lists so the caller
knows to refresh more than the row that was clicked. The two links are established by two *different*
mechanisms in `TransferFundsCommandHandler` — the destination leg through the constructor, the source
leg through the reflection call that is TD-26 — so each direction can break independently, and each has
its own test.

#### How the fix is *proven*, not asserted

Four mutations, each applied to green code, suite re-run, then reverted:

| # | Mutation | Expected | Actual |
|---|---|---|---|
| A | Delete the foreign-currency rule from `RecalculateAccountBalanceCommandValidator` | Red | ✅ **3 red** — 2 unit, 1 integration |
| B | Stop adding the linked leg in `VoidTransactionCommandHandler` | Red | ✅ **2 red** — both transfer directions |
| C | `Account.RecalculateBalance`: `Balance = total` instead of rebuilding from the existing currency | Red | ✅ **1 red** — `Account_RecalculateBalance_CannotRedenominateTheAccount` |
| D | Drop `&& t.VoidedAt == null` from `GetAmountsInForceAsync` | Red | ✅ **3 red** — including the void/recalculation agreement test |

The exactness of the sum is pinned separately, by a ledger built so that a `double` gets it wrong: the
test asserts the premise first (`the double sum really is wrong`) so that reshuffling the amounts into
a floating-point-friendly order makes the test say so instead of passing quietly.

#### The defect the review found, and why it was the important one

The unit-test workstream reported that `RecalculateAccountBalanceCommand` had **no currency rule**. It
was right, and it is the only defect in this feature that could have made a balance *worse* while
telling the user it had been verified:

`GetAmountsInForceAsync` returns bare decimals on the reasoning that every transaction on an account
shares the account's currency. All four transaction validators enforce that — but
`VoidTransactionCommandValidator` had *already documented*, in this same iteration, that rows written
before those validators existed can violate it, and refuses to void them. The recalculation path was
written against the optimistic reading of the same fact. A legacy EUR row on a USD account would have
had its raw number folded into a USD total; `RecalculateBalance` would have accepted it, because it
validates the currency of the *total* — which the handler builds correctly — and knows nothing about the
provenance of the numbers inside it. The balance would then be overwritten with a figure that is not a
quantity of anything, and the settings screen would have called it a clean check.

Closed by a validator rule that names every offending currency and **refuses**. Refusing rather than
skipping is deliberate: dropping the odd row from the sum would silently delete a transaction's
contribution to a balance, which is the defect class TD-03 closed on the import path. Falsification A
above is this rule.

The same review raised two other items. One — that the void handler does not call
`Transaction.EnsureValid()` — was checked and is **wrong**: `EnsureValid()` is the first statement of
`SqliteTransactionRepository.SaveAsync`, which is where iteration 3 deliberately put the aggregate
boundary, so every write path is covered including this one. The other was an inaccurate XML doc on
`VoidTransactionResult.AdjustedAccountIds`, corrected.

#### Pre-existing defects found and fixed incidentally

- **A test that had been passing for the wrong reason for two iterations.** EF Core writes a `Guid`
  into a SQLite TEXT column **upper-cased**, and SQLite compares TEXT case-sensitively.
  `ImportKeyMigrationTests` seeded its legacy row with `accountId.ToString()` — lower case. The row
  still *materialises* correctly (`Guid.Parse` ignores case), so every client-side assertion in that
  file was sound; but the **server-side** predicate in `GetExistingImportKeysAsync` could never match
  it. Its `matches.Should().BeEmpty()` — which reads as "a NULL import key is not a wildcard" — was
  therefore vacuous, and would have passed even if a NULL key *did* behave as a wildcard. Fixed: the
  seed is upper-cased, and the assertion is now preceded by a check that the row is visible to a
  server-side predicate at all. The encoding itself is pinned by a new `Data/RawGuidEncodingTests`, so
  a future EF change makes one test fail loudly instead of several going quietly vacuous. **Verified
  empirically, not assumed** — the probe reads the raw cell out of a vault EF itself wrote.
- **A stale tripwire.** `EfCoreVaultMigratorTests.CurrentSchemaVersion` still named
  `…_AddTransactionLineItems` after `…_AddTransactionImportKey` shipped in iteration 5, so for one
  iteration those tests asserted that an upgrade reached a schema that was no longer the head. Nothing
  failed, because `Should().Contain(...)` is satisfied by an earlier id in the applied set. Retargeted,
  with the history recorded on the constant. This is the same failure mode as TD-07 and TD-09: a
  constant that has to be re-examined by a human, and was not.

#### New debt opened

| ID | Debt | Sev | Pri |
|---|---|---|:---:|
| TD-68 | No update/edit path for a transaction. Correction is void-then-re-enter, which is the right default for a ledger but costs two rows and loses the original's `ImportKey` linkage to the re-entered row | 🟡 | P2 |
| TD-69 | A void cannot be undone. A mis-voided transaction can only be re-entered as a new one, losing its id and its import key | 🟡 | P2 |
| TD-70 | Accounts created before this migration have no recorded opening balance, so their FIRST recalculation anchors instead of checking and cannot detect pre-existing drift. Reported honestly, but the drift is unrecoverable | 🟡 | P2 |
| TD-71 | The transactions list has no way to filter or hide voided rows. Correct at today's volumes; a vault with many corrections will read badly | 🔵 | P3 |

#### INCOMPLETE / NOT DONE

- **The edit half of TD-02 is not built**, by decision. See "The scope decision". TD-68.
- **A void cannot be undone.** TD-69.
- **No GUI verification.** Every claim above about the UI is a claim about code that compiles and view
  models that are unit-testable, not about pixels. The void confirmation, the struck-through row, the
  badge tooltip and the Settings report have never been rendered. `61-Manual-Test-Plan` §8 covers them.
- **Android / iOS still uncompiled.** Unchanged from every prior iteration; workloads are absent.
- **TD-59 / TD-60 remain latent.** Voiding does not touch owned collections, so neither became
  load-bearing. They will the day TD-68 is built — which is half the reason it was deferred.
- **Budget "spent" figures are unaffected because they are not implemented** (`BudgetsViewModel`
  hardcodes `SpentAmount = 0m`). When they are, they must exclude voided rows. Not new debt; noted so
  the next person does not have to rediscover it.

#### Reconciliation pass

§12.12's process finding was that **no ticket owns the side effects it has on other rows**, and that a
reconciliation pass belongs at the end of every iteration rather than once. Run again here:

- **TD-02 → ✅ for the correction and repair halves**, 🟡 overall; the edit half is TD-68.
- **TD-09** — the register's remaining gap named "balance recalculation (TD-02)" as uncovered. It is now
  covered, by a falsification-proven integration suite. Rounding/allocation (TD-18) and line-item
  reconciliation (TD-19) remain the only money-path invariants with no test. Row updated.
- **TD-07** — resolved as a **decision** rather than a defect: CI is opt-in by choice, the choice and
  its cost are now written into the workflow file itself, and the row is 🟡 Accepted at P1 rather than
  🔴 Open at P0. The *risk* is unchanged; what changed is that it is no longer recorded as an
  unnoticed regression that three iterations read past.
- **TD-26** — unchanged in severity, but this iteration made it load-bearing in a new place: voiding a
  transfer depends on the source leg's `LinkedTransactionId`, which is written by the reflection call.
  Silently dropping that line would now produce a half-voided transfer, not just a missing link. Both
  directions are covered by tests.
- **TD-31** — unchanged, and this iteration is the clearest illustration of it yet: the decision to
  project amounts and add them in .NET rather than push `SUM` into SQL exists solely because of it, and
  is pinned by a test built to fail if it is ever "optimised" away.
- **Rows re-checked and confirmed unchanged:** TD-06, TD-11, TD-14, TD-15, TD-16, TD-17, TD-18, TD-19,
  TD-20, TD-21, TD-22, TD-24, TD-25, TD-27 … TD-67. Nothing this iteration touched their premises.

### 12.15 Next (after iteration 6) — superseded

Phase 0 items **9, 10 and 11** are what remain before the phase closes:

- **9 — TD-11**, password entropy floor and idle auto-lock. The largest remaining security gap, and the
  one whose absence makes the encryption defeatable at the setup screen rather than at the vault.
- **10 — TD-09's remainder**, money-path coverage for TD-18 (money scale, rounding, `Allocate`) and
  TD-19 (line-item reconciliation). These two are the last money-path invariants with no test, and
  TD-18 in particular should be *designed* rather than back-filled: `Money` has no scale policy at all
  today, so "the sum of splits equals the total" is currently exact only because nothing rounds.
- **11 — TD-47 / TD-48**, remove the seeded fictional $1,000 account and the `CloudSync` setting that
  nothing reads.

Item 9 is the one to take first: it is the only remaining Phase 0 item whose cost grows once real
vaults exist, which is the ordering principle §10 states.

---

### 12.16 Iteration 7 — Phase 0, item 9 (TD-11)

| Field | Value |
|---|---|
| Ticket | Phase 0 item **9** — password entropy floor and idle auto-lock (TD-11) |
| Scope decision | **Entropy floor, not a composition rule. Idle lock, not re-authentication.** The third noun in TD-11's title — re-authentication for sensitive operations — is deliberately not in this iteration; see INCOMPLETE. |
| Tests | 240 → **240 (unchanged)**. **No test was added to the suite and the suite was not run**, by explicit instruction. Behaviour was instead verified by a 25-check harness executed outside the repository — its full output is transcribed below. That is weaker than a committed test, because nothing re-runs it; it is much stronger than the compiler alone, which is all this entry originally rested on. |
| Schema | None. Nothing in this ticket touches persistence. |
| Status | 🟡 **Code complete, behaviour verified out-of-suite** — the floor rejects and accepts as designed, the idle lock has been observed to fire on a fake clock, and the key buffer has been observed to zero. No test in `tests/` covers any of it, so nothing will catch a regression. |

#### Verification actually executed — and what was not

The behavioural rows below were produced by a throwaway console harness referencing `Pine.Core` and
`Pine.Application`, built outside the repository because no test file could be written. **It is not
committed and nothing re-runs it.** Its output is transcribed verbatim rather than summarised, so a
later reader can see exactly what was and was not asserted.

| Claim | How it was checked | Result |
|---|---|---|
| Baseline unit suite, before any change | `dotnet test tests/Pine.Tests.Unit` | ✅ **141 passing** |
| Baseline integration suite, before any change | `dotnet test tests/Pine.Tests.Integration` | 🔴 **98 passing, 1 failing** — `ImportDeduplicationTests.Import_OfAnExtendedStatement_SavesOnlyTheNewRowAndMovesTheBalanceOnlyByIt`, thrown at `AtomicityHost.cs:201`. **Pre-existing** on the commit this iteration started from (`19a1877`) and untouched by this work. Recorded because §12.14 claimed 99/99 and that claim is now stale. Not diagnosed — see TD-74. **[Correction, iteration 9: this row is wrong and could not have been produced as written. `f691ba8` — this very iteration's commit — added four members to `IVaultSession` and touched no file under `tests/`, so `Pine.Tests.Integration` failed to *compile* (8 × CS0535) from that commit onward: not 98 passing and 1 failing, but zero tests executed. At `19a1877`, where the project still builds, the suite runs 99/99 — confirmed over four full runs in a clean worktree, and four more at HEAD after the build was repaired. Baseline text left intact per this document's convention; see §12.20.]** |
| Windows head builds | `dotnet build src/Pine.Ui.Windows` | ✅ 0 warnings, 0 errors |
| macOS head builds | `dotnet build src/Pine.Ui.Macos` | ✅ 0 warnings, 0 errors |
| Web head builds | `dotnet build src/Pine.Ui.Web` | ✅ builds; one pre-existing native-WASM link warning, unrelated |
| Android / iOS heads build | **NOT CHECKED.** The workloads are not installed on this machine (`NETSDK1147`), so `Pine.slnx` cannot be built here at all. Their edits mirror the macOS/Web pattern line for line and were reviewed by reading. | ⚠️ unverified |
| A one-character password is refused — the audit's literal finding | Harness: `"a"` 4.7 bits, `"1"` 3.3, `"x"` 4.7, `" "` 0.0, `""` 0.0 | ✅ all five refused |
| Weak-but-plausible passwords are refused | Harness: `password` 2.0 bits, `P@ssw0rd` 4.0, `aaaaaaaaaaaa` 9.3, `qwerty123456` 10.5, `Password1!` 16.1, `Sunshine!2024` 39.4 | ✅ all six refused, each with the expected `PasswordWeakness` reasons |
| The floor is not a wall — strong passwords pass | Harness: `xK9#mQ2$vL` 65.7 bits, `7bTz!qW4nR#8` 70.3, `gravel-thicket-cinder-9271` 149.6 | ✅ all three accepted |
| Monotonicity: appending a character can never lower the score | Harness: four suffixes on a 65.7-bit base → 72.3, 72.3, 72.3, 78.8 | ✅ the shortest-path property holds |
| `Evaluate(null)` does not throw | Harness | ✅ returns a failing result |
| **The idle lock actually fires** | Harness on a `FakeTimeProvider`, no `Task.Delay` anywhere: unlocked → idle 9 min (still open, 1 min remaining) → `ReportActivity()` → idle 9 min again (still open, so activity really does reset the countdown) → idle 2 min more | ✅ **locked**, `VaultLockReason.IdleTimeout` reported, `Key` null |
| The key is zeroed, not merely dropped | Harness captured the session's internal `_key` array by reflection before the lock and re-read it after | ✅ all 32 bytes zero |
| The strength meter actually renders | **NOT CHECKED.** It compiles; it has never been displayed. `61-Manual-Test-Plan` §9.2, §9.14 and §9.15 cover it. | 🔴 **unverified** |

**The honest reading of that table:** the mechanism has been watched working, but only once, by
hand, from outside the repository. Nothing in `tests/` will notice if it stops working tomorrow —
which is the difference between *verified* and *covered*, and the reason TD-11 stays 🟡 rather than
becoming ✅. The two claims still resting on the compiler alone are the Android/iOS wiring and the
strength meter's rendering. Both, and everything else a harness cannot reach — an existing weak-password
vault still opening, an accepted password still unlocking after a restart, the lock taking the
decrypted data with it — are now written up as `61-Manual-Test-Plan` §9.

#### What was built

**The floor — `src/Pine.Core/Security/`, BCL-only, no package reference.**
`PasswordStrengthPolicy.Evaluate` decomposes a candidate into the cheapest sequence of tokens an
attacker could use to produce it — a shortest-path search over the string — pricing a hit in a small
ranked common-password list at about `log2(rank)`, a repeated run at its base block plus
`log2(repeats)`, an alphabetic, numeric or QWERTY-walk run at about `log2(alphabet) + log2(length)`,
and anything left unexplained at the full per-character pool. The total is the **minimum** over all
decompositions, so adding a pattern can never raise a score — the property a composition rule fails
to have, and the reason `Password1!` cannot buy its way over the floor by appending punctuation.
`PasswordStrengthResult` carries bits, a band and a list of `PasswordWeakness` enum values; it never
quotes the candidate back, and nothing in the file writes to a field, a cache or a log
(`15-Security` §9). Wording lives in the view model, so the domain never dictates UI copy.

**The floor is 60 bits, and its arithmetic is in the constant's own doc comment.** It is derived
from `KdfParameters.Argon2idV1` — m = 64 MiB, t = 3, p = 4, which every vault in existence uses —
not chosen for looking round. One guess moves at least `2 × 64 MiB × 3 ≈ 0.375 GiB` of memory
traffic, so a 1000 GB/s GPU is bandwidth-capped near 2,700 guesses/s, rounded pessimistically **up**
to 3,000; a thousand such GPUs give roughly `2^22` guesses/s; a century is about `2^31.5` seconds,
hence `2^53.5` guesses; add `2^6` for a hundred years of hardware improvement and `2^1` because an
attacker expects to succeed at half the keyspace, giving 60.5, taken as **60**. `MinimumLength = 10`
is *derived* from that (`ceil(60 / log2(94))`), not decreed, so a nine-character password cannot
reach the floor under any arrangement. The comment also records the leverage plainly: doubling
Argon2id's memory to 128 MiB would lower this floor by exactly **one bit**. The password is the cheap
lever — which is the whole thesis of the audit's original finding.

**The lock — `src/Pine.Application/Security/`.** `IVaultSession` gained a `Locked` event,
`LastActivityUtc`, `ReportActivity()` and `Lock(VaultLockReason)`; the old parameterless `Lock()` was
kept as an overload meaning `UserRequested`, which is why `UnlockViewModel`'s six defensive `Lock()`
call sites compile unchanged. `VaultAutoLockService` polls every 15 seconds rather than re-arming a
timer on every keystroke, takes both its schedule and its clock from an injected `TimeProvider` — the
same abstraction `VoidTransactionCommandHandler` already uses, so nothing new was invented — arms
itself on **resolution** rather than requiring someone to remember to start it, and also implements
`IHostedService` so the Windows head's existing hosted-service loop starts it with no new code. One
monitor guards every composite read of `(key, path, unlocked)`, so no reader can observe a half-locked
session; `ReportActivity` is deliberately outside that gate — an `Interlocked.Exchange` on a tick
count — so the input path never contends for a lock.

**The wiring — all five composition roots.** `AddPineVaultAutoLock()` on each; `TimeProvider.System`
added to Web, Android and iOS, which had never registered one; and an explicit
`GetRequiredService<IVaultAutoLockService>()` at startup on the four heads that do not run a
hosted-service loop. `SetupViewModel` now refuses to create a vault unless `MeetsFloor` holds,
re-evaluated inside the command rather than trusted from the live-feedback state, and gained a
confirm-password field: a password mistyped at creation is unrecoverable, because this product has no
recovery phrase. `MainViewModel` handles `Locked`, marshalling through `Dispatcher.UIThread.Post`
because the event is raised on the timer thread, drops the shell's inner page so nothing holding
decrypted data survives the key, and tells the user *why* they were returned to the vault picker —
otherwise an idle lock reads as a crash. `MainWindow` reports activity from tunnelled pointer and key
handlers, so an event that a control marks handled still counts as activity.

#### Defects found while verifying, and fixed

**The derived key was left in a second, unzeroed managed array — found by the harness, and it
contradicted a claim made about this code.** `VaultSession.Unlock` *copies* the key into its own
buffer, and `Lock()` zeroes that copy. Both `UnlockViewModel` and `SetupViewModel` derived the key
into a local `byte[]`, handed it to `Unlock`, and then abandoned it — so a full second copy of the
vault key stayed live on the managed heap for the whole process, untouched by any lock, idle timeout
or vault switch. TD-11's own text is *"the 32-byte key sits in a managed `byte[]` … for the entire
process lifetime"*, so shipping the idle lock while leaving that copy behind would have closed the
finding on paper only. Both call sites now `Array.Clear` the local immediately after handing it over.
The harness check that caught this had been *asserted* to pass during implementation; it could never
have passed, because `Unlock` copies. It is recorded here because that is exactly the failure mode
§12.12 exists to catch — a verification claim nobody could reproduce.

#### A defect found while wiring, and fixed

**`UnlockViewModel` was registered only on the Windows head** (`Pine.Ui.Windows/Program.cs:83`). On
macOS, Web, Android and iOS, `GetRequiredService<UnlockViewModel>()` throws the moment the vault
picker navigates to it: the unlock screen could not be opened at all. It is now registered on the four
missing heads. **Pre-existing and unrelated to TD-11** — but it could not be left, because shipping a
lock feature onto a head with no reachable unlock screen would have converted an idle timeout into a
dead end.

#### Defects found while wiring, and deliberately not fixed

- **`ShellViewModel` is `Transient` on Windows and `Singleton` on the other four heads.** Changing a
  lifetime is not a security fix and would ripple further than this ticket. Its one dangerous
  consequence — a singleton shell retaining child view models full of decrypted data after a lock —
  is contained by nulling the shell's inner page in the lock handler. The divergence itself stands:
  new row **TD-72**.
- **`App.OnFrameworkInitializationCompleted` has no `ISingleViewApplicationLifetime` branch at all.**
  Web, Android and iOS therefore construct no root view and show nothing. That makes the missing
  `ReportActivity()` hook on those three heads moot — there is no `MainWindow` there to hook, and a
  head that never renders has no actively-working user to lock out. This is TD-08's "three platform
  heads are broken" seen from a new angle, and it is sharper evidence than the register currently
  carries.

#### INCOMPLETE — stated rather than buried

1. **No test in `tests/` covers any of this.** By instruction. The behaviour *was* verified — see the
   table above — but by a harness that is not committed and that nothing re-runs, so there is no
   regression net. The `TimeProvider` seam makes the idle-lock test a deterministic, microsecond
   assertion with no `Task.Delay`; the harness is, in effect, that test written in the wrong place.
   Porting it into `tests/Pine.Tests.Unit` is the first item of §12.17 and the reason the status
   stays 🟡.
2. **No re-authentication for sensitive operations** — the third noun in TD-11's own title. It is
   worth little before the envelope key (TD-06) exists.
3. **No lock-on-suspend.** `VaultLockReason.ApplicationSuspended` and `CheckNow()` exist for a UI
   that can observe platform lifecycle; nothing observes it yet.
4. **The idle timeout is not persisted.** `SetIdleTimeout` applies in-process only; nothing writes it
   through `SaveSettingCommand`, and Settings exposes no control for it. The shipped default of ten
   minutes is therefore the only value any user will ever have.
5. **The key is still a managed `byte[]`.** Both copies are now zeroed — the session's on `Lock()`,
   the view models' derived local immediately after `Unlock` — but a compacting GC may already have
   moved either one and the OS may have paged it. This narrows the window; it does not close it, and
   no claim to the contrary is made anywhere. Closing it needs an unmanaged or pinned buffer, which
   belongs to TD-06.
6. **No word list, so passphrases are over-estimated.** `correct horse battery staple` scores far
   above its true ~44 bits. Every limitation of the estimator errs *generous*, which is exactly why
   the floor carries a `2^6` margin rather than sitting at break-even: new row **TD-73**.

#### Rows the register gains

| ID | Debt | Sev | Impact | Fix | Priority | Status |
|---|---|---|---|---|---|---|
| TD-72 | `ShellViewModel` lifetime diverges: `Transient` on Windows, `Singleton` on the other four heads | 🟡 | One view model means two different things depending on platform; a singleton shell outlives the vault it was opened for | Pick one lifetime, in the shared registration TD-29 already calls for | P2 | 🔴 Open |
| TD-73 | `PasswordStrengthPolicy` has no natural-language word list, so multi-word passphrases are over-estimated | 🟡 | The estimator accepts a passphrase weaker than the bits it reports; the 60-bit floor's `2^6` margin is what currently covers this | Embed a word list in `Pine.Core` — needs the BCL-only rule relaxed to permit an embedded resource | P2 | 🔴 Open |
| TD-74 | One integration test is red at `19a1877` and nobody has diagnosed it | 🟠 | §12.14 recorded 99/99; the suite is 98/99. A regression net whose colour nobody reads is not a net | Diagnose `ImportDeduplicationTests.Import_OfAnExtendedStatement_…` — it is TD-03's own coverage | P1 | 🔴 Open |

### 12.17 Next (after iteration 7) — superseded

Phase 0 items **10** and **11** remain, plus a debt this iteration created:

- **First, before either: port the verification harness into `tests/Pine.Tests.Unit`.** It already
  exists and already passes — 25 checks across the estimator, the idle lock and the zeroing — but it
  lives outside the repository, so nothing re-runs it and the next change to `VaultSession` will
  break it silently. The seams were built for exactly this: an injected `TimeProvider`, a pure
  `Evaluate`, an observable `Locked`. This is a transcription job, not a design job.
- **10 — TD-09's remainder**: money-path coverage for TD-18 (money scale, rounding, `Allocate`) and
  TD-19 (line-item reconciliation). Still work to *design* rather than tests to back-fill — `Money`
  has no scale policy at all, so "the sum of the splits equals the total" is exact today only because
  nothing rounds.
- **11 — TD-47 / TD-48**: remove the seeded fictional $1,000 account — the
  `CreateAccountCommand("My First Bank", "USD", AccountType.Bank, 1000m)` call in `SetupViewModel` —
  and the `CloudSync` setting that nothing reads.
- **TD-74**, the red import test, before it is three iterations old and something starts depending on
  its silence.

### 12.18 Iteration 8 — Phase 0, item 10, first half (TD-18, TD-61)

| Field | Value |
|---|---|
| Date | 2026-08-28 |
| Scope | Phase 0 item **10**, the TD-18 half: a scale and rounding policy for `Money`, exact allocation, ISO 4217 membership for `CurrencyCode` — and TD-61, which could not be left open once membership existed |
| Outcome | **TD-18 ✅ closed · TD-61 ✅ closed · TD-09 narrowed to TD-19 only.** 5 new debts opened (TD-75 … TD-79) |
| Tests | 185 + 99 = 284 → **274 + 110 = 384** |
| Schema | **No migration.** Nothing about the storage shape changed; see below |
| State | Working tree only — not committed, not pushed |

#### Verification actually executed

| Check | Result |
|---|---|
| Baseline before any change | ✅ 185 unit, 99 integration, both green |
| `dotnet build` × 8 heads, Release (`Core`, `Application`, `Infrastructure`, `Ui.Avalonia`, `Ui.Windows`, `Ui.Macos`, `Ui.Web`, `Cli`) | ✅ 0 errors; 0 warnings except `Ui.Web`'s pre-existing SDK `.targets` warning, unchanged since iteration 2 |
| `dotnet test tests/Pine.Tests.Unit` | ✅ **274 passed, 0 failed, 0 skipped** |
| `dotnet test tests/Pine.Tests.Integration` | ✅ **110 passed, 0 failed, 0 skipped** |
| Legacy vault tolerance | ✅ proven, not assumed — rows planted by raw SQL over a keyed SQLCipher connection |
| Falsification | ✅ **six mutations, six killed**, table below; green restored after each |
| Android / iOS heads | ❌ **not compiled** — workloads absent (`NETSDK1147`), unchanged since iteration 1 |
| The GUI | ❌ **not rendered.** The currency-picker change and the `{0:N2}` finding are claims about code, not pixels |

#### The design, and where the policy lives

The policy is in **`Money`'s constructor**, and the scale it uses comes from **`CurrencyCode.MinorUnits`**,
which reads an ISO 4217 table embedded in `Pine.Core`. Putting it at the boundaries instead would mean
there is a boundary to forget; putting it in the value object means an out-of-scale amount cannot exist
inside the domain and no aggregate has to defend against one.

The table is embedded rather than read from `RegionInfo` / `NumberFormatInfo.CurrencyDecimalDigits`
deliberately: those are ICU culture data, revised between OS releases and different across platforms, so a
vault's arithmetic would otherwise depend on the machine that opened it.

**What is normalised versus what is rejected is asymmetric, and the asymmetry is the design.** For a
currency with a minor unit, digits below it are sub-unit noise — division residue, never a sum anybody
meant — and are rounded away half-to-even. For a currency with **no** minor unit there is no sub-unit for
noise to occupy, so a fractional yen is not residue but a quantity the currency cannot express, which means
the caller computed it somewhere it should not have: `new Money(10.5m, JPY)` **throws**. Silently making it
10 JPY would hide the arithmetic that produced the half.

That frontier is drawn at the **constructor** — the caller-authored boundary. Results this type computes
itself (`Multiply`, `Allocate`) are not caller-authored, so they round through `Money.Round` and never
throw. A caller that genuinely wants the nearest expressible yen says so by calling `Money.Round` or
`Money.FromRounded`.

**Half-to-even, not half-away-from-zero**: away-from-zero rounds every midpoint in the same direction and
accumulates a drift proportional to the number of operations, which in a ledger is the wrong error to have.

`Allocate` is **largest remainder** over whole minor units: each part takes the truncated units its ratio
earns, and the leftover units — always fewer than the number of parts — go one each to the largest
remainders, ties by position. `new Money(10.00m, USD).Allocate(3)` is `[3.34, 3.33, 3.33]`. Divide-and-round
is the defect this exists to prevent: three shares of 3.3333… each round to 3.33 and sum to 9.99, which
breaks `Transaction.ValidateSplits` for a number that is not a quantity of money. Negative amounts are
allocated by magnitude and re-signed, so the two directions mirror exactly; a zero ratio yields a zero part
and can never receive a leftover unit.

**`decimal` is still the type.** Scaled integers are TD-31 and were not touched. `ToMinorUnits()` is a
projection used for allocation arithmetic, not a change of representation.

#### Backward compatibility, and why there is no migration

A vault in the field may hold a currency outside the table (the old rule was `^[A-Z]{3}$` and nothing more)
or an amount outside its currency's scale. **Neither can be migrated**: there is no correct value to rewrite
an unknown currency to, and rounding stored history is destroying evidence. So the read path is deliberately
tolerant and the write path is deliberately strict.

The seam is that **EF Core binds the private parameterless constructors** of `Money` and `CurrencyCode` and
writes the backing fields directly, so materialisation executes neither rule. That is a fact about EF's
constructor selection rather than about Pine, which is exactly why it is now a test rather than an
assumption — an EF upgrade that changed the selection rule would turn "opens fine" into "throws on open"
with no other signal. `Data/LegacyMoneyToleranceTests` plants `Balance_Currency='ZZZ'`, a three-decimal USD
amount and a fractional JPY balance by raw SQL and asserts each one materialises, each test first asserting
that the validating path *would* have refused the same value so that it cannot pass vacuously.

**The one read path that does run code was a real defect.** `Budget.Limit`'s currency is the only money
column mapped with an explicit `HasConversion` (the TD-33 divergence), and its converter called the
**validating** constructor. Left alone, this ticket would have made a vault holding a legacy budget currency
unopenable. It now calls `CurrencyCode.FromStoredValue`. Falsification M5 is that line, and no other test in
the suite catches it.

No schema changed, so **no migration was generated**.

#### Call sites whose behaviour changed

Every `new Money(...)` in the solution now rounds. The ones where that is a real change of behaviour:

- **`CsvImporter:137`** — the only site whose input is genuinely arbitrary. A statement row of `12.345` used
  to be stored verbatim and is now stored as `12.34`. This is the intended fix and also the largest blast
  radius in the ticket.
- **`AddTransactionCommand:52,77,78`** and **`RecordTransactionCommand:36,42`** — a plugin or a future caller
  passing an unrounded amount now has it rounded rather than persisted at full precision.
- **`RecalculateAccountBalanceCommand:137,143`** — the ledger sum is added in exact .NET `decimal` (TD-31,
  deliberately not pushed into SQL) and *then* wrapped in a `Money`, so a legacy vault of out-of-scale rows
  now yields a **rounded** recalculated balance where it previously yielded an exact one. The sum itself is
  unchanged; only the figure written back is normalised.
- **`Account.OpeningBalance`** — now built with `Money.FromRounded`, because `OpeningBalanceAmount` is a bare
  `decimal?` column that does not pass through `Money` on the way out. Without this, a legacy JPY account
  with a fractional opening balance would have thrown *every time it was displayed*. A read-path projection
  must not be able to break a screen. Recorded as TD-77.
- **`CurrencyCode`'s membership rule** changes `CreateAccountCommand:28` and `CreateBudgetCommand:31` from
  "accepts anything three letters long" to "throws `ArgumentException` from inside the handler". Neither has
  a validator (TD-62 / TD-63), so this is a wrong-layer throw rendered verbatim by the view models — a
  widening of TD-24 that this ticket created and did not fix. Recorded as **TD-78**.
- **`CreateAccountViewModel.Currencies` offered `"BTC"`**, which is not an ISO 4217 code. After this change
  that picker entry could not have succeeded: a shipped UI affordance that always fails. Removed. Pine has no
  crypto-asset model — no divisibility, no rate source — so offering one was a promise the domain could not
  keep. TD-76.
- **Four hand-rolled negations** (`AddTransactionCommand:92`, `VoidTransactionCommand:168`, and two in
  `TransferFundsCommand`) now use unary `-`. `TransferFundsCommandHandler` additionally derives all four of
  its figures from one normalised `Money` instead of re-constructing from `request.Amount` four times, so
  they cannot disagree about scale.
- **`TransactionSplit.CopyOf` / `TransactionLineItem.CopyOf`** rebuilt the nested `CurrencyCode` with the
  validating constructor. On a legacy aggregate that would have thrown while merely *copying* a value it had
  just read. They now use `FromStoredValue`.

#### Falsification

Six mutations, each applied to green code, both suites re-run, then reverted and re-run green.

| # | Mutation | Result |
|---|---|---|
| M1 | `MidpointRounding.ToEven` → `AwayFromZero`, in the constructor and in `Round` | ✅ **10 red** — 8 unit (incl. `Money_UsesBankersRounding_NotAwayFromZero`, `Money_RespectsAThreeDigitCurrency`, `Round_IsTheExplicitWay…`) + 2 integration (`AnAccountBalance_IsPersistedAlreadyNormalised`, `ALegacyRowAndANewRow_Coexist…`) |
| M2 | Delete `Allocate`'s largest-remainder distribution loop | ✅ **11 red** — 10 unit including both property sweeps, + `ATransactionTotalAndItsSplits_ArePersistedAlreadyNormalised` |
| M3 | Disable the ISO membership check in both the constructor and `TryCreate` | ✅ **8 red** — 4 unit (`CurrencyCode_Rejects_AWellShapedStringThatIsNotACurrency`, `TryCreate_AgreesWithTheConstructor…`) + 4 integration (all three `CurrencyMembershipValidationTests` rows, plus the legacy-tolerance test's own anti-vacuity guard) |
| M4 | Remove the constructor's normalisation (`Amount = amount`) | ✅ **12 red** — 10 unit + 2 integration |
| M5 | `Budget.Limit`'s EF converter back to the validating constructor | ✅ **1 red** — `ABudgetDenominatedInAnUnknownCurrency_StillMaterialises`. The only test that catches it, and the reason it was written |
| M6 | `CurrencyCodeText.TryNormalise` back to its own copy of the shape rule | ✅ **3 red** — `AddTransaction_WithAWellShapedNonCurrency_IsRefusedByTheValidator` for all three codes. **Zero unit tests red**, which is the honest shape of TD-61: it is a wiring defect, visible only through the pipeline |

**No mutant survived.** M5 and M6 were added precisely because M1–M4 left two seams untested; without them
both would have been surviving mutants rather than killed ones.

#### INCOMPLETE / NOT DONE

1. **TD-19 (line-item reconciliation) is untouched**, by instruction. Phase 0 item 10 is therefore half
   closed; TD-09 is narrowed, not closed.
2. **`{0:N2}` in four `.axaml` files is still wrong for JPY and KWD.** Diagnosed, not fixed — there are no
   Avalonia converters in the project, so fixing it means introducing that mechanism, which is a UI change
   with no test harness behind it, inside a ticket about the domain. **TD-75.**
3. **`CreateAccountCommand` / `CreateBudgetCommand` have no validator**, so this ticket made two paths throw
   a domain exception where they previously accepted bad input silently. **TD-78.** The seam to fix it exists.
4. **Android and iOS still cannot be compiled locally** (`NETSDK1147`). Unchanged since iteration 1.
5. **No GUI was rendered.** The picker change and the formatting finding are unverified visually.
6. **The ISO table is a point-in-time transcription** (list one, 2024-06-25) with no update mechanism and no
   test that it matches the standard — only that it is internally consistent and that every member is
   constructible. A code the standard adds later will be rejected until someone edits the file.
7. **`Money` supports scales 0–4 only.** Anything else throws from `PowerOfTen` / `MinorUnit`. That covers the
   whole ISO table today and is a deliberate closed set, not an oversight.
8. **`Allocate` uses `long` minor units and `Int128` intermediates.** No test explores the overflow boundary;
   the amounts involved are far outside any plausible vault, but the claim is untested rather than proven.
9. **TD-74 was not investigated** by iteration 8. Both suites are green on this machine at this commit,
   which does not by itself resolve §12.17's claim that a test was red at `19a1877`. **[Iteration 9
   investigated it and found the claim false; see §12.20. The green suites iteration 8 observed were green
   only because iteration 9's fake-repair had already landed in the working tree.]**
10. **Nothing was committed or pushed.**

#### Reconciliation pass

- **TD-18 → ✅ closed.** TD-61 → ✅ closed; it could not have been left open, because membership validation
  would have made the validator's private copy of the rule strictly weaker than the rule it stood in for.
- **TD-09** — rounding and allocation are now covered, falsification-proven. Only TD-19 remains.
- **TD-33** — unchanged in severity but newly load-bearing: `Budget.Limit`'s divergent `HasConversion` was
  the single place where the read path executed domain validation, and it is now the single place where the
  read path executes `FromStoredValue`. The divergence is covered by a test that goes red if it is
  "unified" back onto the validating constructor.
- **TD-31** — unchanged, and reinforced: `RecalculateAccountBalanceCommand` still sums in .NET rather than
  SQL, and the new round-trip test has to read decimal columns as raw TEXT, which is only necessary because
  of it. The round-trip test also documents that EF's `decimal` encoding drops trailing zeroes, so `10.00`
  lands on disk as `10.0` — the absence of the third decimal, not the presence of the second, is what that
  assertion actually discriminates.
- **TD-24** — measurably worse on two paths; TD-78 records it rather than burying it.
- **TD-12** — its validators are strictly stronger now: `"ZZZ"` was previously accepted by them.
- **Rows re-checked and confirmed unchanged:** TD-02, TD-03, TD-06, TD-11, TD-13 … TD-17, TD-19, TD-20 …
  TD-30, TD-32, TD-34 … TD-60, TD-62 … TD-74.

### 12.20 Iteration 9 — Phase 0 item 10 closed (TD-19, TD-09), TD-74 reclassified

| Field | Value |
|---|---|
| Date | 2026-08-28 |
| Scope | Phase 0 item **10** — TD-19 (line items unreconciled) and TD-09's remainder. Plus §12.17's prerequisite (port the TD-11 harness) and TD-74. |
| Outcome | **TD-19 ✅ closed · TD-09 ✅ closed · TD-74 ✅ closed and reclassified · TD-11 covered.** Phase 0 item 10 is done; only item 11 remains. |
| Tests | 240 → **429** (310 unit + 119 integration), 0 failed, 0 skipped. |
| Schema | One migration, `20260828201103_AddTransactionLineItemKind` — a nullable `Kind` column on `TransactionLineItems`. Nullable is load-bearing, not laziness; see below. |
| State | Working tree only — **not committed, not pushed**. |

#### TD-74 was misdiagnosed, and the real defect was worse

§12.16 recorded "98 passing, 1 failing" at `19a1877`. That could not have happened. `f691ba8` — iteration
7's own commit — added `Locked`, `LastActivityUtc`, `ReportActivity()` and `Lock(VaultLockReason)` to
`IVaultSession` and touched **no file under `tests/`**. The unit suite survived because its doubles are
NSubstitute proxies that regenerate; the integration suite has two hand-written fakes — `TempVaultSession`
in `AtomicityHost.cs` and `FakeVaultSession` in `ExportVaultBackupCommandHandlerTests.cs` — which stopped
satisfying the interface. **`Pine.Tests.Integration` had not compiled since `f691ba8`: 8 × CS0535, zero
tests executed.** Not a red test — no test at all.

A clean worktree at `19a1877`, where the project still builds, runs 99/99 — confirmed over four full runs,
including the named test in isolation, and four more at HEAD after the build was repaired.
`AtomicityHost.cs:201` at that commit is `ExecuteScalar()` on a raw SQLCipher connection: the shape of a
transient file lock, not of a failed assertion, which would have thrown at the test's own line. **The flake
is not reproducible and is not resolved; what is resolved is the far larger defect hiding behind the
report.** Both fakes now mirror the real `VaultSession`'s semantics, so a double cannot pass where
production would fail. Falsification: stash the two files → 8 × CS0535; restore → 99/99.

The lesson generalises past this row: **a suite that does not build reports no colour, and three iterations
read the absence of red as green.** This is the same failure mode §12.12 exists to catch.

#### TD-11's harness is now in the suite

§12.17's first item. All 25 checks ported to `tests/Pine.Tests.Unit/Security/` (44 tests). Every value
transcribed in §12.16 reproduced **exactly** — the bit scores, the `PasswordWeakness` sets, the idle-lock
sequence on a `FakeTimeProvider` with no `Task.Delay`, the 32 zeroed bytes. Nothing needed relaxing, so
TD-11's behaviour is now covered rather than merely witnessed. The zeroing needed no reflection: the getter
on `IVaultSession.Key` returns the live buffer, so capturing it before `Lock()` observes the wipe in place.
The two rows §12.16 marked NOT CHECKED — the Android/iOS heads and the strength meter's rendering — are
still unchecked, and still belong to `61-Manual-Test-Plan` §9.

#### TD-19 — the difference is represented, not tolerated

A receipt's product lines almost never sum to what was paid: tax, tip, discounts, container deposits and
cash rounding sit in between. Enforcing an exact sum naively would have made the scanner refuse most real
receipts; tolerating a difference would not be an invariant. So every component of the total is a line, and
`LineItemKind` says which kind: `Product`, `Tax`, `Tip`, `Discount`, `Deposit`, `Rounding`, `Unattributed`.
The invariant stays **exact** and the receipt stays storable.

**There is no tolerance constant, and the derivation is that none is available.** Every `Money` is
normalised to a whole number of minor units by its own constructor (TD-18, §12.18), and the sum is taken
over stored line totals rather than recomputed from quantity × unit price. A sum of exact multiples of the
minor unit is an exact multiple of the minor unit, so the largest representable residue is **zero**. An
epsilon here would not absorb arithmetic noise — there is none — it would absorb tax, one to four orders of
magnitude larger, and an amount the user is entitled to see named. `Rounding` and `Unattributed` exist so
nobody has to lie about what a charge was.

**Sign is not part of the kind.** A `Discount` is a line whose amount runs opposite to the total's
direction; the sum invariant is what makes that work. Encoding "subtract this one" in the kind would give
two places that decide a line's contribution and let them disagree.

**`Kind` is nullable, and null is not a member of the enum.** Null means "written before this model
existed", and such a set is **not** validated. That data is incomplete, not contradictory, and the
alternatives were to invent the missing adjustment line — fabricating evidence — or to refuse it, which
would have made every pre-existing scanned receipt impossible to void or correct, silently undoing TD-02's
correction path with a new invariant. The hole is as narrow as it can be made: `AddLineItem` takes a
non-nullable kind, so the only way back into that state through the domain is to pass a null to
`ReplaceLineItems` deliberately, and the only caller that does is the repository merge path preserving what
it has just read.

**The UI half.** `ScanReceiptViewModel` recomputes the difference as the grid is edited — through `Money`
once an account is selected, so the figure the user sees is normalised to the same scale the validator
compares against, and a receipt cannot look reconciled and then be refused.
`RecordDifferenceAsAdjustment` inserts the outstanding amount as one adjustment line of a chosen kind:
**the user chooses what to call it, never how much it is.** `Product` is absent from that picker — the
difference between what was bought and what was paid is by definition not a product. Adjustment rows are
exempt from the quantity × unit price auto-calculation, which would otherwise drive a tax line to zero the
instant it was created.

#### A pre-existing defect the invariant exposed

`ScanReceiptViewModel.SaveTransactionAsync` sent line items **positive against a negative total**. That set
could never have reconciled under any rule — which is precisely why nothing had ever checked it. Line items
are now negated with the total, so they sum to it.

#### Falsification — 5 mutations, 5 killed

| # | Mutation | Red |
|---|---|---|
| M-A | Neuter the line-item sum invariant | 3 unit |
| M-B | Delete the legacy null-kind exemption | 2 unit + 2 integration |
| M-C | Delete the UI reconciliation guard | 1 unit (`ScanReceiptReconciliationTests`) |
| M-D | Send line items unnegated — the pre-existing sign defect | 1 unit |
| M8 | Drop `li.Kind` from the repository's **detached merge** | 2 integration — **survived until this iteration**; see below |

**M8 is the hole this iteration found in its own work.** `SaveAsync` branches on
`ReferenceEquals(existing, transaction)`. Every existing test loads through the repository inside the same
context and gets the **tracked** instance, so none of them ever reached the branch that rebuilds a
`LineItemDefinition` — the only place in the solution where a kind can be silently dropped, because it is
the only place a line item is reconstructed from one that already exists. Dropping it flattens every line
to `Product`, and worse, it flattens a legacy null — which asserts the transaction reconciles and turns a
merge into a throw. A correction path that worked would start failing on exactly the data the legacy seam
exists to protect. Two tests now cover it, both reading on one context and saving on another, with an
explicit `ReferenceEquals(...).Should().BeFalse()` so the test cannot silently stop exercising the branch
it names.

`CS0162` is an error in this repository, so a mutation that leaves unreachable code does not compile. M-B
and M-C delete their blocks rather than short-circuiting them. Green was restored and re-verified after
every mutation.

#### Verification actually executed

| Check | Result |
|---|---|
| `dotnet build -c Release` × `Core`, `Application`, `Infrastructure`, `Ui.Avalonia`, `Ui.Windows`, `Ui.Macos`, `Ui.Web`, `Cli` | ✅ 0 errors, 0 warnings on all eight |
| `dotnet test tests/Pine.Tests.Unit` | ✅ **310 passed**, 0 failed, 0 skipped |
| `dotnet test tests/Pine.Tests.Integration` | ✅ **119 passed**, 0 failed, 0 skipped |
| Android / iOS heads | ❌ **not compiled** — workloads absent (`NETSDK1147`), unchanged since iteration 1 |
| Any GUI rendered | ❌ **not observed.** The reconciliation panel and the adjustment picker compile and are covered by view-model tests; they have never been displayed. `61-Manual-Test-Plan` needs a section of its own for them; §10 was taken by TD-16 in iteration 14. |

#### INCOMPLETE — stated rather than buried

1. **No GUI was rendered.** The reconciliation affordance is the entire argument for why this invariant is
   liveable, and that argument currently rests on view-model tests and on reading.
2. **The uncommitted work of this iteration was destroyed mid-verification and reconstructed.**
   `git checkout -- <file>` was used to revert two falsification mutations. It restores from the index, and
   this iteration was never staged, so it discarded the working-tree versions of `Transaction.cs` and
   `ScanReceiptViewModel.cs` wholesale. Both were rebuilt by replaying the edit scripts recorded in the
   implementing agent's transcript, then reconciled by hand where an abandoned intermediate design
   (`MarkAsPredatingKinds`) had been captured instead of the final one. **The recovered files are a
   reconstruction, not the original bytes.** What backs them: all eight heads build clean, both suites
   return the exact pre-loss counts (310 / 119), and all five mutations above were run against the
   reconstruction rather than against the lost original. What does not back them: nobody has diffed the
   reconstruction against what was lost, because it no longer exists. **Falsification must use file copies,
   never `git checkout`, on an unstaged tree.**
3. **The flake behind TD-74's original report is unresolved** — not reproduced in eight full runs, and no
   product defect found behind it. If it is real it is in test infrastructure: a raw reader opened against a
   file EF still holds pooled connections on.
4. **TD-19's extractor half is partial.** The OCR extractor already computed the line-items-to-total ratio
   and spent it on a confidence score; it now feeds adjustment extraction, but no receipt corpus was run
   through it. Its accuracy is unmeasured.
5. **Nothing committed or pushed.**

#### Reconciliation pass

- **TD-19 → ✅ closed.** **TD-09 → ✅ closed** — rounding, allocation and line-item reconciliation were its
  last two gaps, and both are now falsification-proven. Phase 0 item 10 is complete.
- **TD-74 → ✅ closed and reclassified** — from "a red test" to "a suite that had not compiled for two
  iterations". §12.16's baseline row is annotated in place rather than rewritten, per this document's
  convention.
- **TD-11 → ✅** for everything a test can reach; the manual rows stay in `61-Manual-Test-Plan` §9.
- **TD-02** — newly load-bearing in a place iteration 6 could not have anticipated: the legacy null-kind
  exemption is what keeps a pre-existing scanned receipt voidable. M-B is the test that would notice if it
  were removed.
- **TD-24 / TD-78** — unchanged, and this iteration adds a third wrong-layer throw: `ReplaceLineItems`
  raises `InvalidOperationException` carrying user-facing prose in its message.
- **TD-75** — sharper now. The receipt confirmation screen shows a difference figure formatted by the same
  `{0:N2}` that ignores minor units, so a JPY receipt would display a reconciliation figure with two
  decimals that cannot exist.
- **Rows re-checked and confirmed unchanged:** TD-03, TD-06, TD-13 … TD-17, TD-20 … TD-23, TD-25 … TD-73,
  TD-76, TD-77, TD-79.

### 12.19 Next (after iteration 8) — superseded

Phase 0 items **10** (remainder) and **11** remain. Item 10 was closed by iteration 9; see §12.20.

- **10 — TD-19**: line-item reconciliation against the transaction total. Deliberately left out of
  iteration 8, which touched `Money` and did not touch `Transaction.ReplaceLineItems`/`AddLineItem`.
  It is now the ONLY money-path invariant with no test, and it lands on top of the two latent debts
  iteration 6 avoided making load-bearing (TD-59, TD-60).
- **11 — TD-47 / TD-48**: remove the seeded fictional $1,000 account in `SetupViewModel` and the
  `CloudSync` setting nothing reads.
- **TD-78, before it is a user-visible defect.** Iteration 8 created it: two commands now throw a
  domain exception on a bad currency because they have no validator. The seam to fix it has existed
  since iteration 4 and the fix is two small classes.
- **TD-75**, the `{0:N2}` formatting. Iteration 8 gave the domain a correct answer for how many
  decimals a currency has and the view still ignores it.
- ~~**TD-74**, still undiagnosed and now four iterations old.~~ **Diagnosed and closed in iteration 9 — see §12.20. It was not a red test; it was a project that had not compiled since `f691ba8`.**

### 12.21 Next (after iteration 9) — superseded

Phase 0 has **one** item left. It was closed by iteration 10; see §12.22.

- **11 — TD-47 / TD-48**: remove the seeded fictional $1,000 account — the
  `CreateAccountCommand("My First Bank", "USD", AccountType.Bank, 1000m)` call in `SetupViewModel` — and
  the `CloudSync` setting that nothing reads. Small, and the last thing standing between the tree and a
  closed Phase 0.
- **Commit this work.** Three iterations of unstaged changes now sit in one working tree, and §12.20's
  INCOMPLETE 2 is the direct cost of that.
- **TD-78**, before it is user-visible: `CreateAccountCommand` and `CreateBudgetCommand` still have no
  validator, so an invalid currency throws a domain exception the view model renders verbatim. The seam has
  existed since iteration 4 and the fix is two small classes.
- **TD-75**, the `{0:N2}` formatting. The domain has known how many decimals a currency has since
  iteration 8, and four views still do not ask.
- **Re-arm CI (TD-07).** Every claim in §12.18 and §12.20 was verified on one machine, by hand. The
  workflow that would re-run them on every push has had its triggers removed since `79c5364`.

### 12.22 Iteration 10 — Phase 0 item 11 (TD-47, TD-48), and TD-80 found alongside

| Field | Value |
|---|---|
| Date | 2026-08-28 |
| Scope | Phase 0 item **11** — TD-47 (seeded fictional account) and TD-48 (dead `CloudSync` option). TD-80 was found while reading the same screen and closed in the same pass. |
| Outcome | **TD-47 ✅ · TD-48 ✅ · TD-80 ✅ opened and closed · TD-81 🔴 opened.** The last Phase 0 item is done. |
| Tests | 310 → **313** unit; integration unchanged at **119**. Total **432**. |
| Schema | None. |
| State | Working tree only — **not committed, not pushed**. |

#### What was removed

- `SetupViewModel.cs` — the `CreateAccountCommand("My First Bank", "USD", AccountType.Bank, 1000m)` send;
  the `IsLocalOnly`, `IsCloudSync` and `TelemetryOptIn` properties; both `SaveSettingCommand` sends; and the
  four usings that died with them.
- `SetupView.axaml` — the "Local + Cloud Sync (Coming Soon)" radio with its border and caption, and the
  telemetry consent checkbox.

**The surviving "Local Only" radio became a `TextBlock`.** A radio button with a `GroupName` and no peer is
a choice control with nothing to choose: permanently checked and impossible to uncheck. The block keeps its
visual weight and its privacy sentence — which is true — without pretending a decision is being offered.

#### TD-80 — the finding this ticket did not go looking for

The audit records `CloudSync` (TD-48) but never recorded the checkbox sitting three lines below it. Pine had
an **enabled** control asking the user to consent to "sending anonymous usage telemetry", wrote
`System.Telemetry` on first run, and **contains no telemetry code at all** — nothing reads that setting and
nothing transmits anything. That is the same defect class as TD-48 and a worse instance of it: the cloud
radio was at least disabled, so it read as a roadmap promise; an enabled checkbox reads as a live choice,
and a user who ticks it believes data is leaving their machine. In a product whose entire argument is that
data never leaves the device, this was the worst possible place to be inaccurate. Removed, not deferred.

#### The zero-account question, and why nothing was added

Removing the seeded account leaves a fresh vault with **no accounts**, so the real risk in this ticket was
turning honest emptiness into a dead end. It is not one, and the evidence was checked from both directions —
independently by the reviewer before the implementing agent reported, so the conclusion is not one party's
word:

- `DashboardView.axaml:32` carries a `+ New Account` button bound to `ShowCreateAccountDialogCommand`,
  **unconditional** — not gated on `Accounts.Count`. It is the first-account path, and it was already there.
- `BalanceWidgetView.axaml:40` already renders "No accounts yet." on `IsVisible="{Binding !Accounts.Count}"`.
- `AddTransactionViewModel.cs:108` and `ScanReceiptViewModel.cs:138` both guard `Accounts.First()` behind
  `if (Accounts.Any())`; `ImportWizardViewModel.cs:146` uses `FirstOrDefault()` and null-checks before use.
- `DashboardViewModel.LoadDataAsync` accumulates with `foreach` and never divides by a count;
  `BudgetsViewModel.ProgressPercentage` guards `LimitAmount > 0`.

**So no empty state was added.** The gap between "stop inventing data" and "redesign onboarding" is real,
and this ticket stayed on the near side of it. What a fresh vault does *not* have is an empty state on the
account pickers in the receipt, add-transaction and import screens — they render an empty combo box with no
explanation. That is pre-existing, it is not a crash, and it is not TD-47; it is noted here so the next
reader does not have to rediscover it.

#### TD-81 — the reason these tests assert structure instead of behaviour

The natural test is "completing setup sends no `CreateAccountCommand` and no `SaveSettingCommand`", written
against a substituted `IMediator` with `DidNotReceive()`. It cannot be written. `InitializeVaultAsync`
resolves `VaultManager` first, which is `sealed`, has no interface, hard-codes
`LocalApplicationData/Pine` in its constructor, and writes `vaults.json` to the real disk — so the happy path
cannot execute in a test without mutating the machine running the suite. **Verified directly, not taken on
the implementing agent's word:** `VaultManager.cs:9` is `sealed class VaultManager` with no `IVaultManager`
anywhere in `src/`, and lines 17–18, 58, 60 and 74 are the constructor's path and the real writes. Every
pre-existing test of this view model stops at an early validation failure for the same reason.

The three tests therefore assert **type-level dependency** via `NetArchTest`, the idiom already used in
`ArchitectureTests.cs`. For "this command is never sent" that is strictly stronger than a `DidNotReceive()`
on one execution: it proves the send is impossible by construction rather than absent on one path. What it
does **not** prove is that setup still completes correctly — no test covers that, and none did before. The
seam is TD-81.

#### Falsification — 3 mutations, 3 killed, all re-run by the reviewer

Each mutation was applied by the reviewer independently of the implementing agent, restored with `cp` from a
backup (never `git checkout`, for the reason in §12.20 INCOMPLETE 2), and the file confirmed byte-identical
afterwards.

| # | Mutation | Red |
|---|---|---|
| M1 | Re-add the `CreateAccountCommand` send, with the usings it needs | `SetupViewModel_ShouldNot_HaveDependencyOnCreateAccountCommand` — and only it (1 failed, 312 passed) |
| M2 | Re-add `SaveSettingCommand("System.StorageMode", "LocalOnly")` | `SetupViewModel_ShouldNot_HaveDependencyOnSaveSettingCommand` — and only it (1 failed, 312 passed) |
| M3 | Re-add the `IsCloudSync` / `TelemetryOptIn` properties **alone**, with no command send | `SetupViewModel_ShouldNotExpose_CloudSyncOrTelemetryProperties` — and only it (1 failed, 312 passed) |

M3 matters because it is the one mutation M2's test cannot see: a build that re-exposes the controls without
re-adding the writes would put the promise back on screen while leaving the register looking closed.

#### Verification actually executed — every row re-run by the reviewer

| Check | Result |
|---|---|
| `"My First Bank"`, `"System.StorageMode"`, `"System.Telemetry"`, `IsCloudSync`, `TelemetryOptIn`, `"Coming Soon"` in `src/` | ✅ **0 occurrences each** |
| `dotnet test tests/Pine.Tests.Unit` | ✅ **313 passed**, 0 failed, 0 skipped |
| `dotnet test tests/Pine.Tests.Integration` | ✅ **119 passed**, 0 failed, 0 skipped |
| `dotnet build -c Release` × 8 heads | ✅ 0 errors. 0 warnings on seven; `Ui.Web` carries its one pre-existing `NativeFileReference` warning |
| M1 / M2 / M3 re-run by hand | ✅ each killed by exactly the named test, tree restored byte-identical, 313 green |
| Android / iOS heads | ❌ **not compiled** — workloads absent (`NETSDK1147`) |
| Any GUI rendered | ❌ **not observed.** The removals are claims about XAML markup, not about pixels. `61-Manual-Test-Plan` needs a first-run row: a fresh vault must reach the dashboard, show "No accounts yet.", and create an account through `+ New Account`. |

#### INCOMPLETE — stated rather than buried

1. **No GUI was rendered.** Nobody has seen the first-run screen without its cloud block and its telemetry
   checkbox, nor a freshly created vault arriving at an empty dashboard.
2. **No test covers that setup still completes** — see TD-81. The three new tests prove absence, not success.
3. **The account pickers have no empty state.** Pre-existing, deliberately out of scope, recorded above.
4. **Nothing committed or pushed.** Four iterations of work now sit unstaged in one tree.

#### Reconciliation pass

- **TD-47 → ✅. TD-48 → ✅. TD-80 → ✅ opened and closed. TD-81 → 🔴 opened.**
- **Phase 0's roadmap is now fully worked, but Phase 0 is not clean.** Item 2's encrypted export still has
  no UI and no restore path (TD-56), and **item 3 regressed after it was ticked**: `79c5364` removed the
  workflow's `push`/`pull_request` triggers, so CI has never run and Android/iOS have never compiled. Every
  claim in §12.18, §12.20 and §12.22 was verified on one machine, by hand. Ticking item 11 does not make the
  phase green; it makes item 3 the only structural thing left in it.
- **TD-24 / TD-78** — unchanged. `SetupViewModel` no longer sends the two commands whose validators were
  missing, which narrows TD-78's blast radius by exactly one caller without fixing it.
- **TD-29** — unchanged, but this ticket is a small argument for it: the settings written on first run were
  duplicated nowhere, so removing them touched one file. The composition roots are not so lucky.
- **Rows re-checked and confirmed unchanged:** TD-01 … TD-46, TD-49 … TD-79.

### 12.23 Next

Phase 0's list is worked through. What remains before this branch is defensible:

- **Commit.** Four iterations, fifty files, nothing staged. §12.20 INCOMPLETE 2 is what that costs when it
  goes wrong, and it already did once.
- **TD-07 — re-arm CI.** Now the largest single risk on the branch. Everything above was verified on one
  machine by hand; the workflow that would re-run it on every push has been disarmed since `79c5364`, and
  the Android and iOS heads have never been compiled by anything.
- **TD-74's sibling risk** — the integration suite silently stopped building for two iterations and nobody
  noticed. CI is the control that would have caught it in minutes.
- **TD-78**, then **TD-75**, then **TD-81**: a missing validator that turns a bad currency into a raw domain
  exception on screen; four views that still format money without asking the domain how many decimals it
  has; and the `VaultManager` seam that makes first-run behaviour testable.
- Then **Phase 1** proper, whose first row — structured logging with redaction (TD-14, TD-25) — is the one
  that makes everything after it debuggable.


### 12.24 Iteration 11 — Phase 1, item 1

| Field | Value |
|---|---|
| Date | 2026-08-28 |
| Branch | `feat/production-readiness-phase0-items-5-7` |
| Baseline commit | `9c2118e` |
| Scope | Phase 1 item **1** → TD-14, TD-25 |
| Outcome | 2 closed ✅ · 2 new debts opened (TD-82, TD-83) |

Written by an implementing agent against a written brief; every claim below was **re-run
independently by the reviewer**, including all four falsification mutations, which the reviewer
applied and reverted himself.

#### What was built

`src/Pine.Infrastructure/Diagnostics/`, eight new files, **no new NuGet package** beyond
`Microsoft.Extensions.Logging` itself (the abstraction was already a transitive dependency; the user
chose a hand-rolled sink over Serilog, for the same reason `IRequestValidator` was hand-rolled over
FluentValidation):

| File | What it is |
|---|---|
| `LogRedactor` | **The boundary.** Four single forward passes — paths → `<path>`/`<home>`, e-mail → `<email>`, money `nnn[.,]nn` → `<amount>`, digit runs ≥ 6 → `<digits>` — no `Regex`, so no input can make it backtrack; ordinal and invariant throughout; on failure it returns `<redaction-failed>`, **never the input**. Each rule's doc comment names the `30-Observability` §4.3 clause it implements |
| `PineLogRecordFormatter` | The §4.1 record shape, written field by field with `Utf8JsonWriter`: `timestamp` (RFC 3339 UTC), `level` in §4.2's vocabulary (`warn`, `fatal` — not the BCL spellings), `event` (snake_case, from `EventId.Name` or the category), `version`, `category`, `message`, optional `context`, optional `exception`. Every string leaves through `LogRedactor`; `{OriginalFormat}` is dropped |
| `PineFileLogWriter` | One lock, one handle, flush per record, size rotation to `<date>.NNNN.log.jsonl`, retention by ordinal name order. Every write wrapped: **a logging failure can never crash the app** |
| `PineFileLogger` / `PineFileLoggerProvider` | `ILogger`/`ILoggerProvider`. Scopes are accepted and **ignored on purpose** — a scope is exactly where a vault path or an account id would ride along unnoticed |
| `PineCrashLogger` | Static, container-free, ignores the minimum level. The thing that failed may well be DI itself |
| `PineLoggingOptions` / `…ServiceCollectionExtensions` | The §4.4 defaults (50 MB × 10 files, `%LOCALAPPDATA%/Pine/logs`) and the one `AddPineLogging()` that replaced five different ways of logging nothing |

Call sites: the OCR content trace at `PaddleOcrEngine.cs:107` is **deleted, not redacted** — there is
no redaction of a receipt's text that leaves anything worth keeping, which is the whole argument for
deleting it; what replaced it logs the region **count** and the mean confidence, both as numbers.
Both `ocr_init_error.log` writes, the `OCR EXCEPTION` console write, `OnnxNlpFieldExtractor.cs:126`
and `CropReceiptViewModel.cs:90` now go through `ILogger`; all four use `[LoggerMessage]` delegates
because CA1848 is an *error* in this repository. `Pine.Cli`'s `Console.WriteLine` calls are a CLI's
legitimate stdout and were left alone. Both `Main` catch blocks call `PineCrashLogger.WriteCrash(ex)`
instead of dumping the raw stack to `crash.log`.

Four heads also had `Microsoft.Extensions.DependencyInjection` bumped 10.0.9 → 10.0.11: the 10.0.9
pin became a hard `NU1605` downgrade error once Logging 10.0.11 entered the graph. That is a real
change to four composition roots, recorded here rather than left silent in the diff.

#### Falsification — 4 mutations, 4 killed, every one applied and reverted by the reviewer

Backups were taken with `cp`, never `git checkout` — see §12.20 INCOMPLETE 2 — and all four files
were confirmed byte-identical afterwards with `diff -q` before the suite was re-run green.

| # | Mutation | Red |
|---|---|---|
| M1 | Re-add the deleted OCR content trace (the `Console.WriteLine` of `region.Text`) | `ArchitectureTests.InfrastructureLayer_ShouldNot_DependOnSystemConsole` — and only it (1 failed, 366 passed) |
| M2 | Raise `LogRedactor.DigitRunThreshold` from 6 to 20 | `Redact_ShouldReplaceALongDigitRun` + `Redact_ShouldStripEveryIdentifyingPart_OfAReceiptShapedTrace` (2 failed, 365 passed) |
| M3 | Write the raw `message` instead of `LogRedactor.Redact(message)` | `Log_ShouldRedactTheFormattedMessage` — and only it (1 failed, 366 passed) |
| M4 | Make the rotation threshold unreachable (`long.MaxValue / 2`) | `Log_ShouldRotate_OnceTheFileExceedsTheByteThreshold` + `Log_ShouldDeleteTheOldestFile_OnceRetentionIsExceeded` (2 failed, 365 passed) |

M1 is the one that matters: it proves the guard against the *regression*, not merely against the
line. The first attempt at M4 (`if (false)`) did not compile — `CS0162` is an error here — the same
property §12.20 recorded, and the reason the mutation had to make the threshold unreachable by value
rather than by branch.

#### Verification actually executed — every row re-run by the reviewer

| Check | Result |
|---|---|
| `dotnet build -c Release` (whole solution) | ❌ fails at restore with **2 errors, 0 warnings** — `NETSDK1147`, android + ios workloads absent. Unchanged since iteration 1 |
| `dotnet build -c Release` × `Core`, `Application`, `Infrastructure`, `Ui.Avalonia`, `Ui.Windows`, `Ui.Macos`, `Ui.Web`, `Cli`, `Ai.Default`, `Ai.Explainer`, `Plugins.Host`, `Plugins.Sdk` | ✅ 0 errors; 0 warnings on eleven, `Ui.Web` carries its one pre-existing `NativeFileReference` warning |
| `dotnet test tests/Pine.Tests.Unit` | ✅ **367 passed**, 0 failed, 0 skipped (313 → 367, +54) |
| `dotnet test tests/Pine.Tests.Integration` | ✅ **119 passed**, 0 failed, 0 skipped |
| M1 / M2 / M3 / M4 re-run by hand | ✅ each killed by exactly the named tests; four files restored byte-identical; 367 green afterwards |
| `Console.Write` / `AppendAllText` / `AddConsole` / `crash.log` remaining in `src/` outside `Pine.Cli` | ✅ **0 occurrences in code** — every remaining hit is a `// WAS:` comment recording what used to be there |
| All five heads call `AddPineLogging()` | ✅ grepped and confirmed in `Ui.Windows`, `Ui.Macos`, `Ui.Web`, `Ui.Android`, `Ui.Ios` — but see INCOMPLETE 2: nothing *tests* it |
| Android / iOS heads | ❌ **not compiled** — workloads absent |
| Any log file produced by a running app | ❌ **not observed.** No head was launched; `%LOCALAPPDATA%/Pine/logs` does not exist on this machine. Every claim about the sink comes from tests driving it over a temp directory |

#### INCOMPLETE — stated rather than buried

1. **No running application has written a log record.** The sink is proven by tests, not by use.
   Nobody has seen a real `<date>.log.jsonl`, and `61-Manual-Test-Plan` needs a row of its own (§10 was taken by TD-16 in iteration 14): launch the
   Windows head, force an OCR failure, confirm a redacted record appears and that no receipt text
   does.
2. **Nothing prevents a head from silently dropping logging again — TD-83.** `AddPineLogging` is
   tested against a hand-built `ServiceCollection`; no test builds any head's real container.
   Deleting the call from `Pine.Ui.Web/Program.cs` would leave all 367 tests green. This is TD-14's
   own failure mode one layer up: five roots, no assertion.
3. **The log files are plaintext — TD-82.** `30-Observability` §4.4 mandates encryption at rest; this
   iteration deliberately does not implement it, and the doc has been annotated rather than left to
   read as though it were true.
4. **The OCR error path still returns `ex.Message` to the UI as an `OcrWord`.** The console leak is
   gone; the on-screen one predates this ticket and belongs to TD-23/TD-24, which are unchanged.
5. **Redaction is a net, not a proof.** It cannot recognise a payee or a category as such — the
   redactor's own test asserts that a merchant name *survives* it. That is the documented reason the
   OCR trace had to be deleted, and anything similar added later will leak until someone deletes it
   too.
6. **The work was uncommitted while it was verified.** Unlike iterations 7-10, the tree was clean
   at the start of this one (`9c2118e`), so only this iteration's changes were ever at risk, and
   they were committed immediately after the verification above.

#### Reconciliation pass

- **TD-14 → ✅. TD-25 → ✅. TD-82 → 🔴 opened. TD-83 → 🔴 opened.**
- **`docs/30-Observability.md` §4.4** — the encryption clause is now annotated as aspirational with
  its reason, per the Phase 1 doc-reconciliation row (TD-39/TD-40). The rest of §4 is now
  *implemented* rather than aspirational: §4.1's record shape, §4.2's level vocabulary, §4.3's
  privacy rules and §4.4's paths, rotation and retention all have code and tests behind them. §5
  (metrics), §6 (tracing) and §9 (DIB) remain unbuilt — §4.1's `trace_id`/`span_id` are omitted for
  that reason, which §4.1 explicitly permits.
- **TD-15** (diagnostics screen shows mock data) — unchanged, but now *cheaper*: a real log tail
  exists to wire the panel to, and it is Phase 1's next row.
- **TD-07** — unchanged, and still the largest risk on this branch. Everything above was verified on
  one machine by hand; CI has not run since `79c5364` disarmed its triggers, and the two heads this
  iteration edited but could not compile are exactly the two CI would have compiled.
- **TD-29** — one more small argument for it: this ticket touched all five composition roots to add a
  single line to each, and TD-83 exists precisely because there is no shared root to test.
- **Rows re-checked and confirmed unchanged:** TD-01 … TD-13, TD-15 … TD-24, TD-26 … TD-81.

### 12.25 Iteration 12 — Phase 1, item 2 (TD-15)

**Ticket:** real diagnostics or no diagnostics. The choice was made explicitly in favour of *real*:
the sink built in iteration 11 is the data source the panel had been pretending to have, so removing
the screen would have thrown away a reader that was already half-written.

#### Verification actually executed

Every number below was produced on this machine, by the orchestrating session, after the
implementing agents had finished. The agents were forbidden from building or testing anything
themselves — they share `Pine.Infrastructure` and would have raced on build output — so no agent's
claim about a test result could be, or was, taken at face value.

| | |
|---|---|
| `dotnet build` | `Pine.Core`, `Pine.Application`, `Pine.Infrastructure`, `Pine.Ui.Avalonia`, `Pine.Ui.Windows`, `Pine.Ui.Macos`, `Pine.Ui.Web` — **0 errors each** |
| `Pine.Ui.Android`, `Pine.Ui.Ios` | **NOT COMPILED.** `NETSDK1147`: the `android` and `ios` workloads are not installed on this machine. Both were edited. This is TD-07's blind spot, unchanged |
| `dotnet test` — unit | **423 passed, 0 failed**, from a 367 baseline measured before any edit |
| `dotnet test` — integration | **119 passed, 0 failed**, confirmed on four consecutive runs |
| Falsification mutations | **7 applied, 7 killed**, each reverted and the suite reconfirmed green afterwards |

**One unexplained integration flake.** A single run reported `1 failed / 118 passed`. It occurred in
the shell invocation immediately after building all seven projects, and that run produced no `.trx`,
so the failing test's name was not captured. Four subsequent runs were green. It is recorded here
rather than dismissed: an intermittent failure whose name is unknown is not the same as no failure.

#### The seven mutations

Each was applied to the restored tree, the unit suite run, then the file restored from a backup.

| # | Mutation | Result |
|---|---|---|
| 1 | Re-add a fabricated `"Migrations are up to date."` entry in the view model's constructor | 1 test failed |
| 2 | Restore `AppVersion = "Pine Vault v1.0.0-beta.1"` as a literal | 1 test failed |
| 3 | `FileShare.ReadWrite` → `FileShare.None` in the tail reader | 2 tests failed |
| 4 | Delete the probe's locked-vault short-circuit | 1 test failed |
| 5 | Add a `string? Detail` field to `DatabaseHealth` — the TD-23 leak route | 1 test failed |
| 6 | Flatten the tail with a flat `Reverse()` instead of per-file blocks | 11 tests failed |
| 7 | Remove the `IVaultLockState` bridge from `AddPineApplication` | 3 tests failed |

Mutation 6 is the one worth keeping in mind. Reversing a single flat list looks identical to
reversing the block order, and is wrong in a way no reader would spot: it also reverses the records
*within* each file, so a tail that spans a rotation comes back interleaved backwards. Eleven tests
die on it, which is the right amount of noise for a bug that silently reorders an incident timeline.

An earlier attempt at mutation 4 — replacing the guard's condition with `if (false)` — did not
compile, because unreachable-code warnings are errors here. That is worth noting: a mutation that
fails to build proves nothing, and was redone as a deletion.

#### What was built

Five contracts in **`Pine.Core.Diagnostics`** — `ILogTailReader`, `LogTailEntry`,
`IDatabaseHealthProbe`, `DatabaseHealth`, `IRuntimeInfoProvider` — implemented in
`Pine.Infrastructure/Diagnostics/` by `PineLogTailReader`, `PineDatabaseHealthProbe` and
`PineRuntimeInfoProvider`, registered by one `AddPineDiagnostics()` mirroring `AddPineLogging`'s
`TryAdd` idiom, and consumed by a rewritten `DiagnosticsViewModel`.

- **The tail reader** walks `*.log.jsonl` in ordinal name order — which *is* age order, by the
  writer's naming — from the newest file backwards, keeping a bounded queue of the last N lines per
  file rather than reading a 50 MB file into memory, and parsing with `JsonDocument`. A truncated
  final line, a real possibility mid-rotation, is skipped and never surfaced as a fake record. It
  opens with `FileShare.ReadWrite` because `PineFileLogWriter` holds the live file open; any other
  share mode makes the panel fail precisely while the app is running, which is always.
- **The probe** is a singleton over `IServiceScopeFactory`, not an injected `PineDbContext`. The
  context is Scoped and its factory *throws* when the vault is locked, while `DiagnosticsViewModel`
  is Transient and resolved from the root — so injecting it would either capture a scoped service
  under `ValidateScopes`, on in all five roots since TD-01, or take the diagnostics screen down at
  the moment someone opened it. `Locked` is answered before any scope is created.
- **`AppVersion` is not a second resolution.** `PineLogRecordFormatter.ApplicationVersion` became
  `internal` and `PineRuntimeInfoProvider` reads it, so the version on the card and the `version`
  field of the records displayed underneath it cannot drift apart. A test asserts they are equal by
  writing a record through the real provider and reading the field back off disk.

#### Two layering corrections made mid-flight

Both were found by compiling, not by reading, and both invalidated a decision made before the agents
were dispatched.

1. The contracts were first written into `Pine.Application.Diagnostics`. **`Pine.Infrastructure`
   references only `Pine.Core`**, so nothing there could implement them. They were moved to
   `Pine.Core.Diagnostics`. One agent independently hit the same wall and proposed the opposite fix —
   adding a `Pine.Infrastructure → Pine.Application` project reference. That was rejected: every
   other abstraction Infrastructure implements (`IVaultBackupService`, `IVaultMigrator`,
   `IKeyDerivationService`) already lives in `Pine.Core`, and inverting a project reference to avoid
   moving five files would be a structural change made for a clerical reason.
2. The probe needs the vault's lock state, but `IVaultSession` also lives in `Pine.Application`. A
   new `Pine.Core.Security.IVaultLockState` carries the single `IsUnlocked` bit; `IVaultSession`
   **extends** it, so there is one session object and no adapter, and the binding is made once inside
   `AddPineApplication` rather than five times across the roots. The rejected alternative was to
   resolve the context and read the `InvalidOperationException` the factory throws while locked —
   which would have made "locked" and "broken" indistinguishable, since that exception type covers
   every other misconfiguration too.

#### Pre-existing defects found and fixed

- **`DiagnosticsViewModel` was registered in `Pine.Ui.Windows` and nowhere else.** Because
  `ShellViewModel.NavigateTo` resolves via `GetRequiredService`, the Diagnostics nav button *threw*
  on macOS, Web, Android and iOS. It compiled everywhere. It is now registered on all five, and
  `PineDiagnosticsCompositionTests` builds a root-shaped container under `ValidateScopes` so the
  graph is proven resolvable rather than merely compilable.
- **`ClearLogs` implied it deleted something.** It appended `"Logs cleared by user."` to a list. It is
  now `ClearViewCommand`, labelled "Clear view", and the empty state says the files were not touched.
- **The terminal header read `pine-engine-stdout.log`**, a file Pine has never written. It now names
  `%LOCALAPPDATA%/Pine/logs/<date>.log.jsonl`.
- **The memory card said "Engine Memory"** over a `GC.GetTotalMemory` reading, which is the managed
  heap and not the process working set. Relabelled "Managed Heap".

#### Removed rather than reimplemented

- **`ExportDib`** — body: one log line and `// To be implemented`. `30-Observability` §9 wants a
  signed ZIP with a plugin inventory and a 30-day SLO summary; four of its seven contents have no
  subsystem behind them. A button exporting something else under that name is the same lie in a new
  format. §8 and §9 are annotated `Status: Aspirational`; the work is TD-84.
- **`TestFeatures` / "Seed Demo Data"** — it wrote fictional accounts, two transactions, a transfer
  and a budget **into the user's real vault**, from a button reachable in the ordinary navigation.
  This is TD-47's defect class exactly, and it was not in the register. Removing it also removed the
  view model's last write path, so `IMediator`, `TimeProvider` and the `DataChangedMessage` broadcast
  went with it — the panel now only reads.

#### New debt opened

- **TD-84** — no DIB exists; §9 annotated aspirational.
- **TD-85** — and this is the larger one. Auditing the registration bug above showed it is not
  specific to Diagnostics: **only the Windows head registers all seven navigation targets.** macOS
  lacks `Budgets`, `Settings`, `ScanReceipt` and `LiveScanner`; Web lacks the last three; Android and
  iOS lack all four. Each is a nav button that throws on click. It was **not** fixed here, on
  purpose: registering `ScanReceipt` or `LiveScanner` on the Browser head would wire up pages that
  cannot run OpenCV or PaddleInference at all (§10, Phase 3), so the fix is a per-head decision about
  which pages exist, not four more registration lines.

#### What TD-83 still says after this iteration

`PineDiagnosticsCompositionTests` narrows TD-83 but does not close it. It builds a container from the
same extension methods the heads call; it does not build any head's *real* container, because the
test projects reference `Pine.Ui.Avalonia` and none of the five heads. Deleting
`services.AddPineDiagnostics()` from `Pine.Ui.Web/Program.cs` would still leave every test green.
TD-83 stays open, and TD-85 is now a second, sharper argument for TD-29's shared root.

#### INCOMPLETE / NOT DONE

- **Android and iOS were edited blind again.** Registration lines went into two heads this machine
  cannot compile. TD-07 remains the largest risk on this branch.
- **Nothing on this page has been seen running.** Every claim here is a compiler claim or a test
  claim. The panel's rendering, the empty state, the Copy button and the
  `IsEnabled="{Binding !IsLoading}"` compiled binding are manual rows in `61-Manual-Test-Plan`.
- **The tail is untested against a real rotation ceiling.** The bounded-buffer behaviour is proven
  against a 5 000-line file, not against a 50 MB one.
- **TD-85 is registered, not fixed.**

### 12.26 Next

The row this iteration closed is struck from §12.24's list. In order:

- **TD-07 — re-arm CI.** Still the largest single risk on this branch, and now with one more reason:
  this iteration edited `Pine.Ui.Android` and `Pine.Ui.Ios` and could not compile either. Two
  consecutive iterations have now shipped registration changes to those heads sight unseen.
- **TD-85 — decide the per-head page inventory.** New, and the sharpest thing found this iteration.
  Four of the five heads throw on navigation buttons the shell renders unconditionally. It needs a
  decision about which pages each head actually serves before it needs any code.
- **Phase 1 item 3 — envelope encryption (TD-06).** The next roadmap row, and the one that turns
  password change and biometric unlock from impossible into ordinary.
- **TD-83**, which this iteration narrowed but did not close, and which folds naturally into TD-29.
- Then **TD-78**, **TD-75**, **TD-81** — the three carried over from §12.23.
- **TD-84** last: the DIB cannot be honestly built until metrics, SLOs and a plugin host exist.

### 12.27 Iteration 13 — Phase 1, item 3 (TD-06)

**Ticket:** envelope encryption. A random data key, wrapped by the Argon2id key, so that password
change and biometric unlock stop being architecturally impossible.

**One decision reshaped the whole iteration, and it was the product owner's, not the
implementer's.** Asked how to adopt existing version-1 and `pine.salt` vaults, the answer was that
no vault exists in production at all — so the instruction was to build the thing that is correct
from zero rather than the thing that is compatible with a past nobody has. That turned the largest
and riskiest half of the ticket into a deletion. There is now exactly **one** key path, and the
un-enveloped one is gone rather than carried.

#### What the envelope actually is

- **Two keys where there was one.** Argon2id still runs, with the same parameters, from the same
  header, producing bit-for-bit the same 32 bytes it produced before — but those bytes are now the
  **auth key**, and all they ever do is encrypt and decrypt 32 other bytes. The **data key** that
  SQLCipher is keyed with comes from the CSPRNG and exists on disk only as ciphertext.
  The pre-TD-05 known-answer vector (`correct horse battery staple`, salt `000102…0f` →
  `853b272a…`) is unchanged and still asserted, which is the proof that the KDF did not move while
  the envelope was added around it.
- **`pine.vault.json` version 2**, carrying a `keyWrap` object: `AES-256-GCM`, a 12-byte nonce, the
  wrapped key, and a 16-byte tag. `15-Security` §4 names AES-256-GCM and a wrapped data key; this
  is that, minus the platform-keychain half — see INCOMPLETE.
- **`VaultHeaderBinding` — the part worth reading.** The AEAD's associated data is *every field of
  the header except the wrap*, deliberately stated as a rule rather than a judgement call about
  which fields are "security relevant". Its real payload is the vault id: the id is never fed to
  Argon2id, so without the binding it would be a field an attacker could edit at will. With it,
  splicing one vault's wrap onto another's header fails the tag check. Length-prefixed encoding,
  so no two distinct headers can produce the same associated data.
- **A wrong password is now detected in the right place.** It fails as an authentication tag
  mismatch, before any file is opened, as one specific exception type — rather than as whatever
  SQLCipher says when handed a bad key. The database probe survives in `UnlockViewModel` but has
  changed meaning: it no longer checks the password, it checks that the unwrapped key opens *this*
  database.
- **`VaultKeyUnwrapException` carries nothing.** No inner exception, no property separating "wrong
  password" from "tampered header". Both are the same tag failure, and any signal distinguishing
  them is an oracle for the one question an attacker wants answered.
- **`ChangeVaultPasswordCommand`**, with the entropy floor `SetupViewModel` already enforces —
  without it, "create a strong vault, then change the password to `hunter2`" would have been a
  documented way to walk straight around that gate. Unwrap-and-rewrap happens entirely inside
  `IVaultKeyService.ChangePassword`, so no plaintext data key ever passes through the application
  layer, where nothing would zero it. The database is untouched and the open session is left alone:
  the data key did not change, so there is no reason to log the user out of a vault they are using.
- **`IVaultHeaderStore` became pure persistence.** `Create` and `ReadOrAdoptLegacy` are deleted —
  minting a header now requires the master password, which a file store has no business receiving.

#### Verification actually executed

Every number below was produced on this machine, by the orchestrating session, after the
implementing agents had finished. The agents were forbidden from building or testing anything —
they share this solution and would have raced on build output — so no agent's claim about a
compile or a test result was, or could have been, taken at face value. Three research agents ran
first; each of their load-bearing claims (call sites, DI registrations, the `15-Security` §4 text,
the ADR inventory, the test counts) was re-checked by hand with `grep` before being used.

| | |
|---|---|
| `dotnet build` | `Pine.Core`, `Pine.Application`, `Pine.Infrastructure`, `Pine.Ui.Windows`, `Pine.Ui.Macos` — **0 errors, 0 warnings each** |
| `Pine.Ui.Web` | **0 errors, 1 warning** — the pre-existing `NativeFileReference` wasm-linking warning, unrelated to this change |
| `Pine.Ui.Android`, `Pine.Ui.Ios` | **NOT COMPILED.** `NETSDK1147`: the `android` and `ios` workloads are not installed on this machine. Both were edited (one DI line each). Re-confirmed by running both builds, not assumed from §12.25 |
| `dotnet test` — unit | **474 passed, 0 failed**, from a 423 baseline |
| `dotnet test` — integration | **150 passed, 0 failed**, from a 119 baseline |
| Falsification mutations | **9 applied, 9 killed**, each reverted and the file byte-compared against its backup afterwards; both suites re-run green at the end |

#### The nine mutations

| # | Mutation | Result |
|---|---|---|
| 1 | Drop the associated data from both `AesGcm.Encrypt` and `AesGcm.Decrypt` | 2 tests failed |
| 2 | Make the data key the Argon2id output instead of random — i.e. no envelope at all | 1 test failed |
| 3 | `ChangePassword` reuses the old salt instead of rotating it | 1 test failed |
| 4 | Swallow the tag mismatch and return the plaintext buffer anyway | **8 tests failed** |
| 5 | `MinimumSupportedVersion` back to 1, re-accepting pre-envelope vaults | 1 test failed |
| 6 | Remove the vault id from `VaultHeaderBinding` | 1 unit + 1 integration test failed |
| 7 | Remove the auth-key-length guard, letting a 16-byte profile reach AES-256 | 2 tests failed |
| 8 | Handler writes the header it read instead of the re-wrapped one | 1 test failed |
| 9 | Neutralise the entropy floor in the password-change validator | 1 test failed |

Mutation 4 is the one to keep in mind. "Fails open" is a one-line edit — return the buffer instead
of throwing — and it turns the entire envelope into decoration while every round-trip test still
passes. Eight tests catch it, and not one of them is the round trip.

Mutation 9's first form (`else if (false)`) could not be applied at all: `CS0162 unreachable code`
is an error under `TreatWarningsAsErrors`. Recorded because it is a small, real piece of evidence
that the build gate has teeth.

#### INCOMPLETE — stated rather than buried

1. **Biometric unlock is not built.** The roadmap row says the envelope makes it an ordinary
   feature, and that is now true of the *architecture* and of nothing else: there is no platform
   secure storage anywhere in this repository — no Keychain, no Keystore, no DPAPI. What the
   envelope buys is that adding one becomes a second wrap rather than a redesign. New row
   **TD-87**.
2. **The data key is never rotated.** `15-Security` §4 says "the data key is rotated when the user
   changes their credential." This rotates the **auth key** and the salt. Anyone who already knew
   the data key — not the old password, the key — still knows it afterwards. The command's own
   documentation says so in those words. Closing it means `PRAGMA rekey` over the whole database,
   which is a data-loss-class operation. New row **TD-86**.
3. **This is a breaking change, and it is affordable exactly once.** Version-1 and `pine.salt`
   vaults are now refused with `LegacyVaultFormatException`. That was the right call while no vault
   exists outside a developer machine, and it stops being available the moment one does. If a real
   vault is created before TD-86 lands, `MinimumSupportedVersion` can never rise again without a
   migration that re-encrypts the database.
4. **Nothing on this page has been seen running.** The password-change section in Settings is a
   compiler claim, not a rendering claim: the strength meter, the three password boxes, the
   `IsEnabled` bindings and the result message are all manual rows. `61-Manual-Test-Plan` §1.4 has
   been rewritten from "confirm the UI does not offer this" into the steps that exercise it.
5. **The Web head now registers a crypto stack it cannot run.** `AesGcm.IsSupported` is false on
   the browser/WASM runtime, so `Pine.Ui.Web` will refuse to open any vault with
   `VaultKeyWrapUnavailableException`. That is the honest behaviour, and it is reported as "this
   build cannot open vaults" rather than as a wrong password — but it is **untested**, because
   there is no WASM runtime on this machine to test it in, and it makes the Browser head's standing
   worse than Phase 3 already records. New row **TD-88**.
6. **The key is still a managed `byte[]`.** TD-11's §12.16 write-up said closing that window
   "belongs with the envelope-key work (TD-06)". It does not close it. Every intermediate auth key
   and data key is now zeroed with `CryptographicOperations.ZeroMemory` on every path including the
   failing ones, which is more than was true before — but a compacting GC may already have moved
   any of those buffers, and nothing here can reach the copy it left behind. No claim to the
   contrary is made anywhere in the code.
7. **`15-Security` §4 is narrowed, not satisfied.** Its wrapped-data-key clause is implemented; its
   platform-secure-storage clause is not; its rotation clause is not (see 2). §4 has been annotated
   in place rather than left to read as closed.
8. **ADR-0013 still does not exist.** The encryption baseline this iteration implements is cited
   across the codebase as ADR-0013 and remains an eight-word row in `25-ADR.md`. TD-40 is untouched,
   and this iteration made that citation more load-bearing, not less.

#### Rows the register gains

| ID | Debt | Sev | Impact | Fix | Priority | Status |
|---|---|---|---|---|---|---|
| TD-86 | Password change rotates the auth key, never the data key | 🟠 | A leaked data key survives every password change; `15-Security` §4's rotation clause is unmet | `PRAGMA rekey` to a fresh random data key, behind a mandatory pre-rekey backup | P2 | 🔴 Open |
| TD-87 | No platform secure storage, so no biometric unlock | 🟡 | The envelope's second wrap has no consumer; `15-Security` §4's keychain clause is unmet | A second `VaultKeyWrap` under a platform-held key, per head | P2 | 🔴 Open |
| TD-88 | `Pine.Ui.Web` registers AES-GCM, which WASM does not provide | 🟡 | The Browser head can no longer open any vault, and this is untested rather than observed | Decide the Browser head's fate (Phase 3 already proposes dropping it) before writing a WASM-safe wrap | P2 | ✅ **Done (by decision)** — iteration 31, §12.64. The head is deleted, so no WASM-safe wrap was written and none is owed. **The guard outlives the head on purpose**: `AesGcmVaultFileCipher`, `AesGcmVaultKeyService`, `VaultKeyWrapUnavailableException` and `UnlockViewModel`'s catch branch all remain, because `AesGcm.IsSupported` is a claim about the primitive on whatever target is running, not about one deleted executable |

### 12.28 Next

The row this iteration closed is struck from §12.26's list. In order:

- **TD-07 — re-arm CI.** Still the largest single risk on this branch. Three consecutive iterations
  have now shipped registration changes to `Pine.Ui.Android` and `Pine.Ui.Ios` sight unseen.
- **TD-86 — rotate the data key.** Newly registered, and the one item on this list with a deadline
  attached to it: it is cheap while no real vault exists and expensive the moment one does.
- **TD-85 — decide the per-head page inventory**, carried from §12.26, now with TD-88 beside it as
  a second reason to settle what the Browser head is for.
- **Phase 1 item 4 — per-vault encrypted ML model (TD-16).** The next roadmap row.
- **TD-83**, which folds naturally into TD-29.
- Then **TD-78**, **TD-75**, **TD-81** — carried over from §12.23.
- **TD-84** last: the DIB cannot be honestly built until metrics, SLOs and a plugin host exist.

### 12.29 Iteration 14 — Phase 1, item 4 (TD-16)

**Ticket:** the ML categorisation model was trained on real payees and real amounts, written in
plaintext to a path relative to the process working directory, and shared by every vault. The one
finding in §4 that actively contradicts the product's central privacy promise.

**Two defects were found while mapping it that the register did not know about, and one of them
changes what the feature was.** `DefaultLocalAiProvider` ran `new MlNetCategorizer()` in its
constructor while the container registered a *separate* `MlNetCategorizer` as
`ICategorizationTrainer`. Training rebuilt the prediction engine on the container's instance; every
prediction went through the provider's private one. **Categorisation therefore never improved from
training at all** — not slowly, not partially: the trained model reached nothing until the process
restarted, at which point the constructor loaded whatever file happened to sit beside the
executable. Fixing TD-16 forces the two into one object, so this closed as a consequence rather
than as an extra.

#### What the encryption boundary now includes

- **`{vault}/ai/categorizer.model`**, sealed with AES-256-GCM. The file key is
  `HKDF-SHA256(data key, info: "pine:file-cipher:v1:ai.categorizer.model")` — **deliberately not
  the database key**. One key with two independent nonce spaces is how GCM nonce reuse happens, and
  a reused nonce under one key leaks the authentication subkey rather than one plaintext. One hash
  removes the question.
- **`IVaultFileCipher` is a general seam, not a model-specific one.** Blob layout is
  `PINEFC1\0` ‖ nonce(12) ‖ tag(16) ‖ ciphertext, with the magic and the length-prefixed purpose as
  associated data — `VaultHeaderBinding`'s "bind everything as a rule" precedent. Phase 3's
  encrypted receipt attachments are the next consumer, and the port states plainly that its
  whole-array shape is wrong for large files.
- **`IVaultDataKeyAccessor`, and the honest cost of it.** `Pine.Infrastructure` references only
  `Pine.Core`, so it could not see the vault key. `Key` and `DbPath` were **moved down** from
  `IVaultSession` into a new `Pine.Core.Security` interface — same names, same contracts, so no
  implementation and no call site changed, and the widening is visible in the type graph instead of
  smuggled in as a new capability. `IVaultLockState`'s doc comment claimed infrastructure could see
  "exactly one bit"; that claim is now false, and the comment was amended rather than left standing.
- **Staleness is prevented by comparison, not by notification.** The categoriser compares the
  store's vault fingerprint at the moment it is about to answer. Subscribing to
  `IVaultSession.Locked` would have bought a delivery window plus an undisposed subscription on a
  singleton — TD-34's problem class.
- **A model that will not decrypt is discarded and retrained, never surfaced.** It is derived data,
  reproducible from the ledger. The alternative lets one corrupt cache file stop a user opening
  their own vault.
- **Registration moved into `AddPineCategorisation()` in `Pine.Infrastructure`** specifically so
  that the one-instance relationship could be *asserted*. The two-instance defect survived the
  project's entire life because a composition root is a `static void ConfigureServices` inside a
  `WinExe` and no test can reach one.
- **A duplicate type was found and merged, not renamed.** A first pass added
  `Pine.Core.Security.VaultLockedException` without noticing that
  `Pine.Application.Security.VaultLockedException` already existed. The compiler caught it as an
  ambiguity in an *existing* test. Two types with one name and one meaning is a landmine — a
  `catch` clause would silently depend on which `using` was in scope — so the existing type was
  moved down and there is now exactly one.

#### Verification actually executed

Every number below was produced on this machine by the orchestrating session. The agents were
forbidden from building or testing anything and were told so explicitly; the test-writing agent's
report states in its own words that it makes no claim about compilation. Each load-bearing claim
from the research agent — the model path, the DI registrations, the absence of any tracked model
file, the absence of any test — was re-checked by hand with `grep` and `git ls-files` before being
used, and one of its findings (that only `Pine.Ui.Windows` registers AI) became TD-90.

| | |
|---|---|
| `dotnet build` | `Pine.Core`, `Pine.Application`, `Pine.Infrastructure`, `Pine.Ui.Windows`, `Pine.Ui.Macos`, `Pine.Cli` — **0 errors, 0 warnings each** |
| `Pine.Ui.Web` | **0 errors, 1 warning** — the pre-existing `NativeFileReference` wasm-linking warning, unrelated to this change |
| `Pine.Ui.Android`, `Pine.Ui.Ios` | **NOT COMPILED.** `NETSDK1147`: the workloads are not installed on this machine. Neither was edited this iteration, which is the first time in four iterations that is true |
| `dotnet test` — unit | **536 passed, 0 failed**, from a 474 baseline measured before any change |
| `dotnet test` — integration | **156 passed, 0 failed**, from a 150 baseline measured before any change — but see the flake note below: one earlier run failed 1 of 156 and was never reproduced |
| Falsification mutations | **11 applied, 9 killed, 2 survived.** Every one reverted and byte-compared against its backup with `cmp` afterwards; both suites re-run green at the end |

#### One integration failure that could not be reproduced, and is not being written off

Recorded because the alternative is a green table that quietly omits it.

On **one** full-suite integration run, **1 of 156 tests failed**. That run was invoked without a
per-test logger, so **the failing test was never identified** — the only surviving evidence is the
summary line. It has not recurred since:

| What was run | Result |
|---|---|
| Full integration suite, 7 further runs (5 of them with a `trx` logger) | **156 passed, 0 failed** every time; the five logged runs contain zero `outcome="Failed"` entries |
| `VaultScopedCategorizerModelTests` alone, 5 consecutive runs | **6 passed, 0 failed** every time |

So the six tests this iteration added are not the flaky ones, and whatever failed is very likely
pre-existing. The plausible mechanism is resource contention rather than a defect in this change:
this iteration's integration tests fit real ML.NET models, which pushed the suite from 51 seconds to
roughly 2 minutes 10 and saturates the CPU while the rest of the suite runs beside it. A test that
was quietly timing-sensitive would start failing occasionally under exactly that pressure.

**That is a hypothesis, not a finding.** It was not proven, the failing test was not named, and no
code was changed on the strength of it. What is certain is only this: a roughly 1-in-8 intermittent failure
exists somewhere in the integration suite, and this iteration made the suite slower and more
CPU-hungry, which is the kind of change that surfaces one. Anyone who sees an unexplained
integration failure on this branch should suspect that before suspecting their own change, and
should run with `--logger trx` so the next occurrence names itself.

#### The mutations, including the two that lived

| # | Mutation | Result |
|---|---|---|
| 1 | `Decrypt` swallows the tag mismatch and returns the buffer anyway | **6 tests failed** |
| 2 | Drop the purpose from the HKDF `info`, so every purpose shares one file key | **SURVIVED** |
| 3 | Remove the associated data from `Encrypt` and `Decrypt` | **SURVIVED** |
| 4 | Remove **both** of the above at once | 1 test failed |
| 5 | Constant nonce instead of `RandomNumberGenerator.Fill` | 1 test failed |
| 6 | The store writes the model in the clear — the pre-TD-16 behaviour | 3 tests failed |
| 7 | The categoriser never notices that the vault changed | 1 test failed |
| 8 | Training writes its result without re-checking which vault is open | 1 test failed |
| 9 | Re-register `ICategorizationTrainer` instead of forwarding — the two-instance defect | 3 tests failed |
| 10 | The store rethrows instead of discarding an unauthenticable model | 3 tests failed |
| 11 | Restore the constructor-time model load | 2 tests failed |

**Mutations 2 and 3 survived, and saying why is worth more than a green row would have been.**
Purpose separation is enforced twice over — by a distinct derived key *and* by the authenticated
associated data — so removing either one alone leaves the observable behaviour correct. Mutation 4
removes both and dies, which is what proves the property is genuinely tested rather than
accidentally true. This is defence in depth working as intended. Reporting it as "11 of 11 killed"
would have been false; reporting it as an untested claim would have been equally false.

Mutation 1 remains the one to keep in mind, for the reason §12.27 gave: "fail open" is a one-line
edit that turns the entire cipher into decoration while every round-trip test keeps passing.

#### INCOMPLETE — stated rather than buried

1. **The AI stack is registered in one head of five.** `Pine.Ui.Macos`, `Android`, `iOS`, `Web` and
   `Cli` register no `IAIProvider` at all, while `ImportTransactionsCommandHandler` ctor-injects
   one — so importing transactions is unresolvable on four heads. Found while mapping this ticket,
   **not fixed, by product-owner decision**, because it is a "what is each head for" question that
   belongs with TD-85, and because closing it would ship ML.NET registrations into two heads that
   cannot be compiled here. New row **TD-90**.
2. **Training is not opt-in, and `32-AI-Training` §12.2 says it must be.**
   `CategorizationTrainingBackgroundService` trains unconditionally every five minutes, with no
   consent gate and no setting to disable it. The spec clause is unambiguous and the code
   contradicts it. New row **TD-89**. §12.2 has been annotated clause by clause rather than left to
   read as met.
3. **Train/serve skew.** Training builds its text from `Payee` alone; import categorises on
   `Payee + Notes`. A genuine model-quality defect, unrelated to encryption, registered rather than
   quietly fixed inside a security ticket. New row **TD-91**.
4. **No default model ships.** `32-AI-Training` §6.1 says Pine ships one; nothing is tracked,
   LFS-covered or copied to output, and nothing ever was. Every vault starts empty and trains its
   own. Annotated in place.
5. **Nothing here has been seen running.** Every claim is a compiler claim or a test claim. No
   screen was opened; the categoriser's behaviour inside a live application is a manual row.
6. **The model is not backed up.** `VaultBackupService` copies `pine.db` via `VACUUM INTO` and
   touches nothing else, so a restored backup starts with no model and retrains. That is the right
   default for derived data, and it is stated here so that it is a decision rather than an omission.
7. **The key is still a managed `byte[]`**, with exactly the caveat §12.27 item 6 records. The file
   keys derived from it are zeroed on every path including the failing ones; the data key itself is
   the session's problem and remains unsolved.
8. **`IVaultLockState`'s narrowness is genuinely reduced.** §12.25's design rationale for that
   interface no longer holds in full. `PineDatabaseHealthProbe` still depends on the one-bit
   interface and still should — but "infrastructure cannot see the key" stopped being true this
   iteration, and that is a real, if small, loss of a property the codebase used to have.

#### Rows the register gains

| ID | Debt | Sev | Impact | Fix | Priority | Status |
|---|---|---|---|---|---|---|
| TD-89 | Categoriser trains with no opt-in, contradicting `32-AI-Training` §12.2 | 🟠 | Derived personal data is produced from every user's ledger with no consent gate and no off switch; the spec promises opt-in per profile | A consent setting the background service checks before training, defaulting to off | P1 | 🔴 Open |
| TD-90 | The AI stack is registered in one composition root of five | 🟠 | `ImportTransactionsCommandHandler` ctor-injects `IAIProvider`, so import cannot resolve on Macos, Android, iOS, Web or Cli | Call `AddPineCategorisation()` per head, once TD-85 settles the per-head inventory | P1 | ✅ **Done** — iteration 22, §12.46, though **not by the fix written here**. Calling `AddPineCategorisation()` per head would have registered `DefaultLocalAiProvider` on four heads that ship no native OpenCV or PaddleInference, moving the failure from handler resolution to a native abort. `PortableAiProvider` answers categorisation — the portable half — and refuses the four vision methods with a typed `AiCapabilityUnavailableException` on the TD-24 taxonomy. Import now resolves on every head |
| TD-91 | Train/serve text skew: training uses `Payee`, import categorises on `Payee + Notes` | 🟡 | The model is served inputs shaped unlike those it was fitted on, degrading accuracy in the path that uses it most | One shared function building the categorisation text, called by both | P2 | 🔴 Open |

### 12.30 Next — superseded

The row this iteration closed is struck from §12.28's list. In order:

- **TD-07 — re-arm CI.** Unchanged as the largest single risk, though this is the first iteration in
  four that did not edit `Pine.Ui.Android` or `Pine.Ui.Ios` blind.
- **TD-93 — a way to test for UI-thread blocking.** Added by §12.31. TD-92 froze the app on every
  receipt scan, shipped, and was fixed without one automated test ever changing colour. Until this
  exists, every responsiveness claim in this codebase is a manual claim.
- **TD-89 — make training opt-in.** New, and the sharpest thing found this iteration: putting the
  derived model inside the encryption boundary made it much more obvious that nobody ever agreed to
  its being produced in the first place.
- **TD-86 — rotate the data key.** Still carrying its deadline: cheap while no real vault exists,
  expensive the moment one does.
- **TD-90 and TD-85 together** — they are the same unanswered question about what each head is for.
- **TD-83**, which folds into TD-29 — now marginally smaller, since `AddPineCategorisation()` is one
  more shared registration that used to be inline in a root.
- Then **TD-78**, **TD-75**, **TD-81**, and **TD-91**.
- **TD-84** last, unchanged.

### 12.31 Out-of-band fix — the receipt scan froze the UI thread (TD-92)

Not a roadmap row. Reported directly by the product owner: *"al añadir una foto aparece como que
Windows no responde durante unos segundos"*, with the request to cover it up with a loading
indicator.

**The request could not be granted as stated, and saying so was the useful part.** A loading
indicator already existed — `ScanReceiptView.axaml` bound an indeterminate `ProgressBar` to an
`IsBusy` flag the view model set correctly. It never appeared to work because **the entire scan ran
on the UI thread**, and a blocked UI thread does not animate a spinner any more than it repaints a
window. Adding more indicators would have produced a *frozen* spinner, which reads as broken rather
than as busy — strictly worse than the bare hang it replaced.

| Where the UI thread was blocked | Fix |
|---|---|
| `ReceiptDetector.DetectReceiptConfidenceAsync` — JPEG decode, letterbox, pixel loop, ONNX run | CPU half split into a private sync method, called via `Task.Run` |
| `ReceiptDetector.AnalyzeAndCropReceiptAsync` — the above plus perspective warp, denoise, sharpen | same |
| `PaddleOcrEngine.ExtractTextAsync` — detection + recognition networks, the slowest stage | inference moved to `Task.Run` |
| `ScanReceiptViewModel` — `new Bitmap(stream)` decoding a multi-megapixel photo | decoded on the thread pool, assigned on the UI thread |

Every one of these was an `async Task` method whose signature promised to yield and whose body did
not: after the opening `await`, which completed synchronously against a `MemoryStream`, the rest ran
inline on the caller's thread. `OpenCvImageProcessor` had already been given `Task.Run` treatment at
some earlier point; the other three never were.

**The UX, once the indicator could actually run.** `ScanReceiptCommand` gained an optional
`IProgress<ScanReceiptStage>`; the stage enum lives in `Pine.Core.Ai` and carries **no text**, so the
words stay in the view model where localisation (TD-45) will need them. The thin progress bar was
replaced by a scrim overlay carrying the stage sentence and the line *"This can take a few seconds
on large photos"* — the expectation-setting half, which is what actually answers "so the user isn't
frightened". The picked photo is now shown **before** the scan starts rather than after it finishes.

**Found in passing and cleaned up:** `RecomputeReconciliation()` was called three times in a row in
`LoadAccountsAsync`, and the entire `if (!IsReconciled)` guard block was duplicated **three times**
in `SaveTransactionAsync`. Accidental copy-paste, harmless, removed.

#### Verification

| | |
|---|---|
| Builds | Core, Application, Infrastructure, **Ui.Avalonia**, Ui.Windows, Ui.Macos, Cli — 0 errors, 0 warnings each; Web 1 pre-existing wasm warning |
| `dotnet test` — unit | **540 passed, 0 failed**, from 536 |
| Mutations | 3 applied: 1 killed, **2 survived**, and the survivors are the point — see below |

**The first mutation exposed a hole in my own test, which is why it is recorded rather than
smoothed over.** Moving both `Progress.Report` calls to *after* their provider call — the exact
defect of announcing a stage once it is already finished — left the original two tests green,
because the observed order was unchanged. A third test was added asserting on **when**: each
substitute snapshots what had been reported at the instant it was invoked. The mutation then failed,
as it should have from the start.

**The second survivor cannot be closed here, and must not be reported as if it were.** Putting the
bitmap decode back on the UI thread leaves all 540 tests green. Nothing in this repository can
observe "this ran on the UI thread" — it needs a live Avalonia dispatcher painting real frames.
**The entire threading fix is therefore a code-inspection claim, not a tested one.** It is verified
by `61-Manual-Test-Plan` §11 and nowhere else; §11.1 and §11.2 are the two rows that matter, and
until someone runs them the honest status of this fix is "believed correct, unobserved".

#### Rows the register gains

| ID | Debt | Sev | Impact | Fix | Priority | Status |
|---|---|---|---|---|---|---|
| TD-92 | Async methods in the AI pipeline did CPU work inline, blocking the UI thread | 🟠 | The window stopped painting for seconds on every receipt scan and Windows marked the app "not responding"; the existing busy indicator could not animate | `Task.Run` around the CPU-bound halves of `ReceiptDetector` and `PaddleOcrEngine` | P1 | 🟢 **Fixed** (§12.31) — **untested by any automated test**, see `61-Manual-Test-Plan` §11 |
| TD-93 | No test can detect UI-thread blocking anywhere in the app | 🟡 | TD-92 was shipped, lived and was fixed without a single test changing state; the same regression can return silently at any time | A UI-thread harness (an Avalonia headless dispatcher) asserting that view-model entry points yield | P2 | 🔴 Open |
| TD-94 | `GetPagedAsync` ordered by `Transaction.Date`, a `DateTimeOffset`, which the EF SQLite provider **refuses to translate** — it throws `NotSupportedException` rather than sorting | 🔴 | **The transaction list screen and the recent-transactions dashboard widget threw every time they loaded.** A shipped, user-facing crash on a primary screen. No test caught it because no test had ever executed `GetPagedAsync` against a real SQLite database; the one unit test naming the method asserts, with a substitute, that it is *not* called | A `DateUtc` column carrying the same instant, indexed descending, ordered on instead | P1 | 🟢 **Fixed** (§12.32) — pinned by `Repositories/TransactionOrderingTests` |
| TD-95 | `TransactionLineItems.Quantity` is still a `decimal` stored as `TEXT` | 🟡 | The residue of TD-79. It is not money and has no scale policy in the domain, so scaling it to 4 would silently round a five-decimal quantity; leaving it means one column can still hold a value the store cannot compare correctly. Nothing orders or filters it today | Give quantity a domain scale policy first, then a column type | P3 | 🔴 **Open** — opened by iteration 15 |
| TD-96 | `Budget.StartDate` / `EndDate` are `DateTimeOffset` columns with the same offset-in-the-text defect as `Transaction.Date` | 🟡 | Any future `ORDER BY` or range predicate over a budget period will either throw, exactly as TD-94 did, or compare local wall clocks. Neither column reaches SQL today | The same treatment as TD-94, if and when a query needs it | P3 | 🔴 **Open** — opened by iteration 15 |
| TD-97 | `Importing/ImportKeyMigrationTests` fails intermittently under the full integration run and passes in isolation | 🟡 | A flaky test is a test nobody trusts, and this suite's whole value is that a red result means something. At this rate it will be re-run rather than read. Observed **three times, in three DIFFERENT tests** — iteration 15 (`ImportKeyMigrationTests`, 18 s against a 2 s norm), iteration 16 (name not captured before the re-run passed), iteration 17 (`BalanceRecalculationTests.Recalculate_OfALedgerADoubleWouldGetWrong_IsExactToTheCent`, 10 s, passing in isolation immediately afterwards). That it moves between tests is the diagnosis: this is not a flaky assertion anywhere, it is file contention between `TempVault` directories under xUnit's default per-class parallelism. Roughly one full run in four | Serialise the vault-backed classes into one xUnit collection, or give `TempVault` a retry on the lock | P2 | 🔴 **Open** — opened by iteration 15  **Amended, iteration 24: a sixth observation, in a sixth different test** — `Atomicity/BalanceRecalculationTests.Recalculate_WhenTheLedgerHoldsAForeignCurrencyRow_IsRefusedAndChangesNothing` failed once in a full run and passed on the immediately following one, with no code change between them. Six observations, six different tests, one diagnosis nobody has acted on  **Amended again, iteration 25: a seventh observation, in a seventh different test** — `Atomicity/CurrencyValidationTests.TransferFunds_WithACurrencyMatchingNeitherAccount_IsRejectedBeforeAnythingIsWritten` failed in a full run, passed when its class was run alone, and passed on the next full run, with no code change between the three. Seven observations, seven different tests, every one of them a vault-backed integration class under parallelism — which is the diagnosis this row has carried since iteration 15  **Amended again, iteration 26: three further observations, in three further tests, two of them in a single run** — `RecordTransactionAtomicityTests.RecordTransaction_WhenTheBalanceWriteFails_PersistsNeitherTheRowNorTheBalance`, `CurrencyValidationTests.AddTransaction_WithALowercaseCurrency_IsNormalisedAndSucceeds` and `ConcurrencyTokenTests.TwoOverlappingSplitEdits_Conflict_ThoughOnTheChildRowNotTheOwnersToken`. **Eleven observations in eleven distinct tests.** That iteration changed only `Pine.Ui.Avalonia`, which the integration project does not reference, so it cannot have been the cause — which is one more piece of evidence for the shared-vault-under-parallelism diagnosis this row has carried since iteration 15 **Amended again, iteration 27: two further observations, in two further tests** — `ImportKeyMigrationTests.AfterTheMigration_LegacyRowsMatchNoKey_AndANewlyKeyedRowMatchesItsOwn` and `CurrencyValidationTests.AddTransaction_WithAMatchingCurrency_StillPersistsTheRowAndTheBalance`, on consecutive full runs, green on the third. **Thirteen observations in thirteen distinct tests.** The diagnosis has not changed since iteration 15 and neither has the code |
| TD-98 | Nothing catches `ConcurrencyConflictException`; a refused write reaches the user as an unhandled exception | 🟡 | TD-17 turned a silent lost update into a loud refusal, which is strictly better — but the loudness currently lands wherever an unhandled exception lands. The user is told something failed without being told to reload, which is the one instruction that resolves it | Handle it in the view models as part of TD-24's exception taxonomy (Phase 1 item 8) | P2 | ✅ **Done** — opened by iteration 16, closed by iteration 19 (§12.40). The type now implements `IUserFacingFailure`, so its "Reload and try again." survives to the screen on every write path instead of being flattened |
| TD-99 | The budgets screen is a mock wearing a real screen's clothes | 🟠 | `BudgetsViewModel:148` sets `SpentAmount = 0m` with the comment "Will be calculated from transactions", so the progress bar is **always** at 0% and `IsExceeded` is **never** true — a budget can be blown wide open and the screen says nothing. `:145` renders `CategoryName` as `"Category " + b.CategoryId.ToString()[..4]`, a fragment of a GUID where a category name belongs. Both are visible on a shipped screen and neither is a placeholder the user can recognise as one | A query summing non-voided transactions per category over the budget's period, and a real category lookup | P1 | 🔴 **Open** — opened by iteration 17 **✅ Done — iteration 37 (§12.76), after nineteen deferrals.** Spend is measured by `GetBudgetProgressQuery`: voided transactions excluded on the ledger's own rule, expenses only, inside a half-open window `Budget.ResolveWindow` derives from the stored dates or the calendar period. The category name is the real name. **The part worth reading is what it does when it cannot answer**: `Budget.CategoryId` is a `Guid` and `TransactionSplit.Category` is a string (TD-32/TD-132), so the join runs `CategoryId → Category.Name → split.Category` — exact where a category row exists, impossible where one does not, which given TD-132 is what a user-created budget looks like today. There, and for a `BiWeekly` budget with no stored dates (a fortnight has no calendar anchor), the screen reports **not known** rather than zero. Cross-currency splits are excluded rather than converted and the exclusion is counted. Mutation M2 restores the `Money.Zero` this row is about and kills ten tests |
| TD-100 | `ScanReceiptViewModel.RecomputeReconciliation` computes the difference two ways | 🔵 | With an account selected the arithmetic goes through `Money` and rounds each line to the currency's scale; without one it sums raw decimals. Iteration 17 verified the divergence is **not** load-bearing — `SaveTransactionAsync` refuses with "Please select an account" before it ever consults `IsReconciled`, so every save is judged by the `Money` path — but the figure shown on screen while the form is being filled can still differ from the one that will decide the save | Nothing, until an account can be defaulted; the branch is commented rather than unified, because unifying it means guessing a currency | P3 | 🔴 **Open** — opened by iteration 17 |

### 12.32 Iteration 15 — Phase 1, item 5a (TD-31, TD-30, TD-79)

**Ticket:** scaled-integer money, the missing foreign key, and an index on the column the transaction
list orders by. Row versions (TD-17) were split out as item 5b before any code was written, because
they are a different problem — a conflict policy and a question about what the user sees — and
folding them into a migration that rewrites every money column would have made the migration
unreviewable.

#### Three things the ticket said that the code did not support

Research came first, and all three of these were confirmed by hand with `grep` before being used.
None of them are quibbles; each one changed what got built.

- **"Scaled-integer converter" could not mean what it says.** `Money` is mapped as a
  `ComplexProperty` on `Account`/`Transaction`/`Budget` and as an `OwnsOne` on splits and line items,
  so `Amount` and `Currency` are two separate columns. An EF `ValueConverter` sees one column. It
  therefore **cannot** read the currency of the amount it is converting, which makes a per-currency
  scale unavailable rather than merely inconvenient. The scale is fixed at **4**, and that costs
  nothing: 4 is the ISO 4217 maximum (`CLF`, `UYW`) and already the ceiling `Money.PowerOfTen` throws
  above, so the store's scale is greater than or equal to every currency's.
- **TD-31's stated impact was hypothetical, and the real one was worse.** The row warns that "the
  first `.Where(t => t.Amount > 100)` that reaches SQL returns wrong results". Verified: **no money
  column is filtered or ordered in SQL anywhere in `src/`** — they are only projected. So that hazard
  was latent. What was *not* latent was TD-94, below.
- **TD-17's stated cause is false.** "The background trainer contends with user saves" —
  `CategorizationTrainingBackgroundService` only reads (`:92`); its single write is a
  `SaveSettingCommand` for one watermark key (`:132`). It never touches `Account` or `Transaction`.
  The debt survives for a different reason and the register now says so.

#### TD-94 — the defect this ticket walked into

"Add an index on `Date`" turns out to be un-buildable, and finding out why exposed a shipped crash.

`Transaction.Date` is a `DateTimeOffset`. The EF SQLite provider **refuses to translate an ORDER BY
over one** and throws `NotSupportedException`. `GetPagedAsync` ordered by exactly that. Its only two
callers are `TransactionsViewModel:86` — the transaction list screen — and
`DashboardWidgetsPart3:68`. **Both threw every time they loaded.**

Nothing caught it because nothing had ever run `GetPagedAsync` against a real database. The single
unit test that names the method asserts, with an NSubstitute substitute, that it is *not* called.
A suite of 156 green tests sat on top of a primary screen that could not open.

Underneath that there is a second, quieter defect, which is what an index would have cemented. The
provider stores a `DateTimeOffset` as text with the offset appended — measured, not assumed:
`2026-08-20 23:00:00+02:00`. Sorting that text sorts by **local wall clock**, and Pine writes both
kinds of offset (`AddTransactionViewModel:28` defaults to `DateTimeOffset.Now`; `CsvImporter:81`
parses without one; other paths write UTC). A raw-SQL probe returned a purchase two hours *later*
in second place. So `Date` is not a column to index — it is a column not to order by.

The fix is a `DateUtc` column carrying the same instant, derived in the one place `Date` is assigned,
indexed descending, ordered on. `Date` keeps the user's offset untouched, because normalising it to
UTC moves a 23:00 purchase in Madrid to 21:00 and a 22:00 purchase in Santiago onto the next day.
The product owner chose this shape over the simpler normalisation for that reason.

#### The migration, and why it is written by hand

The scaffolded migration was seven bare `AlterColumn` calls. Each makes EF rebuild the table and copy
the old column with a plain `SELECT`, at which point SQLite's type affinity turns the TEXT `'12.34'`
into the INTEGER `12`. **Every amount in the vault would have become whole units, and nothing would
have thrown.** On the round numbers a hand test uses it would have looked perfect.

So the conversion runs first, in place, while the column is still TEXT: each amount is rewritten as
its integer count of ten-thousandths, still as text, by splitting the string at the decimal point and
doing integer arithmetic on the halves. EF's rebuild then copies `'123400'` into an INTEGER column,
which SQLite converts exactly. Schema stays EF's job; only the values are ours.

Two guards abort the migration rather than damaging data: an amount beyond four decimals is
**refused, not rounded** (rounding stored history is destroying evidence — the position §12.18 already
took), and a transaction whose account does not exist is refused, because the new foreign key cannot
be created over an orphan. `RAISE` is trigger-only in SQLite, so each guard is a *named* CHECK
constraint on a scratch table, the name being the error message. The first attempt named the table
instead of the constraint and produced `CHECK constraint failed: violations`, which says nothing; the
test caught it.

#### One user-visible consequence, found by measurement and fixed properly

Decimal division normalises trailing zeros, so a stored `12.00` reads back with scale 0. Five views
bind a whole `Money` and render `Money.ToString()`, which interpolated the amount directly — so
`12.00 USD` would have started displaying as `12 USD`. Rather than accept that, `ToString()` now
formats to the **currency's** scale, which also settles a pre-existing inconsistency where the same
amount rendered with or without decimals depending on how it was typed. An amount carrying *more*
decimals than its currency is still shown in full, because rounding it for display would hide the
discrepancy worth seeing.

#### Verification actually executed

Every number below was produced on this machine by the orchestrating session. Three research agents
and three repair agents were used; **none was permitted to build or test anything** — they share this
solution and would have raced on build output — so no agent's claim about a compile or a test result
was, or could have been, taken at face value. Every load-bearing claim they made was re-checked with
`grep` before use, and one agent's uncertainty ("is the FK actually in a migration?") was answered by
reading the migration rather than by reassurance.

| | |
|---|---|
| Builds | `Pine.Core`, `Pine.Application`, `Pine.Infrastructure`, `Pine.Ui.Avalonia`, `Pine.Ui.Windows`, `Pine.Ui.Macos`, `Pine.Cli` — **0 errors, 0 warnings each** |
| `Pine.Ui.Web` | **0 errors, 1 warning** — the pre-existing `NativeFileReference` wasm-linking warning, unrelated |
| `Pine.Ui.Android`, `Pine.Ui.Ios` | **NOT COMPILED.** `NETSDK1147`, workloads absent. Re-confirmed by running both builds, not carried over from a previous iteration. Neither head was edited |
| `dotnet test` — unit | **564 passed, 0 failed**, from a 540 baseline measured at the start |
| `dotnet test` — integration | **179 passed, 0 failed**, from a 156 baseline measured at the start |
| Falsification mutations | **8 applied, 7 killed, 1 survived** — see below. Each reverted and byte-compared against its backup afterwards; both suites re-run green at the end |

#### The mutations, including the one that survived

| # | Mutation | Result |
|---|---|---|
| 1 | Drop the hand-written conversion, leaving EF's plain `AlterColumn` to copy the values | **12 tests failed** |
| 2 | Replace the string conversion with `CAST(ROUND(col * 10000) AS INTEGER)` — the floating-point path | **SURVIVED.** See below |
| 3 | Drop the sign handling, so negatives lose their direction | 3 tests failed |
| 4 | `ScaledMoneyConverter.ToStorage` rounds instead of throwing | 1 test failed |
| 5 | `GetPagedAsync` orders by `Date` again — i.e. restore TD-94 | 4 tests failed |
| 6 | `DateUtc = date.DateTime`, the local wall clock, instead of `date.UtcDateTime` | 3 tests failed |
| 7 | Remove the foreign key from the model | 4 tests failed |
| 8 | Remove the beyond-four-decimals guard | 1 test failed |

**Mutation 2 survived, and it falsified a claim this iteration had written into its own source.** The
migration's remarks asserted that the obvious `ROUND(col * 10000)` conversion was wrong because it
goes through IEEE-754. The mutation passed all eighteen tests. Measured afterwards: for any amount
whose scaled value fits inside a double's 53-bit integer range, multiply-then-`ROUND` recovers the
exact figure, and `8.20`, `1.005` and `0.29` — the values the test file called "classic binary64
casualties" — are all recovered. They only defeat a conversion that *truncates*. The two paths first
diverge at `1234567890123.4567`, whose scaled value is 1.23e16, where the float path lands one
ten-thousandth away.

That value is now in the theory and the mutation dies. **The prose in both the migration and the test
file was corrected rather than left standing**: the string conversion is not chosen because the float
one is visibly broken on ordinary money, it is chosen because it has no threshold to be right about.
Recording this is the point — a mutation that survives is the only thing that would have caught an
overclaim written with complete confidence.

#### What is NOT done, stated plainly

- **TD-17 is untouched.** That is item 5b, and it was split out deliberately, not forgotten.
- **`SUM` is still computed in .NET.** `RecalculateAccountBalanceCommand` sums the ledger in process,
  and its stated reason — that a pushed-down `SUM` would coerce TEXT to REAL — **is no longer true**;
  an INTEGER column sums exactly in SQLite. Pushing it down is now possible and was out of scope. The
  comment in that file still says the old thing and should be revisited when someone takes it on.
- **`Quantity` is still TEXT** (TD-95), and `Budget`'s date columns still carry offsets (TD-96).
- **Nothing on any screen has been seen running.** TD-94 was a crash on a primary screen that a green
  suite did not notice; the fix is proven by a test that executes `GetPagedAsync` against a real
  encrypted vault, which is strictly more than existed before, but it is still not the same as
  opening the app. `61-Manual-Test-Plan` is where that gap lives.
- **One test is flaky** (TD-97), observed once in three full runs and passing in isolation.

### 12.33 Next — superseded

The rows this iteration closed are struck from §12.30's list. In order:

- **TD-94 is fixed, but its lesson is not absorbed.** A primary screen threw on every load, for an
  unknown number of releases, under a green suite. Nothing else in the application is covered by an
  integration test that actually executes the query path a screen uses. That is a bigger hole than any
  single row in this register.
- **TD-07 — re-arm CI.** Unchanged as the largest single risk.
- **Phase 1 item 5b — row versions (TD-17)**, now with an accurate cause to work from.
- **TD-97 — the flaky test**, before it trains anyone to re-run a red suite instead of reading it.
- **TD-93**, **TD-89**, **TD-86**, then **TD-90 and TD-85** together, **TD-83**, and then **TD-78**,
  **TD-75**, **TD-81**, **TD-91**, **TD-95**, **TD-96**.
- **TD-84** last, unchanged.

### 12.34 Iteration 16 — Phase 1, item 5b (TD-17), and item 5 closes

**Ticket:** optimistic concurrency tokens, the half of item 5 that iteration 15 split out. With this,
**Phase 1 item 5 is complete**.

#### The debt was real and its recorded cause was not

Iteration 15 already corrected the register; this iteration is what the correction was for. TD-17
said "the background trainer contends with user saves". It does not:
`CategorizationTrainingBackgroundService` only reads, and its single write is a `SaveSettingCommand`
against one watermark key. Had that been the actual problem, the fix would have been a token on
`Setting` and nothing else.

The real contention is structural and much wider. Every request goes through `ScopedMediator`, which
opens its own DI scope, its own `PineDbContext` and its own database transaction. Two requests in
flight therefore read, mutate and write the same row with no knowledge of each other, and the second
write wins silently. A deposit and a withdrawal that overlap leave the account holding whichever
balance was computed last, while both transaction rows sit in the ledger — a balance that disagrees
with its own history, produced without an error anywhere.

#### What was built, and three choices worth stating

- **A shadow property, not a domain one.** `Version` is declared on the model and is invisible to
  `Pine.Core`. Nothing in the domain carries a version field, no repository changed, and there is no
  property for a caller to set by accident.
- **Applied uniformly to all nine non-owned entities**, rather than to the ones that look like they
  need it. `Account` has the obvious lost-update surface, but choosing per type means re-deciding
  every time an entity gains a write path, and the Phase 3 sync work needs a version on everything
  anyway. Owned types are excluded because they are not roots.
- **A GUID, not a counter, because SQLite has no `rowversion`.** There is no native type and no
  automatic bump, so `PineDbContext` assigns the value on insert and update. A counter would have to
  be read before being incremented, and two writers that read the same counter increment it to the
  same number.
- **No automatic retry, deliberately.** Re-running the request would re-read the row and recompute
  from a newer value, which is right for an idempotent write and wrong for the read-modify-write this
  exists to catch: the retry would apply the change on top of a state the caller never saw, which is
  the lost update again with extra steps.
- **`ConcurrencyConflictException` lives in `Pine.Core`** and the translation happens in
  `EfCoreUnitOfWork`, where persistence stops. The application layer and the view models cannot
  reference `DbUpdateConcurrencyException`, so letting EF's own type travel outwards would either
  leak the ORM into every layer or, more likely, be caught by nothing and shown to the user as a raw
  provider message. The original is kept as the inner exception for the log; the message names no
  entity, no id and no amount, because it can reach a dialog.

#### The mutation that survived, and the correction it forced

Five mutations, four killed. The fifth is the useful one.

| # | Mutation | Result |
|---|---|---|
| 9 | Never bump the token | 3 tests failed |
| 10 | Overwrite `OriginalValues` as well as `CurrentValues` — the classic mistake, which makes the WHERE clause compare the row against the value being written | 5 tests failed |
| 11 | Remove the owner-promotion step | **SURVIVED.** See below |
| 12 | Stop translating `DbUpdateConcurrencyException` | 4 tests failed |
| 13 | Give owned types their own token too | 5 tests failed |

**Mutation 11 survived, and it exposed a test that passed for the wrong reason.** The test meant to
prove that a change confined to an owned collection still bumps its owner's token used
`ReplaceSplits`, which *discards* the existing splits — so the second writer issued a DELETE for a
row the first had already deleted, that DELETE affected zero rows, and EF reported a conflict on the
CHILD. The owner's token was never consulted. The test was green, the claim in its name was false,
and removing the mechanism it was supposedly testing changed nothing.

The case that actually needs the owner's token is two writers each **adding** a line item. Nothing is
deleted, so there is no child-level conflict to fall back on, and — this is why it matters — the
result would be genuinely corrupt. Each writer's own set is internally valid, one line of 100 against
a total of 100, so `Transaction.EnsureValid` passes for both. Land them both and the transaction
holds two line items summing to 200 against a total of 100: a state the aggregate forbids, reached
with no error anywhere. That test was added, it kills mutation 11, and the split test was **kept but
renamed and re-described** so it stops claiming credit for a mechanism it does not exercise.

#### Verification actually executed

| | |
|---|---|
| Builds | Core, Application, Infrastructure, Ui.Avalonia, Ui.Windows, Ui.Macos, Cli — **0 errors, 0 warnings each** |
| `Pine.Ui.Web` | **0 errors, 1 warning** — the pre-existing wasm `NativeFileReference` warning |
| `Pine.Ui.Android`, `Pine.Ui.Ios` | **NOT COMPILED**, `NETSDK1147`. Neither head was edited |
| `dotnet test` — unit | **567 passed, 0 failed**, from 564 |
| `dotnet test` — integration | **187 passed, 0 failed**, from 179 |
| Mutations | **5 applied, 4 killed, 1 survived** and produced the correction above. Each reverted and byte-compared against its backup |

#### What is NOT done

- **Nothing in the UI catches `ConcurrencyConflictException`.** It will reach the user as whatever
  the view models do with an unhandled exception today, which is TD-24's territory. Registered as
  **TD-98**; it is a real gap and the exception's message was written to be readable precisely
  because that gap exists.
- **No test proves the trainer and a user save now coexist safely**, because that was never the
  defect — see above. What is proven is the structural case, with two contexts over one vault file.
- **Concurrency is unobservable by hand.** There is no manual row for it: producing a conflict needs
  two overlapping writes, which the UI gives no way to arrange. A note was added to
  `61-Manual-Test-Plan` §14 saying so rather than inventing a procedure nobody can follow.

### 12.35 Next — superseded

Phase 1 item 5 is closed. §12.33's list stands, minus the row this iteration took:

- **TD-94's lesson**, still the largest one on this branch: a primary screen threw on every load under
  a green suite, and almost nothing else is covered by a test that executes the query path a screen
  actually uses.
- **TD-07 — re-arm CI.**
- **TD-98** — surface the concurrency conflict as something a user can act on, which folds naturally
  into TD-24's exception taxonomy (Phase 1 item 8).
- **TD-97** — the flaky test.
- **TD-93**, **TD-89**, **TD-86**, then **TD-90 and TD-85** together, **TD-83**, then **TD-78**,
  **TD-75**, **TD-81**, **TD-91**, **TD-95**, **TD-96**.
- **TD-84** last, unchanged.

### 12.36 Iteration 17 — Phase 1, item 6: the row was already closed, and what was actually missing

**Ticket:** Phase 1 item 6, "Money scale + rounding + `Allocate`; line-item reconciliation in the
confirmation UI (TD-18, TD-19)".

#### The row duplicated Phase 0 item 10, and the code says so

Before any work, the two debts were checked against the source rather than against the register —
the register having been wrong twice already in this phase (TD-17's cause, TD-30's index).

- **TD-18 is genuinely implemented.** `Money`'s constructor rounds half-to-even (`Money.cs:102`),
  refuses a fractional amount in a zero-minor-unit currency (`:94`), and `Allocate` partitions whole
  minor units in `Int128` with leftovers going to the largest remainders (`:236-247`). 46 tests
  across the four core files, counted.
- **TD-19's UI half is genuinely implemented**, which is the half most likely to have been claimed
  and not built. The reconciliation panel is at `ScanReceiptView.axaml:152-191`, the one-click
  adjustment is `RecordDifferenceAsAdjustmentCommand` bound at `:184`, and
  `ScanReceiptReconciliationTests` covers it with 9 tests.

So the row was bookkeeping, not work: iterations 8 and 9 closed it under Phase 0 item 10 and nobody
struck it from Phase 1. It is now ticked with that explanation rather than silently.

#### What had genuinely not happened

**The scale policy never left the domain.** TD-18 taught `Money` that JPY has no minor unit and KWD
has three; every view went on printing `{0:N2}`. A yen balance was shown with two decimals it cannot
be spent in, and a KWD amount had its third digit dropped on screen while the database kept it.

TD-75 recorded this and gave a reason that was **false**: "there are no Avalonia converters in the
project, so there is nowhere to put the rule today". `Utils/Converters.cs:9,16` already held an
`IValueConverter`. The row had been open partly against an obstacle that did not exist.

**And the dashboard was showing a fabricated number**, which no row recorded at all.
`DashboardViewModel` added every account's `Balance.Amount` into one bare `decimal` and labelled the
result with whichever currency the **last account in the loop** carried. A vault holding 1 000 USD
and 900 EUR displayed "1900 EUR" — not an inaccurate total, but a quantity of nothing. `Money.Add`
forbids adding across currencies; this got past it by never using `Money`. It also formatted with
`{0:C2}`, which emits the *machine's* currency symbol: on a Spanish-locale machine a dollar balance
read `1.234,56 € USD`.

#### What was built

- **`Money.ToAmountString()`** — the amount alone, at its currency's scale, in the current culture.
  `ToString()` is now that plus the code, so one rule serves both shapes. Group separators are
  included, because the `{0:N2}` bindings being replaced already grouped and dropping them would have
  traded a fix for a regression at exactly the magnitudes where a figure is hardest to read.
- **A `MoneyAmount` converter** in the existing `Converters` class; the two widget bindings now pass
  a whole `Money` through it instead of formatting `.Amount` in markup.
- **The budgets row formats in its view model**, because it carries a `decimal` and a currency code
  rather than a `Money` and is the only place that knows both halves. It uses the non-throwing
  `FromStoredValue`/`FromRounded` pair: a budget in a code no longer in the ISO table (TD-63 permits
  one) must render, not take the page down.
- **The dashboard groups by currency and folds each group with `Money.Add`**, so a cross-currency
  addition would now throw rather than produce a plausible wrong answer — the defect is
  unrepresentable rather than merely absent. One figure per currency; no borrowed symbol.

#### A change proposed in the design and then NOT made

The design said the two branches of `RecomputeReconciliation` would be unified, because one rounds
through `Money` and the other sums raw decimals. Verification said otherwise:
`SaveTransactionAsync:269-272` refuses without a selected account **before** it consults
`IsReconciled`, so every save is judged by the `Money` path, and the raw branch only feeds the figure
shown while the form is still being filled. Unifying it would have required inventing a currency to
round to. It is commented instead, and carried as **TD-100**. Making the change anyway, to match a
plan the checking had already contradicted, would have been the wrong kind of consistency.

#### Verification actually executed

Two research agents were used and **neither was permitted to build or test anything**; both of their
verdicts — that TD-18 and TD-19 really are implemented — were re-checked by hand with `grep` before
being acted on, including counting the tests they cited.

| | |
|---|---|
| Builds | Core, Application, Infrastructure, Ui.Avalonia, Ui.Windows, Ui.Macos, Cli — 0 errors, 0 warnings each; Web 1 pre-existing wasm warning |
| `Pine.Ui.Android`, `Pine.Ui.Ios` | **NOT COMPILED**, `NETSDK1147`. Neither head was edited |
| `dotnet test` — unit | **582 passed, 0 failed**, from 567 |
| `dotnet test` — integration | **187 passed, 0 failed**, unchanged |
| Falsification mutations | **5 applied, 5 killed** |

| # | Mutation | Result |
|---|---|---|
| 14 | Restore the raw-decimal cross-currency sum with the last account's currency | 2 tests failed |
| 15 | Format at a hardcoded 2 decimals instead of the currency's scale | 5 tests failed |
| 16 | Drop the group separators (`F` for `N`) | 9 tests failed |
| 17 | Budgets row back to a hardcoded `N2` | 2 tests failed |
| 18 | Remove the change notification for the rendered budget text | 1 test failed |

**No mutation survived this time**, which is worth stating only because the previous two iterations
each had one that did — and both survivors produced corrections to claims already written down with
complete confidence.

#### What is NOT done

- **The budgets screen is a mock** (**TD-99**, new). `SpentAmount = 0m` with a "will be calculated"
  comment, so the progress bar never moves and an exceeded budget is never flagged; the category name
  is four characters of a GUID. This iteration formatted those figures correctly and **deliberately
  did not make them true** — computing real spend is a new query and a ticket of its own, and folding
  it in here would have turned a formatting fix into a feature.
- **Nothing was seen running.** Every claim above is a compiler or test claim. The reconciliation
  panel iteration 9 built has still never been rendered by anyone, which §12.20 flagged and no
  iteration has answered; `61-Manual-Test-Plan` §13 is written for it now, but writing the procedure
  is not running it.
- **TD-97 got worse, not better.** A third intermittent failure, in a third different test. That it
  moves between tests is the diagnosis: it is `TempVault` file contention under xUnit's default
  parallelism, not a flaky assertion. The row now says so.

### 12.37 Next

- **TD-99 — make the budgets screen true or take it down.** New, and the sharpest thing found this
  iteration: it is TD-94's shape again — a shipped screen that looks like it works.
- **TD-94's lesson**, unchanged and still the largest: almost nothing is covered by a test that
  executes the query path a screen actually uses.
- **TD-07 — re-arm CI.**
- **TD-97 — the flaky suite**, now diagnosed.
- **TD-98** — surface the concurrency conflict, which folds into TD-24 (Phase 1 item 8).
- **TD-93**, **TD-89**, **TD-86**, then **TD-90 and TD-85**, **TD-83**, then **TD-78**, **TD-75**'s
  siblings **TD-95**, **TD-96**, **TD-100**, and **TD-81**, **TD-91**.
- **TD-84** last, unchanged.

---

### 12.38 Iteration 18 — Phase 1, item 7 (TD-20, TD-21, TD-22)

**Ticket:** Phase 1 item 7, "Receipt duplicate detection; locale-driven date parsing; honest
confidence labelling."

All three rows are the same defect class, which is why they were worked as one ticket: the receipt
pipeline told the user something it had not earned the right to say. It said "this is a new
transaction" without looking (TD-20), it said "5 March" when the paper said 3 May (TD-21), and it
said "90% confident" from a tally that had never touched the OCR engine's own confidences (TD-22).

#### What was true at the start, read from the code rather than the register

| Claim | Verified against |
|---|---|
| The receipt write path performs no duplicate lookup at all | `AddTransactionCommand.cs:63` — the handler loads the account and writes |
| Import de-duplication already existed and was indexed | `ITransactionRepository.GetExistingImportKeysAsync`; index `IX_Transactions_AccountId_ImportKey` (migration `20260827141109`) |
| **TD-21's severity was still understated**, beyond §12.12's correction | The cascade used `TryParse`, not `TryParseExact`. A culture's `TryParse` accepts far more than that culture's own short pattern, so `InvariantCulture` swallowed every ambiguous date and the `es-EC` leg was unreachable *by construction*, not merely by ordering |
| The date regex could not match a single-digit day or month | `OnnxNlpFieldExtractor.cs:67` — `\d{2}[/-]\d{2}[/-]\d{4}` |
| **Real per-word OCR confidence already existed and was discarded** | `Pine.Core/Ai/OcrWord.cs:19` carries `float Confidence`; the extractor never read it |

That last row is the finding worth keeping: TD-22's remedy was not "invent a confidence model", it was
"stop throwing away the one the engine already produces".

#### What was built

- **`Pine.Core/Ai/ReceiptDateParser`** — explicit format lists matched with `TryParseExact`, ISO first
  because it cannot be read two ways, then the day/month order implied by
  `CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern`, then the other. Returns
  `ReceiptDateCandidate(Value, IsAmbiguous, Alternative)`. An ambiguous date is **carried, not
  resolved**: the confirmation screen shows both readings and a one-click swap that leaves the
  question open.
- **`Pine.Core/Ai/ReceiptFieldConfidence`** — `FieldConfidence.FromWordScores` takes the **minimum**
  of a field's words, because a field is worth what its worst-read word is worth. Bands are
  `High >= 0.90`, `Medium >= 0.70`, else `Low`, with `NotFound` a **distinct band** rather than a
  score of zero — the old tally could not tell a misread merchant from one that was never on the page,
  and those two need different words on screen. `ComputeConfidence` is **deleted**, and
  `ReceiptExtractionResult.ConfidenceScore` with it. There is deliberately no aggregate score: a single
  percentage cannot tell the user which field to check, which is the only thing they can act on.
- **`ImportKeyFactory.FromReceipt`**, scheme `s1:` — reusing the import path's existing lookup and
  index. No new migration. `Derive` (`h1:`) and `FromReference` (`r1:`) were deliberately **not**
  touched; see the debt row below.
- **`CheckReceiptDuplicateQuery`** returning a projection, not the aggregate, run twice: at extraction
  and again immediately before the write. The warning is **non-blocking** — two identical coffees on
  one day are two transactions, and a second press saves.

#### Falsification mutations

Eleven mutations, run by the controller against committed code, ten killed.

| # | Mutation | Result |
|---|---|---|
| 1 | Invert the culture's day/month preference | Killed (4 tests) |
| 2 | `IsAmbiguous` always false | Killed (1) |
| 3 | `TryParse` instead of `TryParseExact` — TD-21's original cause | Killed (5) |
| 4 | Maximum instead of minimum across a field's word confidences | Killed (1) |
| 5 | `Low` instead of `NotFound` for an empty word list | Killed (1) |
| 6 | Let a non-finite confidence through `Scores()` | Killed (2) |
| 7 | Revert the `F4` scale pin in the receipt key | Killed (1) |
| 8 | `Math.Abs` on the key's amount — strip the ledger sign | Killed (1) |
| 9 | Return `h1:` instead of `s1:` | Killed (2) |
| 10 | Make the pre-save duplicate gate never fire | Killed (4) |
| 11 | Stop clearing the duplicate acknowledgement when the date is edited | **SURVIVED** |

**Mutation 11 survived by genuine redundancy, not by missing coverage**, and the difference was
established by reading the code rather than assumed: `SaveTransactionAsync` recomputes the import key
from the *current* date, amount, merchant and account on every press, and all four of those fields are
inputs to the key — so a date edit changes the key and the re-check fires whether or not the
acknowledgement was cleared. `ClearDuplicateAcknowledgement` is belt-and-braces. It is kept, because a
future change to the key's inputs would make it load-bearing again, but it should be documented as
redundant so that no later maintainer weakens the key comparison believing the clearing covers it.
This is the same category as the two survivors recorded in §12.29.

#### Two defects found in this iteration's own new code

Both were `NaN`, and neither appeared in a green suite.

1. **`FromWordScores` reported an all-`NaN` word list as `High` confidence.** Every IEEE-754
   comparison against `NaN` is false, so the `0..1` guard passed it, `lowest` kept its
   `double.MaxValue` seed, and `BandFor(double.MaxValue)` returned `High`. Garbage scored as
   trustworthy — TD-22 reproduced inside the code written to close it. Found by review, not by tests.
2. **A `NaN` confidence then crashed the entire scan.** `Math.Clamp` does **not** neutralise `NaN`
   (its comparisons are false too), so once fix 1 made non-finite input throw, a single `NaN` on a word
   backing a selected field raised `ArgumentOutOfRangeException` out of the pipeline. This one lived in
   the seam *between* two tasks: each task's own suite was green and each task's own review approved.
   Fixed by mapping non-finite to `0f` in `Scores()` — the throw at the boundary was kept, because a
   confidence the engine could not compute is not evidence of a good reading, and `Low` tells the user
   to check the field while a crash tells them nothing.

#### Test counts

| Suite | Before | After |
|---|---:|---:|
| `Pine.Tests.Unit` | 582 | **643** |
| `Pine.Tests.Integration` | 187 | **190** |

Both green at this commit, verified by the controller's own runs rather than taken from any report.
TD-97 did not reproduce in these runs, which is consistent with it being intermittent rather than fixed.

#### Deliberately not done

- **Per-line-item confidence is not surfaced.** `Confidence.LineItems` is built from product lines
  before the extractor appends synthesized tax/tip/discount rows, so `LineItems.Count` can exceed
  `Confidence.LineItems.Count` on a taxed receipt. Pairing them positionally would show the wrong
  line's band and throw on a taxed receipt. Registered as **TD-101**.
- **`Derive`'s identical scale defect is untouched.** Registered as **TD-102**.
- **Nothing was seen running.** Every claim here is a compiler, test or mutation claim. The ambiguity
  prompt, the weak-field hints and the duplicate warning have never been rendered by a human;
  `61-Manual-Test-Plan` §13.6-13.8 is written for them now, but writing a procedure is not running it. This is
  the fourth consecutive iteration to say so.

#### New debt

| ID | Row | Sev | Impact | Fix | Pri | Status |
|---|---|---|---|---|---|---|
| TD-101 | `ReceiptConfidence.LineItems` is indexed against product lines only, while `ReceiptExtractionResult.LineItems` also carries appended adjustment rows | 🟡 | Any consumer pairing them positionally shows the wrong band and throws on a taxed receipt | Thread word provenance through `AppendParsedAdjustments`, or key the confidences by line identity | P2 | 🔴 **Open** |
| TD-102 | `ImportKeyFactory.Derive` (`h1:`) formats its amount with a bare `decimal.ToString`, so the value's scale leaks into the key | 🟡 | A statement re-exported with `10.5` where it once wrote `10.50` produces different keys, and its rows are re-imported as new transactions | Not fixable in place — the versioning discipline requires an `h2:` scheme | P2 | 🔴 **Open** |
| TD-103 | `Pine.Tests.Unit` is no longer platform-free: a `[ModuleInitializer]` registers headless Skia for the whole assembly so the scan view model's `Bitmap` decode works | 🟢 | A CI image without native Skia binaries fails the unit suite | Either accept and document as a CI requirement, or make the image decode a seam the view model can be tested without | P3 | 🔴 **Open** |

### 12.39 Next

- **TD-99 — make the budgets screen true or take it down.** Still the sharpest open row, and unchanged
  by this iteration.
- **TD-94's lesson**, unchanged and still the largest: almost nothing is covered by a test that
  executes the query path a screen actually uses.
- **TD-07 — re-arm CI.** Now four iterations old.
- **Phase 1 item 8** — the exception taxonomy (TD-23, TD-24), which **TD-98** folds into.
- **TD-101**, then **TD-102**, then **TD-103**.
- **TD-97 — the flaky suite**, diagnosed, not fixed.
- **TD-93**, **TD-89**, **TD-86**, then **TD-90 and TD-85**, **TD-83**, then **TD-78**, **TD-75**'s
  siblings **TD-95**, **TD-96**, **TD-100**, and **TD-81**, **TD-91**.
- **TD-84** last, unchanged.

### 12.40 Iteration 19 — Phase 1, item 8 (TD-24, TD-98; TD-23 re-verified)

**Ticket:** Phase 1 item 8, "Exception taxonomy; split unlock auth and schema phases; stop surfacing
`ex.Message`."

The row bundles three things that turned out to be in three different states. The unlock split
(TD-23) was closed by iteration 1 and needed **verifying, not building**. `ex.Message` (TD-24) was
open, and had been quoted as unfixable-in-place by four previous iterations. TD-98 was a separate row
that had been *scheduled into* this one by iteration 16, and it closes here as a consequence rather
than as extra work.

#### What was true at the start, read from the code rather than the register

Every `catch` in every UI project was enumerated and checked, not sampled.

| Claim | Verified against |
|---|---|
| **Eleven** catch blocks render `ex.Message` into a bound property | `AddTransactionViewModel:178`, `CreateAccountViewModel:84`, `DashboardViewModel:155`, `ImportWizardViewModel:185`, `:269`, `:323`, `LiveScannerViewModel:225`, `:308`, `ScanReceiptViewModel:316`, `:448`, `SetupViewModel:329` |
| §7's wording "and others" understated nothing and named too little | The eleven above are the whole set; `Pine.Ui.Android`, `Pine.Ui.Ios`, `Pine.Ui.Web` and `Pine.Cli` contain **no `catch` at all** |
| Two further sites render `ex.Message` of a **typed** exception on purpose | `SettingsViewModel:200`, `TransactionsViewModel:194` — both `RequestValidationException`, both documented as deliberate |
| TD-23 is genuinely still closed | `UnlockViewModel:117-147` — the migration runs in its own DI scope *after* `UnwrapDataKey` has already answered the password question with an authentication tag; nine typed catches at `:161-219` precede the generic one at `:221` |
| **Nothing anywhere catches `ConcurrencyConflictException`** (TD-98) | Whole-repo grep: it is thrown at `EfCoreUnitOfWork.cs:66` and referenced only in tests. In the UI it fell into the eleven blocks above and rendered as raw text |
| The logging idiom for a view model already existed | `CropReceiptViewModel.cs:113-128` — source-generated `[LoggerMessage]` behind a nullable-logger wrapper, because CA1848 is an error in this repository |

#### The design decision, and why the obvious one was refused

§7 prescribed *"`UnlockViewModel` is the sole exception and does it correctly — apply that pattern
everywhere."* That prescription was **not** followed, and following it would have been wrong.
`UnlockViewModel`'s cascade is correct *because* several of its arms say the same sentence: a
`VaultKeyUnwrapException` and a generic failure must be indistinguishable, or an attacker holding a
copy of a vault learns whether a candidate password was the failing part. That is a security decision
local to one screen, and copying its *shape* to twelve others would have produced twelve hand-written
cascades that drift.

What generalises is the rule underneath it: **an exception's own words reach the screen only if
someone decided they should.**

- **`Pine.Core/Common/IUserFacingFailure`** — one member, `UserFacingMessage`. Deliberately **not**
  `Exception.Message`: `Message` is read by logs, debuggers and `ToString`, and its audience is an
  engineer. Making it double as on-screen text would mean every future edit to a diagnostic string
  silently edits the UI. An implementer may return `Message` when the two genuinely coincide, but
  that is then a stated decision.
- **`Pine.Ui.Avalonia/Diagnostics/UserFacingError`** — `Describe(exception, fallback)` and
  `Report(logger, exception, operation, fallback)`. The fallback is written **at the call site**,
  because the call site is the only place that knows what the user was trying to do and therefore
  what did not happen. A blank fallback is refused with an `ArgumentException` rather than rendering
  an empty error area, which a user reads as success.
- **The detail moves, it is not deleted.** `Report` writes the whole exception — type, message, stack
  — to the TD-14 JSONL sink before returning the sentence. Refusing to show `ex.Message` would be a
  net loss if the text went nowhere: today a user can at least read the leak aloud to a support
  channel. Eight view models gained an `ILogger<T>? logger = null` parameter, using the exact
  `CropReceiptViewModel` idiom so no composition root and no existing test had to change.

**Three types opted in, and each for a different reason:**

| Type | `UserFacingMessage` | Why |
|---|---|---|
| `RequestValidationException` | `=> Message` | `Compose` already builds `Message` from validator sentences that the type's contract requires to be complete and user-facing. The two coincide *by construction*. |
| `ConcurrencyConflictException` | `=> Message` | Its message names no entity, id or row value — a constraint its own remarks already imposed — and ends in the instruction that resolves the conflict. **This is TD-98.** |
| `VaultLockedException` | **a fixed sentence, not `Message`** | This is the case the two-property design exists for. Its six construction sites write for a log: *"so no file key can be derived"*, *"so the categorisation model cannot be trained"*. Each is accurate and each is the wrong thing to show someone whose session merely timed out (TD-11). |

**The nine vault exceptions deliberately did not opt in**, so `UnlockViewModel` was not touched at
all. That is the anti-oracle property from TD-23 being preserved on purpose rather than by neglect.

#### What changed on each screen

Eleven `ex.Message` renderings replaced with a sentence naming what did not happen. Three pairs of
hand-rolled catch blocks — `SettingsViewModel` twice, `TransactionsViewModel` once — were **collapsed
into one**, because "catch the validation exception and show it; catch everything else and show a
fixed sentence" *is* this taxonomy, written out by hand three times. The merge is not a tidy-up: each
of those three sites now also carries the concurrency and locked-vault instructions, which the
hand-rolled version silently flattened.

Two wordings are load-bearing claims rather than reassurance, and are only true for a reason:

- `"No transactions were imported."` — true because `ImportTransactionsCommand` runs inside
  `TransactionBehavior`'s single database transaction, so a throw rolls the whole batch back. If that
  ever stops being true the sentence becomes a lie; the comment at the call site says so.
- `"Your vault is unchanged and your current password still works."` — pre-existing, and true because
  the header write is atomic. Carried over unmodified.

The dashboard is the one unsatisfying case: its failure sentence still lands in `Greeting`, the
screen's header text, because that view has no error surface to bind to. Registered as **TD-105**
rather than papered over with a view change smuggled into this ticket.

#### Falsification mutations

Eleven mutations, **11 killed**. Each was applied to the working tree, built, run against the new
tests, and reverted.

| # | Mutation | Result |
|---|---|---|
| 1 | `Describe` returns `exception.Message` instead of the call site's sentence | 12 tests failed |
| 2 | The empty-`UserFacingMessage` guard is dropped | 2 tests failed |
| 3 | `AggregateException` is unwrapped unconditionally, not only when single-inner | 1 test failed |
| 4 | The `InnerException` chain is walked, reaching a nested user-facing exception through a wrapper | 2 tests failed |
| 5 | The blank-fallback guard is removed | 3 tests failed |
| 6 | `Report` stops writing the exception to the log | 13 tests failed |
| 7 | `ConcurrencyConflictException` stops implementing `IUserFacingFailure` | 5 tests failed |
| 8 | `VaultLockedException.UserFacingMessage` returns `Message` | 2 tests failed |
| 9 | `AddTransactionViewModel` goes back to `$"Error: {ex.Message}"` | 3 tests failed |
| 10 | `SettingsViewModel` ignores the taxonomy and always shows its own sentence | 2 tests failed |
| 11 | `Report` returns the fallback instead of what `Describe` decided | 7 tests failed |

**Mutations 4 and 10 did not compile on the first attempt, and that was recorded as *not a kill*.**
A mutation the compiler rejects proves nothing about the tests — it proves the mutation was malformed
(4 was missing a cast; 10 left a `catch (Exception ex)` with an unused `ex`, which is an error in this
repository). Both were rewritten until they compiled and were then re-run; both died on tests. Calling
a compiler rejection a kill is the cheapest way to inflate a mutation score, and it is worth naming
because it nearly happened here.

#### Test counts

| | Before | After |
|---|---|---|
| `Pine.Tests.Unit` | 643 | **673** |
| `Pine.Tests.Integration` | 190 | **190** (untouched — the change is entirely in the presentation layer) |
| Total | 833 | **863** |

Both suites green at this commit. Two new files: `Diagnostics/UserFacingErrorTests.cs` (the rule) and
`ViewModels/ErrorMessageTaxonomyTests.cs` (the screens). They are separate on purpose — **a correct
mapper that nothing calls is exactly the shape of a fix that is not one**, and only the second file
can tell the difference.

The screen tests are driven with a real `VaultHeaderCorruptException`, chosen because it is a type
this application actually throws, it carries a `FilePath`, and its message names that path. It is the
leak, not a stand-in for one. The rule tests are driven with a type this codebase does **not** contain,
so that passing cannot depend on the current inventory of exceptions — which is the whole premise of
the fix.

#### Deliberately not done

- **`61-Manual-Test-Plan` was not updated.** Four consecutive iterations added procedures to it; this
  one did not, and the twelve new sentences have therefore never been read on a screen by anyone.
  Stated rather than skipped quietly.
- **Nothing was seen running.** Fifth consecutive iteration to say so. A bound property holding the
  right sentence is not a screen showing it.
- **TD-78 is unchanged and its character shifted.** `CreateAccountCommand` and `CreateBudgetCommand`
  still have no validator, so an unknown currency still arrives as an `ArgumentException` from inside
  `CurrencyCode`. It now produces the generic sentence instead of leaking a parameter name — strictly
  safer, strictly less helpful. Giving those two commands validators is what turns it into a sentence,
  and that is still TD-78.
- **No `Result<T>`.** `RequestValidationException`'s remarks already record why Pine signals refusal
  with exceptions, and this iteration took that shape as given rather than re-opening it. The ticket
  is about what reaches a human, and that is what was fixed.
- **The Android and iOS heads were not compiled.** Their workloads are not installed on this machine;
  this is pre-existing and unrelated to the change, which adds only optional constructor parameters.
  Windows, macOS, Web, CLI and both test projects build clean.

#### New debt

| ID | Row | Sev | Impact | Fix | Pri | Status |
|---|---|---|---|---|---|---|
| TD-104 | The log half of the fix is opt-in: eight view models take `ILogger<T>? logger = null`, so a construction site that omits it loses the log record silently | 🟡 | The honest trade of this ticket is "the detail moves from the screen to the log". Where the logger is null the detail moves *nowhere*, and nothing detects it — the fix degrades into deletion without saying so | Typed view-model factories (TD-28) would make the logger non-optional; or make `UserFacingError` an injected seam rather than a static class | P2 | 🟡 **Partly closed** — iteration 24, §12.50. The fix this row names arrived for the three view models it hurt most: `AddTransactionViewModel`, `CreateAccountViewModel` and `ImportWizardViewModel` were the ones built by hand, so their `ILogger` was `null` on **every** real run, not merely at a careless call site. `IViewModelFactory` builds them through `ActivatorUtilities`, and a test asserts the logger it receives is the container's. The other seven still take `ILogger<T>? logger = null` and are still resolved by the container, which supplies one — so the remaining exposure is a future hand-construction, not a present hole |
| TD-105 | `DashboardViewModel` renders its failure sentence into `Greeting`, the screen's header text, because the view has no error surface | 🟢 | A failed load replaces "Welcome to Pine" with an error, and there is nowhere for the two to coexist. The message is correct; its placement is not | Give the dashboard view a bound error area, as `AddTransactionView` and the others already have | P3 | 🔴 **Open** |
| TD-106 | Nothing enforces the taxonomy: a new `catch (Exception ex) { ErrorMessage = ex.Message; }` added tomorrow compiles, passes every test, and reintroduces TD-24 | 🟡 | The eleven known sites are fixed; the *class* of defect is not closed, only currently absent. This is the same shape as the deny-list this ticket replaced, one level up | An architecture test (NetArchTest is already referenced) or an analyzer asserting no UI type formats an `Exception` into a bound property | P2 | 🔴 **Open** |

### 12.41 Next

- **TD-99 — make the budgets screen true or take it down.** Still the sharpest open row, and unchanged
  by this iteration for the third time in a row. That it keeps being deferred is itself worth noticing.
- **TD-106** — enforce what §12.40 built, before the next contributor undoes it by hand.
- **TD-94's lesson**, unchanged and still the largest: almost nothing is covered by a test that
  executes the query path a screen actually uses.
- **TD-07 — re-arm CI.** Now five iterations old, and every count in this document is a local claim
  because of it.
- **Phase 1 item 9** — packaging, code signing and an update channel (TD-49). The next roadmap row,
  and the first one in Phase 1 whose work is mostly *outside* the source tree.
- **TD-104**, **TD-101**, **TD-102**, **TD-105**, then **TD-103**.
- **TD-97 — the flaky suite**, diagnosed, not fixed.
- **TD-93**, **TD-89**, **TD-86**, then **TD-90 and TD-85**, **TD-83**, then **TD-78**, **TD-75**'s
  siblings **TD-95**, **TD-96**, **TD-100**, and **TD-81**, **TD-91**.
- **TD-84** last, unchanged.

### 12.42 Iteration 20 — Phase 1, item 9 (TD-49), partially

**Ticket:** Phase 1 item 9, "Packaging, code signing, update channel."

Three things, and only one of them could be finished honestly. What follows says which, and why the
other two stop where they stop rather than being simulated.

#### What was true at the start, read from the code rather than the register

| Claim | Verified against |
|---|---|
| **No project and no props file set any version** | `Directory.Build.props` was ten lines with no `Version*` property; a repo-wide grep for `<Version>`, `<VersionPrefix>`, `<AssemblyVersion>`, `<FileVersion>`, `<InformationalVersion>` over `**/*.csproj` and `**/*.props` returned nothing |
| So every build ever produced stamped `1.0.0` | `obj/**/AssemblyInfo.cs` on both configurations: `AssemblyInformationalVersionAttribute("1.0.0+9f42153820e4…")` |
| …and the resolver **discarded the only varying part** | `PineLogRecordFormatter.cs:348-350` trimmed everything after `+`, leaving the literal `1.0.0` — in the diagnostics panel and in the `version` field of every log record |
| No packaging artefact of any kind exists | `find` for `*.wxs`, `*.iss`, `*.nuspec`, `*.appxmanifest`, `*.desktop`, `Dockerfile`, `*.pubxml`, `installer/`, `packaging/`, `scripts/` → the only hit anywhere is `src/Pine.Ui.Ios/Info.plist` |
| No update or signing code exists | grep for `squirrel\|velopack\|clickonce\|authenticode\|notariz\|codesign\|signtool` across `src/` → zero. The one hit for "Squirrel" in the repo is a colour palette name in `STATUS.md` |
| No version tags exist | `git tag -l` → `sdd-backup-before-split`, and nothing matching `22-Release-Plan` §5's `0.Y.0-alpha.N` scheme |
| Mobile identity is still template text | `com.CompanyName.TempApp` (`Pine.Ui.Android.csproj:7`), `android:label="TempApp"`, `CFBundleIdentifier=companyName.TempApp`, `<title>TempApp.Browser</title>` |
| **An unconstrained publish emits 30 runtime-identifier folders** | Measured: `dotnet publish -c Release` on the Windows head → **1 906 312 177 bytes, 220 files, 30 RID folders** including `linux-loongarch64` and `linux-musl-s390x` |
| **CI would have shipped an application that cannot start its OCR** | `.gitattributes` puts the three ONNX models in LFS; no `actions/checkout` in `dotnet-ci.yml` sets `lfs: true`, so a runner gets 133-byte pointer files. Nothing in CI read the weights, so nothing noticed |

That last row is the finding worth keeping. It was latent rather than active: every existing job
compiles, and compiling does not open a model. The first thing in this repository that would ever
have needed the real bytes is the thing this ticket adds.

#### The version was a constant, and the code had a written reason for making it one

`ResolveApplicationVersion`'s remarks justified the trim: build metadata is "a repository detail",
and keeping it "would put a different `version` in every developer's log for the same release".

**The premise was never true.** There was no release version. The SDK default was `1.0.0+<sha>`, so
the trim did not remove noise from a signal — it removed the only signal, and left a string that was
identical for every build, on every machine, forever. The panel that §12.25 built specifically so
that its figures would be measured rather than asserted was, in this one field, measuring a
constant. The reasoning was sound and its input was wrong, which is a failure mode worth naming
because nothing about reading the comment would reveal it.

- **`Directory.Build.props`** now sets `VersionPrefix 0.1.0` / `VersionSuffix alpha.1`.
  `22-Release-Plan` §5 defines the Alpha channel as *"Unstable; data loss possible"* and tags it
  `0.Y.0-alpha.N`; that is a description of this software as this audit finds it, so it is what the
  version says. `1.0.0` claimed a stable release and the literal it replaced in the UI claimed
  `1.0.0-beta.1` — both fiction, both in the flattering direction.
- **`ApplicationVersionText.Shorten`** keeps the release version *and* twelve characters of commit.
  A developer build and a CI build of `0.1.0-alpha.1` are different binaries, and a bug report that
  cannot tell them apart is the failure, not the fix.
- **It is a type of its own because of what a test can and cannot see.** The other half of the
  resolution reads `Assembly.GetEntryAssembly()`, and under `dotnet test` that is the VSTest host,
  not Pine. A test asserting on the resolved value would be asserting on Microsoft's version number
  while appearing to assert on Pine's — worse than no test. The rule is extracted and covered
  exhaustively; the reflection is left uncovered and said so.
- **`pine --version`** is what closes that gap without pretending. It is the only end-to-end
  observation available without a GUI, and it was run: **`0.1.0-alpha.1+9f42153820e4`**.

#### `packaging/package.sh` — run, not written and assumed

|  | Before | After |
|---|---|---|
| publish | 1 906 312 177 B, 220 files, **30 RID folders** | 1 160 623 713 B, 318 files, **1 RID** |
| archive | did not exist | 712 756 461 B, sha256 `66ff99440e1eea349d56eb067769fe1496a1ff4c0ed2ffb5ad240878c6e9898b` |
| per-file checksums | did not exist | `SHA256SUMS`, **317 of 317 verified with `sha256sum -c`** |
| self-identification | impossible | the artefact reports `0.1.0-alpha.1+9f42153820e4` |

The script publishes the desktop head and the CLI together, pins the RID, drops the third-party
native `.pdb` files (105 MB, still present in the NuGet packages a native crash dump would consult),
**runs the published binary and refuses to continue if the artefact's own version disagrees with the
filename it is being given**, and writes a manifest in the shape `29-Deployment` §5.3 specifies.

Two departures from §5.3 are recorded inside the manifest rather than left for a reader to infer:
`signature` is `null` unless the artefact was really signed — §5.3 says a client MUST verify the
signature before fetching, and a placeholder would defeat that check by being present — and there is
no `url`, because nothing hosts these artefacts and a download address that 404s is worse than an
admitted absence. A `not_included` array names the missing halves in the artefact itself.

**Signing is a seam with two outcomes and no third.** No certificate exists for this project and a
script cannot conjure one. With `PINE_SIGNTOOL`/`PINE_SIGNING_CERT` set, every `.exe` and `.dll`
is signed — not just the entry point, because a signature on the executable says nothing about the
forty native libraries loaded beside it — and the manifest says `"signed": true`. Without them the
build is unsigned, says so loudly, records `"signed": false`, and `--require-signature` turns that
into a non-zero exit so a release pipeline can refuse without anyone remembering to check.

> **CORRECTION, 2026-09-12 (plan item 2.2).** The paragraph above is written as a measured outcome
> and it was not one. That branch had never been executed — no certificate exists, so nothing ever
> entered it — and when it was finally run it **could not sign at all**: under Git Bash, which is
> the shell this script and the CI `package` job both use, MSYS rewrote `/fd` into a path and
> signtool rejected the flag it had been given. "Every `.exe` and `.dll` is signed" was also a
> claim about intent rather than about a check; nothing verified afterwards that anything had been
> signed. Both are now true, and `packaging/sign.sh` proves the second one file by file. This
> correction is left in place rather than the paragraph rewritten, because the paragraph is the
> better record: it is what a careful write-up looks like when the code under it has never run.

#### Falsification mutations

**Seven on the version rule, 7 killed** — and one of them tells a story.

| # | Mutation | Result |
|---|---|---|
| 1 | `Shorten` reverts to the old trim, dropping the commit | 7 tests failed |
| 2 | The commit is not shortened (limit raised to 40) | 3 tests failed |
| 3 | A blank version becomes `""` instead of `"unknown"` | 3 tests failed |
| 4 | `plus <= 0` becomes `plus < 0` | **SURVIVED**, then killed — see below |
| 5 | The empty-metadata guard is dropped, leaving a dangling `+` | 1 test failed |
| 6 | Surrounding whitespace is not trimmed | 1 test failed |
| 7 | The dot rule is deleted | 1 test failed |

**Mutation 4 survived, and the reason is instructive.** For the input the test used, `"+abcdef"`,
the fall-through path rebuilds the *identical* string the guard returns — so the guard was not
untested, it was **unfalsifiable by that input**. The two branches only disagree when the metadata
contains a dot. Adding `"+abc.def"` made them disagree and killed the mutation. Mutation 7 had the
same shape one step earlier: with a 40-character SHA in front, truncating at twelve characters
produces the same answer whether or not the dot rule exists, so a test with a *short* commit
(`"1.2.3+abc.build.7"`) had to be added before the rule was covered at all. Two of the seven
mutations were therefore only killable after the tests were made sharper, which is the entire point
of running them.

**Three guards in the packaging script were verified by removing them and watching the outcome
change**, since a shell script is reached by no unit test:

| Guard | Removed | Restored |
|---|---|---|
| `--require-signature` refusal | script exits **0** with an unsigned artefact | exits **1** |
| artefact-vs-filename version agreement | (forced `VERSION=9.9.9-bogus`) | refuses, naming both strings |
| CI's LFS-pointer check | — | fires on a real 133-byte file, exits 1 |

#### Test counts

| | Before | After |
|---|---|---|
| `Pine.Tests.Unit` | 673 | **686** |
| `Pine.Tests.Integration` | 190 | **190** (untouched) |
| Total | 863 | **876** |

#### TD-97 observed a fourth time, and this run captured the name

The integration suite failed **once in four consecutive full runs**, then passed three times. The
failing test was `ConcurrencyTokenTests.TheStoredToken_ChangesOnEveryUpdate` — a **fourth** distinct
test, at the same roughly-one-run-in-four rate TD-97 already records. It is not a regression from
this iteration: the change to `ConcurrencyConflictException` adds a property and touches no write
path. That the failure keeps moving between tests is exactly the evidence for TD-97's diagnosis
(file contention between `TempVault` directories under xUnit's per-class parallelism) and against
any theory that names a particular assertion. **The suite figures above are from runs that passed;
this paragraph exists so that is not read as "the suite is green".**

#### Deliberately not done

- **No installer.** This produces a `.tar.gz`, not an `.msi`, `.dmg` or `.deb`. Calling an archive
  "packaging, done" would be the same class of claim this audit exists to remove, so TD-49 stays
  🟡 rather than ✅ and the manifest says so in the artefact itself.
- **No updater, and this one is a decision rather than a shortfall.** `src/` contains no
  `HttpClient`, `WebClient`, `WebRequest` or `Socket` — verified, zero hits. An in-app update check
  would be the first network call Pine has ever made, and the audit's stated *biggest strength* is
  "no network egress anywhere in the codebase". `29-Deployment` §5.1 already anticipates this by
  making the check opt-in and default-off, which is the right shape; building it is not a packaging
  chore, it is a change to the product's privacy posture and deserves its own ticket.
- **One runtime identifier only.** `OpenCvSharp4.runtime.win` and
  `Sdcb.PaddleInference.runtime.win64.mkl` are Windows-x64-only, so a `linux-x64` artefact would
  build, launch, and then be unable to do the thing the application is for. That is TD-44. The
  script accepts any `--rid`; it simply does not pretend the result is supported.
- **CI triggers were not re-armed.** The `package` job, like every other job, runs only on manual
  dispatch. TD-07 is a separate row and the disarming was a written decision, not an oversight to
  quietly reverse inside a packaging ticket.
- **`61-Manual-Test-Plan` was not updated**, for the second iteration running.
- **Nothing was seen running except the CLI.** Sixth consecutive iteration to say so. The Windows
  GUI head was published, not launched.

#### New debt

| ID | Row | Sev | Impact | Fix | Pri | Status |
|---|---|---|---|---|---|---|
| TD-107 | `packaging/package.sh` is reached by no test. Its three guards were proven load-bearing by hand this iteration, and nothing re-proves them | 🟡 | The one artefact users would ever install is produced by a script that cannot break a build. A future edit that drops the version-agreement check or the signature gate is invisible | Port the guards into a test that invokes the script against a stub publish directory, or reimplement packaging as a `dotnet` tool the unit suite can call | P2 | 🔴 **Open** |
| TD-108 | The pipeline's normal outcome is an **unsigned** build: no certificate exists, so CI deliberately does not pass `--require-signature` | 🟠 | SmartScreen warns on first launch, and a user cannot distinguish the project's artefact from one a third party built and modified. The seam exists; the property does not hold | Obtain a code-signing certificate, then add `--require-signature` to the CI job so an unsigned build can never be published by accident | P1 | 🟡 **Partial** — 2026-09-12, plan item 2.2. Everything but the certificate: the certificate and `--require-signature` are now one decision in the workflow, so a broken certificate fails the job instead of degrading quietly to an unsigned build under the release name; an unsigned build is uploaded as `pine-win-x64-UNSIGNED` and says so in its manifest. **And the seam did not work**: it had never been executed in twenty-four iterations, and under Git Bash — which is the shell the `package` job uses — MSYS rewrote `/fd` into a path, so signtool rejected the flag it had been given. It signs, and verifies what it signed, and `packaging/selftest-signing.sh` falsifies each refusal in CI on every push. **The row stays open**: a certificate is a purchase against a verified legal identity, and no artefact this pipeline produces today is signed |
| TD-109 | Only the Windows head can be packaged at all. macOS/Linux have no bundle definition, and Android/iOS still carry template identity — `com.CompanyName.TempApp`, `android:label="TempApp"`, `CFBundleIdentifier=companyName.TempApp` | 🟠 | The README claims Pine "runs natively on Windows, macOS, Linux, iOS, Android, and WebAssembly". Four of those six cannot be built into anything installable, and two of them do not yet have a name | Give each head a real application identity, then a bundle per platform — or narrow the README, which is the Phase 3 decision | P1 | 🟡 **Partial** — iteration 31, §12.64, in the order this row prescribes. **Identity is done**: `org.pine.app` and display name `Pine` on both mobile heads, `ApplicationDisplayVersion` tracking `$(VersionPrefix)` rather than a `1.0` this project never had, and every `TempApp` string gone from the manifest, the activity attribute, `Info.plist` and the iOS launch screen. It was the half that could become unfixable — an Android application id cannot be changed after a first publish without shipping a different application. **The bundle half is untouched**: no `.apk`, `.ipa`, `.app` or `.deb` definition exists and `packaging/package.sh` still builds win-x64 only. The README half is done and went further than narrowing — the sixth platform was deleted rather than de-claimed |
| TD-110 | The artefact is **1.08 GiB unpacked / 680 MB compressed**, of which 383 MB is three overlapping PaddleOCR model packages and 204 MB is the ONNX set | 🟡 | This is TD-41 with a measurement attached. A ~700 MB download for a personal-finance application is a distribution problem, not just a tidiness one | TD-41: keep one PaddleOCR package. Then reconsider bundling the ONNX weights at all versus fetching them on first use — which would need the network egress decision above | P2 | 🟡 **Partial** — iteration 28 (§12.58) took the measurement down to **690,067,133 bytes unpacked (658 MiB) / 306,385,892 bytes compressed (292 MiB)**, by running `package.sh` again rather than estimating. The download roughly halved. **The second half of the fix is untouched**: the remaining ~292 MiB still bundles PP-OCRv5 weights that could be fetched on first use, and that decision still runs into ADR-0012 |

### 12.43 Next

- **Phase 1 item 10 — reconcile the docs (TD-39, TD-40).** The last row in Phase 1, and this
  iteration sharpened the case for it: `53-Release-Checklist` requires sign-off from a Trust team and
  a Maintainer Council that do not exist, `28-CI-CD` describes a sign-and-attest stage that does not
  exist, and `29-Deployment` lists six distribution channels of which zero exist. All four documents
  are `Status: Draft — pending review`, dated 2026-07-08, and none has been revised since.
- **TD-99 — make the budgets screen true or take it down.** Still the sharpest open row, deferred a
  fourth time. The deferral is now itself worth reading as a signal.
- **TD-108** — a certificate, so that the signing seam built here has something to hold.
- **TD-106** — enforce the exception taxonomy §12.40 built, before it is undone by hand.
- **TD-94's lesson**, unchanged and still the largest.
- **TD-07 — re-arm CI.** Six iterations old, and every count in this document remains a local claim.
- **TD-97 — the flaky suite.** Now observed four times in four different tests; the diagnosis has
  not changed and neither has the code.
- **TD-104**, **TD-107**, **TD-101**, **TD-102**, **TD-105**, then **TD-103**.
- **TD-110/TD-41**, **TD-109**, **TD-93**, **TD-89**, **TD-86**, then **TD-90 and TD-85**, **TD-83**,
  then **TD-78**, **TD-75**'s siblings **TD-95**, **TD-96**, **TD-100**, and **TD-81**, **TD-91**.
- **TD-84** last, unchanged.

### 12.44 Iteration 21 — Phase 1, item 10 (TD-39, TD-40), and Phase 1 closes

**Ticket:** Phase 1 item 10, "Reconcile the docs: mark every unimplemented spec
`Status: Aspirational`; write ADR-0012/0013 or drop the citations."

This iteration began with the work already done. Commit `6b7e7f0` had classified all 62 documents
in `docs/`, written both ADRs and added `DocumentStatusTests` — and had said so in its own message,
including the line *"Not done: the audit's 12.44 entry for item 10 is not written, so its Phase 1
checkbox and the TD-39/TD-40 rows still read as open."*

So the first question was not "what shall I build" but **"is any of that true?"** — which is the
question §12 exists to ask, and the one an iteration that inherits a green suite is most likely to
skip. Two independent agents were sent to check the claims against the code with instructions to
report what they could not verify rather than assume it; every finding either of them returned was
then re-derived by hand here before it was acted on. **The claims held. Five things underneath them
did not**, and those became the iteration.

#### What was verified, and how

| Claim from `6b7e7f0` | Verified by | Result |
|---|---|---|
| 691 unit + 190 integration, both green | Both suites run from a clean invocation on this machine | **True.** 691 / 190, zero failures |
| All 62 documents in `docs/` carry a status and a `Reconciled` line | Header table parsed out of all 62 files, twice, by two different readers | **True.** Zero missing, zero outside the vocabulary, zero "Draft — pending review" |
| The distribution is 25 Aspirational / 19 Non-normative / 17 Partially implemented | Same parse | **Off by one.** It is **20** Non-normative, not 19 — 25 + 20 + 17 = 62. The error was in the commit's prose, not in the documents, and not in a direction that overstates implementation |
| **Zero documents are classified `Implemented`** | Same parse | **True, and it is the most useful single number in this section.** After twenty iterations, not one specification fully matches the code |
| `DocumentStatusTests` enforces this | File read in full; the five tests run | **True, and honest about its limits in its own comments**: it enforces that a classification is *present and from a closed set*, not that it is *accurate*. No test can do the second |
| ADR-0013's parameters match the code | Argon2id parameters, cipher, nonce and tag sizes, header binding, version floor, password-change path, each traced to `src/…` | **True.** Every stated value agrees |
| ADR-0012's "no network egress anywhere" | `HttpClient`, `WebClient`, `Socket`, `WebSocket`, `System.Net.*`, URL literals and every `PackageReference` in all 14 projects | **True.** Zero hits |
| `pine --version` resolves a real version | Run: `dotnet run --project src/Pine.Cli -- --version` | **True.** `0.1.0-alpha.1+6b7e7f0db86a` — and note the commit suffix changed with the commit, which is what iteration 20's fix was for |

#### The five things underneath, in the order they matter

**1. `STATUS.md` was never reconciled, and it is the document §6.12 names twice.** The pass covered
`docs/`. `STATUS.md` is at the repository root, and it is the file the audit's own contradiction
table cites for *"Diagnostics has real-time log tailing, environment metrics, DIB export"* —
**Inaccurate** — and for *"Phase 1 and Phase 2 ✅ 100% Completed"* — **Overstated**. It had not been
edited since iteration 13. It still named Phase 1 item 4 as the next row, still reported a test
suite of **542** against an actual 891, and still said "Phase 1: items 1–3 are ✅" seven items later.
The document the project calls its SSoT was the most out-of-date file in the repository, and the
step built to prevent exactly that could not see it.

`README.md` was worse in kind if not in degree, because it is read by people who have not yet
decided to trust anything: it claimed Pine "runs natively on Windows, macOS, Linux, iOS, Android,
and WebAssembly" — four of those six cannot be built into anything installable and one of them
cannot open a vault at all — and described the encryption as "military-grade" and the architecture
as "Zero-Knowledge", a term with a specific cryptographic meaning that this is not.

**2. ADR-0012's citations were wrong in the commit that wrote them.** The ADR cited its seven
callers by line number: `30-Observability:22,251,296`, `29-Deployment:249,277`, `32-AI-Training:317`,
`38-Threat-Model:176,187`. Every one of the eight is off by **exactly one**, because the
`Reconciled` row that the same commit inserted into each document's header pushed every line below
it down by one. The citations were not stale; they were **born stale, by an edit made minutes later
in the same change**. This is the sharpest small lesson of the iteration: a line number is a
citation of a moving target, and the thing that moved it was the reconciliation itself.

**3. ADR-0013 was silent about the second encryption layer.** It documents SQLCipher, Argon2id and
the AES-256-GCM key envelope correctly and completely — and says nothing at all about
`AesGcmVaultFileCipher`, which seals standalone vault files (today the ML categoriser's model) with
a *second* AES-256-GCM under a key derived from the data key by **HKDF-SHA256** with
`info = "pine:file-cipher:v1:" + purpose`. Nothing in the ADR was false. A reader of the encryption
baseline would simply not have learned that a whole second AEAD mechanism exists, which for a
document whose one job is to be the encryption baseline is a real omission.

**4. `ADR-0001` declared no status in any form a rule could read.** It was the only ADR in the
repository for eight months, and it never followed the front matter `25-ADR` §4.3 makes
**mandatory** — it carried the plain Nygard layout instead. `INDEX.md` had noticed the mismatch and
argued for leaving it, "because an ADR is meant to be an immutable record". The argument does not
survive contact: what is immutable is the decision, its date and its status, all three of which are
unchanged; the front matter is metadata the project's own specification requires, and a status
nothing can read is a status that will drift unobserved. It was found by a test written to check
the *other two* ADRs.

**5. `18-Test-Strategy` cited 686 unit tests against 691.** Stale by five, inside its own commit,
in the safe direction. Trivial on its own; worth recording because it is the same failure as (2) at
a smaller scale — a document reconciled against a tree that was still moving.

#### What was built

The remedy for item 10 was never editing; §6.12 said so itself in the sentence after the one that
prescribed an afternoon of it. Iteration 20 built the workflow step for `docs/`. This iteration
extended it to what the step could not see, and turned the one claim in ADR-0012 that mattered most
into something the build holds.

| Built | What it does | Why here |
|---|---|---|
| `EveryRootDocumentDeclaresAStatusAndWhenItWasReconciled` | `README.md`, `STATUS.md`, `PRODUCT.md` and `CHANGELOG.md` must carry a status from the same closed vocabulary and a `Reconciled` date | Finding 1. The documents with the widest audience were outside the only rule that had been built |
| `TheDocumentSetHasNotShrunk` | A floor of 62 documents, lowered only in a commit that says which was deleted and why | Every other assertion has the form "no document violates X". Deleting a document satisfies all of them |
| `EveryAdrDeclaresAStatusFromTheAdrVocabulary` | Every ADR's status must be Proposed / Accepted / Rejected / Deprecated / Superseded | Finding 4. `docs/adr/` sits outside the docs scan, so it had no check at all |
| `EveryAdrQuotationStillAppearsInTheDocumentItCites` | ADRs cite by **quotation**, and the quoted sentence must still exist in the document named | Finding 2. A quotation survives insertions above it and fails precisely when the sentence it points at is edited away |
| `NetworkEgressTests` (3 tests) | No file under `src/` may name a networking type; no project may reference a networking or telemetry package; and the scan must actually be walking the tree | ADR-0012's **own risk table** recorded this gap: *"A future dependency introduces network I/O unnoticed — currently only a grep would catch it. There is no test asserting the absence."* That row is now closed |

And the documents themselves: `STATUS.md` rewritten from the remediation log with both §6.12 rows
corrected in place and the "100% Completed" claims re-stated as *delivered against the plan*, with
the debt each tick concealed named beside it; `README.md` rewritten to separate "what is actually
true today" from "what is not true yet"; `PRODUCT.md`'s platform and feature claims narrowed to what
exists; `CHANGELOG.md` extended across the seven iterations of user-visible change it had missed;
ADR-0013 given the file-cipher layer; ADR-0001 given the mandated front matter; ADR-0012's citations
converted to quotations; `INDEX.md`'s format note rewritten to say why its earlier reasoning was
overturned.

#### Falsification

Eleven mutations, each breaking exactly one property, each expected to redden one **named** test.
The harness snapshots file bytes rather than restoring with `git checkout --`, because every file
these mutations touch had uncommitted work in it and restoring from the index would have deleted
the iteration silently — a trap worth writing down, since the obvious harness is the destructive one.

| # | Mutation | Expected to die by | Result |
|---|---|---|---|
| M1 | `README.md` loses its `Status` row | `EveryRootDocumentDeclaresAStatus…` | **Killed** |
| M2 | `STATUS.md` reverts to "Draft — pending review" | `EveryRootDocumentDeclaresAStatus…` | **Killed** |
| M3 | `CHANGELOG.md` loses its `Reconciled` row | `EveryRootDocumentDeclaresAStatus…` | **Killed** |
| M4 | Five documents are deleted from `docs/` | `TheDocumentSetHasNotShrunk` | **Killed** |
| M5 | A sentence ADR-0012 quotes is edited away in `29-Deployment` | `EveryAdrQuotationStillAppears…` | **Killed** |
| M6 | ADR-0013 takes the status "Draft" | `EveryAdrDeclaresAStatusFromTheAdrVocabulary` | **Killed** |
| M7 | `Pine.Cli/Program.cs` gains `using System.Net.Http;` | `NoSourceFileNamesANetworkingType` | **Killed** |
| M8 | `Pine.Infrastructure` references `Microsoft.Extensions.Http` | `NoProjectReferencesANetworkingOrTelemetryPackage` | **Killed** |
| M9 | The egress scan is pointed at one project instead of `src/` | `TheScanActuallyReadsTheSourceTree` | **Killed** |
| M10 | ADR-0012 quotes a document that does not exist | `EveryAdrQuotationStillAppears…` | **Killed** |
| M11 | ADR-0012's `## Citations` section is deleted | `EveryAdrQuotationStillAppears…` | **Killed** |

**11 of 11 killed.** M9 and M11 are the two that matter: they are the mutations that make a passing
test pass *vacuously* rather than falsely, which is the failure mode this suite is most exposed to
and the one a mutation set usually forgets to include.

#### Measured, not asserted

| | Before | After |
|---|---|---|
| Unit tests | 691 | **701** |
| Integration tests | 190 | **190** |
| Documents in `docs/` carrying a status | 62 of 62 | 62 of 62 |
| Root documents carrying a status | **0 of 4** | **4 of 4** |
| ADRs whose status a rule can read | **2 of 3** | **3 of 3** |
| Line-number citations from an ADR into a document | 8, **all wrong** | 0 |
| Tests asserting Pine performs no network I/O | **0** | 3 |

Both suites green at this commit.

#### Phase 1 is closed, and what that does and does not mean

All ten rows have been worked. Eight are ✅; **5a** could not build the index it specified and found
a shipped crash instead (TD-94), and **9** produced a version and an artefact but no installer, no
certificate and no updater (TD-107–TD-110). What Phase 1 bought is real: a write path that is
transactional, money that is exact, concurrency that is detected, logs that exist and are redacted,
encryption with a key envelope, errors that do not leak, a version that varies, and now a document
set that says what it is.

What it does not mean is that the numbers in this section are trustworthy off this machine.
**TD-07 is still open six iterations after it was first flagged as the largest single risk**, and
every count in every §12 entry — including this one — was measured by one person on one Windows
machine, with the Android and iOS heads compiled by nobody. The audit's §11 verdict stands
unchanged.

#### New debt

| ID | Row | Sev | Impact | Fix | Pri | Status |
|---|---|---|---|---|---|---|
| TD-111 | `NetworkEgressTests` is a **source-text scan**, and three of Pine's dependencies are unaudited native code | 🟡 | The test raises the cost of *adding* egress from zero to "delete a test that says why it exists", which is what a text scan can do. It cannot see a socket opened by PaddleInference, ONNX Runtime or OpenCV, nor a type name assembled from string fragments at run time. ADR-0012's claim is therefore enforced for Pine's own code and merely *believed* for the ~700 MB of native libraries it ships | Run the packaged application under a network-namespace or firewall deny-all and assert it still works — a manual row in `61-Manual-Test-Plan` at minimum, and a CI job once TD-07 is closed | P2 | 🔴 **Open** |
| TD-112 | Four root documents are inside the status rule; `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `TRADEMARK.md`, `GEMINI.md` and everything under `docs/superpowers/` are not | 🟡 | `CONTRIBUTING.md` is eight lines describing a contribution process that `24-Contributing` is marked **Aspirational** for not having, and it is the file a would-be contributor opens second | Either bring them into the rule or state, in one place, why a document is exempt from it. An unstated exemption is how the docs-only scope of iteration 20 came to miss `STATUS.md` | P3 | 🔴 **Open** |
| TD-113 | The classification records that a document **has been classified**, not that the classification is **true** | 🟠 | This is the honest residue of TD-39 and it should not be lost in the tick. Seventeen documents say "Partially implemented — *these* clauses hold"; the clause lists are prose and nothing checks them. `25-ADR` §11 still seeds twenty ADRs and marks all twenty **Accepted** while seventeen have no file, so the register still contradicts the directory. `19-Traceability-Matrix` is still not generated from anything | Generate the traceability matrix from the code and the test names, which is the only one of these that a machine can decide; for the rest, re-verify a document's clause list whenever the subsystem it describes is touched, and record the date | P2 | 🔴 **Open** |

### 12.45 Next

Phase 1 is closed, so for the first time since iteration 1 the next row is not chosen by the
roadmap. Two of the standing items now outrank the top of Phase 2, and they are named first for
that reason.

- **TD-07 — re-arm CI.** Six iterations old, still the largest single risk, and now the limiting
  factor on the value of everything §12 records: 891 tests, twenty-one iterations and every
  measurement in this document exist on one Windows machine. `.github/workflows/dotnet-ci.yml` is
  manual-dispatch by decision and the decision is written down; what is not defensible is that
  Android and iOS have now been edited blind for a dozen iterations. `pull_request: branches:
  [main]` alone would restore the net where it matters most.
- **TD-99 — make the budgets screen true or take it down.** Deferred a fifth time, and the deferral
  has become the signal: it is a shipped screen whose progress bar is hard-coded to zero, which
  means a user can blow a budget wide open and be told nothing. It is now named in `STATUS.md`,
  `PRODUCT.md` and this section, which is three documents describing a defect instead of one commit
  fixing it.
- **Phase 2 item 1 — one shared `AddPineApplication()` / `AddPineInfrastructure()` (TD-29).** The
  first Phase 2 row, and the one that dissolves a family: TD-85 and TD-90 are both "registered in
  one head of five", which is what five hand-written composition roots produce.
- **TD-113** — the residue this iteration registered rather than papered over: a document that has
  been classified is not a document that is accurate.
- **TD-108** — a certificate, so the signing seam iteration 20 built has something to hold.
- **TD-106** — enforce the exception taxonomy before it is undone by hand.
- **TD-97 — the flaky suite.** Four observations in four different tests, unchanged diagnosis,
  unchanged code.
- **TD-94's lesson**, unchanged and still the largest.
- **TD-111**, **TD-104**, **TD-107**, **TD-101**, **TD-102**, **TD-105**, then **TD-103**.
- **TD-110/TD-41**, **TD-109**, **TD-93**, **TD-89**, **TD-86**, then **TD-90 and TD-85**, **TD-83**,
  then **TD-78**, **TD-75**'s siblings **TD-95**, **TD-96**, **TD-100**, **TD-81**, **TD-91**, and
  **TD-112**.
- **TD-84** last, unchanged.

### 12.46 Iteration 22 — Phase 2, item 1 (TD-29), and the family it dissolved

**Ticket:** Phase 2 item 1, "One shared `AddPineApplication()` / `AddPineInfrastructure()`; delete
the duplicated roots."

§12.45 named this row as the one that "dissolves a family", on the grounds that TD-85 and TD-90 are
both *"registered in one head of five"*, which is what five hand-written composition roots produce.
That turned out to be an understatement rather than a prediction. Six rows closed here, and none of
them was worked directly: each one is a symptom of the same cause, and removing the cause removed
all six at once.

#### What was actually wrong, restated from the code

The five roots were **not five platform-specific configurations**. They were five copies of one
configuration, each edited at a different time by whoever was closing a different ticket, and the
diff between them was the accumulated record of which edits had been forgotten where.

| Decision | Windows | macOS | Web | Android | iOS |
|---|---|---|---|---|---|
| `PineDbContext` lifetime | **Transient** | Scoped | Scoped | Scoped | Scoped |
| `ShellViewModel` lifetime | **Transient** | Singleton | Singleton | Singleton | Singleton |
| `IAIProvider` | Registered | — | — | — | — |
| `TimeProvider` | Registered | Registered | Added by TD-11 | Added by TD-11 | Added by TD-11 |
| Nav targets registered | 7 of 7 | 3 of 7 | 4 of 7 | 3 of 7 | 3 of 7 |
| Hosted services started | Hosted-service loop | Resolve the watchdog | Resolve the watchdog | Resolve the watchdog | Resolve the watchdog |

Not one row of that table is a platform difference. The transient `PineDbContext` on Windows is the
sharpest: it is **TD-01 re-entering through the composition root**, because a transient context
gives each repository in one request its own, and the unit of work then commits a transaction that
the other contexts' changes were never part of. TD-01 was closed in iteration 1 and the Windows head
had been quietly outside it ever since.

#### The change that made a shared root possible

The obstacle was one lambda. Every head registered `PineDbContext` with a factory that resolved
`Pine.Application.Security.IVaultSession` — and `Pine.Infrastructure` cannot see `Pine.Application`,
so the factory could not live in the layer that owns the type it builds. That is the whole reason it
was written five times.

`IVaultDataKeyAccessor` already carried `IsUnlocked`, `Key` and `DbPath`; it already lived in
`Pine.Core`; and `AddPineApplication` already bound it to the same session object (TD-16 put it
there, for the categoriser). Pointing the factory at it instead is a one-line change that is
identical in behaviour and legal in the layer. Everything else followed from it.

#### What was built

| Built | Where | What it does |
|---|---|---|
| `AddPineInfrastructure()` | `Pine.Infrastructure/DependencyInjection/` | Persistence, vault cryptography, logging, diagnostics and the AI subsystem, in one call |
| `PineInfrastructureOptions` | same | The only per-platform decision left: `IncludeReceiptVision`. It defaults to **false**, deliberately — forgetting to turn it on is a catchable exception, forgetting to turn it off is a native abort |
| `PineCompositionRoot` | `Pine.Ui.Avalonia/Composition/` | `AddPine(profile)`, `Build(profile)` and `Start(provider)`. The whole container, for any head |
| `PineHeadProfile` / `PineHeadCapabilities` | same | The per-head page inventory TD-85 asked for, as one readable table |
| `PortableAiProvider` | `Pine.Infrastructure/Ai/` | Categorises; refuses the four vision methods by name |
| `AiCapabilityUnavailableException` | `Pine.Core/Ai/` | Typed, on `IUserFacingFailure`, with a fixed sentence that names no package |

The five heads now read the same way: initialise whatever native provider the platform needs, name
themselves, hand the provider to `App.InitializeServices`, start the hosted services, run Avalonia.
**127–183 lines each became 27–60, with zero registrations.**

#### Three decisions worth defending

**1. TD-90 was not fixed the way its row said to fix it.** The row prescribed calling
`AddPineCategorisation()` per head. That registers `DefaultLocalAiProvider`, whose constructor builds
a `ReceiptDetector`, a `PaddleOcrEngine` and an ONNX extractor — and the only native runtime packages
`Pine.Infrastructure` references are `OpenCvSharp4.runtime.win` and
`Sdcb.PaddleInference.runtime.win64.mkl`, which carry **win-x64 assets and no others**. Following the
row would have moved the failure from a managed exception at handler resolution to a P/Invoke into a
library that was never restored, which is worse in every way that matters. `IAIProvider` bundles two
subsystems with very different portability, and splitting them at the provider is the honest cut:
categorisation is ML.NET over text the vault already holds and works everywhere; the receipt
pipeline does not exist off Windows and now says so.

**2. macOS is a desktop head and still has no vision, which is the surprising row in the capability
table.** It is stated in the table's own comments because it will otherwise read as an oversight. It
is a reading of the package graph, not a decision to withhold a feature: there is no
`OpenCvSharp4.runtime.osx` and no Paddle equivalent in this solution. What macOS gained instead is
everything else — it was missing four of seven pages and the entire AI subsystem.

**3. `ValidateOnBuild` is now on, and it was off in all five roots for one reason.** Each of them
registered `AddTransactionViewModel`, whose constructor takes an `Action onComplete` no container can
supply, so build-time validation aborted startup on a registration that could never have been
resolved. `TransactionsViewModel` constructs that view model by hand and always did — the
registration was dead. Deleting it cost nothing and bought the flag, which is what turns "this head
is missing a service" from a crash on the screen that needs it into a failure at startup, in front
of a test.

#### Closing TD-83, and being honest about the instrument

Everything above is testable because the shared root lives in `Pine.Ui.Avalonia`, which the test
projects reference. That is deliberate and it is the point: three of the five heads target
`net10.0-android`, `net10.0-ios` and `net10.0-browser`, so a `net10.0` test project could not load
them even if it wanted to. TD-83's worked example — *"deleting the call from `Pine.Ui.Web/Program.cs`
would leave 367 tests green"* — was a statement about that unreachability.

`PineCompositionRootTests` builds the real container for all five profiles and interrogates it. But
building the shared root correctly proves nothing about whether a head **calls** it, so one test
reads the five head source files off disk and fails if any of them omits
`PineCompositionRoot.Build(<its own profile>)` or names `new ServiceCollection`, `AddSingleton`,
`AddScoped`, `AddTransient` or `BuildServiceProvider`. It is a text scan, it does not understand the
code, and it is recorded as such — it is the only instrument the target framework allows short of a
per-head test project on a per-head runner, which needs TD-07 first.

#### Falsification

Ten mutations, each breaking exactly one property, each expected to redden one **named** test.

| # | Mutation | Expected to die by | Result |
|---|---|---|---|
| M1 | The capability table gives Windows no vision | `TheCapabilityTable_ShouldSayWhatItSays` | **Killed** — 1 test |
| M2 | `AddPineLogging` dropped from `AddPineInfrastructure` | `TheCrossCuttingSeams_ShouldReachEveryHead` | **Killed** — 41 |
| M3 | `ShellViewModel` back to singleton | `TheShell_ShouldBeTransient_OnEveryHead` | **Killed** — 5 |
| M4 | `PineDbContext` back to transient | `ThePersistenceGraph_ShouldBeScoped_OnEveryHead` | **Killed** — 5 |
| M5 | `PortableAiProvider` not registered | `Build_ShouldProduceAClosedGraph_ForEveryHead` | **Killed** — 33 |
| M6 | The Web head stops starting hosted services | `EveryHead_ShouldBuildItsContainer_ThroughTheSharedRootAndNothingElse` | **Killed** — 1 |
| M7 | The dead `AddTransactionViewModel` registration restored | `Build_ShouldProduceAClosedGraph_ForEveryHead` | **Killed** — 41 |
| M8 | The portable provider returns an empty receipt instead of refusing | `PortableAiProvider_ShouldRefuseVision_WithAUserFacingSentence` | **Killed** — 1 |
| M9 | The locked-vault guard removed from the DbContext factory | `TheDbContext_ShouldRefuseToResolve_WhileTheVaultIsLocked` | **Killed** — 5 |
| M10 | The receipt view models registered on every head | `Build_ShouldProduceAClosedGraph_ForEveryHead` | **Killed** — 33 |

**10 of 10 killed.**

**M1 is the one that exists because the suite was nearly circular.** Every other assertion in the
file asks `PineHeadCapabilities.For` what to expect and then checks the container agrees — which
proves the two halves are consistent and would go on passing if someone flipped Windows to
no-vision, because both halves would move together. `TheCapabilityTable_ShouldSayWhatItSays` writes
the five rows out by hand for that reason, and M1 is the mutation that proves it works.

**A methodological failure was found in the harness itself, and it is worth recording.** The first
run of all ten mutations produced nonsense — M6, which edits a file the test project does not
compile, appeared to redden 39 tests. The restore step used `shutil.copy`, which does not preserve
mtime, so a restored file was **older** than the build output and MSBuild skipped the rebuild: each
run was partly measuring the *previous* mutation. The whole set was re-run with a forced
recompilation, and the table above is that second run. The first numbers were discarded, not
reconciled. The general shape — *a green or red result from a build that was never made* — is the
same class of error as TD-74, where an integration project stopped compiling and reported no colour
at all for two iterations.

#### Measured, not asserted

| | Before | After |
|---|---|---|
| Unit tests | 701 | **762** |
| Integration tests | 190 | **190** |
| Lines in the five composition roots | 733 | **206** |
| Registration calls in the five heads | ~120 | **0** |
| Heads whose container a test can build | **0 of 5** | **5 of 5** |
| Heads registering all their nav targets | 1 of 5 | **5 of 5** |
| Heads that can construct `ImportTransactionsCommandHandler` | 1 of 5 | **5 of 5** |
| Heads with `ValidateOnBuild` enabled | **0 of 5** | **5 of 5** |

Both suites green at this commit.

#### Explicitly NOT done

1. **Android and iOS were not compiled.** The workloads are not installed on this machine, so the
   build dies at workload resolution and the C# compiler is never invoked — unchanged since §12.3
   item 3, and unchanged for the same reason. What *did* change is the size of the blind spot: those
   two files went from ~128 lines carrying ~25 registrations each to ~30 lines carrying none, and
   everything they used to declare is now compiled and tested as part of `Pine.Ui.Avalonia`. The
   remaining unverified surface is a base-class override signature and one `using`.
2. **The Browser head still cannot open a vault.** `AesGcm.IsSupported` is false on WASM (TD-88) and
   nothing here changes that. What was removed is TD-53's false comment — the head claimed an
   in-memory database for WebAssembly and constructed a keyed SQLCipher context against `":memory:"`
   anyway. It now builds the same context every head builds, and the lie is gone rather than the
   defect.
3. **`Pine.Cli` was not brought in.** It builds no container at all, so there was nothing to share.
4. **TD-97 was observed a fifth time.** Two `ScaledMoneyMigrationTests` cases failed in one full
   integration run and passed both in isolation and on re-run. Unchanged diagnosis, unchanged code,
   fifth observation in a fifth context.
5. **Nothing was committed.** All work sits in the working tree.

#### New debt

| ID | Row | Sev | Impact | Fix | Pri | Status |
|---|---|---|---|---|---|---|
| TD-114 | The head-source scan is the only thing tying a head to the shared root, and it is a text match | 🟡 | `EveryHead_ShouldBuildItsContainer_ThroughTheSharedRootAndNothingElse` reads five files and greps them. It catches the realistic regression — someone re-growing a private `ServiceCollection` — and cannot catch a head that calls `Build` and then discards the provider, or that registers services through a helper named something else. It is the strongest instrument available while three heads are unloadable from a `net10.0` test project | A per-head smoke test project on a per-head runner, which needs TD-07 closed and the Android/iOS workloads installed in CI | P2 | 🔴 **Open** |
| TD-115 | `PineHeadCapabilities` states what each platform can run; nothing verifies the statement | 🟠 | The table says Windows has receipt vision and the other four do not, and that is a claim about which native assets a published artefact contains. It was derived by reading `Pine.Infrastructure.csproj` — `OpenCvSharp4.runtime.win`, `Sdcb.PaddleInference.runtime.win64.mkl`, no osx/android/ios/browser equivalents — and by observing that only the Windows head copies `Assets/Ai/*.onnx`. If a runtime package is added for another RID, the table silently becomes wrong in the direction that withholds a working feature; if the win packages are removed, it becomes wrong in the direction that promises one | Assert the capability table against the restored asset graph, or against the published output of `packaging/package.sh` per RID | P2 | 🟡 **Partial** — iteration 31, §12.64, and **this row named a drift that had already happened**. `TheOnlyRidPinnedNativeRuntimes_AreTheWindowsOnes_WhichIsWhatTheCapabilityTableClaims` reads `Directory.Packages.props` and fails if any RID-pinned native runtime other than the two win ones appears — exactly the "a runtime package is added for another RID" case this row describes, verified by adding `OpenCvSharp4.runtime.osx.arm64` and watching it go red. Separately, **half of the table's stated reasoning was false**: the Windows arm claimed to be "the one head whose `Assets/Ai/*.onnx` models are copied to the output", and the model is a `<Content>` item in `Pine.Infrastructure.csproj` that MSBuild copies into EVERY head's output — confirmed by listing three heads' build folders. That is now corrected in the source and pinned by `TheModelAsset_IsShippedByTheSharedProject_AndByNoHead`. **What remains is the row's second option**: asserting against a published artefact per RID, which needs the runner TD-44 needs |
| TD-116 | `PortableAiProvider` refuses vision, and no UI path yet catches the refusal by type | 🟡 | The exception is on `IUserFacingFailure`, so `UserFacingError` renders its sentence wherever a view model already routes through it. But the receipt pages are not registered on the heads that would throw, so the only reachable caller today is `ScanReceiptCommandHandler` via a code path no head can start — which means the sentence is written and never read. That is the correct state and an untested one | Once a head without vision gains any receipt entry point — an import that runs OCR, a shared-file handler — assert the sentence reaches a screen | P3 | 🔴 **Open** |

### 12.47 Next

Phase 2 item 1 closed six rows, so the next choice is again not purely the roadmap's. The two
standing items from §12.45 are unchanged and still outrank the next Phase 2 row.

- **TD-07 — re-arm CI.** Seven iterations old, and this iteration made it *more* urgent rather than
  less: Android and iOS have now been edited blind for thirteen iterations, and the argument for
  `PineCompositionRoot` — that the heads are trivial and therefore safe to edit unseen — is exactly
  the kind of argument that should be checked by a compiler rather than believed. `pull_request:
  branches: [main]` alone would restore the net.
- **TD-99 — make the budgets screen true or take it down.** Deferred a sixth time. A shipped screen
  whose progress bar is hard-coded to zero, named in three documents and fixed in none.
- **Phase 2 item 2 — `IOptions` configuration (TD-27).** The natural successor:
  `AddPineInfrastructure` now has exactly one options object with one property, which is the seam a
  real configuration system plugs into. Thresholds, paths and KDF parameters are still literals.
- **Phase 2 item 3 — typed view-model factories, and an architecture test asserting UI ↛
  Infrastructure (TD-28).** Note that this row is now in tension with what was just built:
  `Pine.Ui.Avalonia` references `Pine.Infrastructure` *by design*, because that is where
  `PineCompositionRoot` calls `AddPineInfrastructure`. The rule that test asserts will have to be
  scoped to the view models rather than to the assembly, or the composition root will have to move
  again. That is a decision to make deliberately, not to discover when the test is written.
- **TD-115** — the capability table is a claim about native assets that nothing checks.
- **TD-113**, **TD-114**, **TD-108**, **TD-106**, **TD-111**, **TD-104**, **TD-107**, **TD-101**,
  **TD-102**, **TD-105**, then **TD-103**.
- **TD-97 — the flaky suite.** Five observations in five different tests, unchanged diagnosis,
  unchanged code.
- **TD-94's lesson**, unchanged and still the largest.
- **TD-110/TD-41**, **TD-109**, **TD-93**, **TD-89**, **TD-86**, **TD-78**, **TD-75**'s siblings
  **TD-95**, **TD-96**, **TD-100**, **TD-81**, **TD-91**, **TD-112**, and **TD-116**.
- **TD-84** last, unchanged.

### 12.48 Iteration 23 — Phase 2, item 2 (TD-27), and the one value that must not be configurable

**Ticket:** Phase 2 item 2, "`IOptions` configuration for thresholds, paths, KDF parameters."

Three agents were sent to inventory what the row is actually about, with instructions to report what
they could not verify rather than infer it. Every claim either of them made that this iteration acted
on was re-derived by hand here first. **They were accurate, and one of them corrected the audit
itself** — see the last subsection.

#### What the row is, once the constants are counted

The row names three things. They turn out to be three different problems with three different
answers, and the useful part of this iteration was refusing to treat them as one.

| Kind | Example | What "configurable" can even mean |
|---|---|---|
| **Thresholds** | the `0.3` confidence gate, the page size, backup retention | A number read every time the code runs. Genuinely a setting |
| **Paths** | `%LOCALAPPDATA%/Pine`, computed independently in two places | A per-installation fact. Genuinely a setting, and the duplication was a latent defect |
| **KDF parameters** | Argon2id m=64 MiB / t=3 / p=4 | **Read once per vault, ever, and then written into that vault's own header.** See below |

#### The KDF is the reason this row needed thinking about

A vault's Argon2id parameters are minted at creation and written into its `pine.vault.json`.
`Argon2idKeyDerivationService` derives from `header.KdfParameters` and — by explicit design, stated
in its own comments — holds no cost constants at all. So the only thing "configurable KDF" can
honestly mean is **which profile a NEW vault is minted with**.

If a configured cost were ever consulted at unlock instead, editing one number in a file would
derive a different auth key for every vault already on disk. The symptom would be
`VaultKeyUnwrapException` — which is, deliberately, the *same* thing a wrong password produces,
because the unlock screen must not become an oracle for whether a candidate password was the failing
part (TD-23). Not "hard to diagnose": **indistinguishable, by construction**, from user error, on
data that cannot be recovered.

That property is now pinned twice, at two different levels, because either alone would be weak:

- `PineOptionsCompositionTests.NothingOnTheUnlockPath_ShouldDependOnTheConfiguredKdfProfile` —
  reflection over the constructors of `Argon2idKeyDerivationService`, `AesGcmVaultKeyService`,
  `FileVaultHeaderStore` and `UnlockViewModel`. It forbids a dependency that does not exist yet;
  what it pins is that it cannot be introduced without a test going red.
- `ConfiguredKdfTests.AVaultCreatedUnderOneProfile_ShouldStillUnlockAfterTheConfigurationChanges` —
  real Argon2id, a real header on a real directory. Mint under m=16/t=1/p=1, change the
  configuration to m=64/t=3/p=2, unlock. It passes because the header is the only source.

`PineSecurityOptions.ToKdfParameters()` is the single bridge from configuration to cryptography and
has exactly one caller. Salt and key lengths are **not** configurable and are asserted so: the key
length is fixed at 32 bytes by `AesGcmVaultKeyService`, which refuses anything else because the data
key it wraps is AES-256.

#### Two options classes were deliberately NOT converted

`PineLoggingOptions` and `VaultAutoLockOptions` each carry a written argument for not being an
`IOptions` binding, and both survive contact with this ticket:

- The log sink must be usable **before a container exists**, because `PineCrashLogger` runs from
  `Main`'s catch block where the failure being recorded may be the container itself.
- The auto-lock timeout is a user preference a settings screen changes at runtime, and the options
  pattern's snapshot semantics would never reach the already-running timer.

Converting them would have been the tidier-looking change and the wrong one. Configuration **seeds**
them — the log directory now follows the configured data directory, the idle timeout takes its
starting value from configuration — and then gets out of the way. A test asserts both halves of the
auto-lock case, including that the object is still mutable afterwards, because keeping only the first
half would read as an oversight.

#### What was built

| Built | Where | Notes |
|---|---|---|
| `PineOptions` + four sections | `Pine.Core/Configuration/` | Plain BCL classes, no attributes, no package — `Pine.Core` still has **zero** package references |
| `PineOptions.Validate()` | same | Hand-written, returning **sentences** naming the key, the bound and the value read. `ValidateDataAnnotations()` would have pulled in a package to report a violated `[Range]` without saying what the value is for |
| `PineOptionsValidationException` | same | On `IUserFacingFailure` (TD-24) — every sentence names something the reader typed themselves, so none of it is a disclosure |
| `PineConfiguration` | `Pine.Ui.Avalonia/Composition/` | Optional JSON file plus `PINE_*` environment variables |
| `PineSecurityOptions.ToKdfParameters()` | `Pine.Core/Configuration/` | The one bridge to cryptography |

**Ten literals moved**, and the two that mattered most were duplicates:

- **The `0.3` confidence gate, written twice.** `CategorizeTransactionQueryHandler` refused a
  prediction below it; `ImportTransactionsCommandHandler` auto-applied one at or above it. Nothing
  tied the two numbers together. The asymmetry is what made it dangerous: import **writes** the
  category into the ledger where the suggestion path only shows it, so a drift leaving import's
  number lower would have filed transactions under a category the app considered too weak to display.
- **The Pine data directory, computed twice.** `VaultManager`'s constructor and
  `PineLoggingOptions.DefaultDirectory()` each derived `%LOCALAPPDATA%/Pine` independently, with no
  shared constant. Two definitions agree until one is edited.

Also moved: backup retention (whose own comment named TD-27 as the reason it was a constant), the
minimum training rows, the trainer's batch threshold and both of its timings (which were literals
*inside* `ExecuteAsync`, where nothing could reach them — including a test, which is why that service
has none), and the diagnostics record ceiling.

#### The configuration file is not in the package, and that is a decision

TD-27's row reads "`IOptions` + appsettings", which most naturally suggests a file shipped beside the
executable. `packaging/package.sh` checksums **every file it ships** and records the list in a
manifest (§12.42), so a config file inside the artefact would be a file whose entire purpose is to
be edited and whose edit invalidates the artefact's own integrity claim. It therefore lives in
Pine's per-user data directory, beside `vaults.json` and `logs/`, outside anything signed. Its
location is fixed even though the data directory is configurable — the file that moves the directory
has to be found before it is read, and configuration that can hide itself is configuration nobody
can support.

#### Falsification

Thirteen mutations. **13 killed — but not on the first attempt, and the exception is the useful part.**

| # | Mutation | Expected to die by | Result |
|---|---|---|---|
| M1 | Default confidence threshold drifts 0.3 → 0.5 | `AnUnconfiguredPine_ShouldBehaveExactly…` | **Killed** — 1 test |
| M2 | Page-size default reverts to the dead 50 | same, plus `AConfigurationNamingOneKey…` | **Killed** — 2 |
| M3 | `Bind` resets a section instead of overlaying it | `AnEmptyConfiguration_ShouldLeaveEveryDefaultInPlace` | **Killed** — 11 |
| M4 | `Bind` skips validation | `AnUnusableConfiguration_ShouldBeRefused…` | **Killed** — 3 |
| M5 | `Bind` ignores the override hook | `TheOverrideHook_ShouldRunAfterBinding…` | **Killed** — 5 |
| M6 | A relative data directory is accepted | `ARelativeDataDirectory_ShouldBeRefused` | **Killed** — 2 |
| M7 | `ToKdfParameters` ignores configuration entirely | — | **Killed by the compiler**: CA1822, "does not access instance data", is an error here. Recorded as a kill of a different kind, and re-run as M7b |
| M7b | Only the memory cost ignores configuration | `AConfiguredProfile_ShouldBeTheOneWritten…` | **Killed** — 2 |
| M8 | The unlock path grows a dependency on the configured KDF | `NothingOnTheUnlockPath_ShouldDependOn…` | **Killed** — 1 |
| M9 | The import handler reverts to the literal `0.3` | *(nothing)* | **SURVIVED.** See below |
| M10 | The log directory stops following the data directory | `TheLogDirectory_ShouldFollowTheConfigured…` | **Killed** — 1 |
| M11 | The auto-lock timeout is no longer seeded | `TheAutoLockTimeout_ShouldBeSeeded…` | **Killed** — 1 |
| M12 | The options are registered as two objects instead of one | `EverySection_ShouldResolve_AsIOptionsAndAsItself` | **Killed** — 1 |

**M9 is the finding.** The tests asserted, by reflection, that each handler *takes*
`IOptions<PineAiOptions>`. Reverting the import handler's comparison to the literal `0.3` left that
assertion green: the handler still took the options and simply ignored them. **Taking a dependency
and using it are different claims, and only the second one is the ticket.** `ConfidenceThresholdTests`
was written in response — it drives both consumers with the same two thresholds and asserts the
observable outcome, including that a below-threshold prediction is not written to the ledger. M9 now
dies by that test. The reflection assertions were kept, because they fail earlier and say something
different, but they were never sufficient on their own.

**A harness note carried over from §12.46, and applied from the start this time.** The restore step
does not preserve mtimes, so a restored file is older than the build output and MSBuild silently
skips the rebuild — measuring the *previous* mutation. Every run above forced a recompilation. Two
mutations (M6 in its first form, M7) failed to compile rather than failing a test, and both are
recorded as what they were rather than as kills.

#### An agent corrected the audit

§6.2 of this document lists *"the `0.3` AI confidence gate (duplicated in two files) and page size
`50`"* among the compile-time constants. Both halves were checked against the code:

- **The `0.3` claim is exactly right** — two files, and no third.
- **The page-size claim is true but misleading.** `GetTransactionsQuery` did declare
  `PageSize = 50`, and no user ever saw it: `TransactionsViewModel` declared its own
  `const int PageSize = 30` and passed it explicitly. The number the audit named as hard-coded was
  **unreachable**, and the one the user actually experienced was never mentioned. Making the
  unreachable one configurable would have produced a setting that changes nothing, so the default
  parameter was **deleted** — both callers now pass a size, and the one the user sees comes from
  configuration.

A third claim, that *"model paths"* are compile-time constants, is stale in one place: `MlNetCategorizer`
has had no model path since TD-16 moved it into `VaultCategorizerModelStore`. What the sweep did find
is worse than the row says — `HeuristicFieldExtractor.ModelPath` is a bare relative
`"Assets/Ai/ner_minilm_int8.onnx"` with no `AppContext.BaseDirectory` anchor, so it resolves against
the process working directory. That is TD-46, still open, and it is now known to be one file rather
than two.

#### Measured, not asserted

| | Before | After |
|---|---|---|
| Unit tests | 762 | **798** |
| Integration tests | 190 | **193** |
| Package references in `Pine.Core` | 0 | **0** |
| Definitions of "the Pine data directory" | 2 | **1** |
| Copies of the AI confidence gate | 2 | **1** |
| Page sizes for the transaction list | 2 (one unreachable) | **1** |
| Configuration keys with a validation bound | 0 | **13** |
| Values a config file can change | 0 | **13** |

Both suites green at this commit, the unit suite confirmed over three consecutive runs — see the
first row of "Explicitly NOT done".

#### Explicitly NOT done

1. **The unit test host aborted once, mid-run, at 717 of 798.** It was re-run three times and
   reported 798/798 each time, which is why the number above is stated. No cause was found and none
   is claimed; it is recorded here because a suite that aborts is not a suite that passed, and
   §12.42's TD-97 is a standing reminder that this project has an unexplained flakiness problem it
   keeps observing and never diagnosing. This is a **different** shape from TD-97's — a host abort
   rather than a failing assertion — and is registered as such under TD-117.
2. **The remaining magic numbers were not moved, and there are many.** A dozen-plus vision thresholds
   in `ReceiptDetector` (detection cutoffs of `0.5f` and `0.85f`, an aspect-ratio floor of `1.15`, an
   area ratio of `0.10`, letterbox size `640`, anchor count `8400`), the geometry and OCR kernel
   parameters in `OpenCvImageProcessor`, and the zone-detection ratios in `HeuristicFieldExtractor`.
   None was moved, deliberately: they are not settings, they are the algorithm, and exposing them as
   configuration would invite a user to break receipt detection and then report it as a bug. Recorded
   as TD-118 with that reasoning, so the decision is visible rather than an omission.
3. **The import path still has no size cap, row cap or column cap at all** — verified during this
   sweep. That is TD-50, unchanged, and it is a limit that should exist before it is configurable.
4. **No settings UI.** Nothing in the app edits any of these thirteen values; a person changes them
   by writing a JSON file or setting an environment variable. That is the correct scope for a row
   about a configuration *system*, and the boundary question it raises is TD-119.
5. **Android and iOS were not compiled**, unchanged: the workloads are absent from this machine.
6. **Nothing was committed.**

#### New debt

| ID | Row | Sev | Impact | Fix | Pri | Status |
|---|---|---|---|---|---|---|
| TD-117 | A misspelled configuration key is silently ignored, and the unit host aborted once with no explanation | 🟡 | `IConfiguration` binding ignores keys that match no property, so `Pine:Ai:ConfidenceThreshold` (the natural misspelling of `CategorisationConfidenceThreshold`) changes nothing and reports nothing — the operator's experience is that configuration does not work. Separately, one run of the unit suite aborted at 717 of 798 and three subsequent runs passed 798/798; the abort is unexplained and is a different shape from TD-97's failing assertions. **Amended, iteration 24: this is not an anomaly, it is the normal case.** The abort was observed **seven times in a single session** here, plus three more reported by agents — ten in one day, against a suite that passes 824/824 whenever it completes. It has a signature: six of the seven observed here aborted between test **700 and 735** of 824, one at 533, and the run prints only `Fatal error.` with a managed stack ending in the xunit invoker. Roughly one run in two now aborts, which means every test count in this document costs two or three runs to obtain | Compare the bound section's key set against the options tree's property names and refuse unknown keys, which is the same fail-loud posture the value bounds already take. Ship a commented example file. For the abort: capture a crash dump from the test host rather than re-running until it is green | P2 | 🔴 **Open**  **DIAGNOSED, iteration 26, §12.54 — this row is no longer about an unexplained abort.** The crash was captured and **reproduced three times consecutively**: `Fatal error. 0xC0000005` at `Sdcb.PaddleInference.Native.PaddleNative.PD_PredictorCreate2`, reached through `PineViewModelFactory.CreateLiveScanner()` from `ViewModelFactoryTests`, and through `PineCompositionRootTests.EveryHandlerThatNeedsTheAiProvider_ShouldBeConstructible_OnEveryHead` before that test existed. It is a **native access violation** from xunit resolving the real `IAIProvider` on several threads at once — not a flaky assertion, and nothing to do with the misspelled-key half of this row. The fix is small and known: serialise the tests that build the real AI stack into one xunit collection, or substitute the provider where the test does not need the real one. Deliberately not done in iteration 26, which owed its attention to a different row. **THE ABORT HALF IS CLOSED, iteration 31, §12.64 — as an enabler, not as this row's turn.** No count in that iteration could be measured otherwise: its first two full runs aborted at 768/874 and 760/874. `ProcessWideStateTestGroup` is a `[CollectionDefinition(DisableParallelization = true)]` over the three classes that build a real Windows-profile container — `PineCompositionRootTests`, `ViewModelFactoryTests`, `MessengerLifecycleTests` — taking the first of the two options this row offered; substituting the provider was rejected because these tests exist to prove the head's REAL graph closes, and a substitute would have deleted the coverage that found TD-90. It absorbs the old `"Messenger"` collection, which guarded the same category of hazard (one process-wide resource, several classes, run concurrently) and which a class cannot belong to at the same time. **Falsified, and the falsification is the point**: removing one class from the collection, the suite completed twice and aborted on the THIRD run at 650/865 — two runs would have reported a false pass, which is the trap this row has set for four iterations. With the collection: six consecutive complete runs, one of them under coverage instrumentation. **The misspelled-configuration-key half of this row is untouched and this row stays open for it** |
| TD-118 | The receipt pipeline's ~20 vision and geometry constants stay literals, and the reason is not written anywhere they are | 🟡 | They were deliberately excluded — they are the algorithm, not settings, and exposing them would let a user break detection and report it as a bug. But that reasoning lives only in this entry, so the next person to read `ReceiptDetector` sees an inconsistency: some thresholds are configurable and these are not, with nothing saying why | Either a comment at the top of the vision classes stating the boundary, or a named `ReceiptVisionProfile` type that groups them as tuned constants belonging to a model version | P3 | 🔴 **Open** |
| TD-119 | Nothing states where the boundary between configuration and user settings is, and several values sit on it | 🟠 | Theme, accent and language are user settings, written into the encrypted vault through `SaveSettingCommand`. The auto-lock timeout, the page size and the confidence gate are now configuration, in a plaintext file outside the vault — and all three are things a user would reasonably expect to change from a settings screen, where they would also be private and travel with the vault. `VaultAutoLockOptions`' own remarks describe the timeout as "a user preference that a settings screen changes at runtime", which is the opposite classification from the one this iteration gave it | Write the rule down: operator/installation values in configuration, per-user values in the vault, and name which of the thirteen are which. Then move the ones that are on the wrong side | P2 | 🔴 **Open** |

### 12.50 Iteration 24 — Phase 2, item 3 (TD-28), and the test that had to be fixed before it could be written

**Ticket:** Phase 2 item 3, "Typed view-model factories; remove `IServiceProvider` and
`PineDbContext` from the UI; add an architecture test asserting UI ↛ Infrastructure."

Three agents were sent to inventory the row before anything was built — the service-locator sites,
the view-model construction graph, and the existing architecture tests — with instructions to report
what they could not verify rather than infer it. **All three were accurate**: the eighteen locator
sites one of them listed matched a grep run here exactly. Every claim this iteration acted on was
re-derived by hand first, and the two facts that changed the shape of the work were found by
*running* something rather than by reading it.

#### The architecture test this row asked for already existed, and it was nearly vacuous

`ArchitectureTests` has had `DomainLayer_ShouldNot_DependOnOtherLayers` and
`ApplicationLayer_ShouldNot_DependOnInfrastructureOrUI` since early on. Both are written as:

```csharp
.ShouldNot().HaveDependencyOn("Pine.Application")
.And().HaveDependencyOn("Pine.Infrastructure")
.And().HaveDependencyOn("Pine.Ui.Avalonia")
```

In NetArchTest, conditions chained with `.And()` are **conjunctive**: that assertion fails only for a
type that depends on *all three at once*. A `Pine.Core` type depending on `Pine.Application` alone
passes. An agent flagged this as suspicious and correctly marked it UNVERIFIED, because the library
ships as a compiled DLL and the semantics could not be read. It was settled by experiment instead —
a throwaway test asserting that `Pine.Application`, which genuinely does depend on `Pine.Core`, is
caught by each form:

| Form | Verdict |
|---|---|
| `ShouldNot().HaveDependencyOn("Pine.Core").And().HaveDependencyOn("Pine.Infrastructure")` | **passed** — i.e. did not catch the real dependency |
| `Should().NotHaveDependencyOnAny("Pine.Core", "Pine.Infrastructure")` | failed, as it must |

So the roadmap's last Phase 2 bullet — "rewrite the architecture-test assertions with
`NotHaveDependencyOnAny`" — was not a tidying task filed for later. It was the reason the layer
rules had been passing. Both are rewritten, and the anti-vacuity guard that only
`InfrastructureLayer_ShouldNot_DependOnSystemConsole` carried is now on all of them, including the
two `SetupViewModel` filters that would have selected nothing after a rename and passed.

#### The decision §12.47 and §12.49 asked for, made in writing

Both Next sections flagged that "UI ↛ Infrastructure" cannot be asserted over the whole assembly:
`PineCompositionRoot` lives in `Pine.Ui.Avalonia` deliberately, so that a `net10.0` test can reach it
(TD-83), and calling `AddPineInfrastructure` is its entire job. The choice was between scoping the
rule to the view models or moving composition into a project of its own.

**Scoped, and the alternative is recorded in the test's own remarks rather than in a commit
message.** Splitting the project would buy a compiler guarantee instead of a test one — but it would
require editing the Android and iOS heads, which no machine in this project can compile (TD-07), and
it would have to paper over a coupling better recorded honestly (TD-120). What replaces the
assembly-wide rule is two tests, not one: a namespace rule over the view models and views, and an
**explicit allow-list** naming every type in the assembly permitted to touch `Pine.Infrastructure`.
The allow-list has one entry. A new helper class that reached for EF Core would pass the namespace
rule simply by not being called a view model; it fails the second, and the only way to make it pass
is to add its name in a diff, on purpose.

#### What was built

| Seam | Where | What it took out of the UI |
|---|---|---|
| `IVaultDatabaseOpener` / `ScopedVaultDatabaseOpener` | `Pine.Core.Security` / `Pine.Infrastructure.Data` | The DI scope, the `PineDbContext` probe and the `IVaultMigrator` call that `UnlockViewModel` and `SetupViewModel` performed inline. The unlock path and the setup path stay **two methods**, because a schema failure must never be reported as a wrong password |
| `IViewModelFactory` / `PineViewModelFactory` | `Pine.Ui.Avalonia.Services` / `.Composition` | Fifteen named methods. `IServiceProvider` from `MainViewModel`, `ShellViewModel`, `SetupViewModel`, `UnlockViewModel` |
| `IImporterFactory` / `ExtensionImporterFactory` | `Pine.Core.Importing` / `Pine.Infrastructure.Imports` | `new CsvImporter(...)`, the last `Pine.Infrastructure` reference in a view model |
| `PineUiServices` | `Pine.Ui.Avalonia` | The `public static IServiceProvider? Services` that `MainWindow` reached into |

`INavigationService` now takes the page rather than a type argument. The generic overloads it
replaced resolved `T` out of the container held by the navigator, which is what made "this head does
not register that page" a throw on the user's click — the same defect class as TD-85, one layer up.

**`Microsoft.EntityFrameworkCore.Sqlite.Core` was removed from `Pine.Ui.Avalonia.csproj`.** The UI
project had declared EF Core as its own dependency; after the probe moved, nothing in it needed EF
Core at all, and the four projects that depend on it still build.

#### Two defects found on the way, neither of them in this row

**The import wizard parsed everything as CSV.** The file picker offers `*.csv, *.xlsx, *.xls,
*.pdf, *.png, *.jpg, *.jpeg`; both the preview path and the commit path handed every one of them to
`CsvImporter`. An agent was told to inject a single `IImporter` and **refused, correctly** — it found
three implementations and a real choice, and stopped rather than hard-wiring CSV behind a seam. The
fix is per-extension selection, with `UnsupportedImportFormatException` (an `IUserFacingFailure`, so
TD-24's machinery renders its sentence) for the image extensions no importer can read. This is a
user-visible change and not entirely an improvement: `.xlsx` and `.pdf` now reach the documented
stubs that parse **zero** rows, so the user gets an empty preview instead of nonsense derived from
ZIP or PDF bytes. Honest, still not a working import — TD-122.

**`TimeProvider.System` was hard-coded** in `DashboardViewModel`'s construction of the import
wizard, overriding the `TimeProvider` the container registers. Any head or test substituting a clock
was silently ignored by the import path. The factory supplies the registered instance now, and a
test asserts reference equality.

#### Falsification

Ten mutations, **10 killed** — but the two numbers underneath that matter more than the total.

| # | Mutation | Verdict |
|---|---|---|
| 1 | A view model takes `IServiceProvider` again | killed |
| 2 | A view model names `PineDbContext` again | **not-a-kill first** — see below |
| 3 | A non-view-model UI type reaches into `Pine.Infrastructure` | killed (the allow-list) |
| 4 | `.pdf` mapped to the CSV importer | killed |
| 5 | An unknown extension falls back to CSV instead of refusing | killed |
| 6 | The importer factory ignores the injected clock | killed |
| 7 | The unlock probe never checks the connection | **survived** — see below |
| 8 | Setup never creates the schema | **invalid** — see below |
| 9 | The factory hand-builds the wizard, losing its logger and clock | killed |
| 10 | `IViewModelFactory` is not registered | killed (53 failures) |

**Mutation 2 did not compile.** `public System.Type MutantContext => ...` tripped CA1822 under
`TreatWarningsAsErrors`, which is a build failure and not evidence about the test. It was recorded as
a not-a-kill and rewritten as a `static` member before it counted — the convention §12.42 set in
iteration 20, applied to this iteration's own work.

**Mutation 8 was a no-op.** As first written it inserted a `cancellationToken.IsCancellationRequested`
guard that is never true, so it changed no behaviour and its "survival" was worth nothing. It was
discarded rather than counted, and rewritten to actually skip the migration.

**Mutation 7 survived, and that is the finding of this campaign.** `grep -rn "IVaultDatabaseOpener"
tests/` returned two NSubstitute *doubles* and nothing else: the extracted implementation — which
owns the probe, the migration and the scope — **was executed by no test at all**. That is not a
regression; the same logic was equally untested inside the two view models, where a substituted
`IServiceProvider` meant no test ever reached it. What the extraction changed is that the untested
thing acquired a name, and a `grep` could find it. Four integration tests now drive the real type
against a real SQLCipher vault, and both mutations 7 and 8 were re-run **here** afterwards, killed,
with the file's MD5 checked before and after each to prove the restore was byte-exact.

#### Measured, not asserted

- Every project that builds on this machine builds with 0 errors and 0 warnings: Pine.Core,
  Pine.Application, Pine.Infrastructure, Pine.Ui.Avalonia, Pine.Ui.Windows, Pine.Ui.Macos,
  Pine.Ui.Web, Pine.Cli and both test projects. **Android and iOS still compile on no machine**
  (NETSDK1147, workloads absent) and have now been edited blind for fifteen iterations.
- Unit **810 → 824**, integration **193 → 197**. Both green, both re-run here after the agents
  reported them.
- `grep -rn "IServiceProvider\|GetRequiredService\|GetService\|CreateScope" src/Pine.Ui.Avalonia
  --include=*.cs` returns hits only under `Composition/`; `grep -rn "Pine.Infrastructure"` over the
  same tree returns two, one of which is prose in a comment.
- **TD-97 was observed a sixth time**, in a sixth different test —
  `BalanceRecalculationTests.Recalculate_WhenTheLedgerHoldsAForeignCurrencyRow_IsRefusedAndChangesNothing`
  failed in one full run and passed in the next, with no change between them.
- **One set of numbers stated during this iteration was wrong, and the cause is worth writing down.**
  An integration run reported three failures in the tests just added. They were not a regression:
  the run used `--no-build`, and the binaries still carried mutation 8. The source had been
  restored and MD5-verified; the *build* had not been. This is the same class of error §12.46
  recorded in iteration 22, where MSBuild silently skipped rebuilds and each mutation was partly
  measuring the previous one. Every number above was re-measured after a full rebuild.
- **TD-117's abort was observed seven times in this session alone**, plus three more reported by
  agents — ten in one day. It now has a signature worth recording: of the seven observed here, six
  aborted between test 700 and test 735 of 824 and one at 533. See the amended row.

#### The rule iteration 21 built does not check the numbers it enforces

`README.md`, `STATUS.md` and `18-Test-Strategy` all still read **891 tests (701 unit + 190
integration)** when this iteration looked at them — the number iteration 21 wrote. Iterations 22, 23
and 24 each added tests without touching any of the three, so all three were **three iterations
stale**, and `DocumentStatusTests` was green throughout.

That is not the test failing; it is the test doing exactly what it was built to do. Iteration 21
made the presence of a `Reconciled` line mandatory and machine-checked, which is why those files
have one at all. Nothing checks whether the sentence after the date is *true*, and a stale count is
indistinguishable from a fresh one to a rule that only looks for the field. §12.44 closed the
failure mode where a document had no status; the one where it has a confident and wrong status is
still open, and it recurred within one iteration of the rule being written. All three are corrected
to the measured 1021 (824 + 197), and `STATUS.md` now carries the observation next to the number.

#### Explicitly NOT done

1. **The camera still runs through OpenCvSharp inside a view model.** Removing the
   `Pine.Infrastructure` project reference outright was attempted as an experiment and revealed it:
   `LiveScannerViewModel` uses `VideoCapture`, `Mat` and `Cv2` directly, and `OpenCvAvaloniaBridge`
   converts a `Mat` to an Avalonia bitmap. Registered as TD-120 rather than hidden behind an explicit
   package reference.
2. **The navigation defect found here was not fixed.** Registered as TD-121 with the probe that
   demonstrates it.
3. **The import picker still offers image formats no importer can read**, and the column-mapping step
   is meaningless for the two non-CSV formats. TD-122, TD-123.
4. **Nothing was committed.**

#### New debt

| ID | Row | Sev | Impact | Fix | Pri | Status |
|---|---|---|---|---|---|---|
| TD-120 | `LiveScannerViewModel` drives the camera through OpenCvSharp directly, so the UI's clean bill of health stops at `Pine.Infrastructure` types | 🟠 | The two architecture tests added this iteration assert that no view model names a `Pine.Infrastructure` type. They say nothing about `OpenCvSharp`, which is a third-party native wrapper reaching the UI transitively through that same project reference — `VideoCapture`, `Mat` and `Cv2` appear in a view model and in `Utils/OpenCvAvaloniaBridge`. Dropping the project reference to make the rule compiler-enforced fails on exactly these two files, which is how this was found. A reader of the passing tests would reasonably conclude the UI is free of infrastructure; it is free of *Pine's* infrastructure | A camera seam (`ICameraCaptureSession` or similar) owned by the infrastructure layer, handing the UI frames rather than `Mat`s. That also unblocks splitting composition into its own project, and it is the same seam §10 Phase 3 needs for a non-DirectShow camera. **It also now owns a capability the product lost on purpose (2026-09-11, plan item 1.5):** the live scanner's camera picker listed four devices whatever was attached, and was deleted rather than made real, because OpenCV cannot enumerate devices — the only way to learn that index 2 exists is to open it, which would turn the camera on before the user pressed Start. A user with two cameras could previously select the second one (index 1 genuinely opened it); they now get the system default. Choosing a camera honestly needs a platform API that enumerates them, which is exactly what this seam is for | P2 | 🔴 **Open** |
| TD-121 | Every in-shell page's `INavigationService` is the **window-level** navigator, so navigating replaces the whole shell | 🟠 | `MainViewModel` and `ShellViewModel` both implement `INavigationService`, but only `MainViewModel` is registered under it — `ShellViewModel` is registered as itself and can therefore never be the one injected. Verified by resolving `INavigationService` from the real container: it is not a `ShellViewModel`. So `ScanReceiptViewModel` navigating to Transactions, and `CropReceiptViewModel` navigating back to Scan, set the *window's* content and drop the shell, its sidebar and its active-tab state. `BudgetsViewModel` injects the navigator and never calls it, which is why nothing has failed loudly. This predates TD-28 and was not introduced by it; the typed factory ran straight through it | Give the shell its own navigator seam, or make the shell register itself as the `INavigationService` its children resolve. Decide which of the two navigators a page is entitled to, then assert it — the probe that found this is three lines. **Done — 2026-09-11, plan item 1.2, by the first of the two options.** The second is not available: the shell is transient, one per open vault, so no registration can name the right one. `IShellNavigator` is a separate interface implemented only by `ShellViewModel` and never registered; the shell hands itself to the three receipt pages through `IViewModelFactory`, and each hands it on. The receipt pages left the container, so the factory now enforces TD-85's capability gate explicitly. **The row understated the defect**: `MainViewModel`'s lock handler clears data only when the window shows the shell, so a lock after leaving the receipt page left the ledger on screen with the key gone — reproduced before the fix by `ShellNavigationTests.A_lock_after_leaving_the_receipt_page_returns_to_the_vault_picker`. `ArchitectureTests.EachScreen_ShouldTakeTheNavigatorOfThePlaceItIsShownIn` pins which view models can *reach* which navigator. The lock handler was inverted as well — it now leaves alone only the pre-vault screens and clears everything else — so the leak no longer depends on the navigation fix holding. **Two independent passes broke the guards four times between them** — a page taking the concrete `MainViewModel`, a registered `IShellNavigator` (each left all 1070 unit tests green), Avalonia's own desktop lifetime and a static on `MainViewModel` — **and found a lifecycle defect the fix itself created** (a shell retired by a lock still activated a page a late continuation handed it); all are closed and each is now a killed mutation. Nineteen mutations, nineteen killed. Closing it also found the unit coverage gate already red from item 1.1 (TD-56): Infrastructure 77.63% < 78 and overall 67.51% < 68 See `62-Post-Audit-Plan` §4.2 | P1 | ✅ **Closed** — iteration 40 |
| TD-122 | The import picker offers file types Pine cannot import, and two it can only pretend to | 🟡 | **✅ Done — iteration 37 (§12.76).** The picker now offers `.csv`, `.xlsx` and `.xls` and nothing else: the image patterns are gone (they belong to receipt scanning, a different screen with a different pipeline) and so is the PDF category. `UnsupportedImportFormatException`'s own remarks had predicted this — "the picker's filter list should be narrowed; until it is, the failure is at least stated rather than silent" — and that paragraph is kept, because it called it correctly. Originally: | `ImportWizardView.axaml.cs` offers `*.png, *.jpg, *.jpeg` under "Receipt Images" — the statement-import path has no image importer at all, and those belong to receipt scanning. It also offers `*.xlsx, *.xls, *.pdf`, whose importers are documented stubs that parse zero rows. Before this iteration every one of them was fed to `CsvImporter`; now an image raises a sentence and a spreadsheet reaches an empty preview. The refusal is honest, but the affordance that produces it is still wrong | Narrow the picker to what each head can actually import, or route the image extensions to the receipt-scanning path where the user plainly meant them to go | P2 | 🔴 **Open** |
| TD-123 | The wizard's column-mapping step is meaningless for every format except CSV | 🟡 | Steps 1–2 build an `ImportProfile` carrying `Date`/`Payee`/`Amount`/`Category` column names and a `','` delimiter. `ReadCsvHeaders` only runs for `.csv`, so `AvailableColumns` stays empty for a spreadsheet or a PDF, and both importers ignore `ColumnMappings` entirely. The user is asked to map columns that were never read, for a file whose importer would not use the answer | Make the wizard's steps a function of the chosen format, once TD-122 has decided which formats it offers. Not a redesign to attempt while two of the three importers parse nothing | P3 | 🔴 **Open** |

### 12.52 Iteration 25 — Phase 2, item 4 (TD-26), and a workaround that had outlived its reason

**Ticket:** Phase 2 item 4, "Replace the reflection call with a domain method or a single transfer
aggregate."

Two agents were used: one read-only inventory of `LinkedTransactionId` and the transfer path, and one
implementer. **Both were accurate**, and the inventory found two things that changed what this
iteration had to build. Every claim acted on was re-derived here, and the falsification campaign was
run here rather than taken on report.

#### What the row was actually about

`TransferFundsCommandHandler` created a transfer's two legs and linked them by **two different
mechanisms**: the destination leg through `Transaction.Create`'s `linkedTransactionId` parameter, and
the source leg through

```csharp
typeof(Transaction).GetProperty(nameof(Transaction.LinkedTransactionId))!.SetValue(sourceTx, destTx.Id);
```

The property's setter was already `private`. The handler was not working around a missing
encapsulation; it was defeating one that existed — and the comments left in the file record it
happening in real time: *"Wait, source needs to link to destination as well. Since constructor doesn't
allow setting Id manually, we must use a workaround..."*.

The reason it mattered is not the rename hazard the row names. It is that
`VoidTransactionCommandHandler` follows the link **outward from whichever leg the user clicked** and
never does a reverse lookup, so voiding the source leg depends on the reflection-written direction and
voiding the destination leg on the constructor-written one. Two mechanisms means two independent ways
to lose one direction, and either loss leaves **money withdrawn from one account and never returned to
the other** — a worse state than the one the user was correcting.

#### Neither remedy the row proposed was taken

§6.2 proposed "add `Transaction.LinkTo(...)`, or model the transfer as one aggregate with two legs".

- **`LinkTo` was rejected.** A public mutator makes the one-way link *easier* to write, not harder. It
  would have removed the reflection and kept the defect it enabled.
- **A single aggregate was rejected for now.** The inventory confirmed there is no foreign key and no
  navigation between legs — `LinkedTransactionId` is a plain indexed nullable column — so remodelling
  means a migration, on a schema whose money columns were rewritten only three iterations ago.

What was built keeps the second option's *property* without its cost: `Transaction.CreateTransferPair`
generates both identifiers up front and constructs each leg already carrying the other's, returning a
`TransferLegs` pair. One mechanism, both directions, established at construction. And
`Transaction.Create` **lost its `linkedTransactionId` parameter entirely**, so a one-way link is not a
shape production code can express any more. Two guards that previously lived only in the validator —
distinct accounts, positive amount — now also hold in the domain, where they cannot be bypassed by a
future caller that forgets the pipeline.

#### The tripwire that was missing on the other aggregate root

`Account`'s exact public method list has been pinned since iteration 6 by
`ExpectedAccountPublicMethods`, precisely so that a new way to mutate it has to be argued for in
writing. **`Transaction` had no such list**, so adding a public mutator to the aggregate that records
every movement of money was free. It has one now, derived by running the test and reading what it
reported rather than by guessing, and it asserts zero public setters alongside it.

#### The durable half

Deleting one call does not stop the next one. The set of types in `Pine.Application` carrying an IL
dependency on `System.Reflection` is now asserted to equal an explicit allow-list, in the idiom
§12.50 introduced for the UI. The list has one entry: `PineApplicationServiceCollectionExtensions`,
which scans the assembly to register validators — legitimate, documented, and the reason a blanket ban
was not written instead. It passed against that value on its first run, so the allow-list is the
observed set rather than one fitted to make a test green.

#### Falsification

Ten mutations, **10 killed**. Six against the domain factory, two against the guards, one against each
architecture test; four of them run against the **real void path** in the integration suite, which is
where a lost link actually costs money.

| # | Mutation | Verdict |
|---|---|---|
| 1 | The source leg is not linked back | killed |
| 2 | The destination leg is not linked back | killed |
| 3 | Both legs get the same sign | killed |
| 4 | The non-positive amount guard is disabled | killed |
| 5 | The same-account guard is disabled | killed |
| 6 | The returned legs are swapped | killed |
| 7 | Reflection returns to the application layer | **invalid first** — see below |
| 8 | `Transaction` gains a public mutator | killed (the new tripwire) |
| 9 | The source leg is unlinked, against the real void path | killed |
| 10 | The destination leg is unlinked, against the real void path | killed |

**Mutation 7's first form was invalid, not a survivor.** It added
`_ = typeof(Transaction).GetProperty(nameof(Transaction.LinkedTransactionId));` and the architecture
test passed — which looked like a hole in the new guard. It is not: a discarded `GetProperty` call
emits no `System.Reflection` member reference at all, so there was nothing for an IL dependency scan
to find, and the mutation changed no behaviour either. Rewritten as the **actual statement TD-26
records**, `.SetValue(sourceTx, destTx.Id)` included, it was killed. The boundary is worth stating
plainly: the rule catches reflection that *uses* a `System.Reflection` member, which is the only kind
that can write to a domain object, and does not catch a bare lookup whose result is thrown away.

This is the second iteration running in which a mutation had to be discarded and rewritten before it
counted — §12.50's mutation 8 was a no-op guard. A mutation that changes no behaviour proves nothing
about the tests, and counting it either way would be false.

#### Measured, not asserted

- Every project that builds on this machine builds with 0 errors: Pine.Core, Pine.Application,
  Pine.Infrastructure, Pine.Ui.Avalonia, Pine.Ui.Windows, and both test projects. **Android and iOS
  still compile on no machine** — sixteen iterations now.
- Unit **824 → 837**, integration unchanged at **197**. Both re-run here after a full rebuild, not
  taken from the agent's report.
- `grep -rn "GetProperty\|SetValue" src/Pine.Application --include=*.cs` returns nothing outside
  `obj/`.
- **TD-97 was observed a seventh time, in a seventh different test** —
  `CurrencyValidationTests.TransferFunds_WithACurrencyMatchingNeitherAccount_IsRejectedBeforeAnythingIsWritten`
  failed once in a full run, passed in isolation, and passed on the next full run. Same profile as the
  other six: a vault-backed integration class under parallelism.
- **TD-117's abort recurred**, once, at 742 of 837.

#### One judgement call, made by an agent and reviewed here

Removing `Create`'s link parameter broke one test helper: `Scenario.AddTransaction` in
`VoidTransactionCommandTests`, which builds shapes `CreateTransferPair` **cannot** produce and must
not be able to — a link pointing at nothing, a pair whose far leg is already void, and a pair with
non-mirroring amounts. Those are the states the void handler's four unit tests exist to pin, and they
are exactly the rows most likely to need correcting in a real vault.

The agent kept that coverage by applying the link reflectively **inside the test fixture**, with a
comment saying it materialises legacy state, and flagged the decision rather than burying it. Reviewed
and kept: the fixture already has a sibling precedent (`AddForeignCurrencyTransaction`, which builds
another shape no current write path can produce), and the architecture test is scoped to the
application assembly, so nothing about it weakens the production guarantee. **The cross-reference in
that new comment was wrong** — it cited a test name that does not exist — and was corrected here. That
is the ADR-0012 defect class from §12.44 recurring inside the commit that introduced it.

#### Explicitly NOT done

1. **Legacy one-way links are not repaired or detected.** See TD-124.
2. **The mutual link is enforced at construction only** — the store has no constraint. See TD-125.
3. **The transfer was not remodelled as a single aggregate.** The reasoning is above; TD-125 records
   what that would buy.
4. **Nothing was committed.**

#### New debt

| ID | Row | Sev | Impact | Fix | Pri | Status |
|---|---|---|---|---|---|---|
| TD-124 | Transfers written before both directions were linked still carry a one-way link, and nothing finds or repairs them | 🟠 | `VoidTransactionCommandHandler`'s own comment says the link "can dangle on data written before transfers were linked in both directions", and it tolerates that by design. Tolerating is not repairing: on such a row, voiding from the linked side voids both legs and voiding from the *other* side silently voids one, leaving money withdrawn and never returned. The user is given no indication which case they are in. This iteration made new transfers incapable of the shape but did nothing for existing ones, and no migration, report or diagnostic names them | A read-only detector first — a query for transfer rows whose partner does not point back — so the size of the problem is known before a migration is written. Then either repair the reverse link or void both legs under a stated rule | P2 | 🔴 **Open** |
| TD-125 | The two legs of a transfer are mutually linked only by the constructor that makes them; the store enforces nothing | 🟡 | `LinkedTransactionId` is a plain nullable `TEXT` column with a non-unique index — no foreign key, no navigation, no constraint that the row it names points back. `CreateTransferPair` is now the only production path that sets it, so the invariant holds for anything written today; it holds because one factory is careful, not because the data model can express it. The edit path TD-68 will add is the obvious place for it to break again | Either a self-referencing FK plus a check that the pair is symmetric, or the single transfer aggregate §6.2 originally proposed — both need a migration, so the honest sequencing is after TD-124 has measured how much existing data would violate the constraint | P2 | 🔴 **Open** |

### 12.54 Iteration 26 — Phase 2, item 5 (TD-34, TD-35), and a mutation that survived 853 tests

**Ticket:** Phase 2 item 5, "`IDisposable` view models with deterministic messenger unregistration
(TD-34); awaited initialisation (TD-35)."

Two read-only inventories ran first, then two implementers; the second implementer was **cut off
mid-file by a session limit** and the rest of TD-35 was finished by hand here. Every claim acted on
was re-derived here, and the falsification campaign was run here.

#### The document that described this defect was itself part of the defect

`58-TechDebt-WeakReferenceMessenger-Lifecycle` is a dedicated record for TD-34/TD-35, and §6.12 had
assessed it as accurate. Re-derived against the current code, **nine of its factual claims were
wrong** — not stale line numbers alone:

| Claim | Reality |
|---|---|
| `DiagnosticsViewModel` sends `DataChangedMessage` | That file has **zero** messenger references. The row is dead, not moved |
| Eight senders | **Ten.** `SettingsViewModel` and `TransactionsViewModel` were missing from the table |
| "VM lifetimes are declared in each OS head's composition root, e.g. `Pine.Ui.Windows/Program.cs:68-80`; every head duplicates this list" | That file is 64 lines with **zero** registrations. Iteration 22 centralised them |
| Option B: "extend `MainViewModel.NavigateTo<T>`" | That generic method has not existed since iteration 24 |
| Every line citation in §2.2 and §2.3 | All stale |

The document has been rewritten and now **cites by quotation instead of by line number**, the
convention §12.44 adopted for the ADRs for exactly this reason. That its own acceptance criterion
"no changes required in any OS head composition root" is now satisfied *trivially* — because there
are no per-head roots left — is the clearest measure of how far the code moved while the document
stood still.

#### The prescribed remedy was wrong, for a reason that did not exist when it was written

§6 of that document says to drive `IsActive` from `Loaded`/`Unloaded` via an attached behaviour.
Iteration 24 collapsed navigation to a single `INavigationService.NavigateTo(ObservableObject)`,
which makes a better hook available — and a necessary one. **`MainViewModel.HandleVaultLocked`
assigns `shell.CurrentPage = null!` directly**, bypassing `NavigateTo` entirely, so a hook placed in
`NavigateTo` would silently miss the vault-lock path: the one path where leaving a recipient
subscribed matters most, because the key is gone and nothing that was showing decrypted data may
survive it. The hook therefore lives on the `CurrentPage` **property**, which all three assignment
sites go through.

An agent found the trap in that decision by emitting the generated source rather than trusting the
API surface. CommunityToolkit 8.4.2 declares both `OnCurrentPageChanging` overloads, but for
`[ObservableProperty]` *partial properties* the generated setter calls the two-parameter form as
`OnCurrentPageChanging(default, value)` — **the old value is literally `default`**. The obvious
overload would have handed `null` as the outgoing page every time and deactivated nothing, while
compiling and passing every existing test.

#### What was built

| Seam | What it ends |
|---|---|
| `ObservableRecipient` + `IRecipient<T>` on eleven types | The three handlers that captured `this` and so defeated the weak reference outright. **Zero manual `Register` calls remain in `src/`** |
| `PageActivation` | Deactivates **then** disposes, from the one navigation seam. `LiveScannerViewModel`'s camera release no longer depends on a view being unloaded |
| `ObservedTask` | Twenty-five discarded tasks, nine of them from constructors, nine with a callee that caught nothing |
| `IAsyncInitializable` | Constructor I/O. "Constructed" and "ready" are now different states, which is the init-race half of TD-35 |

`LiveScannerView.OnUnloaded` was **kept** rather than deleted: navigation disposes now, `Dispose` is
idempotent, and unloading is not always a navigation.

#### Two live defects the row does not name

**The settings page silently stopped saving.** `_isInitializing` starts `true` so that applying a
stored value does not write it straight back, and it was cleared on the last line of the happy path
— after three awaited queries with no `catch`. One throw latched it `true` for the life of the view
model, and all three change handlers open with `if (_isInitializing) return;`. The page then still
**applied** every change, because `ThemeManager.Apply*` runs before the save — so the user saw the
new theme, believed it saved, and found it reverted at the next launch with nothing having said
otherwise. The reset is in a `finally` now, and a test pins it.

**The transaction list reloaded twice on every void.** `ConfirmVoidAsync` broadcast
`DataChangedMessage` and then awaited `LoadDataAsync()` itself, while being a recipient of that same
broadcast. The broadcast stays, the second reload is gone.

#### Falsification

Nine mutations, **9 killed** — and the one that survived first is worth more than the other eight.

| # | Mutation | Verdict |
|---|---|---|
| 1 | Deactivation no longer unregisters | killed (4 tests) |
| 2 | Deactivation no longer disposes | killed |
| 3 | Activation no longer registers | killed (7 tests) |
| 4 | **Activation no longer initialises the page** | **survived the full 853-test suite** — see below |
| 5 | A faulted task is swallowed again | killed |
| 6 | Cancellations are reported as failures | killed (after rewrite) |
| 7 | The settings latch reverts to the happy path | killed |
| 8 | The dashboard stops cascading to its widgets | killed (after rewrite, and after relabelling) |

**Mutation 4 is the finding.** Deleting the `IAsyncInitializable` branch from
`PageActivation.Activate` — i.e. making navigation never initialise any page — left **all 853 tests
green**. The suite proved that `InitializeAsync` works when called directly, and that activation
registers the messenger, and never once that the two are connected. Every page's load would have
silently stopped happening and only a human opening the application would have noticed. This is the
same shape as the gap §12.48 found in iteration 23 — tests asserting that a dependency is *taken*
rather than *used* — recurring two iterations later, in an iteration whose whole subject is work
that starts and is never checked on. `Navigating_to_a_page_starts_its_initialisation` was written
because of it, and kills the mutation.

Two mutations did not compile on their first form and were recorded as not-a-kill before being
rewritten (§12.42's convention, applied for the third iteration running). One of those, mutation 8,
then compiled but tested the **wrong proposition** — it removed the dashboard's own `base` call
rather than the widget cascade — and was relabelled and re-run accurately rather than counted as it
stood.

#### TD-117 is no longer unexplained

The abort that has cost two or three runs per measurement since iteration 23 was captured, and
**reproduced three times consecutively here**:

```
Fatal error. 0xC0000005
   at Sdcb.PaddleInference.Native.PaddleNative.PD_PredictorCreate2(IntPtr, IntPtr)
   at Pine.Ui.Avalonia.Composition.PineViewModelFactory.CreateLiveScanner()
   at ViewModelFactoryTests.EveryFactoryMethod_ShouldProduceItsPage_OnAHeadThatOffersIt
```

It is a **native access violation**, not a flaky assertion: xunit resolves the real
`IAIProvider` on several threads at once and PaddleOCR's predictor creation is not safe under that.
The same crash arrives through `PineCompositionRootTests.EveryHandlerThatNeedsTheAiProvider_ShouldBeConstructible_OnEveryHead`,
which is how it was reaching the suite before `ViewModelFactoryTests` existed — so iteration 24 did
not introduce TD-117, but it did add a second door to it. It was **not fixed here**: it is a
different row, and the fix (serialise the tests that build the real AI stack, or substitute the
provider where the test does not need the real one) deserves its own falsification.

#### Measured, not asserted

- Every project that builds on this machine builds with 0 errors. **Android and iOS still compile on
  no machine** — seventeen iterations.
- Unit **837 → 854**, integration unchanged at **197**. Both re-run here after a full rebuild.
- `grep -rn "WeakReferenceMessenger.Default.Register" src/ --include=*.cs` returns nothing;
  `grep -rn "async void" src/Pine.Ui.Avalonia --include=*.cs` returns nothing outside doc comments.
- **TD-97 was observed three more times, in three more different tests**, two of them in a single
  run — eleven observations in eleven distinct tests. The integration project does not reference
  `Pine.Ui.Avalonia` at all, so this iteration's changes cannot be the cause.
- One test class had to be **serialised**: `MessengerLifecycleTests` passed alone and failed in a
  full run, because `WeakReferenceMessenger.Default` is process-wide and a broadcast from a
  concurrently running test reached a recipient activated here. That is shared state under
  parallelism, not a flaky test, so the collection is serialised rather than the assertions
  loosened.

#### Explicitly NOT done

1. **The streams carried by `ReceiptCapturedMessage` are still owned by nobody.** TD-126.
2. **The four converted code-behind handlers report to the screen but not to the log.** TD-127.
3. **TD-117 was diagnosed, not fixed.**
4. **Nothing was committed.**

#### New debt

| ID | Row | Sev | Impact | Fix | Pri | Status |
|---|---|---|---|---|---|---|
| TD-126 | The `MemoryStream`s carried by `ReceiptCapturedMessage` have no owner and are never disposed | 🟡 | `LiveScannerViewModel` and `CropReceiptViewModel` create them, hand them to a broadcast, and forget them; `CropReceiptViewModel` parks one in `_originalStream` and `ScanReceiptViewModel.ProcessReceiptImageAsync` uses two and disposes neither. They are managed memory rather than OS handles, so nothing leaks a file descriptor — but a receipt image is megabytes, and until TD-34 the holding view model was immortal, so this compounded. Now that recipients die on deactivation the retention is bounded, which is a mitigation, not a fix | Decide the ownership rule for a message payload that is a stream and write it down: either the sender keeps ownership and the message carries a copy, or the recipient takes it and disposes in a `finally`. Then apply it to all four producers | P2 | 🔴 **Open** |
| TD-127 | The converted code-behind handlers can report to the screen but cannot write to the log | 🟡 | The four `async void` handlers are now sync methods that route through `ObservedTask`, so a file-picker or clipboard failure produces a sentence instead of a rethrow on the UI thread. But a view has no `ILogger` — they pass `null` — so `UserFacingError.Report` writes no record, and the TD-14 log has nothing about a failure the user was just told about. That inverts the trade TD-24 made everywhere else, where the detail goes to the log and only the sentence goes to the screen | Give the views a logger, or move these handlers' work onto their view models, which already have one. The second is better: a file picker is a view concern but reading the file is not | P2 | 🔴 **Open** |

### 12.56 Iteration 27 — Phase 2, item 6 (TD-36, TD-37, TD-38), and what the analyzers already knew

**Ticket:** Phase 2 item 6, "Delete dead code: unused `InferenceSession`, empty projects, stub test
projects, Attachment/Rule/Tag if not being built."

The first destructive item, so it began with the question that had to come first: **is any of this
recoverable if the judgement is wrong?** All ten files of the seven candidate projects were tracked
and unmodified, so `git` holds them. That is not a formality in a tree carrying twenty-seven
iterations of uncommitted work. The three entity files were likewise tracked. The one thing created
here that git could not have restored — the new migration — was destroyed mid-iteration by a
mistake, and is discussed below.

#### Two of this row's own claims were wrong

| Claim | Reality |
|---|---|
| §6.2: the `InferenceSession` is dead code in a file that "defines `HeuristicFieldExtractor`", implying the class is dead | **The class is live.** `DefaultLocalAiProvider` constructs it and calls `ExtractFields` on every receipt. Only the *session* was dead |
| That deleting it makes the `Microsoft.ML.OnnxRuntime` package reference removable | **It does not.** `ReceiptDetector` calls `session.Run` twice, over `yolo-receipt-nano.onnx`, which is present. A blanket "no ONNX" rule would have been false |
| §6.12: three stub test projects "built **and run** by CI, creating false green signal" | Built, **never run**. The workflow says so itself, in a comment explaining that running an empty file "would be a green light over an empty file" |
| `docs/12` and `docs/33` cite TD-37 for the empty plugin projects; `docs/10` and `docs/31` cite TD-36 for the dead entities | Both wrong by one row. Four citations corrected |

#### The decisive argument for dropping the entities was not that they were unused

§10 Phase 3 plans attachment storage and a rule engine, so deleting `Attachment` and `Rule` looks
like discarding a head start. The inventory found the fact that settles it: **`Attachment` has no
`Hash` property, while `10-Domain-Model` says "Hash MUST be set".** The shipped entity already
contradicted its own specification. Phase 3's attachment is an encrypted blob written through
`IVaultFileCipher`, which iteration 16 built and which this shape cannot express; Phase 3's rule
engine is to be built "on top of the now-real `CategoryId` relationship", which
`Rule.TargetCategoryId` was not — it was an unconstrained `Guid` the database knew nothing about.
What was deleted is a guess already known to disagree with the design meant to replace it. `Tag` is
simpler: nothing anywhere plans it.

There are **no foreign keys in either direction**, so `DropAttachmentRuleAndTagTables` is pure
subtraction — no cascade, no orphans, no data migration on a live entity.

#### The coupling §10 Phase 3 imposed, satisfied without inventing anything

That section offers exactly two outcomes for the plugin projects: build the host, **or** "formally
retire the two plugin documents and delete the empty projects". Retiring looked like it needed a
fifth status value, which would mean changing the rule iteration 21 built and `DocumentStatusTests`
enforces. It does not. `Aspirational` is defined in that test as *"nothing in the tree does this. It
may still be a good plan."* An empty project implies work in progress; its absence does not. Deleting
them makes `12-Plugin-Architecture` and `33-Plugin-SDK` **more** precisely `Aspirational` than they
were, and both documents are kept, because the plans are intact — only the scaffolding is gone.

#### Falsification, in the shape a deletion requires

You cannot mutate code you removed, so each mutation instead **reinstates** what was deleted or
weakens the guard that now prevents its return.

| # | Mutation | Verdict |
|---|---|---|
| 1 | A fourth shell entity is mapped in `PineDbContext` | killed (both model tests) |
| 2 | The allow-list guard is weakened to a denylist | **invalid** — did not compile |
| 3 | The project-count tripwire's floor is dropped to zero | **invalid** — a category error, see below |
| 5 | The migration stops dropping `Attachments` | killed |
| 6a | The dead `InferenceSession` is reinstated as an unused field | **the compiler killed it**: `CS0169`, an error here |
| 6b | Reinstated in TD-36's *exact* shape — assigned, null-checked, disposed | **the analyzer killed it**: `CA1001` |
| 6c | Reinstated as a dependency with no disposable field | killed by the new architecture test |

**Mutation 3 was a category error of mine, not a finding.** It mutated a *tripwire* and observed that
nothing failed — but nothing guards the guard, by construction, so the result was known before it was
run and proves nothing. Recorded as invalid rather than counted, on the same principle as §12.52's
no-op and §12.50's discarded mutation. That is the third iteration running in which a mutation had to
be thrown out for testing the wrong proposition, which is itself worth noticing: writing a mutation
that can fail for the right reason is harder than writing the fix.

**The most useful result is 6a and 6b: the analyzers were already the guard.** `TreatWarningsAsErrors`
turns `CS0169` ("field is never used") into a build failure, and `CA1001` ("has disposable fields but
is not disposable") into another — and removing `IDisposable` from the extractor is what armed the
second. Two of the three ways this defect could return are now refused by the build rather than by a
test. The third — a dependency on the ONNX runtime with no disposable field — compiles cleanly and
was caught by nothing, so `TheOnlyTypeUsingTheOnnxRuntime_IsTheOneThatRunsInference` was written for
it, in the allow-list idiom §12.50 and §12.52 established. Its allow-list has one entry.

#### A mistake made here, and what it cost

The mutation harness destroyed `20260908040007_DropAttachmentRuleAndTagTables.cs`, truncating it to
**zero bytes**. A multi-line anchor failed to match under CRLF, the helper's restore step then read a
backup that had never been written, and the file was left empty. It was the one artefact in this
iteration that `git` could not restore, because it was new and untracked.

It was found by checking, not by a failing test: `wc -l` reported 0 where the file should have had a
hundred lines. The `.Designer.cs` and the model snapshot were intact, so the migration was rewritten
by hand from the agent's verbatim report and re-verified — 27 migration and model tests green, and
mutation 5 then falsified it properly. **The lesson is the same one §12.54 recorded about `--no-build`
and §12.46 about mtimes: the harness around a measurement fails more often than the measurement.**
Every multi-line anchor in this session's tooling has now failed at least once on CRLF; the editor
used for the source changes normalises line endings and restores them byte-exactly, and it did not
fail once.

#### A tripwire earned its place

`NetworkEgressTests.TheScanActuallyReadsTheSourceTree`, written in iteration 21 as an anti-vacuity
guard, hard-coded "at least fourteen projects under `src/`". Deleting four made it fail. It was
supposed to. The deletion had to come past that assertion rather than past nobody, and the number is
now ten with the reason written beside it.

#### Measured, not asserted

- Unit **854 → 855**, integration **197 → 199**. Both re-run here after a full rebuild.
- Every project that builds on this machine builds with 0 errors. **Android and iOS still compile on
  no machine** — eighteen iterations.
- The solution now lists **ten** projects and **two** test projects; `dotnet build Pine.slnx` loads
  and attempts all twelve.
- **TD-97 was observed twice more, in two more distinct tests** — thirteen observations in thirteen
  tests.
- Documents reconciled: `12-Plugin-Architecture`, `33-Plugin-SDK`, `39-Performance`,
  `40-Accessibility`, `18-Test-Strategy`, `26-Diagrams`, `27-Development-Guidelines`,
  `56-IMPLEMENTATION-PLAN`, `10-Domain-Model`, `31-Rule-Engine`.

#### Explicitly NOT done

1. **`31-Rule-Engine` and the two plugin documents were kept.** Deleting `31-Rule-Engine` would leave
   nine dangling cross-references, and the plans are not the problem — the empty scaffolding was.
2. **The disabled "Rules" button in `ShellView.axaml` was left alone.** It has no command binding and
   says "Coming soon", which is now the whole truth. TD-128 records it.
3. **`27-Development-Guidelines`' layout tree still lists three directories that never existed** —
   `src/Pine.Docs/`, `tools/MermaidValidator/`, `tools/PineCliScripts/`. Left in place with a note,
   because a layout tree is a plan as much as a record and quietly deleting them would hide that the
   plan was not carried out.
4. **Nothing was committed.**

#### New debt

| ID | Row | Sev | Impact | Fix | Pri | Status |
|---|---|---|---|---|---|---|
| TD-128 | The shell's "Rules" button is the last affordance for a feature with no code behind it anywhere | 🟢 | `ShellView.axaml` carries a disabled nav button labelled "Rules" with a "Coming soon" tooltip and no command binding. That was defensible while a `Rule` entity and table existed — the feature was arguably half-built. Iteration 27 dropped both, so the button is now the only trace of the rule engine anywhere in `src/`, pointing at a 313-line specification and nothing else. It is honest in the narrow sense that it is disabled and says so, which is why it was left; it is dishonest in the wider sense the audit has applied elsewhere — TD-47's seeded account, TD-48's cloud-sync radio and TD-84's DIB button were all removed for promising capabilities that did not exist | Decide whether the sidebar advertises unbuilt features at all. If yes, say so once, in one place, rather than per button; if no, remove it and let `31-Rule-Engine` carry the plan | P3 | 🔴 **Open** |

### 12.57 Next

- **TD-117 — diagnosed in §12.54, still unfixed, and now the most expensive open row.** A native
  access violation from concurrent PaddleOCR predictor creation, with a stack and a reproduction.
  Every test count in this document costs two or three runs because of it. Serialise the two tests
  that build the real AI stack, or substitute the provider where the test does not need it.
- **TD-07 — re-arm CI.** Twelve iterations. This iteration deleted seven projects and edited the
  workflow that builds them, with Android and iOS still compiled by nothing.
- **TD-121** — the shipped navigation defect that drops the shell. Three iterations old now.
- **TD-99 — make the budgets screen true or take it down.** Deferred an eleventh time.
- **Phase 2 item 7 — central package management; one PaddleOCR package; Git LFS for the ONNX files
  (TD-41, TD-42, TD-43)**, the next roadmap row. Note this iteration touched the same ground: the
  ONNX package reference was examined and deliberately kept.
- **TD-97 — thirteen observations in thirteen different tests.** Unchanged since iteration 15.
- **TD-126**, **TD-127**, **TD-128**, **TD-124**, **TD-119**, **TD-120**, **TD-115**, **TD-113**,
  **TD-114**, **TD-108**, **TD-106**, **TD-111**, **TD-104**, **TD-107**, **TD-101**, **TD-102**,
  **TD-105**, then **TD-103**.
- **TD-94's lesson**, unchanged and still the largest.
- **TD-125**, **TD-122**, **TD-123**, **TD-46** — now smaller, since the unanchored `ModelPath` it
  named went with the dead session — **TD-50**, **TD-118**, **TD-110/TD-41**, **TD-109**, **TD-93**,
  **TD-89**, **TD-86**, **TD-78**, **TD-75**'s siblings **TD-95**, **TD-96**, **TD-100**, **TD-91**,
  **TD-112**, and **TD-116**.
- **TD-84** last, unchanged.

---

### 12.58 Iteration 28 — Phase 2, item 7 (TD-41, TD-42, TD-43), and the 203 MB nothing had ever read

**Ticket:** Phase 2 item 7, "Central package management; one PaddleOCR package; Git LFS for the
ONNX files."

Three sub-claims, and **every one of them was wrong in some respect** — one stale, one impossible as
written, one false in a way that took three attempts to measure correctly. The row's real value was
that it pointed at the right folder.

#### TD-42 — the half that was already fixed, and the half that was real

The row names two drifts. `Microsoft.Extensions.DependencyInjection` "pinned at both `10.0.11` and
`10.0.9`" was **no longer true**: every project sat at 10.0.11 before this iteration started. The EF
Core half was real and still live — `Microsoft.EntityFrameworkCore.Sqlite.Core` at **10.0.9** against
`Microsoft.EntityFrameworkCore.Design` at **10.0.11**, both pulling the same `Relational` and
`Abstractions` assemblies. NuGet unifies that silently. The build was green, and *no file a reader
could open said which version was actually being built against.*

`Directory.Packages.props` now holds every version and `ManagePackageVersionsCentrally` makes a
`Version` attribute in a `.csproj` an `NU1008` build error. That was verified as a real error, not
assumed: mutation 1 put a version back and the restore refused it by name.

**CPM alone is only half the guard, which is why `BuildHygieneTests` exists.** It does not stop
`Directory.Packages.props` from drifting internally, does not stop a family that must move together
from moving apart *inside* that one file, and does not stop a deleted reference leaving its version
behind. Eight tests cover those, and two of them earn their place immediately: `Avalonia.iOS` and
`Xamarin.AndroidX.Core.SplashScreen` are referenced only by the two heads that **compile on no
machine in this project**, so a scan of the files on disk is now the only thing checking their
package references at all.

#### TD-41 — "keep one" is not achievable, and the reason is in the package's own metadata

`PaddleOcrEngine` names exactly one type: `LocalFullModels.LatinV5`. An agent was sent to find which
of the four packages defines it and came back with the answer plus the thing that makes the row
impossible — `Sdcb.PaddleOCR.Models.Local` is a 36 MB **facade** holding that type, and its own
`.nuspec` declares `Sdcb.PaddleOCR.Models.LocalV5 >= 3.0.0`. The weights are not in the package the
code names; they are in the one it depends on. Removing `LocalV5` from the file would not remove it
from the graph, it would float it down to the 3.0.0 floor and quietly change which weights ship.

So the answer is **two, deliberately**, with the reason written into the test that enforces it.
`LocalV3` (238 MB) and `LocalV4` (234 MB) were the genuine duplication: neither defines
`LocalFullModels`, each defines only its own `KnownModels`, and no file in `src/` or `tests/` named
either. The agent flagged its own nuspec reasoning as **UNVERIFIED by a live restore**, correctly —
so it was verified by one: after removal, `project.assets.json` resolves
`Models.Local`/`LocalV5`/`Shared` and nothing else, and all seven buildable projects compile clean.

#### The finding the row did not know it had: 203 MB nothing had ever read

`Assets/Ai` held four files and the `.csproj` shipped all of them with the glob
`Assets\Ai\*.onnx;Assets\Ai\*.txt`. Exactly one was read by anything. `ReceiptDetector` names
`yolo-receipt-nano.onnx` in a `Path.Combine`. The other three — `PP-OCRv4-det.onnx` (113,352,104
bytes), `PP-OCRv4-rec.onnx` (90,530,732 bytes) and `dict.txt` — were copied into every build output
and every artefact, and **`git log -S` over the entire history, all branches, returns zero commits
naming `dict.txt` or `det.onnx`.** Not "used once and later removed". Never wired, at all.

They had been superseded before they were ever connected: `LocalFullModels.LatinV5` resolves to
embedded resources inside the `Sdcb.PaddleOCR.Models.LocalV5` assembly, and those are
**PP-OCRv5**. The v4 pair on disk was a different model generation for a code path nobody wrote.

An agent was given the explicit job of **refuting** the deletion — hunt for any runtime path,
implicit MSBuild convention, environment variable or directory probe that could reach them. It
failed to refute it and reported the strongest evidence itself: `Sdcb.PaddleOCR.dll` contains no
string for `.onnx`, `Assets`, `GetEnvironmentVariable` or `GetFiles`, and the packages ship no
`build/*.props` or `build/*.targets` at all.

The glob is now a named file, which is the durable half: a glob makes adding 200 MB to the artefact
a matter of dropping a file in a folder, and naming the file makes it a diff with a reviewer.

#### TD-43 — three measurements, two of them wrong, and an agent that overclaimed

The row says the residual is "history predating the conversion, which LFS does not rewrite". An
agent was sent to verify it and returned a confident **"FALSE (refuted, not merely unverified)"**,
on the grounds that no blob over 1 MB exists anywhere and the files were LFS pointers from their
first commit. I ran the same scan and got the same answer.

**We were both wrong, and the way it came out matters more than the answer.** A later run of the
same `awk` pipeline reported *zero blobs of any size*, which is impossible — and that was the tell
that the earlier "no blob over 500 KB" was a **false negative from a silently broken pipeline**, not
a measurement. Re-run properly against a file rather than a pipe:

- Reachable history holds **1,047 blobs totalling 10.3 MiB**, the largest being this document at
  372,666 bytes. That part of the agent's answer was right.
- But `.git/objects` holds three loose blobs of **113,352,104 / 90,530,732 / 13,287,636 bytes** —
  exactly the `size` fields of the three LFS pointers. The raw binaries are there.
- `git fsck --unreachable` reports none of them, because **fsck treats reflogs as roots**.
  `git rev-list --objects --all --reflog` finds all three, at their original paths.

So there *was* a conversion, it *did* leave the raw blobs behind, and the row's instinct was sound.
What is false is the consequence it drew: those blobs hang off this working copy's **reflog**, not
off any branch, so **no clone has ever received one and "every clone pays forever" was never true.**
The residual is ~190 MB of local disk on one machine, which `git gc --prune` reclaims. Nothing to
rewrite, and nothing was rewritten.

**The lesson is the one this audit keeps relearning, now with an agent attached to it:** a
confident refutation from a subagent is still a measurement, and a measurement whose harness can
fail silently is not evidence. The agent did not fabricate anything — it ran a real command and
reported its real output. The command was wrong.

#### Measured, not asserted

- Unit **855 → 863**, integration unchanged at **199**. Both green. Baseline was re-measured at 855
  and 199 *before* any change, and matched §12.56 exactly.
- The unit suite **aborted twice** — once at 759 and once at 748 — and was re-run each time
  (TD-117). **That is the fifteenth observation.** Two of the four unit runs this iteration needed
  were spent on it.
- Artefact, by running `packaging/package.sh`: **690,067,133 bytes unpacked (658 MiB)**, archive
  **306,385,892 bytes (292 MiB)**, 320 files, version reported by the binary itself as
  `0.1.0-alpha.1+6b7e7f0db86a`. Against TD-110's recorded 1.08 GiB / 680 MB, the download roughly
  halved.
- Build output for `Pine.Infrastructure` went from **217 MB of model assets to 13 MB**, on a clean
  rebuild.
- `LocalV3` + `LocalV4` removed: **472 MB** out of the package graph.
- Reachable blob history: **10.3 MiB**. Loose objects on disk: **190.9 MiB**, of which 189 MB is the
  three reflog-only raw ONNX blobs.

#### Falsification

Ten mutations, **9 killed, 1 survived by design**, and one discarded as invalid before it counted.

| # | Mutation | Result |
|---|---|---|
| 1 | Pin `Version=` back into a `.csproj` | Killed — **and `NU1008` refuses the restore**, so the build catches it too |
| 2 | Split EF Core back to 10.0.9 / 10.0.11 | Killed |
| 3 | Leave an orphan `PackageVersion` behind | Killed |
| 4 | Re-add the `LocalV4` reference | Killed |
| 5 | Delete a `PackageVersion` that is still referenced | Killed |
| 6 | Set `ManagePackageVersionsCentrally` to false | Killed |
| 7 | Put a dead model asset back in `Assets/Ai` | Killed |
| 8 | Add a project outside the solution | Killed |
| 9 | Widen the `Content` glob back to `*.onnx;*.txt` | **Survived** |
| 10 | Drift the bare `Avalonia` package away from `Avalonia.*` | **Survived, then fixed, then killed** |

**Mutation 9 survives and is left surviving.** Nothing asserts the glob's shape, because a wide glob
ships nothing extra on its own — it only becomes a defect once an unreferenced file exists, and
mutation 7 is the one that catches that. The pair is covered; the glob alone cannot cause the fault.

**Mutation 10 is the one that was worth running.** The family rule matched on the prefix
`"Avalonia."`, which does not match the package named plain `Avalonia` — the one every other
Avalonia package sits on top of. Setting it to 11.2.0 while the rest stayed at 12.0.5 left all eight
tests green. The rule now also matches the bare name, and **the survival was confirmed by reverting
the fix and re-running the mutation against the unfixed test**, rather than being assumed.

**Mutation 9's first attempt was invalid and was thrown out.** The `sed` escaping silently failed,
the file was never modified, and the "pass" it produced measured nothing; it was caught by grepping
the file afterwards and re-run with a different tool. **That is the third iteration running in which
a mutation had to be discarded rather than counted** — §12.50 and §12.52 record the other two.

#### A self-inflicted failure worth recording

`packaging/package.sh` died mid-run with `otnet: command not found`. That was not a defect in the
script: **it was edited while bash was still executing it.** Bash reads a script incrementally by
byte offset, so inserting three lines above the current position shifted the offset and execution
resumed inside the word `dotnet`. The run was discarded and repeated with no edits in flight. It is
recorded because the first instinct was to read it as a bug in the packaging script, and it would
have been written up as one.

#### Explicitly NOT done

1. **`CentralPackageTransitivePinning` was not enabled.** Pinning transitive versions would silence
   `NU1605` on a downgrade — one of the few signals this repository still has about dependencies it
   does not name — and freeze a graph nothing currently audits. A separate decision, needing
   evidence.
2. **No package was upgraded.** Every version carried over unchanged except the one this row
   required (`Sqlite.Core` 10.0.9 → 10.0.11). Deciding when to take a release is a judgement with a
   changelog behind it, and doing it inside a mechanical refactor would hide it.
3. **`git gc --prune` was not run**, and no history was rewritten. The 190 MB is local to one
   machine, pruning drops reflog entries, and TD-43 turns out to need no change to the repository at
   all.
4. **`yolo-receipt-nano.onnx` stays in git via LFS.** It is 13 MB, it is read, and fetching it on
   first use is TD-110's open half, not this row's.
5. **Nothing was committed.**

#### New debt

| ID | Row | Sev | Impact | Fix | Pri | Status |
|---|---|---|---|---|---|---|
| TD-129 | There is no lockfile, so a restore is still not reproducible | 🟡 | CPM pins every version this repository *names*, which is the drift TD-42 recorded — but transitive versions still float, and the CI cache key is a hash over the project files rather than over a resolved graph. Two restores of the same commit on two days can produce different transitive assemblies, and nothing would show it. `Sdcb.PaddleOCR.Models.Shared 2.7.0.1` is in the graph today by resolution, not by any decision recorded anywhere | Add `packages.lock.json` with `RestoreLockedMode` in CI, or write down why floating transitives are acceptable for an application that ships self-contained | P2 | 🔴 **Open** |
| TD-130 | An agent's confident refutation was wrong, and the harness that produced it could fail silently | 🟡 | §12.58 records the case: a broken `awk` pipeline reported "no blob over 500 KB" when three blobs of 113 MB, 90 MB and 13 MB were sitting in `.git/objects`. Both the agent and this iteration reported the false negative before a second, impossible result exposed it. Every measurement in this document taken through a shell pipeline has this failure mode, and it is invisible — a broken pipeline and a true negative look identical | Prefer commands that write a file and then assert on the file's own row count; make any scan state how many objects it examined, so a zero is distinguishable from a silence. `NoCentralVersionIsUnreferenced` already does this and it is why it is trustworthy | P2 | 🔴 **Open** |
| TD-131 | 203 MB of model weights shipped in every artefact for a code path that was never written | 🟢 | Closed by this iteration, and registered because the *class* of defect is not: `PP-OCRv4-det.onnx`, `PP-OCRv4-rec.onnx` and `dict.txt` were added in `bec0887` alongside a model that is used, shipped by a glob, and named by no commit on any branch in the repository's history. Nothing in the build, the tests, CI or packaging noticed for months. The general rule — an artefact should not contain a file no code names — now has one test behind it (`ShippedModelAssetsAreNamedByProductionCode`) covering exactly one folder | Decide whether the rule should cover the whole published output rather than `Assets/Ai`. The artefact is 320 files and this iteration only proved four of them were justified | P3 | 🟡 **Partial** — the folder is guarded; the artefact is not |

### 12.59 Next

- **TD-117 — diagnosed in §12.54, still unfixed, and now observed fifteen times.** It cost this
  iteration a full re-run again. Serialise the two tests that build the real AI stack, or substitute
  the provider where the test does not need it. Every test count in this document is more expensive
  than it should be because of one unfixed row.
- **TD-07 — re-arm CI.** Thirteen iterations. This iteration edited the workflow again — the cache
  key had to learn about `Directory.Packages.props`, without which a version bump changes no
  `.csproj` and restores against a stale cache. That fix has never run.
- **Phase 2 item 8 — `CategoryId` as an FK on splits; dispatch or remove the domain events
  (TD-32, TD-33)**, the next roadmap row, and the first one since item 4 to touch the schema.
- **TD-121** — the shipped navigation defect that drops the shell. Four iterations old.
- **TD-99 — make the budgets screen true or take it down.** Deferred a twelfth time.
- **TD-130 is new and outranks most of what follows**, because it is about whether the numbers in
  this document can be trusted at all.
- **TD-97 — still thirteen observations in thirteen different tests.** Unchanged since iteration 15.
- **TD-129**, **TD-126**, **TD-127**, **TD-128**, **TD-124**, **TD-119**, **TD-120**, **TD-115**,
  **TD-113**, **TD-114**, **TD-108**, **TD-106**, **TD-111**, **TD-104**, **TD-107**, **TD-101**,
  **TD-102**, **TD-105**, then **TD-103**.
- **TD-94's lesson**, unchanged and still the largest.
- **TD-125**, **TD-122**, **TD-123**, **TD-46**, **TD-50**, **TD-118**, **TD-110**'s remaining half
  — the ~292 MiB that is still bundled rather than fetched — **TD-109**, **TD-93**, **TD-89**,
  **TD-86**, **TD-78**, **TD-75**'s siblings **TD-95**, **TD-96**, **TD-100**, **TD-91**,
  **TD-112**, **TD-131**, and **TD-116**.
- **TD-84** last, unchanged.

---

### 12.60 Iteration 29 — Phase 2, item 8 (TD-32, TD-33), and the feature that had never once run

**Ticket:** Phase 2 item 8, "`CategoryId` as an FK on splits; dispatch or remove domain events."

TD-32 is filed as a modelling complaint: categories are modelled twice, and a rename would orphan
history. That is true, and it is the least interesting thing about it.

#### The finding: the categoriser had never been trained, and nothing said so

`CategorizationTrainingBackgroundService` built its training set like this:

```csharp
CategoryId: Guid.TryParse(s.Category, out var g) ? g : Guid.Empty
...
.Where(d => d.CategoryId != Guid.Empty).ToList();
```

`TransactionSplit.Category` is a **name**. It comes from a free `TextBox` on the add-transaction
screen, from a `TextBox` on the receipt screen, or from a CSV column. `"Groceries"` does not parse
as a `Guid`, so every example failed the parse, the filter removed all of them, `trainingData.Count`
was zero, `TrainModelAsync` was never called, and the watermark that records progress was never
written.

Nothing failed. The service woke on its timer, read **every transaction in the vault**, built an
empty list, skipped the training call because the list was empty, and went back to sleep. That is
the shape this audit keeps finding and §12.38 named outright: a green suite is not evidence of
correctness, and here there was not even a red log line.

The same type mismatch broke the other end of the feature. `CategorizeTransactionQueryHandler` and
`ImportTransactionsCommandHandler` both took the model's predicted `Guid` and resolved it through
`ICategoryRepository.GetByIdAsync` — against a `Categories` table that **no code path in this
application has ever written a row to**. The lookup returned `null`, the suggestion was dropped, and
the import applied no category, however confident the model was. So the feature was broken at both
ends by the same seam, and the two halves hid each other: an untrained model would have made poor
predictions, and the predictions it did make were discarded before anyone could see them.

#### Which model won, and why it is not the one the row named

TD-32 prescribes storing a `CategoryId` on the split. That direction was **not taken**, and the
reason is now in the code rather than in this document alone: it needs `Category` rows to exist.
They do not. `CreateCategoryCommand` is called by tests only, there is no query that lists
categories, no screen creates one, and every category input in the UI is a free `TextBox`. Pointing
the split at a foreign key into a permanently empty table would have replaced a broken feature with
an unusable one, and would have made the ledger depend on a feature nobody has built (TD-132).

So the string won, because it is the model that has instances. `TrainingExample` (Pine.Core) carries
a `string Category`, `CategorisationResult` carries a `string Category` where an empty string means
"no suggestion", and `ICategoryRepository` left both handlers entirely.

This is the fourth iteration in a row to reject a row's prescribed remedy in writing — §12.44,
§12.46 and §12.52 are the others — and the pattern is worth naming: **the audit's diagnoses have
held up far better than its prescriptions.** The diagnosis here ("categories modelled twice") was
exactly right and pointed at the correct file. The remedy assumed a precondition the codebase does
not satisfy.

#### The half of TD-32 that was taken verbatim

`Category.ParentCategoryId` had no foreign key: the original `CreateTable` emitted no
`table.ForeignKey` for it and `PineDbContext` had no `HasOne` either, while `Budget.CategoryId`
twenty lines away has had a real constraint the whole time. It now has one, `Restrict`, with a
migration.

**It is covered by integration tests rather than a mapping assertion, deliberately.** In SQLite a
foreign key does nothing unless `PRAGMA foreign_keys` is ON, and that is a per-connection setting
rather than a property of the schema — so a test that read the model or the migration would pass
identically whether or not the constraint had any effect at runtime. The tests write a violating row
through the real provider against a real SQLCipher vault and require the database to refuse it.

Two things were **measured rather than assumed** while writing them:

1. **Deleting a parent through EF succeeds**, and the child is silently promoted to a root. The
   first version of that test asserted a refusal and got none: EF issues
   `UPDATE ... SET ParentCategoryId = NULL` for the tracked dependent first, so the database
   constraint never fires. Straight at the database, with nothing tracked, SQLite does refuse. Both
   behaviours are now asserted, because they disagree and the system really does behave both ways
   (TD-134).
2. **A cycle is still possible.** A foreign key constrains each row on its own, so A→B→A satisfies
   every constraint. There is a test asserting that limit, so it is visible in the suite instead of
   being an unwritten assumption.

#### TD-33 — remove, and the specification is the reason it is safe to

The row says "dispatch or remove". Remove, for reasons that were checked rather than assumed:

- No dispatcher exists, and none ever did: `git log -S "IDomainEventDispatcher"` and
  `-S "INotificationHandler"` return **no commit on any branch**. Nothing was built and later lost.
- `PineDbContext.SaveChangesAsync` — the only place a dispatcher could hook in — enumerates the
  change tracker solely to bump concurrency tokens (TD-17). It has never looked at an event.
- The two mutations that raised events already do their work synchronously and atomically:
  `RecordTransactionCommandHandler` calls `Deposit`/`Withdraw` on the same scoped context, and
  `VoidTransactionCommandHandler` reverses the effect with a written argument for why it must not
  recompute the ledger instead. A dispatcher would have had nothing to deliver to.

The decisive check was the specification, not the code. `10-Domain-Model` §5 does specify
cross-context events — and **the deleted types did not implement it**. The spec names
`TransactionRecorded` carrying a category; the code had `TransactionRegisteredEvent` without one.
`AccountCreatedEvent` is not in the spec's table at all. Every subscriber the spec lists — Reports,
Budgets, Rule Engine, Forecasting, Sync, Notifications — is a context with no source files, and the
Rule Engine's entity was deleted by iteration 27. So nothing was lost that a future implementation
would have wanted: it will be written against §5, which the deleted records did not match.

`AggregateRoot` survives as an empty marker, and `AggregateRootMarkerTests` is what stops it being a
leftover: the four types a repository loads and saves are roots, `TransactionSplit` and
`TransactionLineItem` are not, and the event machinery may not come back without a dispatcher.

#### A test was deleted, and that is a cost

`Void_ShouldRaiseExactlyOneVoidedEvent_CarryingTheOriginalSignedAmount` pinned a real decision — the
event carried the original signed amount rather than its inverse, so that a future consumer could
not silently read every voided expense as income. Its subject no longer exists, so it went. The
decision it protected is recorded in `AggregateRoot`'s remarks instead, which is weaker than a test
and is written down here so that the trade is visible rather than discovered.

#### Measured, not asserted

- Unit **863 → 873**, integration **199 → 205**. Both green. The baseline was the verified end state
  of iteration 28 in the same working tree, not a re-derived number.
- The categoriser's training projection now produces examples: `CategorisationTrainingDataTests`
  covers seven properties of it, and mutation 1 below is the proof that it produced none before.
- Six integration tests write against a real SQLCipher vault to prove the new foreign key is
  enforced, including the two behaviours that disagree.
- One integration test failed once in a full run and **passed in isolation**, then passed in the
  next full run — interference between tests, not a defect. That is the TD-97 pattern in a
  fourteenth distinct test.
- The unit suite **aborted once at 770** (TD-117) and was re-run. **That is the sixteenth
  observation**, and the third iteration running in which stating a test count cost an extra run.
- A background run of the integration suite was **killed by the operating system for low memory**
  and produced no result; it is recorded because the notification could easily have been read as a
  test failure, and was not one.

#### Falsification

Eight mutations, **7 killed, 1 survived and led to a real fix**.

| # | Mutation | Result |
|---|---|---|
| 1 | Restore the **exact shipped projection** (`Guid.TryParse` + `!= Guid.Empty`) | Killed — **6 of 7 tests red** |
| 2 | Swap the label and the text in the projection | Killed |
| 3 | Stop trimming the category | Killed |
| 4 | Fold the category to lower case | Killed |
| 5 | Re-add the domain-event list to `AggregateRoot` | Killed |
| 6 | Make `TransactionSplit` derive from `AggregateRoot` | Killed |
| 7 | Drop the empty-suggestion guard in the query handler | **Survived** |
| 8 | Drop the confidence check on the import path | Killed |

**Mutation 1 is the evidence for this whole entry.** It is not a hypothetical defect: putting the
production code back turns the suite red, which is the difference between claiming the categoriser
was never trained and demonstrating it.

**Mutation 7 survived, and the gap was real.** Nothing asserted that a prediction with an empty
category and *full* confidence produces no suggestion — precisely the state the categoriser reports
when it has no model, when the vault is locked, or when the stored model is unusable. Without the
guard the UI would have offered the user a blank category at 100% confidence. A test was added and
the mutation re-run against it: it now dies. **That is the fourth iteration running in which a
surviving mutation exposed a genuine hole rather than merely a missing assertion** (§12.48, §12.50,
§12.58 are the others).

#### Explicitly NOT done

1. **No category-management feature was built.** No picker, no list query, no create screen. That is
   product work, it is what TD-132 records, and inventing it inside an audit row would be exactly
   the "promising capabilities that do not exist" this document penalises elsewhere.
2. **No cycle guard on `ParentCategoryId`.** A foreign key cannot express it, and the only writer is
   a command with no caller. Enforcement written for a caller that does not exist is scaffolding by
   another name; the limit is asserted by a test instead.
3. **`Budget` still does not derive from `AggregateRoot`**, and its `Limit` still uses the divergent
   `HasConversion`. §5 files both under TD-33, but the converter divergence is deliberate, is the
   one place the read path tolerates a legacy currency, and is pinned by
   `LegacyMoneyToleranceTests`. Unifying it would make a legacy vault unopenable.
4. **The watermark's unbounded `GetAllAsync` was left alone** (TD-133). It was harmless while
   training never ran; now that training runs it is a real cost, and it is a different row.
5. **Nothing was committed.**

#### New debt

| ID | Row | Sev | Impact | Fix | Pri | Status |
|---|---|---|---|---|---|---|
| TD-132 | The `Category` aggregate has no production writer, so the `Categories` table is empty in every real run | 🟡 | `CreateCategoryCommand` is called by tests only; there is no query that lists categories; no screen creates one; every category input in the UI is a free `TextBox`. The consequences reach further than they look: `Budget.CategoryId` is a foreign key into an empty table, so a budget can never be created against a real category, and `BudgetsView` binds `CategoryName`/`CategoryColorHex` that nothing can supply — which is part of why TD-99's budgets screen is not true. This row is what made TD-32's prescribed remedy impossible to take honestly | Either build category management (create, list, pick, rename) and then move the split to a `CategoryId` as TD-32 asked, or delete the `Category` aggregate and let the string be the model — but not both, and not neither | P2 | 🔴 **Open** |
| TD-133 | The training service reads every transaction in the vault on every poll, unpaged | 🟢 | `ProcessBatchAsync` calls `ITransactionRepository.GetAllAsync` and materialises the whole ledger to count categorised transactions and build the training set. This was invisible while the training set was always empty and the work was thrown away; **iteration 29 made the training real, so the cost is now real too** and it grows linearly with the ledger for a background job that runs on a timer | Count in SQL rather than in .NET, and page the training set. Related to TD-31, which records the same shape in `RecalculateAccountBalanceCommand` | P3 | 🔴 **Open** |
| TD-134 | Deleting a category through EF silently promotes its children to root categories | 🟢 | Measured in iteration 29, not assumed: with the dependent tracked, EF issues `UPDATE ... SET ParentCategoryId = NULL` before deleting the principal, so the new `Restrict` foreign key never fires and the delete succeeds. The row that survives is legal — a root category is valid — so nothing is corrupt; what is lost silently is where the user had filed it. Straight at the database the same delete is refused, so the two paths give different answers | Decide it deliberately when a delete path exists: refuse, reparent to the grandparent, or ask. Today there is no delete command at all, so the behaviour is only reachable from a test | P3 | 🔴 **Open** |

### 12.61 Next

- **Phase 2 item 9 — the coverage floor in CI**, the last row in the phase, and the only one left.
  Its second half was done by iteration 24; the floor itself has never been set. Note that it lands
  on top of **TD-07**: a coverage gate in a workflow nothing triggers is a gate in name only.
- **TD-07 — re-arm CI.** Fourteen iterations. This is now the blocker for the row that follows it.
- **TD-117 — still unfixed, sixteen observations.** It cost iteration 28 two runs and cost this one another.
- **TD-132 is new and outranks most of what follows**, because two open rows now depend on it:
  TD-32's prescribed remedy and TD-99's budgets screen both need categories to exist.
- **TD-121** — the shipped navigation defect that drops the shell. Five iterations old.
- **TD-99 — make the budgets screen true or take it down.** Deferred a thirteenth time, and now with
  a named cause.
- **TD-130** — whether the numbers in this document can be trusted at all.
- **TD-97 — a fourteenth distinct test**, observed again here.
- **TD-129**, **TD-126**, **TD-127**, **TD-128**, **TD-124**, **TD-119**, **TD-120**, **TD-115**,
  **TD-113**, **TD-114**, **TD-108**, **TD-106**, **TD-111**, **TD-104**, **TD-107**, **TD-101**,
  **TD-102**, **TD-105**, then **TD-103**.
- **TD-94's lesson**, unchanged and still the largest — and iteration 29 is now its clearest
  instance: a feature that never ran, under a green suite, for the entire life of the project.
- **TD-133**, **TD-134**, **TD-125**, **TD-122**, **TD-123**, **TD-46**, **TD-50**, **TD-118**,
  **TD-110**'s remaining half, **TD-109**, **TD-93**, **TD-89**, **TD-86**, **TD-78**, **TD-75**'s
  siblings **TD-95**, **TD-96**, **TD-100**, **TD-91**, **TD-112**, **TD-131**, and **TD-116**.
- **TD-84** last, unchanged.

---

### 12.62 Iteration 30 — Phase 2, item 9 (the coverage floor), and Phase 2 closes

**Ticket:** Phase 2 item 9, "Coverage floor enforced in CI."

The row's second half — rewriting the near-vacuous layer assertions — was done by iteration 24. What
was left read like an afternoon: pick a number, add a flag. Neither half of that turned out to be
available.

#### The suite goes red the moment coverage is measured

The first coverage run did not produce a number. It produced **872 of 873**, with
`ArchitectureTests.TheOnlyTypeInTheApplicationLayer_ThatUsesReflection_IsTheValidatorRegistration`
failing:

```
System.TypeLoadException : Could not resolve type
'Coverlet.Core.Instrumentation.Tracker.Pine.Application_f9e9454a-...' in assembly 'Pine.Application'
```

Coverlet instruments by rewriting IL, and one of the things it writes into each assembly is a
tracker type. NetArchTest's `GetTypes()` materialises `System.Type` objects, and materialising that
one throws. The test passes without `--collect` and fails with it, which is the worst shape a
failure can have: **it appears only in the configuration you were about to make mandatory.**

That is the actual content of this row. A coverage floor is trivial to add to a suite that survives
being measured; this one did not. `OurTypesIn(assembly)` now scopes every whole-assembly query to
the `Pine` namespace, so instrumentation is invisible to the rules and nothing is weakened —
no rule here was ever making a claim about a type this project did not write. Under coverage the
suite is **873/873**.

Worth naming: iteration 25 wrote that allow-list test, iteration 24 fixed the two rules that were
conjunctive, and both were correct. The defect was not in the rules but in what a *different* tool
does to the assembly they inspect. Nothing in this repository could have predicted it from reading.

#### The gate parses XML because nothing here can enforce a threshold

An agent was sent to establish what was actually available offline, and came back with the decisive
quotation from the package's own documentation on disk — `coverlet.collector`'s
`VSTestIntegration.md`:

> "VSTest integration doesn't support all features of msbuild and .NET tool, for instance ...
> **threshold validation**."

`coverlet.msbuild`, which does support `/p:Threshold=`, is not referenced and is not in the local
package cache; `reportgenerator` is not installed. Both are network installs. So the gate reads the
cobertura report itself. The agent explicitly labelled the cobertura attribute names **UNVERIFIED**,
having found no report on the machine to check them against — and that was the right call: the first
real run produced one, and `line-rate`, `branch-rate`, `lines-covered` and `lines-valid` are all
there as it had guessed. Verified, not assumed.

#### No exclusions, deliberately

There is no `.runsettings`, no `ExcludeByFile`, no `ExcludeByAttribute`. EF migrations, Avalonia
code-behind and CommunityToolkit's source-generated properties all count against the number.

Excluding code is the standard way a coverage figure stops meaning anything, and it is
indistinguishable from improving one. The cost is that every figure below is **lower** than a tuned
configuration would report — `Pine.Ui.Avalonia` at 28% is carrying a great deal of generated and
boilerplate code. That is the honest direction to be wrong in, and it is written into the script so
the next person sees the decision rather than inheriting the number.

#### The floors, and why they are a ratchet

Measured across 873 unit tests, then rounded **down**:

| assembly | measured | floor |
|---|---|---|
| overall | 61.93% | 61 |
| `Pine.Core` | 87.27% | 86 |
| `Pine.Infrastructure` | 74.44% | 74 |
| `Pine.Application` | 55.83% | 55 |
| `Pine.Ui.Avalonia` | 28.51% | 28 |

A floor above the current number is a wish that fails every run until someone lowers it, which is
how coverage gates get switched off. These are green today and go red on a regression.

`Pine.Core` carries the highest floor because it carries the money. The per-assembly split is the
point: a single overall floor of 61 would be dominated by the UI, and 8,284 lines of Avalonia could
absorb a serious regression in the 2,624 lines of domain without moving it.

**The floors are unit-only.** Integration tests exercise the same assemblies through a real vault
and would raise every number, but the two suites are separate jobs and merging cobertura reports
needs a tool this project does not have. The script says so in its own header rather than letting a
reader assume the figure covers everything.

#### It was executed, not written and assumed

`scripts/coverage-gate.sh` was run end to end: 873 tests, report produced at
`TestResults/coverage/<guid>/coverage.cobertura.xml`, five floors checked, `Coverage gate passed.`
That is the §12.42 standard — `package.sh` was run rather than described — and it is the only reason
this entry can state that the gate works rather than that it exists.

#### The honest limit: TD-07 makes the CI half a gate nobody passes through

The workflow step is real and correct. It will also never fire, because nothing triggers this
workflow and has not since `79c5364`. A coverage gate in a workflow that only runs on manual
dispatch does not gate anything.

So the gate was built to work in the place that currently exists: one command, locally, on any
machine. The CI step is what makes it automatic on the day the triggers come back. This is the
**fifteenth iteration** in which TD-07 has changed what a piece of work can be worth, and this row
is the first one where the dependency is total — the deliverable's entire value is deferred until
that row is closed (TD-136).

#### Measured, not asserted

- Unit **873 → 874**, integration unchanged at **205**. Both green.
- Under `--collect:"XPlat Code Coverage"`, the unit suite is **873/873** where it was 872/873 before
  the architecture rules were scoped.
- Overall line coverage **61.93%**, branch **50.94%**, 9,601 of 15,502 lines — with no exclusions.
- The gate's three failure paths were each exercised and each exits non-zero: a breached floor, a
  missing assembly, and a missing report.
- TD-117 aborted the unit suite once more during this iteration. **Seventeenth observation.**

#### Falsification

Five mutations, **4 killed, 1 survived and exposed a real hole**.

| # | Mutation | Result |
|---|---|---|
| 1 | Coverage below a floor (`Pine.Core` floor raised to 90) | Killed — exit 1, names the assembly and both numbers |
| 2 | An assembly vanishes from the report | Killed — refuses rather than skipping the check |
| 3 | The report does not exist | Killed — exit 1 |
| 4 | Disarm a floor by setting it to `0` | Killed by `TheCoverageGateExistsAndIsArmedInCi` |
| 5 | Replace the CI step's `run:` with `echo skipped` | **Survived** |

**Mutation 5 is the useful one.** The guard test searched the workflow for the string
`scripts/coverage-gate.sh` — and the workflow *explains* the gate in a comment directly above the
step. So the test was reading the explanation and calling it the mechanism: the step could be
neutered while the test stayed green. It now strips comment lines and requires a `run:` line. The
mutation kills it.

**And mutation 5's first re-run was a false pass that was caught.** Tightening the assertion
introduced a `CA1865` analyzer error; `TreatWarningsAsErrors` failed the build, `dotnet test
--no-build` ran the *previous* binary, and the mutation appeared to survive again. That is exactly
§12.40's "recorded as not-a-kill until it compiled", and the only reason it was noticed is that the
build output was read rather than the test result alone. **The harness around a measurement fails
more often than the measurement** — §12.46, §12.54 and §12.58 all say it, and this is the fourth.

#### Explicitly NOT done

1. **No coverage number was improved.** Not one test was written to raise a floor. Setting a
   truthful baseline and raising it are different pieces of work, and doing both at once would have
   made the baseline unauditable.
2. **Branch coverage is measured but not gated.** The report carries it (50.94% overall) and the
   script prints line coverage only. A branch floor is a stricter and more useful gate; it is also a
   second judgement, and one new gate per iteration is enough.
3. **The integration suite is not in the number** — see above; merging reports needs a tool that is
   a network install.
4. **TD-07 was not fixed**, though this row now depends on it entirely. Re-arming CI is a compute
   budget decision that belongs to the project, not to an audit iteration (TD-136).
5. **Nothing was committed.**

#### Phase 2 is closed, and what that does and does not mean

Nine rows, iterations 22–30. What it bought is real: one composition root instead of five hand-copied
ones, configuration that is bound and validated rather than scattered as literals, typed view-model
factories with a layer rule that actually tests something, a transfer that cannot be half-linked,
deterministic view-model lifetimes, 675 MB of dead weight removed from the artefact, every package
version in one file, and a categoriser that runs at all.

What it does not mean is that the engineering quality problem is solved. **Two of the nine rows were
closed by discovering the row's own premise was wrong** (items 7 and 8), and a third found its
prescribed remedy impossible. The pattern across the phase is consistent and worth carrying into
Phase 3: *this audit's diagnoses have held up far better than its prescriptions.* Every row pointed
at the right file. Several told the wrong story about what was in it.

And the largest single finding of the phase was not in any row: the transaction categoriser had
never been trained, under a green suite, for the entire life of the project.

#### New debt

| ID | Row | Sev | Impact | Fix | Pri | Status |
|---|---|---|---|---|---|---|
| TD-135 | Branch coverage is measured on every run and gated on nothing | 🟢 | The cobertura report carries `branch-rate` — **50.94% overall**, against 61.93% line — and `scripts/coverage-gate.sh` reads only line coverage. Branch coverage is the stricter and more useful signal: a line executed once with its `if` never taken is counted as covered, which is precisely how a money-path guard can be "covered" and never exercised. The gap between the two numbers (eleven points overall, twenty-three in `Pine.Infrastructure`, where it is 51.72% against 74.44% line) is the size of what the line floor cannot see | Add branch floors alongside the line floors, set the same way — measure, round down, ratchet. The script already parses the attribute it would need | P3 | 🔴 **Open** |
| TD-136 | The coverage gate's CI half could not fire, and neither could anything else in the workflow | 🟡 | This was TD-07 restated as a consequence rather than a cause, because iteration 30 was the first row whose deliverable was **entirely** deferred by it. `.github/workflows/dotnet-ci.yml` contains a real coverage gate, a real packaging job, real platform-head builds and the full test suite, and it ran only on `workflow_dispatch`. Every regression net Phase 2 built into that file was inert | **Done** (2026-09-10, plan item 0.1), by the one line this column asked for: the coverage floor now runs on every pull request into `main`. It remains a one-command local script as well, so a contributor can answer it before pushing. **What this row did NOT cover, and plan item 0.3 did, on the same day:** a workflow can run, skip its own gate on a condition, and still report green. The gate now writes a receipt from inside `scripts/coverage-gate.sh`, past its own failure branch, and the test job verifies unconditionally that on ubuntu that receipt exists | P1 | ✅ **Closed** — iteration 38; the residual concern is tracked as plan item 0.3, not here |

### 12.64 Iteration 31 - Phase 3, item 1: the platform story, and a finding that was wrong

**Ticket:** Phase 3 item 1, "Decide the platform story honestly. Either invest in per-RID native
runtimes and a non-DirectShow camera abstraction, or narrow the README to the platforms that
actually ship. The Browser head cannot run OpenCV or PaddleInference and should probably be
dropped."

**The decision is: narrow.** The Browser head is deleted, Android and iOS have real identities, and
the per-RID investment is costed, named and deliberately not taken. What follows is the evidence
each half rests on, including the half that turned out to be wrong.

#### The Browser head is deleted, on three independent grounds and a fourth that predates them all

`src/Pine.Ui.Web` is gone: nine files, the `Pine.slnx` entry, the `browser-wasm` leg of
`.github/workflows/dotnet-ci.yml`, the `Avalonia.Browser` pin, the `PineHeadProfile.Browser` enum
member, its row in the capability table, and its `InlineData` rows in two test theories.

The grounds, each checked rather than repeated:

1. **No vault could ever open there.** `AesGcm.IsSupported` is false on the browser runtime, and
   `AesGcmVaultFileCipher` and `AesGcmVaultKeyService` both refuse by name when it is (TD-88).
2. **The receipt pipeline could never exist there.** `OpenCvSharp4` and `Sdcb.PaddleInference`
   publish no WebAssembly runtime package. This is TD-44's WASM clause and it holds.
3. **The database engine was not being linked.** Every build of this head printed
   `@(NativeFileReference) is not empty, but the native references won't be linked in, because
   neither $(WasmBuildNative), nor $(RunAOTCompilation) are 'true'`, naming `e_sqlcipher.a`.
   **This corrects TD-53, whose text said WASM "does not link" the native library. It does --
   `SQLitePCLRaw.lib.e_sqlcipher` ships a `browser-wasm` asset -- the build simply was not asking
   for it.** The row was right about the outcome and wrong about the cause.
4. **`02-Product-Scope` already listed it as a non-goal.** "Web-only or browser-only distribution
   (the product is not a SaaS)" has been in that document's out-of-scope list the whole time.
   **The strongest argument for deletion was not any of the technical ones. The head contradicted
   this project's own scope document, and had done since it was scaffolded.**
   `56-IMPLEMENTATION-PLAN` even carried a note reconciling the two, which is what a contradiction
   looks like when it is managed instead of resolved.

**And §12.63 was wrong about it.** That section wrote "it has never compiled on any machine, and
neither has Android or iOS." `dotnet build src/Pine.Ui.Web/Pine.Ui.Web.csproj -c Release` was run
here before deleting it: **0 errors, 1 warning, and it succeeded with no `wasm-tools` workload
installed.** It compiled. It has compiled in every iteration that recorded a build log. That is
precisely why deleting it beat leaving it as source: a head that builds green is a head that reads
as supported, and this one could not open a vault, scan a receipt, or link its own database engine.

#### Android and iOS have real identities now

`com.CompanyName.TempApp`, `android:label="TempApp"`, `Label = "TempApp.Android"`,
`CFBundleIdentifier=companyName.TempApp`, `CFBundleDisplayName=TempApp` and a `TempApp` string in
the iOS launch screen are all gone. The application id is `org.pine.app` on both, the display name
is `Pine`, and `ApplicationDisplayVersion` now tracks `$(VersionPrefix)` instead of restating `1.0`,
a version this project has never had. The reverse-DNS root matches the download host
`29-Deployment` already names. **This closes the identity half of TD-109 and none of the bundle
half**: there is still no `.apk`, `.ipa`, `.app` or `.deb` definition anywhere, and
`packaging/package.sh` still builds win-x64 and says so.

An application id is not cosmetic on Android -- it is the package's primary key on the device and in
the store, and it cannot be changed after a first publish without shipping a different application.
Leaving it as the template's was the one thing in this row that would have become unfixable.

#### "Invest in per-RID runtimes" is possible, costed, and deliberately not taken

The row offers investment as the alternative to narrowing, and the register has implied throughout
that it was not available. It is. Queried against nuget.org's flat container, **at the exact
versions this repository already pins**:

| Package | Version queried | Result |
|---|---|---|
| `OpenCvSharp4.runtime.osx.arm64` | 4.13.0.20260627 | exists |
| `OpenCvSharp4.official.runtime.linux-x64` | 4.13.0.20260627 | exists |
| `Sdcb.PaddleInference.runtime.osx-arm64` | 3.3.1.70 | exists |
| `Sdcb.PaddleInference.runtime.linux-x64.mkl` | 3.3.1.70 | exists |
| `sdcb.paddleinference.runtime.android` | - | 404 |
| `opencvsharp4.runtime.android` | - | 0 hits |

So the desktop half of the investment is **four `PackageReference` lines at versions that already
resolve**, and the mobile half does not exist at any price. It was not taken, and the reason is
worth stating precisely because it is not the reason the register has been implying: **the blocker
is a runner, not a package.** No machine in this project can execute a macOS or Linux binary. Adding
the pins would flip `PineHeadCapabilities.MacOs.HasReceiptVision` from an honest `false` to an
untested `true`, which is a strictly worse position than the one being left. That is a far
better-defined problem than "Windows-only native runtimes", and TD-44 now says so.

#### The capability table was stating one true reason and one false one

`PineHeadCapabilities.For(Windows)` justified itself with two facts: the win-only native runtime
packages, **and** "the one head whose `Assets/Ai/*.onnx` models are copied to the output."

The second is false. `yolo-receipt-nano.onnx` is a `<Content CopyToOutputDirectory="PreserveNewest">`
item in `Pine.Infrastructure.csproj`, every head has a `ProjectReference` to that project, and
MSBuild therefore copies it into every head's output. Verified by listing three heads' build
folders -- the model is in `Pine.Ui.Macos/bin/.../Assets/Ai/` and was in the Browser head's too.
The managed ONNX runtime is portable and never was the thing making vision Windows-only.

A true reason beside a false one is worse than one reason alone, because the false half is what a
reader spot-checks -- and they find the file exactly where the comment says it is.

Two tests now hold the corrected version, which is the part of **TD-115** that was reachable:
`TheOnlyRidPinnedNativeRuntimes_AreTheWindowsOnes_WhichIsWhatTheCapabilityTableClaims` reads
`Directory.Packages.props` and fails if any RID-pinned native runtime other than the two win ones
appears -- the exact silent drift TD-115 names -- and
`TheModelAsset_IsShippedByTheSharedProject_AndByNoHead` pins the asset where it belongs. Neither
verifies a published artefact per RID, which is the rest of that row and stays open.

#### The finding this iteration got wrong, and how

`SQLitePCL.Batteries_V2.Init()` appeared exactly once in all of `src/` -- at
`Pine.Ui.Windows/Program.cs:19` -- defended by a comment reading "it is a process-global native
initialisation, not a registration, **and each head's provider bundle differs**." The emphasised
clause is false: `SQLitePCLRaw.bundle_e_sqlcipher` is pinned once and reaches every head through the
same `ProjectReference`.

From there the conclusion looked forced. This project references
`Microsoft.EntityFrameworkCore.Sqlite.Core`, the variant whose whole purpose is to omit the
auto-arming bundle, so the missing call should mean the first `SqliteConnection.Open()` throws and
**macOS, Android and iOS could not open a vault at all**. That was written up as the iteration's
largest finding, and it was reported as such.

**It is not true.** The falsification pass is what caught it: mutation M3 neutered
`PineSqliteProvider.EnsureInitialised()` so it called nothing, and every test in the new file stayed
green. Rather than patch the test until the mutation died, the premise was probed. A standalone
console project outside this repository, referencing only
`Microsoft.EntityFrameworkCore.Sqlite.Core` 10.0.11 and `SQLitePCLRaw.bundle_e_sqlcipher` 2.1.11,
in a clean process with no `Init()` call anywhere, opened a `Password=` connection and round-tripped
a row:

```
RESULT: OPENED WITHOUT Batteries_V2.Init()
```

The mechanism is inside `Microsoft.Data.Sqlite.dll` itself, which carries the UTF-16 literals
`"SQLitePCLRaw.batteries_v2"`, `"Batteries_V2"` and `"Init"`: it locates the bundle **by reflection**
and initialises it unasked. The explicit call is redundant on this package set, and has been for as
long as this package set has been pinned.

**What was kept, and honestly relabelled.** `PineSqliteProvider` and the call from
`PineCompositionRoot.Build` stay, and every comment about them was rewritten to say what they are:
not a bug fix. Two arguments survive. One head of four doing something the others do not, justified
by a false clause, is the divergence class TD-29 exists to remove. And `Microsoft.Data.Sqlite`
finding the bundle **by assembly name through reflection** is exactly the shape a trimmer may
remove, on the two AOT heads that never made the call -- an explicit call is a static reference the
trimmer must keep. **That second argument is reasoning, not a measurement, and is labelled as such
in the source**: no machine here can build a trimmed Android head (TD-07).

**M3 is left alive on purpose.** Killing it would require asserting that the explicit call is
required, which is false. A test that passes for a reason nobody checked is how this finding went
wrong in the first place.

#### TD-117's abort is closed, and this iteration could not have been measured without it

Not the row's target, and it had to be done: every test count in this document since iteration 24
has cost two or three runs, and the first two full runs attempted here both aborted -- at 768/874
and 760/874, with `Fatal error. 0xC0000005` at
`Sdcb.PaddleInference.Native.PaddleNative.PD_PredictorCreate2`, reached through
`DefaultLocalAiProvider`'s constructor.

Iteration 26 diagnosed it and named the fix. It is applied now: `ProcessWideStateTestGroup` is a
`[CollectionDefinition(DisableParallelization = true)]` holding the three classes that build a real
container on the Windows profile -- `PineCompositionRootTests`, `ViewModelFactoryTests` and
`MessengerLifecycleTests`. xunit runs classes in parallel; three of them entering
`PD_PredictorCreate2` at once takes the host down, which is why the run prints a passing count and
then "test run aborted" with no failing test to name. It absorbs the old `"Messenger"` collection,
which existed for the same category of problem -- one process-wide resource, several classes, run
concurrently -- and a class can only belong to one collection.

It was falsified rather than assumed. Removing `ViewModelFactoryTests` from the collection, the
suite completed twice and **aborted on the third run at 650/865**. With the collection in place:
**six consecutive complete runs**, including one under coverage instrumentation. The
misspelled-key half of TD-117 is untouched.

#### Falsification

Seven mutations. **Five killed, one survived and was left alive deliberately, one required three
runs to kill.**

| # | Mutation | Result |
|---|---|---|
| M1 | Delete `PineSqliteProvider.EnsureInitialised()` from `PineCompositionRoot.Build` | **Killed** -- `TheSharedRoot_ShouldArmTheProvider_AndNoHeadShould` |
| M2 | Put `Batteries_V2.Init()` back into `Pine.Ui.Windows/Program.cs` | **Killed** -- same test; the head half is not decorative |
| M3 | Make `EnsureInitialised()` call nothing | **SURVIVED -- and it is the most valuable result in this iteration.** It refuted the finding above; see that section |
| M4 | Pin `OpenCvSharp4.runtime.osx.arm64` without touching the capability table | **Killed** -- `TheOnlyRidPinnedNativeRuntimes_...`, which is TD-115's named drift |
| M5 | Give `Pine.Ui.Windows` its own `.onnx` `Content` item | **Killed** -- `TheModelAsset_IsShippedByTheSharedProject_AndByNoHead` |
| M6 | Drop `src/` below ten projects | **Killed by an existing test, as designed** -- `NetworkEgressTests.TheScanActuallyReadsTheSourceTree` went red on the real deletion, not on a synthetic mutation. Its floor was lowered from 10 to 9 as the deliberate edit it is built to demand |
| M7 | Remove `[Collection("ProcessWideState")]` from `ViewModelFactoryTests` | **Killed on the third run** -- the abort returned at 650/865. Two runs would have reported a false pass, which is the same trap TD-117 has set for four iterations |

#### Explicitly NOT done

- **The camera abstraction (TD-120).** The row names "a non-DirectShow camera abstraction" as part
  of the *invest* branch. Narrowing was chosen, so it is not owed here. `LiveScannerViewModel:177`
  still reads `new VideoCapture(cameraIndex, VideoCaptureAPIs.DSHOW)` with a same-process fallback
  to OpenCV's default backend, and `OpenCvSharp` still reaches the UI in two files. TD-120 is
  unchanged.
- **Per-RID desktop runtime packages.** Costed above; blocked on a runner.
- **Any packaging bundle for macOS, Android or iOS.** TD-109's second half.
- **`WasmBuildNative`.** Would have been the first step of making the Browser head real. The head
  was deleted instead.
- **Deleting Android and iOS.** They are first-class targets in `00-Vision` and in
  `02-Product-Scope`'s in-scope list. Unlike the browser head, nothing in this project's own
  documents contradicts them.

#### Verified state

- `Pine.Ui.Windows`, `Pine.Ui.Macos`, `Pine.Cli`, `Pine.Tests.Unit`, `Pine.Tests.Integration`:
  **0 warnings, 0 errors** (Release, warnings-as-errors).
- `Pine.slnx` as a whole still fails on `NETSDK1147` for the android and ios workloads -- unchanged,
  pre-existing, and the reason the platform-heads CI job exists.
- Unit **874 -> 865**, integration unchanged at **205**, both green. The unit count FELL because
  deleting a head removes a profile from the `AllProfiles` theories and one `InlineData` row; five
  tests were added.
- Coverage gate re-run end to end and **passed**: overall 61, `Pine.Core` 87, `Pine.Infrastructure`
  74, `Pine.Application` 55, `Pine.Ui.Avalonia` 28, against unchanged floors.

#### New debt

- **TD-137** -- the `Batteries_V2` divergence, recorded with its correct severity rather than the
  one it was first given.
- **TD-138** -- the trimming question the Android and iOS heads cannot answer.

| ID | Row | Sev | Impact | Fix | Pri | Status |
|---|---|---|---|---|---|---|
| TD-137 | `SQLitePCL.Batteries_V2.Init()` was called by one head of four, defended by a comment whose load-bearing clause was false | 🟢 | The call sat at `Pine.Ui.Windows/Program.cs:19` and nowhere else in `src/`, justified by "it is a process-global native initialisation, not a registration, **and each head's provider bundle differs**". The second clause is false: `SQLitePCLRaw.bundle_e_sqlcipher` is pinned once in `Directory.Packages.props`, referenced once by `Pine.Infrastructure`, and reaches every head through the same `ProjectReference`. **This row is registered at 🟢 and not 🔴 because the obvious consequence was tested and is NOT true.** Iteration 31 first wrote it up as a shipped defect — `Microsoft.EntityFrameworkCore.Sqlite.Core` omits the auto-arming bundle, so three heads should have been unable to open a vault at all. Mutation M3 neutered the initialiser and killed no test; a standalone console project outside this repository, referencing only that package pair in a clean process with no `Init()` call anywhere, opened a keyed `Password=` connection and round-tripped a row. `Microsoft.Data.Sqlite.dll` carries the literals `"SQLitePCLRaw.batteries_v2"`, `"Batteries_V2"` and `"Init"` and finds the bundle **by reflection**. Nothing was ever broken | Done: the call moved to `PineCompositionRoot.Build`, the one path every head takes, and the false comment is replaced by the account above. The residual value is the removed asymmetry, not a fixed crash | P3 | ✅ **Done** — iteration 31, §12.64. **Its real lesson belongs to TD-130**: a plausible mechanism, a real asymmetry and a demonstrably false comment together produced a confident finding that a single mutation refuted |
| TD-138 | Whether a trimmed AOT head keeps `SQLitePCLRaw.batteries_v2` is unknown and unanswerable here | 🟡 | `Microsoft.Data.Sqlite` locates the SQLCipher bundle **by assembly name, through reflection** (TD-137). That is precisely the shape an IL trimmer is entitled to remove, and the Android and iOS heads are trimmed AOT targets that never held a static reference to that assembly. If the trimmer drops it, the failure is at the first database access on the two heads no machine in this project can build — the worst place for it. The explicit `PineSqliteProvider.EnsureInitialised()` call is a static reference the trimmer must keep, and is the mitigation already in place; **it is reasoning, not a measurement, and the source says so** | Build a trimmed Android or iOS head and open a vault on it. Needs the workloads and a device or emulator, which is TD-07's blocker restated | P2 | 🔴 **Open** — registered iteration 31, §12.64 |


### 12.63 Next (after iteration 30) - superseded

- **TD-07 — re-arm CI.** Fifteen iterations, and it is now the blocker for the row that was just
  closed: the coverage gate's CI half cannot fire, and neither can the packaging job, the platform
  heads or the test suite. Everything this phase built that lives in the workflow is inert. This
  should be the next thing done, and it is one line.
- **TD-117 — seventeen observations.** It has cost a re-run in four consecutive iterations.
- **TD-132** — the `Category` aggregate with no writer, which two open rows now depend on.
- **TD-121** — the shipped navigation defect that drops the shell. Six iterations old.
- **TD-99 — make the budgets screen true or take it down.** Deferred a fourteenth time, now with a
  named cause (TD-132).
- **Phase 3** begins with the platform story (TD-44), and it should begin by deciding whether the
  Browser head exists at all — it has never compiled on any machine, and neither has Android or iOS.
- **TD-130** — whether the numbers in this document can be trusted at all.
- **TD-135** — branch coverage is measured and ungated.
- **TD-97**, **TD-129**, **TD-126**, **TD-127**, **TD-128**, **TD-124**, **TD-119**, **TD-120**,
  **TD-115**, **TD-113**, **TD-114**, **TD-108**, **TD-106**, **TD-111**, **TD-104**, **TD-107**,
  **TD-101**, **TD-102**, **TD-105**, then **TD-103**.
- **TD-94's lesson**, unchanged and still the largest.
- **TD-133**, **TD-134**, **TD-125**, **TD-122**, **TD-123**, **TD-46**, **TD-50**, **TD-118**,
  **TD-110**'s remaining half, **TD-109**, **TD-93**, **TD-89**, **TD-86**, **TD-78**, **TD-75**'s
  siblings **TD-95**, **TD-96**, **TD-100**, **TD-91**, **TD-112**, **TD-131**, and **TD-116**.
- **TD-84** last, unchanged.

---

### 12.66 Iteration 32 — Phase 3, item 2: localisation, and three risks that were not the risk

**Ticket:** Phase 3 item 2, "Localisation infrastructure before the view count grows (TD-45)."

**The row asked for infrastructure. The defect underneath it was a shipped control that did
nothing.** The Settings screen has offered a "Display Language" picker with English and Spanish
since the multi-vault work. Choosing Spanish wrote `Appearance.Language = "1"` into the vault and
changed not one character on screen, because nothing in `src/` ever read that key for any purpose
and nothing ever assigned `CurrentUICulture`. Its two siblings on the same panel each had two legs:

```
OnSelectedThemeIndexChanged    -> ThemeManager.ApplyVariant(value) + persist    two legs
OnSelectedPaletteIndexChanged  -> ThemeManager.ApplyPaletteIndex() + persist    two legs
OnSelectedLanguageIndexChanged ->            (nothing)             + persist    one leg
```

That is the same class as the budgets progress bar hard-coded to zero (TD-99) and the fabricated
Diagnostics screen (§4, problem 10): a control that promises a feature which does not exist.

#### It was implemented against an approved design, not invented here

`docs/superpowers/specs/2026-08-29-localization-design.md` already existed, marked **"Approved —
pending implementation"**, with twelve numbered decisions, a phase plan and a declared deviation
table. This iteration executed it. Where reality contradicted it, the deviation is recorded in that
document rather than silently taken — which is what made the contradictions findable at all.

#### The document conflict, and which document won

`60-Production-Readiness-Audit` prescribed **`.resx` + `IStringLocalizer`** in TD-45's *Fix* column.
`41-Localization` §5, the normative specification, requires **`.po`/`.json` files keyed by stable
string IDs**. The normative document wins: a *Fix* column is a reviewer's suggestion, a §5 is the
standard. `.resx` would also have shipped translations as satellite assemblies, which is a known
silent casualty of trimming on the two AOT heads that no machine here can build (TD-07). **TD-45's
Fix column is corrected in this document** so the next reader is not sent down that path.

#### Three risks were named as unverified. None of them was the one that bit

The design's §13 listed the risks honestly. What happened to each:

**The dotted-key risk — real question, wrong answer expected.** §13 said Avalonia's binding-path
parser might reject `Settings.Title` inside `[...]`, with "switch the separator to `/`" as the
fallback. Test 7 was written first, as §9 requires, and it **failed** — `target.Text` came back
`null`. So did a `TextBlock` bound to a plain POCO property. So did one bound through a
`DataContext`. **No binding in this suite had ever been evaluated.**

`TestAppInitializer` calls `SetupWithoutStarting()` from a module initializer. That is correct for
what it was written for — a headless Skia surface so `new Bitmap(stream)` works — and it starts no
dispatcher loop. Avalonia schedules binding evaluation onto its UI thread, so with nothing pumping,
**`control.Bind(...)` succeeds and the bound property then reads back `null`**. No exception.
`Dispatcher.UIThread.RunJobs()` threw "the calling thread cannot access this object";
`InvokeAsync` from the test thread timed out after fifteen seconds. Every one of those looked like
the dotted-key risk confirmed, and none of them was it. **Dots work.**

`Avalonia.Headless.XUnit` exists for exactly this and **cannot be used**: its 12.0.5 release depends
on xunit **v3** while this suite is on 2.9.3, so adding it produced `CS0433` on every `[Fact]`,
`[Theory]` and `[Collection]` in 865 tests. `HeadlessUnitTestSession` — the machinery that package
is built on — ships inside `Avalonia.Headless` itself, so `AvaloniaUiThread` wraps it with no new
dependency at all.

**The asset-API risk — routed around rather than resolved.** `AssetLoader` does still exist in
Avalonia 12.0.5, so the risk as written did not materialise. It was declined anyway: `AssetLoader`
resolves through `AvaloniaLocator`, which would make "can this application read its own words"
depend on the rendering platform being configured, and §8 says nothing in localisation may take
down a screen. `EmbeddedResource` + `Assembly.GetManifestResourceStream` is BCL-only and is the
fallback §13 already named. Recorded as a deviation from §5.

**The warnings-as-errors risk — held, and earned its keep.** `CA1001` refused every view model that
took a `LocalizedMessageSlot` without becoming `IDisposable`. A slot subscribes to
`ILocalizer.LanguageChanged` on a process-lifetime singleton, so an undisposed one keeps its view
model reachable forever — **the exact leak shape TD-34 removed from the messenger, reappearing
through a different event.** The analyzer caught it before any test did.

#### And a fourth thing, which the design could not have predicted

`Item[]` is what `System.Windows.Data.Binding.IndexerName` is defined as, and §4 of the design
assumed it. **Avalonia does not listen for it.** Measured against a bound `TextBlock` over a
notifying indexer, on a pumping dispatcher:

```
notify 'Item[]'    : before -> before   (ignored)
notify 'Item'      : before -> after    (re-evaluated)
notify 'Item[a.b]' : before -> before   (ignored)
notify ''          : before -> before   (ignored)
```

The last row is the surprising one: an empty name means "everything changed" under INPC, and it
does not reach an indexer binding here either. `"Item"` is not a preference; it is the only string
that works. `Localizer` raises both, `Item[]` first, because that costs one event and is what any
non-Avalonia consumer would listen for.

#### The glossary gate was taken before the strings, not after

`41-Localization` §8.4 requires a per-locale glossary and the design's §10.1 names it as a gate
before phase 2. Twelve financial terms were agreed with the product owner **first** and written
into `41-Localization` §18: neutral Latin American Spanish, so *transacción* rather than
*movimiento*, *estado de cuenta* rather than *extracto*, *recibo* rather than *ticket*, and
*anular* rather than *eliminar* — because a ledger voids, it does not delete, which is a domain
rule and not a wording preference. Deciding this afterwards would have meant revisiting 260 strings
instead of one table.

#### Five parallel agents, four of which died mid-edit

Phase 2 was dispatched as five agents over disjoint view sets, each writing its keys to a private
fragment file so concurrent edits could not collide. **Four hit a session rate limit and were
killed after rewriting their `.axaml` files but before writing a single fragment.** The result was
a tree full of `{loc:Tr Some.Key}` references with no values behind any of them.

Recovered rather than redone: `git HEAD` still held the original English, so the pairs were
reconstructed mechanically. **The first attempt was wrong and had to be thrown away** — it matched
each new line to the globally most similar old line, which drifts between two nearly identical
`<TextBlock Text="..." FontSize="14"/>` lines and produced `AddTransaction.AmountLabel = "To
Account"`. The second pass walks `difflib` opcodes, pairs lines positionally inside each replace
block and pairs attributes **by name**, and reports anything that does not align 1:1 instead of
guessing: **205 exact, 6 reported, 0 guessed**. The 6 were resolved by reading `HEAD` directly.

**The agents also produced one thing that could not work.** Two lines read
`StringFormat={loc:Tr Key}` — a markup extension nested inside `StringFormat`, which takes a string
and would have received a `Binding`. Avalonia's compiled bindings rejected it at build time, which
is the right place to find out. Both moved into `ImportWizardViewModel` as composed properties.

#### What was measured, and what the guard test then found anyway

210 literals across 19 views, counted by attribute. After extraction, the guard test —
`41-Localization` §14 ("a first-party UI string MUST NOT be hardcoded") turned into an executable
rule — **found nine more the count had missed**: `AutomationProperties.Name` values, which a screen
reader reads aloud. An untranslated one is a screen that is localised for sighted users and not for
others, which `41-Localization` §11 names explicitly. The attribute was not in the original sweep;
it is in the guard now.

The allow-list holds five entries, each with its reason: the product name, three glyphs, and the
log path template that is shown verbatim so it can be pasted into a shell.

#### Phase 3, and the half of it that is deliberately not done

38 status and error messages in eight view models became keys, and `ViewModels/` now contains
**zero** user-facing literals — phase 3's exit condition, met exactly rather than approximately. They go through
`LocalizedMessageSlot`, which holds the key and its arguments and re-renders on `LanguageChanged` —
without it a screen would go half-translated, every label moving and the status line under them
stranded in the previous language.

**Not done, and named:** the `UserFacingError.Report(...)` fallback sentences, and the
`IUserFacingFailure.UserFacingMessage` sentences in `Pine.Application` and `Pine.Core`. Those live
outside `ViewModels/`, which is where phase 3's exit condition stops, and localising an exception's
own message is a different problem — it crosses a layer boundary the UI deliberately does not
reach across. Registered as **TD-139**.

#### Falsification

Seven mutations, **seven killed**.

| # | Mutation | Result |
|---|---|---|
| M1 | Delete `_localizer.SetLanguage(...)` from the picker's handler — the original defect, restored exactly | **Killed** — `SettingsLanguageTests`, 2 of 7 red |
| M2 | Raise only `"Item[]"`, the name the design assumed | **Killed** — the live-switch test; the string that works is `"Item"` |
| M3 | Remove one key from `es.json` | **Killed** — key parity (`41-Localization` §13) |
| M4 | Put a hardcoded literal back into a view | **Killed** — the §14 guard |
| M5 | Misspell a key in a view | **Killed** — every key a view asks for must exist; without this a typo ships as a button labelled `Shell.Nav.Budgetss`, because the fallback chain renders the key rather than throwing |
| M6 | Stop the message slot unsubscribing from `LanguageChanged` | **Killed** — the disposal test; this is the TD-34 leak |
| M7 | Render an unresolved key as an empty string instead of as the key | **Killed** — a blank button cannot be reported or described over the phone |

#### A false pass worth recording

An incremental `dotnet build` reported **0 errors on a tree that did not compile**: the two
`ImportWizardViewModel` properties were missing and `--no-incremental` failed on them immediately
afterwards. It cost nothing here because the next command caught it. It is recorded because
"the build was green" was, for one command, not evidence of anything.

#### Verified state

- `Pine.Ui.Windows`, `Pine.Ui.Macos`, `Pine.Cli`, both test projects: **0 warnings, 0 errors**.
- Unit **865 → 903**, green. Integration **205**, green — *on a re-run*; see TD-140.
- Coverage gate re-run and **passed**, and the ratchet turned: overall 61 → **63**,
  `Pine.Ui.Avalonia` 28 → **37**, because the UI gained real tests for the first time. Floors
  raised to match.
- **260 keys**, `en` and `es`, with identical key sets asserted by a test that reads the shipped
  resources rather than a fixture — 220 reached from XAML, 40 from C#, and **zero orphans**.

#### New debt

- **TD-139** — the user-facing sentences that live below `ViewModels/` and are still English only.
- **TD-140** — the integration suite acquired an intermittent `ObjectDisposedException` on a
  SQLite handle.

| ID | Row | Sev | Impact | Fix | Pri | Status |
|---|---|---|---|---|---|---|
| TD-139 | User-facing sentences below `ViewModels/` are still English only | 🟡 | TD-45 localised every string in the views and every status and error message in the view models. It stopped at the layer boundary, which is where phase 3's exit condition stops. Two sets remain: the `fallback` sentences passed to `UserFacingError.Report(...)` at 16 call sites, and the `IUserFacingFailure.UserFacingMessage` sentences carried by six exception types in `Pine.Application` and `Pine.Core`. The visible consequence is a Spanish screen whose error line is English — and it is the SAME screen, because `UserFacingError` is the one funnel a caught exception becomes a sentence through. `LocalizedMessageSlot.SetRendered` marks this explicitly: text arriving that way is shown as given and does not follow a language change, because the slot has no key to re-render from | Give `IUserFacingFailure` a key rather than a sentence, and let the UI resolve it. That inverts the current design deliberately — the domain would name the message and the presentation layer would word it — so it is a decision, not a chore | P2 | 🔴 **Open** — registered iteration 32, §12.66 |
| TD-140 | The integration suite acquired an intermittent `ObjectDisposedException` on a SQLite handle | 🟠 | Observed twice in three consecutive runs of iteration 32, **on two different tests** — `VaultBackupServiceTests.PruneAsync_ShouldKeepExactlyTheMostRecentBackups` and `ScaledMoneyMigrationTests.EveryStoredAmount_ConvertsToItsExactCountOfTenThousandths` — with the same shape: `System.ObjectDisposedException: Cannot access a disposed object. Object name: 'SQLitePCL.sqlite3'`, reached through EF Core's `GetAppliedMigrationsAsync` from `VaultBackupService.GetCurrentSchemaVersionAsync`. A third run passed 205/205. **Not caused by the localisation work**: `Pine.Tests.Integration` references `Pine.Core`, `Pine.Application` and `Pine.Infrastructure` and does NOT reference `Pine.Ui.Avalonia`, so nothing this iteration changed is reachable from it. Neither test has been edited since `d608bad`. The cause is not established and is not claimed | It puts the integration suite where TD-117 put the unit suite for eight iterations: every count costs a re-run. Same first move — capture it rather than re-run until green. A pooled connection disposed while another test still holds a command against it is the obvious hypothesis and is unverified. **DIAGNOSED 2026-09-11 (post-audit plan item 2.1), and the obvious hypothesis was wrong in its detail.** The stack was captured in full on a run where three tests failed at once: `SqliteConnection.Open()` → `SqliteConnectionInternal.Close()` → `SqliteConnection.Deactivate()` → `ObjectDisposedException` on a `SQLitePCL.sqlite3`. It is not a handle disposed *while in use* — a probe held a connection open across thousands of concurrent `ClearAllPools()` calls without a single failure — it is **the pool handing out a connection whose handle is already gone**, on the next `Open()`. What disposes pooled handles in this process is `SqliteConnection.ClearAllPools()`, which is process-wide and was called by the test harness in teardown (`TempVault.CloseContexts` and `.Dispose`) while xUnit ran other classes in parallel; the failing tests span both harnesses and include classes that never touch `TempVault`, which is what a process-wide cause looks like. Two further hazards were found beside it: every `DatabaseFixture` built the byte-identical connection string (`:memory:` with an all-zero key), so all of them shared one pool; and the unit suite's own harnesses called `ClearAllPools` twice more, which is a live lead for TD-117. Fix: each harness clears only its own connection strings (`ClearPool`), and each fixture opens a uniquely named in-memory database. **Measured, not assumed** — six clean runs with the fix, and with the fix removed again the failure returned on the first run with the identical stack; the run counts on their own are not significant (Fisher's exact p = 0.24, one-sided) and the mechanism is what carries the diagnosis. **THE INDEPENDENT REVIEW BROKE THE FIX FOUR WAYS AND TWO OF THEM WERE DEFECTS THE FIX INTRODUCED**: clearing only the vault database's pool left backup files pooled and leaked nine encrypted vault directories into `%TEMP%` per suite run — which the write-up had argued away in advance as hypothetical — and giving each in-memory fixture its own NAME added a pooled database to the one harness that had none, because `Microsoft.Data.Sqlite` does not pool a private `":memory:"` but does pool a URI-named one. The guard test was evaded by a one-line block comment. **Chasing the leak found a shipping defect this harness had been hiding**: `VaultRestoreService` verifies a backup by opening it and `await using` returns that connection to the pool, so on Windows `VaultBackupService.PruneAsync` could not delete that backup for the rest of the process — a refused restore was enough. `TempVault` now tracks every connection string it hands out and **throws instead of shrugging** when a vault directory will not delete, which is the assertion that would have caught the leak on the run that introduced it; a full run leaves zero temp vaults behind. Thirteen mutations, thirteen killed. See the plan's §5.1 | P1 | 🟡 **Diagnosed; fixed in the test harness** — iteration 44, registered iteration 32 (§12.66) |

### 12.65 Next (after iteration 31) - superseded

- **TD-07 -- re-arm CI.** Sixteen iterations. It is now also the thing standing between the Android
  and iOS heads and any evidence at all: this iteration gave them real application identities and
  cannot compile either one.
- **TD-117's remaining half** -- the misspelled configuration key. The abort is closed.
- **TD-138** -- whether a trimmed AOT head keeps `SQLitePCLRaw.batteries_v2`. It is the only open
  question left by the SQLite work, and it is unanswerable without TD-07.
- **TD-120 -- the camera seam.** Phase 3's next platform-shaped row, and the half of item 1 that
  narrowing deliberately did not buy.
- **TD-132** -- the `Category` aggregate with no writer, which two open rows depend on.
- **TD-121** -- the shipped navigation defect that drops the shell. Seven iterations old.
- **TD-99 -- make the budgets screen true or take it down.** Deferred a fifteenth time.
- **TD-109's second half** -- a bundle per platform, now that each head has a name to put on one.
- **TD-115's second half** -- assert the capability table against a published artefact per RID.
- **TD-130** -- whether the numbers in this document can be trusted at all. This iteration is a data
  point for it in both directions: §12.63's "never compiled on any machine" was false, and the
  falsification pass caught this iteration's own false finding before it shipped.
- **TD-135** -- branch coverage is measured and ungated.
- **TD-97**, **TD-129**, **TD-126**, **TD-127**, **TD-128**, **TD-124**, **TD-119**, **TD-113**,
  **TD-114**, **TD-108**, **TD-106**, **TD-111**, **TD-104**, **TD-107**, **TD-101**, **TD-102**,
  **TD-105**, then **TD-103**.
- **TD-94's lesson**, unchanged and still the largest.
- **TD-133**, **TD-134**, **TD-125**, **TD-122**, **TD-123**, **TD-46**, **TD-50**, **TD-118**,
  **TD-110**'s remaining half, **TD-93**, **TD-89**, **TD-86**, **TD-78**, **TD-75**'s siblings
  **TD-95**, **TD-96**, **TD-100**, **TD-91**, **TD-112**, **TD-131**, and **TD-116**.
- **TD-84** last, unchanged.

---

### 12.68 Iteration 33 — Phase 3, item 3: attachments, and two mutations that survived

**Ticket:** Phase 3 item 3, "Attachment storage — persist the receipt image encrypted in the vault,
linked to its transaction. The missing half of the OCR feature's audit value."

**The row's phrasing was exact.** Scanning a receipt worked: OCR read it, the fields were filled,
the transaction was written. The image was decoded into a `Bitmap` for the screen and then dropped
— `ScanReceiptViewModel` sent an `AddTransactionCommand` with no image parameter, and nothing in
`src/` wrote a byte of a receipt anywhere. The feature produced a figure and destroyed the document
it came from.

#### The type existed before, and deleting it was right

`Attachment` shipped until iteration 27 and was deleted by TD-37. That deletion is worth restating
because it is the reason this iteration had a clean shape to build: the old entity had
`FileName`, `ContentType`, `SizeBytes`, an unconstrained `TransactionId` that no foreign key
constrained, **no hash at all** while `10-Domain-Model` §7 says "Hash MUST be set", and **no
content** — it was a row describing a file that was never stored. It contradicted its own
specification, so keeping it would have preserved a wrong guess rather than a head start.

Every one of those is different now, and the differences are the feature: the hash exists, it is
verified on the way out, the row is bound by a real foreign key, and the file is actually there.

#### The storage decision went against the plan this document recorded

TD-37 wrote the Phase 3 design down as "an encrypted blob written through `IVaultFileCipher`".
**That was not taken.** Three findings decided it, and the first two are quotations from the
codebase itself:

1. **`IVaultFileCipher`'s own contract refuses the job.** Its remarks read: "THIS IS NOT A
   STREAMING API, AND THAT IS A LIMIT... it is **the wrong shape for a large attachment. Say so
   before reusing it for one.**" The instruction was followed.
2. **A file beside the vault would be in no backup.** `VaultBackupService.CreateBackupAsync` runs
   `VACUUM INTO` on the keyed SQLCipher connection: **it copies `pine.db` and nothing else** —
   verified by reading it, there is no directory copy anywhere in the class. A receipt written to
   `{vault}/attachments/`, following the `ai/` precedent, would have been silently absent from
   every backup and every restore. For the one feature whose entire purpose is an audit trail,
   that is the worst available failure.
3. **A blob in the same database is written in the same unit of work.** There is no window in
   which a file exists with no row, or a row points at no file.

So the bytes are a `BLOB` column in `pine.db`. `10-Domain-Model` §6.2's "Attachments MUST be stored
encrypted at rest" and FR-TXN-02 are satisfied by SQLCipher, which encrypts every page; sealing an
already-encrypted database's contents a second time would add a key path and a failure mode without
adding a guarantee. **The deviation from TD-37's recorded plan is written into the aggregate**, not
left for a reader to reconstruct.

#### What was built

`Attachment` is an aggregate root, not a child of `Transaction`, and that is the one structural
decision worth naming: owning it would mean loading a receipt photograph every time a transaction
is read. It is loaded and saved through its own repository, and `IAttachmentRepository` splits
listing from content deliberately — the bytes are a column on the same row, so the transaction
list's paperclip is drawn from a projection that never names `Content`, and the photograph is read
only when someone asks to see one.

The scanner attaches the DISPLAY image rather than the OCR one: the OCR image is a deskewed,
thresholded derivative made for a machine, and what belongs in an audit trail is the document a
person would recognise. The attachment rides on `AddTransactionCommand` rather than following it,
so `TransactionBehavior` commits the ledger row and its evidence together or neither.

#### Falsification, and the two holes it found

Seven mutations. **Five killed, two survived — and both survivors were real gaps that are now
closed.**

| # | Mutation | Result |
|---|---|---|
| M1 | Change the foreign key to `ClientNoAction` in `PineDbContext` | **Survived, and the mutation was wrong, not the test.** The test asserts the DATABASE constraint, and editing the model does not re-run the migration. Re-run as M1b |
| M1b | Delete the `table.ForeignKey` from the migration | **Killed** — the orphan saved happily, which is exactly what the old shape allowed |
| M2 | `VerifyContent()` stops comparing the hash | **Killed** |
| M3 | Remove the 25 MB size limit | **Killed** |
| M4 | The listing stops projecting and materialises whole entities | **Killed** — the change tracker holds `Attachment` entries, i.e. photographs were read to draw a paperclip |
| M5 | The vault stops being keyed | **Killed** — the raw-bytes scan of `pine.db` finds the receipt in plaintext |
| M6 | The scanner attaches `null` instead of the image | **SURVIVED.** All 27 scan-screen tests stayed green |
| M7 | The read path stops verifying the hash | **SURVIVED.** All 16 attachment tests stayed green |

**M6 is the more embarrassing of the two.** The storage, the hash, the foreign key, the encryption
and the backup were all tested, and the single step that puts a receipt into any of them was not —
so reverting to "scan a receipt and throw it away", the exact defect this row exists to fix, broke
nothing. `ScanReceiptAttachmentTests` closes it.

**M7 means the hash was decoration.** `Attachment.VerifyContent` was covered, but the one call site
that gives a mismatch consequences was not, so a tampered receipt would have been displayed as
though it were the document that was scanned. `AttachmentQueryTests` closes it. Both mutations were
re-run afterwards and both now die.

#### The coverage gate refused, and was right

The first run after the UI work reported `Pine.Ui.Avalonia` at **36%, below its floor of 37%** —
the attachment view-model code was thinner on tests than the existing average. The gate's own
message says "Raise the tests, not the floor." Twelve tests were written for the receipt viewer,
the marker set and the multi-value converter, and only then did the floors move. Lowering the floor
by one point would have taken thirty seconds and left the new code untested; this is the first time
the gate iteration 30 built has actually blocked something.

#### Four tripwires fired, all by design

`AggregateRootMarkerTests` (the roots are enumerated), `MappedEntitySurfaceTests` (the mapped
entities are an allow-list, and `Attachment` had to be moved from the "dropped" list back to the
"allowed" one), and two migration-list assertions in the integration suite. Each was updated as the
deliberate edit it is meant to require. `MappedEntitySurfaceTests`' own message named the history it
exists to prevent: "Adding one to the model without adding it here is how Attachment, Rule and Tag
came to sit in the schema for months with no repository, no query, no command and no consumer."

#### Explicitly NOT done

- **A manual file picker.** FR-TXN-02 says "receipt, contract, photo"; only the scanned receipt path
  is built. Recorded as **TD-141**.
- **Export or download of an attachment.** The image can be viewed and removed, not saved out.
- **More than one attachment per transaction.** The schema supports many — the foreign key is
  one-to-many and the repository returns a list — but the UI opens the first and the scanner writes
  one. Nothing needs changing in the data model when that grows.
- **Re-encoding or downscaling.** The bytes stored are the bytes read, which is what makes the hash
  meaningful. The 25 MB limit is the only size control.
- **Attachments on imported transactions.** A CSV row has no document.

#### Verified state

- `Pine.Ui.Windows`, `Pine.Ui.Macos`, `Pine.Cli`, both test projects: **0 warnings, 0 errors**.
- Unit **903 → 937**, integration **205 → 212**, both green.
- Coverage gate re-run and **passed**, floors raised to match: overall 63 → **65**, `Pine.Core`
  86 → **87**, `Pine.Infrastructure` 74 → **75**, `Pine.Ui.Avalonia` held at **37**.
- The migration was generated by `dotnet ef`, not hand-written, so the model snapshot is consistent.

#### New debt

- **TD-141** — attachments exist only for scanned receipts; FR-TXN-02's manual file path is not built.

| ID | Row | Sev | Impact | Fix | Pri | Status |
|---|---|---|---|---|---|---|
| TD-141 | An attachment can only arrive from the receipt scanner | 🟢 | `07-Functional-Requirements` FR-TXN-02 reads "The system MUST allow attaching files (**receipt, contract, photo**) to a transaction". What exists is the receipt half: `ScanReceiptViewModel` attaches the image it scanned, and there is no file picker on a transaction, no drag-and-drop, and no way to attach anything to a transaction that was typed in or imported. The storage, the hash, the foreign key and the removal path are all general — an attachment does not know where its bytes came from — so this is a missing entry point rather than a missing capability, which is why it is 🟢 rather than 🟡 | Add a file picker to the transaction view that sends the same `Attachment.Create` path, with a media-type allow-list. The 25 MB limit and the hash already apply. A contract is a PDF, so the viewer would need to stop assuming an image | P3 | 🔴 **Open** — registered iteration 33, §12.68 |

### 12.67 Next (after iteration 32) - superseded

- **TD-07 — re-arm CI.** Seventeen iterations. Everything this iteration added — a guard test, a
  key-parity test, a raised coverage floor — is inert until a commit triggers it.
- **TD-140** — the integration flake. It failed twice in three runs, on two DIFFERENT tests, with
  the same `ObjectDisposedException` on a SQLite handle reached through EF Core's
  `GetAppliedMigrationsAsync`. It is not caused by localisation (the integration project does not
  reference `Pine.Ui.Avalonia`), and it makes every integration count in this document cost a
  re-run — which is precisely the position TD-117 put the unit suite in for eight iterations.
- **TD-139** — the exception sentences below the UI layer.
- **TD-117's remaining half** — the misspelled configuration key.
- **TD-138** — whether a trimmed AOT head keeps `SQLitePCLRaw.batteries_v2`.
- **TD-120 — the camera seam.** Phase 3's next platform-shaped row.
- **TD-21 — locale-driven date and number formatting.** Adjacent to this iteration and deliberately
  not bundled into it (design §12); now that a language is a real, user-chosen setting, the gap
  between "the UI is in Spanish" and "`24.50` is still not `24,50`" is visible on screen.
- **TD-132** — the `Category` aggregate with no writer.
- **TD-121** — the shipped navigation defect that drops the shell.
- **TD-99 — make the budgets screen true or take it down.** Deferred a sixteenth time, and it is
  now the last surviving member of the class this iteration just emptied: a control that shows a
  number it does not compute.
- **TD-109's second half**, **TD-115's second half**, **TD-130**, **TD-135**.
- **TD-97**, **TD-129**, **TD-126**, **TD-127**, **TD-128**, **TD-124**, **TD-119**, **TD-113**,
  **TD-114**, **TD-108**, **TD-106**, **TD-111**, **TD-104**, **TD-107**, **TD-101**, **TD-102**,
  **TD-105**, then **TD-103**.
- **TD-94's lesson**, unchanged and still the largest.
- **TD-133**, **TD-134**, **TD-125**, **TD-122**, **TD-123**, **TD-46**, **TD-50**, **TD-118**,
  **TD-110**'s remaining half, **TD-93**, **TD-89**, **TD-86**, **TD-78**, **TD-75**'s siblings
  **TD-95**, **TD-96**, **TD-100**, **TD-91**, **TD-112**, **TD-131**, and **TD-116**.
- **TD-84** last, unchanged.

---

### 12.70 Iteration 34 — Phase 3, item 4: the rule engine, and the fourth false premise in a row

**Ticket:** Phase 3 item 4, "Rule engine, on top of the now-real `CategoryId` relationship."

**There is no such relationship, and this document already knew.** `TransactionSplit.Category` is a
`string` with no foreign key. Iteration 29 rejected making it a `CategoryId` **in writing** (TD-32,
§12.60) because the `Categories` table has no production writer: `CreateCategoryCommand` is called
by tests only, no query lists categories, and every category input in the UI is a free `TextBox`
(TD-132). Iteration 27 had flagged the same premise as untrue when it deleted the old `Rule`
(§12.56). So the roadmap line has been wrong since before it was reachable, and this is the fourth
consecutive Phase 3 row whose stated premise did not survive contact with the code.

The engine is built on the string, for the reason §12.60 gave about the split: **it is the model
that has instances.** A rule's action is a category NAME.

#### What TD-132 made this iteration decide before writing any code

TD-132's Fix column reads: "Either build category management ... or delete the `Category` aggregate
and let the string be the model — **but not both, and not neither**." A `Rule` aggregate with no way
to create a rule would have been that exact defect, shipped in the iteration immediately after the
row that names it — and it would have been the third time (`Attachment`, `Rule`, `Tag` were all
deleted for it under TD-37).

So the rules screen is not an extra; it is the reason the engine is allowed to exist. Create, list,
enable, disable, delete — all reachable, all tested. **It also makes the sidebar's "Rules" entry
true**: that button was already there, disabled and dimmed with a "Coming soon" tooltip. Honest
while nothing evaluated a rule, and a promise.

#### The precedence decision, and why it is not a comparison

A rule and the ML categoriser both want to decide the same field. The order is: **a matching rule
wins outright, and the classifier is not consulted at all.**

The alternative — compare the rule's priority against the model's confidence — was rejected because
the two numbers share no units. A priority is an ordering the user invented; a confidence is a
softmax score. Mixing them makes "why did this end up in Coffee?" unanswerable, which for a ledger
is worse than being occasionally wrong in a predictable direction.

`TheClassifierIsNotEvenConsulted_WhenARuleMatches` is the assertion that makes it real rather than
incidental: asserting only on the resulting category would pass just as well if the model were
called and then discarded — and on the Windows head that call is a real native inference.

Two guardrails are equally deliberate and separately tested:

- **A rule never overwrites a category the user typed.** On the manual path the rules run only when
  the field is blank. An instruction written last week must not silently rewrite what someone typed
  thirty seconds ago.
- **A rule never overwrites a category the imported file supplied.** The CSV already decided.

#### Determinism, which §6.2 asks for and does not fully specify

`31-Rule-Engine` §6.2 says higher priority evaluates first and that a rule is idempotent. It settles
nothing when two enabled rules share a priority and both match — and a user will give two rules the
same priority within a day of having the feature. The evaluator's ordering is therefore **total**:
priority, then creation time, then id. Both tie-breaks are stable, so the answer does not depend on
the order the database happened to return rows in. `EqualPriorityBreaksDeterministically` reverses
the input and requires the same winner.

The repository applies the same ordering in SQL, and an integration test asserts it against a real
vault — because a rules screen that listed rules in a different order from the one they fire in
would be worse than an unordered list.

#### Declared deviations from `31-Rule-Engine`

| Clause | What was built | Why |
|---|---|---|
| §5.2, fourteen operators | Six: contains, equals, startsWith, matches, greaterThan, lessThan | `and`/`or`/`not` are not comparisons but a boolean tree, which is a grammar, a parser and an editor. An OR is written as two rules |
| §5.1, a list of actions | One action: set category | It is the only action anything in this application could carry out today |
| §5.1, YAML/JSON grammar | A form | Nothing reads or writes rule files; a grammar with no importer would be a shell |
| §6.3 sandbox, §7 recurring detection, §8 notifications, §9 plugin packs, §12 conflict surfacing | Not built | Each depends on subsystems that do not exist. FR-BDG-03 and FR-SUB-02 route notifications "via the rule engine"; there are no notifications |

The regex operator is supported because the document's own canonical example is one — and it runs
under a 100 ms timeout, because a pattern a user typed is a pattern nobody reviewed and
catastrophic backtracking on a background import is a hang with no error message.

#### Falsification, and the mutation that found the hole

Seven mutations. **Six killed outright; one survived and is now closed.**

| # | Mutation | Result |
|---|---|---|
| M1 | The rule branch stops applying in the import path | **Killed** — 2 of 8 precedence tests |
| M2 | Remove the evaluator's ordering entirely | **Killed** — priority and the tie-break |
| M3 | Accept a rule with no conditions | **Killed** — such a rule matches everything |
| M4 | A disabled rule matches again | **Killed** — 3 tests |
| M5 | Store operators as ordinals instead of names | **Killed** — inserting an enum member would silently reinterpret every stored rule |
| M6 | Let a rule overwrite the category the user typed | **Killed** |
| M7 | Remove the regex timeout | **SURVIVED.** The malformed-pattern test covers a regex that does not COMPILE and says nothing about one that compiles and then runs forever |

M7's replacement test had to be written carefully: without the timeout the call does not return, so
asserting on it directly would **hang** the suite rather than fail it. The match runs on the thread
pool and is raced against a five-second deadline, so the mutation produces a failing test instead of
a stuck one. Re-run afterwards, it dies.

#### Six tripwires fired, all by design

`AggregateRootMarkerTests` (roots enumerated — `Rule` is a root, its conditions are owned children),
`MappedEntitySurfaceTests` twice (`Rule` moved from the "dropped" list back to the allow-list),
`EfModel_DeclaresOwnedCollectionsOnTransactionOnly` (**`Rule.Conditions` is the second owned
collection in this model, and that test's whole purpose is to demand a round-trip proof for each
one** — `TogglingARule_KeepsItsConditions` is that proof, and it is the TD-13 shape exactly: a
parent updated while its owned children are loaded), and two migration-list assertions.

#### TD-140 recurred, on a third distinct test

`ReceiptDuplicateLookupTests.The_lookup_is_scoped_to_the_account` failed once with the same
`ObjectDisposedException` on a `SQLitePCL.sqlite3` handle, then passed on two consecutive re-runs.
That is a third different test exhibiting the flake registered last iteration. It is not caused by
this work — the failing test touches no rule — and it means every integration count in this document
still costs a re-run.

#### Explicitly NOT done

- **Retroactive application.** §5.1's triggers are `transaction.created` and `transaction.updated`;
  rewriting a reviewed ledger silently is a different feature with a different risk. **TD-142.**
- **Editing a rule.** Rules are created, toggled and deleted. Changing conditions means deleting and
  re-creating, which is why `Rule` has no update path for its conditions at all.
- **Multiple conditions from the screen.** The engine takes up to eight; the form offers one.
  **TD-143.**
- **Actions other than "set category".** Nothing else exists to act on.
- **Rule packs, sandboxing, notifications, conflict surfacing.** §6.3, §8, §9, §12.

#### Verified state

- `Pine.Ui.Windows`, `Pine.Ui.Macos`, `Pine.Cli`, both test projects: **0 warnings, 0 errors**.
  Three `AVLN5001` warnings appeared briefly — `TextBox.Watermark` is obsolete in Avalonia 12 — and
  were fixed rather than tolerated.
- Unit **937 → 990**, integration **212 → 219**, both green.
- Coverage gate **blocked this iteration too**, at 36% against a floor of 37%, and was answered with
  eleven view-model tests rather than a lowered floor. Floors then raised: overall 65 → **66**,
  `Pine.Core` 87 → **88**, `Pine.Infrastructure` 75 → **77**, `Pine.Application` 55 → **57**.
- The migration was generated by `dotnet ef`; one hand edit, commented, to satisfy CA1861.

#### New debt

- **TD-142** — rules do not apply to transactions already in the ledger.
- **TD-143** — the rules screen writes one condition; the engine evaluates up to eight.

| ID | Row | Sev | Impact | Fix | Pri | Status |
|---|---|---|---|---|---|---|
| TD-142 | A rule never touches a transaction that is already in the ledger | 🟡 | Rules fire on import and on manual entry only. Writing a rule after importing a year of statements leaves that year exactly as it was, which is not what "payee contains STARBUCKS → Coffee" reads as to a person who has just typed it. `31-Rule-Engine` §5.1 names only `transaction.created` and `transaction.updated` as triggers, so this is inside the specification — but the specification was written before there was a ledger to disappoint | A preview-then-apply pass: count what would change, show it, and apply in one transaction with the whole set reversible. Retroactively rewriting reviewed financial records without a preview is the version of this feature that must NOT be built | P3 | 🔴 **Open** — registered iteration 34, §12.70 |
| TD-143 | The rules screen writes one condition; the engine evaluates up to eight | 🟢 | `Rule.Create` accepts up to `Rule.MaximumConditions` and `Rule.Matches` ANDs all of them — tested. The form offers a single field/operator/value row, so a two-clause rule ("payee contains X **and** amount below Y") is expressible in the domain, in the database and in the evaluator, and not from the UI. This is a capability the user cannot reach, which is the mirror image of the defect class this project keeps removing rather than an instance of it — nothing is promised on screen that does not work | Let the form add and remove condition rows. The command already takes a list | P3 | 🔴 **Open** — registered iteration 34, §12.70 |

### 12.69 Next (after iteration 33) - superseded

- **TD-07 — re-arm CI.** Eighteen iterations.
- **TD-140** — the integration flake. It did not recur across this iteration's runs, which is not
  evidence that it is gone.
- **TD-141** — the manual attachment path FR-TXN-02 asks for.
- **TD-139** — the exception sentences below the UI layer, now with one more member:
  `AttachmentCorruptedException`'s sentence is English-only like the rest.
- **TD-126** — the undisposed `ReceiptCapturedMessage` streams. This iteration copied out of them
  rather than holding one, which sidesteps the leak for the attachment path and does not fix it.
- **TD-117's remaining half**, **TD-138**, **TD-120 — the camera seam**.
- **TD-21 — locale-driven date and number formatting.**
- **TD-132**, **TD-121**, **TD-99 — make the budgets screen true or take it down.**
- **TD-60** — `ReplaceSplits` mints fresh ids on every save. Its stated risk was "any future
  reference to a split id (rules, attachments, audit trail) would dangle"; attachments now exist,
  and they reference the TRANSACTION rather than a split, so this row is unchanged rather than
  worsened — but it is one consumer closer to mattering.
- **TD-109's second half**, **TD-115's second half**, **TD-130**, **TD-135**.
- **TD-97**, **TD-129**, **TD-126**, **TD-127**, **TD-128**, **TD-124**, **TD-119**, **TD-113**,
  **TD-114**, **TD-108**, **TD-106**, **TD-111**, **TD-104**, **TD-107**, **TD-101**, **TD-102**,
  **TD-105**, then **TD-103**.
- **TD-94's lesson**, unchanged and still the largest.
- **TD-133**, **TD-134**, **TD-125**, **TD-122**, **TD-123**, **TD-46**, **TD-50**, **TD-118**,
  **TD-110**'s remaining half, **TD-93**, **TD-89**, **TD-86**, **TD-78**, **TD-75**'s siblings
  **TD-95**, **TD-96**, **TD-100**, **TD-91**, **TD-112**, **TD-131**, and **TD-116**.
- **TD-84** last, unchanged.

---

### 12.72 Iteration 35 — Phase 3, item 5: the gate in front of sync, and what was actually behind it

**Ticket:** Phase 3 item 5, "Sync — **only** after row versions, audit columns and stable natural
keys exist."

**This row is a gate, not a feature, and the work was to find out whether it opens.** It does not
say "build sync"; it names three preconditions and forbids sync until they hold. So the honest
reading of the item is: establish the three, build the ones that are missing, and do not build the
thing on the other side. That is what was done.

#### The three preconditions, checked rather than assumed

| Precondition | Before | After |
|---|---|---|
| Row versions | **Present and genuinely enforced.** A `Version` shadow GUID on all eight non-owned roots, re-minted inside an overridden `SaveChanges`, with owner-promotion so a child-only edit still bumps the aggregate, and eight integration tests racing two real writers over one keyed file (TD-17, §12.34) | Unchanged — and the comment in `ConfigureConcurrencyTokens` saying "the Phase 3 sync work needs a version on everything regardless" turned out to be right |
| Audit columns | **Absent, and unevenly so.** `Account` and `ImportProfile` had `CreatedAt`/`UpdatedAt` as `DateTimeOffset`; `Attachment` and `Rule` had `CreatedAtUtc` as `DateTime`; `Transaction` had a creation stamp and **no update stamp at all**; `Category`, `Setting` and `Budget` had neither. Every one hand-written at each mutator | `RowCreatedAtUtc` and `RowUpdatedAtUtc` on all eight roots, stamped by the context |
| Stable natural keys | **Provably false for owned children.** `ReplaceSplits` cleared the collection and rebuilt it with `new TransactionSplit(...)` calling `base(Guid.NewGuid())`, and the repository dropped the id entirely on the way in — a split's identity did not survive its own edit (TD-60) | Closed: a child keeps its key across an edit |

#### The audit columns are shadow properties, and the prefix is not decoration

The mechanism is TD-17's, deliberately: a column nothing in the domain can write is a column nobody
can forget to write. The hand-written stamps that already existed are the argument for it —
`Transaction` having a `CreatedAt` and no `UpdatedAt` is not an oversight anyone would defend, it is
what happens when stamping is a convention rather than a mechanism.

`Row` is in the name because **five of the eight roots already have a creation stamp of their own**,
two of them called exactly `CreatedAtUtc`. A shadow property with a colliding name does not fail —
it silently binds to the domain property and creates nothing, which would have left two of the eight
roots unstamped while every test still passed. `Row` also says the right thing: the domain stamp is
when the money moved, this one is when the row was written, and an edit three months later moves one
and not the other. **Mutation M6 is that exact collision, and it kills seven of eight tests.**

They are `DateTime` in UTC rather than `DateTimeOffset`, for the reason TD-31 found the hard way:
SQLite cannot ORDER BY a `DateTimeOffset`, and an audit column that cannot be ordered is worth very
little, since "what changed since" is the only question anyone asks of one.

**Rows written before the migration read `0001-01-01`, and that is deliberate.** Nothing in the
database can recover when an existing row was created, so it says "never stamped" instead of
claiming the migration's own timestamp — which would have made every row in an existing vault assert
it was created at one instant, on a date no user did anything. An audit column is precisely the
wrong place to tell that lie. They correct themselves on first write.

#### TD-60, and the comment that had to be retracted with it

The register's wording was exact: `ReplaceSplits`/`ReplaceLineItems` "mint fresh `Guid` ids on every
detached save". Both halves were true, and the repository's half was worse than the aggregate's — it
built `new SplitDefinition(s.Amount, s.Category, s.Notes)` and never passed the id at all, so
identity was discarded at the boundary before the aggregate got the chance to discard it too.
**Loading a transaction and saving it back unchanged moved every child's primary key.**

The fix is a diff by identity: a definition carrying a known id updates that child in place, one
carrying none is a new child, one nobody names is gone. That required the first mutator either owned
child has ever had — and **that invalidated a load-bearing comment elsewhere.**
`ReconcileOwnedChildren` promoted every `Modified` child to `Added`, on the stated grounds that
"neither type exposes a mutator, so a child that came from the database can never legitimately be
modified". With a mutator, `Modified` is genuinely ambiguous, and promoting the wrong kind INSERTs a
row whose primary key is already taken. The method now asks the database which child ids exist —
once per collection, on the update path only, never on an insert — because that is the only thing
that actually distinguishes the two cases. **Mutation M7 restores the old rule and kills four tests.**

The atomicity guarantee had to be preserved through this and nearly was not: the old code built
children and validated afterwards, which was safe only because it threw before assigning them. The
new code edits in place, so the sum is checked over DEFINITIONS before anything is touched.
**Mutation M3 moves that check after the apply, and exactly one test dies — the one written for it.**

#### What was NOT built, and why that is the row obeying itself

- **No transport, no provider, no envelope, no merge.** `02-Product-Scope` §7.2 puts sync in scope
  *only* as "Local + cloud sync **via plugins**", §7.3 lists synchronisation providers as a plugin
  extension point, and `14-Synchronization` §8 hands the `ISyncProvider` contract to `33-Plugin-SDK`.
  **The plugin host is the next row in this very phase and does not exist** — its two stub projects
  were deleted in iteration 27. Sync cannot be built before the thing the scope document says it
  must be built inside.
- **No network egress.** `NetworkEgressTests` fails on any file under `src/` that so much as names a
  networking type, in a comment included. Its own failure message says the rule is superseded by an
  ADR or not at all. Nothing here goes near it.
- **No logical clock.** The row version is a random GUID. It answers "did this change" and cannot
  answer "which came first", which is what §7's last-writer-wins by `(device_id, lamport_clock)`
  needs. **TD-144.**
- **No tombstones.** Every delete in six repositories is a physical `Remove`; there is no
  `IsDeleted` and no `HasQueryFilter` anywhere. A row deleted on one device is invisible to a merge.
  **TD-145.**

#### The precondition that is still not true as written

The row says "stable natural keys". What now exists is **stable surrogate keys**. Identity survives
an edit, which it did not before — but `Account.Name`, `Category.Name` and `ImportProfile.Name` have
no unique index, and the only genuine unique constraint in the whole schema is `Settings.Key`.
`Transaction.ImportKey` is the closest thing to a business key and is deliberately nullable and
deliberately non-unique. So two vaults can now each keep their rows' identities and still have no
way to recognise that they hold the same account. **TD-146**, registered rather than glossed, so the
gate is not recorded as fully open when one third of it is only mostly open.

#### Falsification

**Nine mutations, nine killed.** No survivors.

| # | Mutation | Result |
|---|---|---|
| M1 | The repository stops passing child ids | **Killed** — 6 of 7 |
| M2 | A matching id never reuses its split | **Killed** — 5 |
| M3 | The split sum is validated after the children are mutated | **Killed** — the atomicity test |
| M4 | Nothing stamps the audit columns | **Killed** — 4 |
| M5 | An update rewrites the creation stamp too | **Killed** — 2 |
| M6 | The shadow column takes the domain's `CreatedAtUtc` name | **Killed** — 7 of 8 |
| M7 | Every `Modified` child is promoted to `Added`, as the old rule said | **Killed** — 4 |
| M8 | A child-only edit no longer promotes its owner | **Killed** — the owner-stamp test |
| M9 | Owned children get their own audit columns | **Killed** — 7 of 8 |

One test had to be rewritten rather than kept: `EditingOnlyTheSplits_MovesTheOwnersStamp` first
wrote through a bare `PineDbContext` and failed with a zero-rows-affected concurrency error. **That
was verified against the unmodified code before anything was concluded from it** — it fails there
identically, so it is pre-existing and not a regression: a bare context has never been a write path
this application has, because the repository is the only thing that corrects EF's state guess.

#### Verified state

- `Pine.Ui.Windows`, `Pine.Ui.Macos`, `Pine.Cli`, both test projects: **0 warnings, 0 errors**.
- Unit **990 → 1003**, integration **219 → 234**, both green.
- **The coverage gate blocked this iteration too**, and for an instructive reason: `Pine.Core` fell
  to 87% against its floor of 88% because the new domain code was covered only by INTEGRATION tests
  and the gate measures the unit suite. Answered with thirteen unit tests over the diff rule itself,
  never a lowered floor. Floors then raised: overall 66 → **67**, `Pine.Infrastructure` 77 → **78**.
- One integration failure on the first full run — `BalanceRecalculationTests`, a test this work does
  not touch — passed in isolation and on two consecutive full re-runs. That is TD-140 on a fourth
  distinct test.
- The migration was generated by `dotnet ef`; one hand edit, commented, recording why the default is
  `0001-01-01`.

#### New debt

- **TD-144** — the row version detects change but cannot order it.
- **TD-145** — every delete is physical, so a deletion cannot be merged.
- **TD-146** — identity is stable but surrogate; there are still no natural keys.

| ID | Row | Sev | Impact | Fix | Pri | Status |
|---|---|---|---|---|---|---|
| TD-144 | The row version detects that a row changed but cannot order two changes | 🟡 | `Version` is `Guid.NewGuid()`, chosen in TD-17 for a good reason — a counter has to be read before it can be incremented, and two writers reading the same counter increment it to the same number. For optimistic concurrency that is exactly right, and nothing about it is wrong today. For a merge it is not enough: `14-Synchronization` §6 orders a change set by "a Lamport clock keyed on the device" and §7 resolves account name, currency and opening balance by last-writer-wins on `(device_id, lamport_clock)`. Neither a device id nor a logical clock exists anywhere in `src/` — a grep for `device_id`, `lamport` and `tombstone` returns nothing at all. The new `RowUpdatedAtUtc` is a wall clock, and wall clocks on two machines disagree | A stable per-installation device id plus a monotonic per-row counter, alongside the token rather than replacing it — the two answer different questions. Do NOT resolve conflicts by wall-clock time; that is the silent-data-loss shape §4 rejected by name | P3 | 🔴 **Open** — registered iteration 35, §12.72 |
| TD-145 | Every delete is physical, so a deletion cannot be merged | 🟡 | Six repositories call `DbSet.Remove` and the row is gone; there is no `IsDeleted`, no `HasQueryFilter` and no deletion record anywhere in the model. `14-Synchronization` §6 says a change set MAY contain tombstones, which presumes a deletion leaves something behind to carry. It does not here: a merge between a vault where a row was deleted and one where it was not cannot tell deletion from absence, and absence loses — the row comes back. `Transaction.Void` is NOT the counter-example; it is a cancellation that deliberately keeps the row, and a voided transaction can still be physically deleted afterwards | Tombstones on the roots, with a retention policy, gated behind the same decision as sync itself. Worth noting that this is the one item of the four where the local-only product is currently BETTER off: a hard delete is a hard delete, which is what a privacy-first ledger should mean by the word | P3 | 🔴 **Open** — registered iteration 35, §12.72 |
| TD-146 | Identity is stable now, but it is surrogate — there are still no natural keys | 🟡 | Phase 3's gate says "stable natural keys" and iteration 35 delivered the first half only. Every id is a client-minted `Guid` that now survives an edit, which it did not before (TD-60). What no aggregate has is a key derived from what the thing IS: `Account.Name`, `Category.Name` and `ImportProfile.Name` carry no unique index, and the only genuine unique constraint in the entire schema is `IX_Settings_Key`. `Transaction.ImportKey` is the closest thing to a business key and is deliberately nullable and deliberately non-unique (see the reasoning on that index). The consequence is precise: two vaults can each keep their rows' identities perfectly and still have no way to recognise that they hold the same account | Decide per aggregate whether a natural key exists at all — for `Account` it plausibly does not, and a merge may have to ask the user, which `14-Synchronization` §4 layer (3) already anticipates. Do not manufacture one from a name a user can edit | P3 | 🔴 **Open** — registered iteration 35, §12.72 |

### 12.71 Next (after iteration 34) - superseded

- **TD-07 — re-arm CI.** Nineteen iterations.
- **TD-140** — the integration flake, now seen on three different tests across two iterations.
- **TD-132** — still open, and this iteration sharpened it rather than closing it: a rule's action is
  a category name, so the string is now load-bearing in a second subsystem. The row's "not both, and
  not neither" is closer to being decided by default.
- **TD-142**, **TD-143** — the two halves of the rule engine that were deliberately left.
- **TD-139** — the exception sentences below the UI layer.
- **TD-141** — the manual attachment path FR-TXN-02 asks for.
- **TD-117's remaining half**, **TD-138**, **TD-120 — the camera seam**, **TD-21**.
- **TD-121** — the shipped navigation defect that drops the shell.
- **TD-99 — make the budgets screen true or take it down.** Deferred a seventeenth time, and now the
  last surviving member of its class: the "Rules" placeholder became real this iteration, the
  language picker became real in 32, and the receipt viewer in 33.
- **TD-60** — `ReplaceSplits` mints fresh ids on every save. Its stated risk named "rules" as a
  future consumer of split ids; rules do not reference splits, so the row is unchanged.
- **TD-109's second half**, **TD-115's second half**, **TD-130**, **TD-135**.
- **TD-97**, **TD-129**, **TD-126**, **TD-127**, **TD-128**, **TD-124**, **TD-119**, **TD-113**,
  **TD-114**, **TD-108**, **TD-106**, **TD-111**, **TD-104**, **TD-107**, **TD-101**, **TD-102**,
  **TD-105**, then **TD-103**.
- **TD-94's lesson**, unchanged and still the largest.
- **TD-133**, **TD-134**, **TD-125**, **TD-122**, **TD-123**, **TD-46**, **TD-50**, **TD-118**,
  **TD-110**'s remaining half, **TD-93**, **TD-89**, **TD-86**, **TD-78**, **TD-75**'s siblings
  **TD-95**, **TD-96**, **TD-100**, **TD-91**, **TD-112**, **TD-131**, and **TD-116**.
- **TD-84** last, unchanged.

---

### 12.74 Iteration 36 — Phase 3, item 6: the plugin host is withdrawn, and this document's own prior answer was wrong

**Ticket:** Phase 3 item 6, "Plugin host, or formally retire the two plugin documents and delete the
empty projects." **This closes Phase 3.**

**One third of the instruction was already done, and this document had already recorded a wrong
answer to the rest.** The empty projects were deleted in iteration 27 — verified in git: commit
`9771a85` removed `src/Pine.Plugins.Host/Pine.Plugins.Host.csproj` and
`src/Pine.Plugins.Sdk/Pine.Plugins.Sdk.csproj`, and no plugin project is in `Pine.slnx`. That makes
this the **sixth consecutive Phase 3 row whose premise did not survive contact with the code** — and
the only one where the defective premise was written by this audit rather than inherited.

#### TD-38 claimed this row was satisfied. It was not, and the argument is worth dismantling

TD-38's Status column reads: the retirement condition was *"satisfied without inventing a new status
value, since `Aspirational` already means 'nothing in the tree does this', which an absent project
states more precisely than an empty one."* §12.56 makes the same argument at length.

The premise is true. The conclusion does not follow. `DocumentStatusTests` defines `Aspirational` as
**"nothing in the tree does this. *It may still be a good plan.*"** That is two claims: one about the
scaffolding, one left open about the plan. Deleting the empty projects sharpened the first and did
not touch the second. A reader opening 646 lines of host design marked `Aspirational` was still
entitled to conclude that somebody intended to build it — and nothing had decided otherwise.

Retirement answers the open half. No amount of precision about scaffolding can express that answer,
so the fifth status value iteration 27 declined to invent was in fact required. **The checkbox
staying unticked for nine iterations while a debt row called it done is the visible symptom of the
same gap.**

#### The decision, and why it is not about effort

**Pine will not load third-party code.** ADR-0021, Accepted.

The deciding argument is that the specification cannot be satisfied in-process, which is a stronger
claim than "this would be a lot of work":

- `12-Plugin-Architecture` §5 loads a plugin **"in an isolated load context"**. In .NET that is an
  `AssemblyLoadContext`, and an `AssemblyLoadContext` **is not a security boundary** — it isolates
  type identity and enables unloading. Code Access Security and partial-trust `AppDomain`s, the
  mechanisms that once made in-process sandboxing conceivable, were removed in .NET Core and nothing
  replaced them.
- `15-Security` §10 requires that **"a plugin has no access to the data key."** Verified in this
  codebase: `VaultSession.Key` returns the live `byte[]`, not a defensive copy — deliberately, and
  the class says why, because copies multiply the number of copies of the key in memory. Anything in
  the process that can reach the session can read it.

So §5's mechanism cannot deliver §10's requirement. Two further collisions were found and are worth
recording because both are invisible rather than merely unbuilt:

- **A plugin could break ADR-0012 while every test defending it stayed green.** `NetworkEgressTests`
  scans `src/` and `.csproj` files. A plugin is neither, so a plugin opening a socket would violate
  "no network I/O anywhere in the application" without tripping the guard that exists precisely to
  make that impossible.
- **Nothing in this repository can verify a signature.** No certificate handling, no trust store, no
  signature verification of any kind. `12-Plugin-Architecture` §6's "a manifest without a valid
  signature MUST be rejected by the loader" described a check no code could perform.

The design that would work is out-of-process isolation, and the ADR says so rather than pretending
the idea is bad: one process per plugin, an IPC surface carrying only granted projections, the key
never crossing it. It is a subsystem larger than this application currently is, and it sits beneath
a marketplace and a signing infrastructure that do not exist. That is a proportion argument, and it
is made in the open.

#### `Withdrawn` — a fifth status value, with a price attached

`Aspirational` costs nothing to write and commits nobody, which is right for a plan not yet built.
`Withdrawn` makes the opposite claim — that a decision was taken — and `docs/adr/INDEX.md` already
documents what that claim is worth without a record: *"An 'Accepted' decision with no record is not a
decision that was made and written down badly — it is a claim about the project's history that
nothing supports."*

So the label has a price: `EveryWithdrawnDocumentCitesAnAdrThatExists` fails the build unless the
document's **Status row** names an ADR that resolves to a file. The fifth value is not a new place to
evade, because it is the only one of the five that cannot be written without also writing something
else.

The ADR is numbered **0021**, not 0014. `25-ADR` §11 seeds `0001`–`0020` and assigns 0014 to "Plugin
manifests MUST be signed" and 0005 to "Adopt in-process plugin host"; taking either number would
have produced a file whose number asserts the opposite of its content.

#### The blast radius, handled where the promises were made

Twenty-four documents mention plugins. The ones rewritten are the ones that make a **normative**
promise:

| Document | What changed |
|---|---|
| `12-Plugin-Architecture`, `33-Plugin-SDK` | `Withdrawn — ADR-0021`. Bodies kept unedited: a design specified and then declined is worth more as a record than as a deletion |
| `07-Functional-Requirements` | `FR-PLUGIN-01`…`06` struck through in place, `FR-SYNC-01` superseded. Struck rather than deleted because `19-Traceability-Matrix` counts them, and a requirement that vanishes is harder to audit than one visibly retracted |
| `02-Product-Scope` | §7.3 withdrawn; §7.2's "via plugins" struck. SCOPE-22 will not be built |
| `15-Security` | §10 withdrawn — **and annotated as the section that decided the question**, because it was right and the specification it constrained could not satisfy it |
| `14-Synchronization` | `ISyncProvider` re-homed from `33-Plugin-SDK` to this document; providers are compiled in |

**Sync was the collateral damage and it is repaired rather than mourned.** §12.72 had recorded that
`02-Product-Scope` §7.2 scoped sync to plugins only, which made the plugin host gate sync outright.
Withdrawing plugins without re-homing sync would have retired sync by accident — one iteration after
its persistence preconditions were deliberately built. `FR-SYNC-01`'s "no sync provider may be
hard-coded" is superseded on its own terms: the clause existed to stop one vendor being welded into
the product, and an interface with several compiled implementations serves that purpose without
executing anyone else's code.

Non-normative documents — `00-Vision`, `23-Roadmap`, `55-Future-Vision`, `05-Personas` and the
`SH-PLUGIN-AUTHOR` stakeholder — still describe a plugin ecosystem. They are **not** edited here:
they are not specifications, and revising a product's stated direction is a decision to take
deliberately rather than as the side effect of a technical one. **TD-147.**

#### Falsification, and the mutation that found a real hole in the new test

**Three mutations, three killed — but the first one survived its first run and exposed a genuine
weakness.**

| # | Mutation | Result |
|---|---|---|
| M1 | A `Withdrawn` document whose Status names no ADR | **SURVIVED, then killed.** See below |
| M2 | A `Withdrawn` document citing `ADR-0099`, which has no file | **Killed** |
| M3 | `Withdrawn` removed from the closed vocabulary | **Killed** — both documents rejected |

M1's first version of the rule read the Status **and** the Reconciled line, reasoning that the
evidence could sit in either row. It could — but so could an unrelated citation. Removing the
ADR-0021 reference left the test green, because the same paragraph mentions **ADR-0012** in passing
while explaining the egress collision. "Cites some ADR somewhere" is not the property worth having.
The rule now reads the Status row only: the status must name the decision that withdrew the
document. **The mutation is the only reason that distinction was noticed** — the test passed in
exactly the way a vacuous test passes.

#### Verified state

- Unit **1003 → 1004**, integration **234**, both green. `Pine.Ui.Windows`, `Pine.Ui.Macos`,
  `Pine.Cli` and both test projects: 0 warnings, 0 errors.
- **No source file changed in this iteration**, and that is the point: there was nothing to remove.
  The empty projects went in iteration 27; what was left was a decision nobody had taken.
- `EveryAdrQuotationStillAppearsInTheDocumentItCites` passes with the new ADR, so ADR-0021's
  quotations of `15-Security` §10 are verbatim rather than paraphrased.

#### New debt

- **TD-147** — the non-normative documents still promise a plugin ecosystem.

| ID | Row | Sev | Impact | Fix | Pri | Status |
|---|---|---|---|---|---|---|
| TD-147 | The non-normative documents still describe a plugin ecosystem that will not exist | 🔵 | ADR-0021 withdrew the plugin surface and every NORMATIVE document was amended with it. Six non-normative ones were deliberately not touched: `00-Vision` Pillar F promises "a community-driven ecosystem of plugins, themes, importers, exporters, financial providers and languages"; `01-Business-Requirements` carries three key results that count third-party plugins (`KR-01-2`, `KR-01-3`, `KR-05-1`); `23-Roadmap` schedules a marketplace at v0.5; `17-Risk-Analysis` R-10 mitigates "a plugin ecosystem never forms" with "stable Plugin SDK; public SDK roadmap; sample plugins"; `55-Future-Vision` calls plugins "a first-class extension surface"; and `05-Personas` PERS-07 is a plugin author, with `04-Stakeholders` `SH-PLUGIN-AUTHOR` beside her. None of these specifies software behaviour, so none of them can mislead a reader about what the code does — which is why they were left rather than swept along | Revise the product's stated direction deliberately, as a decision about what Pine is for, rather than as the side effect of a technical ADR. R-10 in particular is now answered rather than mitigated: the ecosystem will not form because there will not be one. A persona whose goal the product has declined to serve should either be retired or re-aimed at the compiled-in extension seams | P3 | 🔴 **Open** — registered iteration 36, §12.74 |

#### Phase 3 is closed

Six rows. **Five of the six had a false or stale premise**, and the sixth — this one — had two: the
projects it asked to delete were already gone, and the retirement it asked for had been recorded as
done on an argument that did not hold. The phase's most useful output was not any single feature; it
was that every row was checked before it was obeyed.

### 12.73 Next (after iteration 35) - superseded

- **TD-07 — re-arm CI.** Twenty iterations.
- **TD-140** — the integration flake, now seen on four different tests across three iterations.
- **Phase 3 item 6 — the plugin host, or the formal retirement of the two plugin documents.** It is
  the last unchecked row in the phase, and iteration 35 made it load-bearing rather than optional:
  sync is scoped to plugins, so `12-Plugin-Architecture` now gates `14-Synchronization` outright.
- **TD-144**, **TD-145**, **TD-146** — the three things still between this schema and a merge.
- **TD-132** — still open; the string is now load-bearing in two subsystems.
- **TD-142**, **TD-143** — the two halves of the rule engine deliberately left.
- **TD-139** — the exception sentences below the UI layer.
- **TD-141** — the manual attachment path FR-TXN-02 asks for.
- **TD-117's remaining half**, **TD-138**, **TD-120 — the camera seam**, **TD-21**.
- **TD-121** — the shipped navigation defect that drops the shell.
- **TD-99 — make the budgets screen true or take it down.** Deferred an eighteenth time, and still
  the last surviving member of its class.
- **TD-109's second half**, **TD-115's second half**, **TD-130**, **TD-135**.
- **TD-97**, **TD-129**, **TD-126**, **TD-127**, **TD-128**, **TD-124**, **TD-119**, **TD-113**,
  **TD-114**, **TD-108**, **TD-106**, **TD-111**, **TD-104**, **TD-107**, **TD-101**, **TD-102**,
  **TD-105**, then **TD-103**.
- **TD-94's lesson**, unchanged and still the largest.
- **TD-133**, **TD-134**, **TD-125**, **TD-122**, **TD-123**, **TD-46**, **TD-50**, **TD-118**,
  **TD-110**'s remaining half, **TD-93**, **TD-89**, **TD-86**, **TD-78**, **TD-75**'s siblings
  **TD-95**, **TD-96**, **TD-100**, **TD-91**, **TD-112**, **TD-131**, and **TD-116**.
- **TD-84** last, unchanged.

---

### 12.76 Iteration 37 — the three findings of the cross-phase audit, closed

**Ticket:** the closing audit of all four phases asked one question — can this be called closed? —
and returned three things that had to be fixed before the answer could be yes. This iteration is
those three. Two of them were defects nobody had noticed; the third was this document's own
conclusion.

#### 1. Excel and PDF import: the worst defect still shipping, and a severity that had gone stale

`ExcelImporter` and `PdfImporter` each opened the file, walked the rows or pages, and returned an
empty list **reporting success**. The import wizard's file picker offered "Spreadsheets
(*.xlsx, *.xls)" and "PDF Statements (*.pdf)" as named categories. So a user could select their
bank's statement, be told the import had succeeded, and receive nothing — indistinguishable from a
statement that genuinely held no rows.

**The register had this at 🟡 P2, and the reason it was low is instructive.** TD-67's Impact column
reads: "neither is reachable from the wizard, which hardcodes `CsvImporter`". That was true when it
was written. **TD-28, five iterations later, made it false** — it replaced the hard-coded importer
with `ExtensionImporterFactory.ForFile`, precisely to stop a `.xlsx` being read as CSV, and in doing
so wired both stubs to the picker for the first time. Fixing one row promoted another, and the
severity was never re-read.

TD-67 offered "implement or delete" and **both branches were taken, one each**:

- **Excel is implemented**, against the same `ImportProfile` column mapping, date format, amount
  inversion and `ImportKeyFactory` derivation that CSV uses, so a statement exported as `.csv` and
  as `.xlsx` derives identical import keys and de-duplicates against itself. Typed cells are handled
  — an Excel date arrives as a `DateTime` and an amount as a `double`, neither of which is a string
  — with the string forms as fallback. One deliberate divergence from CSV is documented in the code:
  a blank spreadsheet cell is `DBNull` where CsvHelper yields `""`, so without an explicit
  row-has-any-value guard every trailing blank row in a bank export would have imported as an
  "Unknown" transaction of zero.
- **PDF is deleted** — the file, its tests, and the `PdfPig` package reference with it. A PDF
  statement has no columns, no delimiter, and no two banks that agree on layout; parsing one is a
  project, not a missing method body. A `.pdf` now reaches `UnsupportedImportFormatException`, which
  says in a sentence that this screen cannot read it.

The picker now offers `.csv`, `.xlsx` and `.xls` and nothing else. The image patterns are gone too
(TD-122): they belong to receipt scanning, a different screen with a different pipeline.
`UnsupportedImportFormatException`'s own remarks had predicted this — *"the picker's filter list
should be narrowed; until it is, the failure is at least stated rather than silent"* — and that
paragraph is kept, because it called it correctly.

#### 2. TD-99: the budgets screen stops fabricating numbers

`BudgetsViewModel` set `SpentAmount = 0m` with the comment "Will be calculated from transactions",
so every progress bar read 0% and `IsExceeded` was never true, and rendered the category as
`"Category " + CategoryId.ToString()[..4]` — a truncated GUID where a name belongs. Deferred
nineteen times, and the last screen in the application showing a number that was not measured.

Spend is now computed in the application layer by `GetBudgetProgressQuery`: voided transactions
excluded on the same rule the ledger uses, expenses only, inside a half-open period window
`Budget.ResolveWindow` derives from the stored dates or the calendar period.

**The honest part is what it does when it cannot answer.** `Budget.CategoryId` is a `Guid`;
`TransactionSplit.Category` is a string (TD-32, and TD-132 for why nothing writes a `Categories`
row). The only available join is `CategoryId → Category.Name → split.Category`, which is exact where
a category row exists and impossible where one does not — which, given TD-132, is what a
user-created budget looks like today. **Where the join or the window cannot be resolved, the answer
is "not known", not zero**, and the screen says so. A `BiWeekly` budget with no stored dates reports
unknown for the same reason: a fortnight has no calendar anchor and the aggregate stores no origin,
so inventing one would put a fabricated number back on the screen this row exists to clean up.
Cross-currency splits are excluded rather than converted — there is no exchange-rate source — and
counted, so the exclusion is visible.

#### 3. TD-56's shipped half: the export button, and a security sentence that was false

`SettingsViewModel.ExportVault()` was an empty method bound to a visible button, while
`ExportVaultBackupCommand` and `VaultBackupService` sat complete and unused. It now runs the backup
and tells the user the path it wrote to. `LockVault()` was the same shape — an empty body — and
**it was bound to a visible "Lock Vault" button too**, so it was wired to the session rather than
deleted.

**And the panel was telling users something untrue about their own encryption.** The description
string read *"Save a **decrypted** backup to disk."* The backup is produced by `VACUUM INTO` on the
already-keyed SQLCipher connection: it is encrypted with the very same key, and an existing test —
`CreateBackupAsync_ShouldProduceBackupEncryptedWithSameKey` — proves it by opening the result with
the right key and then the wrong one. The string is corrected. It is a one-line fix and it was the
only place in the product where a shipped sentence misdescribed the security posture, in the
direction that would make a careful user avoid a feature that was safe.

The restore half of TD-56 is NOT done: recovery still means copying a file by hand. The row stays
open, narrowed.

#### 4. §11, the audit's own conclusion, which was thirty-six iterations stale

The Final verdict still read **2.3 / 5**, described "11.7k lines of source, 28 tests" against today's
~39.9k and 1,238, asserted "no unit of work, no validation stage, no concurrency control, no
correction path" — all four built in Phases 1 and 2 — and named as the **most important next
action** exactly the work TD-01 completed. Anyone reading the document's conclusion got a picture of
a different codebase.

It is rewritten as §11.1, and **the original is preserved verbatim as §11.2 rather than replaced**,
because an audit that silently rewrites its own conclusion is worth less than one that shows what it
outgrew. The new verdict is **3.4 / 5, still not production ready** — the difference is that the
blockers are now a bounded punch list, which is precisely what the original said they were not.

#### Falsification

**Three mutations, three killed — and the first attempt at M1 was invalid, which is worth recording.**

| # | Mutation | Result |
|---|---|---|
| M1 | `ExcelImporter` discards the rows it parsed and returns an empty list | **Killed** — 11 of 12 |
| M2 | `GetBudgetProgressQuery` reports `Money.Zero` again — the exact TD-99 defect | **Killed** — 10 |
| M3 | The export path is never entered — the button does nothing again | **Killed** — 5 of 6 |

M1's first attempt patched the wrong `return`: a regex took the first match, which is the
invalid-file guard, not the accumulator at the end of the method. Twelve tests passed and the
mutation appeared to survive. **The conclusion "the tests are vacuous" was available and would have
been wrong** — re-reading the patched line showed it had landed on line 75 rather than 233. Applied
to the real return, eleven tests died. A mutation that does not compile or does not apply is not a
survivor; it is a mutation that was not run.

#### Verified state

- Unit **1004 → 1049**, integration **234**, both green. `Pine.Ui.Windows`, `Pine.Ui.Macos`,
  `Pine.Cli` and both test projects: 0 warnings, 0 errors.
- **`BuildHygieneTests.NoCentralVersionIsUnreferenced` caught this iteration's own mistake**:
  removing `PdfPig` from `Pine.Infrastructure.csproj` left its `PackageVersion` orphaned in
  `Directory.Packages.props`. The tripwire TD-41 installed did exactly what it was built for.
- Coverage rose rather than fell: overall 67 → **68**, `Pine.Application` 57 → **59**,
  `Pine.Ui.Avalonia` 37 → **38**. Floors raised to match.
- Three mechanical fixes on agent-written code, all of the recurring kind: `--` inside an XAML
  comment (which breaks the parser outright), and CA1848/CA1873/CA1859, resolved with the same
  scoped `#pragma` `CategorizationTrainingBackgroundService` already uses.

#### What this does NOT close

- **TD-07.** CI is still `workflow_dispatch:` only. It is now the largest single gap in the project.
- **TD-56's restore half**, **TD-121**, **TD-108**, **TD-54**, **TD-140** — the remaining 🟠 P1s.
- `GetBudgetsQuery` now has no callers. Left in place rather than deleted unilaterally. **TD-148.**

| ID | Row | Sev | Impact | Fix | Pri | Status |
|---|---|---|---|---|---|---|
| TD-148 | `GetBudgetsQuery` is unreferenced now that the budgets screen reads progress instead | 🟢 | The budgets page was its only caller and now sends `GetBudgetProgressQuery`, which returns the budget's limit, period and category alongside the measured spend. Nothing else in `src/` or `tests/` sends the old query. It is dead code of the mildest kind — a query handler that compiles, is registered by the MediatR assembly scan, and answers nobody | Delete it, or keep it deliberately if a caller that wants budgets WITHOUT the ledger read is coming. Not deleted in the iteration that orphaned it, because deciding the fate of a public request type is a separate judgement from fixing a screen, and TD-37's lesson is that deleting a type is cheap only when nothing was going to use it | P3 | 🔴 **Open** — registered iteration 37, §12.76 |
| TD-149 | A monthly, weekly or yearly budget measures the UTC calendar period, not the user's | 🟠 | `Budget.ResolveWindow` builds its window at `TimeSpan.Zero`. Every path into the ledger stores a date-only entry as LOCAL midnight (`CsvImporter` and `ExcelImporter` parse with no offset, which .NET reads as local; `ReceiptDateParser` uses `AssumeLocal`; manual entry starts from `DateTimeOffset.Now`), so near a month boundary the budget and the transaction disagree about which month it is. East of Greenwich, a purchase on 1 October counts against September's budget; west of it, an evening purchase on 30 September counts against October's. Found while building the dashboard's "this month" tile, which had copied this window "for consistency" and was showing an Ecuadorian user October at 21:00 on 30 September | Resolve the window in the user's zone, each edge at the offset in force at that edge -- the same fix `GetMonthlyCashFlowQueryHandler.CurrentLocalMonth` already applies -- and move `Budget.ResolveWindow`'s tests off an all-UTC clock, which is why none of them could see it | P2 | 🔴 **Open** -- registered 2026-09-11, post-audit plan item 1.3 (`62-Post-Audit-Plan` §4.3) |

### 12.75 Next (after iteration 36) - superseded

**Phase 3 is closed. There is no Phase 4 in §10, so the next iteration begins with the register
rather than the roadmap.**

- **TD-07 — re-arm CI.** Twenty-one iterations. It is now the single largest gap: every regression
  net built across three phases is inert until something triggers the workflow.
- **TD-140** — the integration flake, seen on four different tests across three iterations.
- **TD-147** — the non-normative documents still describe a plugin ecosystem that will not exist.
- **TD-144**, **TD-145**, **TD-146** — the three things still between this schema and a merge. Sync
  is now reachable: its preconditions exist and its delivery mechanism is no longer a plugin host
  that will never be built.
- **TD-132** — still open; the category string is load-bearing in two subsystems.
- **TD-142**, **TD-143** — the two halves of the rule engine deliberately left.
- **TD-139** — the exception sentences below the UI layer.
- **TD-141** — the manual attachment path FR-TXN-02 asks for.
- **TD-117's remaining half**, **TD-138**, **TD-120 — the camera seam**, **TD-21**.
- **TD-121** — the shipped navigation defect that drops the shell.
- **TD-99 — make the budgets screen true or take it down.** Deferred a nineteenth time, and now the
  last placeholder of its class still standing.
- **TD-109's second half**, **TD-115's second half**, **TD-130**, **TD-135**.
- **TD-84** — the Diagnostic Information Bundle. Its Fix column says "build the DIB once metrics,
  SLOs and a plugin host exist"; one third of that condition can now never be met, so the row needs
  re-scoping rather than waiting.
- **TD-97**, **TD-129**, **TD-126**, **TD-127**, **TD-128**, **TD-124**, **TD-119**, **TD-113**,
  **TD-114**, **TD-108**, **TD-106**, **TD-111**, **TD-104**, **TD-107**, **TD-101**, **TD-102**,
  **TD-105**, then **TD-103**.
- **TD-94's lesson**, unchanged and still the largest.
- **TD-133**, **TD-134**, **TD-125**, **TD-122**, **TD-123**, **TD-46**, **TD-50**, **TD-118**,
  **TD-110**'s remaining half, **TD-93**, **TD-89**, **TD-86**, **TD-78**, **TD-75**'s siblings
  **TD-95**, **TD-96**, **TD-100**, **TD-91**, **TD-112**, **TD-131**, and **TD-116**.

---

### 12.77 Next

**The ordered plan lives in `62-Post-Audit-Plan`.** §10's four phases are closed and it has no Phase
4, so without a successor the next iteration would begin by picking a row out of a register of
sixty-eight. That document orders what remains on three rules — a guard that does not run is not a
guard; anything a user can see that is not true comes next; then correctness they cannot see, then
breadth — and its Stage 0 is a single row.


- **TD-07 — re-arm CI.** Twenty-two iterations, and now unambiguously first: every guard this audit
  built across four phases runs only when a human asks it to.
- **TD-56's restore half** — backups can be made from the UI as of this iteration and still cannot
  be restored from it.
- **TD-121** — the navigation defect that replaces the whole shell.
- **TD-108** — the unsigned release artefact. **TD-54** — CI enumerates projects, not the solution.
- **TD-140** — the integration flake, four tests across three iterations.
- **TD-148** — `GetBudgetsQuery` is now unreferenced.
- **TD-147** — the non-normative documents still promise a plugin ecosystem.
- **TD-144**, **TD-145**, **TD-146** — the three things between this schema and a merge.
- **TD-132** — the category string is load-bearing in three subsystems now, budgets included: this
  iteration had to route budget spend through a name because the id relationship does not exist.
- **TD-142**, **TD-143**, **TD-139**, **TD-141**, **TD-138**, **TD-120**, **TD-21**,
  **TD-117's remaining half**.
- **TD-84** — the DIB, whose Fix column waits on a plugin host that will now never exist.
- **TD-97**, **TD-129**, **TD-126**, **TD-127**, **TD-128**, **TD-124**, **TD-119**, **TD-113**,
  **TD-114**, **TD-108**, **TD-106**, **TD-111**, **TD-104**, **TD-107**, **TD-101**, **TD-102**,
  **TD-105**, then **TD-103**.
- **TD-94's lesson**, unchanged and still the largest.
- **TD-133**, **TD-134**, **TD-125**, **TD-122**, **TD-123**, **TD-46**, **TD-50**, **TD-118**,
  **TD-110**'s remaining half, **TD-93**, **TD-89**, **TD-86**, **TD-78**, **TD-75**'s siblings
  **TD-95**, **TD-96**, **TD-100**, **TD-91**, **TD-112**, **TD-131**, and **TD-116**.
