using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BabyDance.Editor
{
    public static class SceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/Dance.unity";

        /// <summary>CLI: tools/unity.sh exec BabyDance.Editor.SceneBuilder.Build</summary>
        public static void Build()
        {
            var characterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetTools.CharacterFbx);
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(AssetTools.ControllerPath);
            var dances = AssetDatabase.FindAssets("t:DanceClipInfo", new[] { AssetTools.DanceFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p)
                .Select(AssetDatabase.LoadAssetAtPath<DanceClipInfo>)
                .ToArray();
            if (characterPrefab == null || controller == null || dances.Length == 0)
                throw new InvalidOperationException($"{Log.Tag} missing inputs: character={characterPrefab != null} controller={controller != null} dances={dances.Length}");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraGo.tag = "MainCamera";
            cameraGo.transform.position = new Vector3(0f, 1.3f, 3.2f);
            cameraGo.transform.LookAt(new Vector3(0f, 0.9f, 0f));
            var camera = cameraGo.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.15f, 0.16f, 0.2f);
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;

            var lightGo = new GameObject("Directional Light", typeof(Light));
            var light = lightGo.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            GameObject.CreatePrimitive(PrimitiveType.Plane).name = "Ground";

            var character = (GameObject)PrefabUtility.InstantiatePrefab(characterPrefab);
            character.name = "Dancer";
            character.GetComponent<Animator>().runtimeAnimatorController = controller;
            var driver = character.AddComponent<DanceDriver>();
            driver.dances = dances;

            var playerGo = new GameObject("Player", typeof(AudioSource), typeof(AudioLoader), typeof(DancePlayer), typeof(DanceUi));
            var player = playerGo.GetComponent<DancePlayer>();
            player.audio = playerGo.GetComponent<AudioLoader>();
            player.driver = driver;
            playerGo.GetComponent<DanceUi>().player = player;

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException($"{Log.Tag} failed to save {ScenePath}");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log($"{Log.Tag} BabyDance.Editor.SceneBuilder.Build done: {ScenePath} dances={dances.Length}");
        }
    }
}
