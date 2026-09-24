using System.Collections.Generic;
using System.IO;
using OdinNative.Netcode;
using OdinNative.Netcode.Voice;
using OdinNative.Unity.Audio;
using Unity.BossRoom.Gameplay.UI;
using Unity.BossRoom.OdinServices.Backend;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.BossRoom.Editor.OdinMigration
{
    /// <summary>
    /// One-time migration of scenes and prefabs from Unity Gaming Services to ODIN.
    /// Run from the menu or with -executeMethod Unity.BossRoom.Editor.OdinMigration.OdinSampleMigration.RunFromCommandLine
    /// </summary>
    public static class OdinSampleMigration
    {
        const string k_NetworkingManagerPrefab = "Assets/Prefabs/NetworkingManager.prefab";
        const string k_PlayerAvatarPrefab = "Assets/Prefabs/Character/PlayerAvatar.prefab";
        const string k_MainMenuCanvasPrefab = "Assets/Prefabs/UI/MainMenu UI Canvas.prefab";
        const string k_NetworkOverlayPrefab = "Assets/Prefabs/NetworkOverlay.prefab";
        const string k_IPPopupPrefab = "Assets/Prefabs/UI/IPPopup.prefab";
        const string k_NetworkSimulatorPrefab = "Assets/Prefabs/UI/NetworkSimulator.prefab";
        const string k_MainMenuScene = "Assets/Scenes/MainMenu.unity";
        const string k_StartupScene = "Assets/Scenes/Startup.unity";
        const string k_ConfigPath = "Assets/Resources/" + OdinSampleConfig.ResourceName + ".asset";

        static readonly List<string> s_Log = new List<string>();

        [MenuItem("Boss Room/ODIN/Run UGS to ODIN Migration")]
        public static void Run()
        {
            s_Log.Clear();

            MigrateNetworkingManager();
            MigratePlayerAvatar();
            MigrateMainMenuCanvas();
            RemoveMissingScripts(k_NetworkOverlayPrefab);
            MigrateScene(k_MainMenuScene, k_IPPopupPrefab);
            MigrateScene(k_StartupScene, k_NetworkSimulatorPrefab);
            DeleteAsset(k_IPPopupPrefab);
            DeleteAsset(k_NetworkSimulatorPrefab);
            CreateConfig();

            AssetDatabase.SaveAssets();
            Debug.Log("[ODIN Migration]\n" + string.Join("\n", s_Log));
        }

        public static void RunFromCommandLine()
        {
            Run();
            EditorApplication.Exit(0);
        }

        static void MigrateNetworkingManager()
        {
            var root = PrefabUtility.LoadPrefabContents(k_NetworkingManagerPrefab);
            try
            {
                var networkManager = root.GetComponentInChildren<NetworkManager>(true);
                var target = networkManager.gameObject;

                foreach (var transport in target.GetComponents<NetworkTransport>())
                {
                    if (transport is OdinNetcodeTransport)
                    {
                        continue;
                    }

                    s_Log.Add($"Removed {transport.GetType().Name} from {target.name}");
                    Object.DestroyImmediate(transport, true);
                }

                if (!target.TryGetComponent(out OdinNetcodeTransport odinTransport))
                {
                    odinTransport = target.AddComponent<OdinNetcodeTransport>();
                    s_Log.Add($"Added {nameof(OdinNetcodeTransport)} to {target.name}");
                }

                if (!target.TryGetComponent(out OdinMicrophoneReader microphone))
                {
                    microphone = target.AddComponent<OdinMicrophoneReader>();
                }

                if (!target.TryGetComponent(out OdinNetcodeVoice voice))
                {
                    voice = target.AddComponent<OdinNetcodeVoice>();
                    s_Log.Add($"Added {nameof(OdinNetcodeVoice)} to {target.name}");
                }

                voice.Transport = odinTransport;
                voice.Microphone = microphone;
                networkManager.NetworkConfig.NetworkTransport = odinTransport;

                RemoveMissingScriptsRecursive(root);
                PrefabUtility.SaveAsPrefabAsset(root, k_NetworkingManagerPrefab);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static void MigratePlayerAvatar()
        {
            var root = PrefabUtility.LoadPrefabContents(k_PlayerAvatarPrefab);
            try
            {
                if (!root.TryGetComponent(out OdinNetworkPlayerVoice _))
                {
                    root.AddComponent<OdinNetworkPlayerVoice>();
                    s_Log.Add($"Added {nameof(OdinNetworkPlayerVoice)} to {root.name}");
                }

                PrefabUtility.SaveAsPrefabAsset(root, k_PlayerAvatarPrefab);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static void MigrateMainMenuCanvas()
        {
            var root = PrefabUtility.LoadPrefabContents(k_MainMenuCanvasPrefab);
            try
            {
                RemovePrefabInstances(root, k_IPPopupPrefab, "MainMenu UI Canvas");
                RemoveDirectIpButtons(root);

                foreach (var tooltip in root.GetComponentsInChildren<UITooltipDetector>(true))
                {
                    var serialized = new SerializedObject(tooltip);
                    var text = serialized.FindProperty("m_TooltipText");
                    if (text != null && text.stringValue.Contains("Unity Gaming Services"))
                    {
                        text.stringValue = "ODIN is not configured. Set a backend URL or a development access key in Assets/Resources/OdinSampleConfig.";
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                        s_Log.Add($"Updated setup tooltip on {tooltip.name}");
                    }
                }

                RemoveMissingScriptsRecursive(root);
                PrefabUtility.SaveAsPrefabAsset(root, k_MainMenuCanvasPrefab);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static void RemoveDirectIpButtons(GameObject root)
        {
            foreach (var button in root.GetComponentsInChildren<Button>(true))
            {
                for (var i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                {
                    if (button.onClick.GetPersistentMethodName(i) == "OnDirectIPClicked")
                    {
                        var go = button.gameObject;
                        var buttonName = go.name;
                        if (PrefabUtility.IsPartOfPrefabInstance(go) && !PrefabUtility.IsOutermostPrefabInstanceRoot(go))
                        {
                            // objects inside nested prefab instances cannot be destroyed, only hidden
                            go.SetActive(false);
                            s_Log.Add($"Hid direct IP button {buttonName}");
                        }
                        else
                        {
                            Object.DestroyImmediate(go, true);
                            s_Log.Add($"Removed direct IP button {buttonName}");
                        }
                        break;
                    }
                }
            }
        }

        static void MigrateScene(string scenePath, string removedPrefabPath)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            foreach (var root in scene.GetRootGameObjects())
            {
                RemovePrefabInstances(root, removedPrefabPath, scene.name);
            }

            foreach (var root in scene.GetRootGameObjects())
            {
                if (root == null)
                {
                    continue;
                }

                RemoveDirectIpButtons(root);
                RemoveMissingScriptsRecursive(root);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        static void RemovePrefabInstances(GameObject root, string prefabPath, string context)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null || root == null)
            {
                return;
            }

            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                // children of an already removed instance are destroyed with it
                if (transform == null)
                {
                    continue;
                }

                var go = transform.gameObject;
                if (PrefabUtility.IsOutermostPrefabInstanceRoot(go) && PrefabUtility.GetCorrespondingObjectFromSource(go) == prefab)
                {
                    s_Log.Add($"Removed {go.name} from {context}");
                    Object.DestroyImmediate(go, true);
                }
            }
        }

        static void RemoveMissingScripts(string prefabPath)
        {
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                if (RemoveMissingScriptsRecursive(root) > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static int RemoveMissingScriptsRecursive(GameObject root)
        {
            var removed = 0;
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                // components of prefab instances are fixed in their prefab asset
                if (PrefabUtility.IsPartOfPrefabInstance(transform.gameObject) && !PrefabUtility.IsAddedGameObjectOverride(transform.gameObject))
                {
                    continue;
                }

                var count = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(transform.gameObject);
                if (count > 0)
                {
                    s_Log.Add($"Removed {count} missing script(s) from {transform.name}");
                    removed += count;
                }
            }

            return removed;
        }

        static void DeleteAsset(string path)
        {
            if (File.Exists(path) && AssetDatabase.DeleteAsset(path))
            {
                s_Log.Add($"Deleted {path}");
            }
        }

        static void CreateConfig()
        {
            if (AssetDatabase.LoadAssetAtPath<OdinSampleConfig>(k_ConfigPath) != null)
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(k_ConfigPath));
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<OdinSampleConfig>(), k_ConfigPath);
            s_Log.Add($"Created {k_ConfigPath}");
        }
    }
}
