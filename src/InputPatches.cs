using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Rewired;
using Rewired.Data;

namespace OutOfSpace.MorePlayers
{
    // Patch a stable, named initialization boundary instead of the obfuscated ReInput internals.
    [HarmonyPatch(typeof(InputManager_Base), "Initialize")]
    internal static class ExpandInputPlayers
    {
        private static void Prefix(InputManager_Base __instance)
        {
            var data = __instance.userData;
            var players = (List<Player_Editor>)AccessTools.Field(typeof(UserData), "players").GetValue(data);
            var template = players.Single(p => p.id == 4);
            Plugin.Log.LogInfo("Before expansion: " + string.Join(",", players.Select(p => p.id + ":" + p.name).ToArray()));
            // UserData includes its reserved System entry. Runtime game-player IDs are assigned
            // separately by Rewired; preserve the serialized order and the System definition.
            while (players.Count(p => p.id != 9999999) < Plugin.Limit)
            {
                int id = players.Where(p => p.id != 9999999).Max(p => p.id) + 1;
                var p = template.Clone();
                AccessTools.Field(typeof(Player_Editor), "_id").SetValue(p, id);
                AccessTools.Field(typeof(Player_Editor), "_name").SetValue(p, "MorePlayers" + id);
                AccessTools.Field(typeof(Player_Editor), "_descriptiveName").SetValue(p, "Player " + id);
                AccessTools.Field(typeof(Player_Editor), "_assignKeyboardOnStart").SetValue(p, false);
                AccessTools.Field(typeof(Player_Editor), "_assignMouseOnStart").SetValue(p, false);
                AccessTools.Field(typeof(Player_Editor), "_excludeFromControllerAutoAssignment").SetValue(p, false);
                players.Add(p);
            }
            data.ConfigVars.maxJoysticksPerPlayer = 1;
            data.ConfigVars.autoAssignJoysticks = true;
            data.ConfigVars.distributeJoysticksEvenly = true;
            if (Plugin.RawInput.Value)
            {
                data.ConfigVars.useXInput = false;
                // Resolve by the installed assembly's enum name to avoid hard-coded backend integers.
                var field = data.ConfigVars.GetType().GetField("windowsStandalonePrimaryInputSource");
                field.SetValue(data.ConfigVars, Enum.Parse(field.FieldType, "RawInput"));
            }
            Plugin.Log.LogInfo("Input definitions: " + string.Join(",", players.Select(p => p.id.ToString()).ToArray()) +
                "; backend=" + data.ConfigVars.windowsStandalonePrimaryInputSource + "; XInput=" + data.ConfigVars.useXInput +
                "; Rewired=" + typeof(ReInput).Assembly.GetName().Version);
        }
        private static void Postfix()
        {
            if (!ReInput.isReady) return;
            ControllerAssignments.Restore();
            ReInput.ControllerConnectedEvent -= ControllerAssignments.Connected;
            ReInput.ControllerConnectedEvent += ControllerAssignments.Connected;
        }
    }

    internal static class ControllerAssignments
    {
        private static readonly Dictionary<string, int> Owners = new Dictionary<string, int>();
        private static string Key(Joystick j) => j.deviceInstanceGuid != Guid.Empty ? j.deviceInstanceGuid.ToString() : j.hardwareTypeGuid + ":" + j.name + ":" + j.systemId;

        internal static void Capture()
        {
            if (!ReInput.isReady) return;
            foreach (var p in ReInput.players.Players)
                foreach (var j in p.controllers.Joysticks) Owners[Key(j)] = p.id;
        }
        internal static void Connected(ControllerStatusChangedEventArgs args)
        {
            if (args.controllerType == ControllerType.Joystick) Restore();
        }
        internal static void Restore()
        {
            if (!ReInput.isReady) return;
            var devices = ReInput.controllers.Joysticks.ToArray();
            var plan = new Dictionary<Joystick, int>();
            var used = new HashSet<int>();
            foreach (var j in devices)
            {
                int owner;
                if (Owners.TryGetValue(Key(j), out owner) && ReInput.players.GetPlayer(owner) != null && used.Add(owner))
                    plan[j] = owner;
            }
            foreach (var j in devices.Where(j => !plan.ContainsKey(j)))
            {
                var current = ReInput.players.Players.FirstOrDefault(p => p.controllers.ContainsController(j) && !used.Contains(p.id));
                var free = current ?? ReInput.players.Players.FirstOrDefault(p => !used.Contains(p.id));
                if (free == null) continue;
                plan[j] = free.id;
                used.Add(free.id);
            }
            foreach (var p in ReInput.players.Players) p.controllers.ClearControllersOfType(ControllerType.Joystick);
            foreach (var entry in plan) ReInput.players.GetPlayer(entry.Value).controllers.AddController(entry.Key, true);
            ControllerProfiles.EnsureMaps();
            Capture();
            Plugin.Log.LogInfo("Controller assignments: " + string.Join("; ", ReInput.players.Players.Select(p =>
                "P" + (p.id + 1) + "=" + string.Join(",", p.controllers.Joysticks.Select(j => j.name + " [" + j.deviceInstanceGuid + "]").ToArray())).ToArray()));
        }
    }
}
