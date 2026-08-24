# SteamP2PFriends & Unturned Multiplayer Context

Unturned P2P listen-host multiplayer ecosystem that provides server-authoritative co-op without dedicated servers (U3DS), powered by a Minecraft LAN-style chunk lease architecture.

## Architecture & Lifecycle

**Multi-Observer Core**:
The central engine that tracks player presence, computes region demand unions, and manages activation leases.
_Avoid_: Patch pool, global ticker

**World Presence Observer**:
A connected player (Host, authorized Guest, or pending Guest) who physically occupies the world and projects region demand.
_Avoid_: Client, visitor, user

**Region Lease**:
A functional capability ticket granted to a domain adapter when at least one observer requires that region.
_Avoid_: Chunk lock, region claim

**Hysteresis Release**:
A bounded delay (2 seconds) before destroying region state after the last observer leaves, preventing rapid recreation spikes.
_Avoid_: Immediate unload, instant GC

## State & Epoch Axes

**Session Epoch**:
The unique identifier of an entire listen-host world instance, incrementing only on map transitions or server restart.
_Avoid_: World version, game ID

**Connection Generation**:
A monotonic sequence allocated to a specific observer, incrementing only when that observer reconnects.
_Avoid_: Client token, player epoch

**Region Generation**:
A sequence tracking physical reconstruction of an authoritative world region or bound.
_Avoid_: Spawn counter, map version

**Entity Generation**:
A sequence tracking single-entity recreation or slot reuse.
_Avoid_: Object ID, entity version

## Admission & Security

**Route B Quarantine**:
A 30-second in-game soft-isolation state where unapproved visitors exist in the world but cannot move, attack, interact, or take damage.
_Avoid_: Pre-join queue, handshake block, hard whitelist

**Gameplay Authorization**:
The administrative permission granting an observer full gameplay rights, completely decoupled from world presence and rendering.
_Avoid_: World admission, connection permit
