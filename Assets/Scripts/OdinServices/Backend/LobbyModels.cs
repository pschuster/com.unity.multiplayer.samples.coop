using System;

namespace Unity.BossRoom.OdinServices.Backend
{
    // Field names match the JSON of the Cortex function so JsonUtility can map them.

    [Serializable]
    public class PlayerLogin
    {
        public string playerId;
        public string displayName;
        public string playerToken;
        public long expiresAt;
    }

    [Serializable]
    public class LobbyMember
    {
        public string playerId;
        public string displayName;
        public bool isOwner;
    }

    [Serializable]
    public class LobbyInfo
    {
        public string id;
        public string name;
        public string joinCode;
        public bool isPrivate;
        public string status;
        public int maxMembers;
        public int memberCount;
        public string ownerId;
        public string roomId;
        public string sessionId;
        public string createdAt;
        public string hostName;
        public LobbyMember[] members;
    }

    [Serializable]
    public class LobbyList
    {
        public LobbyInfo[] lobbies;
    }

    [Serializable]
    public class RoomToken
    {
        public string roomId;
        public string token;
    }

    [Serializable]
    public class TranscriptMessage
    {
        public string id;
        public string senderName;
        public string content;
        public string timestamp;
        public bool flagged;
        public string[] categories;
    }

    [Serializable]
    public class Transcript
    {
        public TranscriptMessage[] messages;
    }

    [Serializable]
    class BackendError
    {
        public string error;
        public string message;
    }

    /// <summary>
    /// A failed backend call. <see cref="StatusCode"/> is 0 for network failures.
    /// </summary>
    public class BackendException : Exception
    {
        public long StatusCode { get; }
        public string ErrorCode { get; }

        public bool IsNetworkError => StatusCode == 0;
        public bool IsNotFound => StatusCode == 404;
        public bool IsForbidden => StatusCode == 403;
        public bool IsUnauthorized => StatusCode == 401;
        /// <summary>The Cortex join gate refused a voice token because the player is banned; the message says until when.</summary>
        public bool IsBanned => StatusCode == 403 && ErrorCode == "banned";

        public BackendException(long statusCode, string errorCode, string message)
            : base(message)
        {
            StatusCode = statusCode;
            ErrorCode = errorCode;
        }
    }
}
