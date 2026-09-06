using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace BabyDance.Editor
{
    public static class AssetTools
    {
        public const string CharacterFbx = CharacterImportSettings.Folder + "BabyBunny.fbx";

        public static string[] CharacterFbxPaths() =>
            Directory.GetFiles(CharacterImportSettings.Folder, "*.fbx")
                .Select(p => p.Replace('\\', '/'))
                .OrderBy(p => p)
                .ToArray();

        public static string[] DanceFbxPaths() => CharacterFbxPaths().Where(p => p != CharacterFbx).ToArray();

        public static AnimationClip MainClip(string fbxPath) =>
            AssetDatabase.LoadAllAssetRepresentationsAtPath(fbxPath).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));

        /// <summary>CLI: tools/unity.sh exec BabyDance.Editor.AssetTools.ReimportCharacters</summary>
        public static void ReimportCharacters()
        {
            var paths = CharacterFbxPaths();
            if (paths.Length == 0) throw new InvalidOperationException($"{Log.Tag} no FBX in {CharacterImportSettings.Folder}");

            foreach (var p in paths)
                AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.SaveAssets();

            foreach (var p in paths)
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(p);
                var avatar = AssetDatabase.LoadAllAssetsAtPath(p).OfType<Avatar>().FirstOrDefault();
                var clip = MainClip(p);
                var clipDesc = clip == null ? "none" : $"{clip.name}:{clip.length:F2}s:loop={clip.isLooping}";
                Debug.Log($"{Log.Tag} {p} type={importer.animationType} avatarValid={avatar != null && avatar.isValid} human={avatar != null && avatar.isHuman} clip={clipDesc}");

                if (avatar == null || !avatar.isValid || !avatar.isHuman)
                    throw new InvalidOperationException($"{Log.Tag} {p} has no valid Humanoid avatar");
                if (p != CharacterFbx && (clip == null || !clip.isLooping))
                    throw new InvalidOperationException($"{Log.Tag} {p} has no looping AnimationClip");
            }
            Debug.Log($"{Log.Tag} BabyDance.Editor.AssetTools.ReimportCharacters done: {paths.Length} files");
        }

        /// <summary>URP の標準マテリアル設定を使い、名前付き FBX スロットへ remap する。</summary>
        public static void BuildFaceMaterials()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(CharacterFbx);
            foreach (var name in new[] { "FaceEyes", "FaceMouth" })
            {
                var texturePath = $"Assets/Characters/{name}.png";
                var textureImporter = (TextureImporter)AssetImporter.GetAtPath(texturePath);
                if (textureImporter == null) throw new InvalidOperationException($"Missing atlas {texturePath}");
                textureImporter.textureType = TextureImporterType.Default;
                textureImporter.sRGBTexture = true;
                textureImporter.alphaSource = TextureImporterAlphaSource.FromInput;
                textureImporter.alphaIsTransparency = true;
                textureImporter.wrapMode = TextureWrapMode.Clamp;
                textureImporter.mipmapEnabled = true;
                textureImporter.npotScale = TextureImporterNPOTScale.None;
                textureImporter.filterMode = FilterMode.Bilinear;
                textureImporter.SaveAndReimport();
                var path = $"Assets/Characters/{name}.mat";
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) throw new InvalidOperationException("URP Lit shader missing");
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader);
                    AssetDatabase.CreateAsset(material, path);
                }
                material.shader = shader;
                material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
                material.SetColor("_BaseColor", Color.white);
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f);
                material.SetFloat("_AlphaClip", 0f);
                material.SetFloat("_Metallic", 0f);
                material.SetFloat("_Smoothness", 0f);
                BaseShaderGUI.SetMaterialKeywords(material);
                EditorUtility.SetDirty(material);
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), material);
            }
            AssetDatabase.SaveAssets();
            importer.SaveAndReimport();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterFbx);
            foreach (var name in new[] { "FaceEyes", "FaceMouth" })
            {
                var renderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.name == name).ToArray();
                var expected = AssetDatabase.LoadAssetAtPath<Material>($"Assets/Characters/{name}.mat");
                if (renderers.Length != 1 || renderers[0].sharedMaterial != expected)
                    throw new InvalidOperationException($"Face renderer/remap missing: {name}, count={renderers.Length}");
                Debug.Log($"{Log.Tag} face {name} renderer=1 material={expected.name}");
            }
            Debug.Log($"{Log.Tag} BabyDance.Editor.AssetTools.BuildFaceMaterials done");
        }

        public const string DanceFolder = "Assets/Dance";
        public const string ControllerPath = DanceFolder + "/Dance.controller";
        public const string LayerName = "Base Layer";

        /// <summary>CLI: tools/unity.sh exec BabyDance.Editor.AssetTools.BuildDanceAssets</summary>
        public static void BuildDanceAssets()
        {
            var dancePaths = DanceFbxPaths();
            if (dancePaths.Length == 0)
                throw new InvalidOperationException($"{Log.Tag} no dance FBX found in {CharacterImportSettings.Folder}");

            if (!AssetDatabase.IsValidFolder(DanceFolder))
                AssetDatabase.CreateFolder("Assets", "Dance");

            // 作り直すと GUID が変わり、GUID で controller を参照している Dance.unity が
            // 無言で切れる。既存があれば読み直し、中身だけ差し替える。
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)
                             ?? AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var stateMachine = controller.layers[0].stateMachine;
            var keep = new HashSet<string>();
            var wanted = new HashSet<string>();

            foreach (var fbx in dancePaths)
            {
                var clip = MainClip(fbx) ?? throw new InvalidOperationException($"{Log.Tag} no AnimationClip in {fbx}");
                // 消して足し直すと state に新しい fileID が振られ、再生成のたびに差分ノイズだけが出る。
                // 名前で引き当て、既存があれば motion を差し替える。
                var state = stateMachine.states.Select(c => c.state).FirstOrDefault(s => s.name == clip.name)
                            ?? stateMachine.AddState(clip.name);
                state.motion = clip;
                EditorUtility.SetDirty(state);
                wanted.Add(clip.name);

                var infoPath = $"{DanceFolder}/{clip.name}.asset";
                if (!keep.Add(infoPath)) throw new InvalidOperationException($"{Log.Tag} duplicate clip name: {clip.name}");
                var info = AssetDatabase.LoadAssetAtPath<DanceClipInfo>(infoPath);
                if (info == null)
                {
                    info = ScriptableObject.CreateInstance<DanceClipInfo>();
                    // 新規作成時のみ 120 BPM で等速になる拍数を初期値にする。既存は人手調整値を保つ。
                    info.beatsPerLoop = Mathf.Max(1, Mathf.RoundToInt(clip.length * 2f));
                    AssetDatabase.CreateAsset(info, infoPath);
                }
                info.clip = clip;
                info.stateName = $"{LayerName}.{clip.name}";
                EditorUtility.SetDirty(info);
                Debug.Log($"{Log.Tag} dance {clip.name} length={clip.length:F2}s beatsPerLoop={info.beatsPerLoop} beatOffset={info.beatOffset}");
            }

            foreach (var child in stateMachine.states)
            {
                if (wanted.Contains(child.state.name)) continue;
                var removed = child.state.name;
                stateMachine.RemoveState(child.state); // 配列の再代入では state がサブアセットとして残る
                Debug.Log($"{Log.Tag} removed state {removed}");
            }

            var stale = AssetDatabase.FindAssets("t:DanceClipInfo", new[] { DanceFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !keep.Contains(p))
                .ToArray();
            foreach (var p in stale)
            {
                AssetDatabase.DeleteAsset(p);
                Debug.Log($"{Log.Tag} deleted stale {p}");
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"{Log.Tag} BabyDance.Editor.AssetTools.BuildDanceAssets done: {stateMachine.states.Length} states");
        }
    }
}
