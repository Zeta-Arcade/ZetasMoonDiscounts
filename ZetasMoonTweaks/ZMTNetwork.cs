using LethalNetworkAPI;
using Unity.Netcode;

namespace ZetasMoonTweaks
{
    internal class ZMTNetwork
    {
        public static LNetworkMessage<SeedMessage> NewSeedMessage;
        public static LNetworkMessage<ItemPickup> ItemDestroyMessage;
        public static LNetworkMessage<OxydeTablet> OxydeTabletMessage;
        public static LNetworkMessage<RandomMoonsMessage> RandomMoonsNetworkMessage;
        public static LNetworkMessage<RandomMoonsRequest> RandomMoonsRequestMessage;
        public static void Init()
        {
            NewSeedMessage = LNetworkMessage<SeedMessage>.Create("SeedSent", onClientReceived: OnNewSeed);
            ItemDestroyMessage = LNetworkMessage<ItemPickup>.Create("ItemDestroy", onServerReceived: OnItemDestroy);
            OxydeTabletMessage = LNetworkMessage<OxydeTablet>.Create("OxydeTablet", onServerReceived: OnOxydeTablet);
            RandomMoonsNetworkMessage = LNetworkMessage<RandomMoonsMessage>.Create("RandomMoonsSent", onClientReceived: OnRandomMoonsReceived);
            RandomMoonsRequestMessage = LNetworkMessage<RandomMoonsRequest>.Create("RandomMoonsRequest", onServerReceived: OnRandomMoonsRequest);
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
            NewSeedMessage.SendClients(new SeedMessage { seed = seed });
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
            OxydeTabletMessage.SendClients(new OxydeTablet { NewUnlockStatus = message.NewUnlockStatus });
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
        public class RandomMoonsMessage
        {
            public string selectedMoons;
            public int quotaNumber;
        }
        public class RandomMoonsRequest
        {
            public bool requestSelection;
        }
        private static void OnRandomMoonsReceived(RandomMoonsMessage message)
        {
            if (NetworkManager.Singleton.IsHost) return;
            if (ZMTBase.Instance == null) return;
            if (ZMTBase.Config.gamemode.Value != ConfigHandler.Gamemode.RandomMoons) return;
            if (string.IsNullOrWhiteSpace(message.selectedMoons)) return;
            ZMTBase.Logger.LogInfo($"Received Random Moons selection from host: " + $"Quota {message.quotaNumber + 1}, Moons: {message.selectedMoons}");
            ZMTBase.Instance.SetRandomMoons(message.selectedMoons, message.quotaNumber);
            Patches.StartOfRoundPatch.RandomMoonsSyncReceived();
        }
        private static void OnRandomMoonsRequest(RandomMoonsRequest message, ulong senderClientId)
        {
            if (!NetworkManager.Singleton.IsHost) return;
            if (ZMTBase.Instance == null) return;
            if (ZMTBase.Config.gamemode.Value != ConfigHandler.Gamemode.RandomMoons) return;
            if (ZMTBase.Instance.currentlySelectedRandomMoons.Count == 0)
            {
                ZMTBase.Logger.LogWarning($"Random Moons request received from client {senderClientId}, but no moons have been selected!");
                return;
            }
            string selectedMoons = string.Join(";", ZMTBase.Instance.currentlySelectedRandomMoons);
            int quotaNumber = ZMTBase.Instance.lastRandomMoonsQuota;
            ZMTBase.Logger.LogInfo($"Sending Random Moons selection to client {senderClientId}: " + $"Quota {quotaNumber + 1}, Moons: {selectedMoons}");
            RandomMoonsNetworkMessage.SendClient(new RandomMoonsMessage{selectedMoons = selectedMoons, quotaNumber = quotaNumber}, senderClientId);
        }
        public static void RequestRandomMoons()
        {
            if (!NetworkManager.Singleton.IsListening) return;
            if (NetworkManager.Singleton.IsHost) return;
            if (ZMTBase.Config.gamemode.Value != ConfigHandler.Gamemode.RandomMoons) return;
            ZMTBase.Logger.LogDebug($"Requesting Random Moons selection from host");
            RandomMoonsRequestMessage.SendServer(new RandomMoonsRequest{requestSelection = true});
        }
        public static void SendRandomMoons(string selectedMoons, int quotaNumber)
        {
            if (!NetworkManager.Singleton.IsListening) return;
            if (!NetworkManager.Singleton.IsHost) return;
            RandomMoonsNetworkMessage.SendClients(new RandomMoonsMessage{selectedMoons = selectedMoons, quotaNumber = quotaNumber});
        }
    }
}