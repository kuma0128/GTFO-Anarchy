using System.Collections;
using System.Reflection;
using System.Text.Json;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using ChainedPuzzles;
using Enemies;
using GameData;
using HarmonyLib;
using LevelGeneration;
using Player;
using UnityEngine;
using UnityEngine.AI;

namespace AnarchyValidation;

// Checks that need game time: warden event effects, snatcher capture/release and warden intel display.
internal static partial class RuntimeChecks
{
    internal static int IntelCalls, CustomSubCalls, WaveTriggers;
    internal static string LastCustomSub = "";
    private static Coroutine? intelWatch;
    private const int CloseSecurityDoor = 10000; // AWO WEE_Type
    private const uint PouncerEnemyId = 46;

    internal static bool TryHandle(Driver driver, string request)
    {
        var words = request.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        IEnumerator? routine = words[0] switch
        {
            "events" when words.Length == 2 => RunEvents(driver, words[1]),
            "d1-flow" => D1Flow(driver),
            "terminal" when words.Length == 6 => TerminalCommand(driver, words),
            "clear" when words.Length == 2 => Clear(driver, words[1]),
            "sensor" when words.Length == 3 => Sensor(driver, words[1], int.Parse(words[2])),
            "snatcher" when words.Length == 2 && words[1] is "kill" or "timeout" => Snatcher(driver, words[1]),
            "intel-burst" when words.Length == 2 => IntelBurst(int.Parse(words[1])),
            _ => null,
        };
        if (words[0] == "start" && words.Length == 3)
        {
            driver.StartCoroutine(Guard(request, Redrop(driver, words[1], words[2])).WrapToIl2Cpp());
            return true;
        }
        if (words[0] == "intel-watch")
        {
            intelWatch ??= driver.StartCoroutine(IntelWatch().WrapToIl2Cpp());
            return true;
        }
        // Every loaded datablock's IDs and names, for checking references against what the game actually has.
        if (words[0] == "dump-blocks")
        {
            var dump = new SortedDictionary<string, List<object>>();
            foreach (var type in typeof(GameDataBlockBase<>).Assembly.GetTypes())
            {
                var baseType = type.BaseType;
                if (baseType == null || !baseType.IsGenericType || baseType.GetGenericTypeDefinition() != typeof(GameDataBlockBase<>)) continue;
                IEnumerable? blocks;
                try { blocks = baseType.GetMethod("GetAllBlocks", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null) as IEnumerable; }
                catch (Exception ex) { Driver.Record($"DUMP_BLOCKS_SKIP {type.Name} {ex.InnerException?.Message ?? ex.Message}"); continue; }
                if (blocks == null) continue;
                var rows = new List<object>();
                foreach (var block in blocks)
                {
                    // Layouts also list their zones, so zone references into vanilla dimension layouts can be checked.
                    var zones = (block as LevelLayoutDataBlock)?.Zones?.ToArray().Select(z => (int)z.LocalIndex).ToArray();
                    rows.Add(new { id = type.GetProperty("persistentID")?.GetValue(block), name = type.GetProperty("name")?.GetValue(block), zones });
                }
                dump[type.Name] = rows;
            }
            File.WriteAllText(Path.Combine(Driver.LogRoot, "blocks.json"), JsonSerializer.Serialize(dump));
            Driver.Record("DUMP_BLOCKS " + dump.Count + " types, " + dump.Values.Sum(v => v.Count) + " blocks");
            return true;
        }
        // The generated tiles and plugs, to show a data change leaves the level layout unchanged.
        if (words[0] == "dump-level" && words.Length == 2)
        {
            var tiles = Builder.CurrentFloor.allZones.ToArray().SelectMany(z => z.m_areas.ToArray().Select(a =>
                $"{z.DimensionIndex}/{z.Layer.m_type}/{z.LocalIndex} {a.m_geomorph?.gameObject.name} {Vector3Int.RoundToInt(a.transform.position)}"));
            var plugs = UnityEngine.Object.FindObjectsOfType<LG_Plug>().Select(p => $"{p.gameObject.name} {Vector3Int.RoundToInt(p.transform.position)}").OrderBy(x => x);
            File.WriteAllLines(Path.Combine(Driver.LogRoot, words[1] + "-level.txt"), tiles.OrderBy(x => x).Concat(plugs));
            Driver.Record("DUMP_LEVEL " + words[1]);
            return true;
        }
        if (words[0] == "shot" && words.Length == 2)
        {
            var path = Path.Combine(Driver.LogRoot, words[1] + ".png");
            ScreenCapture.CaptureScreenshot(path);
            Driver.Record("SHOT " + path);
            return true;
        }
        if (routine == null) return false;
        driver.StartCoroutine(Guard(request, routine).WrapToIl2Cpp());
        return true;
    }

    // Run nested steps in place and report an exception instead of letting the coroutine die silently.
    private static IEnumerator Guard(string name, IEnumerator routine)
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(routine);
        while (stack.Count > 0)
        {
            object? current;
            try
            {
                if (!stack.Peek().MoveNext()) { stack.Pop(); continue; }
                current = stack.Peek().Current;
            }
            catch (Exception ex) { Driver.Record("FAIL " + name + " " + ex); yield break; }
            if (current is IEnumerator nested) { stack.Push(nested); continue; }
            yield return current;
        }
    }

    private static WaitForSecondsRealtime Wait(float seconds) => new(seconds);
    private static PlayerAgent LocalPlayer => PlayerManager.GetLocalPlayerAgent();
    private static string Json(object value) => JsonSerializer.Serialize(value);
    private static string Text(Localization.LocalizedText? text) => text?.ToString()?.Trim() ?? "";
    private static string Plain(string text) => System.Text.RegularExpressions.Regex.Replace(text, "<[^>]*>", "").Trim();

    // Run the event now through the normal executor. A non-zero currentDuration is treated as a checkpoint
    // resume and skips the event, so the delay is cleared until the caller has waited for the event to run.
    private static IEnumerator ExecuteNow(WardenObjectiveEventData e, float settle)
    {
        float delay = e.Delay;
        e.Delay = 0;
        try { WardenObjectiveManager.CheckAndExecuteEventsOnTrigger(e, eWardenObjectiveEventTrigger.None, true, 0); }
        catch { e.Delay = delay; throw; }
        yield return Wait(settle);
        e.Delay = delay;
    }

    // Every warden event list reachable from the built level: zone, terminal and objective events.
    private static List<(string Source, WardenObjectiveEventData Event)> LevelEvents()
    {
        var found = new List<(string, WardenObjectiveEventData)>();
        void AddLists(object owner, string prefix)
        {
            foreach (var property in owner.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.Name.EndsWith("k__BackingField")) continue;
                if (property.PropertyType == typeof(Il2CppSystem.Collections.Generic.List<WardenObjectiveEventData>))
                {
                    var list = (Il2CppSystem.Collections.Generic.List<WardenObjectiveEventData>?)property.GetValue(owner);
                    if (list == null) continue;
                    for (int i = 0; i < list.Count; i++) found.Add(($"{prefix} {property.Name}[{i}]", list[i]));
                }
            }
        }
        foreach (var zone in Builder.CurrentFloor.allZones)
        {
            var data = zone.m_settings?.m_zoneData;
            if (data == null) continue;
            var prefix = $"{zone.DimensionIndex}/{zone.Layer.m_type}/{zone.LocalIndex}";
            AddLists(data, prefix);
            if (data.TerminalPlacements == null) continue;
            for (int t = 0; t < data.TerminalPlacements.Count; t++)
            {
                var commands = data.TerminalPlacements[t].UniqueCommands;
                if (commands == null) continue;
                for (int c = 0; c < commands.Count; c++) AddLists(commands[c], $"{prefix} terminal{t} {commands[c].Command}");
            }
        }
        foreach (var layer in new[] { LG_LayerType.MainLayer, LG_LayerType.SecondaryLayer, LG_LayerType.ThirdLayer })
        {
            for (int chain = 0; WardenObjectiveManager.TryGetWardenObjectiveDataForLayer(layer, chain, out var block); chain++)
            {
                var prefix = $"objective {layer}#{chain} {block.name}";
                AddLists(block, prefix);
                if (block.ReactorWaves == null) continue;
                for (int w = 0; w < block.ReactorWaves.Count; w++) AddLists(block.ReactorWaves[w], $"{prefix} ReactorWaves[{w}]");
            }
        }
        return found;
    }

    private static LG_Zone? Zone(eDimensionIndex dimension, LG_LayerType layer, eLocalZoneIndex index)
        => Builder.CurrentFloor.TryGetZoneByLocalIndex(dimension, layer, index, out var zone) ? zone : null;

    private static LG_SecurityDoor? Door(eDimensionIndex dimension, LG_LayerType layer, eLocalZoneIndex index)
        => Zone(dimension, layer, index)?.m_sourceGate?.SpawnedDoor?.TryCast<LG_SecurityDoor>();

    private static string DoorStatus(LG_SecurityDoor? door) => door == null ? "none" : door.m_sync.GetCurrentSyncState().status.ToString();

    private static bool LightsOn() => EnvironmentStateManager.Current.m_stateReplicator.State.GetLightState(eDimensionIndex.Reality, LG_LayerType.MainLayer, eLocalZoneIndex.Zone_0);

    private static int SurvivalWaves() => WaveIds().Length;

    private static long[] WaveIds()
    {
        var ids = new List<long>();
        foreach (var e in Mastermind.Current.m_events) if (e.TryCast<SurvivalWave>() != null) ids.Add(e.Pointer.ToInt64());
        return ids.ToArray();
    }

    // Objective lines currently on the HUD, without rich-text tags.
    private static string HudObjectives()
    {
        var gui = GuiManager.PlayerLayer?.WardenObjectives;
        if (gui == null) return "";
        var lines = new List<string>();
        foreach (var p in gui.m_progressionObjectives) if (p?.m_text != null) lines.Add(Plain(p.m_text.text));
        return string.Join(" | ", lines);
    }

    private static int ActivePuzzles()
    {
        int count = 0;
        foreach (var p in ChainedPuzzleManager.Current.m_instances) if (p != null && p.IsActive) count++;
        return count;
    }

    private sealed record Snapshot(string Door, bool Lights, uint Fog, eDimensionIndex PlayerDimension, long[] Waves, int WaveTriggers, int Intel, int CustomSub, string Hud, int Puzzles, int ActivePuzzles, eWardenObjectiveStatus Objective, int Chain);

    private static Snapshot Capture(WardenObjectiveEventData e) => new(
        DoorStatus(Door(e.DimensionIndex, e.Layer, e.LocalIndex)), LightsOn(), EnvironmentStateManager.GetCurrentFogID(e.DimensionIndex),
        LocalPlayer.DimensionIndex, WaveIds(), WaveTriggers, IntelCalls, CustomSubCalls, HudObjectives(), ChainedPuzzleManager.Current.m_instances.Count, ActivePuzzles(),
        WardenObjectiveManager.Current.m_stateReplicator.State.GetLayerStatus(e.Layer), WardenObjectiveManager.GetCurrentChainIndex(e.Layer));

    private static bool InTerminalCommand(string source) => source.Contains(" terminal");

    // What each event type used by Anarchy must change in the level.
    private static (bool Ok, string Detail) Evaluate(string source, WardenObjectiveEventData e, Snapshot before, Snapshot after)
    {
        var subObjective = Plain(WardenObjectiveManager.ReplaceFragmentsInString(e.Layer, WardenObjectiveManager.GetCurrentChainIndex(e.Layer), Text(e.CustomSubObjective), false));
        (bool, string) result = (int)e.Type switch
        {
            1 => (after.Door is "Open" or "Opening", $"door {before.Door} -> {after.Door}"),
            2 => (after.Door != "Closed_LockedWithNoKey" && after.Door != "none", $"door {before.Door} -> {after.Door}"),
            3 => (!after.Lights, $"lights {before.Lights} -> {after.Lights}"),
            4 => (after.Lights, $"lights {before.Lights} -> {after.Lights}"),
            6 => (after.Fog == e.FogSetting, $"fog {before.Fog} -> {after.Fog}, expected {e.FogSetting}"),
            7 or 8 => (after.PlayerDimension == e.DimensionIndex, $"player {before.PlayerDimension} -> {after.PlayerDimension}, expected {e.DimensionIndex}"),
            9 => e.EnemyWaveData.WaveSettings == 0 ? (after.WaveTriggers == before.WaveTriggers, "no wave settings") : (after.WaveTriggers > before.WaveTriggers, $"waves started, running {before.Waves.Length} -> {after.Waves.Length}"),
            // Other sources may start new waves meanwhile; the waves running before the event must be gone.
            10 => (!after.Waves.Intersect(before.Waves).Any(), $"waves running before {before.Waves.Length}, still running {after.Waves.Intersect(before.Waves).Count()}, new {after.Waves.Except(before.Waves).Count()}"),
            // The objective state syncs custom sub-objectives by TextDataBlock ID, so text written inline (ID 0) never reaches the HUD.
            11 when e.CustomSubObjective != null && e.CustomSubObjective.Id == 0 && subObjective.Length > 0 => (false, $"inline text with TextDataBlock ID 0 cannot be synced; HUD {JsonSerializer.Serialize(after.Hud)}"),
            // The HUD must show the event's own text; an update with empty text does not count.
            11 => (subObjective.Length == 0 ? after.CustomSub > before.CustomSub : Plain(LastCustomSub) == subObjective || after.Hud.Contains(subObjective),
                $"custom sub-objective updates {before.CustomSub} -> {after.CustomSub}, HUD {JsonSerializer.Serialize(after.Hud)}"),
            // Completing one objective of a chain moves the layer on to the next objective.
            12 => (after.Objective == eWardenObjectiveStatus.WardenObjectiveItemSolved || after.Chain > before.Chain, $"{e.Layer} {before.Objective} #{before.Chain} -> {after.Objective} #{after.Chain}"),
            // A terminal command's scan is created with the command and only that command can start it; see the terminal check.
            22 when InTerminalCommand(source) => (true, "terminal command scan: checked by running the command"),
            22 => (after.Puzzles > before.Puzzles || after.ActivePuzzles > before.ActivePuzzles, $"puzzles {before.Puzzles}/{before.ActivePuzzles} active -> {after.Puzzles}/{after.ActivePuzzles}"),
            CloseSecurityDoor => (after.Door == "Closed", $"door {before.Door} -> {after.Door}"),
            _ => (true, "no level state to compare"),
        };
        if (Text(e.WardenIntel).Length > 0 && after.Intel <= before.Intel) return (false, result.Item2 + "; warden intel was not sent to the HUD");
        return result;
    }

    private static IEnumerator WarpHome(Vector3 home)
    {
        var player = LocalPlayer;
        if (player.DimensionIndex == eDimensionIndex.Reality) yield break;
        player.RequestWarpToSync(eDimensionIndex.Reality, home, player.FPSCamera.CameraRayDir, PlayerAgent.WarpOptions.All);
        yield return Wait(3);
    }

    private static IEnumerator RunEvents(Driver driver, string stage)
    {
        var home = LocalPlayer.Position;
        var events = LevelEvents();
        var wave = events.FirstOrDefault(x => x.Event.Type == eWardenObjectiveEventType.SpawnEnemyWave && x.Event.EnemyWaveData.WaveSettings != 0).Event;
        Driver.Record($"EVENTS_BEGIN {stage} {events.Count} timescale={Time.timeScale}");
        int failed = 0;
        for (int i = 0; i < events.Count; i++)
        {
            var (source, e) = events[i];
            // EventBreak only marks where an objective's OnActivate list pauses; the objective steps over it instead of executing it.
            if (e.Type == eWardenObjectiveEventType.EventBreak)
            {
                Driver.Record("EVENT_RESULT " + Json(new { stage, index = i, source, type = (int)e.Type, name = e.Type.ToString(), ok = true, detail = "skipped: objective step marker", intel = "" }));
                continue;
            }
            // Give the checks something to act on: an open door to close and a running wave to stop.
            if ((int)e.Type == CloseSecurityDoor && Door(e.DimensionIndex, e.Layer, e.LocalIndex) is { } closing && DoorStatus(closing) != "Open")
            {
                closing.ForceOpenSecurityDoor();
                yield return Wait(4);
            }
            if (e.Type == eWardenObjectiveEventType.StopEnemyWaves && wave != null && SurvivalWaves() == 0)
            {
                yield return ExecuteNow(wave, wave.EnemyWaveData.SpawnDelay + 2);
            }
            var before = Capture(e);
            Driver.Record($"EVENT_BEGIN {i} type={(int)e.Type} {source}");
            // A wave starts SpawnDelay seconds after its event runs.
            yield return ExecuteNow(e, e.Type == eWardenObjectiveEventType.SpawnEnemyWave ? e.EnemyWaveData.SpawnDelay + 3 : (int)e.Type is 6 or 7 or 8 ? 5 : 3);
            var after = Capture(e);
            var (ok, detail) = Evaluate(source, e, before, after);
            if (!ok) failed++;
            Driver.Record("EVENT_RESULT " + Json(new { stage, index = i, source, type = (int)e.Type, name = (int)e.Type == CloseSecurityDoor ? "CloseSecurityDoor (AWO)" : e.Type.ToString(), ok, detail, intel = Text(e.WardenIntel) }));
            if (after.Waves.Length > 0) driver.RunConsole("StopAllWave");
            yield return WarpHome(home);
        }
        driver.RunConsole("KillEnemies Everything");
        Driver.Record($"EVENTS_DONE {stage} failed={failed}");
    }

    // D1 zone 7: opening its door closes ZONE 4 behind the team (AWO), and the terminal command reopens it.
    private static IEnumerator D1Flow(Driver driver)
    {
        var zone4 = Door(eDimensionIndex.Reality, LG_LayerType.MainLayer, eLocalZoneIndex.Zone_4);
        var zone7 = Door(eDimensionIndex.Reality, LG_LayerType.MainLayer, eLocalZoneIndex.Zone_7);
        var terminal = Zone(eDimensionIndex.Reality, LG_LayerType.MainLayer, eLocalZoneIndex.Zone_7)?.TerminalsSpawnedInZone.ToArray().FirstOrDefault();
        if (zone4 == null || zone7 == null || terminal == null) throw new InvalidOperationException("D1 zone 4/7 doors or the zone 7 terminal were not found");
        void Step(string step) => Driver.Record("D1_FLOW " + Json(new { step, zone4 = DoorStatus(zone4), zone7 = DoorStatus(zone7), traversable = zone4.Gate.IsTraversable, intel = IntelCalls, waves = SurvivalWaves(), terminal = terminal.CurrentStateName.ToString(), output = terminal.m_command.m_outputQueue.Count }));
        Step("start");
        zone4.ForceOpenSecurityDoor();
        yield return Wait(5);
        Step("zone4 opened");
        zone7.ForceOpenSecurityDoor();
        // The zone events fire once the door has finished opening.
        yield return Wait(12);
        Step("zone7 opened");
        yield return TypeCommand(driver, terminal, "REBOOT_SECURITY_OVERRIDE");
        // Its events run after 10.4 s of terminal output, and the wave stop has a further 5 s delay.
        for (int t = 5; t <= 25; t += 5)
        {
            yield return Wait(5);
            Step($"reboot command {t}s");
        }
        terminal.ExitFPSView();
    }

    // Walk up to a terminal, log in and type a command, as a player does. The terminal only prints
    // (and runs the command's events at the end of its output) while someone is using it.
    private static IEnumerator TypeCommand(Driver driver, LG_ComputerTerminal terminal, string command)
    {
        var player = LocalPlayer;
        var dimension = terminal.SpawnNode.m_dimension.DimensionIndex;
        if (player.DimensionIndex != dimension)
        {
            var near = NavMesh.SamplePosition(terminal.transform.position, out var hit, 3f, -1) ? hit.position : terminal.transform.position;
            player.RequestWarpToSync(dimension, near, player.FPSCamera.CameraRayDir, PlayerAgent.WarpOptions.All);
            yield return Wait(4);
        }
        driver.RunConsole("TP " + terminal.ItemKey);
        yield return Wait(2);
        terminal.OnInteract(player);
        yield return Wait(2);
        terminal.m_command.EvaluateInput(command);
    }

    // terminal <dimension> <layer> <zone> <command> <seconds>: run a unique command and follow what it starts.
    private static IEnumerator TerminalCommand(Driver driver, string[] words)
    {
        var home = LocalPlayer.Position;
        var command = words[4];
        var terminal = Zone((eDimensionIndex)int.Parse(words[1]), (LG_LayerType)int.Parse(words[2]), (eLocalZoneIndex)int.Parse(words[3]))?
            .TerminalsSpawnedInZone.ToArray().FirstOrDefault(t => t.m_command.TryGetCommand(command, out _, out _, out _));
        if (terminal == null) throw new InvalidOperationException($"No terminal with {command} in that zone");
        void Step(string step) => Driver.Record("TERMINAL " + Json(new { command, step, puzzles = ChainedPuzzleManager.Current.m_instances.Count, activePuzzles = ActivePuzzles(), waves = SurvivalWaves(), intel = IntelCalls, output = terminal.m_command.m_outputQueue.Count, player = LocalPlayer.DimensionIndex.ToString() }));
        Step("start");
        yield return TypeCommand(driver, terminal, command);
        Step("typed");
        for (int t = 5; t <= int.Parse(words[5]); t += 5)
        {
            yield return Wait(5);
            Step($"{t}s");
        }
        terminal.ExitFPSView();
        driver.RunConsole("StopAllWave");
        yield return WarpHome(home);
        Driver.Record("TERMINAL_DONE " + command);
    }

    // Walk into an ExtraObjectiveSetup security sensor and follow its events.
    private static IEnumerator Sensor(Driver driver, string stage, int index)
    {
        var file = Path.Combine(Driver.ModRoot, "Custom", "ExtraObjectiveSetup", "SecuritySensor", $"AY-{stage}.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var p = doc.RootElement.GetProperty("Definitions")[0].GetProperty("SensorGroup")[index].GetProperty("Position");
        var target = new Vector3(p.GetProperty("x").GetSingle(), p.GetProperty("y").GetSingle(), p.GetProperty("z").GetSingle());
        var player = LocalPlayer;
        var home = player.Position;
        void Step(string step) => Driver.Record("SENSOR " + Json(new { stage, index, step, lights = LightsOn(), waves = SurvivalWaves(), x = player.Position.x, y = player.Position.y, z = player.Position.z }));
        Step("start");
        player.TeleportTo(target);
        yield return Wait(3);
        Step("entered");
        player.TeleportTo(home);
        yield return Wait(2);
        driver.RunConsole("StopAllWave");
        for (int t = 5; t <= 30; t += 5)
        {
            yield return Wait(5);
            Step($"+{t}s");
        }
        driver.RunConsole("KillEnemies Everything");
        Driver.Record($"SENSOR_DONE {stage} {index}");
    }

    private static IEnumerator Snatcher(Driver driver, string mode)
    {
        var player = LocalPlayer;
        var home = player.Position;
        var forward = Vector3.ProjectOnPlane(player.FPSCamera.CameraRayDir, Vector3.up).normalized;
        var spawn = NavMesh.SamplePosition(home + forward * 7f, out var hit, 4f, -1) ? hit.position : home + forward * 3f;
        Driver.Record("SNATCHER_SPAWN " + Json(new { mode, enemy = GameDataBlockBase<EnemyDataBlock>.GetBlock(PouncerEnemyId)?.name, x = spawn.x, y = spawn.y, z = spawn.z }));
        EnemyAgent.SpawnEnemy(PouncerEnemyId, spawn, player.CourseNode, Agents.AgentMode.Agressive);
        yield return Wait(2);
        var pouncer = UnityEngine.Object.FindObjectsOfType<EnemyAgent>().Where(x => x.Alive && x.EnemyDataID == PouncerEnemyId).OrderBy(x => Vector3.Distance(x.Position, spawn)).FirstOrDefault();
        if (pouncer == null) throw new InvalidOperationException("Snatcher did not spawn");
        var behaviour = pouncer.AI.m_behaviour.TryCast<PouncerBehaviour>();
        var data = behaviour?.Data;
        if (data != null)
            Driver.Record("SNATCHER_DATA " + Json(new { consume = data.ConsumeDuration, heldStart = data.HeldStateData.HeldStartAnimationDuration, maxHeld = data.HeldStateData.MaxHeldDuration, spitOut = data.HeldStateData.SpitOutStateDuration }));
        float start = Time.realtimeSinceStartup, captured = -1, released = -1;
        string last = "";
        while (Time.realtimeSinceStartup - start < 150)
        {
            var arena = player.Dimension?.IsArenaDimension ?? false;
            var state = player.Locomotion.m_currentStateEnum;
            var sample = Json(new { ploc = state.ToString(), dimension = player.DimensionIndex.ToString(), arena, covered = player.FPSCamera.PouncerScreenFX.covered, alive = player.Alive, holding = behaviour?.CapturedPlayer != null, pouncer = behaviour?.m_currentStateName.ToString(), pouncerAlive = pouncer.Alive });
            if (sample != last) { Driver.Record($"SNATCHER_STATE t={Time.realtimeSinceStartup - start:F1} {sample}"); last = sample; }
            if (captured < 0 && (arena || state == PlayerLocomotion.PLOC_State.GrabbedByPouncer)) captured = Time.realtimeSinceStartup;
            if (mode == "kill" && captured > 0 && pouncer.Alive && Time.realtimeSinceStartup - captured > 4)
            {
                Driver.Record("SNATCHER_KILL");
                driver.RunConsole("KillEnemies Everything");
            }
            if (captured > 0 && released < 0 && !arena && state != PlayerLocomotion.PLOC_State.GrabbedByPouncer && !player.FPSCamera.PouncerScreenFX.covered) released = Time.realtimeSinceStartup;
            if (released > 0 && Time.realtimeSinceStartup - released > 4) break;
            yield return Wait(0.25f);
        }
        bool onNavMesh = NavMesh.SamplePosition(player.Position, out var floor, 1.5f, -1);
        var finalState = player.Locomotion.m_currentStateEnum;
        bool ok = captured > 0 && released > 0 && player.Alive && player.DimensionIndex == eDimensionIndex.Reality && !(player.Dimension?.IsArenaDimension ?? false)
            && finalState is not (PlayerLocomotion.PLOC_State.GrabbedByPouncer or PlayerLocomotion.PLOC_State.Downed) && !player.FPSCamera.PouncerScreenFX.covered && onNavMesh;
        Driver.Record("SNATCHER_RESULT " + Json(new { mode, ok, captured = captured > 0, heldSeconds = captured > 0 && released > 0 ? released - captured : -1, ploc = finalState.ToString(), dimension = player.DimensionIndex.ToString(), covered = player.FPSCamera.PouncerScreenFX.covered, alive = player.Alive, navMeshDistance = onNavMesh ? Vector3.Distance(floor.position, player.Position) : -1 }));
        driver.RunConsole("KillEnemies Everything");
        yield return Wait(2);
        yield return WarpHome(home);
    }

    // Drop again in the same game session after an abort. The abort lands in the lobby state without opening
    // the lobby menu, which starting a level needs; if asking for the menu is not enough, leave the lobby and
    // select the expedition again. The HUD, and so the warden intel queue, carries over either way.
    private static IEnumerator Redrop(Driver driver, string tier, string index)
    {
        void Log(string step)
        {
            var lobby = GameStateManager.CurrentState.TryCast<GS_Lobby>();
            Driver.Record("LOBBY " + Json(new { step, state = GameStateManager.CurrentStateName.ToString(), inMenu = lobby?.m_inLobbyMenu, enterMenu = lobby?.m_enterLobbyMenu, automaticBuild = lobby?.m_automaticBuildStarted, ready = GameStateManager.IsReady }));
        }
        bool TryStart()
        {
            GameStateManager.IsReady = true;
            var lobby = GameStateManager.CurrentState.TryCast<GS_Lobby>();
            if (lobby != null) lobby.m_automaticBuildStarted = false;
            bool started = GameStateManager.CurrentState.TryStartLevelTrigger();
            Driver.Record("START_RESULT " + started);
            return started;
        }
        Log("after abort");
        var current = GameStateManager.CurrentState.TryCast<GS_Lobby>();
        if (current != null) current.m_enterLobbyMenu = true;
        yield return Wait(3);
        Log("menu requested");
        if (TryStart()) yield break;
        // Leave the lobby as the menu does, then host a new one the way the first drop was set up.
        SNetwork.SNet.SessionHub.LeaveHub(false);
        for (int i = 0; i < 30 && GameStateManager.CurrentStateName != eGameStateName.NoLobby; i++) yield return Wait(0.5f);
        yield return Wait(3);
        Log("left lobby");
        driver.RunConsole($"LoadExpedition {tier} {index}");
        for (int i = 0; i < 20 && GameStateManager.CurrentStateName != eGameStateName.Lobby; i++) yield return Wait(0.5f);
        yield return Wait(4);
        Log("lobby again");
        TryStart();
    }

    private static IEnumerator IntelWatch()
    {
        string last = "";
        while (true)
        {
            var intel = GuiManager.PlayerLayer?.m_wardenIntel;
            if (intel != null)
            {
                var text = intel.m_intelText;
                var sample = Json(new { state = GameStateManager.CurrentStateName.ToString(), text = text?.text, shown = intel.gameObject.activeInHierarchy && text != null && text.gameObject.activeInHierarchy, alpha = text != null ? Math.Round(text.alpha, 2) : -1, running = intel.IsNewObjectiveSequenceRunning, queue = intel.m_subObjectiveMessageQueue?.Count ?? -1 });
                if (sample != last) { Driver.Record("INTEL_STATE " + sample); last = sample; }
            }
            yield return Wait(0.2f);
        }
    }

    // Queue several of the level's own warden intel messages back to back.
    private static IEnumerator IntelBurst(int count)
    {
        foreach (var (source, e) in LevelEvents().Where(x => x.Event.Type == eWardenObjectiveEventType.None && Text(x.Event.WardenIntel).Length > 0).Take(count))
        {
            Driver.Record("INTEL_BURST " + source);
            yield return ExecuteNow(e, 0.3f);
        }
        Driver.Record("INTEL_BURST_DONE");
    }
}

[HarmonyPatch(typeof(PUI_WardenIntel), nameof(PUI_WardenIntel.ShowSubObjectiveMessage))]
internal static class IntelHook
{
    private static void Postfix(string newSubObj)
    {
        RuntimeChecks.IntelCalls++;
        Driver.Record("INTEL_SENT " + JsonSerializer.Serialize(newSubObj));
    }
}

[HarmonyPatch(typeof(PUI_GameObjectives), nameof(PUI_GameObjectives.SetCustomSubText))]
internal static class CustomSubHook
{
    private static void Postfix(Il2CppSystem.Func<string> text)
    {
        RuntimeChecks.CustomSubCalls++;
        RuntimeChecks.LastCustomSub = text?.Invoke() ?? "";
        Driver.Record("CUSTOM_SUB " + JsonSerializer.Serialize(RuntimeChecks.LastCustomSub));
    }
}

[HarmonyPatch]
internal static class WaveHook
{
    private static IEnumerable<MethodBase> TargetMethods() => typeof(Mastermind).GetMethods().Where(m => m.Name == nameof(Mastermind.TriggerSurvivalWave));

    private static void Postfix(bool __result)
    {
        if (__result) RuntimeChecks.WaveTriggers++;
        Driver.Record("WAVE_TRIGGER " + __result);
    }
}
