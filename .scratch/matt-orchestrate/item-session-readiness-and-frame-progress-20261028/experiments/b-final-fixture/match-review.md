# SFP-R2-G1 preparation for expert equivalence check

Status: source match accepted by the original expert under the same SFP-R2-G1 attempt; RED/GREEN completed only after activation. Results and identities: `g1-experiment-result.md`; complete per-process evidence: `logs/`.

## Isolated candidates and fixture

- `baseline.tar` is `git archive` of fixed HEAD `5c496c0b3c8aa61ba825348bec637071759223c0`.
- `green/` overlays the current candidate's production/test C# compile inputs on that archive; it does not include main-tree `bin/obj`.
- `red/` starts from the same archive and overlays the same current candidate inputs, including A initialization fixes and strict tests. Its only additional source delta is the coordinator old-gate extraction described below.
- RED and GREEN `ItemPreparationAndFirstDemandTests.cs` and Whitelist csproj were byte-compared equal. The test's 12 Natural items, late-input Begin, real observe/leave/2-second expiry setup, shared callback, 21 frames, first-frame budget, held/no-partial-delivery and one final delivery assertions are unchanged.
- Test callback is the frozen strict fixture: ObserveItemAuthority + shared.Flush only; no `seam.Flush` was inserted.

## RED production seam and order

`SteamP2PFriendsPlugin.Update -> MultiObserverShadowCoordinator.Tick -> RunActiveFrame` is the real production route. RED keeps that route and extracts the former due callback without changing its order:

1. Each frame: `ResourceProduction.AdvanceTime(deltaTime)` through `AdvanceEngineClock`.
2. Non-due frame: return at `_nextReconcileAt` (the old one-second gate; no Item consumer).
3. Due frame: capture/admission, identity commit, ledger and Resource/Collision/Item reconcile.
4. Preserve old shared-clock/flush aliases: `ControlPlaneLifecycle.AdvanceTime`, `ControlPlaneLifecycle.Flush`, then `ResourceProduction.Flush`.
5. Run one actual `ItemProduction.Flush(0f)` in the extracted frame method; this is the original due consumer and includes Item seam shared flush plus `StepItemWork`.
6. Invoke the continuation in original order: transitions, `AuditNativeState`, summary, shared-fault recovery, write resume and backoff success.

Capture rejection returns false before Item flush/continuation; callback exceptions propagate before them. Exceptions from the old Item flush or continuation propagate directly and truncate the remaining tail, matching the old call shape; RED adds no catch/reset behavior. GREEN is the frozen current `RunActiveFrame`: due-only refresh/cycle plus one bounded Item consumer per eligible frame.

## Inputs and commands (run only after expert match)

Each project is rooted independently at this directory's `red/` or `green/`. Exact resolved paths for every production and whitelist `HintPath`, including pre-build existence, are recorded in `hintpath-inventory.md`. Production `..\Libs\*.dll` and test external `..\..\Libs\*.dll` resolve to the common experiment `Libs/`; it is a 30-file byte-for-byte copy from `D:\Agent-工作目录\DevelopMyUNMultiplayerModAndModloader\Libs`, marked read-only, with original Libs untouched. The test project's production reference resolves to each case's own `bin/Release/SteamP2PFriends.dll`, intentionally absent until that case's production build. `bin/`, `obj/`, and test output remain case-local. The target runner uses declared `Libs/Mono.Cecil.dll` to read MVIDs from physical production DLL, test-copy DLL and test EXE before loading; it loads only the test-copy plugin, then checks the unique loaded plugin's exact case path/MVID and the loaded test EXE's exact path/MVID before and after target invocation. Target false or exception exits nonzero.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File '<absolute b-final-fixture/run-sfp03-case.ps1>' -CaseName RED
powershell.exe -NoProfile -ExecutionPolicy Bypass -File '<absolute b-final-fixture/run-sfp03-case.ps1>' -CaseName GREEN
```

Run RED then GREEN as shown, with the same fixture/input. The case script checks `$LASTEXITCODE` immediately after each `dotnet msbuild` and target process, stopping that case on nonzero. It records each exact command, start/end UTC, stdout, stderr and exit separately under a fresh `logs/<CASE>/attempt-N/`; a failed earlier attempt is never overwritten. Its `finally` only restores the location after the exit has already been captured. The target runner reports physical metadata and actual loaded test/plugin identities; it does not claim to enumerate every loaded assembly. Only the existing Framework target runs after the existing BattlEye type-resolution prelude.

RED production build alone passes `/p:WarningsNotAsErrors=CS0414`: its old-gate coordinator keeps the current progress field for strict-fixture reflection compatibility, but that B-only field is intentionally write-only in the old cadence. The compiler still reports this single warning; this does not suppress it or alter generated behavior. No other warning is relaxed.
