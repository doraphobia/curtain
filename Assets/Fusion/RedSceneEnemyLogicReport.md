# RedScene Enemy Logic Report

This report describes the enemy logic that is currently implemented for RedScene, and separates it from the intended future design.

## Boot World Rule

RedScene now creates a Boot World controller at runtime if one is not already present in the scene.

During Boot World:

- Day/night simulation can keep running.
- Night enemy spawning can keep running.
- Enemies can wander and leave footprint traces.
- Player control, gameplay UI, minimap/topology UI, and gameplay camera behaviours are disabled by the Boot World controller.
- Fusion enemies and legacy enemies are prevented from confirming, tracking, chasing, damaging sanity, selecting doors, breaking doors, or entering rooms.
- Spawn warning UI is suppressed while Boot World is active.

The temporary title UI is generated with TMP text:

- Logo
- Press Any Key
- Language
- Settings
- Quit

Keyboard input, empty-space mouse clicks, and generic gamepad buttons can start the gameplay transition. Clicking temporary UI buttons does not start gameplay.

## Gameplay Canonical Start

Gameplay canonical start means: after the living title screen ends, what exact game state should normal gameplay begin from?

Because Boot World is a real running world, time can already be moving before the player starts. Without a canonical start, pressing start might begin gameplay at a random point in the day/night cycle, depending on how long the player stayed on the title screen.

The current Boot World controller exposes:

- gameplayStartStageIndex
- gameplayStartStageTimer
- dayNightFastForwardMultiplier
- maxDayNightFastForwardSeconds
- snapDayNightAtTransitionEnd

These let the title transition fast-forward the existing day/night cycle toward a chosen starting point. The default assumption is stage index 0, timer 0, which usually means the first day stage starts at the beginning. This is not a final design decision yet.

## Current Fusion Enemy Pipeline

Runtime owner:

- `FusionNightEnemySpawner`

Runtime enemy:

- `FusionNightFootprintEnemy`

Supporting systems:

- `EnemyFootprintTrace`
- `EnemyFootstepAudio`
- `VisionSensor2D`
- `VisionRenderController`
- `VisibilityWorld`
- `CombatAttackSource`
- `RuntimeTileMeshFusionDoor`

## Spawn Flow

1. `FusionNightEnemySpawner` watches `StageCycleController.IsNight`.
2. When night starts, it spawns up to `enemiesPerNight`, while respecting `maxActiveEnemies`.
3. Spawn candidates are chosen outside the fusion floor bounds.
4. During normal gameplay, offscreen spawn warning UI can appear before the enemy is created.
5. During Boot World, that warning is suppressed.
6. When day starts, outdoor enemies can be cleared. Indoor enemies are preserved by the existing cleanup rule.

## Fusion Enemy State Tree

The active Fusion enemy state enum is:

- `WanderOutside`
- `WatchingWindow`
- `TargetingDoor`
- `BreakingDoor`
- `EnteredRoom`
- `ChasingPlayer`

Current flow:

1. `WanderOutside`
   - Enemy picks random outdoor waypoints around the fusion world bounds.
   - Movement is clamped so it does not casually walk into fusion floors.
   - Footprints and synced Foley footsteps can spawn while moving.

2. Visibility check
   - Enemy samples `VisionSensor2D`.
   - The sampler reads `VisibilityWorld`.
   - If the player's world position is inside the sampled vision snapshot, detection succeeds.
   - Current detection is immediate for behaviour.
   - The visual cone has alert color progress, but this is not yet the full 1-second gameplay confirmation gate.

3. `WatchingWindow`
   - Used when detection happens but no valid fusion door can be selected.
   - Footprint spawning can pause in this state.

4. `TargetingDoor`
   - Enemy selects the nearest active `RuntimeTileMeshFusionDoor`.
   - Enemy moves toward the door.
   - Footsteps can become faster/louder via state mix parameters.

5. `BreakingDoor`
   - Enemy stops moving.
   - Footprints and footsteps pause.
   - `CombatAttackSource` repeatedly applies impact damage to the door.
   - Door hit/break audio and UI should come from combat/receiver listeners, not directly from enemy movement code.

6. `EnteredRoom`
   - A short delay runs after the door is destroyed or open.

7. `ChasingPlayer`
   - Enemy moves directly toward the player's current position.
   - Enemy can drain sanity on contact.

## Footprint And Foley Logic

`EnemyFootprintTrace` owns visual footprint timing:

- Alternates left and right.
- Uses movement direction to offset heel/foot placement.
- Pauses while watching or breaking doors.
- Uses faster interval when targeting doors or chasing.
- Calls `EnemyFootstepAudio.PlayFootstep(...)` at the exact footprint spawn point.

`EnemyFootstepAudio` now owns 3D Foley playback:

- Each enemy gets its own `FoleyPlayer`.
- Indoor surface defaults to `Concrete`.
- Outdoor surface tries `Outdoor`, then falls back to `Grass`.
- Left/right currently influence landing point and rhythm, not separate clip banks.
- Chase, targeting-door, watching, and normal movement each have Inspector volume/pitch multipliers.

## Vision Rendering

Current Fusion enemy vision rendering:

- `VisionSensor2D` samples manually from the enemy's current facing direction.
- `VisionRenderController` renders the sampled snapshot.
- Searching color is yellow.
- Alert color progresses toward red when the visual system sees the player.
- In Boot World, alert progress is forced back to 0, so enemies should stay in passive/searching visual mode.

## Legacy Enemy Pipeline

The older `EnemyController` still exists.

It uses a separate state tree:

- `SpawnOutside`
- `SearchOutside`
- `DetectPlayer`
- `MoveToExteriorDoor`
- `BreakingDoor`
- `EnterRoom`
- `ChasePlayer`
- `AttackPlayer`
- `LostPlayer`
- `SearchLastKnownRoom`

It also has older room/window concepts such as `Room`, `BreakableExteriorDoor`, `WindowPortal`, and `EnemyVision`.

This legacy controller now also respects Boot World passive mode, but RedScene should use the Fusion enemy path as the primary reference.

## Known Design Gaps

The following behaviours are not fully implemented yet:

- Behaviour-level 1-second confirmation before chase.
- Recording player last-known position every 0.5 seconds while visible.
- Searching left/right around the last-known position after losing sight.
- Entering a room through opened doors without breaking them when exiting/searching.
- Door target selection based on the detected room/opening instead of simply nearest door.
- A topology-aware enemy pathfinder for room interiors and outdoor-to-door travel.
- Final Boot World title layout, settings, language switching, and game start transition visuals.
- Final canonical gameplay start choice.

## Recommended Next Decisions

1. Choose the canonical gameplay start:
   - Start at morning / first day stage.
   - Start at the current simulated Boot World time.
   - Start at a fixed pre-night warning stage.

2. Decide if Boot World should always begin in a specific day/night state, or inherit whatever RedScene currently has.

3. Decide whether title-screen enemies should leave permanent footprints or clear them on gameplay start.

4. Decide if enemy detection should first become purely visual-red progress for 1 second, then become gameplay detection.

5. Decide whether Fusion enemy should replace legacy `EnemyController` entirely, or whether legacy rooms must remain supported.
