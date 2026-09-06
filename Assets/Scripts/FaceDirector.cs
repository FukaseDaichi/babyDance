using System;
using UnityEngine;

namespace BabyDance
{
    /// <summary>FaceScore のコマを2枚のデカールに適用する。拍と切替情報は呼び出し側が渡す。</summary>
    public sealed class FaceDirector : MonoBehaviour
    {
        public int Expression { get; set; }
        public double SwitchBeat { get; set; } = double.NegativeInfinity;

        private static readonly int BaseMapST = Shader.PropertyToID("_BaseMap_ST");
        private SkinnedMeshRenderer _eyes;
        private SkinnedMeshRenderer _mouth;
        private MaterialPropertyBlock _properties;

        public void Tick(double beat)
        {
            if (_eyes == null || _mouth == null) ResolveRenderers();
            var pose = FaceScore.PoseAt(beat, Expression, SwitchBeat);
            Apply(_eyes, FaceScore.AtlasRect(pose.EyeFrame, 4, 2));
            Apply(_mouth, FaceScore.AtlasRect(pose.MouthFrame, 3, 1));
        }

        private void ResolveRenderers()
        {
            var eyes = 0;
            var mouths = 0;
            foreach (var renderer in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.name == "FaceEyes") { _eyes = renderer; eyes++; }
                if (renderer.name == "FaceMouth") { _mouth = renderer; mouths++; }
            }
            if (eyes != 1 || mouths != 1)
                throw new InvalidOperationException($"{Log.Tag} FaceDirector requires one FaceEyes and one FaceMouth SkinnedMeshRenderer: eyes={eyes} mouths={mouths}");
            _properties = new MaterialPropertyBlock();
            Debug.Log($"{Log.Tag} FaceDirector ready: FaceEyes=1 FaceMouth=1");
        }

        private void Apply(SkinnedMeshRenderer renderer, (double X, double Y, double Z, double W) rect)
        {
            renderer.GetPropertyBlock(_properties);
            _properties.SetVector(BaseMapST, new Vector4((float)rect.X, (float)rect.Y, (float)rect.Z, (float)rect.W));
            renderer.SetPropertyBlock(_properties);
        }
    }
}
