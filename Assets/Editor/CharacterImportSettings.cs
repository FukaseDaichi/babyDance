using System.IO;
using UnityEditor;

namespace BabyDance.Editor
{
    /// <summary>
    /// Assets/Characters/ 配下の FBX を Humanoid にし、
    /// クリップ名をファイル名に統一し、ループとルート固定を設定する。
    /// </summary>
    public sealed class CharacterImportSettings : AssetPostprocessor
    {
        public const string Folder = "Assets/Characters/";

        private bool IsTarget => assetPath.StartsWith(Folder) && assetPath.EndsWith(".fbx");

        private void OnPreprocessModel()
        {
            if (!IsTarget) return;
            var importer = (ModelImporter)assetImporter;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
        }

        private void OnPreprocessAnimation()
        {
            if (!IsTarget) return;
            var importer = (ModelImporter)assetImporter;
            var clips = importer.defaultClipAnimations;
            if (clips.Length == 0) return;
            var name = Path.GetFileNameWithoutExtension(assetPath);
            for (var i = 0; i < clips.Length; i++)
            {
                var c = clips[i];
                c.name = clips.Length == 1 ? name : $"{name}_{i}";
                c.loopTime = true;
                c.lockRootRotation = true;
                c.keepOriginalOrientation = true;
                c.lockRootHeightY = true;
                c.keepOriginalPositionY = true;
                c.lockRootPositionXZ = true;
                c.keepOriginalPositionXZ = true;
            }
            importer.clipAnimations = clips;
        }
    }
}
