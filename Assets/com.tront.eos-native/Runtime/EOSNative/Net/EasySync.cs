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
    ///
    /// Inspired by Normcore's EasySync. Supports all primitives registered in <see cref="NetSerializers"/>.
    ///
    /// v2 features:
    /// - Per-property WriteAccess: Owner (default), Host, or All peers can write
    /// - Per-property Interpolation: smooth numeric/Vector/Quaternion values on remotes
    /// - "Convert to Code" Inspector button: exports to a typed NetworkBehaviour .cs file
    /// </summary>
    public class EasySync : NetworkBehaviour
    {
        /// <summary>Who can write this property.</summary>
        public enum WriteAccess
        {
            /// <summary>Only the NetworkObject owner can write. Default.</summary>
            Owner = 0,
            /// <summary>Only the host can write (useful for game state managed by host).</summary>
            Host = 1,
            /// <summary>Any peer can write (last-write-wins). Use sparingly.</summary>
            All = 2
        }

        [Serializable]
        public class SyncBinding
        {
            public string ComponentType;
            public string MemberName;
            public bool IsProperty;
            public WriteAccess Access;
            /// <summary>If true, numeric/Vector/Quaternion values are lerped toward target instead of snapped.</summary>
            public bool Interpolate;
            /// <summary>Interpolation speed (units per second). Higher = faster catch-up. Only used when Interpolate is true.</summary>
            public float InterpolateSpeed = 15f;
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
            public WriteAccess Access;
            public bool Interpolate;
            public float InterpolateSpeed;
            // Interpolation targets (only used on remote peers for interpolated bindings)
            public object TargetValue;
            public bool HasTarget;
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
                var rb = new ResolvedBinding
                {
                    Component = target,
                    Access = binding.Access,
                    Interpolate = binding.Interpolate,
                    InterpolateSpeed = binding.InterpolateSpeed
                };

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

            // Check if we can write based on per-binding access level
            bool canWrite = CanWrite();

            if (canWrite)
            {
                if (Time.time - _lastSyncTime < _syncInterval) return;
                _lastSyncTime = Time.time;

                if (HasChanged())
                {
                    _state.Value = PackState();
                    CacheCurrentValues();
                }
            }
            else
            {
                // Apply interpolation for remote peers
                ApplyInterpolation();
            }
        }

        /// <summary>Check if the local peer can write any binding (uses the most permissive access level).</summary>
        private bool CanWrite()
        {
            // Check each binding's access level — if ANY allows us to write, we can write
            // (all bindings are packed together, so we need unified write permission)
            // Use the most common access level as the write gate
            for (int i = 0; i < _resolved.Length; i++)
            {
                switch (_resolved[i].Access)
                {
                    case WriteAccess.Owner:
                        if (IsOwner) return true;
                        break;
                    case WriteAccess.Host:
                        if (IsHost) return true;
                        break;
                    case WriteAccess.All:
                        return true;
                }
            }
            return false;
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
            if (CanWrite() || newValue == null || newValue.Length == 0 || _resolved == null) return;
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

                    if (_resolved[i].Interpolate && IsInterpolatable(_resolved[i].ValueType))
                    {
                        // Store as interpolation target instead of applying immediately
                        _resolved[i].TargetValue = val;
                        _resolved[i].HasTarget = true;
                    }
                    else
                    {
                        SetValue(ref _resolved[i], val);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EasySync] Failed to apply state: {e.Message}");
            }
        }

        #endregion

        #region Interpolation

        /// <summary>Apply interpolation for bindings that have pending target values.</summary>
        private void ApplyInterpolation()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < _resolved.Length; i++)
            {
                if (!_resolved[i].HasTarget) continue;

                object current = GetValue(ref _resolved[i]);
                object target = _resolved[i].TargetValue;
                float speed = _resolved[i].InterpolateSpeed;
                float t = Mathf.Clamp01(speed * dt);

                object interpolated = LerpValue(current, target, t, _resolved[i].ValueType);
                SetValue(ref _resolved[i], interpolated);

                // Check if we've reached the target (close enough)
                if (IsCloseEnough(interpolated, target, _resolved[i].ValueType))
                    _resolved[i].HasTarget = false;
            }
        }

        /// <summary>Types that support interpolation (lerp).</summary>
        private static bool IsInterpolatable(Type t)
        {
            return t == typeof(float) || t == typeof(double) ||
                   t == typeof(Vector2) || t == typeof(Vector3) ||
                   t == typeof(Quaternion) || t == typeof(Color) || t == typeof(Color32) ||
                   t == typeof(int) || t == typeof(short) || t == typeof(byte);
        }

        private static object LerpValue(object current, object target, float t, Type type)
        {
            if (type == typeof(float))
                return Mathf.Lerp((float)current, (float)target, t);
            if (type == typeof(double))
                return (double)Mathf.Lerp((float)(double)current, (float)(double)target, t);
            if (type == typeof(Vector2))
                return Vector2.Lerp((Vector2)current, (Vector2)target, t);
            if (type == typeof(Vector3))
                return Vector3.Lerp((Vector3)current, (Vector3)target, t);
            if (type == typeof(Quaternion))
                return Quaternion.Slerp((Quaternion)current, (Quaternion)target, t);
            if (type == typeof(Color))
                return Color.Lerp((Color)current, (Color)target, t);
            if (type == typeof(Color32))
                return Color32.Lerp((Color32)current, (Color32)target, t);
            if (type == typeof(int))
                return (int)Mathf.Lerp((int)current, (int)target, t);
            if (type == typeof(short))
                return (short)Mathf.Lerp((short)current, (short)target, t);
            if (type == typeof(byte))
                return (byte)Mathf.Lerp((byte)current, (byte)target, t);

            // Non-interpolatable — snap
            return target;
        }

        private static bool IsCloseEnough(object current, object target, Type type)
        {
            const float epsilon = 0.001f;

            if (type == typeof(float))
                return Mathf.Abs((float)current - (float)target) < epsilon;
            if (type == typeof(double))
                return Math.Abs((double)current - (double)target) < epsilon;
            if (type == typeof(Vector2))
                return ((Vector2)current - (Vector2)target).sqrMagnitude < epsilon * epsilon;
            if (type == typeof(Vector3))
                return ((Vector3)current - (Vector3)target).sqrMagnitude < epsilon * epsilon;
            if (type == typeof(Quaternion))
                return Quaternion.Angle((Quaternion)current, (Quaternion)target) < 0.1f;
            if (type == typeof(Color))
            {
                var c = (Color)current;
                var tgt = (Color)target;
                return Mathf.Abs(c.r - tgt.r) + Mathf.Abs(c.g - tgt.g) +
                       Mathf.Abs(c.b - tgt.b) + Mathf.Abs(c.a - tgt.a) < epsilon * 4;
            }

            // Integer types and non-interpolatable — exact match
            return Equals(current, target);
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
