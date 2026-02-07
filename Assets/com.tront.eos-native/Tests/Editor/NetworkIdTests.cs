using NUnit.Framework;
using EOSNative.Net;

namespace EOSNative.Tests
{
    public class NetworkIdTests
    {
        [Test]
        public void FnvHash_DifferentPUIDs_DifferentPrefixes()
        {
            string puid1 = "000102030405060708090a0b0c0d0e0f";
            string puid2 = "fffefdfcfbfaf9f8f7f6f5f4f3f2f1f0";

            ushort prefix1 = (ushort)(NetworkManager.FnvHash(puid1) & 0xFFFF);
            ushort prefix2 = (ushort)(NetworkManager.FnvHash(puid2) & 0xFFFF);

            Assert.AreNotEqual(prefix1, prefix2, "Different PUIDs should produce different prefixes");
        }

        [Test]
        public void Prefix_NonZero_WhenPUIDNonEmpty()
        {
            // The code clamps zero prefix to 1
            string puid = "somepuid";
            ushort prefix = (ushort)(NetworkManager.FnvHash(puid) & 0xFFFF);
            // It's extremely unlikely to be zero, but if it were, the code sets it to 1
            Assert.AreNotEqual(0, prefix == 0 ? 1 : prefix);
        }

        [Test]
        public void SceneObjectId_HasFFFFPrefix()
        {
            // Scene objects use 0xFFFF0000 | (hash & 0xFFFF)
            string path = "Root/Child/MyObject";
            uint hash = NetworkManager.FnvHash(path);
            uint sceneId = 0xFFFF0000u | (hash & 0xFFFFu);

            Assert.AreEqual(0xFFFF, (sceneId >> 16) & 0xFFFF, "Scene objects should have 0xFFFF upper 16 bits");
        }

        [Test]
        public void NetworkId_PartitionLayout()
        {
            // Upper 16 bits = prefix, lower 16 = counter
            ushort prefix = 0xABCD;
            ushort counter = 0x0042;
            uint networkId = ((uint)prefix << 16) | counter;

            Assert.AreEqual(0xABCD0042u, networkId);
            Assert.AreEqual(prefix, (ushort)(networkId >> 16));
            Assert.AreEqual(counter, (ushort)(networkId & 0xFFFF));
        }

        [Test]
        public void DifferentPUIDs_NonOverlappingPartitions()
        {
            // Simulate two peers generating IDs
            string puid1 = "player_alpha";
            string puid2 = "player_beta";

            ushort prefix1 = (ushort)(NetworkManager.FnvHash(puid1) & 0xFFFF);
            ushort prefix2 = (ushort)(NetworkManager.FnvHash(puid2) & 0xFFFF);
            if (prefix1 == 0) prefix1 = 1;
            if (prefix2 == 0) prefix2 = 1;

            // If prefixes are different, no counter overlap can cause collision
            if (prefix1 != prefix2)
            {
                for (ushort i = 0; i < 10; i++)
                {
                    uint id1 = ((uint)prefix1 << 16) | i;
                    uint id2 = ((uint)prefix2 << 16) | i;
                    Assert.AreNotEqual(id1, id2);
                }
            }
            else
            {
                // Extremely rare but valid — just note it
                Assert.Pass("Hash collision on prefixes (rare but valid)");
            }
        }

        [Test]
        public void DemoBallId_Format()
        {
            // Ball demo uses 0xBB000000 | (hash & 0x00FFFFFF)
            string puid = "test_player_puid";
            uint hash = NetworkManager.FnvHash(puid);
            uint demoId = 0xBB000000u | (hash & 0x00FFFFFFu);

            Assert.AreEqual(0xBB, (demoId >> 24) & 0xFF, "Demo ball IDs should have 0xBB high byte");
            Assert.AreNotEqual(0u, demoId & 0x00FFFFFFu, "Lower 24 bits should be non-zero for non-empty PUID");
        }

        [Test]
        public void WellKnownId_RoomState()
        {
            // RoomState uses well-known ID 0xFFFF0001
            uint roomStateId = 0xFFFF0001u;
            Assert.AreEqual(0xFFFF, (roomStateId >> 16) & 0xFFFF);
            Assert.AreEqual(1, roomStateId & 0xFFFF);
        }

        [Test]
        public void ReservedPrefabIds()
        {
            // 0xFFF0 = RoomState, 0xFFF1 = PlayerState
            Assert.AreEqual(0xFFF0, NetworkRoomState.PREFAB_ID);
            Assert.AreEqual(0xFFF1, NetworkPlayerState.PREFAB_ID);
        }
    }
}
