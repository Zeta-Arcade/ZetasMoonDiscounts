using LethalNetworkAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Netcode;

namespace ZetasMoonTweaks
{
    internal class ZMTNetwork
    {
        public static LNetworkMessage<SeedMessage> NewSeedMessage;
        public static LNetworkMessage<ItemPickup> ItemDestroyMessage;
        public static LNetworkMessage<OxydeTablet> OxydeTabletMessage;
        public static void Init()
        {
            NewSeedMessage = LNetworkMessage<SeedMessage>.Create("SeedSent", onClientReceived: OnNewSeed);
            ItemDestroyMessage = LNetworkMessage<ItemPickup>.Create("ItemDestroy", onServerReceived: OnItemDestroy);
            OxydeTabletMessage = LNetworkMessage<OxydeTablet>.Create("OxydeTablet", onServerReceived: OnOxydeTablet);
        }
        private static void OnNewSeed(SeedMessage message) //Everyone reacts to receiving the new seed
        {
            if (ZMTBase.Instance == null) return;
            ZMTBase.Instance.SetNewSeed(message.seed);
        }
        public static void SendNewSeed(int seed) 
        {
            if (!NetworkManager.Singleton.IsListening) return; //Only host sends the signal
            if (!NetworkManager.Singleton.IsHost) return;
            NewSeedMessage.SendClients(new SeedMessage {seed = seed });
        }
        public class SeedMessage
        {
            public int seed;
        }
        
        private static void OnOxydeTablet(OxydeTablet message, ulong senderClientId)
        {
            if (!NetworkManager.Singleton.IsHost) return;
            if (ZMTBase.Instance == null) return;
            ZMTBase.Instance.UpdateOxydeTabletStatus(message.NewUnlockStatus);
            OxydeTabletMessage.SendClients(new OxydeTablet{NewUnlockStatus = message.NewUnlockStatus});
        }
        public static void SendOxydeTablet(bool newUnlockStatus)
        {
            if (!NetworkManager.Singleton.IsListening) return;
            OxydeTabletMessage.SendServer(new OxydeTablet { NewUnlockStatus = newUnlockStatus });
        }
        public class ItemPickup
        {
            public ulong playerID;
            public int slot;
        }
        private static void OnItemDestroy(ItemPickup message, ulong senderClientId)
        {
            if (!NetworkManager.Singleton.IsHost) return;
            if (ZMTBase.Instance == null) return;
            ZMTBase.Instance.DestroyScrapInSlot(message.playerID, message.slot);
        }
        public static void SendItemDestroy(ulong playerID, int slot)
        {
            if (!NetworkManager.Singleton.IsListening) return;
            ItemDestroyMessage.SendServer(new ItemPickup { playerID = playerID, slot = slot });
        }
        public class OxydeTablet
        {
            public bool NewUnlockStatus;
        }
    }
}
