using UnityEngine;
using UnityEditor;


namespace Unity.Netcode.Samples.BossRoom
{
    /// <summary>
    /// Implement your Tutorial callbacks here.
    /// </summary>
    [CreateAssetMenu(fileName = k_DefaultFileName, menuName = "Tutorials/" + k_DefaultFileName + " Instance")]
    public class TutorialCallbacks : ScriptableObject
    {
        [SerializeField]
        SceneAsset m_StartupScene;

        /// <summary>
        /// The default file name used to create asset of this class type.
        /// </summary>
        const string k_DefaultFileName = "TutorialCallbacks";

        const string k_OdinConfigPath = "Assets/Resources/OdinSampleConfig.asset";

        /// <summary>
        /// Creates a TutorialCallbacks asset and shows it in the Project window.
        /// </summary>
        /// <param name="assetPath">
        /// A relative path to the project's root. If not provided, the Project window's currently active folder path is used.
        /// </param>
        /// <returns>The created asset</returns>
        public static ScriptableObject CreateAndShowAsset(string assetPath = null)
        {
            assetPath = assetPath ?? $"{Unity.Tutorials.Editor.TutorialEditorUtils.GetActiveFolderPath()}/{k_DefaultFileName}.asset";
            var asset = CreateInstance<TutorialCallbacks>();
            AssetDatabase.CreateAsset(asset, AssetDatabase.GenerateUniqueAssetPath(assetPath));
            EditorUtility.FocusProjectWindow(); // needed in order to make the selection of newly created asset to really work
            Selection.activeObject = asset;
            return asset;
        }

        public void StartTutorial(Unity.Tutorials.Editor.Tutorial tutorial)
        {
            Unity.Tutorials.Editor.TutorialWindow.StartTutorial(tutorial);
        }

        /// <summary>
        /// True once ODIN has either a backend URL or a development access key configured.
        /// </summary>
        public bool IsOdinConfigured()
        {
            var config = AssetDatabase.LoadAssetAtPath<Unity.BossRoom.OdinServices.Backend.OdinSampleConfig>(k_OdinConfigPath);
            return config != null && config.IsConfigured;
        }

        public void ShowOdinConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<Unity.BossRoom.OdinServices.Backend.OdinSampleConfig>(k_OdinConfigPath);
            if (config == null)
            {
                Debug.LogWarning($"{k_OdinConfigPath} not found. Create it with Assets > Create > Boss Room > ODIN Sample Config.");
                return;
            }

            Selection.activeObject = config;
            EditorGUIUtility.PingObject(config);
        }

        public void OpenURL(string url)
        {
            Unity.Tutorials.Editor.TutorialEditorUtils.OpenUrl(url);
        }

        public void LoadStartupScene()
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(AssetDatabase.GetAssetPath(m_StartupScene));
        }
    }
}
