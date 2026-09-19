using System.Threading.Tasks;
using Unity.BossRoom.Gameplay.Configuration;
using TMPro;
using Unity.BossRoom.ConnectionManagement;
using Unity.BossRoom.Infrastructure;
using Unity.BossRoom.OdinServices.Auth;
using Unity.BossRoom.OdinServices.Backend;
using Unity.BossRoom.OdinServices.Sessions;
using UnityEngine;
using VContainer;

namespace Unity.BossRoom.Gameplay.UI
{
    public class SessionUIMediator : MonoBehaviour
    {
        [SerializeField]
        CanvasGroup m_CanvasGroup;
        [SerializeField]
        SessionJoiningUI m_SessionJoiningUI;
        [SerializeField]
        SessionCreationUI m_SessionCreationUI;
        [SerializeField]
        UITinter m_JoinToggleHighlight;
        [SerializeField]
        UITinter m_JoinToggleTabBlocker;
        [SerializeField]
        UITinter m_CreateToggleHighlight;
        [SerializeField]
        UITinter m_CreateToggleTabBlocker;
        [SerializeField]
        TextMeshProUGUI m_PlayerNameLabel;
        [SerializeField]
        GameObject m_LoadingSpinner;

        PlayerAuthFacade m_PlayerAuthFacade;
        GatheringsFacade m_GatheringsFacade;
        LocalSessionUser m_LocalUser;
        LocalSession m_LocalSession;
        NameGenerationData m_NameGenerationData;
        ConnectionManager m_ConnectionManager;
        ISubscriber<ConnectStatus> m_ConnectStatusSubscriber;

        const string k_DefaultSessionName = "no-name";
        const int k_MaxPlayers = 8;

        [Inject]
        void InjectDependenciesAndInitialize(
            PlayerAuthFacade playerAuthFacade,
            GatheringsFacade gatheringsFacade,
            LocalSessionUser localUser,
            LocalSession localSession,
            NameGenerationData nameGenerationData,
            ISubscriber<ConnectStatus> connectStatusSub,
            ConnectionManager connectionManager
        )
        {
            m_PlayerAuthFacade = playerAuthFacade;
            m_NameGenerationData = nameGenerationData;
            m_LocalUser = localUser;
            m_GatheringsFacade = gatheringsFacade;
            m_LocalSession = localSession;
            m_ConnectionManager = connectionManager;
            m_ConnectStatusSubscriber = connectStatusSub;
            RegenerateName();

            m_ConnectStatusSubscriber.Subscribe(OnConnectStatus);
        }

        void OnConnectStatus(ConnectStatus status)
        {
            if (status is ConnectStatus.GenericDisconnect or ConnectStatus.StartClientFailed or ConnectStatus.StartHostFailed)
            {
                UnblockUIAfterLoadingIsComplete();
            }
        }

        void OnDestroy()
        {
            m_ConnectStatusSubscriber?.Unsubscribe(OnConnectStatus);
        }

        // Lobby requests done from UI. A lobby is joined first; its ODIN room token is what the connection uses.
        public async void CreateSessionRequest(string sessionName, bool isPrivate)
        {
            // before sending request, populate an empty session name, if necessary
            if (string.IsNullOrEmpty(sessionName))
            {
                sessionName = k_DefaultSessionName;
            }

            BlockUIWhileLoadingIsInProgress();

            if (!await EnsureSignedIn())
            {
                return;
            }

            var result = await m_GatheringsFacade.TryCreateLobbyAsync(sessionName, k_MaxPlayers, isPrivate);
            HandleLobbyResult(result.Success, host: true);
        }

        public async void QuerySessionRequest(bool blockUI)
        {
            if (!m_GatheringsFacade.SupportsLobbyList || !m_PlayerAuthFacade.IsSignedIn)
            {
                return;
            }

            if (blockUI)
            {
                BlockUIWhileLoadingIsInProgress();
            }

            await m_GatheringsFacade.RetrieveAndPublishLobbyListAsync();

            if (blockUI)
            {
                UnblockUIAfterLoadingIsComplete();
            }
        }

        public async void JoinSessionWithCodeRequest(string sessionCode)
        {
            BlockUIWhileLoadingIsInProgress();

            if (!await EnsureSignedIn())
            {
                return;
            }

            var result = await m_GatheringsFacade.TryJoinLobbyByCodeAsync(sessionCode);
            HandleLobbyResult(result.Success, host: false);
        }

        public async void JoinSessionRequest(LobbyInfo lobby)
        {
            BlockUIWhileLoadingIsInProgress();

            if (!await EnsureSignedIn())
            {
                return;
            }

            var result = await m_GatheringsFacade.TryJoinLobbyByIdAsync(lobby.id);
            HandleLobbyResult(result.Success, host: false);
        }

        public async void QuickJoinRequest()
        {
            BlockUIWhileLoadingIsInProgress();

            if (!await EnsureSignedIn())
            {
                return;
            }

            var result = await m_GatheringsFacade.TryQuickJoinLobbyAsync();
            if (result.NoLobbyFound)
            {
                // like matchmaking: nobody to join, so open a lobby for others
                var created = await m_GatheringsFacade.TryCreateLobbyAsync($"{m_LocalUser.DisplayName}'s game", k_MaxPlayers, false);
                HandleLobbyResult(created.Success, host: true);
                return;
            }

            HandleLobbyResult(result.Success, host: false);
        }

        async Task<bool> EnsureSignedIn()
        {
            if (await m_PlayerAuthFacade.EnsurePlayerIsAuthorized(m_LocalUser.DisplayName))
            {
                m_LocalUser.ID = m_PlayerAuthFacade.PlayerId;
                return true;
            }

            UnblockUIAfterLoadingIsComplete();
            return false;
        }

        void HandleLobbyResult(bool success, bool host)
        {
            if (!success)
            {
                UnblockUIAfterLoadingIsComplete();
                return;
            }

            Debug.Log($"Joined lobby with ID: {m_LocalSession.SessionID}");

            if (host)
            {
                m_ConnectionManager.StartHostSession(m_LocalUser.DisplayName);
            }
            else
            {
                m_ConnectionManager.StartClientSession(m_LocalUser.DisplayName);
            }
        }

        //show/hide UI

        public void Show()
        {
            m_CanvasGroup.alpha = 1f;
            m_CanvasGroup.blocksRaycasts = true;
        }

        public void Hide()
        {
            m_CanvasGroup.alpha = 0f;
            m_CanvasGroup.blocksRaycasts = false;
            m_SessionCreationUI.Hide();
            m_SessionJoiningUI.Hide();
        }

        public void ToggleJoinSessionUI()
        {
            m_SessionJoiningUI.Show();
            m_SessionCreationUI.Hide();
            m_JoinToggleHighlight.SetToColor(1);
            m_JoinToggleTabBlocker.SetToColor(1);
            m_CreateToggleHighlight.SetToColor(0);
            m_CreateToggleTabBlocker.SetToColor(0);
        }

        public void ToggleCreateSessionUI()
        {
            m_SessionJoiningUI.Hide();
            m_SessionCreationUI.Show();
            m_JoinToggleHighlight.SetToColor(0);
            m_JoinToggleTabBlocker.SetToColor(0);
            m_CreateToggleHighlight.SetToColor(1);
            m_CreateToggleTabBlocker.SetToColor(1);
        }

        public void RegenerateName()
        {
            m_LocalUser.DisplayName = m_NameGenerationData.GenerateName();
            m_PlayerNameLabel.text = m_LocalUser.DisplayName;
        }

        void BlockUIWhileLoadingIsInProgress()
        {
            m_CanvasGroup.interactable = false;
            m_LoadingSpinner.SetActive(true);
        }

        void UnblockUIAfterLoadingIsComplete()
        {
            // this callback can happen after we've already switched to a different scene
            // in that case the canvas group would be null
            if (m_CanvasGroup != null)
            {
                m_CanvasGroup.interactable = true;
                m_LoadingSpinner.SetActive(false);
            }
        }
    }
}
