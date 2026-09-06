using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BabyDance.Editor
{
    public static class FaceAssetVerification
    {
        public static void VerifyImportedFaces()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetTools.CharacterFbx);
            foreach (var name in new[] { "FaceEyes", "FaceMouth" })
            {
                var renderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.name == name).ToArray();
                if (renderers.Length != 1) throw new InvalidOperationException($"{name}: renderer count={renderers.Length}");
                var renderer = renderers[0];
                var mesh = renderer.sharedMesh;
                if (mesh == null || mesh.vertexCount == 0) throw new InvalidOperationException($"{name}: empty mesh");
                var weights = mesh.boneWeights;
                if (weights.Length != mesh.vertexCount || weights.Any(w => w.weight0 != 1f || w.weight1 != 0f ||
                    w.weight2 != 0f || w.weight3 != 0f || renderer.bones[w.boneIndex0].name != "mixamorig:Head"))
                    throw new InvalidOperationException($"{name}: vertices are not weighted entirely to Head");
                var uv = mesh.uv;
                if (uv.Length != mesh.vertexCount || uv.Any(v => v.x < 0 || v.x > 1 || v.y < 0 || v.y > 1))
                    throw new InvalidOperationException($"{name}: invalid UV domain");
                var texture = renderer.sharedMaterial.GetTexture("_BaseMap") as Texture2D;
                var width = name == "FaceEyes" ? 512 : 384;
                var height = name == "FaceEyes" ? 256 : 128;
                if (texture == null || texture.width != width || texture.height != height || texture.mipmapCount <= 1 || texture.wrapMode != TextureWrapMode.Clamp)
                    throw new InvalidOperationException($"{name}: atlas size/mipmap/wrap mismatch");
                if (renderer.sharedMaterial.GetFloat("_Surface") != 1f || renderer.sharedMaterial.GetFloat("_ZWrite") != 0f)
                    throw new InvalidOperationException($"{name}: not a transparent material");
                Debug.Log($"{Log.Tag} imported {name} vertices={mesh.vertexCount} HeadWeight=1 atlas={texture.width}x{texture.height} mips={texture.mipmapCount} wrap={texture.wrapMode}");
            }
            Debug.Log($"{Log.Tag} BabyDance.Editor.FaceAssetVerification.VerifyImportedFaces done");
        }

        /// <summary>全ダンス20点について全 HumanBodyBones の Y 範囲を CSV に記録する。</summary>
        public static void SnapshotRig()
        {
            var output = Environment.GetEnvironmentVariable("FACE_RIG_OUTPUT");
            if (string.IsNullOrEmpty(output)) throw new InvalidOperationException("FACE_RIG_OUTPUT is required");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetTools.CharacterFbx);
            var go = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var animator = go.GetComponent<Animator>();
                if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
                    throw new InvalidOperationException("Valid humanoid required");
                var paths = AssetTools.DanceFbxPaths();
                if (paths.Length == 0) throw new InvalidOperationException("No dance clips");
                var lines = new List<string> { "clip,bone,present,minBoneY,maxBoneY" };
                foreach (var path in paths)
                {
                    var clip = AssetTools.MainClip(path);
                    if (clip == null) throw new InvalidOperationException($"Missing clip {path}");
                    var min = Enumerable.Repeat(float.PositiveInfinity, (int)HumanBodyBones.LastBone).ToArray();
                    var max = Enumerable.Repeat(float.NegativeInfinity, min.Length).ToArray();
                    for (var sample = 0; sample < 20; sample++)
                    {
                        clip.SampleAnimation(go, clip.length * sample / 20f);
                        for (var i = 0; i < min.Length; i++)
                        {
                            var bone = animator.GetBoneTransform((HumanBodyBones)i);
                            if (bone == null) continue;
                            min[i] = Mathf.Min(min[i], bone.position.y);
                            max[i] = Mathf.Max(max[i], bone.position.y);
                        }
                    }
                    for (var i = 0; i < min.Length; i++)
                    {
                        var present = !float.IsPositiveInfinity(min[i]);
                        lines.Add($"{clip.name},{(HumanBodyBones)i},{present},{min[i].ToString("R", CultureInfo.InvariantCulture)},{max[i].ToString("R", CultureInfo.InvariantCulture)}");
                    }
                    Debug.Log($"{Log.Tag} rig {clip.name} bones={min.Count(v => !float.IsPositiveInfinity(v))} samples=20 minBoneY={min.Min():R} maxBoneY={max.Max():R}");
                }
                File.WriteAllLines(output, lines);
                Debug.Log($"{Log.Tag} BabyDance.Editor.FaceAssetVerification.SnapshotRig done: {output} rows={lines.Count - 1}");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
