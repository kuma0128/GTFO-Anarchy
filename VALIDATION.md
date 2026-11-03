# Anarchy validation

## 1.1.7 dependency update

Validated on 1 November 2026 (JST), with GTFO revision **34873**, using the same CLI tooling as 1.1.6 (CConsole 0.14.0 and the temporary validation plugin). The runtime contained the 41 packages in the [runtime dependency list](tools/runtime-dependencies.json), including BepInExPack_GTFO 3.2.4, GTFO-API 0.6.0, CheeseGeos 0.6.0, AdvancedWardenObjective 2.6.3, AmorLib 1.3.0, EnemyAnimationFix 1.5.0, OptionalNetworkAPI 1.3.2, MemoryLeakFix 1.3.10 and SnatcherBugFix 0.6.0. DoorEnemyFix was not installed.

| Stage | Result | Scan instances checked | Error/Exception lines | Warning lines |
| --- | --- | ---: | ---: | ---: |
| C1 | InLevel; audit passed | 0 | 23 | 39 |
| C2 | InLevel; audit passed | 4 | 259 | 101 |
| D1 | InLevel; audit passed | 1 | 69 | 357 |
| D2 | InLevel; audit passed | 9 | 3419 | 80 |
| D3 | InLevel; audit passed | 5 | 126 | 48 |

**19/19** configured scan instances were registered at their configured positions, and all sampled navigation-mesh points were within **0.0975 m**. Fog block 225 passed in all five runs.

- **Compared with 1.1.6:** after normalizing paths and numbers, the build-phase Error/Exception lines of C2, D1, D2 and D3 are identical to the 1.1.6 run. C1 differs only in its entrance wave (two more spawn-node fallbacks, one ladder message, two fewer door-wave messages), which depends on the unattended player at the elevator as described below.
- **New startup messages:** `Assembly OptionalNetworkAPI.dll is not registered in il2cpp` from Il2CppInterop when the new EnemyAnimationFix dependency loads (EnemyAnimationFix and all later plugins still loaded), and GTFO-API reporting that `mining_reactor_01_gencluster_v1.prefab` is already registered. CheeseGeos 0.6.0 ships byte-identical `markers-compressed` and `rlc_markers-compressed` bundles; the first registration is used.
- **CheeseGeos paths:** a control run of D3 with deliberately missing paths logged `Asset ... did not loaded!` and `tile prefab is null`, and generation never finished (timeout). The passing run therefore loaded the new 0.6.0 paths. A second control run with the 1.1.6 paths also reached `InLevel`, because CheeseGeos 0.6.0 still ships its legacy `rlc_mining` bundle; that compatibility depends on the bundle remaining in future CheeseGeos versions.
- **Not covered by the build runs:** the same limits as 1.1.6 apply. Warden events, snatcher capture/release and warden intel across consecutive expeditions were checked afterwards (next section); full mission clears and multiplayer synchronization were not tested.

## 1.1.7 events, snatchers and warden intel

Checked on 3 November 2026 (JST) with the same runtime, GTFO revision **34873**, using `tools/game-validation/checks.cjs` and the checks added to the validation plugin (`RuntimeChecks.cs`). God mode was on and the game ran at normal speed (`TimeScale 1`).

### Warden events (AdvancedWardenObjective 2.6.3)

AWO hooks the game's event coroutine. It takes over only its own event types (10000 and above) and four vanilla types when they use fields the game ignores. In C1–D3, Anarchy has one AWO event, `CloseSecurityDoor` in D1 ZONE 7 `EventsOnOpenDoor` (it closes the ZONE 4 door behind the team), and no fields that trigger AWO's vanilla overrides. Every other event passes through the hook to the game's own executor.

Every warden event of the built levels (zone and terminal command events, objective events including chained objectives and reactor waves) was run one at a time through `WardenObjectiveManager.CheckAndExecuteEventsOnTrigger`, and its effect was checked. Delays were cleared for the call, so delayed timing itself was not tested.

| Stage | Events | EventBreak (not run) | Effect confirmed | Logged by AWO's hook |
| --- | ---: | ---: | ---: | ---: |
| C1 | 12 | 0 | 12 | 12 |
| C2 | 25 | 1 | 22 | 24 |
| D1 | 49 | 3 | 46 | 46 |
| D2 | 34 | 0 | 34 | 34 |
| D3 | 23 | 0 | 22 | 23 |
| **Total** | **143** | **4** | **136 / 139** | **139 / 139** |

- **Types covered:** SpawnEnemyWave 35, OpenSecurityDoor 14, UnlockSecurityDoor 13, StopEnemyWaves 11, UpdateCustomSubObjective 9, AllLightsOff 7, SetFogSetting 5, DimensionWarpTeam 4, AllLightsOn 3, DimensionFlashTeam 3, ForceCompleteObjective 2, ActivateChainedPuzzle 2, CloseSecurityDoor (AWO) 1, and 30 events that only show warden intel. Every event with warden intel sent it to the HUD.
- **D1 played through its own triggers:** opening the ZONE 7 door showed "Reboot the alarm system…" and AWO closed the ZONE 4 door (Closed, not traversable). Typing `REBOOT_SECURITY_OVERRIDE` at the ZONE 7 terminal reopened it, and "Alarms are successfully deactivated." followed 5 s later. Walking into the first ExtraObjectiveSetup security sensor turned the lights off and started its wave; the lights came back on about 20 s later.
- **Terminal command scans:** both ActivateChainedPuzzle events belong to terminal commands (C2 `OVERRIDE_LOCKDOWN`, D2 dimension 1 `COPY_GENOME_DB`). Typing each command at its terminal started its scan and a wave within 5 s.
- **Not shown on the HUD (existing data, not AWO):** the three UpdateCustomSubObjective events whose text is written inline (C2 `EventsOnElevatorLand[0]` and `EventsOnActivate[16]`, D3 chain objective `EventsOnActivate[0]`). The objective state syncs custom sub-objectives as TextDataBlock IDs (`pWardenObjectiveInteraction.newCustomSubObjectiveTextID`), and inline text has ID 0. D1's sub-objectives, which reference TextDataBlocks, are shown. AWO does not handle this event type.
- **Data note (existing):** C1 chain objective `EventsOnGotoWin[2]` starts a wave with settings 111111 ("Trickle 4-30 SSpB 2"). The game reads the ID as 45575 and logs `SurvivalWave got no SurvivalWaveSettings with ID 45575`, so this exit wave has no settings. IDs above 65535 do not survive.
- **Logs:** the error lines during the run are the game's own messages (their text is in the game's metadata, not in any plugin): `OnDoorIsOpened: LinkedToZoneData`, `OpenSecurityDoorWithActiveEnemyWave`, spawn-node fallbacks, and, in C1, an out-of-range lookup of `Dimension_1` while a door's zone events ran. No exception came from AWO.
- **AWO 2.6.4** (released 3 November 2026, requires GTFO-API 0.6.1) fixes delayed AWO-handled events that fire before `InLevel`, such as events on elevator land. Anarchy's elevator-land events are vanilla types that AWO passes through; with 2.6.3 they ran on landing in C2 and D1.

### Snatcher capture and release (SnatcherBugFix 0.6.0)

In D3, a snatcher (enemy 46) was spawned 7 m in front of the idle player, once for each way out.

| Release | Capture | Back in reality | Screen cover cleared | Standing again | End state |
| --- | --- | --- | --- | --- | --- |
| Killed 4 s after capture | Moved to the arena dimension (Dimension_20) | 0.2 s after the kill | With the warp | 2.3 s after the kill | Stand, alive, 0.06 m from the navigation mesh |
| Spat out | Same | 5.6 s after capture | 0.3 s later | 2.4 s after the warp | Stand, alive, 0.08 m from the navigation mesh |

The pouncer data gives consume 0.23 s, held start 1 s, maximum hold 4 s and spit-out 1.4 s, which matches the spit-out time. While a player is held, Unity logs `ArgumentNullException: Value cannot be null. Parameter name: ps` (49–63 lines per run). The same happens with SnatcherBugFix 0.5.0, with no SnatcherBugFix, and with MemoryLeakFix 1.3.9 plus EnemyAnimationFix 1.4.8, so it does not come from the 1.1.7 updates. Its source was not identified (no stack trace). Capture and release behaved the same in all four setups.

### Warden intel across consecutive expeditions

One GTFO process dropped into D1 three times. In each drop, three of D1's warden intel messages were shown back to back 25 s after landing, and the expedition was aborted while two of them were still queued. The game cannot start a level from the lobby an abort returns to, so the run left the lobby and hosted a new one before the next drop. The HUD was sampled every 0.2 s.

| Setup | Drop 1 | Drop 2 | Drop 3 | Queue after each abort |
| --- | --- | --- | --- | --- |
| As released (WardenIntelOhBehave, MemoryLeakFix 1.3.10) | Shown | Shown | Shown | 2 → 0 |
| Without WardenIntelOhBehave (MemoryLeakFix 1.3.10) | Shown | Shown | Shown | 2 → 0 |
| Control: without WardenIntelOhBehave, MemoryLeakFix 1.3.9 | Shown | **Nothing shown** | **Nothing shown** | 2 → 2, 9 → 2, 16 → 9 |

"Shown" means the four landing messages (the game's objective intro and Anarchy's "Find the Data Cube in ZONE 147") and the first queued message reached the HUD. The control run reproduces the bug: the queue is left over and nothing appears on the next drops. MemoryLeakFix 1.3.10 alone clears the queue at level cleanup, as WardenIntelOhBehave does.

### Not covered

Full mission clears, checkpoints, multiplayer synchronization, delayed event timing, the natural triggers of events outside D1 (they were run directly), and the security sensors of C2, D2 and D3.

## 1.1.6 validation

Validated on 7 September 2026 (JST), with GTFO revision **34873**, Unity **2019.4.21f1**, on Windows. The release fixes data-loading and level-generation issues found while updating the older Anarchy package.

## Follow-up: layout changes found in screenshot comparison

A same-seed comparison on 7 September found that lowering minimum coverage in 1.1.6 **changes the generated rooms**, rather than only suppressing generation diagnostics:

| Zone | Previous layout settings | 1.1.6 | Impact |
| --- | --- | --- | --- |
| C2 ZONE104 | Coverage 40, 2 areas | Coverage 3, 1 area | Smaller; different room asset and position |
| D2 ZONE317 | Coverage 30, 2 areas | Coverage 10, 1 area | Smaller; different room asset and position |

These minimum-coverage changes need review if preserving the original stage scale is the goal. The initial build checks did not catch this change in scale. C1, D1 and D3, and the other zones in C2/D2, retained the same basic area layout measurements: 80 of 82 runtime zones, including dimensions, matched in coverage, area count, room asset and transforms rounded to 0.001 m. This does not establish identical internal walls, stairs or other geomorph child objects, or compare enemies, props, resources, lighting or objective behavior. Use the screenshots to review visual differences within those base assets.

The comparison uses GTFO revision 34873 and the same dependency versions and seeds on both sides. Its previous-layout variant restores only the five main layout JSON files and `GameData_DimensionDataBlock_bin.json` from `31f5257`, keeping the other 1.1.6 fixes active. It is not a test of the complete unmodified 1.1.5 package. Repeated builds reproduced the smaller C2/D2 results. Screenshots were captured in the actual game through the CLI bridge, CConsole Freecam and Unity ScreenCapture, with developer rendering/lighting settings for visibility.

The released 1.1.6 data and tag have not been replaced. See [the parameter explanations](tools/parameters-1.1.6.md) and [the comparison summary](tools/geometry-comparison-1.1.6.json) for the measured changes.

## Changes

- Assign D3's exit fog its referenced ID, **225**, resolving the duplicate ID 224. The runtime now loads `Fog_anarchy_d3_after_exit` with infection `0.05`.
- Restore the D3 scan override file missing from the GitHub source and use `Anarchy_D3_L1` as its lookup key. The secondary-layer scans use the expedition's main layout as their key.
- Remove three empty `{}` GameData files. Current MTFO expects a `Blocks` array and threw exceptions while reading those files. Without them, MTFO loads the standard definitions before PartialData applies Anarchy's layouts, objectives and rundown.
- Correct C1's Refinery/Storage subcomplex selections and reserve its chained-objective pickup placement.
- Enable C2 zone 105 pickup/resource allocation, reserve the large pickup in zone 104, and correct its coverage bounds. Lower zone 104's minimum coverage from 20 to 3, preserving its scan coordinates; the follow-up comparison above documents the resulting shrink.
- Reserve D1's generator-cell placements and restore the desert dimension's standard lighting ID 18 and terminal prohibition.
- Remove D2's unsupported six-scan concurrent-cluster flag. The dependency supports at most four concurrent scans; the six existing scans and their positions are retained. Lower ZONE317's minimum coverage from 20 to 10; the follow-up comparison above documents the resulting shrink.
- Correct D3's reversed coverage range and select Refinery for its exit geomorph.
- Update the 33 direct dependency requirements to the versions used for testing. No debug or cheat plugin is included in the release.

## Checks

CLIからゲーム内へコマンドを送る仕組みと再実行手順は、[game-validationの日本語説明](tools/game-validation/README.md) に記載しています。

The CLI validator parses the shipped data with JSON comments and trailing commas supported. It checks root block IDs, active expedition/layout/objective references, zone indices and parent references, coverage bounds, concrete subcomplex selections, the exit fog reference and scan lookup keys. From a source checkout, run:

```powershell
npm ci --ignore-scripts --prefix tools
npm test --prefix tools
pwsh -File tools/package-release.ps1 -Output C:/output/Anarchy-1.1.6.zip
```

Result: **105 JSON files, five expeditions and 71 active zones passed**. One fully commented template is skipped. The package script verifies every ZIP entry against its source using SHA-256.

Each public expedition was generated by the actual GTFO executable in a separate test installation. A new game process was used for each test. CLI commands created a local lobby, started level generation, waited for `InLevel`, checked runtime data and inspected the BepInEx and Unity logs. All enabled secondary/overload layers and dimensions were generated. Hidden E/X content was excluded.

Test tooling: CConsole **0.14.0**, fast startup/elevator and invulnerability, plus a temporary BepInEx validation plugin. Invulnerability prevents the unattended build check from ending in combat failure. These checks do not simulate a normal four-player mission.

<!-- RESULTS_START -->
| Stage | Result | Scan instances checked | Error/Exception lines | Warning lines | Started (UTC) |
| --- | --- | ---: | ---: | ---: | --- |
| C1 | InLevel; audit passed | 0 | 22 | 39 | 2026-09-06 15:21:47 |
| C2 | InLevel; audit passed | 4 | 259 | 101 | 2026-09-06 15:12:54 |
| D1 | InLevel; audit passed | 1 | 69 | 357 | 2026-09-06 15:14:17 |
| D2 | InLevel; audit passed | 9 | 3419 | 80 | 2026-09-06 15:15:43 |
| D3 | InLevel; audit passed | 5 | 126 | 48 | 2026-09-06 15:17:17 |

**19/19** configured scan instances were registered and at the configured positions (maximum difference 1.907e-6 m). All sampled navigation-mesh points were within **0.0975 m** of the configured positions. Fog block 225 passed in all five runs. Each run used the same candidate game data.

Machine-readable results and log hashes: [runtime results](https://github.com/kuma0128/GTFO-Anarchy/blob/anarchy-1.1.6/tools/runtime-results.json). Error counts include messages containing Exception and exclude the separately captured startup segment. Counts can vary with resource allocation and timing; they are not counts of distinct mod bugs.
<!-- RESULTS_END -->

The scan audit queries the dependency's registered scan instances by override index, compares their actual positions to the configuration, and samples the generated navigation mesh. It covers C2 indices 12–15, D1 index 56, D2 indices 21–29 and D3 indices 87–91. The audit also reads fog block 225 from the running game. It does not activate D3's exit door or prove that every objective event has executed.

## Remaining log messages

The logs are **not error-free**. Completing generation is recorded separately from the number of messages marked Error. Remaining messages were reviewed as follows:

- **Navigation/grid diagnostics:** a control build of the standard **R7C2 Sublimation**, using the same dependency stack but no Anarchy data, also reached `InLevel` and produced **3,391** Error/Exception lines. Its desert dimension emitted **1,536** `Dimension_1 is borken` messages and **1,536** matching missing-grid-cell messages, the same pair counts as Anarchy D2's corresponding dimension. This reproduces the main D2 diagnostic volume without Anarchy. Smaller broken-cell diagnostics also remain elsewhere; a build check does not prove enemy navigation at every point.
- **Allocation fallbacks:** the engine creates additional resource containers or pickups when preallocated slots are exhausted. The standard control build also uses these recovery paths. Some remain in C1, C2 and D2. No claim is made that every random resource placement has been playtested.
- **C1 resource selection:** its custom pools fall back to their generic `ALL` entries. Generation completes with the authored pools retained.
- **C1 entrance wave:** its existing wave setting uses `FromElevatorDirection`. An unattended player remaining at the elevator can trigger the engine's player-node spawn fallback. In an additional controlled test, the first security door was opened by the test helper and the player moved to zone 63; no further spawn-node fallback was logged during the following 70 seconds. The original wave settings are retained. This observation does not constitute a normal playthrough of the entrance scan.
- **Static dimensions:** zones without a normal source gate log diagnostics, including in the standard control build.
- **Dependencies/props:** D1 uses the normal implementation for a T-scan without a custom override. The EOS terminal hook logs an early reactor-terminal registration warning. C2 can log a decorative lab display text-generation exception. D3's reactor geomorph reports no reactor chained puzzle; the expedition's objective is configured separately. These did not prevent generation.
- **Startup:** dependency/IL2CPP, font/material and test-console diagnostics remain. The previous MTFO exceptions caused by the three empty Anarchy files are removed. CConsole diagnostics concern the test environment and CConsole is not shipped.

Raw logs contain local account/session information and are retained locally, rather than included in the release.

## Validation limits

This is an actual level-generation and scan-placement check, **not a complete mission clear**. Reactor sequences, terminal commands, final extraction, every dynamic fog/sensor event, checkpoints, joining in progress and host/client synchronization were not exhaustively exercised. The tests establish that the five public stages generate and the fixed scan positions resolve on this game/dependency combination; they do not certify that the mod has no gameplay bugs.

The tested runtime dependency versions are recorded in the [runtime dependency list](https://github.com/kuma0128/GTFO-Anarchy/blob/anarchy-1.1.6/tools/runtime-dependencies.json). The release manifest lists direct requirements; future automatic dependency updates may change results.
