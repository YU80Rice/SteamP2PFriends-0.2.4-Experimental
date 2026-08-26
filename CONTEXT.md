# SteamP2PFriends & Unturned Multiplayer Context

Unturned P2P listen-host multiplayer ecosystem that provides server-authoritative co-op without dedicated servers (U3DS), powered by a Minecraft LAN-style chunk lease architecture.

## Architecture & Lifecycle

**Control Plane**:
The pure in-memory coordination engine (zero Unturned/Unity dependency) managing observers, leases, epochs, generational tokens, and spatial indexing.
_Avoid_: Core patch, background manager

**Data Plane**:
The execution layer consisting of domain adapters and transport channels that perform IL hook interception, RPC transmission, and game state mutation.
_Avoid_: Worker thread, hook manager

**Multi-Observer Core**:
The central engine that tracks player presence, computes region demand unions, and manages activation leases.
_Avoid_: Patch pool, global ticker

**Spatial Observer Index**:
The unified spatial partitioning module calculating observer-to-region spatial presence masks and broadcasting diff events to all domain adapters.
_Avoid_: Per-adapter distance check, polling grid

**Domain Adapter Pipeline**:
A declarative, pluggable registry of `ILifecycleDomainAdapter` and `IStateReplicationAdapter` implementations driven by the Control Plane.
_Avoid_: Hardcoded switch-case, patch dispatcher

**Production Control Seam**:
The single production seam through which World Presence Observer changes reach the Control Plane and then the Domain Adapter Pipeline.
_Avoid_: Shadow path, direct adapter call

**Structure Baseline**:
The agreed repository shape, naming, metadata, evidence, and collaboration rules that must be established before functional repairs are migrated.
_Avoid_: Feature freeze, temporary cleanup

**Migration Slice**:
A bounded domain move from the legacy production path to the Production Control Seam, with behavior preserved and evidence proving the old writer is no longer authoritative.
_Avoid_: Big-bang rewrite, parallel writer

**Resource Migration Sample**:
The first Migration Slice, using Resource as the reference domain for Region Lease, spatial demand, collision, harvest, and replication integration.
_Avoid_: Resource feature, M6 implementation

**Authority Writer**:
The one production path allowed to mutate a given domain state during a Migration Slice.
_Avoid_: primary service, active implementation

**Patch Registration Orchestrator**:
The top-level registration module that orders domain, transport, security, diagnostic, and verification registration without owning their details.
_Avoid_: patch pool, mega registry

**Evidence Class**:
The type of proof attached to a claim: PureMemory, StaticIL, BuildArtifact, or Runtime.
_Avoid_: test level, confidence score

**Behavior-Preserving Structural Change**:
A repository or plugin organization change that preserves patch targets, order, protocols, configuration, state-machine semantics, and release identity.
_Avoid_: safe refactor, cleanup refactor

**Domain Ownership**:
The rule that assigns a patch, adapter, test, or document to the one domain or responsibility whose behavior it serves.
_Avoid_: file grouping, folder preference

**Module Ownership Catalog**:
The structure-only catalog that records each module's physical authority root, authority type,
and Registration Trace coverage; it does not register patches or own runtime state.
_Avoid_: runtime registry, duplicate module index

**Platform Boundary**:
The physical boundary for client, host, transport, UI, and diagnostics integrations with
Unturned, Steamworks, Unity, or other external runtime services.
_Avoid_: Core business logic, second transport entry

**Controlled Cross-Domain Patch Set**:
The single `Core/Patches` location for patches whose call chain cannot prove one domain owner;
each retained patch requires a documented reason and is registered only by the Patch Registration Orchestrator.
_Avoid_: catch-all folder, copied compatibility patch

**Metadata Source**:
The single build-owned source from which version identity is propagated to assembly metadata, logs, documentation checks, and audit output.
_Avoid_: version string, release label

**Domain Id**:
The immutable machine identity of a domain, distinct from its display name and stable across localization or presentation changes.
_Avoid_: DomainName, DomainKey, display name

**Region Key**:
The canonical typed identity of a two-dimensional world region, including its coordinates and encoding rules.
_Avoid_: raw region int, packed coordinate

**Bound Key**:
The canonical identity of a navigation bound used by the Zombie domain, kept distinct from a two-dimensional Region Key.
_Avoid_: zombie region key, byte region

**Evidence Gate**:
The acceptance rule that requires the relevant Evidence Class before a Migration Slice can advance.
_Avoid_: green build, PASS flag

**Registration Closure**:
The point after bootstrap at which the Domain Adapter Pipeline becomes immutable for the current plugin session.
_Avoid_: late registration, dynamic patch load

**SDK Registration Order**:
The ordering of plugin registration and lifecycle hooks derived from the traced U3-SDK runtime call chain, not from an abstract plugin-only sequence.
_Avoid_: startup guess, arbitrary initialization order

**Build Fingerprint**:
The runtime-visible identity evidence for the loaded plugin build, combining version metadata with independently reproducible assembly identity data.
_Avoid_: log version, build label

**Independent Artifact Verification**:
The acceptance-side recomputation of a supplied plugin artifact's identity, used to corroborate rather than merely trust a runtime self-report.
_Avoid_: user hash, log confirmation

**Registration Trace**:
The documented mapping from a plugin registration module to the U3-SDK event, callback, or native call-chain position it depends on.
_Avoid_: startup order list, patch order guess

**Migration Manifest**:
The per-batch record of moved files, namespace ownership, preserved patch metadata, retired writers, evidence classes, and unresolved items.
_Avoid_: change summary, file list

**Adapter Fault Supervisor**:
The universal circuit breaker that detects demand mismatches or runtime exceptions in specific adapters/regions, quarantining destructive actions without affecting other domains.
_Avoid_: Global try-catch, crash handler

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
