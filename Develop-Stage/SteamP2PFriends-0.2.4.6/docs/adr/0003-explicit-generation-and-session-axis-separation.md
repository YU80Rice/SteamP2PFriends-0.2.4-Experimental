# 0003: Explicit Generation and Session Axis Separation

## Context
When a player disconnects, reconnects, or moves between regions, compressing world session state, connection tokens, region rebuilds, and entity IDs into a single boolean (like `isItemsLoaded` or `isNetworked`) causes ghost data, duplicate spawns, and stale events poisoning new sessions.

## Decision
We separate state into four independent monotonic axes:
1. `SessionEpoch`: tracks the entire listen-host server lifetime.
2. `ConnectionGeneration`: tracks an individual player's active connection instance.
3. `RegionGeneration`: tracks physical rebuilds of a region or navigation bound.
4. `EntityGeneration`: tracks individual entity recycling within a bound.

## Consequences
- Single player disconnection increments only `ConnectionGeneration`, never discarding other online players' leases or resetting `SessionEpoch`.
- Re-entering a region cleanly detects stale baselines and safely re-enqueues initial snapshots.
- Teardown and reconnect state machines operate deterministically without race conditions.
