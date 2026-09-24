using System;
using System.Collections.Generic;
using Unity.BossRoom.OdinServices.Backend;
using UnityEngine;

namespace Unity.BossRoom.OdinServices.Sessions
{
    /// <summary>
    /// A local wrapper around a session's remote data, with additional functionality for providing that data to UI
    /// elements and tracking local player objects.
    /// </summary>
    [Serializable]
    public sealed class LocalSession
    {
        Dictionary<string, LocalSessionUser> m_SessionUsers = new();
        public Dictionary<string, LocalSessionUser> sessionUsers => m_SessionUsers;

        SessionData m_Data;

        public event Action<LocalSession> changed;

        public string SessionID
        {
            get => m_Data.SessionID;
            set
            {
                m_Data.SessionID = value;
                OnChanged();
            }
        }

        public string SessionCode
        {
            get => m_Data.SessionCode;
            set
            {
                m_Data.SessionCode = value;
                OnChanged();
            }
        }

        public string SessionName => m_Data.SessionName;

        public string RoomId => m_Data.RoomId;

        public struct SessionData
        {
            public string SessionID { get; set; }
            public string SessionCode { get; set; }
            public string RoomId { get; set; }
            public string SessionName { get; set; }
            public bool Private { get; set; }
            public int MaxPlayerCount { get; set; }

            public SessionData(SessionData existing)
            {
                SessionID = existing.SessionID;
                SessionCode = existing.SessionCode;
                RoomId = existing.RoomId;
                SessionName = existing.SessionName;
                Private = existing.Private;
                MaxPlayerCount = existing.MaxPlayerCount;
            }

            public SessionData(string sessionCode)
            {
                SessionID = null;
                SessionCode = sessionCode;
                RoomId = null;
                SessionName = null;
                Private = false;
                MaxPlayerCount = -1;
            }
        }

        public void AddUser(LocalSessionUser user)
        {
            if (!m_SessionUsers.ContainsKey(user.ID))
            {
                DoAddUser(user);
                OnChanged();
            }
        }

        void DoAddUser(LocalSessionUser user)
        {
            m_SessionUsers.Add(user.ID, user);
            user.changed += OnChangedUser;
        }

        public void RemoveUser(LocalSessionUser user)
        {
            DoRemoveUser(user);
            OnChanged();
        }

        void DoRemoveUser(LocalSessionUser user)
        {
            if (!m_SessionUsers.ContainsKey(user.ID))
            {
                Debug.LogWarning($"Player {user.DisplayName}({user.ID}) does not exist in session: {SessionID}");
                return;
            }

            m_SessionUsers.Remove(user.ID);
            user.changed -= OnChangedUser;
        }

        void OnChangedUser(LocalSessionUser user)
        {
            OnChanged();
        }

        void OnChanged()
        {
            changed?.Invoke(this);
        }

        public void CopyDataFrom(SessionData data, Dictionary<string, LocalSessionUser> currUsers)
        {
            m_Data = data;

            if (currUsers == null)
            {
                m_SessionUsers = new Dictionary<string, LocalSessionUser>();
            }
            else
            {
                List<LocalSessionUser> toRemove = new List<LocalSessionUser>();
                foreach (var oldUser in m_SessionUsers)
                {
                    if (currUsers.ContainsKey(oldUser.Key))
                    {
                        oldUser.Value.CopyDataFrom(currUsers[oldUser.Key]);
                    }
                    else
                    {
                        toRemove.Add(oldUser.Value);
                    }
                }

                foreach (var remove in toRemove)
                {
                    DoRemoveUser(remove);
                }

                foreach (var currUser in currUsers)
                {
                    if (!m_SessionUsers.ContainsKey(currUser.Key))
                    {
                        DoAddUser(currUser.Value);
                    }
                }
            }

            OnChanged();
        }

        public void ApplyRemoteData(LobbyInfo lobby, LocalSessionUser localUser)
        {
            var info = new SessionData
            {
                SessionID = lobby.id,
                SessionName = lobby.name,
                MaxPlayerCount = lobby.maxMembers,
                SessionCode = lobby.joinCode,
                Private = lobby.isPrivate,
                RoomId = lobby.roomId,
            };

            var localSessionUsers = new Dictionary<string, LocalSessionUser>();
            if (lobby.members != null)
            {
                foreach (var member in lobby.members)
                {
                    if (string.IsNullOrEmpty(member.playerId) || localSessionUsers.ContainsKey(member.playerId))
                    {
                        continue;
                    }

                    localSessionUsers.Add(member.playerId, new LocalSessionUser
                    {
                        IsHost = member.isOwner,
                        DisplayName = member.displayName,
                        ID = member.playerId
                    });
                }
            }

            // the local development backend has no member list, but the local user is always part of its own session
            if (localUser != null && !string.IsNullOrEmpty(localUser.ID) && !localSessionUsers.ContainsKey(localUser.ID))
            {
                localSessionUsers.Add(localUser.ID, new LocalSessionUser
                {
                    IsHost = lobby.ownerId == localUser.ID,
                    DisplayName = localUser.DisplayName,
                    ID = localUser.ID
                });
            }

            CopyDataFrom(info, localSessionUsers);
        }

        public void Reset(LocalSessionUser localUser)
        {
            CopyDataFrom(new SessionData(), new Dictionary<string, LocalSessionUser>());
            AddUser(localUser);
        }
    }
}
