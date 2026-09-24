using System;
using System.Threading.Tasks;
using OdinNative.Unity;

namespace Unity.BossRoom.OdinServices.Backend
{
    /// <summary>
    /// Development fallback without a server: a lobby is only its join code, which doubles as the ODIN room name,
    /// and room tokens are generated locally from <see cref="OdinSampleConfig.DevelopmentAccessKey"/>.
    /// </summary>
    public class LocalLobbyBackend : ILobbyBackend
    {
        const string k_CodeCharacters = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        readonly OdinSampleConfig m_Config;
        readonly Random m_Random = new Random();
        PlayerLogin m_Login;

        public LocalLobbyBackend(OdinSampleConfig config)
        {
            m_Config = config;
        }

        public bool SupportsLobbyList => false;
        public bool SupportsTranscription => false;
        public bool IsLoggedIn => m_Login != null;

        public Task<PlayerLogin> LoginAsync(string deviceId, string profile, string displayName)
        {
            m_Login = new PlayerLogin { playerId = $"{deviceId}{profile}", displayName = displayName, playerToken = string.Empty, expiresAt = long.MaxValue };
            return Task.FromResult(m_Login);
        }

        public Task<LobbyInfo[]> ListLobbiesAsync() => Task.FromResult(Array.Empty<LobbyInfo>());

        public Task<LobbyInfo> CreateLobbyAsync(string name, bool isPrivate, int maxPlayers)
        {
            var code = new char[8];
            for (var i = 0; i < code.Length; i++)
            {
                code[i] = k_CodeCharacters[m_Random.Next(k_CodeCharacters.Length)];
            }

            return Task.FromResult(CreateLobbyInfo(new string(code), name, isPrivate, maxPlayers, isOwner: true));
        }

        public Task<LobbyInfo> JoinLobbyByCodeAsync(string joinCode)
        {
            var code = joinCode.Replace("-", string.Empty).ToUpperInvariant();
            if (code.Length != 8)
            {
                throw new BackendException(400, "invalid_code", "Join code must have 8 characters");
            }

            return Task.FromResult(CreateLobbyInfo(code, code, false, 8, isOwner: false));
        }

        public Task<LobbyInfo> JoinLobbyByIdAsync(string lobbyId) => JoinLobbyByCodeAsync(lobbyId);

        public Task<LobbyInfo> QuickJoinLobbyAsync() =>
            throw new BackendException(404, "no_lobby", "Quick join needs the ODIN Cortex backend");

        public Task<LobbyInfo> GetLobbyAsync(string lobbyId) => JoinLobbyByCodeAsync(lobbyId);

        public Task LeaveLobbyAsync(string lobbyId) => Task.CompletedTask;

        public Task RemovePlayerAsync(string lobbyId, string playerId) => Task.CompletedTask;

        public Task<RoomToken> GetRoomTokenAsync(string lobbyId)
        {
            var roomId = RoomIdForCode(lobbyId);
            var token = OdinRoom.GenerateTestToken(roomId, m_Login?.playerId ?? Guid.NewGuid().ToString("N"), 60, m_Config.DevelopmentAccessKey);
            return Task.FromResult(new RoomToken { roomId = roomId, token = token });
        }

        public Task<LobbyInfo> StartGameAsync(string lobbyId) => GetLobbyAsync(lobbyId);

        public Task<TranscriptMessage[]> GetTranscriptAsync(string lobbyId, string afterTimestamp) =>
            Task.FromResult(Array.Empty<TranscriptMessage>());

        static string RoomIdForCode(string code) => $"bossroom-local-{code}";

        LobbyInfo CreateLobbyInfo(string code, string name, bool isPrivate, int maxPlayers, bool isOwner) => new LobbyInfo
        {
            // the code is the id, so every call derives the same lobby and room
            id = code,
            name = name,
            joinCode = $"{code.Substring(0, 4)}-{code.Substring(4)}",
            isPrivate = isPrivate,
            status = "active",
            maxMembers = maxPlayers,
            memberCount = 1,
            ownerId = isOwner ? m_Login?.playerId : null,
            roomId = RoomIdForCode(code),
            hostName = isOwner ? m_Login?.displayName : string.Empty,
            members = Array.Empty<LobbyMember>(),
        };
    }
}
