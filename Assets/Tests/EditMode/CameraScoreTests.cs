using System;
using NUnit.Framework;

namespace BabyDance.Tests
{
    public class CameraScoreTests
    {
        private const double Eps = 1e-9;

        [Test]
        public void ClampLimitsPitchDistanceAndFov()
        {
            var low = CameraScore.Clamp(new CameraPose(0.5, 12, -30, 5, CameraTarget.Hips));
            Assert.That(low.Distance, Is.EqualTo(1.0).Within(Eps));
            Assert.That(low.Pitch, Is.EqualTo(-20.0).Within(Eps));
            Assert.That(low.Fov, Is.EqualTo(10.0).Within(Eps));
            Assert.That(low.Yaw, Is.EqualTo(12.0).Within(Eps), "yaw は clamp 対象外");

            var high = CameraScore.Clamp(new CameraPose(2.0, 0, 30, 70, CameraTarget.Head));
            Assert.That(high.Fov, Is.EqualTo(55.0).Within(Eps));
            Assert.That(high.Pitch, Is.EqualTo(30.0).Within(Eps), "上向きピッチは制限しない");
        }

        [Test]
        public void CutSwitchesExactlyOnBeatAndHoldsBetween()
        {
            // 拍 30 でミディアム（FOV 26）→ 顔クローズアップ（FOV 10）にカット
            Assert.That(CameraScore.PoseAt(29.99).Fov, Is.EqualTo(26.0).Within(Eps));
            Assert.That(CameraScore.PoseAt(30.0).Fov, Is.EqualTo(10.0).Within(Eps));
            var first = CameraScore.PoseAt(30.0);
            for (var b = 30.0; b < 32.0; b += 1.0 / 16)
            {
                var p = CameraScore.PoseAt(b);
                Assert.That(p.Fov, Is.EqualTo(first.Fov), $"beat {b}");
                Assert.That(p.Distance, Is.EqualTo(first.Distance), $"beat {b}");
                Assert.That(p.Yaw, Is.EqualTo(first.Yaw), $"beat {b}");
            }
        }

        [Test]
        public void EaseReachesTargetThenCutsToNextCue()
        {
            // イントロの寄り: 拍 4〜8 で距離 3.4 → 2.6。拍 8 で A の距離 1.9 にカット
            Assert.That(CameraScore.PoseAt(4.0).Distance, Is.EqualTo(3.4).Within(Eps));
            Assert.That(CameraScore.PoseAt(6.0).Distance, Is.EqualTo(3.0).Within(Eps), "SmoothStep の中点は線形の中点と一致する");
            Assert.That(CameraScore.PoseAt(7.999).Distance, Is.EqualTo(2.6).Within(1e-3));
            Assert.That(CameraScore.PoseAt(8.0).Distance, Is.EqualTo(1.9).Within(Eps));
        }

        [Test]
        public void ChorusRushLandsAndStaysStill()
        {
            // 拍 74〜74.5 で FOV 55 → 14 に急接近し、以後 80 直前まで bit 一致で不変
            var prev = CameraScore.PoseAt(74.0).Fov;
            Assert.That(prev, Is.EqualTo(55.0).Within(Eps));
            for (var b = 74.0 + 1.0 / 32; b <= 74.5; b += 1.0 / 32)
            {
                var fov = CameraScore.PoseAt(b).Fov;
                Assert.That(fov, Is.LessThan(prev), $"beat {b}");
                prev = fov;
            }
            var landed = CameraScore.PoseAt(74.5);
            Assert.That(landed.Fov, Is.EqualTo(14.0).Within(Eps));
            for (var b = 74.5; b < 80.0; b += 1.0 / 16)
            {
                var p = CameraScore.PoseAt(b);
                Assert.That(p.Distance, Is.EqualTo(landed.Distance), $"beat {b}");
                Assert.That(p.Pitch, Is.EqualTo(landed.Pitch), $"beat {b}");
                Assert.That(p.Fov, Is.EqualTo(landed.Fov), $"beat {b}");
            }
        }

        [Test]
        public void CycleRepeatsFromPhraseAAndSkipsIntro()
        {
            var a = CameraScore.PoseAt(8.0);
            var wrapped = CameraScore.PoseAt(8.0 + CameraScore.CycleBeats);
            Assert.That(wrapped.Distance, Is.EqualTo(a.Distance));
            Assert.That(wrapped.Yaw, Is.EqualTo(a.Yaw));
            Assert.That(wrapped.Fov, Is.EqualTo(a.Fov));
            Assert.That(wrapped.Target, Is.EqualTo(a.Target));
            Assert.That(wrapped.Distance, Is.Not.EqualTo(CameraScore.FixedPose.Distance), "イントロには戻らない");

            var x = CameraScore.PoseAt(50.0);
            var y = CameraScore.PoseAt(50.0);
            Assert.That(y.Yaw, Is.EqualTo(x.Yaw));
            Assert.That(y.Pitch, Is.EqualTo(x.Pitch));
            Assert.That(y.Roll, Is.EqualTo(x.Roll));

            Assert.That(CameraScore.PoseAt(-2.0).Distance, Is.EqualTo(3.4).Within(Eps), "助走中はイントロの引き画");
        }

        [Test]
        public void PunchInFiresEveryBeatOnlyInChorus()
        {
            var onBeat = CameraScore.PoseAt(84.0).Fov;
            var offBeat = CameraScore.PoseAt(84.5).Fov;
            var nextBeat = CameraScore.PoseAt(85.0).Fov;
            Assert.That(onBeat, Is.EqualTo(24.0 - 1.5).Within(Eps));
            Assert.That(offBeat, Is.GreaterThan(onBeat));
            Assert.That(nextBeat, Is.EqualTo(onBeat).Within(Eps), "1 拍周期");
            Assert.That(CameraScore.PoseAt(44.0).Fov, Is.EqualTo(26.0).Within(Eps), "オービット中はパンチイン無し");
        }

        [Test]
        public void ImpactInsertLastsEighthBeatOnBarHeads()
        {
            Assert.That(CameraScore.PoseAt(96.05).Distance, Is.EqualTo(1.2).Within(Eps));
            Assert.That(CameraScore.PoseAt(96.05).Target, Is.EqualTo(CameraTarget.Head));
            Assert.That(CameraScore.PoseAt(96.2).Distance, Is.EqualTo(2.0).Within(Eps));
            Assert.That(CameraScore.PoseAt(100.05).Distance, Is.EqualTo(1.2).Within(Eps));
            Assert.That(CameraScore.PoseAt(98.05).Distance, Is.EqualTo(2.0).Within(Eps), "バーの 3 拍目には入らない");
            Assert.That(CameraScore.PoseAt(84.05).Distance, Is.EqualTo(2.2).Within(Eps), "衝撃カット無しのキュー");
        }

        [Test]
        public void ShakeIsDeterministicAndBounded()
        {
            var still = CameraScore.PoseAt(20.0);
            Assert.That(still.Yaw, Is.EqualTo(-140.0), "Shake 0 のキューはノイズ項が無い");
            Assert.That(still.Roll, Is.EqualTo(0.0));

            var moved = false;
            for (var b = 56.0; b < 64.0; b += 1.0 / 16)
            {
                var p = CameraScore.PoseAt(b);
                Assert.That(Math.Abs(p.Yaw - 60.0), Is.LessThanOrEqualTo(0.6 * 0.3 + Eps), $"beat {b}");
                Assert.That(Math.Abs(p.Roll), Is.LessThanOrEqualTo(0.4 * 0.3 + Eps), $"beat {b}");
                if (p.Yaw != 60.0) moved = true;
            }
            Assert.That(moved, "Shake 0.3 のキューではヨーが揺れる");
        }

        [Test]
        public void AllPosesRespectLimits()
        {
            for (var b = -4.0; b <= 140.0; b += 1.0 / 16)
            {
                var p = CameraScore.PoseAt(b);
                Assert.That(p.Pitch, Is.GreaterThanOrEqualTo(CameraScore.MinPitch), $"beat {b}");
                Assert.That(p.Distance, Is.GreaterThanOrEqualTo(CameraScore.MinDistance), $"beat {b}");
                Assert.That(p.Fov, Is.InRange(CameraScore.MinFov, CameraScore.MaxFov), $"beat {b}");
            }
        }
    }
}
