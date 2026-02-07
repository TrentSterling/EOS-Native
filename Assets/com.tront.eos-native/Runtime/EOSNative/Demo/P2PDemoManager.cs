using System.Collections.Generic;
using Epic.OnlineServices;
using Epic.OnlineServices.P2P;
using EOSNative.Lobbies;
using EOSNative.Logging;
using EOSNative.P2P;
using UnityEngine;

namespace EOSNative.Demo
{
    /// <summary>
    /// P2P Ball Demo scene manager.
    /// Generates ground + crates at runtime, spawns local/remote balls,
    /// broadcasts positions via EOSP2PManager, routes incoming packets.
    /// </summary>
    public class P2PDemoManager : MonoBehaviour
    {
        #region Singleton

        private static P2PDemoManager _instance;
        public static P2PDemoManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<P2PDemoManager>();
                    if (_instance == null)
                    {
                        var go = new GameObject("P2PDemoManager");
                        if (EOSManager.Instance != null)
                            go.transform.SetParent(EOSManager.Instance.transform);
                        else
                            DontDestroyOnLoad(go);
                        _instance = go.AddComponent<P2PDemoManager>();
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Message IDs

        private const byte MSG_POSITION = 0x01; // Vector3Half(6) + compressed rot(4) = 10 bytes
        private const byte MSG_JOIN = 0x02;      // R(1) + G(1) + B(1) = 3 bytes
        private const byte MSG_LEAVE = 0x03;     // 0 bytes (just the msgId)

        private const byte CHANNEL_POSITION = 0;
        private const byte CHANNEL_RELIABLE = 1;

        #endregion

        #region Fields

        private P2PPlayerBall _localBall;
        private P2PSpringSync _localSync;
        private Color _localColor;
        private readonly Dictionary<string, P2PPlayerBall> _remoteBalls = new();
        private readonly Dictionary<string, P2PSpringSync> _remoteSyncs = new();
        private readonly NetWriter _writer = new();
        private bool _sceneGenerated;
        private bool _localSpawned;

        #endregion

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            if (transform.parent == null)
                DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            GenerateScene();

            // If already in a lobby when demo starts, spawn immediately
            if (EOSLobbyManager.Instance != null && EOSLobbyManager.Instance.IsInLobby)
                SpawnLocalBall();
        }

        private void OnEnable()
        {
            var p2p = EOSP2PManager.Instance;
            p2p.OnPeerConnected += OnPeerConnected;
            p2p.OnPeerDisconnected += OnPeerDisconnected;
            p2p.OnPacketReceived += p2p.Router.ProcessIncoming;

            var router = p2p.Router;
            router.Register(MSG_POSITION, HandlePosition);
            router.Register(MSG_JOIN, HandleJoin);
            router.Register(MSG_LEAVE, HandleLeave);

            var lobby = EOSLobbyManager.Instance;
            lobby.OnLobbyJoined += OnLobbyJoined;
            lobby.OnLobbyLeft += OnLobbyLeftHandler;
        }

        private void OnDisable()
        {
            var p2p = EOSP2PManager.Instance;
            if (p2p != null)
            {
                p2p.OnPeerConnected -= OnPeerConnected;
                p2p.OnPeerDisconnected -= OnPeerDisconnected;
                p2p.OnPacketReceived -= p2p.Router.ProcessIncoming;

                var router = p2p.Router;
                router.Unregister(MSG_POSITION);
                router.Unregister(MSG_JOIN);
                router.Unregister(MSG_LEAVE);
            }

            var lobby = EOSLobbyManager.Instance;
            if (lobby != null)
            {
                lobby.OnLobbyJoined -= OnLobbyJoined;
                lobby.OnLobbyLeft -= OnLobbyLeftHandler;
            }
        }

        private void FixedUpdate()
        {
            if (_localBall == null || _localSync == null) return;

            // Broadcast local position to all peers
            _localSync.GetCompressedState(out var pos, out uint rot);

            _writer.Reset();
            _writer.WriteVector3Half(pos.ToVector3());
            _writer.WriteUInt32(rot);

            EOSP2PManager.Instance.Router.SendToAll(
                MSG_POSITION, _writer, PacketReliability.UnreliableUnordered, CHANNEL_POSITION);
        }

        #region Scene Generation

        private void GenerateScene()
        {
            if (_sceneGenerated) return;
            _sceneGenerated = true;

            // Ground plane
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(30f, 1f, 30f);
            ground.GetComponent<Renderer>().material.color = new Color(0.3f, 0.4f, 0.3f);

            // Crate obstacles
            var cratePositions = new Vector3[]
            {
                new(3f, 0.5f, 3f),
                new(-4f, 0.5f, 2f),
                new(5f, 0.5f, -4f),
                new(-3f, 0.5f, -5f),
                new(0f, 0.5f, 6f),
                new(-6f, 0.5f, -1f),
                new(7f, 0.5f, 1f),
                new(1f, 0.5f, -7f),
            };

            foreach (var pos in cratePositions)
            {
                var crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                crate.name = "Crate";
                crate.transform.position = pos;
                crate.transform.localScale = new Vector3(1.5f, 1.5f, 1.5f);
                crate.GetComponent<Renderer>().material.color = new Color(0.6f, 0.45f, 0.25f);

                var rb = crate.AddComponent<Rigidbody>();
                rb.mass = 3f;
            }

            // Wall borders
            CreateWall("WallN", new Vector3(0f, 1f, 15f), new Vector3(32f, 2f, 1f));
            CreateWall("WallS", new Vector3(0f, 1f, -15f), new Vector3(32f, 2f, 1f));
            CreateWall("WallE", new Vector3(15f, 1f, 0f), new Vector3(1f, 2f, 32f));
            CreateWall("WallW", new Vector3(-15f, 1f, 0f), new Vector3(1f, 2f, 32f));

            // Camera
            var camGo = Camera.main != null ? Camera.main.gameObject : new GameObject("DemoCamera");
            if (camGo.GetComponent<Camera>() == null) camGo.AddComponent<Camera>();
            camGo.transform.position = new Vector3(0f, 10f, -5f);
            if (camGo.GetComponent<P2PDemoCamera>() == null) camGo.AddComponent<P2PDemoCamera>();

            // Light
            if (FindAnyObjectByType<Light>() == null)
            {
                var lightGo = new GameObject("DirectionalLight");
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1f;
                lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }
        }

        private void CreateWall(string name, Vector3 pos, Vector3 scale)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            wall.transform.position = pos;
            wall.transform.localScale = scale;
            wall.GetComponent<Renderer>().material.color = new Color(0.5f, 0.5f, 0.5f);
        }

        #endregion

        #region Ball Spawning

        private void SpawnLocalBall()
        {
            if (_localSpawned) return;
            _localSpawned = true;

            _localColor = UnityEngine.Random.ColorHSV(0f, 1f, 0.7f, 1f, 0.7f, 1f);

            var ball = CreateBall("LocalBall", true);
            _localBall = ball.GetComponent<P2PPlayerBall>();
            _localSync = ball.GetComponent<P2PSpringSync>();
            _localBall.SetColor(_localColor);

            EOSDebugLogger.Log(DebugCategory.PlayerBall, "P2PDemoManager", "Local ball spawned");
        }

        private void SpawnRemoteBall(string puid, Color color)
        {
            if (_remoteBalls.ContainsKey(puid)) return;

            var ball = CreateBall($"RemoteBall_{puid}", false);
            // Offset spawn so balls don't overlap
            ball.transform.position = new Vector3(
                UnityEngine.Random.Range(-2f, 2f), 1f,
                UnityEngine.Random.Range(-2f, 2f)
            );

            var playerBall = ball.GetComponent<P2PPlayerBall>();
            playerBall.SetColor(color);

            _remoteBalls[puid] = playerBall;
            _remoteSyncs[puid] = ball.GetComponent<P2PSpringSync>();

            EOSDebugLogger.Log(DebugCategory.PlayerBall, "P2PDemoManager", $"Remote ball spawned for {puid}");
        }

        private void DestroyRemoteBall(string puid)
        {
            if (_remoteBalls.TryGetValue(puid, out var ball))
            {
                if (ball != null) Destroy(ball.gameObject);
                _remoteBalls.Remove(puid);
                _remoteSyncs.Remove(puid);
                EOSDebugLogger.Log(DebugCategory.PlayerBall, "P2PDemoManager", $"Remote ball destroyed for {puid}");
            }
        }

        private GameObject CreateBall(string name, bool isLocal)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.position = new Vector3(0f, 1f, 0f);

            var rb = go.GetComponent<Rigidbody>();
            if (rb == null) rb = go.AddComponent<Rigidbody>();
            rb.mass = 1f;
            rb.angularDamping = 0.5f;

            var playerBall = go.AddComponent<P2PPlayerBall>();
            playerBall.IsLocal = isLocal;

            var sync = go.AddComponent<P2PSpringSync>();
            sync.IsLocal = isLocal;

            return go;
        }

        #endregion

        #region Lobby Events

        private void OnLobbyJoined(LobbyData lobby)
        {
            SpawnLocalBall();
        }

        private void OnLobbyLeftHandler()
        {
            // Destroy all remote balls
            foreach (var kvp in _remoteBalls)
            {
                if (kvp.Value != null) Destroy(kvp.Value.gameObject);
            }
            _remoteBalls.Clear();
            _remoteSyncs.Clear();

            // Destroy local ball
            if (_localBall != null)
            {
                Destroy(_localBall.gameObject);
                _localBall = null;
                _localSync = null;
                _localSpawned = false;
            }
        }

        #endregion

        #region P2P Events

        private void OnPeerConnected(ProductUserId peer)
        {
            // Send join packet with our color
            _writer.Reset();
            _writer.WriteByte((byte)(_localColor.r * 255f));
            _writer.WriteByte((byte)(_localColor.g * 255f));
            _writer.WriteByte((byte)(_localColor.b * 255f));

            EOSP2PManager.Instance.Router.SendToPeerImmediate(
                MSG_JOIN, _writer, peer, PacketReliability.ReliableOrdered, CHANNEL_RELIABLE);
        }

        private void OnPeerDisconnected(ProductUserId peer)
        {
            DestroyRemoteBall(peer.ToString());
        }

        #endregion

        #region Message Handlers

        private void HandlePosition(ProductUserId sender, NetReader reader)
        {
            string puid = sender.ToString();
            if (!_remoteSyncs.TryGetValue(puid, out var sync) || sync == null) return;

            var pos = reader.ReadVector3Half();
            uint rot = reader.ReadUInt32();
            var rotation = P2PSpringSync.DecompressRotation(rot);

            sync.SetTarget(pos, rotation);
        }

        private void HandleJoin(ProductUserId sender, NetReader reader)
        {
            Color color = new(reader.ReadByte() / 255f, reader.ReadByte() / 255f, reader.ReadByte() / 255f);
            SpawnRemoteBall(sender.ToString(), color);
        }

        private void HandleLeave(ProductUserId sender, NetReader reader)
        {
            DestroyRemoteBall(sender.ToString());
        }

        #endregion

        #region HUD

        private void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                normal = { textColor = Color.white }
            };

            float y = 10f;
            GUI.Label(new Rect(10, y, 400, 25), "P2P Ball Demo", style);
            y += 20f;
            GUI.Label(new Rect(10, y, 400, 25), "WASD: Move | Space: Jump | F1: EOS Overlay", style);
            y += 20f;

            var lobby = EOSLobbyManager.Instance;
            if (lobby != null && lobby.IsInLobby)
            {
                GUI.Label(new Rect(10, y, 400, 25), $"Lobby: {lobby.CurrentLobby.LobbyId?.Substring(0, Mathf.Min(8, lobby.CurrentLobby.LobbyId?.Length ?? 0))}...", style);
                y += 20f;
                GUI.Label(new Rect(10, y, 400, 25), $"Peers: {EOSP2PManager.Instance.Peers.Count}", style);
                y += 20f;
                GUI.Label(new Rect(10, y, 400, 25), $"Remote balls: {_remoteBalls.Count}", style);
            }
            else
            {
                GUI.Label(new Rect(10, y, 400, 25), "Join/create a lobby via F1 overlay to start", style);
            }

            // Name labels above balls
            if (Camera.main != null)
            {
                if (_localBall != null)
                    DrawBallLabel(_localBall.transform.position, "YOU");

                foreach (var kvp in _remoteBalls)
                {
                    if (kvp.Value != null)
                    {
                        string shortPuid = kvp.Key.Length > 6 ? kvp.Key.Substring(0, 6) : kvp.Key;
                        DrawBallLabel(kvp.Value.transform.position, shortPuid);
                    }
                }
            }
        }

        private void DrawBallLabel(Vector3 worldPos, string label)
        {
            Vector3 screenPos = Camera.main.WorldToScreenPoint(worldPos + Vector3.up * 1.5f);
            if (screenPos.z <= 0) return;

            float width = Mathf.Max(60f, label.Length * 9f);
            var rect = new Rect(screenPos.x - width / 2f, Screen.height - screenPos.y, width, 22f);
            GUI.Label(rect, label, new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 13,
                normal = { textColor = Color.white }
            });
        }

        #endregion
    }
}
