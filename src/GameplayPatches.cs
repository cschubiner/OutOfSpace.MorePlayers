using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using BeholdStudios.OutOfSpace.UI;

namespace OutOfSpace.MorePlayers
{
    internal static class Palette
    {
        private static readonly Color[] Extra = {
            new Color(0.1f, 0.85f, 0.92f), new Color(0.9f, 0.25f, 0.62f),
            new Color(0.96f, 0.6f, 0.12f), new Color(0.82f, 0.86f, 0.94f)
        };
        internal static Color ColorFor(int index) => index < 8 ? Extra[(index - 4) % 4] : Color.HSVToRGB((index * .618034f) % 1, .65f, .95f);
        internal static void Expand(List<Color> colors)
        {
            while (colors.Count < Plugin.Limit) colors.Add(ColorFor(Math.Max(4, colors.Count)));
        }
        internal static void Expand(List<Material> materials)
        {
            while (materials.Count < Plugin.Limit)
            {
                int i = materials.Count;
                var m = new Material(materials[i % 4]);
                m.name = "MorePlayers P" + (i + 1);
                m.color = ColorFor(Math.Max(4, i));
                materials.Add(m);
            }
        }
    }

    [HarmonyPatch(typeof(PlayerCharacter), "IdentifyPlayer")]
    internal static class CharacterPalette
    {
        private static void Prefix(PlayerCharacter __instance)
        {
            if (!Plugin.Local) return;
            Palette.Expand(__instance.playerIdMaterials);
            Palette.Expand(__instance.playerIdColors);
            Palette.Expand(__instance.lightColorDualshock);
        }
    }

    [HarmonyPatch(typeof(UIManager), "Awake")]
    internal static class ShopPalette
    {
        private static void Postfix(UIManager __instance)
        {
            var ui = __instance.GetComponentInChildren<ShopUI>(true);
            if (ui == null) return;
            var colors = ui.playerColors.ToList();
            Palette.Expand(colors);
            ui.playerColors = colors.ToArray();
        }
    }

    [HarmonyPatch]
    internal static class LocalCharacterId
    {
        private static MethodBase TargetMethod() => typeof(PlayerCharacter).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(m => m.Name.EndsWith(".OnPhotonInstantiate"));
        private static void Prefix(PlayerCharacter __instance)
        {
            if (Plugin.Local && __instance.photonView.IsMine)
                __instance.playerIDLocal = ((PlayerData)__instance.photonView.InstantiationData[0]).ControllerId;
        }
    }

    [HarmonyPatch(typeof(StaminaUI), "Awake")]
    internal static class StaminaSlots
    {
        private static void Prefix(StaminaUI __instance)
        {
            var bars = __instance.GetComponentsInChildren<StaminaBar>(true).ToList();
            if (bars.Count == 0) return;
            while (bars.Count < Plugin.Limit)
            {
                var bar = UnityEngine.Object.Instantiate(bars[0], bars[0].transform.parent);
                bar.name = "MorePlayers Stamina " + (bars.Count + 1);
                bars.Add(bar);
            }
        }
    }

    [HarmonyPatch(typeof(PlayerSpawner), "Awake")]
    internal static class SpawnSlots
    {
        private static void Postfix(PlayerSpawner __instance)
        {
            if (!Plugin.Local) return;
            int original = __instance.playerInstantiatePos.Count;
            if (original == 0) throw new InvalidOperationException("No stock spawn positions found.");
            // Reuse proven safe spawn locations; stock players also spawn together on a local machine.
            while (__instance.playerInstantiatePos.Count < Plugin.Limit)
                __instance.playerInstantiatePos.Add(__instance.playerInstantiatePos[__instance.playerInstantiatePos.Count % original]);
        }
    }

    [HarmonyPatch(typeof(NetworkConnect), "Connect")]
    internal static class LocalOnlyGuard
    {
        private static readonly HashSet<int> Pending = new HashSet<int>();
        private static bool Prefix(NetworkConnect __instance, ref bool online, bool isPrivate)
        {
            if (online && MultiplayerManager.instance != null && MultiplayerManager.instance.LocalPlayerDatas.Count > 4)
            {
                online = false;
                Plugin.Log.LogWarning("Groups larger than four use local play. Online expansion is unsupported.");
            }
            if (!online && PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
            {
                if (Pending.Add(__instance.GetInstanceID()))
                    __instance.StartCoroutine(ConnectAfterDisconnect(__instance, isPrivate));
                return false;
            }
            return true;
        }
        private static IEnumerator ConnectAfterDisconnect(NetworkConnect manager, bool isPrivate)
        {
            int key = manager.GetInstanceID();
            try
            {
                AccessTools.Method(typeof(NetworkConnect), "UpdateNetworkStatus").Invoke(manager, new object[] { NetworkStatus.GoingOffline });
                PhotonNetwork.Disconnect();
                float deadline = Time.realtimeSinceStartup + 10f;
                while (PhotonNetwork.IsConnected && Time.realtimeSinceStartup < deadline) yield return null;
                if (PhotonNetwork.IsConnected)
                    Plugin.Log.LogError("Timed out disconnecting background Photon connection; select Ready again.");
                else if (manager != null && MultiplayerManager.instance != null && MultiplayerManager.instance.LocalPlayerDatas.Count > 0)
                    manager.Connect(false, isPrivate);
            }
            finally { Pending.Remove(key); }
        }
    }

    [HarmonyPatch(typeof(NetworkConnect), "CreateRoom")]
    internal static class OfflineCapacity
    {
        private static void Postfix()
        {
            if (PhotonNetwork.OfflineMode && PhotonNetwork.CurrentRoom != null)
                PhotonNetwork.CurrentRoom.MaxPlayers = (byte)Plugin.Limit;
        }
    }

    [HarmonyPatch(typeof(PlayerSelectUI), "AddLocalPlayer")]
    internal static class JoinGuard
    {
        private static bool Prefix(PlayerSelectUI __instance, Rewired.Player rewiredPlayer)
        {
            return rewiredPlayer != null && __instance.players.Count < Math.Min(Plugin.LocalLimit(), __instance.playerUIs.Count)
                && !MultiplayerManager.instance.LocalPlayerDatas.Any(p => p.ControllerId == rewiredPlayer.id);
        }
    }
}
