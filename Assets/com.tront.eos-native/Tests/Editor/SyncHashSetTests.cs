using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using EOSNative.Net;
using EOSNative.P2P;

namespace EOSNative.Tests
{
    [TestFixture]
    public class SyncHashSetTests
    {
        private readonly List<GameObject> _cleanup = new();

        private (NetworkObject netObj, SyncHashSet<T> set) CreateSyncHashSet<T>(HashSet<T> initial = null)
        {
            var go = new GameObject("TestObj");
            _cleanup.Add(go);
            var netObj = go.AddComponent<NetworkObject>();
            var set = netObj.SyncHashSet(initial);
            return (netObj, set);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _cleanup)
                if (go != null) Object.DestroyImmediate(go);
            _cleanup.Clear();
        }

        #region Basic Operations

        [Test]
        public void Add_IncreasesCount()
        {
            var (_, set) = CreateSyncHashSet<string>();
            Assert.AreEqual(0, set.Count);

            set.Add("Poison");
            Assert.AreEqual(1, set.Count);

            set.Add("Fire");
            Assert.AreEqual(2, set.Count);
        }

        [Test]
        public void Add_DuplicateReturnsFalse()
        {
            var (_, set) = CreateSyncHashSet<string>();
            Assert.IsTrue(set.Add("Poison"));
            Assert.IsFalse(set.Add("Poison"));
            Assert.AreEqual(1, set.Count);
        }

        [Test]
        public void Remove_DecreasesCount()
        {
            var (_, set) = CreateSyncHashSet<string>();
            set.Add("Poison");
            set.Add("Fire");

            Assert.IsTrue(set.Remove("Poison"));
            Assert.AreEqual(1, set.Count);
            Assert.IsFalse(set.Contains("Poison"));
        }

        [Test]
        public void Remove_NonExistentReturnsFalse()
        {
            var (_, set) = CreateSyncHashSet<string>();
            Assert.IsFalse(set.Remove("Ghost"));
        }

        [Test]
        public void Clear_EmptiesSet()
        {
            var (_, set) = CreateSyncHashSet<string>();
            set.Add("A");
            set.Add("B");
            set.Add("C");

            set.Clear();
            Assert.AreEqual(0, set.Count);
        }

        [Test]
        public void Clear_EmptySetIsNoop()
        {
            var (_, set) = CreateSyncHashSet<string>();
            set.Clear(); // Should not throw or mark dirty
            Assert.IsFalse(set.IsDirty);
        }

        [Test]
        public void Contains_FindsItem()
        {
            var (_, set) = CreateSyncHashSet<int>();
            set.Add(42);
            Assert.IsTrue(set.Contains(42));
            Assert.IsFalse(set.Contains(99));
        }

        [Test]
        public void InitialItems_ArePreserved()
        {
            var initial = new HashSet<string> { "A", "B", "C" };
            var (_, set) = CreateSyncHashSet(initial);
            Assert.AreEqual(3, set.Count);
            Assert.IsTrue(set.Contains("A"));
            Assert.IsTrue(set.Contains("B"));
            Assert.IsTrue(set.Contains("C"));
        }

        [Test]
        public void Enumeration_ReturnsAllItems()
        {
            var (_, set) = CreateSyncHashSet<int>();
            set.Add(1);
            set.Add(2);
            set.Add(3);

            var found = new HashSet<int>();
            foreach (var item in set)
                found.Add(item);

            Assert.AreEqual(3, found.Count);
            Assert.IsTrue(found.Contains(1));
            Assert.IsTrue(found.Contains(2));
            Assert.IsTrue(found.Contains(3));
        }

        #endregion

        #region Dirty Tracking

        [Test]
        public void Add_MarksDirty()
        {
            var (_, set) = CreateSyncHashSet<string>();
            Assert.IsFalse(set.IsDirty);
            set.Add("test");
            Assert.IsTrue(set.IsDirty);
        }

        [Test]
        public void Remove_MarksDirty()
        {
            var (_, set) = CreateSyncHashSet<string>();
            set.Add("test");
            set.ClearDirty();

            set.Remove("test");
            Assert.IsTrue(set.IsDirty);
        }

        [Test]
        public void Clear_MarksDirty()
        {
            var (_, set) = CreateSyncHashSet<string>();
            set.Add("test");
            set.ClearDirty();

            set.Clear();
            Assert.IsTrue(set.IsDirty);
        }

        [Test]
        public void ClearDirty_ResetsDirtyFlag()
        {
            var (_, set) = CreateSyncHashSet<string>();
            set.Add("test");
            Assert.IsTrue(set.IsDirty);

            set.ClearDirty();
            Assert.IsFalse(set.IsDirty);
        }

        #endregion

        #region OnChanged Callbacks

        [Test]
        public void OnChanged_FiresOnAdd()
        {
            var (_, set) = CreateSyncHashSet<string>();
            SyncHashSetOp? receivedOp = null;
            string receivedItem = null;
            set.OnChanged += (op, item) => { receivedOp = op; receivedItem = item; };

            set.Add("Poison");
            Assert.AreEqual(SyncHashSetOp.Add, receivedOp);
            Assert.AreEqual("Poison", receivedItem);
        }

        [Test]
        public void OnChanged_FiresOnRemove()
        {
            var (_, set) = CreateSyncHashSet<string>();
            set.Add("Poison");

            SyncHashSetOp? receivedOp = null;
            string receivedItem = null;
            set.OnChanged += (op, item) => { receivedOp = op; receivedItem = item; };

            set.Remove("Poison");
            Assert.AreEqual(SyncHashSetOp.Remove, receivedOp);
            Assert.AreEqual("Poison", receivedItem);
        }

        [Test]
        public void OnChanged_FiresOnClear()
        {
            var (_, set) = CreateSyncHashSet<string>();
            set.Add("A");

            SyncHashSetOp? receivedOp = null;
            set.OnChanged += (op, item) => { receivedOp = op; };

            set.Clear();
            Assert.AreEqual(SyncHashSetOp.Clear, receivedOp);
        }

        [Test]
        public void OnChanged_DoesNotFireOnDuplicateAdd()
        {
            var (_, set) = CreateSyncHashSet<string>();
            set.Add("Poison");

            int fireCount = 0;
            set.OnChanged += (op, item) => fireCount++;

            set.Add("Poison"); // duplicate
            Assert.AreEqual(0, fireCount);
        }

        #endregion

        #region Delta Serialization

        [Test]
        public void Delta_Add_RoundTrips()
        {
            var (_, sender) = CreateSyncHashSet<string>();
            var (_, receiver) = CreateSyncHashSet<string>();

            sender.Add("Poison");
            sender.Add("Fire");

            var writer = new NetWriter();
            sender.WriteTo(writer);

            var reader = new NetReader(writer.ToArray());
            receiver.ReadFrom(reader);

            Assert.AreEqual(2, receiver.Count);
            Assert.IsTrue(receiver.Contains("Poison"));
            Assert.IsTrue(receiver.Contains("Fire"));
        }

        [Test]
        public void Delta_Remove_RoundTrips()
        {
            var initial = new HashSet<string> { "Poison", "Fire", "Ice" };
            var (_, sender) = CreateSyncHashSet(new HashSet<string>(initial));
            var (_, receiver) = CreateSyncHashSet(new HashSet<string>(initial));

            sender.Remove("Fire");

            var writer = new NetWriter();
            sender.WriteTo(writer);

            var reader = new NetReader(writer.ToArray());
            receiver.ReadFrom(reader);

            Assert.AreEqual(2, receiver.Count);
            Assert.IsTrue(receiver.Contains("Poison"));
            Assert.IsFalse(receiver.Contains("Fire"));
            Assert.IsTrue(receiver.Contains("Ice"));
        }

        [Test]
        public void Delta_Clear_RoundTrips()
        {
            var initial = new HashSet<string> { "A", "B", "C" };
            var (_, sender) = CreateSyncHashSet(new HashSet<string>(initial));
            var (_, receiver) = CreateSyncHashSet(new HashSet<string>(initial));

            sender.Clear();

            var writer = new NetWriter();
            sender.WriteTo(writer);

            var reader = new NetReader(writer.ToArray());
            receiver.ReadFrom(reader);

            Assert.AreEqual(0, receiver.Count);
        }

        [Test]
        public void Delta_MixedOps_RoundTrips()
        {
            var (_, sender) = CreateSyncHashSet<int>();
            var (_, receiver) = CreateSyncHashSet<int>();

            sender.Add(1);
            sender.Add(2);
            sender.Add(3);
            sender.Remove(2);

            var writer = new NetWriter();
            sender.WriteTo(writer);

            var reader = new NetReader(writer.ToArray());
            receiver.ReadFrom(reader);

            Assert.AreEqual(2, receiver.Count);
            Assert.IsTrue(receiver.Contains(1));
            Assert.IsFalse(receiver.Contains(2));
            Assert.IsTrue(receiver.Contains(3));
        }

        [Test]
        public void Delta_OnChanged_FiresOnReceiver()
        {
            var (_, sender) = CreateSyncHashSet<string>();
            var (_, receiver) = CreateSyncHashSet<string>();

            sender.Add("test");

            var writer = new NetWriter();
            sender.WriteTo(writer);

            var ops = new List<SyncHashSetOp>();
            receiver.OnChanged += (op, item) => ops.Add(op);

            var reader = new NetReader(writer.ToArray());
            receiver.ReadFrom(reader);

            Assert.AreEqual(1, ops.Count);
            Assert.AreEqual(SyncHashSetOp.Add, ops[0]);
        }

        #endregion

        #region Full State (spawn/snapshot)

        [Test]
        public void FullState_RoundTrips()
        {
            var (_, sender) = CreateSyncHashSet<string>();
            sender.Add("A");
            sender.Add("B");
            sender.Add("C");

            var writer = new NetWriter();
            sender.WriteFullState(writer);

            var (_, receiver) = CreateSyncHashSet<string>();
            var reader = new NetReader(writer.ToArray());
            receiver.ReadFullState(reader);

            Assert.AreEqual(3, receiver.Count);
            Assert.IsTrue(receiver.Contains("A"));
            Assert.IsTrue(receiver.Contains("B"));
            Assert.IsTrue(receiver.Contains("C"));
        }

        [Test]
        public void FullState_ReplacesExistingContents()
        {
            var (_, sender) = CreateSyncHashSet<int>();
            sender.Add(10);
            sender.Add(20);

            var (_, receiver) = CreateSyncHashSet<int>();
            receiver.Add(99); // Should be replaced

            var writer = new NetWriter();
            sender.WriteFullState(writer);

            var reader = new NetReader(writer.ToArray());
            receiver.ReadFullState(reader);

            Assert.AreEqual(2, receiver.Count);
            Assert.IsTrue(receiver.Contains(10));
            Assert.IsTrue(receiver.Contains(20));
            Assert.IsFalse(receiver.Contains(99));
        }

        [Test]
        public void FullState_EmptySet()
        {
            var (_, sender) = CreateSyncHashSet<string>();

            var writer = new NetWriter();
            sender.WriteFullState(writer);

            var (_, receiver) = CreateSyncHashSet<string>();
            receiver.Add("stale");
            var reader = new NetReader(writer.ToArray());
            receiver.ReadFullState(reader);

            Assert.AreEqual(0, receiver.Count);
        }

        #endregion

        #region Integer Types

        [Test]
        public void IntSet_FullRoundTrip()
        {
            var (_, sender) = CreateSyncHashSet<int>();
            for (int i = 0; i < 100; i++)
                sender.Add(i);

            var writer = new NetWriter();
            sender.WriteFullState(writer);

            var (_, receiver) = CreateSyncHashSet<int>();
            var reader = new NetReader(writer.ToArray());
            receiver.ReadFullState(reader);

            Assert.AreEqual(100, receiver.Count);
            for (int i = 0; i < 100; i++)
                Assert.IsTrue(receiver.Contains(i));
        }

        #endregion
    }
}
