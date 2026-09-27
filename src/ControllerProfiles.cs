using System;
using System.Collections.Generic;
using System.Linq;
using Rewired;
using UnityEngine;

namespace OutOfSpace.MorePlayers
{
    // This Rewired build predates these devices. It detects their HID reports but
    // creates empty generic maps, so Player.GetAnyButtonDown never sees a press.
    internal static class ControllerProfiles
    {
        internal static void EnsureMaps()
        {
            if (!ReInput.isReady) return;
            foreach (var player in ReInput.players.Players)
                foreach (var joystick in player.controllers.Joysticks)
                {
                    if (joystick.hardwareTypeGuid != Guid.Empty) continue;
                    string hardware = joystick.hardwareIdentifier;
                    bool dualSense = hardware.StartsWith("WindowsRawInput", StringComparison.Ordinal) &&
                        hardware.IndexOf("0ce6054c-", StringComparison.OrdinalIgnoreCase) >= 0;
                    // Switch Pro enhanced reports are misread by this legacy Raw Input
                    // backend (including a changing packet counter as button presses).
                    // Leave them unmapped until a report-aware input backend is available.
                    if (!dualSense) continue;
                    foreach (var map in player.controllers.maps.GetMaps(joystick))
                    {
                        // Never replace existing mappings or user remaps.
                        if (map.AllMaps.Any() || map.layoutId != 0 || (map.categoryId != 5 && map.categoryId != 6)) continue;
                        Fill(map, joystick, dualSense);
                        Plugin.Log.LogInfo("Added native " + (dualSense ? "DualSense" : "Switch Pro") +
                            " mappings: P" + (player.id + 1) + ", category=" + map.categoryId + ", bindings=" + map.AllMaps.Count());
                    }
                }
        }

        private static void Fill(ControllerMap map, Joystick joystick, bool dualSense)
        {
            // Raw HID button numbers are zero based. Switch uses its printed A/B
            // and Y/X labels for confirm/cancel and use/shop, respectively.
            int confirm = 1, cancel = dualSense ? 2 : 0;
            int use = dualSense ? 0 : 2, shop = 3;
            Action<string, int> button = (action, index) => Bind(map, joystick, action, "Button " + index);
            Action<string, int, bool> axis = (action, index, invert) => Bind(map, joystick, action, "Axis " + index, Pole.Positive, AxisRange.Full, invert);
            button("CategoryLeft", 4);
            button("CategoryRight", 5);
            axis("MoveHorizontal", 0, false);
            axis("MoveVertical", 1, true);
            if (map.categoryId == 5)
            {
                button("Start", 9);
                button("Reset", 8);
                button("Submit", confirm);
                button("Cancel", cancel);
                Bind(map, joystick, "NavigateLeft", "Hat 0 Left");
                Bind(map, joystick, "NavigateRight", "Hat 0 Right");
                Bind(map, joystick, "NavigateLeft", "Axis 0", Pole.Positive, AxisRange.Negative);
                Bind(map, joystick, "NavigateRight", "Axis 0", Pole.Positive, AxisRange.Positive);
            }
            else
            {
                button("Interact", confirm);
                button("Drop", cancel);
                button("UseItem", use);
                button("Shop", shop);
                button("Info", 4);
                button("Ping", 5);
                button("UseItem", 7);
                Bind(map, joystick, "MoveVertical", "Hat 0 Up");
                Bind(map, joystick, "MoveVertical", "Hat 0 Down", Pole.Negative);
                Bind(map, joystick, "MoveHorizontal", "Hat 0 Left", Pole.Negative);
                Bind(map, joystick, "MoveHorizontal", "Hat 0 Right");
                axis("LookHorizontal", 2, false);
                axis("LookVertical", dualSense ? 5 : 3, true);
            }
        }

        private static void Bind(ControllerMap map, Joystick joystick, string action, string element,
            Pole pole = Pole.Positive, AxisRange range = AxisRange.Full, bool invert = false)
        {
            var id = joystick.ElementIdentifiers.FirstOrDefault(e => e.name == element);
            int actionId = ReInput.mapping.GetActionId(action);
            if (id == null || actionId < 0 || !map.CreateElementMap(actionId, pole, id.id, id.elementType, range, invert))
                throw new InvalidOperationException("Cannot map " + joystick.name + ": " + action + " to " + element);
        }
    }

    internal static class InputActivity
    {
        private static readonly Dictionary<int, string> LastButtons = new Dictionary<int, string>();
        private static readonly Dictionary<int, float> RawTimes = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> ActionTimes = new Dictionary<int, float>();

        internal static void Capture()
        {
            if (!ReInput.isReady) return;
            foreach (var j in ReInput.controllers.Joysticks)
                for (int i = 0; i < j.buttonCount; i++)
                    if (j.GetButtonDown(i))
                    {
                        LastButtons[j.id] = "B" + i;
                        RawTimes[j.id] = Time.unscaledTime;
                    }
            foreach (var p in ReInput.players.Players)
                if (p.GetAnyButtonDown()) ActionTimes[p.id] = Time.unscaledTime;
        }

        private static string Age(Dictionary<int, float> times, int id) => times.ContainsKey(id)
            ? (Time.unscaledTime - times[id]).ToString("0") + "s" : "never";

        internal static string Describe(Player player)
        {
            int bindings = player.controllers.maps.GetAllMaps(ControllerType.Joystick)
                .Where(m => m.enabled && player.controllers.Joysticks.Any(j => j.id == m.controllerId)).Sum(m => m.AllMaps.Count());
            return " | binds " + bindings + " | raw " + string.Join(",", player.controllers.Joysticks.Select(j =>
                (LastButtons.ContainsKey(j.id) ? LastButtons[j.id] + " " : "") + Age(RawTimes, j.id)).ToArray()) +
                " | mapped " + Age(ActionTimes, player.id);
        }
    }
}
