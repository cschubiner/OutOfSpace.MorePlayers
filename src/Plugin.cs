using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Photon.Pun;
using Rewired;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OutOfSpace.MorePlayers
{
    [BepInPlugin(Id, "More Local Players", "0.2.1")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Id = "local.outofspace.moreplayers";
        internal static ManualLogSource Log;
        internal static int Limit = 16;
        internal static bool Ready;
        internal static ConfigEntry<bool> RawInput;
        private bool overlay;
        private float nextReport;
        private string status = "Initializing input...";
        private Harmony harmony;

        // The main menu contacts Photon even before joining a room. A background connection
        // is not an online game and must not reduce the available couch-player slots.
        internal static bool Local => !PhotonNetwork.InRoom || PhotonNetwork.OfflineMode;
        internal static int InputLimit() => Ready && ReInput.isReady ? Math.Min(Limit, ReInput.players.playerCount) : 4;
        internal static int LocalLimit() => Local ? InputLimit() : 4;

        private void Awake()
        {
            Log = Logger;
            RoomSizes.Setting = Config.Bind("Rooms", "Size", 2,
                new ConfigDescription("Average room floor area: 0 Standard, 1 +20%, 2 +40%, 3 +70%, 4 2x. Also adjustable in the local lobby. Applies to newly generated ships.", new AcceptableValueRange<int>(0, 4)));
            Limit = Config.Bind("Players", "MaxLocalPlayers", 16,
                new ConfigDescription("Restart required. Local slots, from four to sixteen. Hardware controller capacity depends on the input backend.", new AcceptableValueRange<int>(4, 16))).Value;
            RawInput = Config.Bind("Input", "DisableXInput", false,
                "Restart required. Uses Raw Input without XInput for more than four compatible controllers. May change Xbox trigger mappings and rumble.");
            overlay = Config.Bind("Diagnostics", "ShowOverlay", false, "F8 toggles the controller and player diagnostic overlay.").Value;
            harmony = new Harmony(Id);
            try
            {
                VerifyAssembly(typeof(PlayerSelectUI).Assembly, "4D5E474A5AD7D6227DFB7CEF7D09FFA357A19CCF601D65503CFE01C68EAE934C");
                VerifyAssembly(typeof(ReInput).Assembly, "9B28BB0BF216DB66E0ED424ED45872087B413D538D4EBF38BAA1C2EC9519C764");
                harmony.PatchAll(typeof(Plugin).Assembly);
                PatchFour(typeof(PlayerSelectUI), "LookForLocalPlayers", nameof(LocalLimit));
                PatchFour(typeof(PlayerSelectUI), "ButtonPressedAnyPlayer", nameof(LocalLimit));
                PatchFour(typeof(PlayerSelectUI), "ButtonPressedByActivePlayer", nameof(LocalLimit));
                PatchFour(typeof(MultiplayerManager), "LookForLocalPlayers", nameof(LocalLimit));
                PatchFour(typeof(InitializeInputSettings), "Awake", nameof(InputLimit));
                // Conversion operators are overloaded by return type, so patch their MethodInfo objects directly.
                foreach (var method in typeof(GameValue).GetMethods().Where(m => m.Name == "op_Implicit"))
                    harmony.Patch(method, transpiler: new HarmonyMethod(typeof(Plugin), nameof(ClampBalanceCount)));
                Ready = true;
                Log.LogInfo("More Local Players 0.2.1 enabled; configured slots=" + Limit + "; F8 diagnostics.");
            }
            catch (Exception ex)
            {
                harmony.UnpatchSelf();
                Ready = false;
                Log.LogError("Patch initialization failed. Original game behavior restored: " + ex);
            }
        }

        private static void VerifyAssembly(Assembly assembly, string expected)
        {
            using (var stream = File.OpenRead(assembly.Location))
            using (var sha = SHA256.Create())
                if (BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "") != expected)
                    throw new InvalidOperationException("Untested game assembly: " + assembly.GetName().Name + ". This build targets Steam build 6616527.");
        }

        private static readonly Dictionary<MethodBase, string> FourTargets = new Dictionary<MethodBase, string>();
        private void PatchFour(Type type, string name, string replacement)
        {
            var method = AccessTools.Method(type, name) ?? throw new MissingMethodException(type.FullName, name);
            FourTargets[method] = replacement;
            harmony.Patch(method, transpiler: new HarmonyMethod(typeof(Plugin), nameof(ReplaceFour)));
        }

        private static IEnumerable<CodeInstruction> ReplaceFour(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            int hits = 0;
            foreach (var code in instructions)
            {
                if (code.LoadsConstant(4))
                {
                    code.opcode = OpCodes.Call;
                    code.operand = AccessTools.Method(typeof(Plugin), FourTargets[__originalMethod]);
                    hits++;
                }
                yield return code;
            }
            if (hits < 1 || hits > 2) throw new InvalidOperationException("Unexpected player-limit IL in " + __originalMethod);
            Log.LogInfo("Patched " + __originalMethod.DeclaringType.Name + "." + __originalMethod.Name + ": " + hits + " verified limit constants.");
        }

        public static int BalanceCount(int count) => Local ? Math.Min(count, 4) : count;
        private static IEnumerable<CodeInstruction> ClampBalanceCount(IEnumerable<CodeInstruction> instructions)
        {
            int hits = 0;
            var count = AccessTools.PropertyGetter(typeof(MultiplayerManager), "playerCount");
            foreach (var code in instructions)
            {
                yield return code;
                if (code.Calls(count))
                {
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Plugin), nameof(BalanceCount)));
                    hits++;
                }
            }
            if (hits != 1) throw new InvalidOperationException("Unexpected GameValue player-count lookup.");
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F8)) overlay = !overlay;
            ControllerAssignments.Capture();
            InputActivity.Capture();
            if (!ReInput.isReady || Time.unscaledTime < nextReport) return;
            nextReport = Time.unscaledTime + 3;
            ControllerProfiles.EnsureMaps();
            status = Diagnostics();
            // Local file only; no network listener or telemetry.
            try { File.WriteAllText(Path.Combine(Paths.BepInExRootPath, "MorePlayers-status.txt"), status); }
            catch (IOException) { }
        }

        internal static string Diagnostics()
        {
            var lines = new List<string> { "More Local Players 0.2.1 | " + SceneManager.GetActiveScene().name,
                "Configured: " + Limit + " | logical players: " + ReInput.players.playerCount + " | detected controllers: " + ReInput.controllers.joystickCount,
                "Mode: " + (Local ? "Local" : "Online (stock four-player limit)") };
            foreach (var p in ReInput.players.Players)
                lines.Add("P" + (p.id + 1) + ": " + string.Join(", ", p.controllers.Joysticks.Select(j => "#" + j.id + " " + j.name).ToArray()) +
                    (p.controllers.hasKeyboard ? " + keyboard" : "") + " | move " + p.GetAxis("MoveHorizontal").ToString("0.00") + ", " + p.GetAxis("MoveVertical").ToString("0.00") + InputActivity.Describe(p));
            if (MultiplayerManager.instance != null)
                lines.Add("Roster: " + MultiplayerManager.instance.LocalPlayerDatas.Count + " | characters: " + MultiplayerManager.instance.playerCount);
            if (MultiplayerManager.instance != null)
                lines.Add("Joined input slots: " + string.Join(", ", MultiplayerManager.instance.LocalPlayerDatas.Select(p => "P" + (p.ControllerId + 1)).ToArray()));
            lines.Add("F8: show/hide diagnostics");
            return string.Join("\n", lines.ToArray());
        }

        private void OnGUI()
        {
            if (!overlay) return;
            GUI.Box(new Rect(12, 12, 1050, 125 + InputLimit() * 22), "");
            GUI.Label(new Rect(24, 22, 1030, 115 + InputLimit() * 22), status);
        }
    }
}
