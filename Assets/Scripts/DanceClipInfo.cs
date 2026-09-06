using UnityEngine;

namespace BabyDance
{
    /// <summary>ダンス 1 本のメタデータ。beatsPerLoop と beatOffset は耳合わせで人手調整する。</summary>
    [CreateAssetMenu(menuName = "BabyDance/Dance Clip Info")]
    public sealed class DanceClipInfo : ScriptableObject
    {
        public AnimationClip clip;
        public string stateName;
        [Min(1)] public int beatsPerLoop = 8;
        public float beatOffset;
        [Range(0, 3)] public int expression;
    }
}
