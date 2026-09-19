using System.Threading.Tasks;
using OdinNative.Netcode;
using Unity.BossRoom.OdinServices.Sessions;
using UnityEngine;

namespace Unity.BossRoom.ConnectionManagement
{
    /// <summary>
    /// ConnectionMethod contains all setup needed to setup NGO to be ready to start a connection, either host or client
    /// side.
    /// Please override this abstract class to add a new transport or way of connecting.
    /// </summary>
    public abstract class ConnectionMethodBase
    {
        protected ConnectionManager m_ConnectionManager;
        protected readonly string m_PlayerId;
        protected readonly string m_PlayerName;

        /// <summary>
        /// Setup the host connection prior to starting the NetworkManager
        /// </summary>
        public abstract void SetupHostConnection();

        /// <summary>
        /// Setup the client connection prior to starting the NetworkManager
        /// </summary>
        public abstract void SetupClientConnection();

        /// <summary>
        /// Setup the client for reconnection prior to reconnecting
        /// </summary>
        /// <returns>
        /// success = true if succeeded in setting up reconnection, false if failed.
        /// shouldTryAgain = true if we should try again after failing, false if not.
        /// </returns>
        public abstract Task<(bool success, bool shouldTryAgain)> SetupClientReconnectionAsync();

        protected ConnectionMethodBase(ConnectionManager connectionManager, string playerId, string playerName)
        {
            m_ConnectionManager = connectionManager;
            m_PlayerId = playerId;
            m_PlayerName = playerName;
        }

        protected void SetConnectionPayload()
        {
            var payload = JsonUtility.ToJson(new ConnectionPayload
            {
                playerId = m_PlayerId,
                playerName = m_PlayerName,
                isDebug = Debug.isDebugBuild
            });

            var payloadBytes = System.Text.Encoding.UTF8.GetBytes(payload);

            m_ConnectionManager.NetworkManager.NetworkConfig.ConnectionData = payloadBytes;
        }
    }

    /// <summary>
    /// Connects through the ODIN room of the current lobby. The lobby provides room name and token.
    /// </summary>
    class ConnectionMethodOdin : ConnectionMethodBase
    {
        readonly GatheringsFacade m_GatheringsFacade;

        public ConnectionMethodOdin(GatheringsFacade gatheringsFacade, ConnectionManager connectionManager, string playerId, string playerName)
            : base(connectionManager, playerId, playerName)
        {
            m_GatheringsFacade = gatheringsFacade;
        }

        public override void SetupHostConnection()
        {
            SetConnectionPayload(); // Need to set connection payload for host as well, as host is a client too
            ConfigureTransport();
        }

        public override void SetupClientConnection()
        {
            SetConnectionPayload();
            ConfigureTransport();
        }

        public override async Task<(bool success, bool shouldTryAgain)> SetupClientReconnectionAsync()
        {
            // a token is single use per join, so every attempt needs a fresh one; this also tells us if the lobby still exists
            var (success, shouldTryAgain) = await m_GatheringsFacade.RefreshRoomTokenAsync();
            Debug.Log(success ? "Refreshed ODIN room token for reconnection." : "Could not refresh ODIN room token.");
            return (success, shouldTryAgain);
        }

        void ConfigureTransport()
        {
            if (string.IsNullOrEmpty(m_GatheringsFacade.CurrentRoomToken))
            {
                throw new System.InvalidOperationException("Join a lobby before starting the connection.");
            }

            if (m_ConnectionManager.NetworkManager.NetworkConfig.NetworkTransport is not OdinNetcodeTransport transport)
            {
                throw new System.InvalidOperationException($"The NetworkManager must use the {nameof(OdinNetcodeTransport)}.");
            }

            transport.RoomName = m_GatheringsFacade.CurrentRoomId;
            transport.Token = m_GatheringsFacade.CurrentRoomToken;
            transport.DisplayName = m_PlayerName;
        }
    }
}
