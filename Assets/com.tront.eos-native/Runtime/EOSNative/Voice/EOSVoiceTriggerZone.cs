using EOSNative.Net;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace EOSNative.Voice
{
    /// <summary>
    /// A trigger-based voice zone. Players in the same zone can hear each other.
    /// Attach to a GameObject with a Collider set to isTrigger=true.
    ///
    /// Usage:
    /// 1. Set EOSVoiceZoneManager to Custom mode
    /// 2. Add a Collider (Box, Sphere, etc.) and set Is Trigger = true
    /// 3. Set the zone name — players in the same zone hear each other
    /// 4. Players in different zones are muted
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class EOSVoiceTriggerZone : MonoBehaviour
    {
        [Header("Zone Settings")]
        [Tooltip("Unique name for this zone")]
        [SerializeField] private string _zoneName = "Zone1";

        [Tooltip("Color for gizmo visualization")]
        [SerializeField] private Color _gizmoColor = new Color(0f, 1f, 0.5f, 0.3f);

        [Header("Options")]
        [Tooltip("Set this as the default zone for players not in any trigger")]
        [SerializeField] private bool _isDefaultZone = false;

        [Tooltip("Tag to filter which objects are tracked as players")]
        [SerializeField] private string _playerTag = "Player";

        /// <summary>Zone name for this trigger.</summary>
        public string ZoneName => _zoneName;

        /// <summary>Whether this is the default zone.</summary>
        public bool IsDefaultZone => _isDefaultZone;

        private void Awake()
        {
            var col = GetComponent<Collider>();
            if (col != null && !col.isTrigger)
            {
                col.isTrigger = true;
                Debug.LogWarning($"[EOSVoiceTriggerZone] Collider on '{gameObject.name}' set to trigger mode.");
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsValidPlayer(other)) return;

            var zoneManager = EOSVoiceZoneManager.Instance;
            if (zoneManager == null) return;

            if (IsLocalPlayer(other))
            {
                zoneManager.SetLocalZone(_zoneName);
                Debug.Log($"[EOSVoiceTriggerZone] Local player entered zone: {_zoneName}");
            }
            else
            {
                string puid = GetPlayerPuid(other);
                if (!string.IsNullOrEmpty(puid))
                {
                    zoneManager.SetPlayerZone(puid, _zoneName);
                    Debug.Log($"[EOSVoiceTriggerZone] Player {puid.Substring(0, Mathf.Min(8, puid.Length))}... entered zone: {_zoneName}");
                }
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (!IsValidPlayer(other)) return;

            var zoneManager = EOSVoiceZoneManager.Instance;
            if (zoneManager == null) return;

            string defaultZone = GetDefaultZoneName();

            if (IsLocalPlayer(other))
            {
                zoneManager.SetLocalZone(defaultZone);
                Debug.Log($"[EOSVoiceTriggerZone] Local player exited to zone: {defaultZone}");
            }
            else
            {
                string puid = GetPlayerPuid(other);
                if (!string.IsNullOrEmpty(puid))
                {
                    zoneManager.SetPlayerZone(puid, defaultZone);
                    Debug.Log($"[EOSVoiceTriggerZone] Player {puid.Substring(0, Mathf.Min(8, puid.Length))}... exited to zone: {defaultZone}");
                }
            }
        }

        private bool IsValidPlayer(Collider other)
        {
            if (string.IsNullOrEmpty(_playerTag)) return true;
            return other.CompareTag(_playerTag);
        }

        private bool IsLocalPlayer(Collider other)
        {
            var netObj = other.GetComponentInParent<NetworkObject>();
            if (netObj != null)
            {
                return netObj.IsOwner;
            }
            return false;
        }

        private string GetPlayerPuid(Collider other)
        {
            var netObj = other.GetComponentInParent<NetworkObject>();
            if (netObj == null || netObj.OwnerId == null) return null;
            return netObj.OwnerId.ToString();
        }

        private string GetDefaultZoneName()
        {
#if UNITY_2023_1_OR_NEWER
            var zones = FindObjectsByType<EOSVoiceTriggerZone>(FindObjectsSortMode.None);
#else
            var zones = FindObjectsOfType<EOSVoiceTriggerZone>();
#endif
            foreach (var zone in zones)
            {
                if (zone._isDefaultZone)
                    return zone._zoneName;
            }
            return "default";
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            var col = GetComponent<Collider>();
            if (col == null) return;

            Gizmos.color = _gizmoColor;

            if (col is BoxCollider box)
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawCube(box.center, box.size);
                Gizmos.DrawWireCube(box.center, box.size);
            }
            else if (col is SphereCollider sphere)
            {
                Gizmos.DrawSphere(transform.position + sphere.center, sphere.radius);
                Gizmos.DrawWireSphere(transform.position + sphere.center, sphere.radius);
            }
            else if (col is CapsuleCollider capsule)
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawWireSphere(capsule.center + Vector3.up * (capsule.height / 2 - capsule.radius), capsule.radius);
                Gizmos.DrawWireSphere(capsule.center - Vector3.up * (capsule.height / 2 - capsule.radius), capsule.radius);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Handles.Label(transform.position + Vector3.up * 2f, $"Voice Zone: {_zoneName}");
        }
#endif
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(EOSVoiceTriggerZone))]
    public class EOSVoiceTriggerZoneEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var zone = (EOSVoiceTriggerZone)target;

            EditorGUILayout.Space(10);

            var col = zone.GetComponent<Collider>();
            if (col == null)
            {
                EditorGUILayout.HelpBox("Add a Collider component (Box, Sphere, etc.) to define the zone area.", MessageType.Warning);
            }
            else if (!col.isTrigger)
            {
                EditorGUILayout.HelpBox("Collider should be set to 'Is Trigger'. It will be set automatically at runtime.", MessageType.Info);
                if (GUILayout.Button("Set as Trigger"))
                {
                    col.isTrigger = true;
                    EditorUtility.SetDirty(col);
                }
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.HelpBox(
                "Voice Zones work with Custom mode.\n" +
                "1. Set EOSVoiceZoneManager to Custom mode\n" +
                "2. Players in the same zone can hear each other\n" +
                "3. Players in different zones are muted",
                MessageType.Info);
        }
    }
#endif
}
