using HarmonyLib;
using MrovLib;
using System.Linq;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;
using ZetasMoonDiscounts;

[HarmonyPatch(typeof(NetworkManager))]
internal static class NetworkPrefabPatch2
{
    //private static readonly string modGUID = ZetasMoonDiscounts.ZetasMoonDiscountsBase.modGUID;

    [HarmonyPostfix]
    [HarmonyPatch(nameof(NetworkManager.SetSingleton))]
    private static void RegisterPrefab()
    {
        var prefab = new GameObject(ZetasMoonDiscountsBase.GUID + " Prefab");
        prefab.hideFlags |= HideFlags.HideAndDontSave;
        Object.DontDestroyOnLoad(prefab);
        var networkObject = prefab.AddComponent<NetworkObject>();
        var fieldInfo = typeof(NetworkObject).GetField("GlobalObjectIdHash", BindingFlags.Instance | BindingFlags.NonPublic);
        fieldInfo!.SetValue(networkObject, GetHash(ZetasMoonDiscountsBase.GUID));

        NetworkManager.Singleton.PrefabHandler.AddNetworkPrefab(prefab);
        return;

        static uint GetHash(string value)
        {
            return value?.Aggregate(17u, (current, c) => unchecked((current * 31) ^ c)) ?? 0u;
        }
    }
}
