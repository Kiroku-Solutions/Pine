# 0021 — Pine will not load third-party code; the plugin host and SDK are withdrawn

| Field          | Value                                                       |
|----------------|-------------------------------------------------------------|
| ADR            | `0021`                                                      |
| Title          | No plugin host                                               |
| Status         | Accepted                                                     |
| Date           | 2026-09-09                                                   |
| Authors        | Written as the decision was taken (audit §10 Phase 3, item 6) |
| Reviewers      | n/a — this repository has one contributor                    |
| Supersedes     | The seeded rows `0005` "Adopt in-process plugin host" and `0014` "Plugin manifests MUST be signed" in `25-ADR` §11 — neither of which has a file, so this supersedes a claim rather than a record |
| Superseded by  | —                                                            |
| Closes OD      | `OD-01` (plugin trust model)                                 |
| Deciders       | The security analysis below, and the absence of any way around it |

> **Why this number.** `25-ADR` §11 seeds `0001`–`0020` and marks all twenty Accepted; seventeen
> have no file, and `docs/adr/INDEX.md` instructs the reader to treat that table as a backlog rather
> than a register. Taking `0014` — the number the backlog assigns to "Plugin manifests MUST be
> signed" — would have produced a file whose number asserts the opposite of its content. `0021` is
> the first serial past the seeded range and collides with nothing.

## Context

`12-Plugin-Architecture` and `33-Plugin-SDK` specify, across 646 lines, an in-process plugin host: a
signed manifest, a default-deny permission model with typed capability tokens, a marketplace, a risk
score, a submission pipeline, and fifteen plugin categories. `02-Product-Scope` §7.3 calls the plugin
surface "one of Pine's strategic moats". Neither document has ever had a line of code behind it. The
two scaffold projects, `Pine.Plugins.Host` and `Pine.Plugins.Sdk`, contained zero `.cs` files from
the day they were created and were deleted in iteration 27 (TD-38).

The audit's §10 Phase 3 offered exactly two outcomes: build the host, or formally retire the two
documents and delete the empty projects. This ADR records the second.

**The deciding argument is not cost. It is that the specification cannot be satisfied in-process.**

`15-Security` §10 states four requirements a plugin host must meet. The first two are:

> - A plugin runs in an isolated load context.
> - A plugin has no access to the data key.

In .NET, the mechanism named by the first is `AssemblyLoadContext`, and **an `AssemblyLoadContext`
is not a security boundary.** It isolates type identity and enables unloading; it does not restrict
what the loaded code may touch. Code Access Security and partial-trust `AppDomain`s — the mechanisms
that once made in-process sandboxing conceivable — do not exist in modern .NET, and Microsoft's
guidance is explicit that the framework offers no in-process sandbox to replace them.

The second requirement is therefore unsatisfiable by the first. Concretely, in this codebase:
`VaultSession.Key` returns the live `byte[]` holding the vault's data key — not a defensive copy, by
a documented and deliberate choice, because handing out copies multiplies the number of copies of
the key in memory. Any code in the process that can reach the session, by dependency injection or by
reflection over the object graph, can read that array. That is not a defect to be fixed before
plugins ship; it is a property of running third-party code in the same process as a decryption key,
and the alternative — a defensive copy — makes it worse rather than better.

There is a real solution: **out-of-process isolation.** One process per plugin, an IPC surface that
exposes only the granted projections, and the key never crossing it. That is the design the
specification would need. It is also an entire subsystem — a supervisor, a protocol, a versioned
wire contract, lifecycle and crash handling — sitting beneath a marketplace, a signing
infrastructure that does not exist anywhere in this repository, and a review pipeline. For an
application with one contributor and no released version, choosing that is choosing to build it
instead of the product.

Two further collisions are worth recording:

- **ADR-0012** establishes that Pine performs no network I/O at all, and `NetworkEgressTests` enforces
  it by scanning `src/`. A plugin is not under `src/` and is not a `PackageReference`, so a plugin
  that opened a socket would violate the ADR while passing every test that defends it. The
  enforcement mechanism and the plugin model are incompatible in a way that is invisible.
- **Nothing in the repository can verify a signature.** There is no certificate handling, no
  public-key trust store and no signature verification of any kind. "A manifest without a valid
  signature MUST be rejected by the loader" describes a check nothing could perform.

## Decision

**Pine does not load third-party code. There is no plugin host, no plugin SDK, no marketplace, and
no extension mechanism that executes code Pine did not ship.**

`12-Plugin-Architecture` and `33-Plugin-SDK` are marked `Withdrawn` and kept in place, unedited below
their header, as the record of a design that was specified and then declined.

Extensibility remains a goal and is re-homed rather than abandoned: it is delivered by compiled-in
implementations behind the ports that already exist — `IImporter` and its factory, `IAIProvider`,
`IDashboardWidgetViewModel`, the theme resource dictionaries. Adding a capability means adding code
to Pine and shipping it, where it is reviewable, auditable and covered by the same tests as
everything else.

**Synchronisation is re-homed with it.** `02-Product-Scope` §7.2 scoped sync to plugins only and
`14-Synchronization` §8 handed the provider contract to `33-Plugin-SDK`; with this decision those
clauses would leave sync with no delivery mechanism at all. Sync becomes a first-class subsystem
with compiled-in providers. `FR-SYNC-01`'s "no sync provider may be hard-coded" is superseded by
this ADR: its purpose was to prevent a single vendor being welded into the product, and a set of
compiled providers behind one interface serves that purpose without executing anyone else's code.

## Consequences

- The five plugin-dependent Functional Requirements (`FR-PLUGIN-01`…`FR-PLUGIN-06`), `SCOPE-22`
  (Plugin Marketplace), `FR-SYNC-01` and `FR-DASH-04` are superseded. They are struck through in
  place rather than deleted, so the traceability matrix keeps its referents.
- The `PluginInstallation` aggregate (`10-Domain-Model` §6.9) and the `plugin_installations` table
  (`11-Data-Model`) will never be built. Neither exists in code today.
- `15-Security` §10, `38-Threat-Model` §6.4 (TM-30…TM-34), `40-Accessibility` §13, `41-Localization`
  §12, `42-UX-Guidelines` §11 and `29-Deployment` §10.4 all constrain a subsystem that will not
  exist. They are annotated, not deleted: a constraint on nothing is harmless, and deleting a threat
  model entry is how a threat model rots.
- The business key results that assume a third-party ecosystem (`KR-01-2`, `KR-01-3`, `KR-05-1`) and
  risk `R-10` ("a plugin ecosystem never forms") are answered rather than mitigated: it will not
  form, because there will not be one.
- Non-normative documents — `00-Vision`, `23-Roadmap`, `55-Future-Vision`, `05-Personas` and the
  `SH-PLUGIN-AUTHOR` stakeholder — still describe an ecosystem. They are not specifications and are
  left for a deliberate revision of the product's direction rather than edited as a side effect of a
  technical decision. Registered as debt.
- **What is gained:** every line of code that touches a vault is code this project shipped. For an
  application whose central claim is that your financial data never leaves your device, that is not
  a consolation prize for failing to build a plugin host — it is a stronger guarantee than the
  plugin host was ever going to provide.

## Alternatives Considered

**Build the in-process host as specified.** Rejected: it would ship `15-Security` §10's promise
without the ability to keep it. A permission model enforced only against code that chooses to respect
it is a permission model against accidents, not against adversaries, and the document does not
present it that way.

**Build out-of-process isolation.** Rejected on proportion, not on merit — this is the design that
would actually work, and if plugins are ever revisited this ADR should be superseded by one that
adopts it. It is a subsystem larger than the application currently is.

**Leave both documents `Aspirational`.** This is what iteration 27 chose, arguing that deleting the
empty projects made the classification "more precisely `Aspirational`". Rejected, and the reasoning
is worth stating because it was written down and believed: `Aspirational` is defined as "nothing in
the tree does this. *It may still be a good plan*." That is a statement about the scaffolding plus an
open question about the plan. Deletion sharpened the first half and left the second untouched — so a
reader still had every reason to believe someone intended to build it. Retirement answers the
question, and no precision about scaffolding can express an answer.

**Keep the documents but delete the plugin sections from every dependent.** Rejected: the dependents
are where the promises were made to a reader, and a promise is retracted where it was made.

## Risks

| Risk | Treatment |
|---|---|
| A future contributor rediscovers the plugin documents and starts building | The `Withdrawn` status and this ADR are the answer, and `EveryWithdrawnDocumentCitesAnAdrThatExists` keeps the two attached to each other |
| Extensibility stagnates because "no plugins" is heard as "no extension points" | The decision names the ports that carry it instead. The importer factory is a closed `switch` and the dashboard widget list is twelve hard-coded lines; both are extension points in shape only, and that is now ordinary technical debt rather than a gap waiting for a plugin host |
| Sync is quietly dropped along with its delivery mechanism | Explicitly re-homed above, and its persistence preconditions were built in the iteration immediately before this one |
| This ADR is cited as authority for refusing all future extensibility | It refuses one thing: executing code Pine did not ship |

## Compliance

- `DocumentStatusTests.EveryWithdrawnDocumentCitesAnAdrThatExists` fails the build if a document
  claims `Withdrawn` without pointing at an ADR file that exists.
- `NetworkEgressTests` continues to enforce ADR-0012. This decision removes the one mechanism that
  could have bypassed it unseen.
- No code change accompanies this ADR, and that is the point: there was nothing to remove.

## Citations

`12-Plugin-Architecture`, `33-Plugin-SDK`, `15-Security` §10, `02-Product-Scope` §7.2/§7.3,
`07-Functional-Requirements` FR-PLUGIN-*, FR-SYNC-01, FR-DASH-04, `14-Synchronization` §8,
`60-Production-Readiness-Audit` §10 Phase 3 item 6 and §12.74.

## Traceability

Closes `OD-01`. Supersedes the seeded `25-ADR` §11 rows `0005` and `0014`, neither of which has a
file. Related: ADR-0012 (no network I/O), ADR-0013 (encryption baseline, whose key-in-managed-memory
risk this decision removes an entire class of caller from).
