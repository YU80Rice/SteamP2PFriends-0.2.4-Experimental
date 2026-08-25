# 0002: Decouple Observer Presence from Gameplay Authorization

## Context
In Route B admission, unknown guests join the server and enter a 30-second in-game quarantine before host approval. Historically, developers conflated "whitelisted/approved" with "should load world/receive snapshots", causing guests to get stuck on black loading screens or be ignored by region demand logic.

## Decision
We strictly decouple `WorldPresenceObserver` (physical presence in the world, generating region demand and receiving initial baseline snapshots) from `GameplayAuthorized` (permission to move, attack, interact with buildables, or run console commands).

## Consequences
- Unapproved guests properly load the world, see terrain, and view items/zombies in isolation without blocking native loading gates.
- Security is strictly enforced server-side via action sanitizers and RPC authority gates, not by preventing world synchronization.
