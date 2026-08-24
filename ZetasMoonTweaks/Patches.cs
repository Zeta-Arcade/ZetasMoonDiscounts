using Dawn;
using Dawn.Utils;
using Dusk;
using HarmonyLib;
using LethalLevelLoader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;

namespace ZetasMoonTweaks
{
    internal class Patches
    {
        public static BepInEx.Logging.ManualLogSource Logger = ZMTBase.Logger;

        private static readonly MethodInfo? SetFromServerMethod = AccessTools.Method(typeof(ProgressivePredicate), "SetFromServer");

        private static void LockMoon(SelectableLevel level)
        {
            if (level.GetDawnInfo()?.DawnPurchaseInfo.PurchasePredicate is ProgressivePredicate progressive)
            {
                SetFromServerMethod?.Invoke(progressive, new object[] { false, progressive.ProgressiveStates.IsHidden });
                Logger.LogWarning($"Locked Dawn moon: {level.PlanetName}");
            }
        }
        [HarmonyPatch(typeof(GrabbableObject), nameof(GrabbableObject.ItemActivate))]
        public static class OxydeTabletActivatePatch
        {
            public static void Postfix(GrabbableObject __instance)
            {
                if (ZMTBase.Config.OxydeTabletForceUnlocksOxyde.Value)
                {
                    if (__instance.itemProperties == null) return;
                    if (__instance.itemProperties.itemName != "Oxyde Tablet") return;
                    ZMTNetwork.SendOxydeTablet(true);
                }
            }
        }
        [HarmonyPatch(typeof(Terminal))] 
        internal class TerminalPatch
        {
            [HarmonyPatch("Start")]
            [HarmonyPostfix]
            private static void TerminalStartPatch(ref Terminal __instance)
            {
                Logger.LogDebug($"Terminal patch detected");
                ZMTBase.Instance.displayedOxydeAlertForQuota = false;
                ZMTBase.Instance.SaveDefaultMoonPrices();
                ZMTBase.Instance.SaveDefaultMoonRiskLevels();
                ZMTBase.Instance.CalculateMoonPrices();
                ZMTBase.Instance.CalculateMoonRiskLevels();
                ZMTBase.Instance.SetOxydeCostFromConfigs();
            }
            [HarmonyPatch("BeginUsingTerminal")]
            [HarmonyPostfix]
            private static void TerminalBeginUsePatch(ref Terminal __instance)
            {
                Logger.LogDebug($"Terminal begin use patch detected");
                ZMTBase.Instance.CalculateMoonPrices();
                ZMTBase.Instance.CalculateMoonRiskLevels();
                ZMTBase.Instance.SetOxydeCostFromConfigs();
            }
            [HarmonyPatch("Update")]
            [HarmonyPostfix]
            static void gatherGroupCreditsValue(ref int ___groupCredits)
            {
                ZMTBase.Instance.groupCredits = ___groupCredits;
            }
        }
        [HarmonyPatch(typeof(StartOfRound))] // On Arrive at moon
        internal class StartOfRoundPatch
        {
            [HarmonyPatch("EndPlayersFiredSequenceClientRpc")]
            [HarmonyPostfix]
            private static void ResetHasRerouted()
            {
                ZMTBase.Instance.hasRoutedForThisQuota = false;
                ZMTBase.Instance.displayedOxydeAlertForQuota = false;
                ZMTBase.ModDataInstance.hasReroutedCompany = false;
                ZMTBase.ModDataInstance.hasReroutedMoonPair = false;
                ZMTBase.ModDataInstance.seedSetForQuota = false;
                ZMTBase.ModDataInstance.isOxydeOveride = false;
                ZMTBase.ModDataInstance.ZMTSaveData();
                if (ZMTBase.Config.RelockOxydeOnEject.Value) //If Enabled option to Relock Oxyde
                {
                    ExtendedLevel oxydeRef = PatchedContent.ExtendedLevels.Find(x => x.NumberlessPlanetName == "Oxyde");
                    if (oxydeRef == null) return;
                    LockMoon(oxydeRef.SelectableLevel);
                    Logger.LogInfo($"Oxyde is now re-locked!");
                }
            }
            [HarmonyPatch("PassTimeToNextDay")]
            [HarmonyPostfix]
            private static void PassTimeToNextDay()
            {
                Logger.LogDebug($"PassTimeToNextDay patch detected");
                ZMTBase.Instance.CalculateMoonPrices();
                ZMTBase.Instance.CalculateMoonRiskLevels();
                ZMTBase.Instance.SetOxydeCostFromConfigs();
            }
            [HarmonyPatch("ArriveAtLevel")]
            [HarmonyPostfix]
            private static void ArriveAtLevelPatch()
            {
                Logger.LogDebug($"ArriveAtLevel patch detected");
                ZMTBase.Instance.CalculateMoonPrices();
                ZMTBase.Instance.CalculateMoonRiskLevels();
                ZMTBase.Instance.SetOxydeCostFromConfigs();
            }
            [HarmonyPatch("Update")]
            [HarmonyPostfix]
            private static void AutoShipToCompanyBuilding(StartOfRound __instance)
            {
                if (!StartOfRound.Instance) return;
                if (!StartOfRound.Instance.currentLevel) return;
                if (!GameNetworkManager.Instance.isHostingGame)
                {
                    return;
                }
                if (ZMTBase.Config.AutoCompanyBuilding.Value && !ZMTBase.Instance.hasRoutedForThisQuota && StartOfRound.Instance.CanChangeLevels() && !ZMTBase.ModDataInstance.hasReroutedCompany && TimeOfDay.Instance.daysUntilDeadline == 0 && TimeOfDay.Instance.quotaFulfilled < TimeOfDay.Instance.profitQuota)
                {
                    Logger.LogDebug($"Trying to reroute ship");
                    ZMTBase.Instance.rerouteShip(GameNetworkManager.Instance, true);
                }
            }
            [HarmonyPatch("ShipHasLeft")]
            [HarmonyPostfix]
            private static void ShipHasLeftPatch()
            {
                if (ZMTBase.Config.DestroyOxydeSellScrapsInOrbit.Value)
                {
                    ZMTBase.Instance.CheckForScrapsToDestroy();
                }
            }
        }
        [HarmonyPatch(typeof(GameNetworkManager))]
        internal class GameNetworkManagerPatch
        {
            [HarmonyPatch("StartHost")]
            [HarmonyPostfix]
            static void StartsHostPatch()
            {
                if (!ZMTBase.ModDataInstance.seedSetForQuota)
                {
                    ZMTBase.Instance?.RollNewSeed();
                }
                ZMTBase.Instance.displayedOxydeAlertForQuota = false;
            }
            [HarmonyPatch("SaveGame")]
            [HarmonyPostfix]
            private static void SaveGameValuesPatch()
            {
                ZMTBase.Instance.CalculateMoonPrices();
                ZMTBase.Instance.CalculateMoonRiskLevels();
                ZMTBase.Instance.SetOxydeCostFromConfigs();
            }
            [HarmonyPatch("ResetSavedGameValues")]
            [HarmonyPostfix]
            private static void ResetSavedGameValuesPatch()
            {
                ZMTBase.Instance.CalculateMoonPrices();
                ZMTBase.Instance.CalculateMoonRiskLevels();
                ZMTBase.Instance.SetOxydeCostFromConfigs();
            }
        }
    }
    [HarmonyPatch(typeof(TimeOfDay))]
    internal class TimeOfDayPatch
    {
        [HarmonyPatch("SetNewProfitQuota")]
        [HarmonyPrefix]
        private static void SetNewProfitQuotaPatchPre()
        {
            ZMTBase.Instance.hasRoutedForThisQuota = false;
            ZMTBase.Instance.displayedOxydeAlertForQuota = false;
            ZMTBase.ModDataInstance.seedSetForQuota = false;
            ZMTBase.ModDataInstance.isOxydeOveride = false;
            ZMTBase.ModDataInstance.hasReroutedCompany = false;
            ZMTBase.ModDataInstance.ZMTSaveData();
            ZMTBase.Instance.RollNewSeed();
            ZMTBase.Instance.CalculateMoonPrices();
            ZMTBase.Instance.CalculateMoonRiskLevels();
            ZMTBase.Instance.SetOxydeCostFromConfigs();
        }
        [HarmonyPostfix]
        [HarmonyPatch("SetBuyingRateForDay")]
        private static void SetBuyingRateForDayPatch()
        {
            ZMTBase.Instance.ModifyBuyingRate();
        }
        [HarmonyPostfix]
        [HarmonyPatch("SetNewProfitQuota")]
        private static void SetNewProfitQuotaPatchPost()
        {
            ZMTBase.Instance.ModifyBuyingRate();
        }
        [HarmonyPostfix]
        [HarmonyPatch("UpdateProfitQuotaCurrentTime")]
        private static void UpdateProfitQuotaCurrentTimePatch()
        {
            if (!StartOfRound.Instance) return;
            if (!StartOfRound.Instance.currentLevel) return;
            if (!GameNetworkManager.Instance.isHostingGame)
            {
                return;
            }
            if (ZMTBase.Config.IronmanAutoRoute.Value && ZMTBase.Config.IronmanMode.Value && StartOfRound.Instance.CanChangeLevels() && !ZMTBase.ModDataInstance.hasReroutedMoonPair && TimeOfDay.Instance.quotaFulfilled < TimeOfDay.Instance.profitQuota)
            {
                ZMTBase.Instance.rerouteShip(GameNetworkManager.Instance, false);
            }
        }
    }
}
