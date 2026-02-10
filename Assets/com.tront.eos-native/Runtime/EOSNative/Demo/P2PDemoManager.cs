using System.Collections.Generic;
using Epic.OnlineServices;
using Epic.OnlineServices.P2P;
using EOSNative.Lobbies;
using EOSNative.Logging;
using EOSNative.Net;
using EOSNative.P2P;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
#if EOS_HAS_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

namespace EOSNative.Demo
{
    /// <summary>
    /// P2P Ball Demo manager.
    /// Spawns local/remote balls from prefabs, manages weapons via NetworkManager.Spawn(),
    /// broadcasts positions via EOSP2PManager, routes incoming packets.
    /// Place in scene with _ballPrefab and _prefabTable assigned in Inspector.
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

        [SerializeField] private GameObject _ballPrefab;
        [SerializeField] private NetworkPrefabTable _prefabTable;

        private P2PPlayerBall _localBall;
        private P2PSpringSync _localSync;
        private DemoBallBehaviour _localBehaviour;
        private Color _localColor;
        private readonly Dictionary<string, P2PPlayerBall> _remoteBalls = new();
        private readonly Dictionary<string, P2PSpringSync> _remoteSyncs = new();
        private readonly Dictionary<string, DemoBallBehaviour> _remoteBehaviours = new();
        private readonly NetWriter _writer = new();
        private bool _localSpawned;
        private int _colorIndex;

        // Weapons (reparenting demo)
        private NetworkObject _heldWeapon;

        // Mobile controls
        private Canvas _mobileCanvas;
        private JoystickDragHandler _joystickHandler;

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
            InitializePrefabs();
            RegisterSceneWeapons();
            CreateMobileControls();

            // If already in a lobby when demo starts, spawn immediately
            if (EOSLobbyManager.Instance != null && EOSLobbyManager.Instance.IsInLobby)
                SpawnLocalBall();
        }

        private void OnEnable()
        {
            var p2p = EOSP2PManager.Instance;
            p2p.OnPeerConnected += OnPeerConnected;
            p2p.OnPeerDisconnected += OnPeerDisconnected;
            // Router.ProcessIncoming is auto-subscribed by EOSP2PManager.Router getter — no manual wiring needed

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

        #region Initialization

        private void InitializePrefabs()
        {
            // Register all prefabs from the table with NetworkManager
            if (_prefabTable != null)
            {
                var nm = NetworkManager.Instance;
                nm.PrefabTable = _prefabTable;
                for (int i = 0; i < _prefabTable.Count; i++)
                {
                    var prefab = _prefabTable.GetPrefab(i);
                    if (prefab != null)
                        nm.RegisterPrefab(prefab, (ushort)i);
                }
            }
        }

        private void RegisterSceneWeapons()
        {
            // Scene-placed weapons need to be registered with NetworkManager
            // so FindNearestWeapon (which searches Objects) can find them,
            // and so SetNetworkParent/DetachFromNetworkParent work.
            var weapons = FindObjectsByType<DemoWeapon>(FindObjectsSortMode.None);
            for (int i = 0; i < weapons.Length; i++)
            {
                var netObj = weapons[i].GetComponent<NetworkObject>();
                if (netObj != null && netObj.NetworkId == 0)
                {
                    uint id = 0xCC000000u | (uint)i;
                    NetworkManager.Instance.RegisterExisting(netObj, id);
                }
            }
            if (weapons.Length > 0)
                EOSDebugLogger.Log(DebugCategory.PlayerBall, "P2PDemoManager", $"Registered {weapons.Length} scene weapons");
        }

        private void CreateMobileControls()
        {
            // Ensure EventSystem exists
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var esGo = new GameObject("EventSystem");
                esGo.AddComponent<EventSystem>();
#if EOS_HAS_INPUT_SYSTEM
                esGo.AddComponent<InputSystemUIInputModule>();
#else
                esGo.AddComponent<StandaloneInputModule>();
#endif
            }

            // Canvas
            var canvasGo = new GameObject("MobileControls");
            _mobileCanvas = canvasGo.AddComponent<Canvas>();
            _mobileCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _mobileCanvas.sortingOrder = 100;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            // --- Virtual Joystick (bottom-left) ---
            var joystickGo = new GameObject("JoystickBg");
            joystickGo.transform.SetParent(canvasGo.transform, false);
            var bgImg = joystickGo.AddComponent<Image>();
            bgImg.color = new Color(1f, 1f, 1f, 0.2f);
            var joystickBg = bgImg.rectTransform;
            joystickBg.anchorMin = new Vector2(0, 0);
            joystickBg.anchorMax = new Vector2(0, 0);
            joystickBg.pivot = new Vector2(0.5f, 0.5f);
            joystickBg.sizeDelta = new Vector2(240, 240);
            joystickBg.anchoredPosition = new Vector2(180, 200);

            var thumbGo = new GameObject("JoystickThumb");
            thumbGo.transform.SetParent(joystickGo.transform, false);
            var thumbImg = thumbGo.AddComponent<Image>();
            thumbImg.color = new Color(1f, 1f, 1f, 0.5f);
            var joystickThumb = thumbImg.rectTransform;
            joystickThumb.sizeDelta = new Vector2(80, 80);
            joystickThumb.anchoredPosition = Vector2.zero;

            // Attach EventSystem drag handler
            _joystickHandler = joystickGo.AddComponent<JoystickDragHandler>();
            _joystickHandler.Thumb = joystickThumb;
            _joystickHandler.Radius = 80f;

            // --- Jump Button (bottom-right) ---
            var jumpGo = new GameObject("JumpBtn");
            jumpGo.transform.SetParent(canvasGo.transform, false);
            var jumpImg = jumpGo.AddComponent<Image>();
            jumpImg.color = new Color(0.2f, 0.6f, 1f, 0.4f);
            var jumpRect = jumpImg.rectTransform;
            jumpRect.anchorMin = new Vector2(1, 0);
            jumpRect.anchorMax = new Vector2(1, 0);
            jumpRect.pivot = new Vector2(0.5f, 0.5f);
            jumpRect.sizeDelta = new Vector2(160, 160);
            jumpRect.anchoredPosition = new Vector2(-150, 200);

            var jumpBtn = jumpGo.AddComponent<Button>();
            jumpBtn.onClick.AddListener(OnJumpPressed);

            var jumpLabel = new GameObject("Label");
            jumpLabel.transform.SetParent(jumpGo.transform, false);
            var jumpText = jumpLabel.AddComponent<Text>();
            jumpText.text = "JUMP";
            jumpText.alignment = TextAnchor.MiddleCenter;
            jumpText.fontSize = 28;
            jumpText.color = Color.white;
            jumpText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                          ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            var jumpTextRect = jumpText.rectTransform;
            jumpTextRect.anchorMin = Vector2.zero;
            jumpTextRect.anchorMax = Vector2.one;
            jumpTextRect.sizeDelta = Vector2.zero;
        }

        private void OnJumpPressed()
        {
            if (_localBall != null)
                _localBall.MobileJump = true;
        }

        #endregion

        #region Ball Spawning

        private void SpawnLocalBall()
        {
            if (_localSpawned) return;
            _localSpawned = true;

            _localColor = UnityEngine.Random.ColorHSV(0f, 1f, 0.7f, 1f, 0.7f, 1f);

            var localPuid = EOSManager.Instance?.LocalProductUserId;
            var ball = CreateBall("LocalBall", true, localPuid);
            _localBall = ball.GetComponent<P2PPlayerBall>();
            _localSync = ball.GetComponent<P2PSpringSync>();
            _localBehaviour = ball.GetComponent<DemoBallBehaviour>();
            _localBall.SetColor(_localColor);

            // Set Layer 2 SyncVars
            _localBehaviour.BallColor.Value = _localColor;
            string playerName = "Player";
            if (localPuid != null)
            {
                var registry = EOSNative.EOSPlayerRegistry.Instance;
                if (registry != null)
                    playerName = registry.GetOrGenerateName(localPuid.ToString());
                else
                    playerName = localPuid.ToString().Substring(0, 6);
            }
            _localBehaviour.DisplayName.Value = playerName;

            EOSDebugLogger.Log(DebugCategory.PlayerBall, "P2PDemoManager", "Local ball spawned");
        }

        private void SpawnRemoteBall(string puid, Color color, ProductUserId senderPuid = null)
        {
            if (_remoteBalls.ContainsKey(puid)) return;

            var ball = CreateBall($"RemoteBall_{puid}", false, senderPuid);
            // Offset spawn so balls don't overlap
            ball.transform.position = new Vector3(
                UnityEngine.Random.Range(-2f, 2f), 1f,
                UnityEngine.Random.Range(-2f, 2f)
            );

            var playerBall = ball.GetComponent<P2PPlayerBall>();
            playerBall.SetColor(color);

            _remoteBalls[puid] = playerBall;
            _remoteSyncs[puid] = ball.GetComponent<P2PSpringSync>();
            _remoteBehaviours[puid] = ball.GetComponent<DemoBallBehaviour>();

            EOSDebugLogger.Log(DebugCategory.PlayerBall, "P2PDemoManager", $"Remote ball spawned for {puid}");
        }

        private void DestroyRemoteBall(string puid)
        {
            if (_remoteBalls.TryGetValue(puid, out var ball))
            {
                if (ball != null) Destroy(ball.gameObject);
                _remoteBalls.Remove(puid);
                _remoteSyncs.Remove(puid);
                _remoteBehaviours.Remove(puid);
                EOSDebugLogger.Log(DebugCategory.PlayerBall, "P2PDemoManager", $"Remote ball destroyed for {puid}");
            }
        }

        private GameObject CreateBall(string name, bool isLocal, ProductUserId ownerPuid = null)
        {
            var go = Instantiate(_ballPrefab);
            go.name = name;
            go.transform.position = new Vector3(0f, 1f, 0f);

            go.GetComponent<P2PPlayerBall>().IsLocal = isLocal;
            go.GetComponent<P2PSpringSync>().IsLocal = isLocal;

            var netObj = go.GetComponent<NetworkObject>();
            if (ownerPuid != null)
                netObj.OwnerId = ownerPuid;

            // Generate deterministic NetworkId from PUID
            if (ownerPuid != null)
            {
                uint deterministicId = 0xBB000000u | (NetworkManager.FnvHash(ownerPuid.ToString()) & 0x00FFFFFFu);
                NetworkManager.Instance.RegisterExisting(netObj, deterministicId);
            }

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
            _remoteBehaviours.Clear();

            // Destroy local ball
            if (_localBall != null)
            {
                Destroy(_localBall.gameObject);
                _localBall = null;
                _localSync = null;
                _localBehaviour = null;
                _localSpawned = false;
            }

            _heldWeapon = null;
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
            _heldWeapon = null;
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
            SpawnRemoteBall(sender.ToString(), color, sender);
        }

        private void HandleLeave(ProductUserId sender, NetReader reader)
        {
            DestroyRemoteBall(sender.ToString());
        }

        #endregion

        #region Input

        private static readonly Color[] _colorPresets = new[]
        {
            Color.red, Color.green, Color.blue, Color.yellow,
            Color.cyan, Color.magenta, new Color(1f, 0.5f, 0f), new Color(0.5f, 0f, 1f)
        };

        private void Update()
        {
            // Feed joystick input to ball
            if (_localBall != null && _joystickHandler != null)
                _localBall.MobileInput = _joystickHandler.Input;

            if (_localBall == null || _localBehaviour == null) return;

#if EOS_HAS_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            bool eDown = keyboard.eKey.wasPressedThisFrame;
            bool qDown = keyboard.qKey.wasPressedThisFrame;
            bool tDown = keyboard.tKey.wasPressedThisFrame;
            bool rDown = keyboard.rKey.wasPressedThisFrame;
            bool fDown = keyboard.fKey.wasPressedThisFrame;
            bool gDown = keyboard.gKey.wasPressedThisFrame;
#else
            bool eDown = Input.GetKeyDown(KeyCode.E);
            bool qDown = Input.GetKeyDown(KeyCode.Q);
            bool tDown = Input.GetKeyDown(KeyCode.T);
            bool rDown = Input.GetKeyDown(KeyCode.R);
            bool fDown = Input.GetKeyDown(KeyCode.F);
            bool gDown = Input.GetKeyDown(KeyCode.G);
#endif

            // E: Cycle color
            if (eDown)
            {
                _colorIndex = (_colorIndex + 1) % _colorPresets.Length;
                var c = _colorPresets[_colorIndex];
                _localBehaviour.ChangeColor(c.r, c.g, c.b);
            }

            // Q: Shockwave impulse — push all nearby balls outward
            if (qDown)
            {
                _localBehaviour.ApplyImpulse(0f, 1f, 0f, 8f);
                foreach (var kvp in _remoteBehaviours)
                {
                    if (kvp.Value != null)
                    {
                        Vector3 dir = (kvp.Value.transform.position - _localBall.transform.position).normalized;
                        kvp.Value.ApplyImpulse(dir.x, dir.y + 0.5f, dir.z, 5f);
                    }
                }
            }

            // T: Chat bubble
            if (tDown)
            {
                _localBehaviour.ChatBubble("Hello!");
            }

            // R: Play effect
            if (rDown)
            {
                _localBehaviour.PlayEffect((byte)UnityEngine.Random.Range(0, 3));
            }

            // F: Pickup / Drop weapon
            if (fDown)
            {
                if (_heldWeapon != null)
                {
                    _heldWeapon.GetComponent<DemoWeapon>().Drop();
                    _heldWeapon = null;
                }
                else
                {
                    var nearest = FindNearestWeapon(2f);
                    if (nearest != null)
                    {
                        var localNetObj = _localBall.GetComponent<NetworkObject>();
                        nearest.GetComponent<DemoWeapon>().Pickup(localNetObj, _localBehaviour.DisplayName.Value);
                        _heldWeapon = nearest;
                    }
                }
            }

            // G: Throw weapon
            if (gDown && _heldWeapon != null)
            {
                var rb = _localBall.GetComponent<Rigidbody>();
                var direction = rb != null ? rb.linearVelocity.normalized : Vector3.forward;
                if (direction.sqrMagnitude < 0.01f) direction = Vector3.forward;
                _heldWeapon.GetComponent<DemoWeapon>().Throw(direction, 8f);
                _heldWeapon = null;
            }
        }

        #endregion

        #region Weapons

        private NetworkObject FindNearestWeapon(float maxDist)
        {
            NetworkObject best = null;
            float bestDist = maxDist;
            var ballPos = _localBall.transform.position;

            foreach (var kvp in NetworkManager.Instance.Objects)
            {
                var weapon = kvp.Value.GetComponent<DemoWeapon>();
                if (weapon == null || weapon.IsHeld) continue;

                float dist = Vector3.Distance(ballPos, kvp.Value.transform.position);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = kvp.Value;
                }
            }

            return best;
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
            GUI.Label(new Rect(10, y, 500, 25), "P2P Ball Demo (Layer 1 + Layer 2)", style);
            y += 20f;
            GUI.Label(new Rect(10, y, 600, 25), "WASD: Move | Space: Jump | E: Color | Q: Shockwave | T: Chat | R: Effect | F: Pickup/Drop | G: Throw", style);
            y += 20f;
            GUI.Label(new Rect(10, y, 500, 25), "F1: EOS Overlay", style);
            y += 20f;

            var lobby = EOSLobbyManager.Instance;
            if (lobby != null && lobby.IsInLobby)
            {
                GUI.Label(new Rect(10, y, 400, 25), $"Lobby: {lobby.CurrentLobby.LobbyId?.Substring(0, Mathf.Min(8, lobby.CurrentLobby.LobbyId?.Length ?? 0))}...", style);
                y += 20f;

                // Diagnostics: P2P status
                var p2p = EOSP2PManager.Instance;
                int peerCount = p2p != null ? p2p.Peers.Count : 0;
                GUI.Label(new Rect(10, y, 500, 25), $"P2P Peers: {peerCount} | Remote balls: {_remoteBalls.Count} | Local ball: {(_localBall != null ? "YES" : "NO")}", style);
                y += 20f;

                // Diagnostics: Voice status
                var voice = EOSNative.Voice.EOSVoiceManager.Instance;
                if (voice != null)
                {
                    string voiceStatus = voice.IsConnected ? "Connected" : "Disconnected";
                    int participants = voice.ParticipantCount;
                    bool muted = voice.IsMuted;
                    GUI.Label(new Rect(10, y, 500, 25), $"Voice: {voiceStatus} | Participants: {participants} | Muted: {muted}", style);
                    y += 20f;
                }

                // Scores
                if (_localBehaviour != null)
                {
                    GUI.Label(new Rect(10, y, 400, 25), $"Your Score: {_localBehaviour.Score.Value}", style);
                    y += 20f;
                }
                foreach (var kvp in _remoteBehaviours)
                {
                    if (kvp.Value != null)
                    {
                        string name = string.IsNullOrEmpty(kvp.Value.DisplayName.Value)
                            ? kvp.Key.Substring(0, Mathf.Min(6, kvp.Key.Length))
                            : kvp.Value.DisplayName.Value;
                        GUI.Label(new Rect(10, y, 400, 25), $"{name}: {kvp.Value.Score.Value}", style);
                        y += 20f;
                    }
                }
            }
            else
            {
                GUI.Label(new Rect(10, y, 400, 25), "Join/create a lobby via F1 overlay to start", style);
            }

            // Weapon debug info (always shown)
            if (_localBall != null)
            {
                y += 10f;
                var debugStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 13,
                    normal = { textColor = new Color(1f, 0.8f, 0.3f) }
                };

                int registeredWeapons = 0;
                float nearestDist = float.MaxValue;
                string nearestName = "none";
                bool nearestHeld = false;
                var ballPos = _localBall.transform.position;

                foreach (var kvp in NetworkManager.Instance.Objects)
                {
                    var w = kvp.Value.GetComponent<DemoWeapon>();
                    if (w == null) continue;
                    registeredWeapons++;
                    float d = Vector3.Distance(ballPos, kvp.Value.transform.position);
                    if (d < nearestDist)
                    {
                        nearestDist = d;
                        nearestName = kvp.Value.name;
                        nearestHeld = w.IsHeld;
                    }
                }

                string heldStr = _heldWeapon != null ? _heldWeapon.name : "none";
                GUI.Label(new Rect(10, y, 600, 25),
                    $"[Weapons] Registered: {registeredWeapons} | Held: {heldStr} | Nearest: {nearestName} ({nearestDist:F1}m, held={nearestHeld}) | Pickup range: 2m",
                    debugStyle);
                y += 18f;

                // Draw pickup range indicator on nearest weapon
                if (nearestDist < 5f && nearestDist < float.MaxValue && Camera.main != null)
                {
                    var rangeStyle = new GUIStyle(GUI.skin.label)
                    {
                        fontSize = 14,
                        fontStyle = FontStyle.Bold,
                        alignment = TextAnchor.MiddleCenter,
                        normal = { textColor = nearestDist <= 2f ? Color.green : Color.red }
                    };
                    // Find nearest weapon transform for screen position
                    foreach (var kvp in NetworkManager.Instance.Objects)
                    {
                        var w = kvp.Value.GetComponent<DemoWeapon>();
                        if (w == null) continue;
                        float d = Vector3.Distance(ballPos, kvp.Value.transform.position);
                        if (Mathf.Abs(d - nearestDist) < 0.01f)
                        {
                            var sp = Camera.main.WorldToScreenPoint(kvp.Value.transform.position + Vector3.up * 1.2f);
                            if (sp.z > 0)
                            {
                                string label = nearestDist <= 2f ? $"[F] PICKUP ({d:F1}m)" : $"({d:F1}m)";
                                GUI.Label(new Rect(sp.x - 60, Screen.height - sp.y, 120, 25), label, rangeStyle);
                            }
                            break;
                        }
                    }
                }
            }
        }

        #endregion

        #region JoystickDragHandler

        /// <summary>
        /// EventSystem-based virtual joystick. Works with both mouse and touch input.
        /// </summary>
        private class JoystickDragHandler : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
        {
            public RectTransform Thumb;
            public float Radius = 80f;

            /// <summary>Current normalized joystick input (-1 to 1 per axis).</summary>
            public Vector2 Input { get; private set; }

            public void OnPointerDown(PointerEventData eventData)
            {
                OnDrag(eventData);
            }

            public void OnDrag(PointerEventData eventData)
            {
                var bg = (RectTransform)transform;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    bg, eventData.position, eventData.pressEventCamera, out var localPoint);
                var offset = Vector2.ClampMagnitude(localPoint, Radius);
                if (Thumb != null) Thumb.anchoredPosition = offset;
                Input = offset / Radius;
            }

            public void OnPointerUp(PointerEventData eventData)
            {
                if (Thumb != null) Thumb.anchoredPosition = Vector2.zero;
                Input = Vector2.zero;
            }
        }

        #endregion
    }
}
