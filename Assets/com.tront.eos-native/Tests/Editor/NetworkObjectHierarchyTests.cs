using System.Reflection;
using NUnit.Framework;
using EOSNative.Net;
using UnityEngine;
using UnityEngine.TestTools;

namespace EOSNative.Tests
{
    /// <summary>
    /// Tests for nested NetworkObjects (v2.33.0) and runtime reparenting (v2.34.0).
    /// Tests focus on the NetworkObject property/state layer — no actual P2P networking.
    /// Spawning tests use the inactive GO + reflection pattern.
    /// </summary>
    public class NetworkObjectHierarchyTests
    {
        private NetworkManager _nm;

        private static readonly FieldInfo NmInstanceField =
            typeof(NetworkManager).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);

        [SetUp]
        public void SetUp()
        {
            var go = new GameObject("NetworkManager");
            go.SetActive(false);
            _nm = go.AddComponent<NetworkManager>();
            NmInstanceField.SetValue(null, _nm);

            typeof(NetworkManager).GetProperty("OfflineMode").SetValue(_nm, true);
            typeof(NetworkManager).GetProperty("IsHost").SetValue(_nm, true);
            typeof(NetworkManager)
                .GetField("_localIdPrefix", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(_nm, (ushort)0xFFFF);
            typeof(NetworkManager)
                .GetField("_localIdCounter", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(_nm, (ushort)1);
        }

        [TearDown]
        public void TearDown()
        {
            NmInstanceField.SetValue(null, null);
            if (_nm != null)
                Object.DestroyImmediate(_nm.gameObject);

            foreach (var name in new[] { "NetworkManager", "EOSP2PManager", "EOSLobbyManager", "NetworkSceneManager" })
            {
                var obj = GameObject.Find(name);
                if (obj != null) Object.DestroyImmediate(obj);
            }
        }

        private NetworkObject CreateNetObj(string name = "TestNetObj")
        {
            var go = new GameObject(name);
            return go.AddComponent<NetworkObject>();
        }

        private void Cleanup(params NetworkObject[] objs)
        {
            foreach (var obj in objs)
                if (obj != null && obj.gameObject != null)
                    Object.DestroyImmediate(obj.gameObject);
        }

        #region Identity Properties

        [Test]
        public void NewNetworkObject_IsRoot()
        {
            var obj = CreateNetObj();
            Assert.IsTrue(obj.IsRootNetworkObject);
            Assert.IsFalse(obj.IsChildNetworkObject);
            Assert.AreEqual(0u, obj.ParentNetworkId);
            Cleanup(obj);
        }

        [Test]
        public void ParentNetworkId_MakesChild()
        {
            var obj = CreateNetObj();
            obj.ParentNetworkId = 0x12340001;
            Assert.IsTrue(obj.IsChildNetworkObject);
            Assert.IsFalse(obj.IsRootNetworkObject);
            Cleanup(obj);
        }

        [Test]
        public void OriginalParentNetworkId_DefaultsToZero()
        {
            var obj = CreateNetObj();
            Assert.AreEqual(0u, obj.OriginalParentNetworkId);
            Cleanup(obj);
        }

        [Test]
        public void OriginalParentNetworkId_SetOnce()
        {
            var obj = CreateNetObj();
            obj.OriginalParentNetworkId = 0xAAAA0001;
            Assert.AreEqual(0xAAAA0001u, obj.OriginalParentNetworkId);
            // Can be changed (internal), but by convention set once at spawn
            obj.OriginalParentNetworkId = 0xBBBB0002;
            Assert.AreEqual(0xBBBB0002u, obj.OriginalParentNetworkId);
            Cleanup(obj);
        }

        #endregion

        #region Serialized Inspector Fields

        [Test]
        public void DestroyWithOwner_DefaultsFalse()
        {
            var obj = CreateNetObj();
            Assert.IsFalse(obj.DestroyWithOwner);
            Cleanup(obj);
        }

        [Test]
        public void DestroyWithOwner_Settable()
        {
            var obj = CreateNetObj();
            obj.DestroyWithOwner = true;
            Assert.IsTrue(obj.DestroyWithOwner);
            obj.DestroyWithOwner = false;
            Assert.IsFalse(obj.DestroyWithOwner);
            Cleanup(obj);
        }

        [Test]
        public void AlwaysVisible_DefaultsFalse()
        {
            var obj = CreateNetObj();
            Assert.IsFalse(obj.AlwaysVisible);
            Cleanup(obj);
        }

        [Test]
        public void AlwaysVisible_Settable()
        {
            var obj = CreateNetObj();
            obj.AlwaysVisible = true;
            Assert.IsTrue(obj.AlwaysVisible);
            Cleanup(obj);
        }

        #endregion

        #region Hierarchy Discovery

        [Test]
        public void GetComponentsInChildren_FindsNested()
        {
            var root = CreateNetObj("Root");
            var childGo = new GameObject("Child");
            childGo.transform.SetParent(root.transform);
            var child = childGo.AddComponent<NetworkObject>();

            var all = root.GetComponentsInChildren<NetworkObject>(true);
            Assert.AreEqual(2, all.Length);
            Assert.AreEqual(root, all[0]); // root at index 0
            Assert.AreEqual(child, all[1]);

            Cleanup(root); // destroys child too since it's a Transform child
        }

        [Test]
        public void GetComponentsInChildren_MultipleChildren()
        {
            var root = CreateNetObj("Root");

            var head = new GameObject("Head");
            head.transform.SetParent(root.transform);
            head.AddComponent<NetworkObject>();

            var leftHand = new GameObject("LeftHand");
            leftHand.transform.SetParent(root.transform);
            leftHand.AddComponent<NetworkObject>();

            var rightHand = new GameObject("RightHand");
            rightHand.transform.SetParent(root.transform);
            rightHand.AddComponent<NetworkObject>();

            var all = root.GetComponentsInChildren<NetworkObject>(true);
            Assert.AreEqual(4, all.Length);
            Assert.AreEqual(root, all[0]);

            Cleanup(root);
        }

        [Test]
        public void SingleNetworkObject_NoPrefabChildren()
        {
            var root = CreateNetObj("Standalone");
            var all = root.GetComponentsInChildren<NetworkObject>(true);
            Assert.AreEqual(1, all.Length);
            Assert.AreEqual(root, all[0]);
            Cleanup(root);
        }

        #endregion

        #region Reparenting State

        [Test]
        public void DetachFromParent_ClearsParentNetworkId()
        {
            var obj = CreateNetObj();
            obj.ParentNetworkId = 0x11110001;
            Assert.IsTrue(obj.IsChildNetworkObject);

            // Simulate detach (what ApplyReparent does)
            obj.ParentNetworkId = 0;
            Assert.IsTrue(obj.IsRootNetworkObject);
            Assert.IsFalse(obj.IsChildNetworkObject);
            Cleanup(obj);
        }

        [Test]
        public void AttachToParent_SetsParentNetworkId()
        {
            var obj = CreateNetObj();
            Assert.IsTrue(obj.IsRootNetworkObject);

            obj.ParentNetworkId = 0x22220001;
            Assert.IsTrue(obj.IsChildNetworkObject);
            Assert.AreEqual(0x22220001u, obj.ParentNetworkId);
            Cleanup(obj);
        }

        [Test]
        public void OriginalParentNetworkId_UnchangedByReparent()
        {
            var obj = CreateNetObj();
            obj.OriginalParentNetworkId = 0xAAAA0001;
            obj.ParentNetworkId = 0xAAAA0001; // initially child of root

            // Detach
            obj.ParentNetworkId = 0;
            Assert.AreEqual(0xAAAA0001u, obj.OriginalParentNetworkId,
                "OriginalParentNetworkId should not change on detach");

            // Re-attach to a different parent
            obj.ParentNetworkId = 0xBBBB0002;
            Assert.AreEqual(0xAAAA0001u, obj.OriginalParentNetworkId,
                "OriginalParentNetworkId should not change on re-attach");

            Cleanup(obj);
        }

        #endregion

        #region Transform Hierarchy

        [Test]
        public void SetParent_Null_DetachesTransform()
        {
            var root = CreateNetObj("Root");
            root.transform.position = new Vector3(10, 0, 0);

            var child = new GameObject("Child");
            child.transform.SetParent(root.transform);
            child.transform.localPosition = new Vector3(0, 5, 0);

            // World pos should be (10, 5, 0)
            Assert.AreEqual(new Vector3(10, 5, 0), child.transform.position);

            // Detach with worldPositionStays: true
            child.transform.SetParent(null, worldPositionStays: true);
            Assert.IsNull(child.transform.parent);
            // World position should be preserved
            Assert.AreEqual(10f, child.transform.position.x, 0.001f);
            Assert.AreEqual(5f, child.transform.position.y, 0.001f);
            Assert.AreEqual(0f, child.transform.position.z, 0.001f);

            Object.DestroyImmediate(root.gameObject);
            Object.DestroyImmediate(child);
        }

        [Test]
        public void SetParent_NewParent_PreservesWorldPosition()
        {
            var parentA = CreateNetObj("ParentA");
            parentA.transform.position = Vector3.zero;

            var parentB = CreateNetObj("ParentB");
            parentB.transform.position = new Vector3(100, 0, 0);

            var obj = CreateNetObj("Obj");
            obj.transform.SetParent(parentA.transform);
            obj.transform.localPosition = new Vector3(5, 5, 0);

            // Reparent to B, preserving world position
            obj.transform.SetParent(parentB.transform, worldPositionStays: true);
            Assert.AreEqual(parentB.transform, obj.transform.parent);
            // World pos should still be (5, 5, 0)
            Assert.AreEqual(5f, obj.transform.position.x, 0.001f);
            Assert.AreEqual(5f, obj.transform.position.y, 0.001f);

            Cleanup(parentA, parentB);
        }

        #endregion

        #region Events

        [Test]
        public void OnReparented_FiresOnInvoke()
        {
            var obj = CreateNetObj();
            var parentA = CreateNetObj("ParentA");
            var parentB = CreateNetObj("ParentB");

            NetworkObject receivedOldParent = null;
            NetworkObject receivedNewParent = null;
            int fireCount = 0;

            obj.OnReparented += (oldP, newP) =>
            {
                receivedOldParent = oldP;
                receivedNewParent = newP;
                fireCount++;
            };

            // Simulate reparent event
            obj.NotifyReparented(parentA, parentB);
            Assert.AreEqual(1, fireCount);
            Assert.AreEqual(parentA, receivedOldParent);
            Assert.AreEqual(parentB, receivedNewParent);

            Cleanup(obj, parentA, parentB);
        }

        [Test]
        public void OnReparented_DetachPassesNull()
        {
            var obj = CreateNetObj();
            var parent = CreateNetObj("Parent");

            NetworkObject receivedNewParent = parent; // set non-null to verify it changes
            obj.OnReparented += (oldP, newP) => receivedNewParent = newP;

            obj.NotifyReparented(parent, null); // detach
            Assert.IsNull(receivedNewParent);

            Cleanup(obj, parent);
        }

        [Test]
        public void OnOwnerChanged_Fires()
        {
            var obj = CreateNetObj();
            int fireCount = 0;
            obj.OnOwnerChanged += (oldO, newO) => fireCount++;

            obj.NotifyOwnerChanged(null, null);
            Assert.AreEqual(1, fireCount);

            Cleanup(obj);
        }

        #endregion

        #region SyncVars on Nested Objects

        [Test]
        public void ChildNetworkObject_CanHaveSyncVars()
        {
            var root = CreateNetObj("Root");
            root.Sync(0);

            var childGo = new GameObject("Child");
            childGo.transform.SetParent(root.transform);
            var child = childGo.AddComponent<NetworkObject>();
            var childSv = child.Sync(42);

            Assert.AreEqual(1, root.SyncVarCount);
            Assert.AreEqual(1, child.SyncVarCount);
            Assert.AreEqual(42, childSv.Value);

            Cleanup(root);
        }

        [Test]
        public void MultipleChildren_IndependentSyncVars()
        {
            var root = CreateNetObj("Root");
            root.Sync("root_data");

            var child1Go = new GameObject("Child1");
            child1Go.transform.SetParent(root.transform);
            var child1 = child1Go.AddComponent<NetworkObject>();
            var sv1 = child1.Sync(new Vector3(1, 2, 3));

            var child2Go = new GameObject("Child2");
            child2Go.transform.SetParent(root.transform);
            var child2 = child2Go.AddComponent<NetworkObject>();
            var sv2 = child2.Sync(99.9f);

            Assert.AreEqual(1, child1.SyncVarCount);
            Assert.AreEqual(1, child2.SyncVarCount);
            Assert.AreEqual(new Vector3(1, 2, 3), sv1.Value);
            Assert.AreEqual(99.9f, sv2.Value, 0.001f);

            Cleanup(root);
        }

        #endregion

        #region Spawning Nested Objects

        /// <summary>Create a prefab with root + N nested children.</summary>
        private GameObject CreateNestedPrefab(string name, int childCount)
        {
            var root = new GameObject(name);
            root.AddComponent<NetworkObject>();
            for (int i = 0; i < childCount; i++)
            {
                var child = new GameObject($"Child{i}");
                child.transform.SetParent(root.transform);
                child.AddComponent<NetworkObject>();
            }
            root.SetActive(false);
            return root;
        }

        [Test]
        public void Spawn_NestedPrefab_RootAndChildrenRegistered()
        {
            var prefab = CreateNestedPrefab("NestedPrefab", 2);
            _nm.RegisterPrefab(prefab, 0);
            var spawned = _nm.Spawn(0, Vector3.zero, Quaternion.identity);

            var allNetObjs = spawned.GetComponentsInChildren<NetworkObject>(true);
            Assert.AreEqual(3, allNetObjs.Length, "Should have root + 2 children");

            foreach (var netObj in allNetObjs)
            {
                Assert.IsTrue(netObj.IsRegistered, $"{netObj.name} should be registered");
                Assert.AreNotEqual(0u, netObj.NetworkId, $"{netObj.name} should have a NetworkId");
            }
        }

        [Test]
        public void Spawn_NestedPrefab_ChildrenHaveParentNetworkId()
        {
            var prefab = CreateNestedPrefab("NestedPrefab", 2);
            _nm.RegisterPrefab(prefab, 0);
            var spawned = _nm.Spawn(0, Vector3.zero, Quaternion.identity);

            var allNetObjs = spawned.GetComponentsInChildren<NetworkObject>(true);
            var root = allNetObjs[0];

            for (int i = 1; i < allNetObjs.Length; i++)
            {
                Assert.AreEqual(root.NetworkId, allNetObjs[i].ParentNetworkId,
                    $"Child {i} should have ParentNetworkId = root's NetworkId");
                Assert.IsTrue(allNetObjs[i].IsChildNetworkObject);
            }
        }

        [Test]
        public void Spawn_NestedPrefab_ChildrenHaveOriginalParentNetworkId()
        {
            var prefab = CreateNestedPrefab("NestedPrefab", 1);
            _nm.RegisterPrefab(prefab, 0);
            var spawned = _nm.Spawn(0, Vector3.zero, Quaternion.identity);

            var allNetObjs = spawned.GetComponentsInChildren<NetworkObject>(true);
            var root = allNetObjs[0];
            var child = allNetObjs[1];

            Assert.AreEqual(root.NetworkId, child.OriginalParentNetworkId,
                "OriginalParentNetworkId should be set to root's NetworkId at spawn");
        }

        [Test]
        public void Spawn_NestedPrefab_ChildrenInObjectsDict()
        {
            var prefab = CreateNestedPrefab("NestedPrefab", 2);
            _nm.RegisterPrefab(prefab, 0);
            var spawned = _nm.Spawn(0, Vector3.zero, Quaternion.identity);

            var allNetObjs = spawned.GetComponentsInChildren<NetworkObject>(true);
            for (int i = 0; i < allNetObjs.Length; i++)
            {
                Assert.IsTrue(_nm.Objects.ContainsKey(allNetObjs[i].NetworkId),
                    $"{allNetObjs[i].name} should be in Objects dictionary");
            }
        }

        [Test]
        public void Spawn_SinglePrefab_NoChildren()
        {
            var prefab = new GameObject("Simple");
            prefab.AddComponent<NetworkObject>();
            prefab.SetActive(false);
            _nm.RegisterPrefab(prefab, 0);

            var spawned = _nm.Spawn(0, Vector3.zero, Quaternion.identity);
            Assert.IsTrue(spawned.IsRootNetworkObject);
            Assert.AreEqual(0u, spawned.ParentNetworkId);
            Assert.AreEqual(0u, spawned.OriginalParentNetworkId);
        }

        [Test]
        public void Spawn_NestedPrefab_UniqueNetworkIds()
        {
            var prefab = CreateNestedPrefab("NestedPrefab", 3);
            _nm.RegisterPrefab(prefab, 0);
            var spawned = _nm.Spawn(0, Vector3.zero, Quaternion.identity);

            var allNetObjs = spawned.GetComponentsInChildren<NetworkObject>(true);
            var ids = new System.Collections.Generic.HashSet<uint>();

            foreach (var netObj in allNetObjs)
            {
                Assert.IsTrue(ids.Add(netObj.NetworkId),
                    $"Duplicate NetworkId {netObj.NetworkId} on {netObj.name}");
            }
        }

        #endregion

        #region Despawn Cascade

        [Test]
        public void Despawn_Root_RemovesAllChildren()
        {
            var prefab = CreateNestedPrefab("NestedPrefab", 2);
            _nm.RegisterPrefab(prefab, 0);
            var spawned = _nm.Spawn(0, Vector3.zero, Quaternion.identity);

            var allNetObjs = spawned.GetComponentsInChildren<NetworkObject>(true);
            var childIds = new uint[allNetObjs.Length - 1];
            for (int i = 1; i < allNetObjs.Length; i++)
                childIds[i - 1] = allNetObjs[i].NetworkId;

            _nm.Despawn(spawned);

            foreach (var childId in childIds)
            {
                Assert.IsFalse(_nm.Objects.ContainsKey(childId),
                    $"Child {childId} should be removed from Objects after root despawn");
            }
        }

        [Test]
        public void Despawn_Child_Directly_IsBlocked()
        {
            var prefab = CreateNestedPrefab("NestedPrefab", 1);
            _nm.RegisterPrefab(prefab, 0);
            var spawned = _nm.Spawn(0, Vector3.zero, Quaternion.identity);

            var allNetObjs = spawned.GetComponentsInChildren<NetworkObject>(true);
            var child = allNetObjs[1];

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Cannot despawn child"));
            _nm.Despawn(child);

            // Child should still be registered
            Assert.IsTrue(_nm.Objects.ContainsKey(child.NetworkId),
                "Child should still be in Objects after blocked direct despawn");
        }

        #endregion

        #region Reparenting via NetworkManager

        [Test]
        public void Reparent_UpdatesParentNetworkId()
        {
            // Spawn two separate root objects
            var prefabA = new GameObject("PrefabA");
            prefabA.AddComponent<NetworkObject>();
            prefabA.SetActive(false);
            _nm.RegisterPrefab(prefabA, 0);

            var prefabB = new GameObject("PrefabB");
            prefabB.AddComponent<NetworkObject>();
            prefabB.SetActive(false);
            _nm.RegisterPrefab(prefabB, 1);

            var objA = _nm.Spawn(0, Vector3.zero, Quaternion.identity);
            var objB = _nm.Spawn(1, new Vector3(10, 0, 0), Quaternion.identity);

            Assert.IsTrue(objA.IsRootNetworkObject);
            Assert.IsTrue(objB.IsRootNetworkObject);

            // Reparent A under B
            objA.SetNetworkParent(objB);

            Assert.AreEqual(objB.NetworkId, objA.ParentNetworkId,
                "ParentNetworkId should be B's NetworkId after reparent");
            Assert.IsTrue(objA.IsChildNetworkObject);
        }

        [Test]
        public void Detach_ClearsParentNetworkId()
        {
            var prefab = CreateNestedPrefab("NestedPrefab", 1);
            _nm.RegisterPrefab(prefab, 0);
            var spawned = _nm.Spawn(0, Vector3.zero, Quaternion.identity);

            var allNetObjs = spawned.GetComponentsInChildren<NetworkObject>(true);
            var child = allNetObjs[1];

            Assert.IsTrue(child.IsChildNetworkObject);

            child.DetachFromNetworkParent();

            Assert.AreEqual(0u, child.ParentNetworkId, "ParentNetworkId should be 0 after detach");
            Assert.IsTrue(child.IsRootNetworkObject);
        }

        [Test]
        public void Reparent_OriginalParentUnchanged()
        {
            var prefab = CreateNestedPrefab("NestedPrefab", 1);
            _nm.RegisterPrefab(prefab, 0);
            var spawned = _nm.Spawn(0, Vector3.zero, Quaternion.identity);

            var allNetObjs = spawned.GetComponentsInChildren<NetworkObject>(true);
            var root = allNetObjs[0];
            var child = allNetObjs[1];
            uint originalParent = child.OriginalParentNetworkId;

            Assert.AreEqual(root.NetworkId, originalParent);

            // Detach
            child.DetachFromNetworkParent();
            Assert.AreEqual(originalParent, child.OriginalParentNetworkId,
                "OriginalParentNetworkId should not change after detach");
        }

        [Test]
        public void Reparent_FiresOnReparentedEvent()
        {
            var prefabA = new GameObject("PrefabA");
            prefabA.AddComponent<NetworkObject>();
            prefabA.SetActive(false);
            _nm.RegisterPrefab(prefabA, 0);

            var prefabB = new GameObject("PrefabB");
            prefabB.AddComponent<NetworkObject>();
            prefabB.SetActive(false);
            _nm.RegisterPrefab(prefabB, 1);

            var objA = _nm.Spawn(0, Vector3.zero, Quaternion.identity);
            var objB = _nm.Spawn(1, Vector3.zero, Quaternion.identity);

            int eventCount = 0;
            NetworkObject receivedNewParent = null;
            objA.OnReparented += (oldP, newP) =>
            {
                eventCount++;
                receivedNewParent = newP;
            };

            objA.SetNetworkParent(objB);

            Assert.AreEqual(1, eventCount, "OnReparented should fire once");
            Assert.AreSame(objB, receivedNewParent, "New parent should be objB");
        }

        [Test]
        public void Reparent_CannotNestUnderChild()
        {
            var prefab = CreateNestedPrefab("NestedPrefab", 1);
            _nm.RegisterPrefab(prefab, 0);
            var spawned = _nm.Spawn(0, Vector3.zero, Quaternion.identity);

            var allNetObjs = spawned.GetComponentsInChildren<NetworkObject>(true);
            var child = allNetObjs[1];

            // Create another object and try to parent under child
            var prefab2 = new GameObject("Other");
            prefab2.AddComponent<NetworkObject>();
            prefab2.SetActive(false);
            _nm.RegisterPrefab(prefab2, 1);
            var other = _nm.Spawn(1, Vector3.zero, Quaternion.identity);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Cannot reparent"));
            other.SetNetworkParent(child);

            Assert.IsTrue(other.IsRootNetworkObject,
                "Object should remain root when trying to parent under a child");
        }

        [Test]
        public void Reparent_NoOp_WhenAlreadyParented()
        {
            var prefabA = new GameObject("PrefabA");
            prefabA.AddComponent<NetworkObject>();
            prefabA.SetActive(false);
            _nm.RegisterPrefab(prefabA, 0);

            var prefabB = new GameObject("PrefabB");
            prefabB.AddComponent<NetworkObject>();
            prefabB.SetActive(false);
            _nm.RegisterPrefab(prefabB, 1);

            var objA = _nm.Spawn(0, Vector3.zero, Quaternion.identity);
            var objB = _nm.Spawn(1, Vector3.zero, Quaternion.identity);

            objA.SetNetworkParent(objB);

            int eventCount = 0;
            objA.OnReparented += (oldP, newP) => eventCount++;

            // Parent again under same object — should be no-op
            objA.SetNetworkParent(objB);
            Assert.AreEqual(0, eventCount, "No event should fire for redundant reparent");
        }

        [Test]
        public void Detach_NoOp_WhenAlreadyRoot()
        {
            var prefab = new GameObject("Simple");
            prefab.AddComponent<NetworkObject>();
            prefab.SetActive(false);
            _nm.RegisterPrefab(prefab, 0);
            var obj = _nm.Spawn(0, Vector3.zero, Quaternion.identity);

            int eventCount = 0;
            obj.OnReparented += (oldP, newP) => eventCount++;

            obj.DetachFromNetworkParent();
            Assert.AreEqual(0, eventCount, "No event should fire for detaching a root object");
        }

        #endregion
    }
}
