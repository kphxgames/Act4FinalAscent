# Architect Boss Split Notes

This note is for future modders touching the Architect after the partial-file refactor.

## File Ownership
- `Act4ArchitectBoss.cs`
  - shared fields and properties
  - room lifecycle hooks
  - top-level combat-side hooks
  - compatibility helpers used by multiple partials
- `Act4ArchitectBossStateMachine.cs`
  - move graph construction
  - phase entry routines
  - direct move execution bodies
  - intent planning for normal phase flow
- `Act4ArchitectBossMechanics.cs`
  - retaliation
  - adaptive resistance / armor helpers
  - summon scheduling
  - linked-shadow bookkeeping
  - ambient loop and finish-run helpers
- `Act4ArchitectBossPresentation.cs`
  - speech
  - beam styling
  - merchant cameo
  - judgment / legend / front-layer VFX
- `Act4ArchitectBossPhaseFour.cs`
  - phase 4 rotation
  - Oblivion countdown logic
  - phase 4-only aura/background/state helpers

## Editing Guide
- Changing normal move cadence or previews:
  - start in `Act4ArchitectBossStateMachine.cs`
- Changing retaliation or summon-trigger behavior:
  - start in `Act4ArchitectBossMechanics.cs`
- Changing Architect dialogue, VFX colors, beam visuals, merchant staging:
  - start in `Act4ArchitectBossPresentation.cs`
- Changing Oblivion timing or phase 4 turn order:
  - start in `Act4ArchitectBossPhaseFour.cs`

## Important Design Rules
- Phase 1/2/3 previews should be planned from the state machine, not corrected later in `AfterSideTurnStart`.
- Strength preview variants exist so players can see the passive Buff intent before the enemy turn actually begins.
- Phase 4 is intentionally more custom than earlier phases because its next move depends on live countdown and shadow state.
- `SetMaxHpCompatAsync` exists because the base game changed return signatures across builds. Keep that helper unless the compatibility target changes.

## Pre-Public Reminders
- Keep new files UTF-8 with BOM, same as the rest of this mod.
- Avoid hardcoded local paths in comments, logs, or helper docs.
- If new player-facing strings are added, remember the localization flow and fallback behavior.
