using NUnit.Framework;
using EOSNative.Net;
using UnityEngine;

namespace EOSNative.Tests
{
    /// <summary>
    /// Tests for nested NetworkObjects (v2.33.0) and runtime reparenting (v2.34.0).
    /// Tests focus on the NetworkObject property/state layer — no actual P2P networking.
    /// </summary>
    public class NetworkObjectHierarchyTests
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
    }
}
