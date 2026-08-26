using BepInEx.Configuration;
using CSync.Extensions;
using CSync.Lib;

namespace ZetasMoonTweaks
{
    public class ConfigHandler : SyncedConfig2<ConfigHandler>
    {
        // General
        [SyncedEntryField] public SyncedEntry<bool> ApplyBuyingChanges;
        [SyncedEntryField] public SyncedEntry<float> NormalSellRate;
        [SyncedEntryField] public SyncedEntry<float> ZeroDaysSellRate;
        [SyncedEntryField] public SyncedEntry<bool> AutoCompanyBuilding;
        // Discounts
        [SyncedEntryField] public SyncedEntry<bool> ApplyDiscountChanges;
        [SyncedEntryField] public SyncedEntry<int> FreeThreshold; //Equal or below is free, above but not equal is "Downgrade" price
        [SyncedEntryField] public SyncedEntry<int> FreeThresholdCap; //Equal or below is free, above but not equal is "Downgrade" price
        [SyncedEntryField] public SyncedEntry<int> DowngradeCost;
        [SyncedEntryField] public SyncedEntry<int> UpgradeThreshold;
        // Ironman
        [SyncedEntryField] public SyncedEntry<string> IronmanMoons;
        [SyncedEntryField] public SyncedEntry<bool> IronmanMode;
        [SyncedEntryField] public SyncedEntry<bool> IronmanRestrictRouting;
        [SyncedEntryField] public SyncedEntry<bool> IronmanAutoRoute;
        // Risk Levels
        [SyncedEntryField] public SyncedEntry<bool> ApplyRiskLevelChanges;
        [SyncedEntryField] public SyncedEntry<float> DefaultWeatherRiskLevelIncrease;
        [SyncedEntryField] public SyncedEntry<string> WeathersAndRiskLevels;
        // Oxyde
        [SyncedEntryField] public SyncedEntry<bool> RelockOxydeOnEject;
        [SyncedEntryField] public SyncedEntry<bool> OxydeTabletForceUnlocksOxyde;
        [SyncedEntryField] public SyncedEntry<OxydeLockModes> OxydeLockMode;
        [SyncedEntryField] public SyncedEntry<int> OxydeUnlockThreshold;
        [SyncedEntryField] public SyncedEntry<int> OxydeUnlockChance;
        [SyncedEntryField] public SyncedEntry<int> ZeroDaysOxydeCost;
        [SyncedEntryField] public SyncedEntry<int> OneDayOxydeCost;
        [SyncedEntryField] public SyncedEntry<int> TwoDayOxydeCost;
        [SyncedEntryField] public SyncedEntry<int> ThreeDayOxydeCost;
        [SyncedEntryField] public SyncedEntry<bool> DestroyOxydeSellScrapsInOrbit;
        public enum OxydeLockModes
        {
            None = 0,
            OddOnly = 1,
            EvenOnly = 2,
            Threshold = 3,
            Rng = 4,
        }

        public ConfigHandler(ConfigFile cfg) : base(PluginInfo.modGUID)
        {
            AutoCompanyBuilding = cfg.BindSyncedEntry<bool>("General", "Auto Company Building", true, "If true, when there are 0 days left to the Deadline, automatically route the ship back to the Company.");
            ApplyBuyingChanges = cfg.BindSyncedEntry<bool>("Buying Rate Changes", "Toggle Buying Rate Changes", true, "If true, the below 2 configs will be applied to the Company Buying Rate (How many credits you get for selling things)");
            NormalSellRate = cfg.BindSyncedEntry<float>("Buying Rate Changes", "Normal Selling Rate", 1f, new ConfigDescription("The selling rate at the company on all days except the final day, where 1f = 100%, 0.5f = 50% etc.", new AcceptableValueRange<float>(0f, 10f)));
            ZeroDaysSellRate = cfg.BindSyncedEntry<float>("Buying Rate Changes", "Zero Days Selling Rate", 1f, new ConfigDescription("The selling rate at the company on the final day, where 1f = 100%, 0.5f = 50% etc.", new AcceptableValueRange<float>(0f, 10f)));
            ApplyDiscountChanges = cfg.BindSyncedEntry<bool>("Discount Settings", "Toggle Moon Discounts", true, "If true, the below configs will be applied to moons");
            FreeThreshold = cfg.BindSyncedEntry<int>("Discount Settings", "Free Threshold", 300, "When at Moon A, if Moon B is this amount cheaper (or even cheaper!) it becomes free to route to. E.g. with the default value of 300, if Moon A costs 800 and you route to it, then any moons that cost 500 or less are free to route to.");
            FreeThresholdCap = cfg.BindSyncedEntry<int>("Discount Settings", "Free Threshold Cap", 1000, "A Cap for the above config, where if a moon costs this amount or more, then other moons cannot route down to it for free. Note moons that are $3500 cheaper than the current moon or more are set to free anyways, bypassing this config");
            DowngradeCost = cfg.BindSyncedEntry<int>("Discount Settings", "Downgrade Cost", 50, "When at Moon A, if Moon B is cheaper but isn't cheap enough to become free with the above config (or costs the same amount as Moon A), then it will be this route price instead.");
            UpgradeThreshold = cfg.BindSyncedEntry<int>("Discount Settings", "Upgrade Threshold", 500, "When at Moon A, if Moon B is more expensive, then if it's within the range of this config then you can route 'up' to it, only paying the difference in cost to route. E.g. (with default config at 500) if Moon A costs 400, Moon B costs 800, and Moon C costs 1200, then you can route from Moon A to B for 400 (Since its less than 500 in difference), but Moon C will cost the full 1200.");
            IronmanMode = cfg.BindSyncedEntry<bool>("Ironman Settings", "Ironman Mode", false, "When true, will just use the Ironman section of the config instead for moon prices. See mod desc. for how Ironman mode works.");
            IronmanRestrictRouting = cfg.BindSyncedEntry<bool>("Ironman Settings", "Restrict Routing", false, "When true, you can only route to the current pair of moons, based on the current Quota No. rather than route to any pairing you like.");
            IronmanAutoRoute = cfg.BindSyncedEntry<bool>("Ironman Settings", "Auto Routing", false, "When true, if Restrict Routing is enabled too, the ship will automatically route to the next pair of moons upon Quota completion. It will also route to the Company on Deadline day.");
            IronmanMoons = cfg.BindSyncedEntry<string>("Ironman Settings", "Ironman Moon List", "", "List of groups of moons, starting with a price for that group of moons, in the format of: Price@Moon1@Moon2..etc. ; e.g. 0@Exp@Vow;300@Assurance@Solace; Only works if Ironman Mode is enabled.");
            ApplyRiskLevelChanges = cfg.BindSyncedEntry<bool>("Dynamic Risk Level Settings", "Toggle Risk Level Changes", false, "If true, a moon's risk level will change depending on the current weather and below settings.");
            DefaultWeatherRiskLevelIncrease = cfg.BindSyncedEntry<float>("Dynamic Risk Level Settings", "Default Weather Risk Level Increase", 1f, "The default difficulty increase for a weather where unspecified below.");
            WeathersAndRiskLevels = cfg.BindSyncedEntry<string>("Dynamic Risk Level Settings", "Weathers and Risk Levels", "", "A list of weathers paired with how much they should increase the difficulty rating, in the format WeatherName@DifficultyIncreaseAmount; e.g. None@0;Rainy@0.2; Be careful to not include any whitespace unless it's a part of the weather name!");
            RelockOxydeOnEject = cfg.BindSyncedEntry<bool>("Oxyde Settings", "Relock Oxyde on Eject", true, "If true, then when an ejection occurs, Oxyde will be Locked again for the save file. This is a bug fix for CodeRebirth.");
            OxydeTabletForceUnlocksOxyde = cfg.BindSyncedEntry<bool>("Oxyde Settings", "Oxyde Tablet Force Unlocks Oxyde", true, "If true, then when an Oxyde Tablet is used, Oxyde will be guaranteed to be unlocked for this Quota, regardless of the Lock Mode, making the Tablet still useful after Oxyde is initially discovered.");
            DestroyOxydeSellScrapsInOrbit = cfg.BindSyncedEntry<bool>("Oxyde Settings", "Destroy Selling Scraps when leaving", true, "If true, then when the ship is leaving Oxyde, any Shredded Scraps / Compacted Cubes will be destroyed to prevent bringing them into orbit, which prevents cheesing the sell process by amassing these scraps early.");
            OxydeLockMode = cfg.BindSyncedEntry<OxydeLockModes>("Oxyde Settings", "Oxyde Lock Mode", OxydeLockModes.None, "Different ways to control when Oxyde is 'Locked' by setting the Route cost to 99999. 'None' = No changes, 'OddOnly' / 'EvenOnly' = Oxyde will only be available on Odd/Even numbered Quotas respectively, 'Threshold' = The quota must reach a certain threshold for Oxyde to become available, 'Rng' = Oxyde has a random chance to be available each Quota.");
            OxydeUnlockThreshold = cfg.BindSyncedEntry<int>("Oxyde Settings", "Oxyde Unlock Threshold", 3000, "How high the Quota should be before Oxyde becomes available, if 'Oxyde Lock Mode' is set to 'Threshold'");
            OxydeUnlockChance = cfg.BindSyncedEntry<int>("Oxyde Settings", "Oxyde Unlock Chance", 75, new ConfigDescription("How likely Oxyde will be available for that Quota, if 'Oxyde Lock Mode' is set to 'Rng'. 100 = 100 % chance.", new AcceptableValueRange<int>(0, 100)));
            ZeroDaysOxydeCost = cfg.BindSyncedEntry<int>("Oxyde Settings", "0 Days Oxyde Cost", -1, "How much Oxyde should cost when there are 0 days left for the Quota. Leave as -1 to not change it.");
            OneDayOxydeCost = cfg.BindSyncedEntry<int>("Oxyde Settings", "1 Days Oxyde Cost", -1, "How much Oxyde should cost when there are 1 days left for the Quota. Leave as -1 to not change it.");
            TwoDayOxydeCost = cfg.BindSyncedEntry<int>("Oxyde Settings", "2 Days Oxyde Cost", -1, "How much Oxyde should cost when there are 2 days left for the Quota. Leave as -1 to not change it.");
            ThreeDayOxydeCost = cfg.BindSyncedEntry<int>("Oxyde Settings", "3 Days Oxyde Cost", -1, "How much Oxyde should cost when there are 3 days (or more!) left for the Quota. Leave as -1 to not change it.");
            ConfigManager.Register(this);
        }
    }
}
