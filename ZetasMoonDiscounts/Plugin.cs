using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using LethalLevelLoader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Unity.Netcode;
using WeatherRegistry;

namespace ZetasMoonDiscounts
{
    [BepInPlugin(modGUID, modName, modVersion)]
    public class ZetasMoonDiscountsBase : BaseUnityPlugin
    {
        private const string modGUID = "ZetaArcade.ZetasMoonDiscounts";
        private const string modName = "ZetasMoonDiscounts";
        private const string modVersion = "1.0.0";
        private readonly Harmony harmony = new Harmony(modGUID);
        public static BepInEx.Logging.ManualLogSource Logger;
        public static ZetasMoonDiscountsBase Instance;
        public ConfigEntry<bool> ApplyBuyingChanges;
        public ConfigEntry<float> NormalSellRate;
        public ConfigEntry<float> ZeroDaysSelLRate;
        public ConfigEntry<int> FreeThreshold; //Equal or below is free, above but not equal is "Downgrade" price
        public ConfigEntry<int> FreeThresholdCap; //Equal or below is free, above but not equal is "Downgrade" price
        public ConfigEntry<int> DowngradeCost;
        public ConfigEntry<int> UpgradeThreshold;
        public ConfigEntry<string> IronmanMoons;
        public ConfigEntry<bool> IronmanMode;
        //internal ExtendedLevel[] AllLevels { get; private set; } = PatchedContent.ExtendedLevels.ToArray();
        private List<LevelPairedWithPrice> MoonDefaultCosts = new List<LevelPairedWithPrice>();
        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            ApplyBuyingChanges = Config.Bind<bool>("Buying Rate Changes", "Toggle Buying Rate Changes", true, "If true, the below 2 configs will be applied to the Company Buying Rate (How many credits you get for selling things)");
            NormalSellRate = Config.Bind<float>("Buying Rate Changes", "Normal Selling Rate", 1f, "The selling rate at the company on all days except the final day, where 1f = 100%, 0.5f = 50% etc.");
            ZeroDaysSelLRate = Config.Bind<float>("Buying Rate Changes", "Zero Days Selling Rate", 1f, "The selling rate at the company on the final day, where 1f = 100%, 0.5f = 50% etc.");
            FreeThreshold = Config.Bind<int>("Discount Settings", "Free Threshold", 300, "When at Moon A, if Moon B is this amount cheaper (or even cheaper!) it becomes free to route to. E.g. with the default value of 300, if Moon A costs 800 and you route to it, then any moons that cost 500 or less are free to route to.");
            FreeThresholdCap = Config.Bind<int>("Discount Settings", "Free Threshold Cap", 1000, "A Cap for the above config, where if a moon costs this amount or more, then other moons cannot route down to it for free. Note moons that are $3500 cheaper than the current moon or more are set to free anyways, bypassing this config");
            DowngradeCost = Config.Bind<int>("Discount Settings", "Downgrade Cost", 50, "When at Moon A, if Moon B is cheaper but isn't cheap enough to become free with the above config (or costs the same amount as Moon A), then it will be this route price instead.");
            UpgradeThreshold = Config.Bind<int>("Discount Settings", "Upgrade Threshold", 500, "When at Moon A, if Moon B is more expensive, then if it's within the range of this config then you can route 'up' to it, only paying the difference in cost to route. E.g. (with default config at 500) if Moon A costs 400, Moon B costs 800, and Moon C costs 1200, then you can route from Moon A to B for 400 (Since its less than 500 in difference), but Moon C will cost the full 1200.");
            IronmanMode = Config.Bind<bool>("Ironman Settings", "Ironman Mode", false, "When true, will just use the Ironman section of the config instead for moon prices. See mod desc. for how Ironman mode works.");
            IronmanMoons = Config.Bind<string>("Ironman Settings", "Ironman Moon List", "", "List of groups of moons, starting with a price for that group of moons, in the format of: Price@Moon1@Moon2..etc. ; e.g. 0@Exp@Vow;300@Assurance@Solace; Only works if Ironman Mode is enabled.");
            Logger = base.Logger;
            Logger.LogInfo($"Plugin is loaded!");
            harmony.PatchAll(typeof(ZetasMoonDiscountsBase));
            harmony.PatchAll(typeof(TerminalPatch));
            harmony.PatchAll(typeof(StartOfRoundPatch));
            harmony.PatchAll(typeof(TimeOfDayPatch));
            harmony.PatchAll(typeof(GameNetworkManagerPatch));
        }
        public void ModifyBuyingRate()
        {
            if (ApplyBuyingChanges.Value)
            {
                if (!TimeOfDay.Instance) return;
                if (!StartOfRound.Instance) return;
                if (TimeOfDay.Instance.daysUntilDeadline != 0)
                {
                    StartOfRound.Instance.companyBuyingRate = NormalSellRate.Value;
                }
                else
                {
                    StartOfRound.Instance.companyBuyingRate = ZeroDaysSelLRate.Value;
                }
            }
        }
        public void CalculateMoonPrices()
        {
            Logger.LogDebug($"Attempting CalculateMoonPrices");
            if (MoonDefaultCosts.Count < 1 && !IronmanMode.Value)
            {
                Logger.LogDebug($"Default moon prices have not been set, skipping Calculation of moon prices");
            }
            else
            {
                Logger.LogDebug($"Default moon prices been set, continuing");
                if (!StartOfRound.Instance) return;
                if (!StartOfRound.Instance.currentLevel) return;
                SelectableLevel currentLevel = StartOfRound.Instance.currentLevel;
                Logger.LogDebug($"Got current level ref");
                ExtendedLevel currentExtendedLevel = PatchedContent.ExtendedLevels.Find(x => x.SelectableLevel == currentLevel);
                if (!IronmanMode.Value) //If in normal mode
                {
                    foreach (ExtendedLevel level in PatchedContent.ExtendedLevels)
                    {
                        level.RoutePrice = MoonDefaultCosts.Find(x => x.Level == level).OriginalPrice; //Set all prices back to default
                    }
                    if (currentExtendedLevel.NumberlessPlanetName == "Gordion" || currentExtendedLevel.NumberlessPlanetName == "Galetry" || currentExtendedLevel.NumberlessPlanetName == "Oxyde" || currentExtendedLevel.RoutePrice == 0)
                    {
                        //CURRENT moon is free/company so dont bother doing discounts
                        Logger.LogDebug($"Current moon " + currentExtendedLevel.NumberlessPlanetName + " is free/company/oxyde, so no discounts will be applied");
                    }
                    else
                    {
                        Logger.LogDebug($"Current moon " + currentExtendedLevel.NumberlessPlanetName + " is not free/company/oxyde, so discounts will be applied");
                        foreach (ExtendedLevel level in PatchedContent.ExtendedLevels) //Looping through every level
                        {
                            if (level.NumberlessPlanetName == "Gordion" || level.NumberlessPlanetName == "Galetry" || level.NumberlessPlanetName == "Oxyde" || level.NumberlessPlanetName == currentExtendedLevel.NumberlessPlanetName || level.RoutePrice == 0)
                            {
                                // "Other moon" is the same moon, or free or company, so keep route prices to them unchanged
                                level.RoutePrice = MoonDefaultCosts.Find(x => x.Level == level).OriginalPrice;
                            }
                            else
                            {
                                //If other moon is X amount cheaper or more, make route price free

                                int CostDif = currentExtendedLevel.RoutePrice - level.RoutePrice;
                                if (CostDif >= FreeThreshold.Value && level.RoutePrice < FreeThresholdCap.Value || CostDif >= 3500) // E.g. Checks the other moon is cheap enough but also isn't at the cap
                                {
                                    level.RoutePrice = 0; //Makes it free
                                    Logger.LogDebug($"Current moon " + currentExtendedLevel.NumberlessPlanetName + "(" + currentExtendedLevel.RoutePrice + ")" + " --> " + level.NumberlessPlanetName + "(" + level.RoutePrice + ")" + " Meets the free discount criteria");
                                }
                                else if (level.RoutePrice <= currentExtendedLevel.RoutePrice) //Else, if it is still cheaper or same price, then make it cost 50 to route to
                                {
                                    level.RoutePrice = DowngradeCost.Value; //Makes discounted, default 50
                                    Logger.LogDebug($"Current moon " + currentExtendedLevel.NumberlessPlanetName + "(" + currentExtendedLevel.RoutePrice + ")" + " --> " + level.NumberlessPlanetName + "(" + level.RoutePrice + ")" + " Meets the downgrade discount criteria");
                                }
                                else
                                { // Else, it must be more expensive then, so flip CostDif, e.g. Rend 750 Dine 800, so 800-750 = 50
                                    CostDif = level.RoutePrice - currentExtendedLevel.RoutePrice;
                                    if (CostDif <= UpgradeThreshold.Value)
                                    {
                                        level.RoutePrice = CostDif; //Upgrade price is just the CostDif between the 2 moons
                                        Logger.LogDebug($"Current moon " + currentExtendedLevel.NumberlessPlanetName + "(" + currentExtendedLevel.RoutePrice + ")" + " --> " + level.NumberlessPlanetName + "(" + level.RoutePrice + ")" + " Meets the upgrade discount criteria");
                                    }
                                    else
                                    {
                                        //The moon does not meet any of the criteria, so it's cost must be set back to default
                                        level.RoutePrice = MoonDefaultCosts.Find(x => x.Level == level).OriginalPrice;
                                        Logger.LogDebug($"Current moon " + currentExtendedLevel.NumberlessPlanetName + "(" + currentExtendedLevel.RoutePrice + ")" + " --> " + level.NumberlessPlanetName + "(" + level.RoutePrice + ")" + " Does not meet any discount criteria");
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    // Ironman Mode :KaguyaFace:
                    Logger.LogDebug($"Ironman Mode Detected");

                    if (currentExtendedLevel.NumberlessPlanetName == "Gordion" || currentExtendedLevel.NumberlessPlanetName == "Galetry" || currentExtendedLevel.NumberlessPlanetName == "Oxyde")
                    {
                        Logger.LogDebug($"Ironman Is Company");
                        string[] entries = IronmanMoons.Value.ToString().Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries); //E.g. 0@Exp@Vow;300@Assurance@Solace; --> 0@Exp@Vow + 0@Assurance@Solace (Each are 1 entry)
                        foreach (string entry in entries) //For each Pair of moons....
                        {
                            Logger.LogDebug($"Ironman currently on entry " + entry);
                            string[] pairs = entry.Split(new[] { '@' }, StringSplitOptions.RemoveEmptyEntries); //Split them, E.g. 0@Exp@Vow --> 0 + Exp + Vow
                            //Current moon is company so go through each pair, find their associated ExtendedLevel, and set RouteCost to the first value converted to int (with error checking)
                            int index = 0;
                            foreach (string pair in pairs) // E.g. 0 + Exp + Vow
                            {
                                Logger.LogDebug($"Ironman currently on pair " + pair);
                                if (index != 0) //Skips the first value, since that should be a number
                                { //Finds the first matching level in list
                                    ExtendedLevel currentConfigLevel = PatchedContent.ExtendedLevels.Find(x => x.SelectableLevel == WeatherRegistry.ConfigHelper.ConvertStringToLevels(pair)[0]);
                                    int value;
                                    bool firstValueValid = int.TryParse(pairs[0].ToString(), out value);
                                    if (firstValueValid)
                                    {
                                        Logger.LogDebug($"Setting " + currentConfigLevel.NumberlessPlanetName + " route price to " + value);
                                        currentConfigLevel.RoutePrice = value; //Assigns the first "pair" value as the RoutePrice for the moon
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
                        string[] entries = IronmanMoons.Value.ToString().Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries); //E.g. 0@Exp@Vow;300@Assurance@Solace; --> 0@Exp@Vow + 0@Assurance@Solace (Each are 1 entry)
                        foreach (string entry in entries) //For each group of moons in the config...
                        {
                            string[] pairs = entry.Split(new[] { '@' }, StringSplitOptions.RemoveEmptyEntries); //Split them, E.g. 0@Exp@Vow --> 0 + Exp + Vow 
                            int index = 0;
                            foreach (string pair in pairs)
                            {
                                if (index != 0) //Skips the initial number part of the config
                                {
                                    ExtendedLevel currentConfigLevel = PatchedContent.ExtendedLevels.Find(x => x.SelectableLevel == WeatherRegistry.ConfigHelper.ConvertStringToLevels(pairs[index])[0]);
                                    Logger.LogDebug($"Current level is: " + currentExtendedLevel.NumberlessPlanetName + " and Current config level is " + currentConfigLevel.NumberlessPlanetName);
                                    if (currentExtendedLevel.NumberlessPlanetName == currentConfigLevel.NumberlessPlanetName)
                                    { //If current level = level in this particular config entry E.g. if Exp=Exp
                                        foundMoonInPair = true; //Set flag to true and add then loop through the config again to add each moon in it
                                        int index2 = 0;
                                        foreach (string pair2 in pairs)
                                        {
                                            if (index2 != 0)
                                            {
                                                ExtendedLevel currentConfigLevel2 = PatchedContent.ExtendedLevels.Find(x => x.SelectableLevel == WeatherRegistry.ConfigHelper.ConvertStringToLevels(pairs[index2])[0]);
                                                Logger.LogDebug($"Trying to add " + pairs[index2] + " passed as " + currentConfigLevel2);
                                                pairedMoonNames.Add(currentConfigLevel2.NumberlessPlanetName); //Always adds 
                                            }
                                            index2++;
                                        }
                                    }
                                }
                                index++;
                            }
                        }
                        foreach (ExtendedLevel level in PatchedContent.ExtendedLevels)
                        {
                            if (level.NumberlessPlanetName != "Gordion" && level.NumberlessPlanetName != "Galetry" && level.NumberlessPlanetName != "Oxyde" && level.NumberlessPlanetName != currentExtendedLevel.NumberlessPlanetName)
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
                                }
                                else
                                {
                                    level.RoutePrice = 0;
                                }
                            }
                        }
                    }
                }
            }
        }
        internal class LevelPairedWithPrice
        {
            public ExtendedLevel Level;
            public int OriginalPrice;
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
        [HarmonyPatch(typeof(Terminal), "Start")] //On Lobby Start, but isn't working currently
        internal class TerminalPatch
        {
            [HarmonyPatch("Start")]
            [HarmonyPostfix]
            private static void TerminalStartPatch(ref Terminal __instance)
            {
                Logger.LogDebug($"Terminal patch detected");
                ZetasMoonDiscountsBase.Instance.SaveDefaultMoonPrices();
                ZetasMoonDiscountsBase.Instance.CalculateMoonPrices();
            }
            [HarmonyPatch("BeginUsingTerminal")]
            [HarmonyPostfix]
            private static void TerminalBeginUsePatch(ref Terminal __instance)
            {
                Logger.LogDebug($"Terminal begin use patch detected");
                ZetasMoonDiscountsBase.Instance.CalculateMoonPrices();
            }
        }
        [HarmonyPatch(typeof(StartOfRound))] // On Arrive at moon
        internal class StartOfRoundPatch
        {
            [HarmonyPatch("PassTimeToNextDay")]
            [HarmonyPostfix]
            private static void PassTimeToNextDay()
            {
                Logger.LogDebug($"PassTimeToNextDay patch detected");
                ZetasMoonDiscountsBase.Instance.CalculateMoonPrices();
            }
            [HarmonyPatch("ArriveAtLevel")]
            [HarmonyPostfix]
            private static void ArriveAtLevelPatch()
            {
                Logger.LogDebug($"ArriveAtLevel patch detected");
                ZetasMoonDiscountsBase.Instance.CalculateMoonPrices();
            }
        }
        [HarmonyPatch(typeof(GameNetworkManager))]
        internal class GameNetworkManagerPatch
        {
            [HarmonyPatch("SaveGame")]
            [HarmonyPostfix]
            private static void SaveGameValuesPatch()
            {
                ZetasMoonDiscountsBase.Instance.CalculateMoonPrices();
            }
            [HarmonyPatch("ResetSavedGameValues")]
            [HarmonyPostfix]
            private static void ResetSavedGameValuesPatch()
            {
                ZetasMoonDiscountsBase.Instance.CalculateMoonPrices();
            }
        }
        [HarmonyPatch(typeof(TimeOfDay))]
        internal class TimeOfDayPatch
        {
            [HarmonyPatch("SetNewProfitQuota")]
            [HarmonyPrefix]
            private static void SetNewProfitQuotaPatch()
            {
                ZetasMoonDiscountsBase.Instance.CalculateMoonPrices();
            }
            [HarmonyPostfix]
            [HarmonyPatch("SetBuyingRateForDay")]
            private static void setBuyingRateForDayPatch()
            {
                ZetasMoonDiscountsBase.Instance.ModifyBuyingRate();
            }
            [HarmonyPostfix]
            [HarmonyPatch("SetNewProfitQuota")]
            private static void setNewProfitQuotaPatch()
            {
                ZetasMoonDiscountsBase.Instance.ModifyBuyingRate();
            }
        }
    }
}