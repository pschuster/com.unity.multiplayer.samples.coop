using System;
using System.Threading.Tasks;
using Unity.BossRoom.Infrastructure;
using Unity.BossRoom.OdinServices.Backend;
using UnityEngine;
using VContainer;

namespace Unity.BossRoom.OdinServices.Sessions
{
    /// <summary>
    /// Lobbies backed by ODIN Cortex gatherings. A joined lobby provides the ODIN room name and token the transport
    /// connects with, so a lobby must be joined before the NetworkManager starts.
    /// </summary>
    public class GatheringsFacade : IDisposable
    {
        const float k_TrackingInterval = 3f;

        [Inject]
        UpdateRunner m_UpdateRunner;
        [Inject]
        LocalSession m_LocalSession;
        [Inject]
        LocalSessionUser m_LocalUser;
        [Inject]
        IPublisher<ServiceErrorMessage> m_ServiceErrorPublisher;
        [Inject]
        IPublisher<SessionListFetchedMessage> m_SessionListFetchedPublisher;
        [Inject]
        ILobbyBackend m_Backend;

        readonly RateLimitCooldown m_RateLimitQuery = new RateLimitCooldown(1f);
        readonly RateLimitCooldown m_RateLimitJoin = new RateLimitCooldown(1f);
        readonly RateLimitCooldown m_RateLimitHost = new RateLimitCooldown(3f);

        bool m_IsTracking;
        bool m_IsRefreshing;

        public LobbyInfo CurrentLobby { get; private set; }

        /// <summary>ODIN room of the current lobby</summary>
        public string CurrentRoomId { get; private set; }

        /// <summary>Token for <see cref="CurrentRoomId"/>, fetched when joining and before every reconnect</summary>
        public string CurrentRoomToken { get; private set; }

        public bool SupportsLobbyList => m_Backend.SupportsLobbyList;

        public bool SupportsTranscription => m_Backend.SupportsTranscription;

        public void Dispose()
        {
            EndTracking();
        }

        public Task<(bool Success, LobbyInfo Lobby)> TryCreateLobbyAsync(string lobbyName, int maxPlayers, bool isPrivate) =>
            TryEnterLobbyAsync(m_RateLimitHost, "Create lobby", () => m_Backend.CreateLobbyAsync(lobbyName, isPrivate, maxPlayers));

        public Task<(bool Success, LobbyInfo Lobby)> TryJoinLobbyByCodeAsync(string joinCode)
        {
            if (string.IsNullOrEmpty(joinCode))
            {
                Debug.LogWarning("Cannot join a lobby without a join code.");
                return Task.FromResult<(bool, LobbyInfo)>((false, null));
            }

            return TryEnterLobbyAsync(m_RateLimitJoin, "Join lobby", () => m_Backend.JoinLobbyByCodeAsync(joinCode));
        }

        public Task<(bool Success, LobbyInfo Lobby)> TryJoinLobbyByIdAsync(string lobbyId) =>
            TryEnterLobbyAsync(m_RateLimitJoin, "Join lobby", () => m_Backend.JoinLobbyByIdAsync(lobbyId));

        /// <summary>
        /// Joins the newest open lobby. Returns <c>NoLobbyFound</c> instead of an error if there is none.
        /// </summary>
        public async Task<(bool Success, bool NoLobbyFound, LobbyInfo Lobby)> TryQuickJoinLobbyAsync()
        {
            if (!m_Backend.SupportsLobbyList)
            {
                return (false, true, null);
            }

            try
            {
                var result = await TryEnterLobbyAsync(m_RateLimitJoin, "Quick join", m_Backend.QuickJoinLobbyAsync, publishNotFound: false);
                return (result.Success, false, result.Lobby);
            }
            catch (BackendException e) when (e.IsNotFound)
            {
                return (false, true, null);
            }
        }

        async Task<(bool Success, LobbyInfo Lobby)> TryEnterLobbyAsync(RateLimitCooldown rateLimit, string action, Func<Task<LobbyInfo>> enter, bool publishNotFound = true)
        {
            if (!rateLimit.CanCall)
            {
                Debug.LogWarning($"{action} hit the rate limit.");
                return (false, null);
            }

            rateLimit.PutOnCooldown();

            LobbyInfo lobby = null;
            try
            {
                lobby = await enter();
                var token = await m_Backend.GetRoomTokenAsync(lobby.id);
                SetCurrentLobby(lobby, token);
                Debug.Log($"Entered lobby {lobby.name} ({lobby.joinCode}), ODIN room {CurrentRoomId}");
                return (true, lobby);
            }
            catch (BackendException e) when (!publishNotFound && e.IsNotFound && lobby == null)
            {
                throw;
            }
            catch (Exception e)
            {
                PublishError(action, e);
                if (lobby != null)
                {
                    // we are a member but cannot connect, do not keep the slot
                    _ = LeaveLobbyAsync(lobby.id);
                }

                return (false, null);
            }
        }

        void SetCurrentLobby(LobbyInfo lobby, RoomToken token)
        {
            CurrentLobby = lobby;
            CurrentRoomId = token.roomId;
            CurrentRoomToken = token.token;
            m_LocalUser.IsHost = !string.IsNullOrEmpty(lobby.ownerId) ? lobby.ownerId == m_LocalUser.ID : m_LocalUser.IsHost;
            m_LocalSession.ApplyRemoteData(lobby, m_LocalUser);
        }

        /// <summary>
        /// Fetches a fresh room token for the current lobby, e.g. before reconnecting.
        /// </summary>
        /// <returns>shouldTryAgain is false if the lobby is gone or we are no longer a member</returns>
        public async Task<(bool Success, bool ShouldTryAgain)> RefreshRoomTokenAsync()
        {
            if (CurrentLobby == null)
            {
                return (false, false);
            }

            try
            {
                var token = await m_Backend.GetRoomTokenAsync(CurrentLobby.id);
                CurrentRoomId = token.roomId;
                CurrentRoomToken = token.token;
                return (true, true);
            }
            catch (BackendException e) when (!e.IsNetworkError && e.StatusCode < 500)
            {
                Debug.Log($"Lobby is no longer available: {e.Message}");
                ResetLobby();
                return (false, false);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Failed to refresh the room token: {e.Message}");
                return (false, true);
            }
        }

        /// <summary>
        /// Starts polling the current lobby for membership changes.
        /// </summary>
        public void BeginTracking()
        {
            if (m_IsTracking || CurrentLobby == null)
            {
                return;
            }

            m_IsTracking = true;
            m_UpdateRunner.Subscribe(RefreshLobby, k_TrackingInterval);
        }

        /// <summary>
        /// Stops tracking and leaves the current lobby. The host's lobby ends with it.
        /// </summary>
        public void EndTracking()
        {
            if (m_IsTracking)
            {
                m_IsTracking = false;
                m_UpdateRunner.Unsubscribe(RefreshLobby);
            }

            if (CurrentLobby != null)
            {
                var lobbyId = CurrentLobby.id;
                ResetLobby();
                _ = LeaveLobbyAsync(lobbyId);
            }
        }

        async void RefreshLobby(float _)
        {
            if (m_IsRefreshing || CurrentLobby == null)
            {
                return;
            }

            m_IsRefreshing = true;
            try
            {
                var lobby = await m_Backend.GetLobbyAsync(CurrentLobby.id);
                if (CurrentLobby != null && lobby.id == CurrentLobby.id)
                {
                    CurrentLobby = lobby;
                    m_LocalSession.ApplyRemoteData(lobby, m_LocalUser);
                }
            }
            catch (BackendException e) when (e.IsForbidden || e.IsNotFound)
            {
                // removed from the lobby or it ended; the transport reports the actual disconnect
                Debug.Log($"No longer in lobby: {e.Message}");
                if (m_IsTracking)
                {
                    m_IsTracking = false;
                    m_UpdateRunner.Unsubscribe(RefreshLobby);
                }

                ResetLobby();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Failed to refresh lobby: {e.Message}");
            }
            finally
            {
                m_IsRefreshing = false;
            }
        }

        public async Task RetrieveAndPublishLobbyListAsync()
        {
            if (!m_RateLimitQuery.CanCall)
            {
                Debug.LogWarning("Retrieving the lobby list hit the rate limit. Will try again soon...");
                return;
            }

            m_RateLimitQuery.PutOnCooldown();

            try
            {
                var lobbies = await m_Backend.ListLobbiesAsync();
                m_SessionListFetchedPublisher.Publish(new SessionListFetchedMessage(lobbies));
            }
            catch (Exception e)
            {
                PublishError("Lobby list", e);
            }
        }

        public async void RemovePlayerFromLobbyAsync(string playerId)
        {
            if (CurrentLobby == null)
            {
                return;
            }

            if (!m_LocalUser.IsHost)
            {
                Debug.LogError("Only the host can remove other players from the lobby.");
                return;
            }

            try
            {
                await m_Backend.RemovePlayerAsync(CurrentLobby.id, playerId);
            }
            catch (Exception e)
            {
                PublishError("Remove player", e);
            }
        }

        /// <summary>
        /// Marks the lobby as started. With transcription enabled on the backend, a Cortex bot joins the voice room.
        /// </summary>
        public async Task StartGameAsync()
        {
            if (CurrentLobby == null || !m_LocalUser.IsHost)
            {
                return;
            }

            try
            {
                var lobby = await m_Backend.StartGameAsync(CurrentLobby.id);
                if (CurrentLobby != null && lobby.id == CurrentLobby.id)
                {
                    CurrentLobby = lobby;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Failed to start the lobby: {e.Message}");
            }
        }

        public async Task<TranscriptMessage[]> GetTranscriptAsync(string afterTimestamp)
        {
            if (CurrentLobby == null || !SupportsTranscription)
            {
                return Array.Empty<TranscriptMessage>();
            }

            return await m_Backend.GetTranscriptAsync(CurrentLobby.id, afterTimestamp);
        }

        async Task LeaveLobbyAsync(string lobbyId)
        {
            try
            {
                await m_Backend.LeaveLobbyAsync(lobbyId);
            }
            catch (BackendException e) when (e.IsNotFound || e.IsForbidden)
            {
                // already gone
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Failed to leave lobby: {e.Message}");
            }
        }

        void ResetLobby()
        {
            CurrentLobby = null;
            CurrentRoomId = null;
            CurrentRoomToken = null;
            m_LocalUser?.ResetState();
            m_LocalSession?.Reset(m_LocalUser);
        }

        void PublishError(string action, Exception e)
        {
            var message = e is BackendException { IsNetworkError: true }
                ? "Could not reach the lobby service. Check your connection."
                : e.Message;
            m_ServiceErrorPublisher.Publish(new ServiceErrorMessage($"{action} failed", message, ServiceErrorMessage.Service.Lobby, e));
        }
    }
}
