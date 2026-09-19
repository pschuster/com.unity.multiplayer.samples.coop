using System;
using System.Collections;
using NUnit.Framework;
using OdinNative.Netcode;
using OdinNative.Unity;
using OdinNative.Wrapper;
using Unity.Netcode;
using Unity.Netcode.TestHelpers.Runtime;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Unity.BossRoom.Tests.Runtime
{
    /// <summary>
    /// Runs a host and a client NetworkManager in one process, connected through the ODIN transport and the public
    /// ODIN gateway. This covers the connection path Boss Room uses, without the scenes and UI.
    /// </summary>
    public class OdinTransportIntegrationTests
    {
        const float k_Timeout = 30f;

        GameObject m_ServerObject;
        GameObject m_ClientObject;
        GameObject m_TestPrefab;
        NetworkManager m_Server;
        NetworkManager m_Client;

        [SetUp]
        public void SetUp()
        {
            var accessKey = OdinClient.CreateAccessKey();
            var roomName = "bossroom-test-" + Guid.NewGuid().ToString("N");

            m_TestPrefab = new GameObject("TestNetworkObject");
            var networkObject = m_TestPrefab.AddComponent<NetworkObject>();
            NetcodeIntegrationTestHelpers.MakeNetworkObjectTestPrefab(networkObject);

            m_Server = CreateNetworkManager("Server", roomName, accessKey, "host", out m_ServerObject);
            m_Client = CreateNetworkManager("Client", roomName, accessKey, "client", out m_ClientObject);
        }

        [TearDown]
        public void TearDown()
        {
            m_Client.Shutdown();
            m_Server.Shutdown();
            Object.DestroyImmediate(m_ClientObject);
            Object.DestroyImmediate(m_ServerObject);
            Object.DestroyImmediate(m_TestPrefab);
        }

        NetworkManager CreateNetworkManager(string name, string roomName, string accessKey, string userId, out GameObject networkManagerObject)
        {
            networkManagerObject = new GameObject($"NetworkManager - {name}");
            networkManagerObject.SetActive(false);

            var networkManager = networkManagerObject.AddComponent<NetworkManager>();
            var transport = networkManagerObject.AddComponent<OdinNetcodeTransport>();
            transport.RoomName = roomName;
            transport.DisplayName = name;
            transport.VerboseLogging = true;
            // a token per peer, as the backend would hand out
            transport.Token = OdinRoom.GenerateTestToken(roomName, userId, 60, accessKey);

            networkManager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                ConnectionApproval = false,
                EnableSceneManagement = false,
                PlayerPrefab = null,
            };
            networkManager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = m_TestPrefab });

            networkManagerObject.SetActive(true);
            return networkManager;
        }

        static IEnumerator WaitFor(Func<bool> condition, string description)
        {
            var deadline = Time.realtimeSinceStartup + k_Timeout;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Assert.Fail($"Timed out waiting for {description}");
                }

                yield return null;
            }
        }

        IEnumerator Connect()
        {
            Assert.IsTrue(m_Server.StartHost(), "StartHost failed");
            yield return WaitFor(() => ((OdinNetcodeTransport)m_Server.NetworkConfig.NetworkTransport).LocalPeerId != 0, "the host to join the ODIN room");

            Assert.IsTrue(m_Client.StartClient(), "StartClient failed");
            yield return WaitFor(() => m_Client.IsConnectedClient && m_Server.ConnectedClientsIds.Count == 2, "the client to connect");
        }

        [UnityTest]
        public IEnumerator ClientConnectsToHostThroughOdin()
        {
            yield return Connect();

            var clientTransport = (OdinNetcodeTransport)m_Client.NetworkConfig.NetworkTransport;
            var serverTransport = (OdinNetcodeTransport)m_Server.NetworkConfig.NetworkTransport;
            Assert.AreEqual(serverTransport.LocalPeerId, clientTransport.HostPeerId, "the client must find the host peer");
            Assert.AreEqual(serverTransport.RoomName, clientTransport.RoomName);
        }

        [UnityTest]
        public IEnumerator SpawnedObjectsReplicateToClient()
        {
            yield return Connect();

            // NetworkObject.Spawn uses NetworkManager.Singleton, which is the first manager created (the host)
            Assert.AreSame(m_Server, NetworkManager.Singleton, "the host must own the spawn");

            var instance = Object.Instantiate(m_TestPrefab);
            var networkObject = instance.GetComponent<NetworkObject>();
            networkObject.Spawn();

            yield return WaitFor(() => m_Client.SpawnManager.SpawnedObjects.ContainsKey(networkObject.NetworkObjectId), "the spawned object to replicate");

            networkObject.Despawn();
            yield return WaitFor(() => !m_Client.SpawnManager.SpawnedObjects.ContainsKey(networkObject.NetworkObjectId), "the despawn to replicate");
        }

        [UnityTest]
        public IEnumerator HostCanDisconnectClient()
        {
            yield return Connect();

            var disconnected = false;
            m_Client.OnClientDisconnectCallback += _ => disconnected = true;

            m_Server.DisconnectClient(m_Client.LocalClientId);
            yield return WaitFor(() => disconnected && !m_Client.IsConnectedClient, "the client to be disconnected by the host");
            Assert.AreEqual(1, m_Server.ConnectedClientsIds.Count, "only the host must remain connected");
        }

        [UnityTest]
        public IEnumerator ClientIsDisconnectedWhenHostShutsDown()
        {
            yield return Connect();

            m_Server.Shutdown();
            yield return WaitFor(() => !m_Client.IsConnectedClient, "the client to notice the host shutdown");
        }
    }
}
