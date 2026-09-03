using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BabyDance.Editor
{
    public static class AssetTools
    {
        private const string Tag = "[BabyDance]";
        public const string CharacterFbx = CharacterImportSettings.Folder + "XBot.fbx";

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
            if (paths.Length == 0) throw new InvalidOperationException($"{Tag} no FBX in {CharacterImportSettings.Folder}");

            foreach (var p in paths)
                AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.SaveAssets();

            foreach (var p in paths)
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(p);
                var avatar = AssetDatabase.LoadAllAssetsAtPath(p).OfType<Avatar>().FirstOrDefault();
                var clip = MainClip(p);
                var clipDesc = clip == null ? "none" : $"{clip.name}:{clip.length:F2}s:loop={clip.isLooping}";
                Debug.Log($"{Tag} {p} type={importer.animationType} avatarValid={avatar != null && avatar.isValid} human={avatar != null && avatar.isHuman} clip={clipDesc}");

                if (avatar == null || !avatar.isValid || !avatar.isHuman)
                    throw new InvalidOperationException($"{Tag} {p} has no valid Humanoid avatar");
                if (p != CharacterFbx && (clip == null || !clip.isLooping))
                    throw new InvalidOperationException($"{Tag} {p} has no looping AnimationClip");
            }
            Debug.Log($"{Tag} BabyDance.Editor.AssetTools.ReimportCharacters done: {paths.Length} files");
        }
    }
}
