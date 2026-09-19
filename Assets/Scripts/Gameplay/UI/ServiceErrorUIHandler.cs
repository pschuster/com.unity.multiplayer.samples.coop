using Unity.BossRoom.Infrastructure;
using Unity.BossRoom.OdinServices;
using Unity.BossRoom.OdinServices.Backend;
using UnityEngine;
using VContainer;

namespace Unity.BossRoom.Gameplay.UI
{
    /// <summary>
    /// Shows a popup for errors of the ODIN services (sign-in, lobbies, voice).
    /// </summary>
    public class ServiceErrorUIHandler : MonoBehaviour
    {
        ISubscriber<ServiceErrorMessage> m_ServiceErrorSubscription;

        void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        [Inject]
        void Initialize(ISubscriber<ServiceErrorMessage> serviceError)
        {
            m_ServiceErrorSubscription = serviceError;
            m_ServiceErrorSubscription.Subscribe(ServiceErrorHandler);
        }

        void ServiceErrorHandler(ServiceErrorMessage error)
        {
            if (error.OriginalException is BackendException backendException)
            {
                switch (backendException.StatusCode)
                {
                    case 0:
                        PopupManager.ShowPopupPanel(error.Title, "Could not reach the ODIN backend. Check your internet connection and the backend URL in OdinSampleConfig.");
                        return;
                    case 401:
                        PopupManager.ShowPopupPanel(error.Title, "Your session expired. Please try again.");
                        return;
                    case 404:
                        PopupManager.ShowPopupPanel("Lobby Not Found", "The join code is incorrect or the lobby has ended.");
                        return;
                    case 409 when backendException.ErrorCode == "lobby_full":
                        PopupManager.ShowPopupPanel("Lobby Full", "This lobby has no free slots.");
                        return;
                }
            }

            PopupManager.ShowPopupPanel(error.Title, error.Message);
        }

        void OnDestroy()
        {
            if (m_ServiceErrorSubscription != null)
            {
                m_ServiceErrorSubscription.Unsubscribe(ServiceErrorHandler);
            }
        }
    }
}
