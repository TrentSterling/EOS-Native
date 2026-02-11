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

        /// <summary>
        /// Scans the entire project for prefabs with a NetworkObject component
        /// and adds any that are missing from the table. Existing entries are preserved
        /// to maintain stable PrefabIds. Optionally filters by a folder path.
        /// </summary>
        /// <param name="folderFilter">Optional folder to limit scanning (e.g. "Assets/Prefabs"). Null = scan all.</param>
        /// <returns>Number of newly added prefabs.</returns>
        public int CollectAll(string folderFilter = null)
        {
            var guids = folderFilter != null
                ? UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { folderFilter })
                : UnityEditor.AssetDatabase.FindAssets("t:Prefab");

            int added = 0;
            foreach (var guid in guids)
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;
                if (prefab.GetComponent<NetworkObject>() == null) continue;
                if (_prefabs.Contains(prefab)) continue;

                _prefabs.Add(prefab);
                added++;
            }

            if (added > 0)
            {
                UnityEditor.EditorUtility.SetDirty(this);
                Debug.Log($"[NetworkPrefabTable] Auto-collected {added} prefab(s). Total: {_prefabs.Count}.", this);
            }

            return added;
        }

        /// <summary>Remove all null/missing entries from the table. Compacts PrefabIds.</summary>
        public void RemoveNulls()
        {
            int removed = _prefabs.RemoveAll(p => p == null);
            if (removed > 0)
            {
                UnityEditor.EditorUtility.SetDirty(this);
                Debug.Log($"[NetworkPrefabTable] Removed {removed} null entries. Remaining: {_prefabs.Count}.", this);
            }
        }
#endif
    }
}
