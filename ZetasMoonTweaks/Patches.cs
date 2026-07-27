using HarmonyLib;
using LethalLevelLoader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZetasMoonTweaks
{
    internal class Patches
    {
        public static BepInEx.Logging.ManualLogSource Logger = ZMTBase.Logger;
        [HarmonyPatch(typeof(Terminal), "Start")] //On Lobby Start, but isn't working currently
        internal class TerminalPatch
        {
            [HarmonyPatch("Start")]
            [HarmonyPostfix]
            private static void TerminalStartPatch(ref Terminal __instance)
            {
                Logger.LogDebug($"Terminal patch detected");
                ZMTBase.Instance.SaveDefaultMoonPrices();
                ZMTBase.Instance.SaveDefaultMoonRiskLevels();
                ZMTBase.Instance.CalculateMoonPrices();
                ZMTBase.Instance.CalculateMoonRiskLevels();
            }
            [HarmonyPatch("BeginUsingTerminal")]
            [HarmonyPostfix]
            private static void TerminalBeginUsePatch(ref Terminal __instance)
            {
                Logger.LogDebug($"Terminal begin use patch detected");
                ZMTBase.Instance.CalculateMoonPrices();
                ZMTBase.Instance.CalculateMoonRiskLevels();
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
            private static GameNetworkManager ___gameNetworkManager = GameNetworkManager.Instance;

            [HarmonyPatch("EndPlayersFiredSequenceClientRpc")]
            [HarmonyPostfix]
            private static void ResetHasRerouted()
            {
                Logger.LogWarning($"EndPlayersFiredSequenceClientRpc Fired!");
                ZMTBase.Instance.hasReroutedCompany = false;
                ZMTBase.Instance.hasReroutedMoonPair = false;
                ExtendedLevel oxydeRef = PatchedContent.ExtendedLevels.Find(x => x.NumberlessPlanetName == "Oxyde");
                if (oxydeRef == null) return;
                if (!ZMTBase.Config.IronmanMode.Value) //If not Ironman Mode
                {
                    if (!oxydeRef.IsRouteLocked) //If its not locked
                    {
                        Logger.LogWarning($"Locking Oxyde!");
                        oxydeRef.IsRouteLocked = true;
                        oxydeRef.IsRouteHidden = true;
                        oxydeRef.ForceSetRoutePrice(oxydeRef.RoutePrice);
                        Logger.LogWarning($"{oxydeRef.ModName}" + " is where Oxyde was found");
                    }
                    else
                    {
                        Logger.LogWarning($"Oxyde is already locked / Its Ironman Mode!");
                    }
                }
            }
            [HarmonyPatch("PassTimeToNextDay")]
            [HarmonyPostfix]
            private static void PassTimeToNextDay()
            {
                Logger.LogDebug($"PassTimeToNextDay patch detected");
                ZMTBase.Instance.CalculateMoonPrices();
                ZMTBase.Instance.CalculateMoonRiskLevels();
            }
            [HarmonyPatch("ArriveAtLevel")]
            [HarmonyPostfix]
            private static void ArriveAtLevelPatch()
            {
                Logger.LogDebug($"ArriveAtLevel patch detected");
                ZMTBase.Instance.CalculateMoonPrices();
                ZMTBase.Instance.CalculateMoonRiskLevels();
            }
            [HarmonyPatch("Update")]
            [HarmonyPostfix]
            private static void AutoShipToCompanyBuilding(StartOfRound __instance)
            {
                if (ZMTBase.Config.IronmanAutoRoute.Value && ZMTBase.Config.IronmanMode.Value)
                {
                    ZMTBase.Instance.rerouteShip(___gameNetworkManager, true);
                }
            }
        }
        [HarmonyPatch(typeof(GameNetworkManager))]
        internal class GameNetworkManagerPatch
        {
            [HarmonyPatch("SaveGame")]
            [HarmonyPostfix]
            private static void SaveGameValuesPatch()
            {
                ZMTBase.Instance.CalculateMoonPrices();
                ZMTBase.Instance.CalculateMoonRiskLevels();
            }
            [HarmonyPatch("ResetSavedGameValues")]
            [HarmonyPostfix]
            private static void ResetSavedGameValuesPatch()
            {
                ZMTBase.Instance.CalculateMoonPrices();
                ZMTBase.Instance.CalculateMoonRiskLevels();
            }
        }
        [HarmonyPatch(typeof(TimeOfDay))]
        internal class TimeOfDayPatch
        {
            private static GameNetworkManager ___gameNetworkManager = GameNetworkManager.Instance;

            [HarmonyPatch("SetNewProfitQuota")]
            [HarmonyPrefix]
            private static void SetNewProfitQuotaPatch()
            {
                ZMTBase.Instance.CalculateMoonPrices();
                ZMTBase.Instance.CalculateMoonRiskLevels();
            }
            [HarmonyPostfix]
            [HarmonyPatch("SetBuyingRateForDay")]
            private static void setBuyingRateForDayPatch()
            {
                ZMTBase.Instance.ModifyBuyingRate();
            }
            [HarmonyPostfix]
            [HarmonyPatch("SetNewProfitQuota")]
            private static void setNewProfitQuotaPatch()
            {
                ZMTBase.Instance.ModifyBuyingRate();
            }
            [HarmonyPostfix]
            [HarmonyPatch("UpdateProfitQuotaCurrentTime")]
            private static void updateProfitQuotaCurrentTimePatch()
            {
                if (ZMTBase.Config.IronmanAutoRoute.Value && ZMTBase.Config.IronmanMode.Value)
                {
                    ZMTBase.Instance.rerouteShip(___gameNetworkManager, false);
                }
            }
        }
    }
}
