using System;
using UnityEngine;

namespace BabyDance
{
    /// <summary>
    /// CameraScore の姿勢を Main Camera に適用する。時間源は呼び出し側が渡す拍のみ。
    /// 注視点は Humanoid ボーンの実座標。唯一の状態は注視点の指数平滑で、拍差分で進める。
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class ShotDirector : MonoBehaviour
    {
        private const double TargetSmoothingBeats = 0.25;
        private const float HeadOffset = 0.1f;

        public Animator dancer;

        /// <summary>真なら譜面を無視し FixedPose に置く（デバッグ・観察用）。</summary>
        public bool Fixed;

        private Camera _camera;
        private Transform _hips;
        private Transform _head;
        private Transform _leftFoot;
        private Transform _rightFoot;
        private Vector3 _smoothedTarget;
        private CameraTarget _lastTarget;
        private bool _hasTarget;
        private double _lastBeat;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        public void Tick(double beat)
        {
            if (_hips == null) ResolveBones();

            var pose = Fixed ? CameraScore.FixedPose : CameraScore.PoseAt(beat);
            var raw = TargetPosition(pose.Target);
            var deltaBeats = beat - _lastBeat;
            _lastBeat = beat;

            // 注視点の種別が変わるカットでは平滑をリセットし、旧注視点から滑る跳ねを防ぐ。
            if (!_hasTarget || pose.Target != _lastTarget || deltaBeats <= 0.0)
            {
                _smoothedTarget = raw;
                _hasTarget = true;
                _lastTarget = pose.Target;
            }
            else
            {
                var k = 1.0 - Math.Exp(-deltaBeats / TargetSmoothingBeats);
                _smoothedTarget = Vector3.Lerp(_smoothedTarget, raw, (float)k);
            }

            Apply(pose, _smoothedTarget);
        }

        private void ResolveBones()
        {
            if (dancer == null) throw new InvalidOperationException($"{Log.Tag} ShotDirector.dancer is not set");
            _hips = Bone(HumanBodyBones.Hips);
            _head = Bone(HumanBodyBones.Head);
            _leftFoot = Bone(HumanBodyBones.LeftFoot);
            _rightFoot = Bone(HumanBodyBones.RightFoot);
        }

        private Transform Bone(HumanBodyBones bone)
        {
            var t = dancer.GetBoneTransform(bone);
            if (t == null) throw new InvalidOperationException($"{Log.Tag} dancer has no humanoid bone {bone}");
            return t;
        }

        private Vector3 TargetPosition(CameraTarget target)
        {
            switch (target)
            {
                case CameraTarget.Head: return _head.position + Vector3.up * HeadOffset;
                case CameraTarget.Feet: return (_leftFoot.position + _rightFoot.position) * 0.5f;
                default: return _hips.position;
            }
        }

        private void Apply(CameraPose pose, Vector3 target)
        {
            var offset = CameraScore.Offset(pose, target.y);
            var position = target + new Vector3((float)offset.X, (float)offset.Y, (float)offset.Z);
            transform.SetPositionAndRotation(
                position,
                Quaternion.LookRotation(target - position) * Quaternion.Euler(0f, 0f, (float)pose.Roll));
            _camera.fieldOfView = (float)pose.Fov;
        }
    }
}
