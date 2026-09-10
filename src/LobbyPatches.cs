using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace OutOfSpace.MorePlayers
{
    [HarmonyPatch(typeof(PlayerSelectUI), "OnEnable")]
    internal static class LobbySlots
    {
        private static void Prefix(PlayerSelectUI __instance)
        {
            if (Plugin.Limit <= 4 || __instance.playerUIs.Count >= Plugin.Limit) return;
            Palette.Expand(__instance.playerMaterials);
            var template = __instance.playerUIs[0];
            while (__instance.playerUIs.Count < Plugin.Limit)
                __instance.playerUIs.Add(Clone(template, __instance.playerUIs.Count, __instance));
            var layout = __instance.gameObject.AddComponent<ExpandedLobbyLayout>();
            layout.Initialize(__instance);
            Plugin.Log.LogInfo("Expanded lobby to " + __instance.playerUIs.Count + " complete UI/model slots.");
        }

        private static PlayerSelectUI.PlayertUI Clone(PlayerSelectUI.PlayertUI source, int index, PlayerSelectUI lobby)
        {
            var map = new Dictionary<Object, Object>();
            var panel = Object.Instantiate(source.playerSelectUI, source.playerSelectUI.transform.parent);
            panel.name = "MorePlayers Lobby P" + (index + 1);
            MapTree(source.playerSelectUI.transform, panel.transform, map);
            var model = Object.Instantiate(source.playerModel, source.playerModel.transform.parent);
            model.name = "MorePlayers Model P" + (index + 1);
            MapTree(source.playerModel.transform, model.transform, map);
            var result = new PlayerSelectUI.PlayertUI();
            foreach (var field in typeof(PlayerSelectUI.PlayertUI).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                var val = field.GetValue(source);
                if (val is Object)
                {
                    var obj = (Object)val;
                    if (!map.ContainsKey(obj)) throw new InvalidOperationException("Unmapped lobby reference " + field.Name);
                    field.SetValue(result, map[obj]);
                }
                else if (val is List<Renderer>)
                    field.SetValue(result, ((List<Renderer>)val).Select(r => (Renderer)map[r]).ToList());
            }
            // Player 1 is the template: its arrows are driven by per-player input, and it has no kick callback to copy.
            result.PlayerId = string.Empty;
            result.ShowUIElements(false);
            BindArrow(result.leftArrow, result, lobby, "PreviousHead");
            BindArrow(result.rightArrow, result, lobby, "NextHead");
            return result;
        }

        private static void BindArrow(GameObject arrow, PlayerSelectUI.PlayertUI slot, PlayerSelectUI lobby, string method)
        {
            if (arrow == null) return;
            foreach (var button in arrow.GetComponentsInChildren<Button>(true))
            {
                button.onClick = new Button.ButtonClickedEvent();
                button.onClick.AddListener(() => {
                    var player = lobby.players.FirstOrDefault(p => p.Id == slot.PlayerId);
                    if (player != null) AccessTools.Method(typeof(PlayerSelectUI), method).Invoke(lobby, new object[] { player.ControllerId });
                });
            }
        }

        private static void MapTree(Transform source, Transform target, Dictionary<Object, Object> map)
        {
            map[source.gameObject] = target.gameObject;
            var a = source.GetComponents<Component>();
            var b = target.GetComponents<Component>();
            for (int i = 0; i < a.Length; i++) if (a[i] != null) map[a[i]] = b[i];
            for (int i = 0; i < source.childCount; i++) MapTree(source.GetChild(i), target.GetChild(i), map);
        }
    }

    internal sealed class ExpandedLobbyLayout : MonoBehaviour
    {
        private PlayerSelectUI lobby;
        private Vector3[] panelPositions;
        private Vector3[] modelPositions;
        private Vector3[] modelScales;
        private Renderer[] scenery;
        private bool[] sceneryEnabled;
        private bool? expanded;
        internal void Initialize(PlayerSelectUI value)
        {
            lobby = value;
            panelPositions = lobby.playerUIs.Select(s => s.playerSelectUI.transform.localPosition).ToArray();
            modelPositions = lobby.playerUIs.Select(s => s.playerModel.transform.localPosition).ToArray();
            modelScales = lobby.playerUIs.Select(s => s.playerModel.transform.localScale).ToArray();
            var truck = lobby.playerUIs[0].playerModel.transform.parent.parent.parent;
            scenery = truck.GetComponentsInChildren<Renderer>(true).Where(r =>
                !lobby.playerUIs.Any(s => r.transform.IsChildOf(s.playerModel.transform))).ToArray();
            sceneryEnabled = scenery.Select(r => r.enabled).ToArray();
            var mode = lobby.offlineModeText.rectTransform;
            mode.SetParent(lobby.transform, false);
            mode.anchorMin = mode.anchorMax = new Vector2(0, 1);
            mode.pivot = new Vector2(0, 1);
            mode.anchoredPosition = new Vector2(32, -76);
            mode.sizeDelta = new Vector2(350, 35);
            lobby.offlineModeText.alignment = TMPro.TextAlignmentOptions.Left;
        }
        private void LateUpdate()
        {
            if (lobby == null) return;
            bool active = Plugin.Local;
            if (expanded != active)
            {
            expanded = active;
            for (int r = 0; r < scenery.Length; r++) if (scenery[r] != null) scenery[r].enabled = !active && sceneryEnabled[r];
            for (int i = 0; i < lobby.playerUIs.Count; i++)
            {
                var slot = lobby.playerUIs[i];
                slot.playerSelectUI.SetActive(active || i < 4);
                if (active)
                {
                    int col = i % 4, row = i / 4;
                    int rows = (Plugin.Limit + 3) / 4;
                    slot.playerSelectUI.transform.localPosition = new Vector3(45 + col * 105, 100 - row * (330f / rows), 0);
                    slot.playerSelectUI.transform.localScale = Vector3.one * .8f;
                    slot.playerModel.transform.localPosition = new Vector3(-2.4f + col * 1.1f, 1.1f, -3.9f + row * 1.25f);
                    slot.playerModel.transform.localScale = modelScales[i] * .72f;
                }
                else
                {
                    slot.playerSelectUI.transform.localPosition = panelPositions[i];
                    slot.playerSelectUI.transform.localScale = Vector3.one;
                    slot.playerModel.transform.localPosition = modelPositions[i];
                    slot.playerModel.transform.localScale = modelScales[i];
                }
            }
            }
            if (active)
            {
                FrameModels();
                lobby.photonConnectedPlayersText.gameObject.SetActive(false);
                lobby.waitingTimeText.gameObject.SetActive(false);
            }
        }

        private void FrameModels()
        {
            var camera = Camera.main;
            if (camera == null) return;
            var canvas = lobby.GetComponentInParent<Canvas>();
            var uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            float depth = 2f;
            for (int i = 0; i < lobby.playerUIs.Count; i++)
            {
                var slot = lobby.playerUIs[i];
                if (!slot.playerModel.activeInHierarchy) continue;
                var renderers = slot.playerModel.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
                if (renderers.Length == 0) continue;
                Bounds bounds = renderers[0].bounds;
                foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                int rows = (Plugin.Limit + 3) / 4;
                float wantedPixels = Screen.height * (.40f / rows);
                float worldHeight = camera.orthographic ? camera.orthographicSize * 2 : 2 * depth * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad / 2);
                float factor = worldHeight * wantedPixels / Screen.height / Mathf.Max(.001f, bounds.size.y);
                slot.playerModel.transform.localScale *= factor;
                bounds = renderers[0].bounds;
                foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                var label = RectTransformUtility.WorldToScreenPoint(uiCamera, slot.confirmLabel.transform.position);
                Vector3 target = camera.ScreenToWorldPoint(new Vector3(label.x, label.y - Screen.height * (.27f / rows), depth));
                slot.playerModel.transform.position += target - bounds.center;
            }
        }
    }

    [HarmonyPatch]
    internal static class OnlineLobbyGuard
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var name in new[] { "StartMatchmaking", "MakePublic", "InviteFriend" })
                yield return AccessTools.Method(typeof(PlayerSelectUI), name);
        }
        private static bool Prefix(PlayerSelectUI __instance)
        {
            if (__instance.players.Count <= 4) return true;
            AccessTools.Method(typeof(PlayerSelectUI), "ShowNotification").Invoke(__instance,
                new object[] { "More than four players requires a local game. Select Ready to play together." });
            return false;
        }
    }
}
