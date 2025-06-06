using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityHierarchyExpansionMemory
{
    [InitializeOnLoad]
    public static class SceneHierarchyExpansionMemory
    {
        #region Constants
        const string PrefsKeyPrefix = "HierarchyExpansion_";
        #endregion

        #region Static
        static MethodInfo _getExpandedIDs;
        static MethodInfo _setExpandedMethod;
        static System.Type _sceneHierarchyWindowType;
        static PropertyInfo _lastInteractedHierarchyWindow;

        static readonly string _projectKeyPrefix;

        static SceneHierarchyExpansionMemory()
        {
            // Compute project-unique prefix
            _projectKeyPrefix = PrefsKeyPrefix + HashProjectPath() + "_";

            // Cache reflection data
            _sceneHierarchyWindowType = typeof(EditorWindow).Assembly.GetType(
                "UnityEditor.SceneHierarchyWindow"
            );
            _getExpandedIDs = _sceneHierarchyWindowType.GetMethod(
                "GetExpandedIDs",
                BindingFlags.NonPublic | BindingFlags.Instance
            );
            _setExpandedMethod = _sceneHierarchyWindowType.GetMethod(
                "SetExpanded",
                BindingFlags.NonPublic | BindingFlags.Instance
            );
            _lastInteractedHierarchyWindow = _sceneHierarchyWindowType.GetProperty(
                "lastInteractedHierarchyWindow",
                BindingFlags.Public | BindingFlags.Static
            );

            // Set callbacks
            EditorSceneManager.sceneClosing += OnSceneClosing;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;

            // Restore expanded state after domain reloads
            RestoreExpandedState();
        }
        #endregion

        #region Unity Lifecycle
        static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
                RestoreExpandedState();
        }

        static void OnSceneClosing(Scene scene, bool removing)
        {
            if (!Application.isPlaying)
                SaveExpandedState(scene);
        }

        static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            if (!Application.isPlaying)
                RestoreExpandedState();
        }
        #endregion

        #region Private API
        static void SaveExpandedState(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            EditorWindow hierarchyWindow = GetSceneHierarchyWindow();
            if (hierarchyWindow == null)
                return;

            int[] expandedIds = GetExpandedIDs(hierarchyWindow);
            List<string> expandedGuids = new List<string>();
            foreach (int id in expandedIds)
            {
                Object obj = EditorUtility.InstanceIDToObject(id);
                if (obj is GameObject go)
                {
                    expandedGuids.Add(GlobalObjectId.GetGlobalObjectIdSlow(go).ToString());
                }
            }

            string sceneGuid = GetSceneGuid(scene);
            if (string.IsNullOrEmpty(sceneGuid))
                return;

            string key = _projectKeyPrefix + sceneGuid;
            EditorPrefs.SetString(key, string.Join("|", expandedGuids));
        }

        static void RestoreExpandedState()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            string sceneGuid = GetSceneGuid(scene);
            if (string.IsNullOrEmpty(sceneGuid))
                return;

            string key = _projectKeyPrefix + sceneGuid;
            if (!EditorPrefs.HasKey(key))
                return;

            string data = EditorPrefs.GetString(key);
            string[] guidStrings = data.Split('|');

            List<int> expandedInstanceIds = new List<int>();
            foreach (string guidStr in guidStrings)
            {
                if (GlobalObjectId.TryParse(guidStr, out GlobalObjectId gid))
                {
                    Object obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gid);
                    if (obj is GameObject go && go.scene == scene)
                        expandedInstanceIds.Add(go.GetInstanceID());
                }
            }

            EditorWindow hierarchyWindow = GetSceneHierarchyWindow();
            if (hierarchyWindow != null)
            {
                foreach (int id in expandedInstanceIds)
                    SetExpanded(hierarchyWindow, id, true);

                hierarchyWindow.Repaint();
            }
        }

        static string GetSceneGuid(Scene scene)
        {
            if (!scene.IsValid())
                return null;
            return AssetDatabase.AssetPathToGUID(scene.path);
        }

        static string HashProjectPath()
        {
            using var sha256 = SHA256.Create();
            byte[] bytes = Encoding.UTF8.GetBytes(Application.dataPath);
            byte[] hash = sha256.ComputeHash(bytes);
            return System.BitConverter.ToString(hash).Replace("-", "").Substring(0, 8); // 8-char prefix
        }

        // Reflection Helpers
        static EditorWindow GetSceneHierarchyWindow() =>
            _lastInteractedHierarchyWindow.GetValue(null) as EditorWindow;

        static int[] GetExpandedIDs(EditorWindow window) =>
            _getExpandedIDs.Invoke(window, null) as int[];

        static void SetExpanded(EditorWindow window, int id, bool expanded) =>
            _setExpandedMethod.Invoke(window, new object[] { id, expanded });
        #endregion
    }
}
