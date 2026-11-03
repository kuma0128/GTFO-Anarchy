# CHANGELOG

```1.1.7```

- Update CheeseGeos 0.5.8 -> 0.6.2. Since 0.6.0 its prefabs are in per-tile folders for GTFO-API's AssetAPI V2; update the D3 complex resource set and zone geomorphs (Refinery_X_RLC_01, Storage_I_RLC_01) and the unreleased E1 resource set to the new paths.
- Update AdvancedWardenObjective 2.5.7 -> 2.6.4 (GTFO-API 0.6.1 / AmorLib 1.3.0). This pulls in BepInExPack_GTFO 3.2.4, which now installs GTFO-API as a separate package.
- Update EnemyAnimationFix 1.4.8 -> 1.5.0 and FlowGeos 0.9.4 -> 0.9.5. Require MemoryLeakFix 1.3.10, SnatcherBugFix 0.6.0, OptionalNetworkAPI 1.3.3 and ExtraObjectiveSetup 1.6.15 directly, so that the latest versions are installed instead of the older ones other packages ask for.
- Remove DoorEnemyFix 1.0.0 (deprecated on Thunderstore) and WardenIntelOhBehave 0.0.1 (MemoryLeakFix 1.3.10 now clears the leftover warden intel queue between expeditions in the same way). Direct dependencies now total 35.
- Show the sub-objective messages that never appeared in C2, D1 and D3: their text was written inline, and the game only syncs TextDataBlock IDs. The text is now in TextDataBlocks 1580-1584.
- C1: give the exit wave valid wave settings (ID 111111 did not fit in 16 bits, so the wave had none), point the win-condition StopEnemyWaves event at reality instead of a missing dimension, and add Refinery entries to the resource set (no more fallback to ALL; same layout).
- C2: OVERRIDE_LOCKDOWN now names ZONE 106, the zone it opens, in its description and intel (it said ZONE 107), and its scan uses the zone's bioscan points instead of a missing world object.
- Remove unused chained puzzles (256, 275-277), dimensions (2, 9-12) and an enemy population entry (enemy 70) that referenced missing data, and clear the missing third layout of the unreleased E1.
- Validated in game (GTFO revision 34873): C1, C2, D1, D2 and D3 reach InLevel and all 19 fixed scan positions pass with the new dependencies. All five were cleared through their own doors, terminal commands, objectives (including the C1 reactor startup and its verification codes) and exit scans, with delayed events firing within 0.3 s of their delays. Warden events, all 19 security sensors, snatcher capture/release, warden intel on repeated drops and the return to the lobby after a team wipe were also checked; see VALIDATION.md. Multiplayer was not tested.

```1.1.6```

- D3: assign unique fog ID 225 to the exit transition; restore the missing scan override file with the correct main layout key; select Refinery for the exit zone.
- Data loading: remove three empty GameData overrides so MTFO can load the standard data before PartialData applies Anarchy's definitions.
- C1: correct zone subcomplexes and reserve the objective pickup placement in zone 68.
- C2: enable pickup and resource allocation in zone 105; correct the coverage bounds and reserve the large pickup in zone 104.
- D1: reserve generator cell placements; restore valid lighting and disable unused terminals in its desert dimension.
- D2: remove the invalid six-way concurrent scan flag and align the collapsed zone's minimum coverage with its generated area.
- D3: correct a reversed coverage range.
- Update dependency requirements and add a CLI data validator plus a runtime validation report.
- Validation covers level generation and fixed scan placement. It does not certify complete mission progression or multiplayer synchronization.

```1.1.3```

- Deleted MyCoolPlugin3
- D1
  - Balanced the resources
    - The difficulty level remains unchanged.

```1.1.1```

- Note: Please recreate your r2modman profile to display the survival time.
- D3
  - Deleted some events whish is unused
  - Changed resourse positions
    - The difficulty level remains unchanged.
- Deleted unused plugins
- Shortened the level build time

```1.1.0```

- D3
  - Release !!
- Improved Chargeup for HEL Gun

```1.0.2```

- New weapon
  - Thermal Rifle
  - Added Thermal Heavy Assult Rifle
  - Added Thermal Assult Rifle
- Added useful plugins
- C2
  - Changed resourse positions
    - The difficulty level remains unchanged.
- D2
  - Fixed scan positions
    - The difficulty level remains unchanged.
  - Added respawn room to prevent glitches
  
```1.0.1```

- Added useful plugins

```1.0.0```

- Added the BetterDoorBulletCollision plugin
- Due to personal commitments, development will be paused, and the rundown will be released as-is.
If there is significant demand for D3, I will consider making it available in the future.

```0.4.27```

- Refactored codes

```0.4.26```

- AY-D1
  - Activated
  - Changed some geos
  - Nerfed some alarms
  - Deleted glitch positions

```0.4.25```

- AY-C1
  - Nerfed Infections
  - More Tool Resourse
  
```0.4.24```

- Added MAGNIFICENT status

```0.4.23```

- Fixed pouncer bugs
- Adjusted some weapons
- AY-C2
  - Adjusted enemy distributions
- AY-D1
  - Desabled this level cuz geo plugin is broken...
  - Please recreate a profile
  
```0.4.22```

- AY-D1
  - Reduced fog repeller
- AY-D2
  - Reduced fog repeller

```0.4.21```

- Anarchy's package rejected
  - Idk the reason..
  - The same content as 0.4.20

```0.4.20```

- AY-C1
  - More ammo packs
- AY-D1
  - Less tool packs
- AY-D2
  - Removed some terminals
  
```0.4.19```

- AY-C1
  - Changed a tile for spawn rooms
- AY-C2
  - Slightly nerfed uplinks
- AY-D1
  - Slightly harder errors
- Adjusted Revolver for balance

```0.4.18```

- AY-C2 Infiltration
  - Slightly harder uplinks
- Adjusted HEL Shotgun for balance..

```0.4.17```

- Implemented security sensors ^ ^
- Adjusted HEL Shotgun for balance

```0.4.16```

- AY-C2 Infiltration
  - Tweaked a dimension
- AY-D2 Meander
  - Slightly nerfed surge alarms

```0.4.15```

- Updated for R8
- AY-C2 is terminated due to a dimension break
- AY-D2 and AY-D1 swapped due to difficulty levels (Now, AY-D1 is called "Catwalk" and AY-D2 is called "Meander".)
- AY-D3, including R8 contents, is coming soon
