using BepInEx.Configuration;
using CSync.Extensions;
using CSync.Lib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZetasMoonTweaks
{
    public class ConfigHandler : SyncedConfig2<ConfigHandler>
    {
        [SyncedEntryField] public SyncedEntry<bool> ApplyBuyingChanges;
        [SyncedEntryField] public SyncedEntry<float> NormalSellRate;
        [SyncedEntryField] public SyncedEntry<float> ZeroDaysSellRate;
        [SyncedEntryField] public SyncedEntry<bool> ApplyDiscountChanges;
        [SyncedEntryField] public SyncedEntry<int> FreeThreshold; //Equal or below is free, above but not equal is "Downgrade" price
        [SyncedEntryField] public SyncedEntry<int> FreeThresholdCap; //Equal or below is free, above but not equal is "Downgrade" price
        [SyncedEntryField] public SyncedEntry<int> DowngradeCost;
        [SyncedEntryField] public SyncedEntry<int> UpgradeThreshold;
        [SyncedEntryField] public SyncedEntry<string> IronmanMoons;
        [SyncedEntryField] public SyncedEntry<bool> IronmanMode;
        [SyncedEntryField] public SyncedEntry<bool> IronmanRestrictRouting;
        [SyncedEntryField] public SyncedEntry<bool> IronmanAutoRoute;
        [SyncedEntryField] public SyncedEntry<bool> ApplyRiskLevelChanges;
        [SyncedEntryField] public SyncedEntry<float> DefaultWeatherRiskLevelIncrease;
        [SyncedEntryField] public SyncedEntry<string> WeathersAndRiskLevels;
        public ConfigHandler(ConfigFile cfg) : base(PluginInfo.modGUID)
        {
            ApplyBuyingChanges = cfg.BindSyncedEntry<bool>("Buying Rate Changes", "Toggle Buying Rate Changes", true, "If true, the below 2 configs will be applied to the Company Buying Rate (How many credits you get for selling things)");
            NormalSellRate = cfg.BindSyncedEntry<float>("Buying Rate Changes", "Normal Selling Rate", 1f, "The selling rate at the company on all days except the final day, where 1f = 100%, 0.5f = 50% etc.");
            ZeroDaysSellRate = cfg.BindSyncedEntry<float>("Buying Rate Changes", "Zero Days Selling Rate", 1f, "The selling rate at the company on the final day, where 1f = 100%, 0.5f = 50% etc.");
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
            ConfigManager.Register(this);
        }
    }
}
