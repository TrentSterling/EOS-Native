using System.Collections.Generic;
using EOSNative.P2P;
using UnityEngine;

namespace EOSNative.Net
{
    /// <summary>
    /// Synchronizes Animator parameters across the network.
    /// Packs float/int/bool parameters into a single SyncVar for bandwidth efficiency.
    /// Trigger parameters are sent via RPC (event-based, not state).
    ///
    /// Usage: Add to any GameObject with an Animator and NetworkObject.
    /// The owner's animator changes auto-sync to all remote peers.
    /// Call <see cref="SetNetworkTrigger"/> for trigger parameters.
    /// </summary>
    public class NetworkAnimator : NetworkBehaviour
    {
        [SerializeField] private Animator _animator;

        [Header("Sync Settings")]
        [SerializeField, Tooltip("How often to check for parameter changes (seconds).")]
        private float _syncInterval = 0.1f;

        private SyncVar<byte[]> _paramState;

        // Discovered parameters (grouped by type for efficient packing)
        private int[] _floatHashes;
        private int[] _intHashes;
        private int[] _boolHashes;
        private string[] _triggerNames;
        private int[] _triggerHashes;

        // Change detection cache (owner only)
        private float[] _prevFloats;
        private int[] _prevInts;
        private bool[] _prevBools;
        private float _lastSyncTime;

        protected override void Awake()
        {
            base.Awake();
            if (_animator == null) _animator = GetComponent<Animator>();

            _paramState = Sync<byte[]>(null);
            _paramState.OnChanged += OnStateReceived;

            DiscoverParameters();
            RegisterTriggerHandlers();
        }

        private void DiscoverParameters()
        {
            if (_animator == null || _animator.runtimeAnimatorController == null) return;

            var parameters = _animator.parameters;
            var floats = new List<int>();
            var ints = new List<int>();
            var bools = new List<int>();
            var tNames = new List<string>();
            var tHashes = new List<int>();

            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                switch (p.type)
                {
                    case AnimatorControllerParameterType.Float: floats.Add(p.nameHash); break;
                    case AnimatorControllerParameterType.Int: ints.Add(p.nameHash); break;
                    case AnimatorControllerParameterType.Bool: bools.Add(p.nameHash); break;
                    case AnimatorControllerParameterType.Trigger:
                        tNames.Add(p.name);
                        tHashes.Add(p.nameHash);
                        break;
                }
            }

            _floatHashes = floats.ToArray();
            _intHashes = ints.ToArray();
            _boolHashes = bools.ToArray();
            _triggerNames = tNames.ToArray();
            _triggerHashes = tHashes.ToArray();

            _prevFloats = new float[_floatHashes.Length];
            _prevInts = new int[_intHashes.Length];
            _prevBools = new bool[_boolHashes.Length];

            CacheCurrentValues();
        }

        private void RegisterTriggerHandlers()
        {
            if (_triggerNames == null || NetworkManager.Instance == null) return;

            for (int i = 0; i < _triggerNames.Length; i++)
            {
                int hash = _triggerHashes[i];
                NetworkManager.Instance.RegisterRPC(Net, $"AnimTrig_{_triggerNames[i]}", _ =>
                {
                    if (_animator != null) _animator.SetTrigger(hash);
                });
            }
        }

        void Update()
        {
            if (!IsOwner || _animator == null || _floatHashes == null) return;
            if (Time.time - _lastSyncTime < _syncInterval) return;
            _lastSyncTime = Time.time;

            if (HasChanged())
            {
                _paramState.Value = PackState();
                CacheCurrentValues();
            }
        }

        #region Change Detection

        private bool HasChanged()
        {
            for (int i = 0; i < _floatHashes.Length; i++)
                if (Mathf.Abs(_animator.GetFloat(_floatHashes[i]) - _prevFloats[i]) > 0.001f) return true;
            for (int i = 0; i < _intHashes.Length; i++)
                if (_animator.GetInteger(_intHashes[i]) != _prevInts[i]) return true;
            for (int i = 0; i < _boolHashes.Length; i++)
                if (_animator.GetBool(_boolHashes[i]) != _prevBools[i]) return true;
            return false;
        }

        private void CacheCurrentValues()
        {
            for (int i = 0; i < _floatHashes.Length; i++)
                _prevFloats[i] = _animator.GetFloat(_floatHashes[i]);
            for (int i = 0; i < _intHashes.Length; i++)
                _prevInts[i] = _animator.GetInteger(_intHashes[i]);
            for (int i = 0; i < _boolHashes.Length; i++)
                _prevBools[i] = _animator.GetBool(_boolHashes[i]);
        }

        #endregion

        #region Serialization

        /// <summary>
        /// Pack all parameters into a byte array.
        /// Format: [floatCount:byte][floats...][intCount:byte][ints...][boolCount:byte][boolMask bytes]
        /// </summary>
        private byte[] PackState()
        {
            var w = NetWriterPool.Get();

            w.WriteByte((byte)_floatHashes.Length);
            for (int i = 0; i < _floatHashes.Length; i++)
                w.WriteFloat(_animator.GetFloat(_floatHashes[i]));

            w.WriteByte((byte)_intHashes.Length);
            for (int i = 0; i < _intHashes.Length; i++)
                w.WriteInt32(_animator.GetInteger(_intHashes[i]));

            w.WriteByte((byte)_boolHashes.Length);
            int boolBytes = (_boolHashes.Length + 7) / 8;
            for (int bi = 0; bi < boolBytes; bi++)
            {
                byte mask = 0;
                for (int bit = 0; bit < 8; bit++)
                {
                    int idx = bi * 8 + bit;
                    if (idx >= _boolHashes.Length) break;
                    if (_animator.GetBool(_boolHashes[idx]))
                        mask |= (byte)(1 << bit);
                }
                w.WriteByte(mask);
            }

            byte[] result = w.ToArray();
            NetWriterPool.Return(w);
            return result;
        }

        private void OnStateReceived(byte[] oldValue, byte[] newValue)
        {
            if (IsOwner || _animator == null || newValue == null || newValue.Length == 0) return;
            ApplyState(newValue);
        }

        private void ApplyState(byte[] data)
        {
            var r = new NetReader(data, 0, data.Length);

            int fc = r.ReadByte();
            for (int i = 0; i < fc && i < _floatHashes.Length; i++)
                _animator.SetFloat(_floatHashes[i], r.ReadFloat());

            int ic = r.ReadByte();
            for (int i = 0; i < ic && i < _intHashes.Length; i++)
                _animator.SetInteger(_intHashes[i], r.ReadInt32());

            int bc = r.ReadByte();
            int boolBytesCount = (bc + 7) / 8;
            for (int bi = 0; bi < boolBytesCount; bi++)
            {
                byte mask = r.ReadByte();
                for (int bit = 0; bit < 8; bit++)
                {
                    int idx = bi * 8 + bit;
                    if (idx >= bc || idx >= _boolHashes.Length) break;
                    _animator.SetBool(_boolHashes[idx], (mask & (1 << bit)) != 0);
                }
            }
        }

        #endregion

        /// <summary>
        /// Fire a trigger parameter on all remote peers. Only the owner should call this.
        /// The trigger fires locally immediately and is sent to others via RPC.
        /// </summary>
        /// <param name="triggerName">The Animator trigger parameter name.</param>
        public void SetNetworkTrigger(string triggerName)
        {
            if (!IsOwner || _animator == null) return;
            _animator.SetTrigger(Animator.StringToHash(triggerName));
            NetworkManager.Instance?.SendRPC(Net, $"AnimTrig_{triggerName}", RPCTarget.Others);
        }

        /// <summary>Number of synced float parameters.</summary>
        public int FloatCount => _floatHashes?.Length ?? 0;

        /// <summary>Number of synced int parameters.</summary>
        public int IntCount => _intHashes?.Length ?? 0;

        /// <summary>Number of synced bool parameters.</summary>
        public int BoolCount => _boolHashes?.Length ?? 0;

        /// <summary>Number of trigger parameters (sent via RPC).</summary>
        public int TriggerCount => _triggerNames?.Length ?? 0;
    }
}
