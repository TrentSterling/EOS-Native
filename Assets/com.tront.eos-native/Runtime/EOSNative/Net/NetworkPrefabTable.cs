using System.Collections.Generic;
using UnityEngine;

namespace EOSNative.Net
{
    /// <summary>
    /// Drag-and-drop prefab registry for NetworkManager.
    /// Index in the list = PrefabId used by Spawn().
    /// Create via right-click → Create → EOS Native → Network Prefab Table.
    /// </summary>
    [CreateAssetMenu(fileName = "NetworkPrefabTable", menuName = "EOS Native/Network Prefab Table")]
    public class NetworkPrefabTable : ScriptableObject
    {
        [SerializeField] private List<GameObject> _prefabs = new();

        /// <summary>Number of prefab slots (including nulls).</summary>
        public int Count => _prefabs.Count;

        /// <summary>Get a prefab by index (PrefabId). Returns null if out of range or empty slot.</summary>
        public GameObject GetPrefab(int index)
        {
            if (index >= 0 && index < _prefabs.Count) return _prefabs[index];
            return null;
        }

        /// <summary>Append a prefab at runtime. Skips null and duplicates.</summary>
        public void AddPrefab(GameObject prefab)
        {
            if (prefab == null || _prefabs.Contains(prefab)) return;
            _prefabs.Add(prefab);
        }

        /// <summary>Remove a prefab slot by index. Shifts subsequent IDs down by one.</summary>
        public void RemovePrefabAt(int index)
        {
            if (index >= 0 && index < _prefabs.Count)
                _prefabs.RemoveAt(index);
        }

        /// <summary>Look up the index (PrefabId) of a prefab. Returns -1 if not found.</summary>
        public int IndexOf(GameObject prefab)
        {
            return _prefabs.IndexOf(prefab);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            for (int i = 0; i < _prefabs.Count; i++)
            {
                if (_prefabs[i] != null && _prefabs[i].GetComponent<NetworkObject>() == null)
                    Debug.LogWarning($"[NetworkPrefabTable] Prefab at index {i} ({_prefabs[i].name}) is missing a NetworkObject component.", this);
            }
        }
#endif
    }
}
