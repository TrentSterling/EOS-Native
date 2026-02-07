using System;
using System.Collections.Generic;
using System.Reflection;
using EOSNative.P2P;
using UnityEngine;

namespace EOSNative.Net
{
    /// <summary>
    /// Sync any public field or property on sibling components without writing code.
    /// Select which properties to sync in the Inspector — only supported types are shown.
    /// Owner writes; remote peers receive and apply automatically.
    ///
    /// Inspired by Normcore's EasySync. Supports all primitives registered in <see cref="NetSerializers"/>.
    /// Per-property write access (Owner/Host/All) is planned for v2.
    /// </summary>
    public class EasySync : NetworkBehaviour
    {
        /// <summary>Who can write this property. Currently only Owner is enforced at runtime.</summary>
        public enum WriteAccess { Owner = 0, Host = 1, All = 2 }

        [Serializable]
        public class SyncBinding
        {
            public string ComponentType;
            public string MemberName;
            public bool IsProperty;
            public WriteAccess Access;
        }

        [HideInInspector] public List<SyncBinding> _bindings = new();

        [SerializeField, Tooltip("How often to check for property changes (seconds).")]
        private float _syncInterval = 0.1f;

        private SyncVar<byte[]> _state;

        // Resolved bindings (cached at Awake)
        private struct ResolvedBinding
        {
            public Component Component;
            public FieldInfo Field;
            public PropertyInfo Property;
            public Type ValueType;
            public byte TypeId;
            public object PrevValue;
        }

        private ResolvedBinding[] _resolved;
        private float _lastSyncTime;

        protected override void Awake()
        {
            base.Awake();
            _state = Sync<byte[]>(null);
            _state.OnChanged += OnStateReceived;
            ResolveBindings();
        }

        private void ResolveBindings()
        {
            if (_bindings == null || _bindings.Count == 0)
            {
                _resolved = Array.Empty<ResolvedBinding>();
                return;
            }

            var resolved = new List<ResolvedBinding>();
            var components = GetComponents<Component>();

            foreach (var binding in _bindings)
            {
                // Find the component by type name
                Component target = null;
                foreach (var comp in components)
                {
                    if (comp != null && comp.GetType().FullName == binding.ComponentType)
                    {
                        target = comp;
                        break;
                    }
                }
                if (target == null)
                {
                    Debug.LogWarning($"[EasySync] Component '{binding.ComponentType}' not found on '{name}'");
                    continue;
                }

                var type = target.GetType();
                var rb = new ResolvedBinding { Component = target };

                if (binding.IsProperty)
                {
                    rb.Property = type.GetProperty(binding.MemberName, BindingFlags.Public | BindingFlags.Instance);
                    if (rb.Property == null || !rb.Property.CanRead || !rb.Property.CanWrite)
                    {
                        Debug.LogWarning($"[EasySync] Property '{binding.MemberName}' not found or not read/write on '{type.Name}'");
                        continue;
                    }
                    rb.ValueType = rb.Property.PropertyType;
                }
                else
                {
                    rb.Field = type.GetField(binding.MemberName, BindingFlags.Public | BindingFlags.Instance);
                    if (rb.Field == null)
                    {
                        Debug.LogWarning($"[EasySync] Field '{binding.MemberName}' not found on '{type.Name}'");
                        continue;
                    }
                    rb.ValueType = rb.Field.FieldType;
                }

                if (!NetSerializers.TryGetTypeId(rb.ValueType, out byte typeId))
                {
                    Debug.LogWarning($"[EasySync] Type '{rb.ValueType.Name}' is not a supported serialization type");
                    continue;
                }

                rb.TypeId = typeId;
                rb.PrevValue = GetValue(ref rb);
                resolved.Add(rb);
            }

            _resolved = resolved.ToArray();
        }

        void Update()
        {
            if (_resolved == null || _resolved.Length == 0) return;
            if (!IsOwner) return;
            if (Time.time - _lastSyncTime < _syncInterval) return;
            _lastSyncTime = Time.time;

            if (HasChanged())
            {
                _state.Value = PackState();
                CacheCurrentValues();
            }
        }

        #region Change Detection

        private bool HasChanged()
        {
            for (int i = 0; i < _resolved.Length; i++)
            {
                object current = GetValue(ref _resolved[i]);
                if (!Equals(current, _resolved[i].PrevValue)) return true;
            }
            return false;
        }

        private void CacheCurrentValues()
        {
            for (int i = 0; i < _resolved.Length; i++)
                _resolved[i].PrevValue = GetValue(ref _resolved[i]);
        }

        #endregion

        #region Reflection Helpers

        private static object GetValue(ref ResolvedBinding b)
        {
            return b.Property != null
                ? b.Property.GetValue(b.Component)
                : b.Field.GetValue(b.Component);
        }

        private static void SetValue(ref ResolvedBinding b, object value)
        {
            if (b.Property != null)
                b.Property.SetValue(b.Component, value);
            else
                b.Field.SetValue(b.Component, value);
        }

        #endregion

        #region Serialization

        private byte[] PackState()
        {
            var w = NetWriterPool.Get();
            for (int i = 0; i < _resolved.Length; i++)
            {
                object val = GetValue(ref _resolved[i]);
                NetSerializers.WriteBoxed(w, _resolved[i].ValueType, val);
            }
            byte[] result = w.ToArray();
            NetWriterPool.Return(w);
            return result;
        }

        private void OnStateReceived(byte[] oldValue, byte[] newValue)
        {
            if (IsOwner || newValue == null || newValue.Length == 0 || _resolved == null) return;
            ApplyState(newValue);
        }

        private void ApplyState(byte[] data)
        {
            try
            {
                var r = new NetReader(data, 0, data.Length);
                for (int i = 0; i < _resolved.Length; i++)
                {
                    object val = NetSerializers.ReadBoxed(r, _resolved[i].TypeId);
                    SetValue(ref _resolved[i], val);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EasySync] Failed to apply state: {e.Message}");
            }
        }

        #endregion

        /// <summary>Number of resolved bindings (available after Awake).</summary>
        public int BindingCount => _resolved?.Length ?? 0;

        /// <summary>Supported primitive types for EasySync.</summary>
        public static readonly Type[] SupportedTypes =
        {
            typeof(bool), typeof(byte), typeof(short), typeof(ushort),
            typeof(int), typeof(uint), typeof(long), typeof(ulong),
            typeof(float), typeof(double), typeof(string),
            typeof(Vector2), typeof(Vector3), typeof(Quaternion),
            typeof(Color), typeof(Color32)
        };
    }
}
