using System;
using Unity.BossRoom.Infrastructure;
using Unity.Netcode;
using UnityEngine;
using VContainer;

namespace Unity.BossRoom.ConnectionManagement
{
    /// <summary>
    /// Base class representing a connection state.
    /// </summary>
    abstract class ConnectionState
    {
        [Inject]
        protected ConnectionManager m_ConnectionManager;

        [Inject]
        protected IPublisher<ConnectStatus> m_ConnectStatusPublisher;

        public abstract void Enter();

        public abstract void Exit();

        public virtual void OnClientConnected(ulong clientId) { }
        public virtual void OnClientDisconnect(ulong clientId) { }

        public virtual void OnServerStarted() { }

        public virtual void StartClientSession(string playerName) { }

        public virtual void StartHostSession(string playerName) { }

        public virtual void StartClient(ConnectionMethodBase connectionMethod) { }

        public virtual void StartHost(ConnectionMethodBase connectionMethod) { }

        public virtual void OnUserRequestedShutdown() { }

        public virtual void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response) { }

        public virtual void OnTransportFailure() { }

        public virtual void OnServerStopped() { }
    }
}
