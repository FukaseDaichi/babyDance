using NUnit.Framework;
using UnityEngine;

namespace BabyDance.Tests
{
    public class DanceDriverMathTests
    {
        private static DanceClipInfo Info(float clipLength, int beatsPerLoop, float beatOffset)
        {
            var clip = new AnimationClip();
            clip.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, clipLength, 1f));
            var info = ScriptableObject.CreateInstance<DanceClipInfo>();
            info.clip = clip;
            info.beatsPerLoop = beatsPerLoop;
            info.beatOffset = beatOffset;
            return info;
        }

        [Test]
        public void SecondsPerBeatIsClipLengthOverBeats()
        {
            Assert.That(DanceDriver.SecondsPerBeat(Info(4f, 8, 0f)), Is.EqualTo(0.5).Within(1e-6));
        }

        [Test]
        public void NormalizedTimeWrapsEveryLoop()
        {
            var info = Info(4f, 8, 0f);
            Assert.That(DanceDriver.NormalizedTime(info, 0.0), Is.EqualTo(0f).Within(1e-6));
            Assert.That(DanceDriver.NormalizedTime(info, 2.0), Is.EqualTo(0.25f).Within(1e-6));
            Assert.That(DanceDriver.NormalizedTime(info, 10.0), Is.EqualTo(0.25f).Within(1e-6));
        }

        [Test]
        public void NormalizedTimeAppliesOffsetAndHandlesNegativeBeats()
        {
            var info = Info(4f, 8, 2f);
            Assert.That(DanceDriver.NormalizedTime(info, 2.0), Is.EqualTo(0f).Within(1e-6));
            Assert.That(DanceDriver.NormalizedTime(info, -1.0), Is.EqualTo(0.625f).Within(1e-6));
        }
    }
}
