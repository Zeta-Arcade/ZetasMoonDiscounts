using LethalModDataLib.Attributes;
using LethalModDataLib.Enums;
using LethalModDataLib.Features;
using LethalModDataLib.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZetasMoonTweaks
{
    public class ModData
    {
        [ModData(SaveWhen.OnSave, LoadWhen.OnLoad, SaveLocation.CurrentSave)]
        public bool hasReroutedCompany;
        [ModData(SaveWhen.OnSave, LoadWhen.OnLoad, SaveLocation.CurrentSave)]
        public bool hasReroutedMoonPair;
        [ModData(SaveWhen.OnSave, LoadWhen.OnLoad, SaveLocation.CurrentSave)]
        public int currentQuotaRoll;
        [ModData(SaveWhen.OnSave, LoadWhen.OnLoad, SaveLocation.CurrentSave)]
        public bool seedSetForQuota;
        public void ZMTSaveData()
        {
            SaveLoadHandler.SaveData(ModDataHelper.GetModDataKey(this, nameof(currentQuotaRoll)));
            SaveLoadHandler.SaveData(ModDataHelper.GetModDataKey(this, nameof(seedSetForQuota)));
            SaveLoadHandler.SaveData(ModDataHelper.GetModDataKey(this, nameof(hasReroutedCompany)));
            SaveLoadHandler.SaveData(ModDataHelper.GetModDataKey(this, nameof(hasReroutedMoonPair)));
        }
        public void ZMTLoadData()
        {
            SaveLoadHandler.LoadData(ModDataHelper.GetModDataKey(this, nameof(currentQuotaRoll)));
            SaveLoadHandler.LoadData(ModDataHelper.GetModDataKey(this, nameof(seedSetForQuota)));
            SaveLoadHandler.LoadData(ModDataHelper.GetModDataKey(this, nameof(hasReroutedCompany)));
            SaveLoadHandler.LoadData(ModDataHelper.GetModDataKey(this, nameof(hasReroutedMoonPair)));
        }
    }
}
