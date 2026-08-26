using HarmonyLib;
using System.Linq;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;
using ZetasMoonTweaks;

[HarmonyPatch(typeof(NetworkManager))]
internal static class NetworkPrefabPatch2
{
    //private static readonly string modGUID = ZetasMoonDiscounts.ZMTBase.modGUID;

    [HarmonyPostfix]
    [HarmonyPatch(nameof(NetworkManager.SetSingleton))]
    private static void RegisterPrefab()
    {
        var prefab = new GameObject(PluginInfo.modGUID + " Prefab");
        prefab.hideFlags |= HideFlags.HideAndDontSave;
        Object.DontDestroyOnLoad(prefab);
        var networkObject = prefab.AddComponent<NetworkObject>();
        var fieldInfo = typeof(NetworkObject).GetField("GlobalObjectIdHash", BindingFlags.Instance | BindingFlags.NonPublic);
        fieldInfo!.SetValue(networkObject, GetHash(PluginInfo.modGUID));

        NetworkManager.Singleton.PrefabHandler.AddNetworkPrefab(prefab);
        return;

        static uint GetHash(string value)
        {
            return value?.Aggregate(17u, (current, c) => unchecked((current * 31) ^ c)) ?? 0u;
        }
    }
}
