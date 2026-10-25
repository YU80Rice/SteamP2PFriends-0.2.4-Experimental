# Implementation audit — SPF 0.2.4.10 R02

- **Ticket:** R02, Guest region entry/return automatically delivers current Item baseline; real failures only notify.
- **Baseline:** branch `codex/structure-baseline-0.2.4`, HEAD `3fb2eb5e87ab3eddbc6782fcc708418a7d11d52a`; R01 is a prior frozen delivery, not R02 review evidence.
- **R02 commit:** `0979b62` (`Fix R02 observer failure identity cleanup`); scoped source, test, ticket, delivery and audit commit on the same branch.
- **Status:** R02 static implementation and review complete (`implemented-pending-runtime` / `ready-for-human`); cumulative round 4 Standards and Spec CLEAN; Guest Runtime pending.
- **Issue:** `.scratch/item-runtime-repair-20261014/issues/02-guest-item-delivery.md` retains all 10 original acceptance criteria; label `ready-for-human`; status `implemented-pending-runtime`.

## Finding and implementation

Current production `DefaultNativeItemWarehouseBridge` treated an allocated spawn list with zero entries as missing natural inputs. Return settlement therefore could not finish a legitimate zero-point region. The production classifier now accepts that present-empty state while rejecting absent/out-of-range spawn input and missing `ItemRegion.items`; `CreateOneNatural` returns `NoChange` when there is no candidate. This establishes a current-code defect. The historic 2026-10-13 packages lack the map's spawn-list state and full send/notice chain, so this audit does not claim that defect as proven root cause of the old town symptom.

Failure handling keeps first exception and bounded region/job/observer/connection/attempt/budget/notice details, quarantines uncertain/half-packet sends, deduplicates repeat callbacks, and scopes unconfirmed-result notices to observer/connection/region. After world settlement is committed and its job dropped, a confirmed-not-sent baseline now creates an observer/token/region delivery-only record. `Consume` processes it before the no-settlement and native-cycle gates, reuses the same `FailedRetryController`, and invokes only the current binder; it never repeats clearing/restock or caches ItemData. Successful peers remain delivered. Unknown results revoke automatic retry and retain half-packet quarantine; exhausted confirmed-not-sent delivery gets one safe recovery notice and can reopen only through existing current-identity/coverage/cooldown authorization. True projection exit, disconnect, session or region-generation invalidation revoke the exact record and retry key. Normal engine compensation exit does not reopen the trace. No new ACK, reset, protocol, writer, warehouse, or retry policy was introduced.

## Acceptance evidence map

| AC | Static evidence and boundary |
|---|---|
| 1 | IT134 follows `ItemProductionControlSeam` acquisition/return-settlement gate to Guest baseline callback; IT137 follows uncertain failure, exit, return, settlement wait and new baseline callback. No player movement/Host visit is needed after the gate completes. |
| 2 | IT133 is a Red→Green call through the shared production natural-input classifier; IT134 exercises settlement behavior. An earlier fake-only IT87 Red is retained but explicitly disqualified as production evidence. |
| 3 | IT133 treats present-empty as ready and missing spawn/region/items as unavailable; IT134 preserves expired-natural removal and unrelated dropped item. |
| 4 | Existing IT41/46/56/59/60 and full whitelist cover long settlement wait/shared result/missing input and asset behavior. |
| 5 | IT138 drives production `DispatchCandidateGuestAskItems` through settlement `Finish` after world commit, proves confirmed-not-sent is not half-packet, waits 0.5 seconds, then retries only that Guest; the co-demand Guest succeeds once and is not resent. IT139 checks the 0.5/1/2 second intervals, initial + 3 cap and manual recovery; IT140 holds normal pre-settlement/pending waits for 40 seconds without starting the failure clock, then enforces the 10-second deadline. |
| 6 | IT135 and IT138–IT141 validate first failure/stack, bounded retry budget, target-specific safe-vs-unknown notices and no repeated unknown retry; production diagnostics stay capped. |
| 7 | IT136 retains notice dedupe. IT142 proves repeated chain events and engine compensation do not reopen the trace, a real projection exit/reentry does, and successful delivery closes that chain; IT143 checks exact pending delivery revocation; IT144 reproduces and fixes connection cleanup identity collision while retaining target-region/failure-kind reopen and the colliding observer dedupe. |
| 8 | IT137 proves possible-half-packet quarantine, true region exit, return settlement wait, new baseline delivery and final unblock. IT141 proves a later unknown result during safe retry quarantines once and stops blind replay. Actual native `ITEM_REGIONS` bytes require Runtime evidence. |
| 9 | No edits to writer, Core identity, native protocol, pickup, or 20m eligibility. IT138/139/143 isolate one observer's delivery retry; R01 evidence remains prior-ticket context only. |
| 10 | Current candidate Release build and full 614/614 Whitelist pass recorded below. The cumulative R02 round-3 Standards P2 is repaired in the separately authorized AC7-Collision slice; one fresh dual-axis round 4 remains mandatory. Source Green is not Guest Runtime PASS. |

## Verification

- Current candidate: production Release build exit 0; Whitelist Release build exit 0; runner exit 0, `614/614 PASS (Failed: 0)`; output says Runtime evidence PENDING: SP, listen-host, U3DS, P2P.
- `dotnet msbuild WhitelistTests/SteamP2PFriends.WhitelistTests.csproj /t:Build /p:Configuration=Release /v:minimal` — exit 0, four existing test-only warnings, no errors.
- `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` — exit 0, `613/613 PASS (Failed: 0)`; output says Runtime evidence PENDING: SP, listen-host, U3DS, P2P.
- G1 history remains under `.scratch/matt-orchestrate/item-runtime-repair-20261025-vela/messages/`; collision Red/Green and current Release logs are `R02-AC7-Collision-red3-run.log`, `R02-AC7-Collision-production-release.log`, `R02-AC7-Collision-whitelist-build.log`, and `R02-AC7-Collision-whitelist-run.log`.
- Native `ItemManager.regions` construction could not run in the pure-host fixture (`ReflectionTypeLoadException`/`ECall`); not counted as business Red, not resolved by installing/scanning Unity. IT133 exercises the production classifier shared by the native bridge; the native transport itself remains a Runtime boundary.

## Review and delivery gates

Round 1 used two fresh independent `gpt-6-luna/high` contexts. Standards NON-CLEAN: duplicate IT88–IT90 identifiers; fixed by moving cases to IT133–IT137. Spec NON-CLEAN: AC8 lacked possible-half-packet exit/return baseline closure; added IT137 with observed Red and Green. Reports/provenance: `.scratch/matt-orchestrate/item-runtime-repair-20261025-vela/messages/R02-round1-{standards,spec}-report.md`, `R02-reviewer-native-provenance.json`.

Round 2 used two fresh independent `gpt-6-luna/high` contexts and remained NON-CLEAN. Standards reported one judgment-only smell: `ItemReturnSettlement` builds region/observer/token string keys and reconstructs matching suffixes for notice dedupe/release (review report lines 1–4). It is explicitly deferred as a maintainability follow-up; colon-delimited numeric identity remains behaviorally adequate, with no documented hard-rule violation. Spec blocked on AC5: the production `Finish` binder catch ignored `SafeToRetry=true`; it also noted a possible AC7 first-trace reopen gap. Original reports: `.scratch/matt-orchestrate/item-runtime-repair-20261025-vela/messages/R02-round2-{standards,spec}-report.md`; native model/effort proof: `R02-round2-reviewer-native-provenance.json`.

The resident expert supplied G1; its one authorized fresh dual-axis round was cumulative round 3. Standards was NON-CLEAN on a new AC7 connection-cleanup identity collision; Spec was CLEAN. The root authorized one strictly bounded `R02-AC7-Collision` increment and one fresh round 4. IT144 observes the pre-fix production disconnect path Red (6 traces vs 5 expected), then Green after replacing only this failure set with a typed field key and exact observer/token cleanup. This does not reopen G1 retry work, reset the three-round history or defer the defect as the separate settlement notification-key smell. The ticket now records `implemented-pending-runtime` / `ready-for-human` after both round-4 axes returned CLEAN.

**Delivery:** `.scratch/item-runtime-repair-20261014/issues/02-guest-item-delivery-delivery.md`. Final scoped commit and root Vela receipt are recorded after round-4 CLEAN. No push, deployment, release, or Runtime PASS is authorized.


## R02-AC7-Collision incremental evidence and gate

- Collision identity: disconnect observer 5/token 7; retain `region Y=5, observer=7, token=7`. Before repair IT144 followed the real `OnObserverDisconnected -> ReleaseObserver -> ClearObserverFailuresForConnection` path and reproduced 6 first-failure traces instead of 5 (runner exit 1). The same test confirms the target connection's records across two regions and both failure kinds are cleared, while the colliding observer remains deduplicated.
- Minimal repair: `_firstObserverFailureKeys` now stores a typed `ObserverFailureKey` (region, observer, token, failure kind); connection cleanup filters exact observer/token fields. No other key family or G1 policy changed.
- After repair: Release production build exit 0; Whitelist Release build exit 0; complete runner exit 0, `614/614 PASS`. Detailed logs and prior 613 G1 evidence remain separately preserved.
- Review history: round 1 NON-CLEAN, round 2 NON-CLEAN, round 3 Standards NON-CLEAN / Spec CLEAN. Round-3 native identities were Standards `01a171fc-b5fd-7aa2-b80b-e6e19212fd3a` and Spec `01a171fc-da2b-7f22-9cf9-b0539f099734`, both `gpt-6-luna/high`; reports are `messages/R02-round3-standards-report.md` and `R02-round3-spec-report.md`, with provenance in `R02-round3-reviewer-native-provenance.json`. Root authorization permits exactly one new independent dual-axis round 4 for the complete candidate.
- The scoped source commit records this reviewed candidate. No push, deployment, release, or Runtime PASS; SP/listen-host/U3DS/P2P runtime evidence remains pending.


## Final authorized review — AC7-Collision / R02 cumulative round 4

- The frozen candidate was the complete R02 delta from `3fb2eb5e87ab3eddbc6782fcc708418a7d11d52a`, excluding protected user changes and unrelated untracked files. Two fresh independent reviewers were launched as `/root/standards_r4` and `/root/spec_r4`; their actual native rollouts are parented to builder native `01a171af-7956-7631-aac7-b1c24a6aa8fb`.
- Standards: CLEAN, native `01a17207-c160-70e2-9779-1627e0b4bd4e`; Spec: CLEAN, native `01a17207-ed09-7c01-897f-fd60bdd55530`. Both native `turn_context` records confirm `gpt-6-luna/high`. Full reports: `.scratch/matt-orchestrate/item-runtime-repair-20261025-vela/messages/R02-round4-standards-report.md`, `R02-round4-spec-report.md`; native session provenance and final-answer line locations: `R02-round4-reviewer-native-provenance.json`.
- Standards confirms the sole round-3 P2 was corrected by field-exact observer/token matching and that the prior `ItemReturnSettlement` notification string key remains an explicitly deferred maintainability smell, not a new blocker. Spec confirms IT144 exercises the actual disconnect cleanup chain and retains the full R02 AC1–10 and G1 retry, unknown-result, lifecycle and runtime boundaries.
- Final static evidence is Release production build exit 0, Whitelist build exit 0, runner exit 0 (`614/614 PASS`, Failed 0). Exact logs are under `.scratch/matt-orchestrate/item-runtime-repair-20261025-vela/messages/`. Guest Runtime remains PENDING for SP, listen-host, U3DS and P2P; no player test or deployment was performed.
- Cumulative history is retained: rounds 1 and 2 NON-CLEAN; round 3 Standards NON-CLEAN / Spec CLEAN; round 4 both CLEAN. This is the AC7-Collision increment's only authorized fresh review round.
