using System.Threading.Tasks;

namespace Unity.BossRoom.OdinServices.Backend
{
    /// <summary>
    /// Player login, lobbies and ODIN room tokens. Implemented by the ODIN Cortex function and by a local
    /// development fallback that needs no server.
    /// </summary>
    public interface ILobbyBackend
    {
        /// <summary>False if lobbies can only be joined by code</summary>
        bool SupportsLobbyList { get; }
        /// <summary>False if the backend has no transcription</summary>
        bool SupportsTranscription { get; }
        bool IsLoggedIn { get; }

        Task<PlayerLogin> LoginAsync(string deviceId, string profile, string displayName);
        Task<LobbyInfo[]> ListLobbiesAsync();
        Task<LobbyInfo> CreateLobbyAsync(string name, bool isPrivate, int maxPlayers);
        Task<LobbyInfo> JoinLobbyByCodeAsync(string joinCode);
        Task<LobbyInfo> JoinLobbyByIdAsync(string lobbyId);
        Task<LobbyInfo> QuickJoinLobbyAsync();
        Task<LobbyInfo> GetLobbyAsync(string lobbyId);
        Task LeaveLobbyAsync(string lobbyId);
        Task RemovePlayerAsync(string lobbyId, string playerId);
        Task<RoomToken> GetRoomTokenAsync(string lobbyId);
        Task<LobbyInfo> StartGameAsync(string lobbyId);
        Task<TranscriptMessage[]> GetTranscriptAsync(string lobbyId, string afterTimestamp);
    }
}
