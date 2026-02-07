using System.Collections.Generic;
using NUnit.Framework;
using EOSNative.Net;
using EOSNative.P2P;
using UnityEngine;

namespace EOSNative.Tests
{
    public class SyncListTests
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

        private (NetworkObject netObj, SyncList<T> list) CreateSyncList<T>(List<T> initial = null)
        {
            var go = new GameObject("TestSyncList");
            var netObj = go.AddComponent<NetworkObject>();
            var list = netObj.SyncList(initial);
            return (netObj, list);
        }

        private void Cleanup(NetworkObject netObj)
        {
            Object.DestroyImmediate(netObj.gameObject);
        }

        #region Basic Operations

        [Test]
        public void Add_IncreasesCount()
        {
            var (netObj, list) = CreateSyncList<string>();
            list.Add("a");
            list.Add("b");
            Assert.AreEqual(2, list.Count);
            Cleanup(netObj);
        }

        [Test]
        public void Indexer_Get()
        {
            var (netObj, list) = CreateSyncList<int>();
            list.Add(10);
            list.Add(20);
            Assert.AreEqual(10, list[0]);
            Assert.AreEqual(20, list[1]);
            Cleanup(netObj);
        }

        [Test]
        public void Indexer_Set()
        {
            var (netObj, list) = CreateSyncList<int>();
            list.Add(10);
            list[0] = 99;
            Assert.AreEqual(99, list[0]);
            Cleanup(netObj);
        }

        [Test]
        public void RemoveAt_DecreasesCount()
        {
            var (netObj, list) = CreateSyncList<string>();
            list.Add("a");
            list.Add("b");
            list.Add("c");
            list.RemoveAt(1);
            Assert.AreEqual(2, list.Count);
            Assert.AreEqual("a", list[0]);
            Assert.AreEqual("c", list[1]);
            Cleanup(netObj);
        }

        [Test]
        public void Insert_AtIndex()
        {
            var (netObj, list) = CreateSyncList<int>();
            list.Add(1);
            list.Add(3);
            list.Insert(1, 2);
            Assert.AreEqual(3, list.Count);
            Assert.AreEqual(1, list[0]);
            Assert.AreEqual(2, list[1]);
            Assert.AreEqual(3, list[2]);
            Cleanup(netObj);
        }

        [Test]
        public void Clear_EmptiesList()
        {
            var (netObj, list) = CreateSyncList<int>();
            list.Add(1);
            list.Add(2);
            list.Clear();
            Assert.AreEqual(0, list.Count);
            Cleanup(netObj);
        }

        [Test]
        public void Contains_Works()
        {
            var (netObj, list) = CreateSyncList<string>();
            list.Add("hello");
            Assert.IsTrue(list.Contains("hello"));
            Assert.IsFalse(list.Contains("world"));
            Cleanup(netObj);
        }

        [Test]
        public void IndexOf_Works()
        {
            var (netObj, list) = CreateSyncList<int>();
            list.Add(10);
            list.Add(20);
            list.Add(30);
            Assert.AreEqual(1, list.IndexOf(20));
            Assert.AreEqual(-1, list.IndexOf(99));
            Cleanup(netObj);
        }

        [Test]
        public void Remove_ByValue()
        {
            var (netObj, list) = CreateSyncList<string>();
            list.Add("a");
            list.Add("b");
            list.Add("c");
            Assert.IsTrue(list.Remove("b"));
            Assert.AreEqual(2, list.Count);
            Assert.IsFalse(list.Remove("z"));
            Cleanup(netObj);
        }

        #endregion

        #region Dirty Tracking

        [Test]
        public void Add_MarksDirty()
        {
            var (netObj, list) = CreateSyncList<int>();
            Assert.IsFalse(list.IsDirty);
            list.Add(1);
            Assert.IsTrue(list.IsDirty);
            Cleanup(netObj);
        }

        [Test]
        public void ClearDirty_Resets()
        {
            var (netObj, list) = CreateSyncList<int>();
            list.Add(1);
            list.ClearDirty();
            Assert.IsFalse(list.IsDirty);
            Cleanup(netObj);
        }

        #endregion

        #region OnChanged

        [Test]
        public void OnChanged_Add()
        {
            var (netObj, list) = CreateSyncList<string>();
            SyncListOp lastOp = (SyncListOp)255;
            string lastNewItem = null;
            list.OnChanged += (op, idx, old, newItem) =>
            {
                lastOp = op;
                lastNewItem = newItem;
            };
            list.Add("hello");
            Assert.AreEqual(SyncListOp.Add, lastOp);
            Assert.AreEqual("hello", lastNewItem);
            Cleanup(netObj);
        }

        [Test]
        public void OnChanged_Set()
        {
            var (netObj, list) = CreateSyncList<int>();
            list.Add(10);
            list.ClearDirty();

            int oldVal = -1, newVal = -1;
            list.OnChanged += (op, idx, old, n) => { oldVal = old; newVal = n; };
            list[0] = 99;
            Assert.AreEqual(10, oldVal);
            Assert.AreEqual(99, newVal);
            Cleanup(netObj);
        }

        [Test]
        public void OnChanged_RemoveAt()
        {
            var (netObj, list) = CreateSyncList<string>();
            list.Add("a");
            list.Add("b");
            list.ClearDirty();

            string removed = null;
            list.OnChanged += (op, idx, old, _) => { if (op == SyncListOp.RemoveAt) removed = old; };
            list.RemoveAt(0);
            Assert.AreEqual("a", removed);
            Cleanup(netObj);
        }

        #endregion

        #region Delta Serialization

        [Test]
        public void Delta_Add_RoundTrip()
        {
            var (netObj1, list1) = CreateSyncList<int>();
            list1.Add(10);
            list1.Add(20);

            var w = new NetWriter();
            list1.WriteTo(w);

            var (netObj2, list2) = CreateSyncList<int>();
            var r = new NetReader(w.ToArray());
            list2.ReadFrom(r);

            Assert.AreEqual(2, list2.Count);
            Assert.AreEqual(10, list2[0]);
            Assert.AreEqual(20, list2[1]);

            Cleanup(netObj1);
            Cleanup(netObj2);
        }

        [Test]
        public void Delta_Mixed_RoundTrip()
        {
            var (netObj1, list1) = CreateSyncList(new List<string> { "a", "b", "c" });
            list1.ClearDirty();

            list1[1] = "B";
            list1.Add("d");

            var w = new NetWriter();
            list1.WriteTo(w);

            var (netObj2, list2) = CreateSyncList(new List<string> { "a", "b", "c" });
            var r = new NetReader(w.ToArray());
            list2.ReadFrom(r);

            Assert.AreEqual(4, list2.Count);
            Assert.AreEqual("a", list2[0]);
            Assert.AreEqual("B", list2[1]);
            Assert.AreEqual("c", list2[2]);
            Assert.AreEqual("d", list2[3]);

            Cleanup(netObj1);
            Cleanup(netObj2);
        }

        #endregion

        #region Full State

        [Test]
        public void FullState_RoundTrip()
        {
            var (netObj1, list1) = CreateSyncList<int>();
            list1.Add(1);
            list1.Add(2);
            list1.Add(3);

            var w = new NetWriter();
            list1.WriteFullState(w);

            var (netObj2, list2) = CreateSyncList<int>();
            var r = new NetReader(w.ToArray());
            list2.ReadFullState(r);

            Assert.AreEqual(3, list2.Count);
            Assert.AreEqual(1, list2[0]);
            Assert.AreEqual(2, list2[1]);
            Assert.AreEqual(3, list2[2]);

            Cleanup(netObj1);
            Cleanup(netObj2);
        }

        [Test]
        public void FullState_ReplacesExisting()
        {
            var (netObj1, list1) = CreateSyncList<string>();
            list1.Add("x");

            var w = new NetWriter();
            list1.WriteFullState(w);

            var (netObj2, list2) = CreateSyncList<string>();
            list2.Add("old1");
            list2.Add("old2");

            var r = new NetReader(w.ToArray());
            list2.ReadFullState(r);

            Assert.AreEqual(1, list2.Count);
            Assert.AreEqual("x", list2[0]);

            Cleanup(netObj1);
            Cleanup(netObj2);
        }

        #endregion

        #region Enumeration

        [Test]
        public void Enumerate_Works()
        {
            var (netObj, list) = CreateSyncList<int>();
            list.Add(1);
            list.Add(2);
            list.Add(3);

            var result = new List<int>();
            foreach (var item in list)
                result.Add(item);

            Assert.AreEqual(3, result.Count);
            Assert.AreEqual(1, result[0]);
            Assert.AreEqual(2, result[1]);
            Assert.AreEqual(3, result[2]);

            Cleanup(netObj);
        }

        #endregion
    }
}
