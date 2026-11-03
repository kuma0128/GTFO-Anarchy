# Rundown Anarchy

- Hello there! Anarchy is a vanilla rundown featuring diverse dimensions that demand excellent teamwork and gaming skills. This mod is highly challenging, with a focus on close-quarters combat and aggressive gameplay. This design reflects my preference for chaotic battles that require strategic thinking. True to its name, you'll face chaos and challenges that require careful strategy. I've crafted unique and difficult levels inspired by my experiences with Rundowns 6 and 7. The difficulty has been adjusted to exceed R8.

- For the complete Anarchy experience, I recommend playing without using boosters, glitches, cheats, or other mods.

- All levels are possible to clear. My team has completed them without using boosters. There are no secret strategies known only to the developers, so give it your all!

Thank you for playing! Enjoy the fight!

Special thanks to my team (Scotch, Jonino, Cross) and friends.

## NOTE

- If the game fails to start or an error occurs, please try recreating your r2modman profile.
- I do not recommend installing any mods that are not included in Anarchy, as doing so may cause unexpected errors.

## VERSION

```1.1.7```

- Updated for CheeseGeos 0.6.2 and GTFO-API 0.6.1 (BepInExPack_GTFO 3.2.4). CheeseGeos changed its asset paths in 0.6.0, so D3 needs this update.
- Updated AdvancedWardenObjective 2.6.4, EnemyAnimationFix 1.5.0, MemoryLeakFix 1.3.10, SnatcherBugFix 0.6.0, FlowGeos 0.9.5, OptionalNetworkAPI 1.3.3 and ExtraObjectiveSetup 1.6.15.
- Removed DoorEnemyFix, which has been deprecated on Thunderstore, and WardenIntelOhBehave, whose fix is now part of MemoryLeakFix.
- Fixed sub-objective messages that were not shown in C2, D1 and D3, the zone named by C2's OVERRIDE_LOCKDOWN command, C1's exit wave settings, and data entries that pointed at missing blocks.
- Please recreate your r2modman profile after updating.
- C1, C2, D1, D2 and D3 were built in game with this dependency combination, and all 19 fixed scan positions passed. All five were played through to extraction (with invulnerability), including the C1 reactor startup. Warden events, all 19 security sensors, snatcher capture/release, warden intel on repeated drops and the return to the lobby after a team wipe were also checked. Multiplayer was not tested.

```1.1.6```

- Fixed D3's exit fog reference and restored its scan position overrides.
- Fixed empty data overrides that caused exceptions in current MTFO.
- Corrected resource allocation, zone coverage, dimension lighting, and scan settings found during in-game build tests.
- Updated dependencies to the versions used for validation.
- C1, C2, D1, D2, and D3 were built in GTFO revision 34873. All 19 fixed scan positions were checked against the generated navigation mesh. Full mission clears and multiplayer synchronization were not retested.
- Remaining engine/dependency diagnostics and the test scope are documented in the [validation report](https://github.com/kuma0128/GTFO-Anarchy/blob/anarchy-1.1.6/VALIDATION.md).

```1.1.5```

- Added some plugins
  - StuckEnemyFix
  - DoorPanelFix
  - ScoutScreamFix
  - BioScannerFix
  - GTFOSilentShotFix
  - ShooterBugFix
  - DoorEnemyFix

```1.1.4```

- D1
  - Nerfed final error alarm

## Rundown Trailer

- Short Trailer 1: <https://youtu.be/xyYnDYv4LIw>
- Short Trailer 2: <https://youtu.be/03U_8CCKJdQ>

## WEAPON

Most weapons are based on R8. Here are the modifications:

- Sniper Rifle
  - Increased ammo capacity(3/12)
- Revolver
  - Increased magazine size(7)
- Precision Rifle
  - Decreased magazine size(9)
- High Caliber Pistol
  - Increased magazine size(7)
- Scatter Gun
  - Removed
- Pistol
  - Added penetration 1
- Assult Rifle
  - Added penetration 1
- HEL Shotgun
  - Decreased magazine size(11)
- Double Tap Rifle
  - Decreased magazine size(12)
- HEL Rifle
  - Changed penetration into 2
- HEL Gun
  - Changed penetration into 2
  
- New weapon
  - Added Thermal Rifle
  - Added Thermal Heavy Assult Rifle
  - Added Thermal Assult Rifle

## Collaborator

Special thanks ^ ^

Old D1 geos assistant: Mr.bro  
D3 LevelLayout assistant: Allets  
Icon: Van Gogh

## Contact

If you have suggestions, feedback, or bug reports, feel free to contact me via:

- Anarchy Feedback Channel (GTFO Modding Server)
- Discord: waiwaichan#9004 (please note: not "waiwichan")
