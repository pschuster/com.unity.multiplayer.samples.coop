using System.Collections.Generic;
using Unity.BossRoom.OdinServices.Backend;

namespace Unity.BossRoom.OdinServices.Sessions
{
    public struct SessionListFetchedMessage
    {
        public readonly IReadOnlyList<LobbyInfo> LocalSessions;

        public SessionListFetchedMessage(IReadOnlyList<LobbyInfo> localSessions)
        {
            LocalSessions = localSessions;
        }
    }
}
