using System.Collections.Generic;
using NUnit.Framework;
using EOSNative.Net;
using EOSNative.P2P;
using UnityEngine;

namespace EOSNative.Tests
{
    public class SyncDictionaryTests
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

        private (NetworkObject netObj, SyncDictionary<TKey, TValue> dict) CreateSyncDict<TKey, TValue>(
            Dictionary<TKey, TValue> initial = null)
        {
            var go = new GameObject("TestSyncDict");
            var netObj = go.AddComponent<NetworkObject>();
            var dict = netObj.SyncDictionary(initial);
            return (netObj, dict);
        }

        private void Cleanup(NetworkObject netObj)
        {
            Object.DestroyImmediate(netObj.gameObject);
        }

        #region Basic Operations

        [Test]
        public void Set_AddsEntry()
        {
            var (netObj, dict) = CreateSyncDict<string, int>();
            dict["score"] = 100;
            Assert.AreEqual(1, dict.Count);
            Assert.AreEqual(100, dict["score"]);
            Cleanup(netObj);
        }

        [Test]
        public void Set_UpdatesEntry()
        {
            var (netObj, dict) = CreateSyncDict<string, int>();
            dict["score"] = 100;
            dict["score"] = 200;
            Assert.AreEqual(1, dict.Count);
            Assert.AreEqual(200, dict["score"]);
            Cleanup(netObj);
        }

        [Test]
        public void Remove_RemovesEntry()
        {
            var (netObj, dict) = CreateSyncDict<string, int>();
            dict["a"] = 1;
            dict["b"] = 2;
            Assert.IsTrue(dict.Remove("a"));
            Assert.AreEqual(1, dict.Count);
            Assert.IsFalse(dict.ContainsKey("a"));
            Cleanup(netObj);
        }

        [Test]
        public void Remove_NonExistent_ReturnsFalse()
        {
            var (netObj, dict) = CreateSyncDict<string, int>();
            Assert.IsFalse(dict.Remove("nope"));
            Cleanup(netObj);
        }

        [Test]
        public void Clear_EmptiesDict()
        {
            var (netObj, dict) = CreateSyncDict<string, int>();
            dict["a"] = 1;
            dict["b"] = 2;
            dict.Clear();
            Assert.AreEqual(0, dict.Count);
            Cleanup(netObj);
        }

        [Test]
        public void ContainsKey_Works()
        {
            var (netObj, dict) = CreateSyncDict<string, int>();
            dict["key"] = 42;
            Assert.IsTrue(dict.ContainsKey("key"));
            Assert.IsFalse(dict.ContainsKey("other"));
            Cleanup(netObj);
        }

        [Test]
        public void TryGetValue_Works()
        {
            var (netObj, dict) = CreateSyncDict<string, int>();
            dict["key"] = 42;
            Assert.IsTrue(dict.TryGetValue("key", out int val));
            Assert.AreEqual(42, val);
            Assert.IsFalse(dict.TryGetValue("other", out _));
            Cleanup(netObj);
        }

        [Test]
        public void Keys_Contains()
        {
            var (netObj, dict) = CreateSyncDict<string, int>();
            dict["a"] = 1;
            dict["b"] = 2;
            var keys = new List<string>(dict.Keys);
            Assert.AreEqual(2, keys.Count);
            Assert.Contains("a", keys);
            Assert.Contains("b", keys);
            Cleanup(netObj);
        }

        [Test]
        public void Values_Contains()
        {
            var (netObj, dict) = CreateSyncDict<string, int>();
            dict["a"] = 1;
            dict["b"] = 2;
            var values = new List<int>(dict.Values);
            Assert.AreEqual(2, values.Count);
            Assert.Contains(1, values);
            Assert.Contains(2, values);
            Cleanup(netObj);
        }

        #endregion

        #region Dirty Tracking

        [Test]
        public void Set_MarksDirty()
        {
            var (netObj, dict) = CreateSyncDict<string, int>();
            Assert.IsFalse(dict.IsDirty);
            dict["x"] = 1;
            Assert.IsTrue(dict.IsDirty);
            Cleanup(netObj);
        }

        [Test]
        public void ClearDirty_Resets()
        {
            var (netObj, dict) = CreateSyncDict<string, int>();
            dict["x"] = 1;
            dict.ClearDirty();
            Assert.IsFalse(dict.IsDirty);
            Cleanup(netObj);
        }

        #endregion

        #region OnChanged

        [Test]
        public void OnChanged_Set()
        {
            var (netObj, dict) = CreateSyncDict<string, int>();
            SyncDictOp lastOp = (SyncDictOp)255;
            string lastKey = null;
            dict.OnChanged += (op, key, old, newVal) =>
            {
                lastOp = op;
                lastKey = key;
            };
            dict["hello"] = 42;
            Assert.AreEqual(SyncDictOp.Set, lastOp);
            Assert.AreEqual("hello", lastKey);
            Cleanup(netObj);
        }

        [Test]
        public void OnChanged_Remove()
        {
            var (netObj, dict) = CreateSyncDict<string, int>();
            dict["x"] = 1;
            dict.ClearDirty();

            SyncDictOp lastOp = (SyncDictOp)255;
            dict.OnChanged += (op, key, old, newVal) => lastOp = op;
            dict.Remove("x");
            Assert.AreEqual(SyncDictOp.Remove, lastOp);
            Cleanup(netObj);
        }

        #endregion

        #region Delta Serialization

        [Test]
        public void Delta_Set_RoundTrip()
        {
            var (netObj1, dict1) = CreateSyncDict<string, int>();
            dict1["a"] = 10;
            dict1["b"] = 20;

            var w = new NetWriter();
            dict1.WriteTo(w);

            var (netObj2, dict2) = CreateSyncDict<string, int>();
            var r = new NetReader(w.ToArray());
            dict2.ReadFrom(r);

            Assert.AreEqual(2, dict2.Count);
            Assert.AreEqual(10, dict2["a"]);
            Assert.AreEqual(20, dict2["b"]);

            Cleanup(netObj1);
            Cleanup(netObj2);
        }

        [Test]
        public void Delta_Remove_RoundTrip()
        {
            var (netObj1, dict1) = CreateSyncDict(new Dictionary<string, int> { { "a", 1 }, { "b", 2 } });
            dict1.ClearDirty();
            dict1.Remove("a");

            var w = new NetWriter();
            dict1.WriteTo(w);

            var (netObj2, dict2) = CreateSyncDict(new Dictionary<string, int> { { "a", 1 }, { "b", 2 } });
            var r = new NetReader(w.ToArray());
            dict2.ReadFrom(r);

            Assert.AreEqual(1, dict2.Count);
            Assert.IsFalse(dict2.ContainsKey("a"));
            Assert.AreEqual(2, dict2["b"]);

            Cleanup(netObj1);
            Cleanup(netObj2);
        }

        #endregion

        #region Full State

        [Test]
        public void FullState_RoundTrip()
        {
            var (netObj1, dict1) = CreateSyncDict<string, int>();
            dict1["x"] = 10;
            dict1["y"] = 20;
            dict1["z"] = 30;

            var w = new NetWriter();
            dict1.WriteFullState(w);

            var (netObj2, dict2) = CreateSyncDict<string, int>();
            var r = new NetReader(w.ToArray());
            dict2.ReadFullState(r);

            Assert.AreEqual(3, dict2.Count);
            Assert.AreEqual(10, dict2["x"]);
            Assert.AreEqual(20, dict2["y"]);
            Assert.AreEqual(30, dict2["z"]);

            Cleanup(netObj1);
            Cleanup(netObj2);
        }

        [Test]
        public void FullState_ReplacesExisting()
        {
            var (netObj1, dict1) = CreateSyncDict<string, int>();
            dict1["new"] = 999;

            var w = new NetWriter();
            dict1.WriteFullState(w);

            var (netObj2, dict2) = CreateSyncDict<string, int>();
            dict2["old1"] = 1;
            dict2["old2"] = 2;

            var r = new NetReader(w.ToArray());
            dict2.ReadFullState(r);

            Assert.AreEqual(1, dict2.Count);
            Assert.AreEqual(999, dict2["new"]);
            Assert.IsFalse(dict2.ContainsKey("old1"));

            Cleanup(netObj1);
            Cleanup(netObj2);
        }

        #endregion

        #region Enumeration

        [Test]
        public void Enumerate_Works()
        {
            var (netObj, dict) = CreateSyncDict<string, int>();
            dict["a"] = 1;
            dict["b"] = 2;

            var result = new Dictionary<string, int>();
            foreach (var kvp in dict)
                result[kvp.Key] = kvp.Value;

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(1, result["a"]);
            Assert.AreEqual(2, result["b"]);

            Cleanup(netObj);
        }

        #endregion
    }
}
