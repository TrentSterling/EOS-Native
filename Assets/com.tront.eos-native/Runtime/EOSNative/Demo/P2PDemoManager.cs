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
    /// Spawns balls via RegisterExisting (Layer 1 position sync), weapons via NetworkManager.Spawn()
    /// (Layer 2 snapshots for late-join). Broadcasts positions via EOSP2PManager, routes incoming packets.
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
        private const byte MSG_OBJECT_POSITION = 0x04; // [count:byte][foreach: netId:uint32 + pos:half3 + rot:uint32]

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
        private readonly List<(NetworkObject obj, P2PSpringSync sync)> _ownedSyncObjects = new();

        // Weapons (reparenting demo)
        private NetworkObject _heldWeapon;
        private bool _weaponsEnsured;

        // Mobile controls
        private Canvas _mobileCanvas;
        private JoystickDragHandler _joystickHandler;

        // Debug label background texture (cached to avoid alloc per frame)
        private Texture2D _debugBgTex;

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
            router.Register(MSG_OBJECT_POSITION, HandleObjectPosition);

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
                router.Unregister(MSG_OBJECT_POSITION);
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
            // Broadcast local ball position to all peers
            if (_localBall != null && _localSync != null)
            {
                _localSync.GetCompressedState(out var pos, out uint rot);

                _writer.Reset();
                _writer.WriteVector3Half(pos.ToVector3());
                _writer.WriteUInt32(rot);

                EOSP2PManager.Instance.Router.SendToAll(
                    MSG_POSITION, _writer, PacketReliability.UnreliableUnordered, CHANNEL_POSITION);
            }

            // Broadcast owned object (crate/weapon) positions — Layer 1 spring sync
            BroadcastOwnedObjectPositions();
        }

        #region Initialization

        private void InitializePrefabs()
        {
            // If auto-created (singleton), find the prefab table in the project
            if (_prefabTable == null)
            {
#if UNITY_EDITOR
                var guids = UnityEditor.AssetDatabase.FindAssets("t:NetworkPrefabTable");
                if (guids.Length > 0)
                {
                    var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                    _prefabTable = UnityEditor.AssetDatabase.LoadAssetAtPath<NetworkPrefabTable>(path);
                    EOSDebugLogger.Log(DebugCategory.EOSManager, "P2PDemoManager",
                        $"Auto-discovered prefab table: {path} ({_prefabTable.Count} prefabs)");
                }
#endif
            }

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
            _weaponsEnsured = false; // re-check weapons on new lobby join
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
            _weaponsEnsured = false;
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

            // Host ensures weapons exist (scene objects or spawned from prefab table)
            // Non-host: scene objects are claimed by snapshot via TryClaimSceneObject — no destruction needed
            if (!_weaponsEnsured && _localBall != null)
            {
                var nm = NetworkManager.Instance;
                if (nm.IsHost)
                {
                    _weaponsEnsured = true;
                    EnsureWeapons();
                }
                else
                {
                    _weaponsEnsured = true; // non-host relies on snapshot claiming
                }
            }

            // Ensure physics objects use P2PSpringSync for Layer 1 position sync
            EnsureSpringSync();

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

            // Weapon aiming and shooting
            if (_heldWeapon != null)
            {
                var weapon = _heldWeapon.GetComponent<DemoWeapon>();
                if (weapon != null)
                {
                    var aimDir = GetAimDirection();
                    if (aimDir.sqrMagnitude > 0.01f)
                        weapon.SetAimDirection(aimDir);

#if EOS_HAS_INPUT_SYSTEM
                    bool firePressed = Mouse.current != null && Mouse.current.leftButton.isPressed;
#else
                    bool firePressed = Input.GetMouseButton(0);
#endif
                    if (firePressed && !IsPointerOverUI() && weapon.CanFire)
                        weapon.ShootHitscan(aimDir);
                }
            }
        }

        #endregion

        #region Weapons

        /// <summary>
        /// Ensure weapons exist. Scene-placed weapons (P2PDemo) are registered via RegisterSceneObjects.
        /// If no weapons exist (e.g. SampleScene), host spawns them from the prefab table.
        /// </summary>
        private void EnsureWeapons()
        {
            // First, force RegisterSceneObjects to pick up any scene-placed weapons
            NetworkManager.Instance.RegisterSceneObjects();

            // Check if any weapons are already registered
            int weaponCount = 0;
            foreach (var kvp in NetworkManager.Instance.Objects)
            {
                if (kvp.Value.GetComponent<DemoWeapon>() != null)
                    weaponCount++;
            }

            if (weaponCount > 0)
            {
                Debug.Log($"[P2PDemo] {weaponCount} weapons registered from scene");
                return;
            }

            // No weapons in scene — spawn from prefab table (index 2 = Weapon)
            if (_prefabTable == null || _prefabTable.Count <= 2) return;
            var weaponPrefab = _prefabTable.GetPrefab(2);
            if (weaponPrefab == null) return;

            var nm = NetworkManager.Instance;
            Vector3[] positions = { new(-3, 0.5f, 0), new(3, 0.5f, 0), new(0, 0.5f, 3) };
            for (int i = 0; i < positions.Length; i++)
            {
                var obj = nm.Spawn(weaponPrefab, positions[i], Quaternion.identity);
                if (obj != null)
                    obj.name = $"Weapon_{i}";
            }
            Debug.Log($"[P2PDemo] Spawned {positions.Length} weapons from prefab table");
        }


        private Vector3 GetAimDirection()
        {
            var cam = Camera.main;
            if (cam == null || _localBall == null) return Vector3.forward;

#if EOS_HAS_INPUT_SYSTEM
            if (Mouse.current == null) return Vector3.forward;
            Vector3 mousePos = Mouse.current.position.ReadValue();
#else
            Vector3 mousePos = Input.mousePosition;
#endif

            var ray = cam.ScreenPointToRay(mousePos);
            float ballY = _localBall.transform.position.y;
            var plane = new Plane(Vector3.up, new Vector3(0f, ballY, 0f));
            if (plane.Raycast(ray, out float dist))
            {
                var aimPoint = ray.GetPoint(dist);
                var dir = aimPoint - _localBall.transform.position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.01f)
                    return dir.normalized;
            }

            return Vector3.forward;
        }

        private bool IsPointerOverUI()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

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

        #region Layer 1 Object Sync

        /// <summary>
        /// Broadcast positions of all owned physics objects (crates, weapons) via Layer 1 P2P.
        /// Uses the same spring sync as player balls for smooth semi-predicted physics.
        /// Skips held/reparented objects — they follow their parent transform.
        /// </summary>
        private void BroadcastOwnedObjectPositions()
        {
            var nm = NetworkManager._instance;
            if (nm == null) return;
            var p2p = EOSP2PManager._instance;
            if (p2p == null || p2p.Peers.Count == 0) return;

            _ownedSyncObjects.Clear();
            foreach (var kvp in nm.Objects)
            {
                var obj = kvp.Value;
                if (obj == null || !obj.IsOwner) continue;
                if (obj.GetComponent<P2PPlayerBall>() != null) continue;
                if (obj.ParentNetworkId != 0) continue; // held objects follow parent

                var sync = obj.GetComponent<P2PSpringSync>();
                if (sync == null || !sync.enabled) continue;

                _ownedSyncObjects.Add((obj, sync));
            }

            if (_ownedSyncObjects.Count == 0) return;

            _writer.Reset();
            _writer.WriteByte((byte)_ownedSyncObjects.Count);

            for (int i = 0; i < _ownedSyncObjects.Count; i++)
            {
                var (obj, sync) = _ownedSyncObjects[i];
                sync.GetCompressedState(out var pos, out uint rot);
                _writer.WriteUInt32(obj.NetworkId);
                _writer.WriteVector3Half(pos.ToVector3());
                _writer.WriteUInt32(rot);
            }

            p2p.Router.SendToAll(MSG_OBJECT_POSITION, _writer, PacketReliability.UnreliableUnordered, CHANNEL_POSITION);
        }

        private void HandleObjectPosition(ProductUserId sender, NetReader reader)
        {
            var nm = NetworkManager._instance;
            if (nm == null) return;

            int count = reader.ReadByte();
            for (int i = 0; i < count; i++)
            {
                uint netId = reader.ReadUInt32();
                var pos = reader.ReadVector3Half();
                uint rot = reader.ReadUInt32();

                if (!nm.Objects.TryGetValue(netId, out var obj) || obj == null || obj.IsOwner) continue;

                var sync = obj.GetComponent<P2PSpringSync>();
                if (sync != null)
                    sync.SetTarget(pos, P2PSpringSync.DecompressRotation(rot));
            }
        }

        /// <summary>
        /// Ensure all registered physics objects have P2PSpringSync for Layer 1 position sync.
        /// Disables NetworkTransform on these objects since P2PSpringSync handles position.
        /// Held/reparented objects have spring sync disabled (they follow parent transform).
        /// </summary>
        private void EnsureSpringSync()
        {
            var nm = NetworkManager._instance;
            if (nm == null) return;

            foreach (var kvp in nm.Objects)
            {
                var obj = kvp.Value;
                if (obj == null) continue;
                if (obj.GetComponent<P2PPlayerBall>() != null) continue;

                bool isChild = obj.ParentNetworkId != 0;

                var sync = obj.GetComponent<P2PSpringSync>();
                if (sync != null)
                {
                    sync.IsLocal = obj.IsOwner;
                    // Disable spring when held (follows parent), re-enable when dropped
                    sync.enabled = !isChild;
                    continue;
                }

                // Don't add to currently-reparented objects
                if (isChild) continue;

                // Only add to objects with Rigidbody (physics objects)
                if (obj.GetComponent<Rigidbody>() == null) continue;

                sync = obj.gameObject.AddComponent<P2PSpringSync>();
                sync.IsLocal = obj.IsOwner;

                // Disable NetworkTransform — P2PSpringSync handles position via Layer 1
                var nt = obj.GetComponent<NetworkTransform>();
                if (nt != null) nt.enabled = false;
            }
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
            GUI.Label(new Rect(10, y, 600, 25), "WASD: Move | Space: Jump | E: Color | Q: Shockwave | T: Chat | R: Effect | F: Pickup/Drop | G: Throw | LMB: Shoot", style);
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
                if (_localBehaviour != null && _localBehaviour.Score != null)
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

            // --- Debug HUD: NetworkManager summary ---
            y += 10f;
            var debugStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                normal = { textColor = new Color(1f, 0.8f, 0.3f) }
            };

            var nm2 = NetworkManager.Instance;
            var p2pInst = EOSP2PManager._instance;
            int pc = p2pInst?.Peers?.Count ?? 0;
            GUI.Label(new Rect(10, y, 900, 25),
                $"[Net] Host:{nm2.IsHost} | Objs:{nm2.Objects.Count} | P2P:{(pc > 0 ? $"OK({pc})" : "NONE")} | Snap:{nm2._snapshotReceived} | Scene:{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}",
                debugStyle);
            y += 18f;

            // Weapon summary
            if (_localBall != null)
            {
                int weaponCount = 0;
                float nearestDist = float.MaxValue;
                string nearestName = "none";
                bool nearestHeld = false;
                var ballPos = _localBall.transform.position;

                foreach (var kvp in nm2.Objects)
                {
                    var w = kvp.Value.GetComponent<DemoWeapon>();
                    if (w == null) continue;
                    weaponCount++;
                    float d = Vector3.Distance(ballPos, kvp.Value.transform.position);
                    if (d < nearestDist) { nearestDist = d; nearestName = kvp.Value.name; nearestHeld = w.IsHeld; }
                }

                string heldStr = _heldWeapon != null ? _heldWeapon.name : "none";
                GUI.Label(new Rect(10, y, 700, 25),
                    $"[Weapons] Count: {weaponCount} | Held: {heldStr} | Nearest: {nearestName} ({nearestDist:F1}m, held={nearestHeld})",
                    debugStyle);
                y += 18f;
            }

            // --- World-space debug labels over all NetworkObjects ---
            DrawWorldDebugLabels();
        }

        private void DrawWorldDebugLabels()
        {
            var cam = Camera.main;
            if (cam == null) return;

            var labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.UpperCenter,
                wordWrap = false
            };
            if (_debugBgTex == null)
            {
                _debugBgTex = new Texture2D(1, 1);
                _debugBgTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.65f));
                _debugBgTex.Apply();
            }
            var bgStyle = new GUIStyle { normal = { background = _debugBgTex } };

            foreach (var kvp in NetworkManager.Instance.Objects)
            {
                var obj = kvp.Value;
                if (obj == null) continue;

                float yOffset = 1.5f;
                var weapon = obj.GetComponent<DemoWeapon>();
                var ball = obj.GetComponent<DemoBallBehaviour>();

                var sp = cam.WorldToScreenPoint(obj.transform.position + Vector3.up * yOffset);
                if (sp.z <= 0 || sp.z > 80f) continue; // behind camera or too far

                float screenY = Screen.height - sp.y;

                // Build info lines
                string line1 = obj.name;
                string idHex = $"0x{obj.NetworkId:X8}";
                string prefabStr = obj.PrefabId == 0xFFFF ? "NoPrefab" :
                                   obj.PrefabId == 0xFFFE ? "Scene" : $"P:{obj.PrefabId}";
                string ownerStr = "none";
                if (obj.OwnerId != null)
                {
                    try { var s = obj.OwnerId.ToString(); ownerStr = s.Substring(0, Mathf.Min(6, s.Length)); }
                    catch { ownerStr = "err"; }
                }
                string line2 = $"{idHex} {prefabStr} O:{ownerStr}";

                // Common info
                string isOwnerStr = obj.IsOwner ? "YES" : "no";
                string parentStr = obj.ParentNetworkId != 0 ? $"0x{obj.ParentNetworkId:X8}" : "-";
                string line2b = $"IsOwner:{isOwnerStr} Parent:{parentStr}";

                // Extra info per type
                string line3 = "";
                string line4 = ""; // visual diagnostics
                if (weapon != null)
                {
                    string holder = string.IsNullOrEmpty(weapon.HolderName.Value) ? "free" : weapon.HolderName.Value;
                    line3 = $"Held:{holder} Aim:{weapon.AimAngle.Value:F0}";
                    // Visual diagnostics for weapons
                    var rend = obj.GetComponent<Renderer>();
                    var mf = obj.GetComponent<MeshFilter>();
                    bool rendOk = rend != null && rend.enabled;
                    bool meshOk = mf != null && mf.sharedMesh != null;
                    bool matOk = rend != null && rend.sharedMaterial != null;
                    bool activeH = obj.gameObject.activeInHierarchy;
                    var scale = obj.transform.localScale;
                    line4 = $"Rend:{(rendOk ? "OK" : "BAD")} Mesh:{(meshOk ? "OK" : "BAD")} Mat:{(matOk ? "OK" : "BAD")} ActH:{activeH} Scl:{scale.x:F1},{scale.y:F1},{scale.z:F1}";
                    labelStyle.normal.textColor = weapon.IsHeld ? Color.yellow : new Color(0.6f, 0.9f, 1f);
                }
                else if (ball != null)
                {
                    string displayName = string.IsNullOrEmpty(ball.DisplayName.Value) ? "?" : ball.DisplayName.Value;
                    line3 = $"{displayName} Score:{ball.Score.Value}";
                    labelStyle.normal.textColor = new Color(0.5f, 1f, 0.5f);
                }
                else
                {
                    // Crates and other objects — show basic visual diagnostics
                    var rend = obj.GetComponent<Renderer>();
                    bool rendOk = rend != null && rend.enabled;
                    bool activeH = obj.gameObject.activeInHierarchy;
                    line3 = $"Rend:{(rendOk ? "OK" : "BAD")} ActH:{activeH} IsOwner:{isOwnerStr}";
                    labelStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f);
                }

                string fullText = $"{line1}\n{line2}\n{line2b}";
                if (!string.IsNullOrEmpty(line3)) fullText += $"\n{line3}";
                if (!string.IsNullOrEmpty(line4)) fullText += $"\n{line4}";

                int lineCount = fullText.Split('\n').Length;
                float w = 280f;
                float h = lineCount * 14f + 4f;
                var rect = new Rect(sp.x - w * 0.5f, screenY - h, w, h);
                GUI.Box(rect, GUIContent.none, bgStyle);
                GUI.Label(rect, fullText, labelStyle);
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
