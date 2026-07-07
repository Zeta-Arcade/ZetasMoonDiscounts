using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using LethalLevelLoader;
using MrovLib;
using MrovLib.Compatibility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine.Rendering;
using WeatherRegistry;
using WeatherTweaks.Definitions;

namespace ZetasMoonDiscounts
{
    [BepInPlugin(modGUID, modName, modVersion)]
    public class ZetasMoonDiscountsBase : BaseUnityPlugin
    {
        private const string modGUID = "ZetaArcade.ZetasMoonDiscounts";
        public static string GUID => modGUID;
        private const string modName = "ZetasMoonDiscounts";
        private const string modVersion = "1.4.0";
        private readonly Harmony harmony = new Harmony(modGUID);
        public static BepInEx.Logging.ManualLogSource Logger;
        public static ZetasMoonDiscountsBase Instance;
        public ConfigEntry<bool> ApplyBuyingChanges;
        public ConfigEntry<float> NormalSellRate;
        public ConfigEntry<float> ZeroDaysSelLRate;
        public ConfigEntry<bool> ApplyDiscountChanges;
        public ConfigEntry<int> FreeThreshold; //Equal or below is free, above but not equal is "Downgrade" price
        public ConfigEntry<int> FreeThresholdCap; //Equal or below is free, above but not equal is "Downgrade" price
        public ConfigEntry<int> DowngradeCost;
        public ConfigEntry<int> UpgradeThreshold;
        public ConfigEntry<string> IronmanMoons;
        public ConfigEntry<bool> IronmanMode;
        public ConfigEntry<bool> IronmanRestrictRouting;
        public ConfigEntry<bool> IronmanAutoRoute;
        public ConfigEntry<bool> ApplyRiskLevelChanges;
        public ConfigEntry<float> DefaultWeatherRiskLevelIncrease;
        public ConfigEntry<string> WeathersAndRiskLevels;
        public bool hasReroutedCompany;
        public bool hasReroutedMoonPair;
        public int groupCredits;
        //internal ExtendedLevel[] AllLevels { get; private set; } = PatchedContent.ExtendedLevels.ToArray();
        private List<LevelPairedWithPrice> MoonDefaultCosts = new List<LevelPairedWithPrice>();
        private List<LevelPairedWithRisk> MoonDefaultRiskLevels = new List<LevelPairedWithRisk>();
        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            ApplyBuyingChanges = Config.Bind<bool>("Buying Rate Changes", "Toggle Buying Rate Changes", true, "If true, the below 2 configs will be applied to the Company Buying Rate (How many credits you get for selling things)");
            NormalSellRate = Config.Bind<float>("Buying Rate Changes", "Normal Selling Rate", 1f, "The selling rate at the company on all days except the final day, where 1f = 100%, 0.5f = 50% etc.");
            ZeroDaysSelLRate = Config.Bind<float>("Buying Rate Changes", "Zero Days Selling Rate", 1f, "The selling rate at the company on the final day, where 1f = 100%, 0.5f = 50% etc.");
            ApplyDiscountChanges = Config.Bind<bool>("Discount Settings", "Toggle Moon Discounts", true, "If true, the below configs will be applied to moons");
            FreeThreshold = Config.Bind<int>("Discount Settings", "Free Threshold", 300, "When at Moon A, if Moon B is this amount cheaper (or even cheaper!) it becomes free to route to. E.g. with the default value of 300, if Moon A costs 800 and you route to it, then any moons that cost 500 or less are free to route to.");
            FreeThresholdCap = Config.Bind<int>("Discount Settings", "Free Threshold Cap", 1000, "A Cap for the above config, where if a moon costs this amount or more, then other moons cannot route down to it for free. Note moons that are $3500 cheaper than the current moon or more are set to free anyways, bypassing this config");
            DowngradeCost = Config.Bind<int>("Discount Settings", "Downgrade Cost", 50, "When at Moon A, if Moon B is cheaper but isn't cheap enough to become free with the above config (or costs the same amount as Moon A), then it will be this route price instead.");
            UpgradeThreshold = Config.Bind<int>("Discount Settings", "Upgrade Threshold", 500, "When at Moon A, if Moon B is more expensive, then if it's within the range of this config then you can route 'up' to it, only paying the difference in cost to route. E.g. (with default config at 500) if Moon A costs 400, Moon B costs 800, and Moon C costs 1200, then you can route from Moon A to B for 400 (Since its less than 500 in difference), but Moon C will cost the full 1200.");
            IronmanMode = Config.Bind<bool>("Ironman Settings", "Ironman Mode", false, "When true, will just use the Ironman section of the config instead for moon prices. See mod desc. for how Ironman mode works.");
            IronmanRestrictRouting = Config.Bind<bool>("Ironman Settings", "Restrict Routing", false, "When true, you can only route to the current pair of moons, based on the current Quota No. rather than route to any pairing you like.");
            IronmanAutoRoute = Config.Bind<bool>("Ironman Settings", "Auto Routing", false, "When true, if Restrict Routing is enabled too, the ship will automatically route to the next pair of moons upon Quota completion. It will also route to the Company on Deadline day.");
            IronmanMoons = Config.Bind<string>("Ironman Settings", "Ironman Moon List", "", "List of groups of moons, starting with a price for that group of moons, in the format of: Price@Moon1@Moon2..etc. ; e.g. 0@Exp@Vow;300@Assurance@Solace; Only works if Ironman Mode is enabled.");
            ApplyRiskLevelChanges = Config.Bind<bool>("Dynamic Risk Level Settings", "Toggle Risk Level Changes", false, "If true, a moon's risk level will change depending on the current weather and below settings.");
            DefaultWeatherRiskLevelIncrease = Config.Bind<float>("Dynamic Risk Level Settings", "Default Weather Risk Level Increase", 1f, "The default difficulty increase for a weather where unspecified below.");
            WeathersAndRiskLevels = Config.Bind<string>("Dynamic Risk Level Settings", "Weathers and Risk Levels", "", "A list of weathers paired with how much they should increase the difficulty rating, in the format WeatherName@DifficultyIncreaseAmount; e.g. None@0;Rainy@0.2; Be careful to not include any whitespace unless it's a part of the weather name!");
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
        public void rerouteShip(GameNetworkManager networkManager, bool toCompany)
        {
            if (!StartOfRound.Instance) return;
            if (!StartOfRound.Instance.currentLevel) return;
            if (!networkManager.isHostingGame)
            {
                return;
            }

            int newMoonID = 0;
            if (toCompany)
            {
                newMoonID = 3;
                if (StartOfRound.Instance.currentLevelID == 3)
                {
                    hasReroutedCompany = true;
                }
            }
            else
            {
                newMoonID = GetNextQuotaMoonID();
                if (StartOfRound.Instance.currentLevelID == newMoonID)
                {
                    hasReroutedMoonPair = true;
                }
            }
            if (newMoonID == -1)
            {
                return;
            }
            if (toCompany)
            {
                if (StartOfRound.Instance.CanChangeLevels() && TimeOfDay.Instance.daysUntilDeadline == 0 && hasReroutedCompany == false && IronmanAutoRoute.Value && TimeOfDay.Instance.quotaFulfilled < TimeOfDay.Instance.profitQuota)
                {
                    StartOfRound.Instance.ChangeLevelServerRpc(newMoonID, groupCredits);
                    StartOfRound.Instance.ChangeLevel(newMoonID);
                    hasReroutedCompany = true;
                }
            }
            else if (StartOfRound.Instance.CanChangeLevels() && hasReroutedMoonPair == false && IronmanAutoRoute.Value && TimeOfDay.Instance.quotaFulfilled < TimeOfDay.Instance.profitQuota)
            {
                // Should probably set the route price to 0 prior to routing, so that it doesnt charge people with the right configs
                StartOfRound.Instance.ChangeLevelServerRpc(newMoonID, groupCredits);
                StartOfRound.Instance.ChangeLevel(newMoonID); //Needs to be the level ID of an the next pair's first moon
                hasReroutedMoonPair = true;
            }
        }
        public int GetNextQuotaMoonID()
        {
            int newID = -1;
            if (!StartOfRound.Instance) return -1;
            if (!StartOfRound.Instance.currentLevel) return -1;
            int currentQuotaNo = TimeOfDay.Instance.timesFulfilledQuota + 1;
            string[] entries = IronmanMoons.Value.ToString().Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries); //E.g. 0@Exp@Vow;300@Assurance@Solace; --> 0@Exp@Vow + 0@Assurance@Solace (Each are 1 entry)
            int pairNo = 0;
            if (TimeOfDay.Instance != null)
            {
                currentQuotaNo = TimeOfDay.Instance.timesFulfilledQuota + 1;
                Logger.LogDebug($"Current quota number is: " + currentQuotaNo);
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
        public void CalculateMoonPrices()
        {
            if (ApplyDiscountChanges.Value || IronmanMode.Value) //If Discounts on OR in Ironman Mode
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
                                    else if (level.RoutePrice <= DowngradeCost.Value) //Else, if the route price is lower than the downgrade price, just keep it the same
                                    {
                                        //level.RoutePrice = DowngradeCost.Value; 
                                        Logger.LogDebug($"Current moon " + currentExtendedLevel.NumberlessPlanetName + "(" + currentExtendedLevel.RoutePrice + ")" + " --> " + level.NumberlessPlanetName + "(" + level.RoutePrice + ")" + " Meets the downgrade discount criteria but is so cheap it will stay the same price");
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
                            int pairNo = 0;
                            int currentQuotaNo = 0;
                            if (TimeOfDay.Instance != null)
                            {
                                currentQuotaNo = TimeOfDay.Instance.timesFulfilledQuota + 1;
                                Logger.LogDebug($"Current quota number is: " + currentQuotaNo);
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

                                                    if (IronmanRestrictRouting.Value)
                                                    {
                                                        if (currentQuotaNo == pairNo)
                                                        {
                                                            currentConfigLevel.IsRouteLocked = false;
                                                            currentConfigLevel.IsRouteHidden = false;
                                                            Logger.LogDebug($"Moon " + currentConfigLevel.NumberlessPlanetName + " is not locked because it is from pair " + pairNo + " and the current quota is " + currentQuotaNo);
                                                            if (IronmanAutoRoute.Value)
                                                            {
                                                                currentConfigLevel.RoutePrice = 0;
                                                            }
                                                            else
                                                            {
                                                                currentConfigLevel.RoutePrice = value; //Assigns the first "pair" value as the RoutePrice for the moon
                                                            }
                                                        }
                                                        else
                                                        {
                                                            currentConfigLevel.IsRouteLocked = true;
                                                            currentConfigLevel.IsRouteHidden = true;
                                                            currentConfigLevel.RoutePrice = 9999;
                                                            Logger.LogDebug($"Moon " + currentConfigLevel.NumberlessPlanetName + " is locked because it is from pair " + pairNo + " and the current quota is " + currentQuotaNo);
                                                        }
                                                    }
                                                    else
                                                    {
                                                        currentConfigLevel.RoutePrice = value; //Assigns the first "pair" value as the RoutePrice for the moon
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
                            string[] entries = IronmanMoons.Value.ToString().Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries); //E.g. 0@Exp@Vow;300@Assurance@Solace; --> 0@Exp@Vow + 0@Assurance@Solace (Each are 1 entry)
                            foreach (string entry in entries) //For each group of moons in the config...
                            {
                                string[] pairs = entry.Split(new[] { '@' }, StringSplitOptions.RemoveEmptyEntries); //Split them, E.g. 0@Exp@Vow --> 0 + Exp + Vow 
                                int index = 0;
                                foreach (string pair in pairs)
                                {
                                    if (index != 0) //Skips the initial number part of the config
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
                                                        bool safe2 = false;
                                                        try
                                                        {
                                                            ExtendedLevel testLevel = PatchedContent.ExtendedLevels.Find(x => x.SelectableLevel == WeatherRegistry.ConfigHelper.ConvertStringToLevels(pairs[index2])[0]);
                                                            safe2 = true;
                                                        }
                                                        catch (Exception ex)
                                                        {
                                                            Logger.LogError($"Error when trying to parse " + pairs + " in the ExtendedLevels list");
                                                        }
                                                        if (safe2)
                                                        {
                                                            ExtendedLevel currentConfigLevel2 = PatchedContent.ExtendedLevels.Find(x => x.SelectableLevel == WeatherRegistry.ConfigHelper.ConvertStringToLevels(pairs[index2])[0]);
                                                            Logger.LogDebug($"Trying to add " + pairs[index2] + " passed as " + currentConfigLevel2);
                                                            pairedMoonNames.Add(currentConfigLevel2.NumberlessPlanetName); //Always adds 
                                                        }
                                                    }
                                                    index2++;
                                                }
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
                                        level.IsRouteLocked = true;
                                        level.IsRouteHidden = false;
                                    }
                                    else
                                    {
                                        level.RoutePrice = 0;
                                        level.IsRouteLocked = false;
                                        level.IsRouteHidden = false;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        public void CalculateMoonRiskLevels()
        {
            Logger.LogDebug($"Attempting CalculateMoonRiskLevels");
            if (MoonDefaultRiskLevels.Count < 1 || !ApplyRiskLevelChanges.Value) //If array is too small or config is disabled
            {
                Logger.LogDebug($"Default moon risk levels have not been set or the option is disabled, skipping Calculation of moon risk levels");
            }
            else
            {
                Logger.LogDebug($"Default moon risk levels have been set, continuing");
                if (!StartOfRound.Instance) return;
                if (!StartOfRound.Instance.currentLevel) return;
                Logger.LogDebug($"Got current level ref");
                if (ApplyRiskLevelChanges.Value)
                {
                    foreach (ExtendedLevel level in PatchedContent.ExtendedLevels)
                    {
                        level.SelectableLevel.riskLevel = MoonDefaultRiskLevels.Find(x => x.Level == level).OriginalRiskLevel; //Set all risk levels back to default
                        if (level.NumberlessPlanetName == "Gordion" || level.NumberlessPlanetName == "Galetry" || level.NumberlessPlanetName == "Oxyde")
                        {
                            //CURRENT moon is free/company so dont change risk level
                            Logger.LogDebug($"Current moon " + level.NumberlessPlanetName + " is free/company/oxyde, so no risk level changes will be applied");
                        }
                        else
                        {
                            Logger.LogDebug($"Current moon " + level.NumberlessPlanetName + " is not free/company/oxyde, so risk level changes will be applied");
                            // Convert current risk level to a float
                            // Get current weather, find associated float increase or use default if cant be found
                            // Add both values together
                            bool isCombined;
                            float newRiskLevelValue = ConvertRiskLevelToFloat(level.SelectableLevel.riskLevel) + GetWeatherRiskLevelFloat(WeatherManager.GetCurrentWeather(level.SelectableLevel), out isCombined);
                            level.SelectableLevel.riskLevel = ConvertFloatToRiskLevel(newRiskLevelValue);
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
        public float SwapLetterForRiskFloat(char target)
        {
            float output = 0f;
            switch (target)
            {
                case 'X':
                    output = 10;
                    break;
                case 'S':
                    output = 6;
                    break;
                case 'A':
                    output = 5;
                    break;
                case 'B':
                    output = 4;
                    break;
                case 'C':
                    output = 3;
                    break;
                case 'D':
                    output = 2;
                    break;
                case 'E':
                    output = 1;
                    break;
                case 'F':
                    output = 0;
                    break;
                default:
                    output = 0;
                    break;
            }
            return output;
        }
        public float SwapSymbolsForRiskFloat(string target)
        {
            float output = 0f;
            switch (target)
            {
                case "-":
                    output = 0.31f;
                    break;
                case "+":
                    output = 0.66f;
                    break;
                case "++":
                    output = 0.76f;
                    break;
                case "*":
                    output = 0.86f;
                    break;
                default:
                    output = 0.5f;
                    break;
            }
            return output;
        }
        public string SwapIntForRiskLetter(int riskInt)
        {
            string output = "";
            if (riskInt > 10)
            {
                output = "X";
            }
            else if (riskInt > 6)
            {
                output = "S";
            }
            else
            {
                switch (riskInt)
                {
                    case 0:
                        output = "F";
                        break;
                    case 1:
                        output = "E";
                        break;
                    case 2:
                        output = "D";
                        break;
                    case 3:
                        output = "C";
                        break;
                    case 4:
                        output = "B";
                        break;
                    case 5:
                        output = "A";
                        break;
                    case 6:
                        output = "S";
                        break;
                    case 10:
                        output = "X";
                        break;
                    default:
                        output = "?";
                        break;
                }
            }
            return output;
        }
        public string SwapFloatDecimalsForRiskSymbol(float riskFloat)
        {
            string output = "";
            // Take float, e.g. 9.76
            float isolatedDecimals = riskFloat - (int)riskFloat; // E.g. 9.76 - 9 = 0.76
            double isolatedDecRounded = Math.Round(isolatedDecimals, 2);
            if (isolatedDecRounded >= 0.85)
            {
                output = "*";
            }
            else if (isolatedDecRounded >= 0.75)
            {
                output = "++";
            }
            else if (isolatedDecRounded >= 0.65)
            {
                output = "+";
            }
            else if (isolatedDecRounded >= 0.3)
            {
                output = "";
            }
            else
            {
                output = "-";
            }
            return output;
        }
        public float ConvertRiskLevelToFloat(string riskLevel)
        {
            float output = 0;
            float baseAmount = 0;
            float decimalAmount = 0;
            char[] characters = riskLevel.ToCharArray();
            baseAmount = SwapLetterForRiskFloat(characters[0]);
            string riskLevelSymbols = riskLevel.TrimStart(characters[0]);
            if (riskLevelSymbols.Length > 0)
            {
                decimalAmount = SwapSymbolsForRiskFloat(riskLevelSymbols);
            }
            else
            {
                decimalAmount = 0.5f;
            }
            Logger.LogDebug($"Converted " + riskLevel + " to " + baseAmount + " + " + decimalAmount);
            output = baseAmount + decimalAmount;
            return output;
        }
        public string ConvertFloatToRiskLevel(float floatRiskLevel)
        {
            string output = "";
            string part1 = SwapIntForRiskLetter((int)floatRiskLevel); //Doesn't round, just cuts off the decimal part
            string part2 = SwapFloatDecimalsForRiskSymbol(floatRiskLevel);
            output = part1 + part2;
            return output;
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
                // Is likely a combined weather, or could just be that WeatherTweaks is installed
                List<string> encounteredWeathers = new List<string>();
                for (int i = 0; i < tweaksWeather.WeatherTypes.Count; i++) //Loop through every current weather 
                {
                    float currentWeatherRiskLevel = ConvertWeatherNameToRiskFloat(tweaksWeather.WeatherTypes[i].WeatherName);
                    totalRiskLevels += currentWeatherRiskLevel;
                    if (!encounteredWeathers.Contains(tweaksWeather.WeatherTypes[i].WeatherName))
                    {
                        encounteredWeathers.Add(tweaksWeather.WeatherTypes[i].WeatherName);
                        if (tweaksWeather.WeatherTypes[i].WeatherName != "None" || tweaksWeather.WeatherTypes[i].WeatherName != "Clear" || tweaksWeather.WeatherTypes[i].WeatherName != "Cloudy" || tweaksWeather.WeatherTypes[i].WeatherName != "NightShift")
                        {
                            totalWeatherCountBonus += 0.15f; //Only add to this list and incresae total coiunt bonus if its a unique weather
                        }
                    }
                    if (highestRiskLevelFound < currentWeatherRiskLevel)
                    {
                        highestRiskLevelFound = currentWeatherRiskLevel;
                    }
                }
                totalRiskLevelsAverage = totalRiskLevels / tweaksWeather.WeatherTypes.Count;
                totalRiskLevelsAverage += totalWeatherCountBonus;
                combinedWeatherRiskLevel = highestRiskLevelFound + totalWeatherCountBonus;
                output = combinedWeatherRiskLevel;
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
            string[] entries = WeathersAndRiskLevels.Value.ToString().Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries); // Go through config
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
                    currentPartValue = DefaultWeatherRiskLevelIncrease.Value;
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
            float value2 = DefaultWeatherRiskLevelIncrease.Value;
            if (weathersAndRisks.TryGetValue(WeatherRegistry.ConfigHelper.ResolveStringToWeather(weatherName).Name, out value2))
            {
                Logger.LogDebug($"Found weather " + weatherName + " in Dictionary of weathers from config, outputting it's risk level");
                output = value2;
            }
            else
            {
                Logger.LogDebug($"Could not find weather " + weatherName + " in Dictionary of weathers from config, risk level will be default");
                output = DefaultWeatherRiskLevelIncrease.Value;
            }
            return output;
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
        [HarmonyPatch(typeof(Terminal), "Start")] //On Lobby Start, but isn't working currently
        internal class TerminalPatch
        {
            [HarmonyPatch("Start")]
            [HarmonyPostfix]
            private static void TerminalStartPatch(ref Terminal __instance)
            {
                Logger.LogDebug($"Terminal patch detected");
                ZetasMoonDiscountsBase.Instance.SaveDefaultMoonPrices();
                ZetasMoonDiscountsBase.Instance.SaveDefaultMoonRiskLevels();
                ZetasMoonDiscountsBase.Instance.CalculateMoonPrices();
                ZetasMoonDiscountsBase.Instance.CalculateMoonRiskLevels();
            }
            [HarmonyPatch("BeginUsingTerminal")]
            [HarmonyPostfix]
            private static void TerminalBeginUsePatch(ref Terminal __instance)
            {
                Logger.LogDebug($"Terminal begin use patch detected");
                ZetasMoonDiscountsBase.Instance.CalculateMoonPrices();
                ZetasMoonDiscountsBase.Instance.CalculateMoonRiskLevels();
            }
            [HarmonyPatch("Update")]
            [HarmonyPostfix]
            static void gatherGroupCreditsValue(ref int ___groupCredits)
            {
                ZetasMoonDiscountsBase.Instance.groupCredits = ___groupCredits;
            }
        }
        [HarmonyPatch(typeof(StartOfRound))] // On Arrive at moon
        internal class StartOfRoundPatch
        {
            private static GameNetworkManager ___gameNetworkManager = GameNetworkManager.Instance;

            [HarmonyPatch("EndOfGame")]
            [HarmonyPostfix]
            static void ResetHasRerouted()
            {
                if (!___gameNetworkManager.isHostingGame)
                {
                    return;
                }

                ZetasMoonDiscountsBase.Instance.hasReroutedCompany = false;
                ZetasMoonDiscountsBase.Instance.hasReroutedMoonPair = false;
            }
            [HarmonyPatch("PassTimeToNextDay")]
            [HarmonyPostfix]
            private static void PassTimeToNextDay()
            {
                Logger.LogDebug($"PassTimeToNextDay patch detected");
                ZetasMoonDiscountsBase.Instance.CalculateMoonPrices();
                ZetasMoonDiscountsBase.Instance.CalculateMoonRiskLevels();
            }
            [HarmonyPatch("ArriveAtLevel")]
            [HarmonyPostfix]
            private static void ArriveAtLevelPatch()
            {
                Logger.LogDebug($"ArriveAtLevel patch detected");
                ZetasMoonDiscountsBase.Instance.CalculateMoonPrices();
                ZetasMoonDiscountsBase.Instance.CalculateMoonRiskLevels();
            }
            [HarmonyPatch("Update")]
            [HarmonyPostfix]
            private static void AutoShipToCompanyBuilding(StartOfRound __instance)
            {
                if (ZetasMoonDiscountsBase.Instance.IronmanAutoRoute.Value)
                {
                    ZetasMoonDiscountsBase.Instance.rerouteShip(___gameNetworkManager, true);
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
                ZetasMoonDiscountsBase.Instance.CalculateMoonPrices();
                ZetasMoonDiscountsBase.Instance.CalculateMoonRiskLevels();
            }
            [HarmonyPatch("ResetSavedGameValues")]
            [HarmonyPostfix]
            private static void ResetSavedGameValuesPatch()
            {
                ZetasMoonDiscountsBase.Instance.CalculateMoonPrices();
                ZetasMoonDiscountsBase.Instance.CalculateMoonRiskLevels();
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
                ZetasMoonDiscountsBase.Instance.CalculateMoonPrices();
                ZetasMoonDiscountsBase.Instance.CalculateMoonRiskLevels();
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
            [HarmonyPostfix]
            [HarmonyPatch("UpdateProfitQuotaCurrentTime")]
            private static void updateProfitQuotaCurrentTimePatch()
            {
                if (ZetasMoonDiscountsBase.Instance.IronmanAutoRoute.Value)
                {
                    ZetasMoonDiscountsBase.Instance.rerouteShip(___gameNetworkManager, false);
                }
            }
        }
    }
}