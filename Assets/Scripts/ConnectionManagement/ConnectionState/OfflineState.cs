using Unity.BossRoom.OdinServices.Auth;
using Unity.BossRoom.OdinServices.Sessions;
using Unity.BossRoom.Utils;
using Unity.Multiplayer.Samples.Utilities;
using UnityEngine.SceneManagement;
using VContainer;

namespace Unity.BossRoom.ConnectionManagement
{
    /// <summary>
    /// Connection state corresponding to when the NetworkManager is shut down. From this state we can transition to the
    /// ClientConnecting sate, if starting as a client, or the StartingHost state, if starting as a host.
    /// </summary>
    class OfflineState : ConnectionState
    {
        [Inject]
        GatheringsFacade m_GatheringsFacade;
        [Inject]
        PlayerAuthFacade m_PlayerAuthFacade;
        [Inject]
        ProfileManager m_ProfileManager;

        const string k_MainMenuSceneName = "MainMenu";

        public override void Enter()
        {
            m_GatheringsFacade.EndTracking();
            m_ConnectionManager.NetworkManager.Shutdown();
            if (SceneManager.GetActiveScene().name != k_MainMenuSceneName)
            {
                SceneLoaderWrapper.Instance.LoadScene(k_MainMenuSceneName, useNetworkSceneManager: false);
            }
        }

        public override void Exit() { }

        public override void StartClientSession(string playerName)
        {
            StartClient(new ConnectionMethodOdin(m_GatheringsFacade, m_ConnectionManager, GetPlayerId(), playerName));
        }

        public override void StartHostSession(string playerName)
        {
            StartHost(new ConnectionMethodOdin(m_GatheringsFacade, m_ConnectionManager, GetPlayerId(), playerName));
        }

        public override void StartClient(ConnectionMethodBase connectionMethod)
        {
            m_ConnectionManager.m_ClientReconnecting.Configure(connectionMethod);
            m_ConnectionManager.ChangeState(m_ConnectionManager.m_ClientConnecting.Configure(connectionMethod));
        }

        public override void StartHost(ConnectionMethodBase connectionMethod)
        {
            m_ConnectionManager.ChangeState(m_ConnectionManager.m_StartingHost.Configure(connectionMethod));
        }

        /// The player id identifies a player across reconnects, so SessionManager can restore their character.
        /// Without a sign-in (e.g. in tests) it falls back to a per-install GUID plus the local profile.
        string GetPlayerId()
        {
            return m_PlayerAuthFacade.IsSignedIn ? m_PlayerAuthFacade.PlayerId : ClientPrefs.GetGuid() + m_ProfileManager.Profile;
        }
    }
}
