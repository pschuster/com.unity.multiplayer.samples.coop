using UnityEngine;

namespace Unity.BossRoom.OdinServices.Backend
{
    /// <summary>
    /// Connection settings for the ODIN services used by the sample. Lives in a Resources folder so it can be loaded
    /// before the dependency container is built.
    /// </summary>
    [CreateAssetMenu(menuName = "Boss Room/ODIN Sample Config", fileName = ResourceName)]
    public class OdinSampleConfig : ScriptableObject
    {
        public const string ResourceName = "OdinSampleConfig";

        [Tooltip("Invoke URL of the deployed Cortex function, e.g. https://cortex.odin.4players.io/invoke/<projectId>/bossroom-backend")]
        [SerializeField]
        string m_BackendUrl;

        [Tooltip("Without a backend URL the sample runs in local development mode: lobbies are only reachable by join code and room tokens are generated on the client with this ODIN access key. Never ship an access key.")]
        [SerializeField]
        string m_DevelopmentAccessKey;

        [Tooltip("ODIN gateway used by the transport")]
        [SerializeField]
        string m_Gateway = "https://gateway.odin.4players.io";

        [Tooltip("Seconds before backend requests time out")]
        [SerializeField]
        int m_RequestTimeoutSeconds = 15;

        public string BackendUrl => (m_BackendUrl ?? string.Empty).Trim().TrimEnd('/');
        public string DevelopmentAccessKey => (m_DevelopmentAccessKey ?? string.Empty).Trim();
        public string Gateway => m_Gateway;
        public int RequestTimeoutSeconds => m_RequestTimeoutSeconds;

        public bool UsesCortexBackend => !string.IsNullOrEmpty(BackendUrl);
        public bool UsesLocalDevelopmentMode => !UsesCortexBackend && !string.IsNullOrEmpty(DevelopmentAccessKey);
        public bool IsConfigured => UsesCortexBackend || UsesLocalDevelopmentMode;

        /// <summary>
        /// Used instead of the Resources asset when set, e.g. by tests.
        /// </summary>
        public static OdinSampleConfig RuntimeOverride { get; set; }

        public static OdinSampleConfig CreateForLocalDevelopment(string accessKey)
        {
            var config = CreateInstance<OdinSampleConfig>();
            config.m_DevelopmentAccessKey = accessKey;
            return config;
        }

        public static OdinSampleConfig Load()
        {
            if (RuntimeOverride != null)
            {
                return RuntimeOverride;
            }

            var config = Resources.Load<OdinSampleConfig>(ResourceName);
            if (config == null)
            {
                Debug.LogWarning($"No {ResourceName} found in a Resources folder. ODIN lobbies are disabled until one is created.");
                config = CreateInstance<OdinSampleConfig>();
            }

            return config;
        }
    }
}
