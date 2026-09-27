using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OutOfSpace.MorePlayers
{
    internal static class RoomSizes
    {
        internal static ConfigEntry<int> Setting;
        internal static readonly string[] Names = { "STANDARD", "+20%", "+40%", "+70%", "2X" };
        internal static readonly float[] Areas = { 1f, 1.2f, 1.4f, 1.7f, 2f };
        // Overlap and doorway trimming damp the smallest increases. These
        // sampled-generation adjustments bring the resulting floor areas closer
        // to the displayed targets without increasing the maximum above 2x.
        private static readonly float[] GenerationAreas = { 1f, 1.3f, 1.5f, 1.75f, 2f };
        internal static int Index => Mathf.Clamp(Setting.Value, 0, Names.Length - 1);
        internal static float Area(SpaceshipGenerator generator) => Plugin.Local && generator.GetShipSize() != generator.numberOfRoomsMini ? GenerationAreas[Index] : 1f;
    }

    [HarmonyPatch(typeof(SpaceshipGenerator), "RandomizeRooms")]
    internal static class RoomDimensions
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var random = AccessTools.Method(typeof(SpaceshipGenerator), "RandomRange", new[] { typeof(int), typeof(int) });
            int matches = 0;
            foreach (var instruction in instructions)
            {
                // The first four draws choose width/height, including the initial
                // room. The remaining two draws choose placement and stay stock.
                if (instruction.Calls(random) && ++matches <= 4)
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(RoomDimensions), nameof(Draw));
                }
                yield return instruction;
            }
            if (matches != 6) throw new InvalidOperationException("Unexpected room dimension draws.");
        }
        private static int Draw(SpaceshipGenerator generator, int min, int max)
        {
            int original = generator.RandomRange(min, max);
            float area = RoomSizes.Area(generator);
            if (area == 1f) return original;
            // Keep each original random dimension and its variation. Fractional
            // growth is rounded probabilistically, avoiding whole-tile jumps at
            // every setting. Use the ship's seeded RNG for reproducibility.
            float interior = (original - 2) * Mathf.Sqrt(area);
            int floor = Mathf.FloorToInt(interior);
            return 2 + floor + (generator.RandomRange(0f, 1f) < interior - floor ? 1 : 0);
        }
    }

    // Apply before the coroutine allocates its tile matrix; retries reuse the
    // original prefab values rather than multiplying an already scaled value.
    [HarmonyPatch(typeof(SpaceshipGenerator), "CreateShip")]
    internal static class LargerRooms
    {
        private static void Prefix(SpaceshipGenerator __instance)
        {
            var original = __instance.GetComponent<RoomGeneratorDefaults>() ?? __instance.gameObject.AddComponent<RoomGeneratorDefaults>();
            original.Apply(__instance);
        }
    }

    [HarmonyPatch(typeof(Spaceship), "RandomizeDirt")]
    internal static class EntranceFloorSort
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var sort = AccessTools.Method(typeof(List<Cell>), "Sort", new[] { typeof(Comparison<Cell>) });
            int matches = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(sort))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(EntranceFloorSort), nameof(Sort));
                    matches++;
                }
                yield return instruction;
            }
            if (matches != 1) throw new InvalidOperationException("Unexpected entrance dirt sort implementation.");
        }
        private static void Sort(List<Cell> cells, Comparison<Cell> original)
        {
            if (!Plugin.Local) { cells.Sort(original); return; }
            var ship = Spaceship.Instance;
            Vector3 door = Singleton<RoomManager>.Instance.entranceRoom.entranceDoor.transform.position;
            var toWorld = AccessTools.Method(typeof(Spaceship), "CellToWorldPos");
            var distances = new Dictionary<Cell, float>();
            foreach (var cell in cells)
                distances[cell] = Vector3.SqrMagnitude((Vector3)toWorld.Invoke(ship, new object[] { cell.x, cell.y }) - door);
            cells.Sort((a, b) => distances[a].CompareTo(distances[b]));
        }
    }

    internal sealed class RoomGeneratorDefaults : MonoBehaviour
    {
        private bool captured;
        private int min, max, radius, cells;
        internal void Apply(SpaceshipGenerator generator)
        {
            if (!captured)
            {
                min = generator.minRoomSize; max = generator.maxRoomsize;
                radius = generator.radius; cells = generator.maxRoomCells;
                captured = true;
            }
            bool active = Plugin.Local && generator.GetShipSize() != generator.numberOfRoomsMini;
            float area = RoomSizes.Area(generator);
            float scale = Mathf.Sqrt(area);
            generator.minRoomSize = min;
            generator.maxRoomsize = max;
            generator.radius = Mathf.CeilToInt(radius * scale);
            generator.maxRoomCells = Mathf.RoundToInt(cells * area);
            Plugin.Log.LogInfo("Room size " + (active ? RoomSizes.Names[RoomSizes.Index] : "STANDARD") +
                ": target floor-area multiplier=" + (active ? RoomSizes.Areas[RoomSizes.Index] : 1f) + ", generation multiplier=" + area +
                ", room cell cap=" + generator.maxRoomCells + ", requested rooms=" + generator.GetShipSize());
        }
    }

    [HarmonyPatch(typeof(PlayerSelectUI), "OnEnable")]
    internal static class RoomSizeMenu
    {
        private static void Postfix(PlayerSelectUI __instance)
        {
            if (__instance.GetComponent<RoomSizeControl>() == null)
                __instance.gameObject.AddComponent<RoomSizeControl>().Initialize(__instance);
        }
    }

    internal sealed class RoomSizeControl : MonoBehaviour
    {
        private PlayerSelectUI lobby;
        private Button button;
        private TextMeshProUGUI value, hint;
        private int shownIndex = -1;
        private bool? shownEnabled;
        internal void Initialize(PlayerSelectUI owner)
        {
            lobby = owner;
            var root = new GameObject("MorePlayers Room Size", typeof(RectTransform), typeof(Image), typeof(Button), typeof(RoomSizeNavigation));
            root.transform.SetParent(lobby.transform, false);
            var rect = (RectTransform)root.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(32, -140);
            rect.sizeDelta = new Vector2(340, 52);
            root.GetComponent<Image>().color = new Color(.38f, .07f, .36f, .95f);
            button = root.GetComponent<Button>();
            button.onClick.AddListener(() => Change(1));
            root.GetComponent<RoomSizeNavigation>().Control = this;
            var navigation = button.navigation;
            navigation.mode = Navigation.Mode.Explicit;
            navigation.selectOnDown = lobby.buttonShipSize.GetComponent<Button>();
            button.navigation = navigation;
            var ship = lobby.buttonShipSize.GetComponent<Button>();
            navigation = ship.navigation;
            navigation.selectOnUp = button;
            ship.navigation = navigation;
            value = Text(root.transform, "Room size value", 23, Vector2.zero, new Vector2(330, 48));
            hint = Text(root.transform, "Room size description", 14, new Vector2(0, -53), new Vector2(340, 44));
            hint.alignment = TextAlignmentOptions.TopLeft;
            Refresh();
        }

        private TextMeshProUGUI Text(Transform parent, string name, float size, Vector2 position, Vector2 dimensions)
        {
            var child = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            child.transform.SetParent(parent, false);
            var text = child.GetComponent<TextMeshProUGUI>();
            text.font = lobby.shipSizeCarousel.label.font;
            text.fontSize = size;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            text.rectTransform.anchorMin = text.rectTransform.anchorMax = text.rectTransform.pivot = new Vector2(0, 1);
            text.rectTransform.anchoredPosition = position;
            text.rectTransform.sizeDelta = dimensions;
            return text;
        }

        internal void Change(int direction)
        {
            if (!button.interactable) return;
            RoomSizes.Setting.Value = (RoomSizes.Index + direction + RoomSizes.Names.Length) % RoomSizes.Names.Length;
            AudioManager.PlayUISound(UISound.CarrouselScroll);
            Refresh();
        }
        private void Update() { if (lobby != null) Refresh(); }
        private void Refresh()
        {
            bool enabled = Plugin.Local && !(bool)AccessTools.Field(typeof(PlayerSelectUI), "gameIsStarting").GetValue(lobby);
            if (shownIndex == RoomSizes.Index && shownEnabled == enabled) return;
            shownIndex = RoomSizes.Index; shownEnabled = enabled;
            button.interactable = enabled;
            value.text = "ROOM SIZE:  " + RoomSizes.Names[RoomSizes.Index] + "  >";
            hint.text = Plugin.Local ? "Left/right or confirm to change.\nRoom count follows ship size." : "Room size customization is for local games.";
        }
    }

    internal sealed class RoomSizeNavigation : MonoBehaviour, IMoveHandler
    {
        internal RoomSizeControl Control;
        public void OnMove(AxisEventData data)
        {
            if (data.moveDir == MoveDirection.Left || data.moveDir == MoveDirection.Right)
            {
                Control.Change(data.moveDir == MoveDirection.Left ? -1 : 1);
                data.Use();
            }
        }
    }
}
