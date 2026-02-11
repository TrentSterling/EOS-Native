using EOSNative.Lobbies;
using EOSNative.P2P;

namespace EOSNative.Net
{
    /// <summary>
    /// Static accessor class for all key EOS-Native managers.
    /// Zero allocation — just property getters delegating to existing singletons.
    /// Matches the PurrNet InstanceFinder pattern for discoverability.
    /// </summary>
    public static class InstanceFinder
    {
        // Core managers
        public static NetworkManager NetworkManager => NetworkManager.Instance;
        public static EOSP2PManager P2PManager => EOSP2PManager.Instance;
        public static EOSLobbyManager LobbyManager => EOSLobbyManager.Instance;
        public static TickSimulation TickSimulation => TickSimulation.Instance;
        public static InterestManager InterestManager => InterestManager.Instance;
        public static NetworkSceneManager SceneManager => NetworkSceneManager.Instance;

        // Convenience shortcuts
        public static bool IsHost => NetworkManager.Instance?.IsHost ?? false;
        public static bool IsOnline => NetworkManager.Instance?.IsOnline ?? false;
        public static bool IsOffline => NetworkManager.Instance?.OfflineMode ?? false;
        public static uint CurrentTick => TickSimulation.Instance?.CurrentTick ?? 0;
        public static float FixedTickTime => TickSimulation.Instance?.FixedTickTime ?? 0f;
    }
}
