# Combat Interaction Framework

`CombatAttackSource` performs discrete Windup -> Impact -> Recovery attacks. It knows only `IDamageReceiver`.

`CombatHealth` owns HP, repair/reset, invulnerability, delayed destruction, and publishes one immutable `ImpactEvent` for every accepted hit. `CombatEventBus` publishes receiver destruction separately for navigation and world-state listeners.

Feedback is event-driven:

- `ImpactCameraFeedback` snapshots the currently enabled gameplay camera through `CurrentCameraService` and layers immutable directional shake instances.
- `ImpactObjectFeedback` provides one local hit reaction per impact.
- `ImpactAudioFeedback` is an optional receiver-filtered spatial listener.
- `DamageReceiverProgressPresenter` listens to health changes and owns the break progress UI.
- `CombatDebugOverlay` is a runtime-only debug reader. Press F9 to show current camera,
  published impacts, active camera shakes, attack phases, and receiver HP.

Doors adapt `IDamageReceiver`; enemies never reference cameras, progress bars, audio, or visual feedback.
