using System;

namespace Unity.BossRoom.OdinServices
{
    public struct ServiceErrorMessage
    {
        public enum Service
        {
            Authentication,
            Lobby,
            Voice,
        }

        public string Title;
        public string Message;
        public Service AffectedService;
        public Exception OriginalException;

        public ServiceErrorMessage(string title, string message, Service service, Exception originalException = null)
        {
            Title = title;
            Message = message;
            AffectedService = service;
            OriginalException = originalException;
        }
    }
}
