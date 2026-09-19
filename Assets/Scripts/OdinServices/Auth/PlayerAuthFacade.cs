using System;
using System.Threading.Tasks;
using Unity.BossRoom.Infrastructure;
using Unity.BossRoom.OdinServices.Backend;
using UnityEngine;
using VContainer;

namespace Unity.BossRoom.OdinServices.Auth
{
    /// <summary>
    /// Signs the player in to the lobby backend. Replaces anonymous Unity Authentication: the identity is the device
    /// id plus the local profile, so several editor instances on one machine get different players.
    /// </summary>
    public class PlayerAuthFacade
    {
        [Inject]
        IPublisher<ServiceErrorMessage> m_ServiceErrorPublisher;
        [Inject]
        ILobbyBackend m_Backend;

        string m_DeviceId;
        string m_Profile;
        string m_DisplayName;

        public bool IsSignedIn => m_Backend.IsLoggedIn && !string.IsNullOrEmpty(PlayerId);
        public string PlayerId { get; private set; }

        public async Task<bool> SignInAsync(string deviceId, string profile, string displayName)
        {
            m_DeviceId = deviceId;
            m_Profile = profile;
            m_DisplayName = displayName;

            try
            {
                var login = await m_Backend.LoginAsync(deviceId, profile, displayName);
                PlayerId = login.playerId;
                Debug.Log($"Signed in. Player ID {PlayerId}");
                return true;
            }
            catch (Exception e)
            {
                PlayerId = null;
                m_ServiceErrorPublisher.Publish(new ServiceErrorMessage("Sign-in failed", e.Message, ServiceErrorMessage.Service.Authentication, e));
                return false;
            }
        }

        /// <summary>
        /// Signs in again if the player token expired or the display name changed, so transcripts show the current name.
        /// </summary>
        public async Task<bool> EnsurePlayerIsAuthorized(string displayName)
        {
            if (string.IsNullOrEmpty(m_DeviceId))
            {
                Debug.LogError($"{nameof(SignInAsync)} must be called before {nameof(EnsurePlayerIsAuthorized)}");
                return false;
            }

            if (IsSignedIn && displayName == m_DisplayName)
            {
                return true;
            }

            return await SignInAsync(m_DeviceId, m_Profile, displayName);
        }
    }
}
