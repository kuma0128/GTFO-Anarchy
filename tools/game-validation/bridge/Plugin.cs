using System.Reflection;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using CConsole.Interop;
using UnityEngine;
using UnityEngine.AI;
using System.Text.Json;
using GameData;
using ScanPosOverride.Managers;

namespace AnarchyValidation;

[BepInPlugin("local.anarchy.validation", "Anarchy CLI Validation", "0.1.0")]
public sealed class Plugin : BasePlugin
{
    public override void Load()
    {
        Application.runInBackground = true;
        Directory.CreateDirectory(Driver.LogRoot);
        AddComponent<Driver>();
    }
}

public sealed class Driver : MonoBehaviour
{
    internal static readonly string LogRoot = Path.GetFullPath(Environment.GetEnvironmentVariable("ANARCHY_VALIDATION_LOGS") ?? Path.Combine(Paths.GameRootPath, "..", "logs"));
    private static readonly string ModRoot = Path.GetFullPath(Environment.GetEnvironmentVariable("ANARCHY_MOD_ROOT") ?? Path.Combine(Paths.PluginPath, "Anarchy"));
    private static readonly string Root = LogRoot;
    private static readonly string Request = Path.Combine(Root, "command.request");
    private string previousState = "";
    private float nextPoll;
    private CustomCmdContext? context;
    public Driver(IntPtr pointer) : base(pointer) { }
    private static void Record(string message)
    {
        string line = $"{DateTime.UtcNow:O} {message}";
        File.AppendAllText(Path.Combine(Root, "bridge.log"), line + Environment.NewLine);
        BepInEx.Logging.Logger.CreateLogSource("AnarchyValidation").LogInfo(message);
    }
    public void Update()
    {
        if (Time.unscaledTime < nextPoll) return;
        nextPoll = Time.unscaledTime + 0.5f;
        try
        {
            var state = GameStateManager.CurrentStateName.ToString();
            if (state != previousState) { Record("STATE " + state); previousState = state; }
            if (!File.Exists(Request)) return;
            var request = File.ReadAllText(Request).Trim();
            File.Move(Request, Path.Combine(Root, "command.accepted"), true);
            Record("COMMAND " + request);
            if (request == "quit") { Application.Quit(); return; }
            if (request == "status") { Record("STATUS " + state); return; }
            if (request == "advance-c1")
            {
                var terminal = UnityEngine.Object.FindObjectsOfType<LevelGeneration.LG_ComputerTerminal>()
                    .First(t => t.SpawnNode != null && (int)t.SpawnNode.m_zone.LocalIndex == 1);
                var gate = terminal.SpawnNode.m_zone.m_sourceGate.SpawnedDoor.Cast<LevelGeneration.LG_SecurityDoor>();
                gate.ForceOpenSecurityDoor();
                Record("MOVE_GATE_OPEN zone 1");
                context ??= (CustomCmdContext)Activator.CreateInstance(typeof(CustomCmdContext), BindingFlags.Instance | BindingFlags.NonPublic, null, new object?[] { null }, null)!;
                Record("MOVE_TEST " + terminal.ItemKey + " zone " + terminal.SpawnNode.m_zone.LocalIndex);
                context.ExecuteCommand("TP " + terminal.ItemKey);
                Record("DISPATCHED advance-c1");
                return;
            }
            if (request == "check-c1-move")
            {
                var player = Player.PlayerManager.GetLocalPlayerAgent();
                var zone = (int)player.CourseNode.m_zone.LocalIndex;
                Record("MOVE_AUDIT " + JsonSerializer.Serialize(new { zone, x = player.Position.x, y = player.Position.y, z = player.Position.z }));
                if (zone != 1) throw new InvalidOperationException("C1 movement test did not reach zone 1");
                return;
            }
            if (request == "build")
            {
                if (state != "Lobby") throw new InvalidOperationException("Build requires Lobby state");
                Record("START_RESULT " + GameStateManager.CurrentState.TryStartLevelTrigger());
                return;
            }
            if (request == "abort")
            {
                GameStateManager.ChangeState(eGameStateName.ExpeditionAbort);
                return;
            }
            if (request.StartsWith("audit "))
            {
                var stage = request.Substring(6);
                if (!new[] { "C1", "C2", "D1", "D2", "D3" }.Contains(stage)) throw new ArgumentException("Unknown stage");
                if (state != "InLevel") throw new InvalidOperationException("Audit requires InLevel state");
                var filename = Path.Combine(ModRoot, "Custom", "ScanPositionOverrides", stage + ".json");
                if (stage != "C1" && !File.Exists(filename)) throw new FileNotFoundException("Required scan override file is missing", filename);
                if (File.Exists(filename))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(filename), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                    foreach (var point in doc.RootElement.GetProperty("Puzzles").EnumerateArray())
                    {
                        if (!point.TryGetProperty("Position", out var coords)) continue;
                        var position = new Vector3(coords.GetProperty("x").GetSingle(), coords.GetProperty("y").GetSingle(), coords.GetProperty("z").GetSingle());
                        bool found = NavMesh.SamplePosition(position, out var hit, 2.0f, -1);
                        uint index = point.GetProperty("Index").GetUInt32();
                        var bioscan = PuzzleOverrideManager.Current.GetBioscanCore(index);
                        var cluster = PuzzleOverrideManager.Current.GetClusterCore(index);
                        bool registered = bioscan != null || cluster != null;
                        Vector3 actualPosition = bioscan != null ? bioscan.m_position : cluster != null ? cluster.transform.position : Vector3.zero;
                        float appliedDistance = registered ? Vector3.Distance(position, actualPosition) : -1;
                        Record("SCAN_AUDIT " + JsonSerializer.Serialize(new { stage, index, found, registered, appliedDistance, distance = found ? hit.distance : -1, x = position.x, y = position.y, z = position.z }));
                        if (!found || hit.distance > 0.2f || !registered || appliedDistance > 0.2f) throw new InvalidOperationException($"Scan {stage}/{index} failed placement verification");
                    }
                }
                var fog = GameDataBlockBase<FogSettingsDataBlock>.GetBlock(225);
                Record("FOG_AUDIT " + JsonSerializer.Serialize(new { name = fog?.name, infection = fog?.Infection }));
                if (fog == null || fog.name != "Fog_anarchy_d3_after_exit" || Math.Abs(fog.Infection - 0.05f) > 0.00001f) throw new InvalidOperationException("Fog 225 failed verification");
                Record("AUDIT_DONE " + stage);
                return;
            }
            context ??= (CustomCmdContext)Activator.CreateInstance(typeof(CustomCmdContext), BindingFlags.Instance | BindingFlags.NonPublic, null, new object?[] { null }, null)!;
            context.ExecuteCommand(request);
            Record("DISPATCHED " + request);
        }
        catch (Exception ex) { Record("FAIL " + ex); }
    }
}
