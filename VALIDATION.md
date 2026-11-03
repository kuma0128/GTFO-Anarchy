# Anarchy validation

## 1.1.7 dependency update

The 1.1.7 release uses the 40 runtime packages in the [runtime dependency list](tools/runtime-dependencies.json) (35 direct requirements in the manifest). It was first validated on 1 November 2026 (JST) with GTFO revision **34873**. On 3 November the list was updated to the newest version of every package and validated again:

| Package | 1 November | Release |
| --- | --- | --- |
| GTFO-API | 0.6.0 | **0.6.1** (required by AdvancedWardenObjective 2.6.4) |
| CheeseGeos | 0.6.0 | **0.6.2** |
| AdvancedWardenObjective | 2.6.3 | **2.6.4** |
| OptionalNetworkAPI | 1.3.2 | **1.3.3** |
| FlowGeos | 0.9.4 | **0.9.5** |
| WardenIntelOhBehave | 0.0.1 | **removed** (MemoryLeakFix 1.3.10 does the same reset; see [Warden intel](#warden-intel-across-consecutive-expeditions)) |

The other packages were already current: BepInExPack_GTFO 3.2.4, AmorLib 1.3.0, EnemyAnimationFix 1.5.0, MemoryLeakFix 1.3.10, SnatcherBugFix 0.6.0, ExtraObjectiveSetup 1.6.15 and the rest of the list. OptionalNetworkAPI and ExtraObjectiveSetup are now listed in the manifest directly, because the packages that pull them in require older versions (EnemyAnimationFix 1.5.0 requires OptionalNetworkAPI 1.3.2, and EOSExt_SecuritySensor 1.1.7 requires ExtraObjectiveSetup 1.4.3). DoorEnemyFix was not installed.

### Build and scan audit

Each cell gives Error/Exception lines / Warning lines. Every run reached `InLevel` and passed the audit.

| Stage | Scan instances checked | 1 November | Release set (3 Nov, 18:46–18:51 JST) | Release (3 Nov, 22:59–23:04 JST) |
| --- | ---: | ---: | ---: | ---: |
| C1 | 0 | 23 / 39 | 15 / 39 | 18 / 39 |
| C2 | 4 | 259 / 101 | 259 / 101 | 260 / 101 |
| D1 | 1 | 69 / 357 | 69 / 357 | 69 / 357 |
| D2 | 9 | 3419 / 80 | 3419 / 80 | 3417 / 80 |
| D3 | 5 | 126 / 48 | 126 / 48 | 126 / 48 |

The release-set runs already had the sub-objective text and C1 wave-ID fixes of the [data fixes](#117-data-fixes). The last column is the release itself: the release set with every data fix (commit `c93d6fa`). **19/19** configured scan instances were registered at their configured positions in every run, and all sampled navigation-mesh points were within **0.0975 m**. Fog block 225 passed in all runs.

- **Error lines with the release set:** C2, D1, D2 and D3 have the same counts as on 1 November. C1 varies with its entrance wave and resource allocation (see [Remaining log messages](#remaining-log-messages)).
- **Error lines in the release, compared with the release set:** D1 and D3 log exactly the same lines. C1 no longer logs the eight `ComplexResourceSetDataBlock ... no geomorph/prefab found with subcomplex Refinery ... defaulting to ALL` messages, but logs six pairs of resource-container fallbacks (`TryGetZoneFunctionDistribution returning FALSE`, `Had to create a new resourceContainer`). These differ between sessions (0, 1, 4 and 5 pairs in four builds before the fix; 4, 6 and 6 pairs in three builds after it), so C1's total rose from 15 to 18 by chance. C2 logs one new message, `UseStaticBioscanPointsInZone was enabled but it failed to get custom bioscan spawn points ... Defaulting to random bioscans points`, from the `OVERRIDE_LOCKDOWN` scan fix described below. D2 logs two resource fallbacks fewer in reality and one more in its dimension, which also varies between sessions.
- **Startup messages:** `Assembly OptionalNetworkAPI.dll is not registered in il2cpp` from Il2CppInterop remains with OptionalNetworkAPI 1.3.3 (EnemyAnimationFix and all later plugins still load). With CheeseGeos 0.6.2 and GTFO-API 0.6.1, GTFO-API no longer reports `mining_reactor_01_gencluster_v1.prefab` as already registered.
- **CheeseGeos paths (0.6.0):** a control run of D3 with deliberately missing paths logged `Asset ... did not loaded!` and `tile prefab is null`, and generation never finished (timeout). The passing run therefore loaded the new paths. A second control run with the 1.1.6 paths also reached `InLevel`, because CheeseGeos 0.6.0 still ships its legacy `rlc_mining` bundle; that compatibility depends on the bundle remaining in future CheeseGeos versions. D3 builds and clears with 0.6.2.

## 1.1.7 data fixes

A reference audit (`node tools/game-validation/audit-refs.cjs <blocks.json>`) checks every datablock and zone reference in Anarchy's data against Anarchy's own blocks and a dump of the blocks loaded in the game (bridge command `dump-blocks`). It found the problems below, all of which were fixed. Only eight entries in the fully commented ExtraObjectiveSetup `Template.json` files remain; those files are not used by any expedition.

- **Sub-objectives that never appeared:** five `UpdateCustomSubObjective` events had their text written inline (C2 `EventsOnElevatorLand[0]` and `EventsOnActivate[16]`, D1 `EventsOnActivate[7]` and `[18]`, D3 chain objective `EventsOnActivate[0]`). The game syncs custom sub-objectives by TextDataBlock ID, and inline text has ID 0, so nothing was shown. The same English text is now in TextDataBlocks **1580–1584** and the events reference them. The clear runs showed them on the HUD, for example "Proceed to ZONE 12 and Escape." in D3.
- **C1 exit wave:** chain objective `EventsOnGotoWin[2]` used wave settings 111111. The game keeps only 16 bits (45575) and logged `SurvivalWave got no SurvivalWaveSettings with ID 45575`, so the exit wave had no settings. The settings block "Trickle 4-30 SSpB 2" now uses ID **142**, and the event references it.
- **C1 StopEnemyWaves:** objective `EventsOnGotoWin[0]` targeted dimension 1, which C1 does not have. It now uses reality (0). C1 no longer logs `LG_Floor: Tried to get dimension with an index (Dimension_1)` (see [Warden events](#warden-events-advancedwardenobjective-264)).
- **C1 resource set:** the C1 complex resource set had no Refinery entries for 1x1 geomorphs and plugs, so the game fell back to `ALL` eight times per build. The 19 Refinery entries are copies of the existing entries. A same-seed build produced the same zone tiles and plugs as before, and only the reactor terminal's serial number changed.
- **C2 `OVERRIDE_LOCKDOWN`:** the scan started by the command (`ActivateChainedPuzzle`) had `UseStaticBioscanPoints` off and no `WorldEventObjectFilter`, so it looked for a world object that does not exist. It now uses the zone's bioscan points, like D2's `COPY_GENOME_DB`. ZONE 105 has no static points, so the game places the scan at random points in the zone and logs the message above. In the clear run, the command started its scan and the run continued to extraction.
- **C2 `OVERRIDE_LOCKDOWN` text:** the command description and its intel named ZONE 107, but the command opens ZONE 106 (zone 7), where the uplink terminal is. ZONE 107 is unlocked later, when the uplink finishes. Both now name ZONE 106. The intel still appears 120 s after the command, as before.
- **Unused blocks with missing references:** chained puzzles 256, 275, 276 and 277 (waves 299, 66, 61 and 60 do not exist) and dimensions 2, 9, 10, 11 and 12 (layouts 157, 182, 193, 195 and 1398700845 do not exist) were removed. No expedition used them. The dimensions in use (14, 16, 17 and 20) are unchanged.
- **Enemy population:** `DefaultPop1` had a role entry for enemy 70, which does not exist. The entry was removed.
- **Unreleased E1:** its third layout pointed at layout 114, which does not exist. It now has no third layout (0).

`npm test --prefix tools` passes with these changes.

## 1.1.7 events, snatchers and warden intel

Checked on 3 November 2026 (JST) with the release (the 40 packages of the release set, AdvancedWardenObjective 2.6.4, and every data fix), GTFO revision **34873**, using `tools/game-validation/checks.cjs` and the checks added to the validation plugin (`RuntimeChecks.cs`). God mode was on and the game ran at normal speed (`TimeScale 1`). The first runs of these checks, earlier on 3 November, used the 1 November set (AdvancedWardenObjective 2.6.3) and predated the data fixes; their differences are noted below.

The final event run, the snatcher check and the failure check ran in a second copy of the test installation while the [mission clears](#117-mission-clears) ran in the first. Both copies use the same Steam account; see [running checks in parallel](#running-checks-in-parallel).

### Warden events (AdvancedWardenObjective 2.6.4)

AWO hooks the game's event coroutine. It takes over only its own event types (10000 and above) and four vanilla types when they use fields the game ignores. In C1–D3, Anarchy has one AWO event, `CloseSecurityDoor` in D1 ZONE 7 `EventsOnOpenDoor` (it closes the ZONE 4 door behind the team), and no fields that trigger AWO's vanilla overrides. Every other event passes through the hook to the game's own executor.

Every warden event of the built levels (zone and terminal command events, objective events including chained objectives and reactor waves) was run one at a time through `WardenObjectiveManager.CheckAndExecuteEventsOnTrigger`, and its effect was checked. Delays were cleared for the call; delay timing was measured separately in the [mission clears](#117-mission-clears).

| Stage | Events | EventBreak (not run) | Effect confirmed | Logged by AWO's hook | First run (1 Nov set, before the data fixes) |
| --- | ---: | ---: | ---: | ---: | --- |
| C1 | 12 | 0 | 12 | 12 | 12 / 12 |
| C2 | 25 | 1 | 24 | 24 | 22 / 24 |
| D1 | 49 | 3 | 46 | 46 | 46 / 46 |
| D2 | 34 | 0 | 34 | 34 | 34 / 34 |
| D3 | 23 | 0 | 23 | 23 | 22 / 23 |
| **Total** | **143** | **4** | **139 / 139** | **139 / 139** | 136 / 139 |

- **Types covered:** SpawnEnemyWave 35, OpenSecurityDoor 14, UnlockSecurityDoor 13, StopEnemyWaves 11, UpdateCustomSubObjective 9, AllLightsOff 7, SetFogSetting 5, DimensionWarpTeam 4, AllLightsOn 3, DimensionFlashTeam 3, ForceCompleteObjective 2, ActivateChainedPuzzle 2, CloseSecurityDoor (AWO) 1, and 30 events that only show warden intel. Every event with warden intel sent it to the HUD.
- **Sub-objectives in TextDataBlocks 1580–1584:** the three failures of the first run were inline-text sub-objectives, now moved to TextDataBlocks (see [data fixes](#117-data-fixes)). All five events now pass their text to the HUD's objective panel (`PUI_GameObjectives.SetCustomSubText`): "Proceed to Zone 106 and locate uplink terminal." and "Proceed immediately to Zone 109 and escape from here." in C2, "Find the Data Cube in ZONE 436" and "… ZONE 441" in D1, and "Proceed to ZONE 12 and Escape." in D3. The mission clears showed the same texts. In D1 and D3 the zone name is wrapped in the orange colour tag twice (once in the text, once by the game's zone-name replacement), which does not change how it looks.
- **Security sensors:** the check walked into all **19** ExtraObjectiveSetup security sensors (C2 4, D1 3, D2 10, D3 2). In every one, entering turned the lights off and started one more enemy wave. The check turns the lights back on before each sensor, because an earlier sensor can leave them off: D2's sensors never turn them on again, and C2's do so only after 120 s. The first attempt, without that step, entered 12 of the 19 sensors with the lights already off. D1's and D3's sensors turned the lights back on after about 20 s.
- **D1 played through its own triggers:** opening the ZONE 7 door showed "Reboot the alarm system…" and AWO closed the ZONE 4 door (Closed, not traversable). Typing `REBOOT_SECURITY_OVERRIDE` at the ZONE 7 terminal reopened it, and it was traversable again 15 s after the command.
- **Terminal command scans:** both ActivateChainedPuzzle events belong to terminal commands (C2 `OVERRIDE_LOCKDOWN`, D2 dimension 1 `COPY_GENOME_DB`). Typing each command at its terminal started its scan and a wave within 5 s.
- **C1 `Dimension_1`:** the first run logged `LG_Floor: Tried to get dimension with an index (Dimension_1) that was out of bounds` twice in C1, each time right after a door opened and before any event that referred to dimension 1 had run. None of the final C1 runs logged it: two event runs (one in each copy), the build and the mission clear. The only reference to dimension 1 in C1's data was the objective's StopEnemyWaves event, now fixed (see [data fixes](#117-data-fixes)), so the game evidently looked up that event's dimension when a door opened. The same message in the C2, D1 and D2 builds, whose counts did not change, is unrelated.
- **Logs:** the error lines during the run are the game's own messages (their text is in the game's metadata, not in any plugin): `OnDoorIsOpened: LinkedToZoneData`, `OpenSecurityDoorWithActiveEnemyWave`, spawn-node fallbacks, and `Implement CaptureToBuffer on Enemies.ES_HibernateWakeUp` when a sensor's wave woke sleeping enemies. C1 logged 3 lines instead of 7: the `Dimension_1` lookups and `SurvivalWave got no SurvivalWaveSettings with ID 45575` (the exit wave, fixed) are gone. One run also logged a Steam lobby reconnection (`RelocateLobby`), and D3 logged MTFO blocking an achievement. No exception came from AWO.
- **AWO 2.6.4** (released 3 November 2026, requires GTFO-API 0.6.1) fixes delayed AWO-handled events that fire before `InLevel`, such as events on elevator land. Anarchy's elevator-land events are vanilla types that AWO passes through; with 2.6.3 and 2.6.4 they ran on landing in C2 and D1.
- **Validation tool fix:** the first attempt of the final run stopped in C2, because the check compared a `LocalizedText` with `!= null`, which throws in IL2CPP. It now uses a pattern match.

### Snatcher capture and release (SnatcherBugFix 0.6.0)

In D3, a snatcher (enemy 46) was spawned in front of the idle player, once for each way out.

| Release | Capture | Held | Back in reality, screen cover cleared | End state |
| --- | --- | ---: | --- | --- |
| Killed while held | Moved to the arena dimension (Dimension_20) | 6.5 s | Yes | Stand, alive, 0.02 m from the navigation mesh |
| Spat out | Same | 7.9 s | Yes | Stand, alive, 0.09 m from the navigation mesh |

The first run, with the 1 November set and only one game running, released the player 0.2 s after the kill (killed 4 s after capture) and 5.6 s after capture when spat out, which matches the pouncer data (consume 0.23 s, held start 1 s, maximum hold 4 s, spit-out 1.4 s). The longer holds in the final run were measured while a second game was running on the same PC. While a player is held, Unity logs `ArgumentNullException: Value cannot be null. Parameter name: ps` (109 lines in the final run, 49–63 lines in earlier runs, roughly in proportion to the hold time). The same happens with SnatcherBugFix 0.5.0, with no SnatcherBugFix, and with MemoryLeakFix 1.3.9 plus EnemyAnimationFix 1.4.8, so it does not come from the 1.1.7 updates. A stack trace points to the game's own `CellSoundEmitter.OnParticleCollision`, called from `FPSCamera.LateUpdate`. `UpdateNavPositions : m_navNode == null!` (2–4 lines when the snatcher spawns) also appears in every setup. Capture and release behaved the same in all setups.

### Warden intel across consecutive expeditions

One GTFO process dropped into D1 three times. In each drop, three of D1's warden intel messages were shown back to back 25 s after landing, and the expedition was aborted while two of them were still queued. The game cannot start a level from the lobby an abort returns to, so the run left the lobby and hosted a new one before the next drop. The HUD was sampled every 0.2 s.

| Setup | Drop 1 | Drop 2 | Drop 3 | Queue after each abort |
| --- | --- | --- | --- | --- |
| 1 November set with WardenIntelOhBehave, MemoryLeakFix 1.3.10 | Shown | Shown | Shown | 2 → 0 |
| 1 November set without WardenIntelOhBehave, MemoryLeakFix 1.3.10 | Shown | Shown | Shown | 2 → 0 |
| Control: without WardenIntelOhBehave, MemoryLeakFix 1.3.9 | Shown | **Nothing shown** | **Nothing shown** | 2 → 2, 9 → 2, 16 → 9 |
| **Release** (release set and every data fix, no WardenIntelOhBehave, MemoryLeakFix 1.3.10) | Shown | Shown | Shown | 2 → 0 |

The release run was the last check, run on its own after the mission clears. "Shown" means the four landing messages (the game's objective intro and Anarchy's "Find the Data Cube in ZONE 147") and the first queued message reached the HUD. The control run reproduces the bug: the queue is left over and nothing appears on the next drops. MemoryLeakFix 1.3.10 alone clears the queue at level cleanup (`ResetSubObjectiveMesssagQueue`), as WardenIntelOhBehave does, so WardenIntelOhBehave was removed from 1.1.7. Anarchy uses no other feature of it.

## 1.1.7 mission clears

Checked on 3 November 2026 (JST) with the release (the release set and every data fix), using `node tools/game-validation/checks.cjs <test-root> clear <stages>`. God mode was on, and the game ran at normal speed. The `clear` check plays the level through its own triggers:

- It opens each reachable security door the way the door asks for: approach, solve its scan, or unlock it (key, cell and generator locks are unlocked directly as a shortcut), then moves the player inside and kills reachable enemies.
- It types every unique terminal command once at its terminal.
- It runs the objectives layer by layer. Special terminal commands are typed. A reactor is started with `REACTOR_STARTUP`; the scan that this starts at the reactor (the objective's `ChainedPuzzleToActive`) is solved, and each verification code is typed at the reactor terminal with `REACTOR_VERIFY`. Other objective types run their `EventsOnActivate` steps through the game's executor, which stops at each EventBreak as the game does, and are then completed with CConsole.
- Finally it solves the exit scan and waits for `ExpeditionSuccess`.

Every delayed event was timed between the game's own `BeginDelay` and `EndDelay` log lines.

| Stage | Result | Doors opened | Terminal commands | Objective layers | Delayed events timed | Largest timing error | Longest delay |
| --- | --- | ---: | --- | --- | ---: | ---: | ---: |
| C1 | ExpeditionSuccess | 6 | — | Main (reactor startup, 6 waves and 6 verification codes), then its chain (carry the MATTER_WAVE_PROJECTOR) | 12 | 0.02 s | 90 s |
| C2 | ExpeditionSuccess | 9 | `OVERRIDE_LOCKDOWN` | Main (uplink, 2 steps) | 26 | 0.30 s | 120 s |
| D1 | ExpeditionSuccess | 10 | `REBOOT_SECURITY_OVERRIDE`, `DESABLE_FIREWALL` | Main (3 Data Cubes, 3 steps) | 37 | 0.03 s | 168 s |
| D2 | ExpeditionSuccess | 15 | `COPY_GENOME_DB`, `Maintenance_Unlock_Alpha` | Third (uplink), Main (HSU) | 35 | 0.02 s | 120 s |
| D3 | ExpeditionSuccess | 16 | — | Secondary (generator cluster), Third (`Init_ventilation_sequence`), Main (`EMERGENCY_ESCAPE_PROTOCOL`, then its survival chain) | 25 | 0.01 s | 180 s |

- **All five stages** reached `ExpeditionSuccess`; **135** delayed events fired within **0.30 s** of their delays (0.30 s in C2, as in the earlier C2 clear; 0.03 s or less elsewhere). One D1 delayed event was still pending when the expedition ended. At the end of D1, the doors of zones 10–13 were still `Closed_LockedWithNoKey`, because the route to the exit does not need them.
- **C1 is cleared for the first time.** Its reactor needs the startup scan solved before it warms up: `REACTOR_STARTUP` printed "Initiating Reactor STARTUP Sequence" and started scan 10, and the reactor stayed `Inactive_Idle` until the scan was solved. A run without that step waited 28 minutes at `Inactive_Idle`; the game's own reactor started normally when its startup interaction was triggered, so this was a gap in the check, not in Anarchy. After the scan, the reactor went through 6 waves (`Startup_intro`, `Startup_intense`, `Startup_waitForVerify`), accepted each code and reached `Startup_complete`, and "REACTOR TEST SEQUENCE COMPLETED" was shown. The exit wave now uses settings 142, and C1 no longer logs `SurvivalWave got no SurvivalWaveSettings`.
- Every command's intel reached the HUD except `Maintenance_Unlock_Alpha`, which has none. The sub-objective texts of TextDataBlocks 1580–1584 were shown as in the [event check](#warden-events-advancedwardenobjective-264).
- **Earlier clears:** C2, D1, D2 and D3 had been cleared earlier on 3 November with the release set and most of the data fixes (largest timing error 0.30 s over 124 delayed events). The C1 clear had stopped after its first door because of a validation-tool fault, then at the reactor startup scan described above.

Error lines in the clears are the game's own messages. Compared with the earlier clears:

- **C1:** reactor state changes (`LG_WardenObjective_Reactor.OnStateChange`, `Reactor IDLE START`, `REACTOR_POWER_DOWN` and `TerminalSeqStarted`), which the game logs at error level, and `Tried to add enemyType: 333 into WaveID … not Linked to an ENEMY_TYPE` (9 lines). Enemy 333 is Anarchy's `shadow_rapid`, used only by C1's reactor waves (wave population 2929 `ShadowHybrid_C1`). The game has no audio switch for a custom enemy type and logs this when a wave with it spawns; the waves spawned and the reactor completed. It had not appeared before because no earlier run reached the reactor waves.
- **C2:** `MovingEnemyTentacleBase, Could not allocate GPUCurvy-tentacle, Global limit is met` (37 lines), the game's limit on tentacle effects with many enemies alive.
- **D1 and D3:** more `ArgumentNullException ... ps` (the game's `CellSoundEmitter`) and `Implement CaptureToBuffer on Enemies.ES_HibernateWakeUp`.
- **D2:** 101 pairs of `Dimension_N is borken` and `COULDN'T FIND CELL IN GRID` from its desert dimension, the same diagnostics that the standard R7C2 produces (see [Remaining log messages](#remaining-log-messages)).
- **C1 and C2:** PlayFab `A conflict occurred trying to make multiple edits to the same data at the same time` (3 and 1 times), from two games of the same account running at once (see [running checks in parallel](#running-checks-in-parallel)).

### Expedition failure

`node tools/game-validation/checks.cjs <test-root> fail C1` turns god mode off and kills the player. The expedition ended in `ExpeditionFail`. The failure screen waits for a player to press its return-to-lobby button; the check presses it (`CM_PageExpeditionFail.m_btnGoToLobby`) once it is shown, and the game went `ExpeditionFail` → `AfterLevel` → `Lobby` 2.2 s after the press. Anarchy has no checkpoints (no zone sets `IsCheckpointDoor`), so a wipe ends the expedition.

### Running checks in parallel

To save time, the final event run, the snatcher check and the failure check ran in a second copy of the test installation (a full copy of `runtime`, 15 GB) while the mission clears ran in the first. The game allows a second instance (`single-instance=` is empty in `boot.config`), and each copy has its own command file and logs. Both games stayed in their own lobbies, and the clears logged no lobby disconnection. Two side effects were seen: PlayFab rejected some simultaneous writes of the same account's data (the conflict lines above), and the snatcher was held longer than in a run with one game. Timing-sensitive checks (the mission clears) and checks that leave and recreate lobbies (warden intel) were not run alongside another game.

### Not covered

Multiplayer synchronization and joining in progress are outside the scope of these checks. The checks solve scans and unlock key, cell and generator locks directly instead of playing them, and they run with god mode on; they show that the levels' own triggers lead to extraction, not that the levels are balanced.

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
