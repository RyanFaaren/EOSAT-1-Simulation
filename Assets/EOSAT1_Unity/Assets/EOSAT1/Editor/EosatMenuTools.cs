// EOSAT-1 VR - Editor helpers for the main menu scene (EOSAT1_Menu).
//
//   EOSAT-1 > Open Menu Scene
//   EOSAT-1 > Rebuild Menu Scene              recreates the scene and puts it first in the build list
//   EOSAT-1 > Start Play Mode at Main Menu    (ticked by default) pressing Play always starts at
//                                             the menu, whichever scene is open; untick it to
//                                             play the open scene directly while you work on it
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Eosat1.EditorTools
{
    [InitializeOnLoad]
    public static class EosatMenuTools
    {
        const string PrefKey = "Eosat1.StartPlayAtMainMenu";
        const string ToggleItem = "EOSAT-1/Start Play Mode at Main Menu";
        const string SceneFile = MenuSettings.MenuSceneName + ".unity";

        static EosatMenuTools()
        {
            // wait until the asset database is ready
            EditorApplication.delayCall += ApplyPlayModeStartScene;
        }

        static bool StartAtMenu
        {
            get { return EditorPrefs.GetBool(PrefKey, true); }
            set { EditorPrefs.SetBool(PrefKey, value); }
        }

        static string FindMenuScenePath()
        {
            foreach (string guid in AssetDatabase.FindAssets(MenuSettings.MenuSceneName + " t:SceneAsset"))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                if (p.EndsWith("/" + SceneFile, StringComparison.OrdinalIgnoreCase)) return p;
            }
            return null;
        }

        /// <summary>The EOSAT1 folder, found from where this script lives (…/EOSAT1/Editor/EosatMenuTools.cs).</summary>
        static string FindRoot()
        {
            const string tail = "/Editor/EosatMenuTools.cs";
            foreach (string guid in AssetDatabase.FindAssets("EosatMenuTools t:MonoScript"))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                if (p.EndsWith(tail, StringComparison.OrdinalIgnoreCase)) return p.Substring(0, p.Length - tail.Length);
            }
            return "Assets/EOSAT1";
        }

        static void ApplyPlayModeStartScene()
        {
            string path = FindMenuScenePath();
            SceneAsset menu = path != null ? AssetDatabase.LoadAssetAtPath<SceneAsset>(path) : null;
            if (menu == null) return;
            if (StartAtMenu) EditorSceneManager.playModeStartScene = menu;
            else if (EditorSceneManager.playModeStartScene == menu) EditorSceneManager.playModeStartScene = null;
        }

        [MenuItem("EOSAT-1/Open Menu Scene", false, 2)]
        public static void OpenMenuScene()
        {
            string path = FindMenuScenePath();
            if (path == null)
            {
                EditorUtility.DisplayDialog("EOSAT-1", "No menu scene yet. Use EOSAT-1 > Rebuild Menu Scene first.", "OK");
                return;
            }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(path);
        }

        [MenuItem(ToggleItem, false, 40)]
        public static void ToggleStartAtMenu()
        {
            StartAtMenu = !StartAtMenu;
            ApplyPlayModeStartScene();
            Debug.Log(StartAtMenu ? "EOSAT-1: Play now starts at the main menu."
                                  : "EOSAT-1: Play now starts in the scene that is open.");
        }

        [MenuItem(ToggleItem, true)]
        public static bool ToggleStartAtMenuValidate()
        {
            Menu.SetChecked(ToggleItem, StartAtMenu);
            return true;
        }

        [MenuItem("EOSAT-1/Rebuild Menu Scene", false, 21)]
        public static void RebuildMenuScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string path = FindMenuScenePath();
            if (path == null)
            {
                string folder = FindRoot() + "/Scenes";
                if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(FindRoot(), "Scenes");
                path = folder + "/" + SceneFile;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("Mission Control Menu");
            var menu = go.AddComponent<MissionControlMenu>();
            menu.roomShader = Shader.Find("EOSAT1/Room");
            menu.panelShader = Shader.Find("EOSAT1/UiPanel");
            menu.mapShader = Shader.Find("EOSAT1/MapScreen");
            menu.consoleShader = Shader.Find("EOSAT1/ConsoleScreen");
            menu.textShader = Shader.Find("EOSAT1/HudText");
            menu.unlitShader = Shader.Find("EOSAT1/UnlitColor");
            menu.earthMap = FindTexture("earth_day");
            if (menu.earthMap == null)
                Debug.LogWarning("EOSAT-1: Textures/earth_day.png was not found, so the ground-track screen will have no map.");

            EditorSceneManager.SaveScene(scene, path);
            PutFirstInBuildSettings(path);
            AssetDatabase.SaveAssets();
            ApplyPlayModeStartScene();
            Debug.Log("EOSAT-1: menu scene saved to " + path + " and put first in the build list. Press Play.");
        }

        static Texture2D FindTexture(string fileName)
        {
            foreach (string guid in AssetDatabase.FindAssets(fileName + " t:Texture2D"))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                if (System.IO.Path.GetFileNameWithoutExtension(p) == fileName) return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            }
            return null;
        }

        static void PutFirstInBuildSettings(string path)
        {
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            list.RemoveAll(s => s.path == path);
            list.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
