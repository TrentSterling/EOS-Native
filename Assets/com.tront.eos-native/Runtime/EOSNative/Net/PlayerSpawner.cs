using System;
using Epic.OnlineServices;
using EOSNative.Lobbies;
using EOSNative.P2P;
using UnityEngine;

namespace EOSNative.Net
{
    /// <summary>
    /// Auto-spawns a player prefab when P2P networking becomes active.
    /// Assign a player prefab and optional spawn points in the Inspector.
    /// When a peer connects (making the session online), the local player is
    /// automatically spawned. Despawns the player when the lobby is left or
    /// all peers disconnect.
    /// </summary>
    public class PlayerSpawner : MonoBehaviour
    {
        [Header("Player Prefab")]
        [Tooltip("The prefab to spawn for the local player. Must have a NetworkObject component.")]
        [SerializeField] private GameObject _playerPrefab;

        [Tooltip("Prefab registry index. Auto-registers the prefab at this index on startup. " +
                 "If you already added the prefab to NetworkManager's Prefabs list, match that index (default 0).")]
        [SerializeField] private ushort _prefabId = 0;

        [Header("Spawn Settings")]
        [Tooltip("Optional spawn points. If empty, spawns at this transform's position. " +
                 "If multiple, cycles through them round-robin for each spawn.")]
        [SerializeField] private Transform[] _spawnPoints;

        [Tooltip("If true (default), the player object is destroyed when the owner disconnects " +
                 "instead of being transferred to the new host.")]
        [SerializeField] private bool _destroyWithOwner = true;

        /// <summary>The local player's spawned NetworkObject, or null if not yet spawned.</summary>
        public NetworkObject LocalPlayer { get; private set; }

        /// <summary>True if the local player has been spawned.</summary>
        public bool HasSpawned => _hasSpawned;

        /// <summary>Fired when the local player is spawned. Arg: the spawned NetworkObject.</summary>
        public event Action<NetworkObject> OnLocalPlayerSpawned;

        /// <summary>Fired when the local player is despawned.</summary>
        public event Action OnLocalPlayerDespawned;

        private bool _hasSpawned;
        private static int _spawnIndex;

        private void Awake()
        {
            if (_playerPrefab != null)
                NetworkManager.Instance.RegisterPrefab(_playerPrefab, _prefabId);
        }

        private void OnEnable()
        {
            var p2p = EOSP2PManager.Instance;
            if (p2p != null)
            {
                p2p.OnPeerConnected -= OnPeerConnected;
                p2p.OnPeerConnected += OnPeerConnected;
                p2p.OnPeerDisconnected -= OnPeerDisconnected;
                p2p.OnPeerDisconnected += OnPeerDisconnected;
            }

            var lobby = EOSLobbyManager.Instance;
            if (lobby != null)
            {
                lobby.OnLobbyLeft -= OnLobbyLeft;
                lobby.OnLobbyLeft += OnLobbyLeft;
            }

            // If already online (component enabled late or re-enabled), spawn immediately
            if (!_hasSpawned && NetworkManager.Instance.IsOnline)
                SpawnLocalPlayer();
        }

        private void OnDisable()
        {
            var p2p = EOSP2PManager.Instance;
            if (p2p != null)
            {
                p2p.OnPeerConnected -= OnPeerConnected;
                p2p.OnPeerDisconnected -= OnPeerDisconnected;
            }

            var lobby = EOSLobbyManager.Instance;
            if (lobby != null)
                lobby.OnLobbyLeft -= OnLobbyLeft;
        }

        private void OnPeerConnected(ProductUserId peer)
        {
            if (!_hasSpawned)
                SpawnLocalPlayer();
        }

        private void OnPeerDisconnected(ProductUserId peer)
        {
            // If all peers disconnected, we're no longer online — clean up
            if (_hasSpawned && !NetworkManager.Instance.IsOnline)
                DespawnLocalPlayer();
        }

        private void OnLobbyLeft()
        {
            DespawnLocalPlayer();
        }

        /// <summary>
        /// Spawn the local player. Called automatically on first peer connect,
        /// but can also be called manually if you need to control timing.
        /// </summary>
        public void SpawnLocalPlayer()
        {
            if (_hasSpawned) return;

            if (_playerPrefab == null)
            {
                Debug.LogError("[PlayerSpawner] No player prefab assigned");
                return;
            }

            var netMgr = NetworkManager.Instance;
            if (netMgr == null) return;

            GetSpawnPoint(out var pos, out var rot);

            var netObj = netMgr.Spawn(_prefabId, pos, rot);
            if (netObj == null)
            {
                Debug.LogError("[PlayerSpawner] Failed to spawn player object");
                return;
            }

            netObj.DestroyWithOwner = _destroyWithOwner;
            LocalPlayer = netObj;
            _hasSpawned = true;

            OnLocalPlayerSpawned?.Invoke(netObj);
        }

        /// <summary>
        /// Despawn the local player. Called automatically on lobby leave or
        /// when all peers disconnect. Can also be called manually.
        /// </summary>
        public void DespawnLocalPlayer()
        {
            if (!_hasSpawned) return;

            if (LocalPlayer != null && LocalPlayer.IsRegistered)
            {
                NetworkManager.Instance?.Despawn(LocalPlayer);
            }

            LocalPlayer = null;
            _hasSpawned = false;
            OnLocalPlayerDespawned?.Invoke();
        }

        private void GetSpawnPoint(out Vector3 position, out Quaternion rotation)
        {
            if (_spawnPoints != null && _spawnPoints.Length > 0)
            {
                var sp = _spawnPoints[_spawnIndex % _spawnPoints.Length];
                _spawnIndex++;
                position = sp != null ? sp.position : transform.position;
                rotation = sp != null ? sp.rotation : transform.rotation;
            }
            else
            {
                position = transform.position;
                rotation = transform.rotation;
            }
        }
    }
}
