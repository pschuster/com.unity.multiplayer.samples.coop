using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Unity.BossRoom.OdinServices.Backend
{
    /// <summary>
    /// Talks to the Boss Room backend function hosted on ODIN Cortex (see Backend/cortex-function).
    /// </summary>
    public class CortexLobbyBackend : ILobbyBackend
    {
        readonly OdinSampleConfig m_Config;
        string m_PlayerToken;
        long m_TokenExpiresAt;

        public CortexLobbyBackend(OdinSampleConfig config)
        {
            m_Config = config;
        }

        public bool SupportsLobbyList => true;
        public bool SupportsTranscription => true;
        public bool IsLoggedIn => !string.IsNullOrEmpty(m_PlayerToken) && DateTimeOffset.UtcNow.ToUnixTimeSeconds() < m_TokenExpiresAt - 60;

        [Serializable]
        class LoginRequest
        {
            public string deviceId;
            public string profile;
            public string displayName;
        }

        [Serializable]
        class CreateLobbyRequest
        {
            public string name;
            public bool isPrivate;
            public int maxMembers;
        }

        [Serializable]
        class KickRequest
        {
            public string playerId;
        }

        public async Task<PlayerLogin> LoginAsync(string deviceId, string profile, string displayName)
        {
            var login = await SendAsync<PlayerLogin>("POST", "/login", new LoginRequest { deviceId = deviceId, profile = profile, displayName = displayName }, authenticated: false);
            m_PlayerToken = login.playerToken;
            m_TokenExpiresAt = login.expiresAt;
            return login;
        }

        public async Task<LobbyInfo[]> ListLobbiesAsync()
        {
            var list = await SendAsync<LobbyList>("GET", "/lobbies");
            return list.lobbies ?? Array.Empty<LobbyInfo>();
        }

        public Task<LobbyInfo> CreateLobbyAsync(string name, bool isPrivate, int maxPlayers) =>
            SendAsync<LobbyInfo>("POST", "/lobbies", new CreateLobbyRequest { name = name, isPrivate = isPrivate, maxMembers = maxPlayers });

        public Task<LobbyInfo> JoinLobbyByCodeAsync(string joinCode) =>
            SendAsync<LobbyInfo>("POST", $"/lobbies/code/{UnityWebRequest.EscapeURL(joinCode)}/join");

        public Task<LobbyInfo> JoinLobbyByIdAsync(string lobbyId) =>
            SendAsync<LobbyInfo>("POST", $"/lobbies/{lobbyId}/join");

        public Task<LobbyInfo> QuickJoinLobbyAsync() =>
            SendAsync<LobbyInfo>("POST", "/lobbies/quickjoin");

        public Task<LobbyInfo> GetLobbyAsync(string lobbyId) =>
            SendAsync<LobbyInfo>("GET", $"/lobbies/{lobbyId}");

        public Task LeaveLobbyAsync(string lobbyId) =>
            SendAsync<BackendError>("POST", $"/lobbies/{lobbyId}/leave");

        public Task RemovePlayerAsync(string lobbyId, string playerId) =>
            SendAsync<BackendError>("POST", $"/lobbies/{lobbyId}/kick", new KickRequest { playerId = playerId });

        public Task<RoomToken> GetRoomTokenAsync(string lobbyId) =>
            SendAsync<RoomToken>("POST", $"/lobbies/{lobbyId}/token");

        public Task<LobbyInfo> StartGameAsync(string lobbyId) =>
            SendAsync<LobbyInfo>("POST", $"/lobbies/{lobbyId}/start");

        public async Task<TranscriptMessage[]> GetTranscriptAsync(string lobbyId, string afterTimestamp)
        {
            var path = string.IsNullOrEmpty(afterTimestamp)
                ? $"/lobbies/{lobbyId}/transcript"
                : $"/lobbies/{lobbyId}/transcript/{UnityWebRequest.EscapeURL(afterTimestamp)}";
            var transcript = await SendAsync<Transcript>("GET", path);
            return transcript.messages ?? Array.Empty<TranscriptMessage>();
        }

        async Task<T> SendAsync<T>(string method, string path, object body = null, bool authenticated = true)
        {
            if (!m_Config.UsesCortexBackend)
            {
                throw new BackendException(0, "not_configured", "No backend URL configured");
            }

            using var request = new UnityWebRequest(m_Config.BackendUrl + path, method);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = m_Config.RequestTimeoutSeconds;
            // The Cortex invoke proxy does not forward parsed JSON bodies, so JSON is sent as plain text.
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body != null ? JsonUtility.ToJson(body) : "{}"));
            request.SetRequestHeader("Content-Type", "text/plain");
            if (authenticated)
            {
                if (string.IsNullOrEmpty(m_PlayerToken))
                {
                    throw new BackendException(401, "unauthorized", "Not signed in");
                }

                request.SetRequestHeader("X-Player-Token", m_PlayerToken);
            }

            var completion = new TaskCompletionSource<bool>();
            request.SendWebRequest().completed += _ => completion.TrySetResult(true);
            await completion.Task;

            var text = request.downloadHandler.text;
            if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.DataProcessingError)
            {
                throw new BackendException(0, "network_error", request.error);
            }

            if (request.responseCode < 200 || request.responseCode >= 300)
            {
                var error = TryParse<BackendError>(text);
                throw new BackendException(request.responseCode, error?.error ?? "http_error", string.IsNullOrEmpty(error?.message) ? request.error : error.message);
            }

            return TryParse<T>(text) ?? throw new BackendException(request.responseCode, "invalid_response", $"Unexpected response from {path}");
        }

        static T TryParse<T>(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return default;
            }

            try
            {
                return JsonUtility.FromJson<T>(json);
            }
            catch (ArgumentException)
            {
                return default;
            }
        }
    }
}
