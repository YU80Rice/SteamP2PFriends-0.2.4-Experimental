# RED versus GREEN source delta

Only `Core/ControlPlane/MultiObserverShadowCoordinator.cs` differs semantically between the isolated source roots. The other eight C# inputs and whitelist project are byte-identical to each other and the current candidate; production project XML is text-equivalent, with line endings as its only byte difference. That production project differs from the main candidate only by line endings.

## RED (`red/Core/ControlPlane/MultiObserverShadowCoordinator.cs`)

- Tick at lines 483–486 passes `ReconcileCurrentObservers(now, out afterItemWork)` through the four-argument seam.
- Reconcile at lines 489–523 returns false on rejected capture; otherwise it keeps the old `ControlPlaneLifecycle.AdvanceTime`, `ControlPlaneLifecycle.Flush`, and `ResourceProduction.Flush` calls, then records the continuation.
- Continuation at lines 526–568 contains the original transitions/audit/summary/fault-recovery tail.
- Frame seam at lines 575–587 advances the shared clock, returns on `now < _nextReconcileAt`, and on due frames runs the true callback, the historical exact condition `if (ItemProductionMode == ItemProductionMode.Candidate) ItemProduction?.Flush(0f)`, then the continuation. The condition, null semantics, and direct exception propagation match the fixed baseline Tick consumer; no writer/readiness/fault gate or catch/reset is added around that old call.
- The three-argument `RunActiveFrame` is the exact test entry point and delegates to that same core seam; no test scheduler or runtime mode switch exists.
- The extracted delegate type is spelled `System.Action` to resolve `SDG.Unturned.Action` ambiguity; this is compile-only qualification. RED retains the progress field because the unchanged strict fixture reflects it; the old-gate build leaves it write-only, so only that known CS0414 warning is retained as a warning, not an error.

## GREEN (`green/Core/ControlPlane/MultiObserverShadowCoordinator.cs`)

- Tick at lines 483–484 passes the current `ReconcileCurrentObservers(now)` callback.
- Reconcile at lines 486–556 retains the current single shared flush and complete original tail.
- Frame seam at lines 559–580 reconciles only when due, refreshes settlement demand only when due, then consumes at most one settlement action on every eligible frame; natural cycle step remains due-only.

The only behavior distinction under the shared strict fixture is the production cadence/location of the existing `ItemProduction.Flush` consumer: RED only after the one-second due gate; GREEN once per eligible frame. The complete actual source files are the reviewable diffs; `match-review.md` records the exact order and runner inputs.
