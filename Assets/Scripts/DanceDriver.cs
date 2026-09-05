using System;
using UnityEngine;

namespace BabyDance
{
    /// <summary>
    /// Animator を無効化し、拍の差分だけ手動で Update する。
    /// 時間源は呼び出し側が渡す拍位置のみ。
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public sealed class DanceDriver : MonoBehaviour
    {
        private const int Layer = 0;

        public DanceClipInfo[] dances = Array.Empty<DanceClipInfo>();

        private Animator _animator;
        private DanceClipInfo _current;
        private double _lastBeat;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            _animator.enabled = false;
            _animator.applyRootMotion = false;
        }

        /// <summary>切替は 1 拍かけてクロスフェードする。初回は即時。</summary>
        public void SetDance(int index, double currentBeat)
        {
            var next = dances[index];
            if (!_animator.HasState(Layer, Animator.StringToHash(next.stateName)))
                throw new InvalidOperationException($"Animator has no state '{next.stateName}'");

            // Play の第3引数は正規化時間、CrossFadeInFixedTime の第4引数は秒。
            var phase = NormalizedTime(next, currentBeat);
            if (_current == null)
                _animator.Play(next.stateName, Layer, phase);
            else
                _animator.CrossFadeInFixedTime(next.stateName, (float)SecondsPerBeat(next), Layer, phase * next.clip.length);

            _current = next;
            _lastBeat = currentBeat;
            _animator.Update(0f);
        }

        public void Tick(double currentBeat)
        {
            if (_current == null) return;
            var deltaBeats = currentBeat - _lastBeat;
            _lastBeat = currentBeat;
            if (deltaBeats <= 0.0) return;
            _animator.Update((float)(deltaBeats * SecondsPerBeat(_current)));
        }

        public static double SecondsPerBeat(DanceClipInfo info) => info.clip.length / info.beatsPerLoop;

        public static float NormalizedTime(DanceClipInfo info, double beat)
        {
            var loops = (beat - info.beatOffset) / info.beatsPerLoop;
            return (float)(loops - Math.Floor(loops));
        }
    }
}
