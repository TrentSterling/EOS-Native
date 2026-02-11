using NUnit.Framework;
using EOSNative.Net;
using EOSNative.P2P;
using UnityEngine;

namespace EOSNative.Tests
{
    // ─── Test NetworkBehaviour subclasses ───
    // Use explicit InitForTest() instead of Awake() to avoid EditMode lifecycle issues.
    // base.Awake() still runs (sets Net), but SyncVar registration is explicit.

    public class HealthBehaviour : NetworkBehaviour
    {
        public SyncVar<int> Health;
        public SyncVar<float> Speed;

        public void InitForTest()
        {
            Health = Sync(100);
            Speed = Sync(5.0f);
        }
    }

    public class CombatBehaviour : NetworkBehaviour
    {
        public SyncVar<string> WeaponName;
        public SyncVar<int> Ammo;
        public SyncVar<bool> IsReloading;

        public void InitForTest()
        {
            WeaponName = Sync("Pistol");
            Ammo = Sync(30);
            IsReloading = Sync(false);
        }
    }

    public class EmptyBehaviour : NetworkBehaviour
    {
        // No SyncVars — tests that empty NBs are handled gracefully
    }

    /// <summary>
    /// Tests per-NetworkBehaviour scoped SyncVars and the sectioned wire format (v2.40.0).
    /// Verifies: NB SyncVar pools, ComponentIndex assignment, sectioned serialization,
    /// dirty tracking per-NB, and mixed self+NB SyncVar scenarios.
    /// </summary>
    public class PerNBSyncVarTests
    {
        [TearDown]
        public void TearDown()
        {
            foreach (var name in new[] { "NetworkManager", "EOSP2PManager", "EOSLobbyManager", "NetworkSceneManager" })
            {
                var obj = GameObject.Find(name);
                if (obj != null) Object.DestroyImmediate(obj);
            }
        }

        /// <summary>
        /// Helper: create a GO with NetworkObject + HealthBehaviour, SyncVars initialized.
        /// Adds NO first so NB.Awake() finds it via GetComponent.
        /// </summary>
        private (NetworkObject netObj, HealthBehaviour nb) CreateHealth()
        {
            var go = new GameObject("HealthTest");
            var netObj = go.AddComponent<NetworkObject>();
            var nb = go.AddComponent<HealthBehaviour>();
            nb.InitForTest();
            return (netObj, nb);
        }

        private (NetworkObject netObj, CombatBehaviour nb) CreateCombat()
        {
            var go = new GameObject("CombatTest");
            var netObj = go.AddComponent<NetworkObject>();
            var nb = go.AddComponent<CombatBehaviour>();
            nb.InitForTest();
            return (netObj, nb);
        }

        #region Sync<T> Registration

        [Test]
        public void NB_Sync_RegistersSyncVars()
        {
            var (netObj, nb) = CreateHealth();
            Assert.AreEqual(2, nb.SyncVarCount);
            Assert.AreEqual(100, nb.Health.Value);
            Assert.AreEqual(5.0f, nb.Speed.Value);
            Object.DestroyImmediate(netObj.gameObject);
        }

        [Test]
        public void NB_Sync_IndicesAreCorrect()
        {
            var (netObj, nb) = CreateCombat();
            Assert.AreEqual(0, nb.WeaponName.Index);
            Assert.AreEqual(1, nb.Ammo.Index);
            Assert.AreEqual(2, nb.IsReloading.Index);
            Object.DestroyImmediate(netObj.gameObject);
        }

        [Test]
        public void NB_SyncVarsAre_ScopedPerBehaviour()
        {
            var go = new GameObject("MultiNB");
            var netObj = go.AddComponent<NetworkObject>();
            var health = go.AddComponent<HealthBehaviour>();
            var combat = go.AddComponent<CombatBehaviour>();
            health.InitForTest();
            combat.InitForTest();

            // Each NB has its own SyncVar pool
            Assert.AreEqual(2, health.SyncVarCount); // Health, Speed
            Assert.AreEqual(3, combat.SyncVarCount); // WeaponName, Ammo, IsReloading

            // Indices are per-NB, not global
            Assert.AreEqual(0, health.Health.Index);
            Assert.AreEqual(1, health.Speed.Index);
            Assert.AreEqual(0, combat.WeaponName.Index);
            Assert.AreEqual(1, combat.Ammo.Index);
            Assert.AreEqual(2, combat.IsReloading.Index);

            Object.DestroyImmediate(go);
        }

        #endregion

        #region ComponentIndex Assignment

        [Test]
        public void ComponentIndex_AssignedByNotifyNetworkSpawn()
        {
            var go = new GameObject("CompIdx");
            var netObj = go.AddComponent<NetworkObject>();
            var health = go.AddComponent<HealthBehaviour>();
            var combat = go.AddComponent<CombatBehaviour>();
            health.InitForTest();
            combat.InitForTest();

            netObj.NotifyNetworkSpawn();

            Assert.AreEqual(0, health.ComponentIndex);
            Assert.AreEqual(1, combat.ComponentIndex);

            Object.DestroyImmediate(go);
        }

        [Test]
        public void ComponentIndex_ThreeNBs()
        {
            var go = new GameObject("ThreeNBs");
            var netObj = go.AddComponent<NetworkObject>();
            var a = go.AddComponent<HealthBehaviour>();
            var b = go.AddComponent<CombatBehaviour>();
            var c = go.AddComponent<EmptyBehaviour>();
            a.InitForTest();
            b.InitForTest();

            netObj.NotifyNetworkSpawn();

            Assert.AreEqual(0, a.ComponentIndex);
            Assert.AreEqual(1, b.ComponentIndex);
            Assert.AreEqual(2, c.ComponentIndex);

            Object.DestroyImmediate(go);
        }

        #endregion

        #region Dirty Tracking Per-NB

        [Test]
        public void NB_MarksDirtyOnChange()
        {
            var (netObj, nb) = CreateHealth();
            Assert.IsFalse(nb.IsDirty);
            nb.Health.Value = 50;
            Assert.IsTrue(nb.IsDirty);
            Object.DestroyImmediate(netObj.gameObject);
        }

        [Test]
        public void NB_DirtyMask_OnlyChangedBits()
        {
            var (netObj, nb) = CreateCombat();
            // Only change Ammo (index 1)
            nb.Ammo.Value = 15;

            uint mask = nb.BuildDirtyMask();
            Assert.AreEqual(0x02u, mask); // bit 1 set

            Object.DestroyImmediate(netObj.gameObject);
        }

        [Test]
        public void NB_DirtyMask_MultipleBits()
        {
            var (netObj, nb) = CreateCombat();
            nb.WeaponName.Value = "Shotgun";  // index 0
            nb.IsReloading.Value = true;       // index 2

            uint mask = nb.BuildDirtyMask();
            Assert.AreEqual(0x05u, mask); // bits 0 and 2

            Object.DestroyImmediate(netObj.gameObject);
        }

        [Test]
        public void NB_ClearDirty_ResetsAll()
        {
            var (netObj, nb) = CreateCombat();
            nb.WeaponName.Value = "Rifle";
            nb.Ammo.Value = 10;
            Assert.IsTrue(nb.IsDirty);

            nb.ClearDirtyNB();
            Assert.IsFalse(nb.IsDirty);
            Assert.IsFalse(nb.WeaponName.IsDirty);
            Assert.IsFalse(nb.Ammo.IsDirty);

            Object.DestroyImmediate(netObj.gameObject);
        }

        [Test]
        public void NB_DirtyBubblesToNetworkObject()
        {
            var go = new GameObject("BubbleTest");
            var netObj = go.AddComponent<NetworkObject>();
            var nb = go.AddComponent<HealthBehaviour>();
            nb.InitForTest();

            netObj.NotifyNetworkSpawn();
            nb.Health.Value = 25;

            var w = new NetWriter();
            netObj.SerializeDirty(w);
            var data = w.ToArray();
            // sectionCount should be >= 1 (NB section present)
            Assert.IsTrue(data.Length > 1, "SerializeDirty should have content for dirty NB");
            Assert.IsTrue(data[0] >= 1, "sectionCount should be at least 1");

            Object.DestroyImmediate(go);
        }

        #endregion

        #region NB Serialization Round-Trip (SerializeDirtyNB / DeserializeDirtyNB)

        [Test]
        public void NB_SerializeDirty_RoundTrip()
        {
            var (_, nb) = CreateHealth();
            nb.Health.Value = 42;
            nb.Speed.Value = 10.5f;

            var w = new NetWriter();
            nb.SerializeDirtyNB(w);

            var (_, nb2) = CreateHealth();
            nb2.DeserializeDirtyNB(new NetReader(w.ToArray()));
            Assert.AreEqual(42, nb2.Health.Value);
            Assert.AreEqual(10.5f, nb2.Speed.Value, 0.001f);

            Object.DestroyImmediate(nb.gameObject);
            Object.DestroyImmediate(nb2.gameObject);
        }

        [Test]
        public void NB_SerializeDirty_OnlyDirtyFields()
        {
            var (_, nb) = CreateCombat();
            // Only dirty Ammo
            nb.Ammo.Value = 5;

            var w = new NetWriter();
            nb.SerializeDirtyNB(w);

            var (_, nb2) = CreateCombat();
            nb2.DeserializeDirtyNB(new NetReader(w.ToArray()));
            Assert.AreEqual("Pistol", nb2.WeaponName.Value); // unchanged
            Assert.AreEqual(5, nb2.Ammo.Value);               // updated
            Assert.AreEqual(false, nb2.IsReloading.Value);     // unchanged

            Object.DestroyImmediate(nb.gameObject);
            Object.DestroyImmediate(nb2.gameObject);
        }

        [Test]
        public void NB_SerializeAll_RoundTrip()
        {
            var (_, nb) = CreateCombat();

            var w = new NetWriter();
            nb.SerializeAllNB(w);

            var (_, nb2) = CreateCombat();
            // Overwrite with different values
            nb2.WeaponName.SetInternal("???");
            nb2.Ammo.SetInternal(0);
            nb2.IsReloading.SetInternal(true);

            nb2.DeserializeAllNB(new NetReader(w.ToArray()));
            Assert.AreEqual("Pistol", nb2.WeaponName.Value);
            Assert.AreEqual(30, nb2.Ammo.Value);
            Assert.AreEqual(false, nb2.IsReloading.Value);

            Object.DestroyImmediate(nb.gameObject);
            Object.DestroyImmediate(nb2.gameObject);
        }

        #endregion

        #region Sectioned Wire Format (NetworkObject level)

        [Test]
        public void Sectioned_SelfOnly_NoBehaviours()
        {
            var go = new GameObject("SelfOnly");
            var netObj = go.AddComponent<NetworkObject>();
            var sv = netObj.Sync(42);
            sv.Value = 99;

            var w = new NetWriter();
            netObj.SerializeDirty(w);
            var data = w.ToArray();

            Assert.AreEqual(1, data[0], "sectionCount should be 1 (self only)");
            Assert.AreEqual(0xFF, data[1], "componentIndex should be SELF (0xFF)");

            // Deserialize
            var go2 = new GameObject("SelfOnly2");
            var netObj2 = go2.AddComponent<NetworkObject>();
            var sv2 = netObj2.Sync(0);

            netObj2.DeserializeDirty(new NetReader(data));
            Assert.AreEqual(99, sv2.Value);

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(go2);
        }

        [Test]
        public void Sectioned_NBOnly_NoSelfSyncVars()
        {
            var go = new GameObject("NBOnly");
            var netObj = go.AddComponent<NetworkObject>();
            var nb = go.AddComponent<HealthBehaviour>();
            nb.InitForTest();
            netObj.NotifyNetworkSpawn();

            nb.Health.Value = 50;

            var w = new NetWriter();
            netObj.SerializeDirty(w);
            var data = w.ToArray();

            Assert.AreEqual(1, data[0], "sectionCount should be 1 (NB only, no self SyncVars)");
            Assert.AreEqual(0, data[1], "componentIndex should be 0 (first NB)");

            // Deserialize
            var go2 = new GameObject("NBOnly2");
            var netObj2 = go2.AddComponent<NetworkObject>();
            var nb2 = go2.AddComponent<HealthBehaviour>();
            nb2.InitForTest();
            netObj2.NotifyNetworkSpawn();

            netObj2.DeserializeDirty(new NetReader(data));
            Assert.AreEqual(50, nb2.Health.Value);

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(go2);
        }

        [Test]
        public void Sectioned_SelfPlusNB()
        {
            var go = new GameObject("SelfPlusNB");
            var netObj = go.AddComponent<NetworkObject>();
            var selfSv = netObj.Sync(0);
            var nb = go.AddComponent<HealthBehaviour>();
            nb.InitForTest();
            netObj.NotifyNetworkSpawn();

            selfSv.Value = 777;
            nb.Health.Value = 50;

            var w = new NetWriter();
            netObj.SerializeDirty(w);
            var data = w.ToArray();

            Assert.AreEqual(2, data[0], "sectionCount should be 2 (self + NB)");
            Assert.AreEqual(0xFF, data[1], "first section should be SELF");

            // Deserialize
            var go2 = new GameObject("SelfPlusNB2");
            var netObj2 = go2.AddComponent<NetworkObject>();
            var selfSv2 = netObj2.Sync(0);
            var nb2 = go2.AddComponent<HealthBehaviour>();
            nb2.InitForTest();
            netObj2.NotifyNetworkSpawn();

            netObj2.DeserializeDirty(new NetReader(data));
            Assert.AreEqual(777, selfSv2.Value);
            Assert.AreEqual(50, nb2.Health.Value);

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(go2);
        }

        [Test]
        public void Sectioned_TwoNBs_OneDirty()
        {
            var go = new GameObject("TwoNBs");
            var netObj = go.AddComponent<NetworkObject>();
            var health = go.AddComponent<HealthBehaviour>();
            var combat = go.AddComponent<CombatBehaviour>();
            health.InitForTest();
            combat.InitForTest();
            netObj.NotifyNetworkSpawn();

            // Only dirty combat
            combat.Ammo.Value = 10;

            var w = new NetWriter();
            netObj.SerializeDirty(w);
            var data = w.ToArray();

            Assert.AreEqual(1, data[0], "sectionCount should be 1 (only combat is dirty)");
            Assert.AreEqual(1, data[1], "componentIndex should be 1 (combat)");

            // Deserialize
            var go2 = new GameObject("TwoNBs2");
            var netObj2 = go2.AddComponent<NetworkObject>();
            var health2 = go2.AddComponent<HealthBehaviour>();
            var combat2 = go2.AddComponent<CombatBehaviour>();
            health2.InitForTest();
            combat2.InitForTest();
            netObj2.NotifyNetworkSpawn();

            netObj2.DeserializeDirty(new NetReader(data));
            Assert.AreEqual(100, health2.Health.Value);  // unchanged
            Assert.AreEqual(10, combat2.Ammo.Value);     // updated

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(go2);
        }

        [Test]
        public void Sectioned_TwoNBs_BothDirty()
        {
            var go = new GameObject("BothDirty");
            var netObj = go.AddComponent<NetworkObject>();
            var health = go.AddComponent<HealthBehaviour>();
            var combat = go.AddComponent<CombatBehaviour>();
            health.InitForTest();
            combat.InitForTest();
            netObj.NotifyNetworkSpawn();

            health.Speed.Value = 20f;
            combat.WeaponName.Value = "Rocket";

            var w = new NetWriter();
            netObj.SerializeDirty(w);
            var data = w.ToArray();

            Assert.AreEqual(2, data[0], "sectionCount should be 2 (both dirty)");

            // Deserialize
            var go2 = new GameObject("BothDirty2");
            var netObj2 = go2.AddComponent<NetworkObject>();
            var health2 = go2.AddComponent<HealthBehaviour>();
            var combat2 = go2.AddComponent<CombatBehaviour>();
            health2.InitForTest();
            combat2.InitForTest();
            netObj2.NotifyNetworkSpawn();

            netObj2.DeserializeDirty(new NetReader(data));
            Assert.AreEqual(20f, health2.Speed.Value, 0.001f);
            Assert.AreEqual("Rocket", combat2.WeaponName.Value);

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(go2);
        }

        [Test]
        public void Sectioned_EmptyNB_SkippedInSerialization()
        {
            var go = new GameObject("EmptyNBTest");
            var netObj = go.AddComponent<NetworkObject>();
            var empty = go.AddComponent<EmptyBehaviour>();
            var health = go.AddComponent<HealthBehaviour>();
            health.InitForTest();
            netObj.NotifyNetworkSpawn();

            health.Health.Value = 75;

            var w = new NetWriter();
            netObj.SerializeDirty(w);
            var data = w.ToArray();

            // EmptyBehaviour has 0 SyncVars, so it should be skipped even if present
            Assert.AreEqual(1, data[0], "sectionCount should be 1 (empty NB skipped)");

            Object.DestroyImmediate(go);
        }

        #endregion

        #region SerializeAll Sectioned

        [Test]
        public void SerializeAll_SelfPlusTwoNBs()
        {
            var go = new GameObject("AllSections");
            var netObj = go.AddComponent<NetworkObject>();
            var selfSv = netObj.Sync("GameState");
            var health = go.AddComponent<HealthBehaviour>();
            var combat = go.AddComponent<CombatBehaviour>();
            health.InitForTest();
            combat.InitForTest();
            netObj.NotifyNetworkSpawn();

            var w = new NetWriter();
            netObj.SerializeAll(w);

            var go2 = new GameObject("AllSections2");
            var netObj2 = go2.AddComponent<NetworkObject>();
            var selfSv2 = netObj2.Sync("");
            var health2 = go2.AddComponent<HealthBehaviour>();
            var combat2 = go2.AddComponent<CombatBehaviour>();
            health2.InitForTest();
            combat2.InitForTest();
            netObj2.NotifyNetworkSpawn();

            // Overwrite defaults
            health2.Health.SetInternal(0);
            health2.Speed.SetInternal(0f);
            combat2.WeaponName.SetInternal("");
            combat2.Ammo.SetInternal(0);
            combat2.IsReloading.SetInternal(true);

            netObj2.DeserializeAll(new NetReader(w.ToArray()));

            Assert.AreEqual("GameState", selfSv2.Value);
            Assert.AreEqual(100, health2.Health.Value);
            Assert.AreEqual(5.0f, health2.Speed.Value, 0.001f);
            Assert.AreEqual("Pistol", combat2.WeaponName.Value);
            Assert.AreEqual(30, combat2.Ammo.Value);
            Assert.AreEqual(false, combat2.IsReloading.Value);

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(go2);
        }

        [Test]
        public void SerializeAll_EmptyNBSkipped()
        {
            var go = new GameObject("AllEmpty");
            var netObj = go.AddComponent<NetworkObject>();
            var empty = go.AddComponent<EmptyBehaviour>();
            netObj.NotifyNetworkSpawn();

            var w = new NetWriter();
            netObj.SerializeAll(w);
            var data = w.ToArray();

            // No self SyncVars, no NB SyncVars (empty) → sectionCount = 0
            Assert.AreEqual(0, data[0], "sectionCount should be 0 with no SyncVars anywhere");

            Object.DestroyImmediate(go);
        }

        #endregion

        #region ClearDirty (full object)

        [Test]
        public void ClearDirty_ClearsAllSectionsAndNBs()
        {
            var go = new GameObject("ClearAll");
            var netObj = go.AddComponent<NetworkObject>();
            var selfSv = netObj.Sync(0);
            var health = go.AddComponent<HealthBehaviour>();
            health.InitForTest();
            netObj.NotifyNetworkSpawn();

            selfSv.Value = 42;
            health.Health.Value = 50;

            netObj.ClearDirty();

            Assert.IsFalse(selfSv.IsDirty);
            Assert.IsFalse(health.Health.IsDirty);
            Assert.IsFalse(health.IsDirty);

            // SerializeDirty should produce 0 sections
            var w = new NetWriter();
            netObj.SerializeDirty(w);
            var data = w.ToArray();
            Assert.AreEqual(0, data[0], "sectionCount should be 0 after ClearDirty");

            Object.DestroyImmediate(go);
        }

        #endregion

        #region Adaptive Mask Size

        [Test]
        public void NB_MaskSize_1Byte_For8OrFewerSyncVars()
        {
            var (_, nb) = CreateHealth(); // 2 SyncVars
            nb.Health.Value = 50;
            nb.Speed.Value = 10f;

            var w = new NetWriter();
            nb.SerializeDirtyNB(w);
            var data = w.ToArray();

            // mask is 1 byte (0x03 = both dirty), then 4 bytes (int) + 4 bytes (float)
            Assert.AreEqual(0x03, data[0], "mask should be 0x03 (both bits set)");

            Object.DestroyImmediate(nb.gameObject);
        }

        #endregion

        #region OnChanged Callbacks on NB SyncVars

        [Test]
        public void NB_OnChanged_Fires()
        {
            var (_, nb) = CreateHealth();
            int oldVal = -1, newVal = -1;
            nb.Health.OnChanged += (o, n) => { oldVal = o; newVal = n; };
            nb.Health.Value = 50;

            Assert.AreEqual(100, oldVal);
            Assert.AreEqual(50, newVal);

            Object.DestroyImmediate(nb.gameObject);
        }

        [Test]
        public void NB_OnChanged_OnDeserialize()
        {
            var (_, nb) = CreateHealth();
            nb.Health.Value = 42;

            var w = new NetWriter();
            nb.SerializeDirtyNB(w);

            var (_, nb2) = CreateHealth();
            int capturedNew = -1;
            nb2.Health.OnChanged += (_, n) => capturedNew = n;

            nb2.DeserializeDirtyNB(new NetReader(w.ToArray()));
            Assert.AreEqual(42, capturedNew);

            Object.DestroyImmediate(nb.gameObject);
            Object.DestroyImmediate(nb2.gameObject);
        }

        #endregion

        #region SELF_COMPONENT_INDEX Constant

        [Test]
        public void SELF_COMPONENT_INDEX_Is0xFF()
        {
            Assert.AreEqual(0xFF, NetworkObject.SELF_COMPONENT_INDEX);
        }

        #endregion
    }
}
