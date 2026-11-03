using System.Collections;
using System.Text.Json;
using System.Text.RegularExpressions;
using BepInEx.Logging;
using ChainedPuzzles;
using GameData;
using LevelGeneration;
using Player;
using UnityEngine;
using UnityEngine.AI;

namespace AnarchyValidation;

// clear <stage>: play a stage through to the success screen. Doors, scans, terminal commands, special terminal
// commands, the reactor and the exit scan are used as a team uses them. Other objective mechanics (picking up
// items, uplink codes, power cells, the HSU, the survival timer) are replaced by running the objective's own
// OnActivate steps and completing it with CConsole.
internal static partial class RuntimeChecks
{
    private static readonly LG_LayerType[] LayerOrder = { LG_LayerType.SecondaryLayer, LG_LayerType.ThirdLayer, LG_LayerType.MainLayer };

    private static IEnumerator Clear(Driver driver, string stage)
    {
        EventTiming.Start();
        var ran = new HashSet<string>();
        Driver.Record("CLEAR_BEGIN " + stage);
        ReportObjectiveItems();
        yield return OpenReachable(driver, ran);
        foreach (var layer in LayerOrder)
        {
            for (int chain = 0; WardenObjectiveManager.TryGetWardenObjectiveDataForLayer(layer, chain, out var block); chain++)
            {
                for (int i = 0; i < 60 && WardenObjectiveManager.GetCurrentChainIndex(layer) < chain; i++) yield return Wait(0.5f);
                Driver.Record("OBJECTIVE_BEGIN " + Json(new { layer = layer.ToString(), chain, block = block.name, type = block.Type.ToString(), status = LayerStatus(layer).ToString() }));
                yield return CompleteObjective(driver, layer, chain, block);
                yield return WaitForEvents(240);
                Driver.Record("OBJECTIVE_DONE " + Json(new { layer = layer.ToString(), chain, status = LayerStatus(layer).ToString(), currentChain = WardenObjectiveManager.GetCurrentChainIndex(layer) }));
                yield return OpenReachable(driver, ran);
            }
        }
        yield return Exit(driver, stage);
        EventTiming.Stop();
    }

    private static eWardenObjectiveStatus LayerStatus(LG_LayerType layer) => WardenObjectiveManager.Current.m_stateReplicator.State.GetLayerStatus(layer);

    private static bool Solved(LG_LayerType layer, int chain) =>
        WardenObjectiveManager.GetCurrentChainIndex(layer) > chain || LayerStatus(layer) == eWardenObjectiveStatus.WardenObjectiveItemSolved;

    // The items objectives need must have spawned: data cubes, big items and the HSU's item.
    private static void ReportObjectiveItems()
    {
        var wanted = new Dictionary<uint, string>();
        foreach (var layer in LayerOrder)
            for (int chain = 0; WardenObjectiveManager.TryGetWardenObjectiveDataForLayer(layer, chain, out var block); chain++)
            {
                if (block.Type == eWardenObjectiveType.GatherSmallItems) wanted[block.Gather_ItemId] = $"{layer}#{chain} gather {block.Gather_RequiredCount}";
                if (block.Type == eWardenObjectiveType.RetrieveBigItems) foreach (var item in block.Retrieve_Items) wanted[item] = $"{layer}#{chain} retrieve";
                if (block.Type == eWardenObjectiveType.ActivateSmallHSU) wanted[block.ActivateHSU_ItemFromStart] = $"{layer}#{chain} HSU item";
            }
        var items = UnityEngine.Object.FindObjectsOfType<Item>();
        foreach (var (id, use) in wanted)
            Driver.Record("OBJECTIVE_ITEM " + Json(new { id, use, name = GameDataBlockBase<ItemDataBlock>.GetBlock(id)?.name, spawned = items.Count(x => x.ItemDataBlock != null && x.ItemDataBlock.persistentID == id) }));
    }

    private static string ZoneKey(LG_Zone zone) => $"{zone.DimensionIndex}/{zone.Layer.m_type}/{zone.LocalIndex}";

    private static LG_SecurityDoor? ZoneDoor(LG_Zone zone) => zone.m_sourceGate?.SpawnedDoor?.TryCast<LG_SecurityDoor>();

    private static bool IsOpen(LG_SecurityDoor? door) => door == null || door.m_sync.GetCurrentSyncState().status is eDoorStatus.Open or eDoorStatus.Opening or eDoorStatus.Destroyed;

    // A zone can be reached once the zone it is built from is open; zones without a source are entered by elevator or warp.
    private static bool Reachable(LG_Zone zone)
    {
        var source = zone.m_settings?.m_sourceZone;
        return source == null || source.Pointer == zone.Pointer || IsOpen(ZoneDoor(source)) && Reachable(source);
    }

    private static IEnumerator OpenReachable(Driver driver, HashSet<string> ran)
    {
        for (int pass = 0; pass < 60; pass++)
        {
            bool progress = false;
            var zones = Builder.CurrentFloor.allZones.ToArray().OrderBy(z => (int)z.Layer.m_type).ThenBy(z => (int)z.DimensionIndex).ThenBy(z => (int)z.LocalIndex).ToArray();
            foreach (var zone in zones)
            {
                var door = ZoneDoor(zone);
                if (IsOpen(door))
                {
                    if (Reachable(zone)) yield return RunCommands(driver, zone, ran);
                    continue;
                }
                if (!Reachable(zone) || door!.m_sync.GetCurrentSyncState().status == eDoorStatus.Closed_LockedWithNoKey) continue;
                yield return OpenDoor(driver, zone, door);
                progress = true;
            }
            if (progress) continue;
            // Delayed events may still unlock doors.
            if (EventTiming.Pending == 0) break;
            yield return WaitForEvents(240);
        }
        var closed = Builder.CurrentFloor.allZones.ToArray().Where(z => !IsOpen(ZoneDoor(z))).Select(z => $"{ZoneKey(z)}:{DoorStatus(ZoneDoor(z))}{(Reachable(z) ? "" : " (behind a closed zone)")}");
        Driver.Record("DOORS_CLOSED " + Json(closed.ToArray()));
    }

    private static IEnumerator OpenDoor(Driver driver, LG_Zone zone, LG_SecurityDoor door)
    {
        var player = LocalPlayer;
        var gate = door.Gate;
        var outside = gate.m_linksFrom?.m_zone?.Pointer == zone.Pointer ? gate.m_linksTo : gate.m_linksFrom;
        var doorPos = door.transform.position;
        var away = (outside?.m_courseNode?.Position ?? doorPos) - doorPos;
        away.y = 0;
        var stand = doorPos + (away.sqrMagnitude > 0.01f ? away.normalized : -door.transform.forward) * 2.5f;
        if (NavMesh.SamplePosition(stand, out var hit, 3f, -1)) stand = hit.position;
        if (player.DimensionIndex != zone.DimensionIndex)
        {
            player.RequestWarpToSync(zone.DimensionIndex, stand, -away.normalized, PlayerAgent.WarpOptions.All);
            yield return Wait(4);
        }
        else
        {
            player.TeleportTo(stand);
            yield return Wait(1.5f);
        }
        string Status() => door.m_sync.GetCurrentSyncState().status.ToString();
        var before = Status();
        var steps = new List<string>();
        door.m_sync.AttemptDoorInteraction(eDoorInteractionType.Approach, 0, 0, stand, player);
        yield return Wait(1);
        if (door.m_sync.GetCurrentSyncState().status is eDoorStatus.Closed_LockedWithKeyItem or eDoorStatus.Closed_LockedWithPowerGenerator or eDoorStatus.Closed_LockedWithBulkheadDC)
        {
            // Developer shortcut for keycards, power cells and bulkhead keys.
            door.m_sync.AttemptDoorInteraction(eDoorInteractionType.Unlock, 0, 0, stand, player);
            steps.Add("unlock " + Status());
            yield return Wait(2);
        }
        if (door.m_sync.GetCurrentSyncState().status is eDoorStatus.Closed_LockedWithChainedPuzzle or eDoorStatus.Closed_LockedWithChainedPuzzle_Alarm)
        {
            door.m_sync.AttemptDoorInteraction(eDoorInteractionType.ActivateChainedPuzzle, 0, 0, stand, player);
            yield return Wait(3);
            steps.Add("scan " + Status());
            var puzzle = door.m_locks.ChainedPuzzleToSolve;
            puzzle?.AttemptInteract(eChainedPuzzleInteraction.Solve);
            for (int i = 0; i < 30 && door.m_sync.GetCurrentSyncState().status == eDoorStatus.ChainedPuzzleActivated; i++) yield return Wait(0.5f);
            steps.Add("solved " + Status());
        }
        door.m_sync.AttemptDoorInteraction(eDoorInteractionType.Open, 0, 0, stand, player);
        for (int i = 0; i < 24 && !IsOpen(door); i++) yield return Wait(0.5f);
        var nodes = zone.m_courseNodes;
        if (IsOpen(door) && nodes != null && nodes.Count > 0 && NavMesh.SamplePosition(nodes[0].Position, out var inside, 4f, -1))
        {
            player.TeleportTo(inside.position);
            yield return Wait(2);
        }
        Driver.Record("DOOR " + Json(new { zone = ZoneKey(zone), before, steps, after = Status(), player = LocalPlayer.CourseNode?.m_zone != null ? ZoneKey(LocalPlayer.CourseNode.m_zone) : "" }));
        driver.RunConsole("KillEnemies Reachable");
    }

    // Every unique terminal command of an open zone, once, after its output and events have played out.
    private static IEnumerator RunCommands(Driver driver, LG_Zone zone, HashSet<string> ran)
    {
        var placements = zone.m_settings?.m_zoneData?.TerminalPlacements;
        if (placements == null) yield break;
        foreach (var placement in placements.ToArray())
        {
            if (placement.UniqueCommands == null) continue;
            foreach (var unique in placement.UniqueCommands.ToArray())
            {
                var key = ZoneKey(zone) + " " + unique.Command;
                if (!ran.Add(key)) continue;
                var terminal = zone.TerminalsSpawnedInZone.ToArray().FirstOrDefault(t => t.m_command.TryGetCommand(unique.Command, out _, out _, out _));
                if (terminal == null) { Driver.Record("COMMAND " + Json(new { zone = ZoneKey(zone), command = unique.Command, result = "no terminal has it" })); continue; }
                float output = unique.PostCommandOutputs?.ToArray().Sum(o => o.Time) ?? 0;
                int intel = IntelCalls;
                yield return TypeCommand(driver, terminal, unique.Command);
                yield return Wait(output + 2);
                yield return WaitForEvents(240);
                terminal.ExitFPSView();
                Driver.Record("COMMAND " + Json(new { zone = ZoneKey(zone), command = unique.Command, result = "ran", intelShown = IntelCalls - intel }));
            }
        }
    }

    private static IEnumerator WaitForEvents(float cap)
    {
        float until = Time.realtimeSinceStartup + cap;
        while (EventTiming.Pending > 0 && Time.realtimeSinceStartup < until) yield return Wait(1);
    }

    private static IEnumerator CompleteObjective(Driver driver, LG_LayerType layer, int chain, WardenObjectiveDataBlock block)
    {
        switch (block.Type)
        {
            case eWardenObjectiveType.SpecialTerminalCommand:
            {
                var terminal = UnityEngine.Object.FindObjectsOfType<LG_ComputerTerminal>().FirstOrDefault(t => t.m_command.TryGetCommand(block.SpecialTerminalCommand, out _, out _, out _));
                Driver.Record("OBJECTIVE_STEP " + Json(new { layer = layer.ToString(), step = "special command " + block.SpecialTerminalCommand, terminal = terminal?.ItemKey }));
                if (terminal != null)
                {
                    yield return TypeCommand(driver, terminal, block.SpecialTerminalCommand);
                    for (int i = 0; i < 120 && !Solved(layer, chain); i++) yield return Wait(0.5f);
                    terminal.ExitFPSView();
                }
                break;
            }
            case eWardenObjectiveType.Reactor_Startup:
                yield return RunReactor(driver, layer, chain);
                break;
            case eWardenObjectiveType.Survival:
                // The objective's own events (sub-objective after 60 s) play out before the timer is skipped.
                yield return ActivationSteps(layer, block);
                break;
            default:
                yield return ActivationSteps(layer, block);
                break;
        }
        if (!Solved(layer, chain))
        {
            driver.RunConsole("CompleteObjective " + layer);
            for (int i = 0; i < 20 && !Solved(layer, chain); i++) yield return Wait(0.5f);
            Driver.Record("OBJECTIVE_STEP " + Json(new { layer = layer.ToString(), step = "CompleteObjective", solved = Solved(layer, chain) }));
        }
    }

    // Run the objective's OnActivate list one EventBreak step at a time, as the objective does while it progresses.
    private static IEnumerator ActivationSteps(LG_LayerType layer, WardenObjectiveDataBlock block)
    {
        var events = block.EventsOnActivate;
        if (events == null || events.Count == 0) yield break;
        int steps = events.ToArray().Count(e => e.Type == eWardenObjectiveEventType.EventBreak) + 1;
        // The index is where the step starts in the list; the call returns where the next step starts.
        int start = 0;
        for (int i = 0; i < steps && start < events.Count; i++)
        {
            int next = WardenObjectiveManager.CheckAndExecuteEventsWithBreaks(layer, events, eWardenObjectiveEventTrigger.None, start, true, 0f);
            Driver.Record("OBJECTIVE_STEP " + Json(new { layer = layer.ToString(), step = $"OnActivate step {i + 1}/{steps}", start, next }));
            yield return Wait(2);
            yield return WaitForEvents(240);
            if (next <= start) break;
            start = next;
        }
    }

    private static IEnumerator RunReactor(Driver driver, LG_LayerType layer, int chain)
    {
        var reactor = UnityEngine.Object.FindObjectsOfType<LG_WardenObjective_Reactor>().FirstOrDefault(r => r.OriginLayer == layer);
        if (reactor == null) { Driver.Record("OBJECTIVE_STEP " + Json(new { layer = layer.ToString(), step = "no reactor" })); yield break; }
        yield return TypeCommand(driver, reactor.m_terminal, "REACTOR_STARTUP");
        // The command starts the objective's ChainedPuzzleToActive at the reactor; the startup begins once it is solved.
        if (WardenObjectiveManager.TryGetWardenObjectiveDataForLayer(layer, chain, out var data) && data.ChainedPuzzleToActive != 0)
        {
            ChainedPuzzleInstance? scan = null;
            for (int i = 0; i < 30 && scan == null; i++)
            {
                scan = FindScan(data.ChainedPuzzleToActive, reactor.transform.position);
                if (scan == null) yield return Wait(0.5f);
            }
            Driver.Record("REACTOR_SCAN " + Json(new { puzzle = data.ChainedPuzzleToActive, found = scan != null, active = scan?.IsActive }));
            reactor.m_terminal.ExitFPSView();
            if (scan != null) yield return SolveScan(scan);
            yield return UseTerminal(driver, reactor.m_terminal);
        }
        string last = "", verified = "";
        float until = Time.realtimeSinceStartup + 40 * 60, nextKill = 0;
        while (Time.realtimeSinceStartup < until && !Solved(layer, chain))
        {
            var status = reactor.m_currentState.status;
            var step = $"{status} wave {reactor.m_currentWaveCount}/{reactor.m_waveCountMax}";
            if (step != last) { Driver.Record("REACTOR " + step); last = step; }
            if (status == eReactorStatus.Startup_complete) break;
            var code = reactor.CurrentStateOverrideCode;
            if (status == eReactorStatus.Startup_waitForVerify && !string.IsNullOrEmpty(code) && code != verified)
            {
                reactor.m_terminal.m_command.EvaluateInput("REACTOR_VERIFY " + code);
                Driver.Record("REACTOR_VERIFY " + code);
                verified = code;
            }
            if (Time.realtimeSinceStartup > nextKill) { driver.RunConsole("KillEnemies Reachable"); nextKill = Time.realtimeSinceStartup + 30; }
            yield return Wait(1);
        }
        reactor.m_terminal.ExitFPSView();
    }

    // An unsolved objective scan with this ID: the one closest to a point, or the last one created. Door scans are left to the doors.
    private static ChainedPuzzleInstance? FindScan(uint id, Vector3? near = null)
    {
        var doorPuzzles = Builder.CurrentFloor.allZones.ToArray().Select(z => ZoneDoor(z)?.m_locks?.ChainedPuzzleToSolve?.Pointer ?? IntPtr.Zero).ToHashSet();
        var scans = ChainedPuzzleManager.Current.m_instances.ToArray()
            .Where(p => p != null && !p.IsSolved && p.Data != null && p.Data.persistentID == id && !doorPuzzles.Contains(p.Pointer));
        if (near == null) return scans.LastOrDefault();
        return scans.OrderBy(p => p.m_sourceArea?.m_courseNode != null ? Vector3.Distance(p.m_sourceArea.m_courseNode.Position, near.Value) : float.MaxValue).FirstOrDefault();
    }

    private static IEnumerator SolveScan(ChainedPuzzleInstance scan)
    {
        var area = scan.m_sourceArea?.m_courseNode?.Position;
        if (area.HasValue && NavMesh.SamplePosition(area.Value, out var hit, 4f, -1)) LocalPlayer.TeleportTo(hit.position);
        yield return Wait(2);
        if (!scan.IsActive) scan.AttemptInteract(eChainedPuzzleInteraction.Activate);
        yield return Wait(3);
        scan.AttemptInteract(eChainedPuzzleInteraction.Solve);
    }

    private static IEnumerator Exit(Driver driver, string stage)
    {
        WardenObjectiveManager.TryGetLastWardenObjectiveDataForLayer(LG_LayerType.MainLayer, out var last);
        uint exitScan = last?.ChainedPuzzleAtExit ?? 0;
        ChainedPuzzleInstance? scan = null;
        for (int i = 0; i < 120 && scan == null; i++)
        {
            scan = FindScan(exitScan);
            if (scan == null) yield return Wait(0.5f);
        }
        Driver.Record("EXIT_SCAN " + Json(new { exitScan, found = scan != null, win = WardenObjectiveManager.WinCondition(LG_LayerType.MainLayer).ToString() }));
        if (scan != null) yield return SolveScan(scan);
        for (int i = 0; i < 120 && GameStateManager.CurrentStateName != eGameStateName.ExpeditionSuccess; i++) yield return Wait(0.5f);
        var layers = LayerOrder.Where(l => WardenObjectiveManager.TryGetWardenObjectiveDataForLayer(l, 0, out _)).ToDictionary(l => l.ToString(), l => LayerStatus(l).ToString());
        Driver.Record("CLEAR_RESULT " + Json(new { stage, success = GameStateManager.CurrentStateName == eGameStateName.ExpeditionSuccess, state = GameStateManager.CurrentStateName.ToString(), layers, delays = EventTiming.Summary() }));
    }
}

// Records how long each warden event actually waited, from the game's own BeginDelay/EndDelay log lines.
internal sealed class EventTiming : ILogListener
{
    private static EventTiming? current;
    private static readonly Regex Line = new(@"ExecuteEvent (BeginDelay|EndDelay), .*?type: (\w+) LocalIndex: (\w+) Delay: ([\d.]+).*(?:with|after) delay ([\d.]+)", RegexOptions.Singleline);
    private readonly Dictionary<string, Queue<double>> begun = new();
    private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    private readonly List<double> errors = new();
    private readonly object gate = new();

    public LogLevel LogLevelFilter => LogLevel.All;

    internal static int Pending { get { var c = current; if (c == null) return 0; lock (c.gate) return c.begun.Values.Sum(q => q.Count); } }

    internal static void Start()
    {
        if (current != null) return;
        current = new EventTiming();
        BepInEx.Logging.Logger.Listeners.Add(current);
    }

    internal static void Stop()
    {
        if (current == null) return;
        BepInEx.Logging.Logger.Listeners.Remove(current);
        current = null;
    }

    internal static object Summary()
    {
        var c = current;
        if (c == null) return new { };
        lock (c.gate) return new { measured = c.errors.Count, maxError = c.errors.Count == 0 ? 0 : Math.Round(c.errors.Max(), 2), pending = c.begun.Values.Sum(q => q.Count) };
    }

    public void LogEvent(object sender, LogEventArgs eventArgs)
    {
        if (eventArgs.Source.SourceName != "Unity" || eventArgs.Data is not string text) return;
        var m = Line.Match(text);
        if (!m.Success) return;
        var key = $"{m.Groups[2].Value}/{m.Groups[3].Value}/{m.Groups[4].Value}";
        double now = clock.Elapsed.TotalSeconds, wait = double.Parse(m.Groups[5].Value, System.Globalization.CultureInfo.InvariantCulture);
        lock (gate)
        {
            if (m.Groups[1].Value == "BeginDelay")
            {
                if (!begun.TryGetValue(key, out var queue)) begun[key] = queue = new Queue<double>();
                queue.Enqueue(now);
                return;
            }
            if (!begun.TryGetValue(key, out var started) || started.Count == 0) return;
            double actual = now - started.Dequeue();
            errors.Add(Math.Abs(actual - wait));
            Driver.Record("EVENT_DELAY " + JsonSerializer.Serialize(new { type = m.Groups[2].Value, zone = m.Groups[3].Value, delay = wait, actual = Math.Round(actual, 2) }));
        }
    }

    public void Dispose() { }
}
