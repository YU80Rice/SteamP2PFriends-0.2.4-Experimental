# 0001: Minecraft LAN-Style Chunk Lease Architecture

## Context
In Unturned listen-host co-op, simulating the entire map like a dedicated server (U3DS) wastes CPU and memory on headless clients, while patching individual systems ad-hoc (0.2.3 P0 patches) causes dual-writer conflicts, race conditions, and inconsistent state across regions.

## Decision
We adopt a Minecraft LAN-style chunk ticket lease architecture (`MultiObserverLeaseEngine`). When any player enters a region, the engine acquires a functional lease (`0 -> 1 Acquire`), driving authoritative logic and syncing baselines. When the last player leaves, the engine waits 2 seconds (`Hysteresis Release`) before safely releasing and destroying unneeded state.

## Consequences
- World simulation scales strictly with the union of active player regions, not total map size.
- Legacy ad-hoc P0 patches are systematically retired in favor of clean `ILifecycleDomainAdapter`s.
- Host machine no longer wastes rendering resources on distant guest areas.
