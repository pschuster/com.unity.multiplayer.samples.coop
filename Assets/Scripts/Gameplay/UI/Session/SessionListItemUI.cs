using TMPro;
using Unity.BossRoom.OdinServices.Backend;
using UnityEngine;
using VContainer;

namespace Unity.BossRoom.Gameplay.UI
{
    /// <summary>
    /// An individual lobby in the list of available lobbies.
    /// </summary>
    public class SessionListItemUI : MonoBehaviour
    {
        [SerializeField]
        TextMeshProUGUI m_SessionNameText;
        [SerializeField]
        TextMeshProUGUI m_SessionCountText;

        [Inject]
        SessionUIMediator m_SessionUIMediator;

        LobbyInfo m_Data;

        public void SetData(LobbyInfo data)
        {
            m_Data = data;
            m_SessionNameText.SetText(data.name);
            m_SessionCountText.SetText($"{data.memberCount}/{data.maxMembers}");
        }

        public void OnClick()
        {
            m_SessionUIMediator.JoinSessionRequest(m_Data);
        }
    }
}
