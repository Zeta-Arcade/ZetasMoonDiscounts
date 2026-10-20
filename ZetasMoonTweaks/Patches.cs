using BepInEx.Bootstrap;
using BepInEx.Logging;
using Dawn;
using DunGen;
using Dusk;
using HarmonyLib;
using LethalLevelLoader;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Mail;
using Unity.Netcode;
using UnityEngine;

namespace ZetasMoonTweaks
{
    internal class Patches
    {
        public static BepInEx.Logging.ManualLogSource Logger = ZMTBase.Logger;

        private delegate void SetFromServerDelegate(ProgressivePredicate instance, bool isUnlocked, bool isHidden);

        private static readonly SetFromServerDelegate? SetFromServer = AccessTools.MethodDelegate<SetFromServerDelegate>(AccessTools.Method(typeof(ProgressivePredicate), "SetFromServer"));

        private static void LockMoon(SelectableLevel level)
        {
            ProgressivePredicate progressive = level.GetDawnInfo()?.DawnPurchaseInfo.PurchasePredicate as ProgressivePredicate;
            if (progressive == null) return;
            if (SetFromServer == null)
            {
                Logger.LogError("Could not find ProgressivePredicate.SetFromServer!");
                return;
            }
            SetFromServer(progressive, false, progressive.ProgressiveStates.IsHidden);
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
            [HarmonyPatch("SetItemSales")]
            [HarmonyPostfix]
            [HarmonyPriority(Priority.Last)]
            private static void SetItemSalesPatch(Terminal __instance)
            {
                // First day of the first quota
                if (ZMTBase.Config.noFirstDayShopSales.Value)
                {
                    if (TimeOfDay.Instance.timesFulfilledQuota < 1 && TimeOfDay.Instance.daysUntilDeadline >= 3)
                    {
                        for (int i = 0; i < __instance.itemSalesPercentages.Length; i++)
                        {
                            __instance.itemSalesPercentages[i] = 100;
                        }
                    }
                }
            }
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
        [HarmonyPatch(typeof(EntranceTeleport))]
        internal class InteriorNamePatch
        {
            public static bool shownToday = false;
            private static int lastShownDay = -1;
            [HarmonyPatch("TeleportPlayerClientRpc")]
            [HarmonyPostfix]
            private static void TeleportPlayerClientRPCPatch(EntranceTeleport __instance, int playerObj)
            {
                if (!ZMTBase.Config.interiorNamePopup.Value) return;
                if (!__instance.isEntranceToBuilding) return;
                if (StartOfRound.Instance == null) return;
                int currentDay = StartOfRound.Instance.gameStats.daysSpent;
                if (lastShownDay != currentDay)
                {
                    shownToday = false;
                    lastShownDay = currentDay;
                    Logger.LogDebug($"InteriorName: Reset popup for day {currentDay}");
                }
                if (shownToday) return;
                if (GameNetworkManager.Instance == null) return;
                if (GameNetworkManager.Instance.localPlayerController == null) return;
                if (__instance.playersManager == null) return;
                if (__instance.playersManager.allPlayerScripts == null) return;
                if (playerObj < 0 || playerObj >= __instance.playersManager.allPlayerScripts.Length) return;
                if (__instance.playersManager.allPlayerScripts[playerObj] != GameNetworkManager.Instance.localPlayerController)
                {
                    return;
                }
                Logger.LogDebug($"InteriorName: Local player entered through entrance");
                ZMTBase.Instance.StartCoroutine(ShowInteriorPopupDelayed(__instance));
            }
            private static void HandlePopup(EntranceTeleport __instance)
            {
                if (!__instance.isEntranceToBuilding) return;
                if (shownToday) return;
                string interiorName = Utils.GetCurrentInteriorName();
                if (string.IsNullOrEmpty(interiorName))
                {
                    Logger.LogWarning($"InteriorName: Could not determine the current interior name!");
                    return;
                }
                Logger.LogInfo($"Interior default name is {interiorName}");
                string[] entries = ZMTBase.Config.interiorsAndOverrides.Value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries); //E.g. Level1Flow@Facility;Level2Flow@Mansion --> Level1Flow@Facility
                bool foundinteriorName = false;
                foreach (string entry in entries)
                {
                    string[] pairs = entry.Split(new[] { '@' }, StringSplitOptions.RemoveEmptyEntries); //Split them again, E.g. Level1Flow@Facility --> Level1Flow, Facility
                    if (pairs.Length == 2 && pairs[0].Trim().Equals(interiorName, StringComparison.OrdinalIgnoreCase))
                    {
                        interiorName = pairs[1].Trim();
                        foundinteriorName = true;
                        Logger.LogWarning($"Interior name found in config");
                        break;
                    }
                }
                if (ZMTBase.Config.fixInteriorNames.Value && !foundinteriorName) //Don't mess with the name if we already found it in the config
                {
                    interiorName = Utils.CleanInteriorName(interiorName);
                    Logger.LogInfo($"Interior name is now {interiorName}");
                }
                string chanceAppend = "";
                if (ZMTBase.Config.addPercentageInfo.Value)
                {
                    DawnDungeonInfo? dungeonInfo = Utils.GetCurrentDungeonInfo();
                    float interiorChance = Utils.GetInteriorChance(dungeonInfo);
                    if (interiorChance >= 0f)
                    {
                        chanceAppend = $"(Interior Chance: {interiorChance:0.##}%)";
                    }
                }
                Logger.LogInfo($"InteriorName: Displaying popup for {interiorName}");
                HUDManager.Instance.DisplayTip(ZMTBase.Config.interiorTitlePopup.Value, $"{ZMTBase.Config.interiorNamePrefix.Value} {interiorName} {chanceAppend}", false, false, "LC_Tip1");
                shownToday = true;
            }
            private static IEnumerator ShowInteriorPopupDelayed(EntranceTeleport entrance)
            {
                // Give the client a moment to finish teleporting
                yield return new WaitForSeconds(0.2f);
                if (shownToday) yield break;
                try
                {
                    HandlePopup(entrance);
                }
                catch (Exception ex)
                {
                    Logger.LogError($"InteriorName: Error displaying popup: {ex}");
                }
            }
        }
        [HarmonyPatch(typeof(StartOfRound))] // On Arrive at moon
        internal class StartOfRoundPatch
        {
            private static int lastAutoRoutedDay = -1;
            private static bool hasRequestedRandomMoons = false;
            private static bool isRequestingRandomMoons = false;
            internal static void RandomMoonsSyncReceived()
            {
                hasRequestedRandomMoons = true;
            }
            internal static void ResetRandomMoonsSync()
            {
                hasRequestedRandomMoons = false;
            }
            [HarmonyPatch("StartGame")]
            [HarmonyPrefix]
            private static void Prefix()
            {
                InteriorNamePatch.shownToday = false;
            }
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
                if (ZMTBase.Config.gamemode.Value == ConfigHandler.Gamemode.DayIronman && ZMTBase.Config.IronmanAutoRoute.Value && GameNetworkManager.Instance != null && GameNetworkManager.Instance.isHostingGame && StartOfRound.Instance != null && StartOfRound.Instance.CanChangeLevels() && TimeOfDay.Instance.quotaFulfilled < TimeOfDay.Instance.profitQuota)
                {
                    ZMTBase.ModDataInstance.hasReroutedMoonPair = false;
                    ZMTBase.Instance.rerouteShip(GameNetworkManager.Instance, false);
                }
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
                if (GameNetworkManager.Instance == null) return;
                if (!GameNetworkManager.Instance.isHostingGame) return;
                if (TimeOfDay.Instance == null) return;
                if (!StartOfRound.Instance.CanChangeLevels()) return;
                bool shouldGoToCompany = ZMTBase.Config.AutoCompanyBuilding.Value && TimeOfDay.Instance.daysUntilDeadline == 0 && TimeOfDay.Instance.quotaFulfilled < TimeOfDay.Instance.profitQuota;
                //Auto Company
                if (ZMTBase.Config.AutoCompanyBuilding.Value && !ZMTBase.Instance.hasRoutedForThisQuota && !ZMTBase.ModDataInstance.hasReroutedCompany && TimeOfDay.Instance.daysUntilDeadline == 0 && TimeOfDay.Instance.quotaFulfilled < TimeOfDay.Instance.profitQuota)
                {
                    Logger.LogDebug($"Trying to reroute ship");
                    ZMTBase.Instance.rerouteShip(GameNetworkManager.Instance, true);
                }
                //Auto next moon if its Day Ironman, but not if we should route to the company
                if (!shouldGoToCompany && ZMTBase.Config.gamemode.Value == ConfigHandler.Gamemode.DayIronman && ZMTBase.Config.IronmanAutoRoute.Value && TimeOfDay.Instance.quotaFulfilled < TimeOfDay.Instance.profitQuota)
                {
                    int currentDay = StartOfRound.Instance.gameStats.daysSpent + 1 - TimeOfDay.Instance.timesFulfilledQuota;
                    if (lastAutoRoutedDay != currentDay)
                    {
                        Logger.LogDebug($"DayIronman attempting automatic routing for day {currentDay + 1}");
                        ZMTBase.ModDataInstance.hasReroutedMoonPair = false;
                        ZMTBase.Instance.rerouteShip(GameNetworkManager.Instance, false);
                        lastAutoRoutedDay = currentDay;
                    }
                }
            }
            [HarmonyPatch("Update")]
            [HarmonyPostfix]
            private static void RequestRandomMoonsIfNeeded()
            {
                if (ZMTBase.Config.gamemode.Value != ConfigHandler.Gamemode.RandomMoons) return;
                if (!NetworkManager.Singleton.IsConnectedClient) return;
                if (NetworkManager.Singleton.IsHost) return;
                if (ZMTBase.Instance == null) return;
                if (hasRequestedRandomMoons) return;
                if (isRequestingRandomMoons) return;
                ZMTBase.Instance.StartCoroutine(RequestRandomMoonsCoroutine());
            }
            private static IEnumerator RequestRandomMoonsCoroutine()
            {
                isRequestingRandomMoons = true;
                while (!hasRequestedRandomMoons)
                {
                    if (!NetworkManager.Singleton.IsConnectedClient || NetworkManager.Singleton.IsHost)
                    {
                        break;
                    }
                    ZMTNetwork.RequestRandomMoons();
                    yield return new WaitForSecondsRealtime(3f);
                }
                isRequestingRandomMoons = false;
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
            [HarmonyPatch("Start")]
            [HarmonyPostfix]
            private static void StartOfRoundStartPatch()
            {
                if (ZMTBase.Config.gamemode.Value != ConfigHandler.Gamemode.RandomMoons) return;
                if (!NetworkManager.Singleton.IsListening) return;
                if (NetworkManager.Singleton.IsHost) return;
                Logger.LogDebug($"Random Moons client joined, requesting selection");
                ZMTNetwork.RequestRandomMoons();
            }
        }
        [HarmonyPatch(typeof(GameNetworkManager))]
        internal class GameNetworkManagerPatch
        {
            [HarmonyPatch("StartHost")]
            [HarmonyPostfix]
            static void StartsHostPatch()
            {
                ZMTBase.Instance.hasRoutedForThisQuota = false;
                ZMTBase.ModDataInstance.hasReroutedCompany = false;
                ZMTBase.ModDataInstance.hasReroutedMoonPair = false;
                if (!ZMTBase.ModDataInstance.seedSetForQuota)
                {
                    ZMTBase.Instance?.RollNewSeed();
                }
                ZMTBase.Instance.displayedOxydeAlertForQuota = false;
                if (ZMTBase.Config.gamemode.Value == ConfigHandler.Gamemode.RandomMoons)
                {
                    ZMTBase.Instance.LoadRandomMoonsFromSave();
                }
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
                ZMTBase.ModDataInstance.hasReroutedMoonPair = false;
                ZMTBase.ModDataInstance.ZMTSaveData();
                ZMTBase.Instance.RollNewSeed();
                if (ZMTBase.Config.gamemode.Value != ConfigHandler.Gamemode.RandomMoons)
                {
                    ZMTBase.Instance.CalculateMoonPrices();
                }
                ZMTBase.Instance.CalculateMoonRiskLevels();
            }
            [HarmonyPostfix]
            [HarmonyPatch("SetNewProfitQuota")]
            private static void SetNewProfitQuotaPatchPost()
            {
                ZMTBase.Instance.SetOxydeCostFromConfigs();
                if (ZMTBase.Config.gamemode.Value == ConfigHandler.Gamemode.RandomMoons)
                {
                    if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost)
                    {
                        Logger.LogDebug("Random Moons new quota detected");
                        ZMTBase.Instance.RollRandomMoons();
                    }
                    else
                    {
                        StartOfRoundPatch.ResetRandomMoonsSync();
                    }
                    ZMTBase.Instance.CalculateMoonPrices();
                }
            }
            [HarmonyPostfix]
            [HarmonyPatch("SetBuyingRateForDay")]
            private static void SetBuyingRateForDayPatch()
            {
                ZMTBase.Instance.ModifyBuyingRate();
            }
            [HarmonyPostfix]
            [HarmonyPatch("UpdateProfitQuotaCurrentTime")]
            private static void UpdateProfitQuotaCurrentTimePatch()
            {
                if (!StartOfRound.Instance) return;
                if (!StartOfRound.Instance.currentLevel) return;
                if (GameNetworkManager.Instance == null) return;
                if (!GameNetworkManager.Instance.isHostingGame) return;
                if (TimeOfDay.Instance == null) return;
                bool shouldAutoRoute = ZMTBase.Config.gamemode.Value == ConfigHandler.Gamemode.QuotaIronman || ZMTBase.Config.gamemode.Value == ConfigHandler.Gamemode.SMHQ || ZMTBase.Config.gamemode.Value == ConfigHandler.Gamemode.RandomMoons;
                bool shouldGoToCompany = ZMTBase.Config.AutoCompanyBuilding.Value && TimeOfDay.Instance.daysUntilDeadline == 0 && TimeOfDay.Instance.quotaFulfilled < TimeOfDay.Instance.profitQuota;
                if (ZMTBase.Config.IronmanAutoRoute.Value && shouldAutoRoute && !shouldGoToCompany && StartOfRound.Instance.CanChangeLevels() && !ZMTBase.ModDataInstance.hasReroutedMoonPair && TimeOfDay.Instance.quotaFulfilled < TimeOfDay.Instance.profitQuota)
                {
                    Logger.LogDebug($"Attempting automatic moon routing");
                    ZMTBase.Instance.rerouteShip(GameNetworkManager.Instance, false);
                }
            }
        }
        [HarmonyPatch(typeof(NetworkObject))]
        internal class PreventSellScrapsOnOxydePatch
        {
            private static readonly HashSet<string> BlockedItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            // Vanilla
            "Shotgun",
            "Kitchen knife",
            // MoreCounterplay
            "Coilless Coilhead",
            // SellBodiesFixed
            "Bracken Remains",
            "Cadaver Flower",
            "Cartridge",
            "Cartridge [Glitched]",
            "Cartridge [Racing into the Night]",
            "CartridgeRDA",
            "Cartridge [ReDreaming Angel]",
            "CartridgeRITN",
            "Clay Surgeon Scissors",
            "Coil-Head Core",
            "Common Enemy Remains",
            "Dead Baboon Hawk",
            "Dead Backwater Gunkfish",
            "Dead Bunker Spider",
            "Dead Hoarder Bug",
            "Dead Hygrodere",
            "Dead Maneater",
            "Dead Manticoil",
            "Dead Nutcracker",
            "Dead Snare Flea",
            "Dead Spore Lizard",
            "Dead Thumper",
            "Dead Tulip Snake",
            "Eyeless Dog Teeth",
            "Feiopar Head",
            "Forest Keeper Sapling",
            "Jester Box",
            "Kidnapper Fox Tail",
            "Old Bird Core",
            "Rare Enemy Remains",
            "Strange Device",
            "UltraRare Enemy Remains",
            "Vials of Ectoplasm"
            };
            [HarmonyPostfix]
            [HarmonyPatch(typeof(NetworkObject), nameof(NetworkObject.Spawn))]
            private static void NetworkObjectSpawnPost(NetworkObject __instance)
            {
                if (!ZMTBase.Config.PreventSellScrapsOnOxyde.Value) return;
                if (!NetworkManager.Singleton.IsServer) return;
                if (StartOfRound.Instance?.currentLevel == null) return;
                ExtendedLevel currentLevel = PatchedContent.ExtendedLevels.Find(x => x.SelectableLevel == StartOfRound.Instance.currentLevel);
                if (currentLevel == null || currentLevel.NumberlessPlanetName != "Oxyde") return;
                GrabbableObject grabbable = __instance.GetComponent<GrabbableObject>();
                if (grabbable == null || grabbable.itemProperties == null) return;
                string itemName = grabbable.itemProperties.itemName?.Trim();
                if (string.IsNullOrEmpty(itemName)) return;
                bool isShotgun = grabbable is ShotgunItem; //Need to make an exception for Nutcrackers or else their AI breaks
                if (!isShotgun && !BlockedItems.Contains(itemName)) return;
                ZMTBase.Instance.StartCoroutine(DespawnBlockedItem(__instance, grabbable));
            }
            private static IEnumerator DespawnBlockedItem(NetworkObject networkObject, GrabbableObject grabbable)
            {
                yield return new WaitForSeconds(0.25f);
                if (!networkObject.IsSpawned || !NetworkManager.Singleton.IsServer) yield break;
                if (grabbable == null) yield break;
                if (grabbable is ShotgunItem shotgun)
                {
                    //Expensive method aaa, but should only run when items spawn on Oxyde IF they're in the blacklist so it should be okay
                    NutcrackerEnemyAI[] nutcrackers = UnityEngine.Object.FindObjectsOfType<NutcrackerEnemyAI>(); 
                    foreach (NutcrackerEnemyAI nutcracker in nutcrackers)
                    {
                        if (nutcracker != null && !nutcracker.isEnemyDead && nutcracker.gun == shotgun)
                        {
                            Logger.LogDebug("Preserving Nutcracker shotgun");
                            yield break;
                        }
                    }
                }
                Logger.LogWarning($"Removing {grabbable.itemProperties.itemName} on Oxyde");
                networkObject.Despawn(true);
            }
        }
        internal class PlayerBodyValuePatch
        {
            [HarmonyPostfix]
            [HarmonyPatch(typeof(RagdollGrabbableObject), nameof(RagdollGrabbableObject.Start))]
            private static void StartPostPatch(RagdollGrabbableObject __instance)
            {
                if (ZMTBase.Config.bodies0Value.Value)
                {
                    __instance.SetScrapValue(0); //Just set it to 0 value
                }
            }
        }
    }
}