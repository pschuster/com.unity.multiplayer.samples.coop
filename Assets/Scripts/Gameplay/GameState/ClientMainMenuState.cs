using System;
using Unity.BossRoom.Gameplay.Configuration;
using Unity.BossRoom.Gameplay.UI;
using Unity.BossRoom.OdinServices.Auth;
using Unity.BossRoom.OdinServices.Backend;
using Unity.BossRoom.OdinServices.Sessions;
using Unity.BossRoom.Utils;
using UnityEngine;
using UnityEngine.UI;
using VContainer;
using VContainer.Unity;

namespace Unity.BossRoom.Gameplay.GameState
{
    /// <summary>
    /// Game Logic that runs when sitting at the MainMenu. This is likely to be "nothing", as no game has been started. But it is
    /// nonetheless important to have a game state, as the GameStateBehaviour system requires that all scenes have states.
    /// </summary>
    /// <remarks> OnNetworkSpawn() won't ever run, because there is no network connection at the main menu screen.
    /// Fortunately we know you are a client, because all players are clients when sitting at the main menu screen.
    /// </remarks>
    public class ClientMainMenuState : GameStateBehaviour
    {
        public override GameState ActiveState => GameState.MainMenu;

        [SerializeField]
        NameGenerationData m_NameGenerationData;
        [SerializeField]
        SessionUIMediator m_SessionUIMediator;
        [SerializeField]
        Button m_SessionButton;
        [SerializeField]
        GameObject m_SignInSpinner;
        [SerializeField]
        UIProfileSelector m_UIProfileSelector;
        [SerializeField]
        UITooltipDetector m_UGSSetupTooltipDetector;

        [Inject]
        PlayerAuthFacade m_AuthServiceFacade;
        [Inject]
        OdinSampleConfig m_OdinSampleConfig;
        [Inject]
        LocalSessionUser m_LocalUser;
        [Inject]
        LocalSession m_LocalSession;
        [Inject]
        ProfileManager m_ProfileManager;

        protected override void Awake()
        {
            base.Awake();

            m_SessionButton.interactable = false;
            m_SessionUIMediator.Hide();

            if (!m_OdinSampleConfig.IsConfigured)
            {
                OnSignInFailed();
                return;
            }

            TrySignIn();
        }

        protected override void Configure(IContainerBuilder builder)
        {
            base.Configure(builder);
            builder.RegisterComponent(m_NameGenerationData);
            builder.RegisterComponent(m_SessionUIMediator);
        }

        async void TrySignIn()
        {
            m_ProfileManager.onProfileChanged += OnProfileChanged;
            if (await m_AuthServiceFacade.SignInAsync(ClientPrefs.GetGuid(), m_ProfileManager.Profile, m_LocalUser.DisplayName))
            {
                OnAuthSignIn();
            }
            else
            {
                OnSignInFailed();
            }
        }

        void OnAuthSignIn()
        {
            m_SessionButton.interactable = true;
            m_UGSSetupTooltipDetector.enabled = false;
            m_SignInSpinner.SetActive(false);

            m_LocalUser.ID = m_AuthServiceFacade.PlayerId;

            // The local SessionUser object will be hooked into UI before the LocalSession is populated during session join, so the LocalSession must know about it already when that happens.
            m_LocalSession.AddUser(m_LocalUser);
        }

        void OnSignInFailed()
        {
            if (m_SessionButton)
            {
                m_SessionButton.interactable = false;
                m_UGSSetupTooltipDetector.enabled = true;
            }

            if (m_SignInSpinner)
            {
                m_SignInSpinner.SetActive(false);
            }
        }

        protected override void OnDestroy()
        {
            m_ProfileManager.onProfileChanged -= OnProfileChanged;
            base.OnDestroy();
        }

        async void OnProfileChanged()
        {
            m_SessionButton.interactable = false;
            m_SignInSpinner.SetActive(true);

            var signedIn = await m_AuthServiceFacade.SignInAsync(ClientPrefs.GetGuid(), m_ProfileManager.Profile, m_LocalUser.DisplayName);
            if (!signedIn)
            {
                OnSignInFailed();
                return;
            }

            m_SessionButton.interactable = true;
            m_SignInSpinner.SetActive(false);

            // Updating LocalUser and LocalSession
            m_LocalSession.RemoveUser(m_LocalUser);
            m_LocalUser.ID = m_AuthServiceFacade.PlayerId;
            m_LocalSession.AddUser(m_LocalUser);
        }

        public void OnStartClicked()
        {
            m_SessionUIMediator.ToggleJoinSessionUI();
            m_SessionUIMediator.Show();
        }

        public void OnChangeProfileClicked()
        {
            m_UIProfileSelector.Show();
        }
    }
}
