using NUnit.Framework;
using EOSNative.Net;
using UnityEngine;

namespace EOSNative.Tests
{
    /// <summary>
    /// Tests for NetworkPrefabTable ScriptableObject (v2.36.0).
    /// Verifies prefab registration, retrieval, null handling, and runtime API.
    /// </summary>
    public class NetworkPrefabTableTests
    {
        private NetworkPrefabTable _table;

        [SetUp]
        public void SetUp()
        {
            _table = ScriptableObject.CreateInstance<NetworkPrefabTable>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_table);
        }

        private GameObject CreatePrefabWithNetObj(string name = "TestPrefab")
        {
            var go = new GameObject(name);
            go.AddComponent<NetworkObject>();
            return go;
        }

        #region Count & GetPrefab

        [Test]
        public void NewTable_CountIsZero()
        {
            Assert.AreEqual(0, _table.Count);
        }

        [Test]
        public void GetPrefab_NegativeIndex_ReturnsNull()
        {
            Assert.IsNull(_table.GetPrefab(-1));
        }

        [Test]
        public void GetPrefab_OutOfRange_ReturnsNull()
        {
            Assert.IsNull(_table.GetPrefab(0));
            Assert.IsNull(_table.GetPrefab(100));
        }

        #endregion

        #region AddPrefab

        [Test]
        public void AddPrefab_IncreasesCount()
        {
            var go = CreatePrefabWithNetObj();
            _table.AddPrefab(go);
            Assert.AreEqual(1, _table.Count);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void AddPrefab_RetrievableByIndex()
        {
            var go = CreatePrefabWithNetObj("PrefabA");
            _table.AddPrefab(go);
            Assert.AreEqual(go, _table.GetPrefab(0));
            Object.DestroyImmediate(go);
        }

        [Test]
        public void AddPrefab_Null_Ignored()
        {
            _table.AddPrefab(null);
            Assert.AreEqual(0, _table.Count);
        }

        [Test]
        public void AddPrefab_Duplicate_Ignored()
        {
            var go = CreatePrefabWithNetObj();
            _table.AddPrefab(go);
            _table.AddPrefab(go);
            Assert.AreEqual(1, _table.Count);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void AddPrefab_MultiplePrefabs_CorrectOrder()
        {
            var a = CreatePrefabWithNetObj("A");
            var b = CreatePrefabWithNetObj("B");
            var c = CreatePrefabWithNetObj("C");

            _table.AddPrefab(a);
            _table.AddPrefab(b);
            _table.AddPrefab(c);

            Assert.AreEqual(3, _table.Count);
            Assert.AreEqual(a, _table.GetPrefab(0));
            Assert.AreEqual(b, _table.GetPrefab(1));
            Assert.AreEqual(c, _table.GetPrefab(2));

            Object.DestroyImmediate(a);
            Object.DestroyImmediate(b);
            Object.DestroyImmediate(c);
        }

        #endregion

        #region RemovePrefabAt

        [Test]
        public void RemovePrefabAt_DecreasesCount()
        {
            var go = CreatePrefabWithNetObj();
            _table.AddPrefab(go);
            Assert.AreEqual(1, _table.Count);

            _table.RemovePrefabAt(0);
            Assert.AreEqual(0, _table.Count);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void RemovePrefabAt_ShiftsSubsequent()
        {
            var a = CreatePrefabWithNetObj("A");
            var b = CreatePrefabWithNetObj("B");
            var c = CreatePrefabWithNetObj("C");

            _table.AddPrefab(a);
            _table.AddPrefab(b);
            _table.AddPrefab(c);

            _table.RemovePrefabAt(0); // remove A, B→0, C→1

            Assert.AreEqual(2, _table.Count);
            Assert.AreEqual(b, _table.GetPrefab(0));
            Assert.AreEqual(c, _table.GetPrefab(1));

            Object.DestroyImmediate(a);
            Object.DestroyImmediate(b);
            Object.DestroyImmediate(c);
        }

        [Test]
        public void RemovePrefabAt_NegativeIndex_NoOp()
        {
            var go = CreatePrefabWithNetObj();
            _table.AddPrefab(go);
            _table.RemovePrefabAt(-1);
            Assert.AreEqual(1, _table.Count);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void RemovePrefabAt_OutOfRange_NoOp()
        {
            var go = CreatePrefabWithNetObj();
            _table.AddPrefab(go);
            _table.RemovePrefabAt(5);
            Assert.AreEqual(1, _table.Count);
            Object.DestroyImmediate(go);
        }

        #endregion
    }
}
