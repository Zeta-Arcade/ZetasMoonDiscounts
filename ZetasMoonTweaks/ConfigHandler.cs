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
        [SyncedEntryField] public SyncedEntry<bool> noFirstDayShopSales;
        [SyncedEntryField] public SyncedEntry<bool> bodies0Value;
        [SyncedEntryField] public SyncedEntry<Gamemode> gamemode;
        // Interior Name Popup
        [SyncedEntryField] public SyncedEntry<bool> interiorNamePopup;
        [SyncedEntryField] public SyncedEntry<string> interiorNamePrefix;
        [SyncedEntryField] public SyncedEntry<string> interiorTitlePopup;
        [SyncedEntryField] public SyncedEntry<bool> fixInteriorNames;
        public ConfigEntry<string> interiorsAndOverrides;
        [SyncedEntryField] public SyncedEntry<bool> addPercentageInfo;

        // Discounts
        [SyncedEntryField] public SyncedEntry<bool> ApplyDiscountChanges;
        [SyncedEntryField] public SyncedEntry<int> FreeThreshold; //Equal or below is free, above but not equal is "Downgrade" price
        [SyncedEntryField] public SyncedEntry<int> FreeThresholdCap; //Equal or below is free, above but not equal is "Downgrade" price
        [SyncedEntryField] public SyncedEntry<int> DowngradeCost;
        [SyncedEntryField] public SyncedEntry<int> UpgradeThreshold;
        // Ironman / SMHQ / Random Moons
        public ConfigEntry<string> IronmanMoons;
        public ConfigEntry<string> SMHQMoons;
        [SyncedEntryField] public SyncedEntry<bool> IronmanRestrictRouting;
        [SyncedEntryField] public SyncedEntry<bool> IronmanAutoRoute;
        [SyncedEntryField] public SyncedEntry<bool> shouldIronmanLoop;
        [SyncedEntryField] public SyncedEntry<int> randomMoonAmount;
        public ConfigEntry<string> RandomMoons;
        // Risk Levels
        [SyncedEntryField] public SyncedEntry<bool> ApplyRiskLevelChanges;
        [SyncedEntryField] public SyncedEntry<float> DefaultWeatherRiskLevelIncrease;
        public ConfigEntry<string> WeathersAndRiskLevels;
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
        [SyncedEntryField] public SyncedEntry<bool> PreventSellScrapsOnOxyde;
        public enum OxydeLockModes
        {
            None = 0,
            OddOnly = 1,
            EvenOnly = 2,
            Threshold = 3,
            Rng = 4,
        }
        public enum Gamemode
        {
            Normal = 0,
            QuotaIronman = 1,
            DayIronman = 2,
            SMHQ = 3,
            RandomMoons = 4
        }

        public ConfigHandler(ConfigFile cfg) : base(PluginInfo.modGUID)
        {
            AutoCompanyBuilding = cfg.BindSyncedEntry<bool>("General", "Auto Company Building", true, "If true, when there are 0 days left to the Deadline, automatically route the ship back to the Company.");
            noFirstDayShopSales = cfg.BindSyncedEntry<bool>("General", "No First Day Shop Sales", false, "If true, no store items will have sales on them during the first day on the first quota. This prevents reloading to generate an advantageous Day 1 sale on something essential like Shovels.");
            bodies0Value = cfg.BindSyncedEntry<bool>("General", "Bodies 0 Value", false, "If true, player bodies will be worth 0 credits instead of 5. Prevents infinite selling exploits.");
            interiorNamePopup = cfg.BindSyncedEntry<bool>("Interior Name Popup", "Toggle Interior Name Popup", true, "If true, a popup will be shown with the name of the interior when you first enter each day.");
            addPercentageInfo = cfg.BindSyncedEntry<bool>("Interior Name Popup", "Add Percentage Info", true, "If true, the interior name popup will also display the chance of that interior appearing at the end.");
            interiorNamePrefix = cfg.BindSyncedEntry<string>("Interior Name Popup", "Interior Name Prefix", "Interior:", "A prefix to add to the interior names. A space is added after by default.");
            interiorTitlePopup = cfg.BindSyncedEntry<string>("Interior Name Popup", "Popup Title", "Current Interior", "The text at the top of the popup, acting as a Title/Header.");
            fixInteriorNames = cfg.BindSyncedEntry<bool>("Interior Name Popup", "Fix Interior Names", true, "If true, interior names will be cleaned up to remove words like 'Flow' and 'Dungeon', seperate PascalCase etc. Overriden by the name overrides config. Disable this if you want to get the original interior names, which can then be used inside the override config to change to whatever you want.");
            interiorsAndOverrides = cfg.Bind<string>("Interior Name Popup", "Interiors and Overrides", "", "A list of interiors paired with their override names, in the format InteriorName@OverrideName; e.g. Level1Flow@Facility;ResearchStationFlow@ACF Facility;");
            ApplyBuyingChanges = cfg.BindSyncedEntry<bool>("Buying Rate Changes", "Toggle Buying Rate Changes", true, "If true, the below 2 configs will be applied to the Company Buying Rate (How many credits you get for selling things)");
            NormalSellRate = cfg.BindSyncedEntry<float>("Buying Rate Changes", "Normal Selling Rate", 1f, new ConfigDescription("The selling rate at the company on all days except the final day, where 1f = 100%, 0.5f = 50% etc.", new AcceptableValueRange<float>(0f, 10f)));
            ZeroDaysSellRate = cfg.BindSyncedEntry<float>("Buying Rate Changes", "Zero Days Selling Rate", 1f, new ConfigDescription("The selling rate at the company on the final day, where 1f = 100%, 0.5f = 50% etc.", new AcceptableValueRange<float>(0f, 10f)));
            ApplyDiscountChanges = cfg.BindSyncedEntry<bool>("Discount Settings", "Toggle Moon Discounts", true, "If true, the below configs will be applied to moons");
            FreeThreshold = cfg.BindSyncedEntry<int>("Discount Settings", "Free Threshold", 300, "When at Moon A, if Moon B is this amount cheaper (or even cheaper!) it becomes free to route to. E.g. with the default value of 300, if Moon A costs 800 and you route to it, then any moons that cost 500 or less are free to route to.");
            FreeThresholdCap = cfg.BindSyncedEntry<int>("Discount Settings", "Free Threshold Cap", 1000, "A Cap for the above config, where if a moon costs this amount or more, then other moons cannot route down to it for free. Note moons that are $3500 cheaper than the current moon or more are set to free anyways, bypassing this config");
            DowngradeCost = cfg.BindSyncedEntry<int>("Discount Settings", "Downgrade Cost", 50, "When at Moon A, if Moon B is cheaper but isn't cheap enough to become free with the above config (or costs the same amount as Moon A), then it will be this route price instead.");
            UpgradeThreshold = cfg.BindSyncedEntry<int>("Discount Settings", "Upgrade Threshold", 500, "When at Moon A, if Moon B is more expensive, then if it's within the range of this config then you can route 'up' to it, only paying the difference in cost to route. E.g. (with default config at 500) if Moon A costs 400, Moon B costs 800, and Moon C costs 1200, then you can route from Moon A to B for 400 (Since its less than 500 in difference), but Moon C will cost the full 1200.");
            gamemode = cfg.BindSyncedEntry<Gamemode>("General", "Gamemode", Gamemode.Normal, "Which Gamemode to be on. Normal = No changes, QuotaIronman = You are restricted to 1 pair of moons from the Ironman config per quota, DayIronman = Same as QuotaIronman, except you advance through each pair each day rather than upon beating a Quota, SMHQ = 'Single Moon High Quota' means you are restricted to just the moon(s) named in the SMHQ config, and it is free to route to, RandomMoons = A random amount of moons are chosen each quota, which will always be free to route to, with everything else restricted.");
            IronmanRestrictRouting = cfg.BindSyncedEntry<bool>("Ironman Settings", "Restrict Routing", false, "When true, you can only route to the current pair of moons, based on the current Quota No. rather than route to any pairing you like.");
            IronmanAutoRoute = cfg.BindSyncedEntry<bool>("Ironman Settings", "Auto Routing", false, "When true, if Restrict Routing is enabled too, the ship will automatically route to the next pair of moons upon Quota completion. It will also route to the Company on Deadline day.");
            shouldIronmanLoop = cfg.BindSyncedEntry<bool>("Ironman Settings", "Loop Ironman Moons", false, "When true, if you reach the end of the Ironman Moon List, it will loop back to the start of the list. If false, then when you reach the end of the list, you will remain there.");
            IronmanMoons = cfg.Bind<string>("Ironman Settings", "Ironman Moon List", "", "List of groups of moons, starting with a price for that group of moons, in the format of: Price@Moon1@Moon2..etc. ; e.g. 0@Exp@Vow;300@Assurance@Solace; Only works if Ironman is the Gamemode.");
            SMHQMoons = cfg.Bind<string>("SMHQ Settings", "SMHQ Moon List", "", "List of moons that are available in SMHQ mode, in the format of: Moon1@Moon2..etc. ; e.g. Exp@Vow;Assurance@Solace; All other moons will be impossible to route to. Only works if SMHQ is the Gamemode.");
            randomMoonAmount = cfg.BindSyncedEntry<int>("Random Moons Settings", "Random Moon Amount", 3, "How many moons should be randomly chosen to be available each Quota. Only works if RandomMoons is the Gamemode.");
            RandomMoons = cfg.Bind<string>("Random Moons Settings", "Random Moon List", "", "List of moons that can be randomly chosen to be available in Random Moons mode, in the format of: Moon1;Moon2; ..etc. ; e.g. Exp;Vow;Assurance;Solace; Only works if RandomMoons is the Gamemode.");
            ApplyRiskLevelChanges = cfg.BindSyncedEntry<bool>("Dynamic Risk Level Settings", "Toggle Risk Level Changes", false, "If true, a moon's risk level will change depending on the current weather and below settings.");
            DefaultWeatherRiskLevelIncrease = cfg.BindSyncedEntry<float>("Dynamic Risk Level Settings", "Default Weather Risk Level Increase", 0.5f, "The default difficulty increase for a weather where unspecified below.");
            WeathersAndRiskLevels = cfg.Bind<string>("Dynamic Risk Level Settings", "Weathers and Risk Levels", "", "A list of weathers paired with how much they should increase the difficulty rating, in the format WeatherName@DifficultyIncreaseAmount; e.g. None@0;Rainy@0.2; Be careful to not include any whitespace unless it's a part of the weather name!");
            RelockOxydeOnEject = cfg.BindSyncedEntry<bool>("Oxyde Settings", "Relock Oxyde on Eject", true, "If true, then when an ejection occurs, Oxyde will be Locked again for the save file. This is a bug fix for CodeRebirth.");
            OxydeTabletForceUnlocksOxyde = cfg.BindSyncedEntry<bool>("Oxyde Settings", "Oxyde Tablet Force Unlocks Oxyde", true, "If true, then when an Oxyde Tablet is used, Oxyde will be guaranteed to be unlocked for this Quota, regardless of the Lock Mode, making the Tablet still useful after Oxyde is initially discovered.");
            DestroyOxydeSellScrapsInOrbit = cfg.BindSyncedEntry<bool>("Oxyde Settings", "Destroy Selling Scraps when leaving", true, "If true, then when the ship is leaving Oxyde, any Shredded Scraps / Compacted Cubes will be destroyed to prevent bringing them into orbit, which prevents cheesing the sell process by amassing these scraps early.");
            PreventSellScrapsOnOxyde = cfg.BindSyncedEntry<bool>("Oxyde Settings", "Prevent Extra Loot on Oxyde", false, "If true, this patches it so Vanilla Nutcrackers/Butlers weapons, SellBodiesFixed Bodies and MoreCounterplay Coilhead Heads do not drop on Oxyde, to prevent infinite farming exploits. Warning: May be incompatible with other spawning methods on Oxyde, such as storage mods retrieving those items or debug mods trying to spawn them in.");
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
