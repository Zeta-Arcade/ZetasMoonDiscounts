using BepInEx;
using Dawn;
using GameNetcodeStuff;
using HarmonyLib;
using LethalLevelLoader;
using LethalModDataLib.Features;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;
using WeatherRegistry;
using WeatherTweaks.Definitions;

namespace ZetasMoonTweaks
{
    [BepInPlugin(PluginInfo.modGUID, PluginInfo.modName, PluginInfo.modVersion)]
    [BepInDependency(LethalLevelLoader.Plugin.ModGUID, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(MrovLib.MyPluginInfo.PLUGIN_GUID, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(WeatherRegistry.PluginInfo.PLUGIN_GUID, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(WeatherTweaks.PluginInfo.PLUGIN_GUID, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.sigurd.csync", "5.0.0")]
    [BepInDependency(Dusk.MyPluginInfo.PLUGIN_GUID, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(LethalModDataLib.PluginInfo.PLUGIN_GUID, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(LethalNetworkAPI.MyPluginInfo.PLUGIN_GUID, BepInDependency.DependencyFlags.HardDependency)]
    public class ZMTBase : BaseUnityPlugin
    {
        private readonly Harmony harmony = new Harmony(PluginInfo.modGUID);
        public static BepInEx.Logging.ManualLogSource Logger;
        public static ZMTBase Instance;
        internal static new ConfigHandler Config;
        internal static new ModData ModDataInstance;
        public int groupCredits;
        public bool displayedOxydeAlertForQuota;
        public bool hasRoutedForThisQuota = false;
        private List<LevelPairedWithPrice> MoonDefaultCosts = new List<LevelPairedWithPrice>();
        private List<LevelPairedWithRisk> MoonDefaultRiskLevels = new List<LevelPairedWithRisk>();
        public List<string> currentlySelectedRandomMoons = new List<string>();
        public int lastRandomMoonsQuota = -1;
        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            Logger = base.Logger;
            Config = new ConfigHandler(base.Config);
            ModDataInstance = new ModData();
            ModDataHandler.RegisterInstance(ModDataInstance, "ZMTModDataInstance");
            ZMTNetwork.Init();
            try
            {
                harmony.PatchAll();
            }
            catch (Exception ex)
            {
                Logger.LogError($"Harmony patching failed: {ex}");
            }
            Logger.LogInfo($"Plugin is loaded!");
            var method = AccessTools.Method(typeof(EntranceTeleport), "TeleportPlayer");
            var patchInfo = Harmony.GetPatchInfo(method);
            Logger.LogDebug($"TeleportPlayer found: {method != null}");
            Logger.LogDebug($"TeleportPlayer patched: {patchInfo != null}");
            if (patchInfo != null)
            {
                foreach (var postfix in patchInfo.Postfixes)
                {
                    Logger.LogDebug($"Postfix owner: {postfix.owner}");
                }
            }
        }
        private static readonly PropertyInfo CostProperty = AccessTools.Property(typeof(DawnPurchaseInfo), "Cost");
        public void RollNewSeed()
        {
            if (!NetworkManager.Singleton.IsHost) return; //Only host can generate the seed
            if (!ModDataInstance.seedSetForQuota) //If the Seed is not set already
            {
                int newQuotaRoll = UnityEngine.Random.Range(0, 100);
                //Broadcast message to everyone to change the seed
                SetNewSeed(newQuotaRoll);
                ZMTNetwork.SendNewSeed(newQuotaRoll);
            }
            else
            {
                Logger.LogDebug("Seed already set as: " + ModDataInstance.currentQuotaRoll);
            }
        }
        public void SetNewSeed(int newQuotaRoll)
        {
            Logger.LogDebug("New seed set as: " + newQuotaRoll);
            ModDataInstance.currentQuotaRoll = newQuotaRoll;
            ModDataInstance.seedSetForQuota = true;
            ModDataInstance.ZMTSaveData();
        }
        public void CheckForScrapsToDestroy()
        {
            if (StartOfRound.Instance.IsHost) //Host destroys all banned objects on in the ship
            {
                var bannedScraps = FindObjectsOfType<GrabbableObject>().Where(o => !o.isHeld && o.isInShipRoom && o.itemProperties && (o.itemProperties.itemName == "Normal Shredded Scraps" || o.itemProperties.itemName == "Sally Cube")).ToList();
                Logger.LogDebug("Destroying " + bannedScraps.Count + " scraps when in the ship when Orbiting");
                for (int i = 0; i < bannedScraps.Count; i++)
                {
                    bannedScraps[i].NetworkObject.Despawn();
                }
            }
            PlayerControllerB player = StartOfRound.Instance.localPlayerController;
            var allSlots = new Dictionary<int, GrabbableObject>();
            if (player != null)
            {
                for (int i = 0; i < player.ItemSlots.Length; i++)
                {
                    allSlots[i] = player.ItemSlots[i];
                }
            }
            foreach (var slot in allSlots.Keys)
            {
                if (allSlots[slot] == null) continue;
                if (allSlots[slot].itemProperties.itemName == "Normal Shredded Scraps" || allSlots[slot].itemProperties.itemName == "Sally Cube")
                {
                    Logger.LogDebug($"Destroying banned scrap in slot " + slot + " scraps when Orbiting");
                    ZMTNetwork.SendItemDestroy(StartOfRound.Instance.localPlayerController.playerClientId, slot);
                }
            }
        }
        public void DestroyScrapInSlot(ulong playerClientID, int slot)
        {
            if (StartOfRound.Instance == null) return;
            PlayerControllerB player = StartOfRound.Instance.allPlayerScripts.FirstOrDefault(p => p.playerClientId == playerClientID);
            if (player == null) return;
            var item = player.ItemSlots[slot];
            if (item == null) return;
            player.carryWeight = Mathf.Clamp(player.carryWeight - (item.itemProperties.weight - 1f), 1f, 10f);
            player.DestroyItemInSlot(slot);
            if (HUDManager.Instance != null)
            {
                HUDManager.Instance.itemSlotIcons[slot].enabled = false;
            }
        }
        public void ModifyBuyingRate()
        {
            if (Config.ApplyBuyingChanges.Value)
            {
                if (!TimeOfDay.Instance) return;
                if (!StartOfRound.Instance) return;
                if (TimeOfDay.Instance.daysUntilDeadline != 0)
                {
                    StartOfRound.Instance.companyBuyingRate = Config.NormalSellRate.Value;
                }
                else
                {
                    StartOfRound.Instance.companyBuyingRate = Config.ZeroDaysSellRate.Value;
                }
            }
        }
        public void rerouteShip(GameNetworkManager networkManager, bool toCompany)
        {
            if (!StartOfRound.Instance) return;
            if (!StartOfRound.Instance.currentLevel) return;
            if (!networkManager.isHostingGame) return;
            int newMoonID = -1;
            if (toCompany)
            {
                newMoonID = 3;
                if (StartOfRound.Instance.currentLevelID == 3)
                {
                    ModDataInstance.hasReroutedCompany = true;
                }
            }
            else
            {
                if (Config.gamemode.Value == ConfigHandler.Gamemode.QuotaIronman)
                {
                    newMoonID = GetNextQuotaMoonID();
                }
                else if (Config.gamemode.Value == ConfigHandler.Gamemode.DayIronman)
                {
                    newMoonID = GetNextDayMoonID();
                }
                else if (Config.gamemode.Value == ConfigHandler.Gamemode.SMHQ)
                {
                    newMoonID = GetSMHQMoonID();
                }
                else if (Config.gamemode.Value == ConfigHandler.Gamemode.RandomMoons)
                {
                    newMoonID = GetRandomMoonID();
                }
                if (StartOfRound.Instance.currentLevelID == newMoonID)
                {
                    ModDataInstance.hasReroutedMoonPair = true;
                }
            }
            if (newMoonID == -1)
            {
                return;
            }
            if (toCompany)
            {
                if (StartOfRound.Instance.CanChangeLevels() && !ModDataInstance.hasReroutedCompany && TimeOfDay.Instance.daysUntilDeadline == 0 && Config.AutoCompanyBuilding.Value && TimeOfDay.Instance.quotaFulfilled < TimeOfDay.Instance.profitQuota)
                {
                    StartOfRound.Instance.ChangeLevelServerRpc(newMoonID, groupCredits);
                    StartOfRound.Instance.ChangeLevel(newMoonID);
                    hasRoutedForThisQuota = true;
                    ModDataInstance.hasReroutedCompany = true;
                    ModDataInstance.ZMTSaveData();
                }
                else
                {
                    ModDataInstance.hasReroutedCompany = false;
                }
            }
            else if (StartOfRound.Instance.CanChangeLevels() && !ModDataInstance.hasReroutedMoonPair && Config.IronmanAutoRoute.Value && TimeOfDay.Instance.quotaFulfilled < TimeOfDay.Instance.profitQuota)
            {
                StartOfRound.Instance.ChangeLevelServerRpc(newMoonID, groupCredits);
                StartOfRound.Instance.ChangeLevel(newMoonID);
                ModDataInstance.hasReroutedMoonPair = true;
                ModDataInstance.ZMTSaveData();
            }
            else
            {
                ModDataInstance.hasReroutedMoonPair = false;
            }
        }
        public void SetDawnPrice(ExtendedLevel levelRef, int newPrice)
        {
            Logger.LogDebug($"Trying to set DawnPrice of " + levelRef.NumberlessPlanetName + " to " + newPrice);
            try
            {
                DawnMoonInfo? moonInfoTest = levelRef.SelectableLevel.GetDawnInfo();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
            }
            DawnMoonInfo? moonInfo = levelRef.SelectableLevel.GetDawnInfo();
            if (moonInfo == null) return;
            try
            {
                CostProperty.SetValue(moonInfo.DawnPurchaseInfo, new SimpleProvider<int>(newPrice));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
            }
            if (moonInfo.RouteNode != null)
            {
                moonInfo.RouteNode.itemCost = newPrice;
            }
            if (moonInfo.ReceiptNode != null)
            {
                moonInfo.ReceiptNode.itemCost = newPrice;
            }
        }
        public int GetNextQuotaMoonID()
        {
            int newID = -1;
            if (!StartOfRound.Instance) return -1;
            if (!StartOfRound.Instance.currentLevel) return -1;
            int currentQuotaNo = TimeOfDay.Instance.timesFulfilledQuota + 1;
            string[] entries = Config.IronmanMoons.Value.ToString().Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries); //E.g. 0@Exp@Vow;300@Assurance@Solace; --> 0@Exp@Vow + 0@Assurance@Solace (Each are 1 entry)
            int pairNo = 0;
            if (TimeOfDay.Instance != null)
            {
                currentQuotaNo = TimeOfDay.Instance.timesFulfilledQuota + 1;
                Logger.LogDebug($"Current quota number is: " + currentQuotaNo);
            }
            if (entries.Length > 0)
            {
                if (Config.shouldIronmanLoop.Value)
                {
                    currentQuotaNo = ((currentQuotaNo - 1) % entries.Length) + 1;
                }
                else
                {
                    currentQuotaNo = Mathf.Clamp(currentQuotaNo, 1, entries.Length);
                }
                Logger.LogDebug($"Ironman selected quota group is: {currentQuotaNo}");
            }
            foreach (string entry in entries) //For each Pair of moons....
            {
                pairNo++;
                string[] pairs = entry.Split(new[] { '@' }, StringSplitOptions.RemoveEmptyEntries); //Split them, E.g. 0@Exp@Vow --> 0 + Exp + Vow                                                                               //Current moon is company so go through each pair, find their associated ExtendedLevel, and set RouteCost to the first value converted to int (with error checking)
                int index = 0;
                bool foundFirstValidMoon = false;
                foreach (string pair in pairs) // E.g. 0 + Exp + Vow
                {
                    if (index != 0)
                    {
                        bool safe = false;
                        try
                        {
                            ExtendedLevel testLevel = PatchedContent.ExtendedLevels.Find(x => x.SelectableLevel == WeatherRegistry.ConfigHelper.ConvertStringToLevels(pair)[0]);
                            safe = true;
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError($"Error when trying to parse " + pair + " in the ExtendedLevels list");
                        }
                        if (safe)
                        {
                            if (!foundFirstValidMoon)
                            {
                                ExtendedLevel currentConfigLevel = PatchedContent.ExtendedLevels.Find(x => x.SelectableLevel == WeatherRegistry.ConfigHelper.ConvertStringToLevels(pair)[0]);

                                if (currentConfigLevel != null)
                                {
                                    int value;
                                    bool firstValueValid = int.TryParse(pairs[0].ToString(), out value);
                                    if (firstValueValid)
                                    {
                                        if (currentQuotaNo == pairNo)
                                        {
                                            newID = currentConfigLevel.SelectableLevel.levelID;
                                            foundFirstValidMoon = true;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    index++;
                }
            }
            return newID;
        }
        public int GetNextDayMoonID()
        {
            if (StartOfRound.Instance?.gameStats == null) return -1;
            int currentDay = StartOfRound.Instance.gameStats.daysSpent + 1 - TimeOfDay.Instance.timesFulfilledQuota; //Times fulfilled quota is effectively how many times the company must have been sold on
            string[] entries = Config.IronmanMoons.Value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (entries.Length == 0)
            {
                Logger.LogDebug("DayIronman has no configured moon groups!");
                return -1;
            }
            int groupIndex = (currentDay - 1) % entries.Length;
            string[] pairs = entries[groupIndex].Split(new[] { '@' }, StringSplitOptions.RemoveEmptyEntries);
            if (pairs.Length < 2) return -1;
            try
            {
                SelectableLevel selectableLevel = WeatherRegistry.ConfigHelper.ConvertStringToLevels(pairs[1].Trim())[0];
                ExtendedLevel level = PatchedContent.ExtendedLevels.Find(x => x.SelectableLevel == selectableLevel);
                if (level == null) return -1;
                Logger.LogDebug($"DayIronman routing to {level.NumberlessPlanetName}");
                return level.SelectableLevel.levelID;
            }
            catch (Exception ex)
            {
                Logger.LogError($"DayIronman could not resolve moon: {ex}");
                return -1;
            }
        }
        public int GetSMHQMoonID()
        {
            string[] entries = Config.SMHQMoons.Value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string entry in entries)
            {
                try
                {
                    SelectableLevel selectableLevel = WeatherRegistry.ConfigHelper.ConvertStringToLevels(entry.Trim())[0];
                    ExtendedLevel level = PatchedContent.ExtendedLevels.Find(x => x.SelectableLevel == selectableLevel);
                    if (level != null)
                    {
                        Logger.LogDebug($"SMHQ routing to {level.NumberlessPlanetName}");
                        return level.SelectableLevel.levelID;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"SMHQ could not resolve moon {entry}: {ex}");
                }
            }
            return -1;
        }
        public int GetRandomMoonID()
        {
            if (currentlySelectedRandomMoons.Count == 0)
            {
                Logger.LogWarning($"Random Moons has no selected moons to route to!");
                return -1;
            }
            foreach (string moonName in currentlySelectedRandomMoons)
            {
                ExtendedLevel currentConfigLevel =PatchedContent.ExtendedLevels.Find(x => x.NumberlessPlanetName == moonName);
                if (currentConfigLevel != null)
                {
                    Logger.LogDebug($"Random Moons automatically routing to " +currentConfigLevel.NumberlessPlanetName);
                    return currentConfigLevel.SelectableLevel.levelID;
                }
            }
            Logger.LogWarning($"Random Moons could not find a valid moon to route to!");
            return -1;
        }
        public void UpdateOxydeTabletStatus(bool newUnlockStatus)
        {
            ModDataInstance.isOxydeOveride = newUnlockStatus;
            displayedOxydeAlertForQuota = false;
            ModDataInstance.ZMTSaveData();
        }
        public void SetOxydeCostFromConfigs()
        {
            bool quotaIsEven;
            int currentQuotaNo = TimeOfDay.Instance.timesFulfilledQuota + 1;
            ExtendedLevel oxydeRef = PatchedContent.ExtendedLevels.Find(x => x.NumberlessPlanetName == "Oxyde");
            if (oxydeRef == null)
            {
                Logger.LogWarning("Couldnt find Oxyde, no changes will be made to the price");
                return;
            }
            if (currentQuotaNo % 2 == 0)
            {
                quotaIsEven = true;
            }
            else
            {
                quotaIsEven = false;
            }
            if (ModDataInstance.isOxydeOveride)
            {
                SetOxydeCostFromDeadline(oxydeRef);
                OxydeHUDAlert(false, "Oxyde has been unlocked for this Quota, thanks to the Oxyde Tablet!");
            }
            else
            {
                switch (Config.OxydeLockMode.Value)
                {
                    case ConfigHandler.OxydeLockModes.None:
                        SetOxydeCostFromDeadline(oxydeRef);
                        break;
                    case ConfigHandler.OxydeLockModes.EvenOnly:
                        if (quotaIsEven)
                        {
                            SetOxydeCostFromDeadline(oxydeRef);
                            OxydeHUDAlert(false, "Oxyde has been unlocked for this Quota, since the Quota # is an Even Number!");
                        }
                        else
                        {
                            oxydeRef.RoutePrice = 9999;
                            SetDawnPrice(oxydeRef, 9999);
                            OxydeHUDAlert(true, "Oxyde has been locked for this Quota, since the Quota # isn't an Even Number!");
                        }
                        break;
                    case ConfigHandler.OxydeLockModes.OddOnly:
                        if (!quotaIsEven)
                        {
                            SetOxydeCostFromDeadline(oxydeRef);
                            OxydeHUDAlert(false, "Oxyde has been unlocked for this Quota, since the Quota # is an Odd Number!");
                        }
                        else
                        {
                            oxydeRef.RoutePrice = 9999;
                            SetDawnPrice(oxydeRef, 9999);
                            OxydeHUDAlert(true, "Oxyde has been locked for this Quota, since the Quota # isn't an Odd Number!");
                        }
                        break;
                    case ConfigHandler.OxydeLockModes.Threshold:
                        if (TimeOfDay.Instance.profitQuota >= Config.OxydeUnlockThreshold.Value)
                        {
                            SetOxydeCostFromDeadline(oxydeRef);
                            OxydeHUDAlert(false, "Oxyde has been unlocked for this Quota, since the Quota is above $" + Config.OxydeUnlockThreshold.Value + "!");
                        }
                        else
                        {
                            oxydeRef.RoutePrice = 9999;
                            SetDawnPrice(oxydeRef, 9999);
                            OxydeHUDAlert(true, "Oxyde has been locked for this Quota, since the Quota isn't at $" + Config.OxydeUnlockThreshold.Value + " yet!");
                        }
                        break;
                    case ConfigHandler.OxydeLockModes.Rng:
                        if (!ModDataInstance.seedSetForQuota)
                        {
                            oxydeRef.RoutePrice = 9999;
                            SetDawnPrice(oxydeRef, 9999);
                            RollNewSeed();
                        }
                        else
                        {
                            if (ModDataInstance.currentQuotaRoll <= Config.OxydeUnlockChance.Value)
                            {
                                SetOxydeCostFromDeadline(oxydeRef);
                                OxydeHUDAlert(false, "Oxyde has been unlocked due to good luck for this Quota! (A " + Config.OxydeUnlockChance.Value + "% chance of happening!)");
                            }
                            else
                            {
                                oxydeRef.RoutePrice = 9999;
                                SetDawnPrice(oxydeRef, 9999);
                                int badLuckChance = 100 - Config.OxydeUnlockChance.Value;
                                OxydeHUDAlert(true, "Oxyde has been locked due to bad luck for this Quota! (A " + badLuckChance + "% chance of happening!)");
                            }
                        }
                        break;
                }
            }
        }
        public void OxydeHUDAlert(bool denied, string reason)
        {
            if (HUDManager.Instance != null && StartOfRound.Instance != null)
            {
                if (StartOfRound.Instance.localPlayerController == null) return;
                if (!displayedOxydeAlertForQuota && StartOfRound.Instance.localPlayerController.isPlayerControlled && StartOfRound.Instance.inShipPhase)
                {
                    displayedOxydeAlertForQuota = true;
                    HUDManager.Instance.DisplayTip("Oxyde Update", reason, denied);
                }
            }
        }
        public void SetOxydeCostFromDeadline(ExtendedLevel OxydeRef)
        {
            //Sets the cost based on how many days are left to the Deadline
            int desiredCost = -1;
            switch (TimeOfDay.Instance.daysUntilDeadline)
            {
                case 0:
                    desiredCost = Config.ZeroDaysOxydeCost;
                    break;
                case 1:
                    desiredCost = Config.OneDayOxydeCost;
                    break;
                case 2:
                    desiredCost = Config.TwoDayOxydeCost;
                    break;
                case 3:
                    desiredCost = Config.ThreeDayOxydeCost;
                    break;
                default:
                    desiredCost = Config.ThreeDayOxydeCost;
                    break;
            }
            if (desiredCost > -1)
            {
                OxydeRef.RoutePrice = desiredCost;
                SetDawnPrice(OxydeRef, desiredCost);
            }
            else
            {
                OxydeRef.RoutePrice = MoonDefaultCosts.Find(x => x.Level == OxydeRef).OriginalPrice;
                SetDawnPrice(OxydeRef, OxydeRef.RoutePrice);
            }
        }
        public void CalculateMoonPrices()
        {
            if (Config.ApplyDiscountChanges.Value || Config.gamemode.Value != ConfigHandler.Gamemode.Normal) //If Discounts on OR in Ironman Mode
            {
                Logger.LogDebug($"Attempting CalculateMoonPrices");
                if (MoonDefaultCosts.Count < 1 && Config.gamemode.Value == ConfigHandler.Gamemode.Normal)
                {
                    Logger.LogDebug($"Default moon prices have not been set, skipping Calculation of moon prices");
                }
                else
                {
                    Logger.LogDebug($"Default moon prices been set, continuing");
                    if (!StartOfRound.Instance) return;
                    if (!StartOfRound.Instance.currentLevel) return;
                    SelectableLevel currentLevel = StartOfRound.Instance.currentLevel;
                    ExtendedLevel currentExtendedLevel = PatchedContent.ExtendedLevels.Find(x => x.SelectableLevel == currentLevel);
                    if (Config.gamemode.Value == ConfigHandler.Gamemode.Normal) //If in normal mode
                    {
                        foreach (ExtendedLevel level in PatchedContent.ExtendedLevels)
                        {
                            level.RoutePrice = MoonDefaultCosts.Find(x => x.Level == level).OriginalPrice; //Set all prices back to default to start
                            SetDawnPrice(level, level.RoutePrice);
                        }
                        if (currentExtendedLevel.NumberlessPlanetName == "Gordion" || currentExtendedLevel.NumberlessPlanetName == "Galetry" || currentExtendedLevel.NumberlessPlanetName == "Oxyde" || currentExtendedLevel.RoutePrice == 0)
                        {
                            //CURRENT moon is free/company so dont bother doing discounts
                            Logger.LogDebug($"Current moon " + currentExtendedLevel.NumberlessPlanetName + " is free/company/oxyde, so no discounts will be applied except for Oxyde Route Cost changes");
                        }
                        else
                        {
                            Logger.LogDebug($"Current moon " + currentExtendedLevel.NumberlessPlanetName + " is not free/company/oxyde, so discounts will be applied");
                            foreach (ExtendedLevel level in PatchedContent.ExtendedLevels) //Looping through every level
                            {
                                if (level.NumberlessPlanetName == "Gordion" || level.NumberlessPlanetName == "Oxyde" || level.NumberlessPlanetName == "Galetry" || level.NumberlessPlanetName == currentExtendedLevel.NumberlessPlanetName || level.RoutePrice == 0)
                                {
                                    // "Other moon" is the same moon, or free or company, so keep route prices to them unchanged
                                    level.RoutePrice = MoonDefaultCosts.Find(x => x.Level == level).OriginalPrice;
                                    SetDawnPrice(level, level.RoutePrice);
                                }
                                else
                                {
                                    //If other moon is X amount cheaper or more, make route price free

                                    int CostDif = currentExtendedLevel.RoutePrice - level.RoutePrice;
                                    if (CostDif >= Config.FreeThreshold.Value && level.RoutePrice < Config.FreeThresholdCap.Value || CostDif >= 3500) // E.g. Checks the other moon is cheap enough but also isn't at the cap
                                    {
                                        level.RoutePrice = 0; //Makes it free
                                        SetDawnPrice(level, 0);
                                        Logger.LogDebug($"Current moon " + currentExtendedLevel.NumberlessPlanetName + "(" + currentExtendedLevel.RoutePrice + ")" + " --> " + level.NumberlessPlanetName + "(" + level.RoutePrice + ")" + " Meets the free discount criteria");
                                    }
                                    else if (level.RoutePrice <= Config.DowngradeCost.Value) //Else, if the route price is lower than the downgrade price, just keep it the same
                                    {
                                        //level.RoutePrice = DowngradeCost.Value; 
                                        Logger.LogDebug($"Current moon " + currentExtendedLevel.NumberlessPlanetName + "(" + currentExtendedLevel.RoutePrice + ")" + " --> " + level.NumberlessPlanetName + "(" + level.RoutePrice + ")" + " Meets the downgrade discount criteria but is so cheap it will stay the same price");
                                    }
                                    else if (level.RoutePrice <= currentExtendedLevel.RoutePrice) //Else, if it is still cheaper or same price, then make it cost 50 to route to
                                    {
                                        level.RoutePrice = Config.DowngradeCost.Value; //Makes discounted, default 50
                                        SetDawnPrice(level, level.RoutePrice);
                                        Logger.LogDebug($"Current moon " + currentExtendedLevel.NumberlessPlanetName + "(" + currentExtendedLevel.RoutePrice + ")" + " --> " + level.NumberlessPlanetName + "(" + level.RoutePrice + ")" + " Meets the downgrade discount criteria");
                                    }
                                    else
                                    { // Else, it must be more expensive then, so flip CostDif, e.g. Rend 750 Dine 800, so 800-750 = 50
                                        CostDif = level.RoutePrice - currentExtendedLevel.RoutePrice;
                                        if (CostDif <= Config.UpgradeThreshold.Value)
                                        {
                                            level.RoutePrice = CostDif; //Upgrade price is just the CostDif between the 2 moons
                                            SetDawnPrice(level, level.RoutePrice);
                                            Logger.LogDebug($"Current moon " + currentExtendedLevel.NumberlessPlanetName + "(" + currentExtendedLevel.RoutePrice + ")" + " --> " + level.NumberlessPlanetName + "(" + level.RoutePrice + ")" + " Meets the upgrade discount criteria");
                                        }
                                        else
                                        {
                                            //The moon does not meet any of the criteria, so it's cost must be set back to default
                                            level.RoutePrice = MoonDefaultCosts.Find(x => x.Level == level).OriginalPrice;
                                            SetDawnPrice(level, level.RoutePrice);
                                            Logger.LogDebug($"Current moon " + currentExtendedLevel.NumberlessPlanetName + "(" + currentExtendedLevel.RoutePrice + ")" + " --> " + level.NumberlessPlanetName + "(" + level.RoutePrice + ")" + " Does not meet any discount criteria");
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else if (Config.gamemode.Value == ConfigHandler.Gamemode.QuotaIronman || Config.gamemode.Value == ConfigHandler.Gamemode.DayIronman)
                    {
                        // Ironman Mode :KaguyaFace:
                        Logger.LogDebug($"Ironman Mode Detected");
                        if (currentExtendedLevel.NumberlessPlanetName == "Gordion" || currentExtendedLevel.NumberlessPlanetName == "Galetry" || currentExtendedLevel.NumberlessPlanetName == "Oxyde")
                        {
                            Logger.LogDebug($"Ironman Is Company");
                            string[] entries = Config.IronmanMoons.Value.ToString().Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries); //E.g. 0@Exp@Vow;300@Assurance@Solace; --> 0@Exp@Vow + 0@Assurance@Solace (Each are 1 entry)
                            int pairNo = 0;
                            int currentMoonNumber = 0;
                            if (entries.Length > 0 && Config.shouldIronmanLoop)
                            {
                                currentMoonNumber = ((currentMoonNumber - 1) % entries.Length) + 1;
                            }
                            else if (entries.Length > 0 && !Config.shouldIronmanLoop)
                            {
                                currentMoonNumber = Mathf.Clamp(currentMoonNumber, 1, entries.Length);
                            }
                            if (TimeOfDay.Instance != null)
                            {
                                if (Config.gamemode.Value == ConfigHandler.Gamemode.QuotaIronman)
                                {
                                    currentMoonNumber = TimeOfDay.Instance.timesFulfilledQuota + 1;
                                    Logger.LogDebug($"Current quota number is: " + currentMoonNumber);
                                }
                                else if (Config.gamemode.Value == ConfigHandler.Gamemode.DayIronman)
                                {
                                    currentMoonNumber = StartOfRound.Instance.gameStats.daysSpent + 1 - TimeOfDay.Instance.timesFulfilledQuota;
                                    Logger.LogDebug($"Current day number is: " + currentMoonNumber);
                                }
                            }
                            foreach (string entry in entries) //For each Pair of moons....
                            {
                                pairNo++;
                                Logger.LogDebug($"Ironman currently on entry " + entry);
                                string[] pairs = entry.Split(new[] { '@' }, StringSplitOptions.RemoveEmptyEntries); //Split them, E.g. 0@Exp@Vow --> 0 + Exp + Vow                                                                               //Current moon is company so go through each pair, find their associated ExtendedLevel, and set RouteCost to the first value converted to int (with error checking)
                                int index = 0;
                                foreach (string pair in pairs) // E.g. 0 + Exp + Vow
                                {
                                    Logger.LogDebug($"Ironman currently on pair " + pair);
                                    if (index != 0) //Skips the first value, since that should be a number
                                    { //Finds the first matching level in list
                                        bool safe = false;
                                        try
                                        {
                                            ExtendedLevel testLevel = PatchedContent.ExtendedLevels.Find(x => x.SelectableLevel == WeatherRegistry.ConfigHelper.ConvertStringToLevels(pair)[0]);
                                            safe = true;
                                        }
                                        catch (Exception ex)
                                        {
                                            Logger.LogError($"Error when trying to parse " + pair + " in the ExtendedLevels list");
                                        }
                                        if (safe)
                                        {
                                            ExtendedLevel currentConfigLevel = PatchedContent.ExtendedLevels.Find(x => x.SelectableLevel == WeatherRegistry.ConfigHelper.ConvertStringToLevels(pair)[0]);

                                            if (currentConfigLevel != null)
                                            {
                                                int value;
                                                bool firstValueValid = int.TryParse(pairs[0].ToString(), out value);
                                                if (firstValueValid)
                                                {
                                                    Logger.LogDebug($"Setting " + currentConfigLevel.NumberlessPlanetName + " route price to " + value);

                                                    if (Config.IronmanRestrictRouting.Value)
                                                    {
                                                        if (currentMoonNumber == pairNo)
                                                        {
                                                            currentConfigLevel.IsRouteLocked = false;
                                                            currentConfigLevel.IsRouteHidden = false;
                                                            Logger.LogDebug($"Moon " + currentConfigLevel.NumberlessPlanetName + " is not locked because it is from pair " + pairNo + " and the current quota/day is " + currentMoonNumber);
                                                            if (Config.IronmanAutoRoute.Value)
                                                            {
                                                                currentConfigLevel.RoutePrice = 0;
                                                                SetDawnPrice(currentConfigLevel, currentConfigLevel.RoutePrice);
                                                            }
                                                            else
                                                            {
                                                                currentConfigLevel.RoutePrice = value; //Assigns the first "pair" value as the RoutePrice for the moon
                                                                SetDawnPrice(currentConfigLevel, currentConfigLevel.RoutePrice);
                                                            }
                                                        }
                                                        else
                                                        {
                                                            currentConfigLevel.IsRouteLocked = true;
                                                            currentConfigLevel.IsRouteHidden = true;
                                                            currentConfigLevel.RoutePrice = 9999;
                                                            SetDawnPrice(currentConfigLevel, currentConfigLevel.RoutePrice);
                                                            Logger.LogDebug($"Moon " + currentConfigLevel.NumberlessPlanetName + " is locked because it is from pair " + pairNo + " and the current quota/day is " + currentMoonNumber);
                                                        }
                                                    }
                                                    else
                                                    {
                                                        currentConfigLevel.RoutePrice = value; //Assigns the first "pair" value as the RoutePrice for the moon
                                                        SetDawnPrice(currentConfigLevel, currentConfigLevel.RoutePrice);
                                                        currentConfigLevel.IsRouteLocked = false;
                                                        currentConfigLevel.IsRouteHidden = false;
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                Logger.LogError($"Ironman could not find a matching level for " + pair + " in the ExtendedLevels list");
                                            }
                                        }
                                    }
                                    index++;
                                }
                            }
                        }
                        else
                        { //Set route price to paired moon and current to 0, set route price to others besides company to 9999
                            Logger.LogDebug($"Ironman is a normal moon");
                            bool foundMoonInPair = false;
                            List<string> pairedMoonNames = new List<string>();
                            string[] entries = Config.IronmanMoons.Value.ToString().Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                            int currentMoonNumber = 0;
                            if (Config.gamemode.Value == ConfigHandler.Gamemode.QuotaIronman)
                            {
                                if (TimeOfDay.Instance != null)
                                {
                                    currentMoonNumber = TimeOfDay.Instance.timesFulfilledQuota + 1;
                                    Logger.LogDebug($"Current quota number is: {currentMoonNumber}");
                                }
                            }
                            else if (Config.gamemode.Value == ConfigHandler.Gamemode.DayIronman)
                            {
                                if (StartOfRound.Instance?.gameStats != null)
                                {
                                    currentMoonNumber = StartOfRound.Instance.gameStats.daysSpent + 1 - TimeOfDay.Instance.timesFulfilledQuota;
                                    Logger.LogDebug($"Current day number is: {currentMoonNumber}");
                                }
                            }
                            int pairNo = 0;
                            foreach (string entry in entries)
                            {
                                pairNo++;
                                // Only process the group for the current quota/day
                                if (pairNo != currentMoonNumber) continue;
                                Logger.LogDebug($"Ironman current group is: {entry}");
                                string[] pairs = entry.Split(new[] { '@' }, StringSplitOptions.RemoveEmptyEntries);
                                int index = 0;
                                foreach (string pair in pairs)
                                {
                                    if (index != 0) // Skip the route price
                                    {
                                        try
                                        {
                                            SelectableLevel selectableLevel = WeatherRegistry.ConfigHelper.ConvertStringToLevels(pair.Trim())[0];
                                            ExtendedLevel currentConfigLevel = PatchedContent.ExtendedLevels.Find(x => x.SelectableLevel == selectableLevel);
                                            if (currentConfigLevel != null)
                                            {
                                                Logger.LogDebug($"Ironman adding moon {currentConfigLevel.NumberlessPlanetName} " + $"from group {pairNo}");
                                                pairedMoonNames.Add(currentConfigLevel.NumberlessPlanetName);
                                            }
                                            else
                                            {
                                                Logger.LogError($"Ironman could not find a matching level for {pair}");
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            Logger.LogError($"Ironman error when trying to parse {pair}: {ex}");
                                        }
                                    }
                                    index++;
                                }
                                break;
                            }
                            foreach (ExtendedLevel level in PatchedContent.ExtendedLevels)
                            {
                                if (level.NumberlessPlanetName != "Gordion" && level.NumberlessPlanetName != "Galetry" && level.NumberlessPlanetName != "Oxyde")
                                {
                                    bool matchesAMoonName = false;
                                    foreach (string moonName in pairedMoonNames)
                                    {
                                        Logger.LogDebug($"Ironman current level checked is " + level.NumberlessPlanetName + " vs. " + moonName);
                                        if (level.NumberlessPlanetName == moonName)
                                        {
                                            Logger.LogDebug($"Ironman they are a match");
                                            matchesAMoonName = true; //Flag for if the currently checked level is one of the paired ones from earlier, since we don't want to overwrite it
                                        }
                                        else
                                        {
                                            Logger.LogDebug($"Ironman they are not a match");
                                        }
                                    }
                                    if (!matchesAMoonName)
                                    {
                                        level.RoutePrice = 9999; //Set route price to 9999
                                        SetDawnPrice(level, level.RoutePrice);
                                        level.IsRouteLocked = true;
                                        level.IsRouteHidden = false;
                                    }
                                    else
                                    {
                                        level.RoutePrice = 0;
                                        SetDawnPrice(level, level.RoutePrice);
                                        level.IsRouteLocked = false;
                                        level.IsRouteHidden = false;
                                    }
                                }
                            }
                        }
                    }
                    else if (Config.gamemode.Value == ConfigHandler.Gamemode.SMHQ)
                    {
                        Logger.LogDebug($"SMHQ Mode Detected");
                        string[] entries = Config.SMHQMoons.Value.ToString().Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                        List<string> allowedMoonNames = new List<string>();
                        foreach (string entry in entries)
                        {
                            Logger.LogDebug($"SMHQ currently on entry " + entry);
                            try
                            {
                                SelectableLevel selectableLevel = WeatherRegistry.ConfigHelper.ConvertStringToLevels(entry.Trim())[0];
                                ExtendedLevel currentConfigLevel = PatchedContent.ExtendedLevels.Find(x => x.SelectableLevel == selectableLevel);
                                if (currentConfigLevel != null)
                                {
                                    Logger.LogDebug($"SMHQ allowing moon " + currentConfigLevel.NumberlessPlanetName);
                                    allowedMoonNames.Add(currentConfigLevel.NumberlessPlanetName);
                                    currentConfigLevel.RoutePrice = 0;
                                    SetDawnPrice(currentConfigLevel, currentConfigLevel.RoutePrice);
                                    currentConfigLevel.IsRouteLocked = false;
                                    currentConfigLevel.IsRouteHidden = false;
                                }
                                else
                                {
                                    Logger.LogError($"SMHQ could not find a matching level for " + entry);
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.LogError($"SMHQ error when trying to parse " + entry + ": " + ex);
                            }
                        }
                        foreach (ExtendedLevel level in PatchedContent.ExtendedLevels)
                        {
                            // Keep company stuff
                            if (level.NumberlessPlanetName == "Gordion" || level.NumberlessPlanetName == "Galetry" || level.NumberlessPlanetName == "Oxyde")
                            {
                                continue;
                            }
                            bool matchesAMoonName = false;
                            foreach (string moonName in allowedMoonNames)
                            {
                                Logger.LogDebug($"SMHQ current level checked is " + level.NumberlessPlanetName + " vs. " + moonName);
                                if (level.NumberlessPlanetName == moonName)
                                {
                                    Logger.LogDebug($"SMHQ they are a match");
                                    matchesAMoonName = true;
                                    break;
                                }
                            }
                            if (!matchesAMoonName)
                            {
                                Logger.LogDebug($"SMHQ locking moon " + level.NumberlessPlanetName);
                                level.RoutePrice = 9999;
                                SetDawnPrice(level, level.RoutePrice);
                                level.IsRouteLocked = true;
                                level.IsRouteHidden = true;
                            }
                            else
                            {
                                Logger.LogDebug($"SMHQ unlocking moon " + level.NumberlessPlanetName);
                                level.RoutePrice = 0;
                                SetDawnPrice(level, level.RoutePrice);
                                level.IsRouteLocked = false;
                                level.IsRouteHidden = false;
                            }
                        }
                    }
                    else if (Config.gamemode.Value == ConfigHandler.Gamemode.RandomMoons)
                    {
                        Logger.LogDebug($"Random Moons Mode Detected");
                        RollRandomMoons();
                        List<string> allowedMoonNames = currentlySelectedRandomMoons;
                        if (allowedMoonNames.Count == 0)
                        {
                            Logger.LogWarning($"Random Moons has no selected moons, skipping route restrictions");
                            return;
                        }
                        foreach (ExtendedLevel level in PatchedContent.ExtendedLevels)
                        {
                            if (level.NumberlessPlanetName == "Gordion" || level.NumberlessPlanetName == "Galetry" || level.NumberlessPlanetName == "Oxyde")
                            {
                                continue;
                            }
                            bool matchesAMoonName = false;
                            foreach (string moonName in allowedMoonNames)
                            {
                                Logger.LogDebug($"Random Moons current level checked is " + level.NumberlessPlanetName + " and " + moonName);
                                if (level.NumberlessPlanetName == moonName)
                                {
                                    Logger.LogDebug($"Random Moons they are a match");
                                    matchesAMoonName = true;
                                    break;
                                }
                            }
                            if (!matchesAMoonName)
                            {
                                Logger.LogDebug($"Random Moons locking moon " + level.NumberlessPlanetName);
                                level.RoutePrice = 9999;
                                SetDawnPrice(level, level.RoutePrice);
                                level.IsRouteLocked = true;
                                level.IsRouteHidden = true;
                            }
                            else
                            {
                                Logger.LogDebug($"Random Moons unlocking moon " + level.NumberlessPlanetName);
                                level.RoutePrice = 0;
                                SetDawnPrice(level, level.RoutePrice);
                                level.IsRouteLocked = false;
                                level.IsRouteHidden = false;
                            }
                        }
                    }
                }
            }
        }
        public void CalculateMoonRiskLevels()
        {
            Logger.LogDebug($"Attempting CalculateMoonRiskLevels");
            if (MoonDefaultRiskLevels.Count < 1 || !Config.ApplyRiskLevelChanges.Value) //If array is too small or config is disabled
            {
                Logger.LogDebug($"Default moon risk levels have not been set or the option is disabled, skipping Calculation of moon risk levels");
            }
            else
            {
                Logger.LogDebug($"Default moon risk levels have been set, continuing");
                if (!StartOfRound.Instance) return;
                if (!StartOfRound.Instance.currentLevel) return;
                Logger.LogDebug($"Got current level ref");
                if (Config.ApplyRiskLevelChanges.Value)
                {
                    foreach (ExtendedLevel level in PatchedContent.ExtendedLevels)
                    {
                        level.SelectableLevel.riskLevel = MoonDefaultRiskLevels.Find(x => x.Level == level).OriginalRiskLevel; //Set all risk levels back to default
                        if (level.NumberlessPlanetName == "Gordion" || level.NumberlessPlanetName == "Galetry" || level.NumberlessPlanetName == "Oxyde")
                        {
                            //CURRENT moon is free/company so dont change risk level
                            Logger.LogDebug($"Current moon " + level.NumberlessPlanetName + " is company/oxyde, so no risk level changes will be applied");
                        }
                        else
                        {
                            Logger.LogDebug($"Current moon " + level.NumberlessPlanetName + " is not company/oxyde, so risk level changes will be applied");
                            // Convert current risk level to a float
                            // Get current weather, find associated float increase or use default if cant be found
                            // Add both values together
                            bool isCombined;
                            float newRiskLevelValue = Utils.ConvertRiskLevelToFloat(level.SelectableLevel.riskLevel) + GetWeatherRiskLevelFloat(WeatherManager.GetCurrentWeather(level.SelectableLevel), out isCombined);
                            level.SelectableLevel.riskLevel = Utils.ConvertFloatToRiskLevel(newRiskLevelValue);
                            if (isCombined)
                            {
                                level.SelectableLevel.riskLevel += "?";
                            }
                            //Convert values back to RiskLevel
                            //Apply new risk level to the current moon
                        }
                    }
                }
            }
        }
        public void RollRandomMoons()
        {
            if (GameNetworkManager.Instance == null) return;
            if (!GameNetworkManager.Instance.isHostingGame) return;
            if (TimeOfDay.Instance == null) return;

            Logger.LogDebug($"Attempting to roll Random Moons");

            int currentQuota = TimeOfDay.Instance.timesFulfilledQuota;

            if (ModDataInstance.lastRandomMoonsQuota == currentQuota && !string.IsNullOrEmpty(ModDataInstance.selectedRandomMoons))
            {
                if (lastRandomMoonsQuota != currentQuota || currentlySelectedRandomMoons.Count == 0)
                {
                    Logger.LogDebug($"Random Moons restoring existing selection for this quota");
                    SetRandomMoons(ModDataInstance.selectedRandomMoons,ModDataInstance.lastRandomMoonsQuota);
                }
                return;
            }
            // Don't reroll if we already have moons selected for this quota
            if (lastRandomMoonsQuota == currentQuota && currentlySelectedRandomMoons.Count > 0)
            {
                Logger.LogDebug($"Random Moons have already been rolled for this quota");
                return;
            }
            string[] entries = Config.RandomMoons.Value.ToString().Split(new[] { ';' },StringSplitOptions.RemoveEmptyEntries);
            List<ExtendedLevel> possibleMoons = new List<ExtendedLevel>();
            foreach (string entry in entries)
            {
                Logger.LogDebug($"Random Moons currently on entry " + entry);
                try
                {
                    SelectableLevel selectableLevel = WeatherRegistry.ConfigHelper.ConvertStringToLevels(entry.Trim())[0];
                    ExtendedLevel currentConfigLevel =PatchedContent.ExtendedLevels.Find(x => x.SelectableLevel == selectableLevel);
                    if (currentConfigLevel != null)
                    {
                        // Don't include company moons
                        if (currentConfigLevel.NumberlessPlanetName == "Gordion" ||currentConfigLevel.NumberlessPlanetName == "Galetry" ||currentConfigLevel.NumberlessPlanetName == "Oxyde")
                        {
                            continue;
                        }
                        // Don't add the same moon twice
                        if (!possibleMoons.Contains(currentConfigLevel))
                        {
                            Logger.LogDebug($"Random Moons adding possible moon " +currentConfigLevel.NumberlessPlanetName);
                            possibleMoons.Add(currentConfigLevel);
                        }
                    }
                    else
                    {
                        Logger.LogError($"Random Moons could not find a matching level for " + entry);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Random Moons error when trying to parse " + entry + ": " + ex);
                }
            }

            if (possibleMoons.Count == 0)
            {
                Logger.LogWarning($"Random Moons has no valid moons to select!");
                return;
            }
            List<string> newlySelectedMoons = new List<string>();
            // Select 3 different moons (or whatever config says), or fewer if the config has less than 3
            int moonsToSelect = Mathf.Min(Config.randomMoonAmount.Value, possibleMoons.Count);
            for (int i = 0; i < moonsToSelect; i++)
            {
                int randomIndex = UnityEngine.Random.Range(0, possibleMoons.Count);
                ExtendedLevel selectedMoon = possibleMoons[randomIndex];
                Logger.LogDebug($"Random Moons selected moon " + selectedMoon.NumberlessPlanetName);
                newlySelectedMoons.Add(selectedMoon.NumberlessPlanetName);
                possibleMoons.RemoveAt(randomIndex);
            }
            string selectedMoonsString = string.Join(";", newlySelectedMoons); //Take list and put them all together
            SetRandomMoons(selectedMoonsString, currentQuota);
            ModDataInstance.ZMTSaveData();
            ZMTNetwork.SendRandomMoons(selectedMoonsString, currentQuota);
            Logger.LogInfo($"Random Moons rolled for quota {currentQuota + 1}: " + ", " + newlySelectedMoons);
        }
        public void SetRandomMoons(string selectedMoons, int quotaNumber)
        {
            currentlySelectedRandomMoons = new List<string>(selectedMoons.Split(new[] { ';' },StringSplitOptions.RemoveEmptyEntries));
            lastRandomMoonsQuota = quotaNumber;
            ModDataInstance.selectedRandomMoons = selectedMoons;
            ModDataInstance.lastRandomMoonsQuota = quotaNumber;
            Logger.LogInfo($"Random Moons selection set to: " + currentlySelectedRandomMoons);
            if (StartOfRound.Instance != null && StartOfRound.Instance.currentLevel != null)
            {
                CalculateMoonPrices();
            }
        }
        public void LoadRandomMoonsFromSave()
        {
            Logger.LogDebug($"Attempting to load Random Moons from save");
            if (ModDataInstance == null) return;
            if (string.IsNullOrEmpty(ModDataInstance.selectedRandomMoons))
            {
                Logger.LogDebug($"No saved Random Moons selection found");
                return;
            }
            SetRandomMoons(ModDataInstance.selectedRandomMoons, ModDataInstance.lastRandomMoonsQuota);
            Logger.LogInfo($"Loaded Random Moons selection from save");
        }
        public float GetWeatherRiskLevelFloat(Weather weather, out bool isCombined)
        {
            isCombined = false;
            float output = 0;
            if (weather is WeatherTweaksWeather tweaksWeather)
            {
                float highestRiskLevelFound = 0;
                float totalRiskLevels = 0;
                float totalRiskLevelsAverage = 0;
                float totalWeatherCountBonus = 0;
                float combinedWeatherRiskLevel = 0;
                float progressingWeatherRiskLevel = 0;
                // Is likely a combined weather, or could just be that WeatherTweaks is installed
                List<string> encounteredWeathers = new List<string>();
                for (int i = 0; i < tweaksWeather.WeatherTypes.Count; i++) //Loop through every current weather 
                {
                    float currentWeatherRiskLevel = ConvertWeatherNameToRiskFloat(tweaksWeather.WeatherTypes[i].WeatherName);
                    totalRiskLevels += currentWeatherRiskLevel;
                    if (tweaksWeather.CustomType == CustomWeatherType.Combined)
                    {
                        if (!encounteredWeathers.Contains(tweaksWeather.WeatherTypes[i].WeatherName)) //Only incresae total count bonus if its a unique weather
                        {
                            encounteredWeathers.Add(tweaksWeather.WeatherTypes[i].WeatherName);
                            if (tweaksWeather.WeatherTypes[i].WeatherName != "None" || tweaksWeather.WeatherTypes[i].WeatherName != "Clear" || tweaksWeather.WeatherTypes[i].WeatherName != "Cloudy" || tweaksWeather.WeatherTypes[i].WeatherName != "NightShift")
                            {
                                float baseWeatherIncrease = 0.5f;
                                float scaledWeatherIncrease = baseWeatherIncrease * currentWeatherRiskLevel; //Half current risk level
                                totalWeatherCountBonus += scaledWeatherIncrease;
                            }
                        }
                    }
                    else //Must be progressing
                    {
                        //Total = each individual weather risk level
                        //Each individual weather risk level = current risk level / total number of weathers
                        float scaledWeatherIncrease = currentWeatherRiskLevel / tweaksWeather.WeatherTypes.Count;
                        totalWeatherCountBonus += scaledWeatherIncrease;
                    }
                    if (highestRiskLevelFound < currentWeatherRiskLevel)
                    {
                        highestRiskLevelFound = currentWeatherRiskLevel;
                    }
                }
                totalRiskLevelsAverage = totalRiskLevels / tweaksWeather.WeatherTypes.Count;
                combinedWeatherRiskLevel = highestRiskLevelFound + totalRiskLevelsAverage + totalWeatherCountBonus; //Combined is the highest level + the average + the bonus
                progressingWeatherRiskLevel = totalWeatherCountBonus;
                if (tweaksWeather.CustomType == CustomWeatherType.Combined)
                {
                    output = combinedWeatherRiskLevel;
                }
                else
                {
                    output = progressingWeatherRiskLevel;
                }
                isCombined = true; //E.g. For Light Fog, highestrisklevelfound will be low, average but totalWeatherCount will be high
            }
            else
            {
                output = ConvertWeatherNameToRiskFloat(weather.Name);
            }
            return output;
        }
        public float ConvertWeatherNameToRiskFloat(string weatherName)
        {
            Logger.LogDebug($"Started converting weather name to risk");
            float output = 0;
            Dictionary<string, float> weathersAndRisks = new Dictionary<string, float>();
            string[] entries = Config.WeathersAndRiskLevels.Value.ToString().Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries); // Go through config
            foreach (string entry in entries)
            {
                string[] parts = entry.Split(new[] { '@' }, StringSplitOptions.RemoveEmptyEntries); //Split each entry
                float currentPartValue = 0f;
                //if (parts.Length == 2) continue;
                if (float.TryParse(parts[1], out float value)) // parts[0] will be weather name, parts[1] will be risk level float
                {
                    Logger.LogDebug($"Succesfully parsed " + entry + "'s second part as a float (" + value + ")");
                    currentPartValue = value;
                }
                else
                { // Could not parse second value properly, so set to default
                    Logger.LogDebug($"Couldn't parse " + entry + "'s second part as a float, so setting to default value instead");
                    currentPartValue = Config.DefaultWeatherRiskLevelIncrease.Value;
                }
                Logger.LogDebug($"Now trying to parse weather " + parts[0] + " in the ConfigHelper");
                // Now take the first part of the config entry and try to get a valid weather ref
                List<Weather> WeathersFromConfig = new List<Weather>();
                try
                {
                    //WeatherRegistry.ConfigHelper.
                    WeathersFromConfig = WeatherRegistry.ConfigHelper.ResolveStringToWeathers(parts[0]);
                }
                catch (Exception e)
                {
                    Logger.LogDebug($"Exception caught, Could not resolve weather " + entry + " in the ConfigHelper");
                }
                bool foundWeather = false;
                string foundWeatherProperName = "";
                foreach (Weather weather in WeathersFromConfig) //Loop through every associated weather from the config, since it could match multiple
                {
                    try
                    {
                        WeatherManager.Weathers.Find(x => x.Name == weather.name);
                        Logger.LogDebug($"Found weather " + entry + " in the WeatherManager");
                        foundWeather = true;
                        foundWeatherProperName = weather.Name;
                    }
                    catch (Exception e)
                    {
                        Logger.LogDebug($"Exception caught, could not find " + entry + " in the WeatherManager");
                    }
                }
                if (foundWeather)
                {
                    try
                    {
                        weathersAndRisks.Add(foundWeatherProperName, currentPartValue); // Dictionary of Weathers - Associated Risk levels
                        Logger.LogDebug($"Added " + foundWeatherProperName + " to the Config Dictionary, with a value of " + currentPartValue);
                    }
                    catch (ArgumentException)
                    {
                        Logger.LogDebug($"Couldn't add weather " + parts[0] + " to Config Dictionary as it is already in there");
                    }
                }
                else
                {
                    Logger.LogDebug($"Couldn't find weather " + parts[0] + ", so it will not be added to the Config Dictionary");
                }
            }
            // Now find incoming weatherName in the Dictionary, and set the output to it's associated value|
            float value2 = Config.DefaultWeatherRiskLevelIncrease.Value;
            if (weathersAndRisks.TryGetValue(WeatherRegistry.ConfigHelper.ResolveStringToWeather(weatherName).Name, out value2))
            {
                Logger.LogDebug($"Found weather " + weatherName + " in Dictionary of weathers from config, outputting it's risk level");
                output = value2;
            }
            else
            {
                Logger.LogDebug($"Could not find weather " + weatherName + " in Dictionary of weathers from config, risk level will be default");
                output = Config.DefaultWeatherRiskLevelIncrease.Value;
            }
            return output;
        }
        public void SaveDefaultMoonRiskLevels()
        {
            Logger.LogDebug($"Attempting to save moon default difficulty ratings");
            if (MoonDefaultRiskLevels.Count > 1)
            {
                Logger.LogDebug($"Default difficulty levels have already have been set, skipping");
            }
            else
            {
                int index = 0;
                foreach (ExtendedLevel level in PatchedContent.ExtendedLevels)
                {
                    LevelPairedWithRisk tempLevelRef = new LevelPairedWithRisk();
                    tempLevelRef.Level = level;
                    tempLevelRef.OriginalRiskLevel = level.SelectableLevel.riskLevel;
                    MoonDefaultRiskLevels.Add(tempLevelRef);
                    Logger.LogDebug($"Saved " + level.NumberlessPlanetName + " default difficulty level as " + level.SelectableLevel.riskLevel);
                    index++;
                }
                Logger.LogDebug($"Finished setting moon default risk levels");
            }
        }
        public void SaveDefaultMoonPrices()
        {
            Logger.LogDebug($"Attempting to save moon default prices");
            Logger.LogDebug($"ExtendedLevels Count is: " + PatchedContent.ExtendedLevels.Count);
            if (MoonDefaultCosts.Count > 1)
            {
                Logger.LogDebug($"Default prices already have been set, skipping");
            }
            else
            {
                int index = 0;
                foreach (ExtendedLevel level in PatchedContent.ExtendedLevels)
                {
                    Logger.LogDebug($"In loop");
                    Logger.LogDebug($"Looping through level, current is: " + level.NumberlessPlanetName);
                    LevelPairedWithPrice tempLevelRef = new LevelPairedWithPrice();
                    tempLevelRef.Level = level;
                    tempLevelRef.OriginalPrice = level.RoutePrice;
                    MoonDefaultCosts.Add(tempLevelRef);
                    Logger.LogDebug($"Saved " + level.NumberlessPlanetName + " default price as " + level.RoutePrice);
                    index++;
                }
                Logger.LogDebug($"Finished setting moon default prices");
            }
        }
        internal class LevelPairedWithPrice
        {
            public ExtendedLevel Level;
            public int OriginalPrice;
        }
        internal class LevelPairedWithRisk
        {
            public ExtendedLevel Level;
            public string OriginalRiskLevel;
        }
    }
}