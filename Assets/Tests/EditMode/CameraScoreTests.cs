using System;
using NUnit.Framework;

namespace BabyDance.Tests
{
    public class CameraScoreTests
    {
        private const double Eps = 1e-9;

        /// <summary>姿勢から「画面に収まる高さ [m]」を独立に計算する。譜面の意図はこの量で書かれている。</summary>
        private static double FrameHeight(CameraPose p) => 2.0 * p.Distance * Math.Tan(p.Fov * Math.PI / 360.0);

        [Test]
        public void DistanceIsDerivedFromFrameHeightAndFov()
        {
            // h = 2 d tan(fov/2) の逆算。fov 30 で 1 m を収めるには tan15 の逆数の半分だけ離れる。
            Assert.That(CameraScore.Distance(1.0, 30.0), Is.EqualTo(1.8660254).Within(1e-6));
            Assert.That(CameraScore.Distance(0.32, 10.0), Is.EqualTo(1.8288084).Within(1e-6));
            Assert.That(FrameHeight(new CameraPose(CameraScore.Distance(0.55, 24.0), 0, 0, 24.0, CameraTarget.Head)),
                Is.EqualTo(0.55).Within(1e-9), "往復して元のフレーム高に戻る");
        }

        [Test]
        public void EveryShotIsSizedForThisCharacter()
        {
            // 全高 0.8 m のキャラを、画面外にも顔の一部だけにもしない。
            var widest = 0.0;
            var tightest = double.MaxValue;
            for (var b = -4.0; b <= 140.0; b += 1.0 / 16)
            {
                var h = FrameHeight(CameraScore.PoseAt(b));
                Assert.That(h, Is.InRange(0.25, 2.0), $"beat {b}");
                widest = Math.Max(widest, h);
                tightest = Math.Min(tightest, h);
            }
            Assert.That(widest, Is.GreaterThanOrEqualTo(CameraScore.CharacterHeight),
                "全身が収まるショットが 1 つも無い");
            Assert.That(tightest, Is.LessThanOrEqualTo(CameraScore.CharacterHeight * 0.5),
                "寄りのショットが 1 つも無い");
        }

        [Test]
        public void HeadShotsFrameTheHeadNotTheWholeBody()
        {
            // 頭（耳込み 0.37 m）を狙うキューで全身が入るようなら、それは寄りになっていない。
            foreach (var beat in new[] { 8.0, 30.0, 79.0, 100.0 })
            {
                var p = CameraScore.PoseAt(beat);
                Assert.That(p.Target, Is.EqualTo(CameraTarget.Head), $"beat {beat}");
                Assert.That(FrameHeight(p), Is.LessThanOrEqualTo(0.6), $"beat {beat}");
            }
        }

        [Test]
        public void LiftAboveGroundKeepsCameraOverTheFloor()
        {
            // 足元アオリ: 注視点 0.05 m・距離 0.884 m・ピッチ -20 度だと y = -0.25 m まで潜る。
            var sunk = 0.05 + 0.884 * Math.Sin(-20.0 * Math.PI / 180.0);
            Assert.That(sunk, Is.LessThan(0.0), "補正前は床下");

            var lifted = CameraScore.LiftAboveGround(-20.0, 0.884, 0.05);
            var y = 0.05 + 0.884 * Math.Sin(lifted * Math.PI / 180.0);
            Assert.That(y, Is.EqualTo(CameraScore.MinCameraHeight).Within(1e-9), "ちょうど下限に乗る");
            Assert.That(lifted, Is.GreaterThan(-20.0).And.LessThan(0.0), "水平より下は保つ");

            // 潜らない姿勢は素通し
            Assert.That(CameraScore.LiftAboveGround(-20.0, 0.5, 1.0), Is.EqualTo(-20.0).Within(1e-9));
            Assert.That(CameraScore.LiftAboveGround(8.0, 1.87, 0.21), Is.EqualTo(8.0).Within(1e-9), "見下ろしは触らない");

            // 注視点が下限より低くても破綻しない
            Assert.That(CameraScore.LiftAboveGround(-20.0, 1.0, 0.0), Is.GreaterThan(0.0));
        }

        [Test]
        public void CameraNeverGoesUnderTheFloorAtAnyBeat()
        {
            // 実行時に取りうる注視点の高さ（足元 / 腰 / 頭）を総当たりする。
            foreach (var targetHeight in new[] { 0.02, 0.05, 0.12, 0.18, 0.21, 0.28, 0.50, 0.56, 0.62 })
            {
                for (var b = -4.0; b <= 140.0; b += 1.0 / 16)
                {
                    var pose = CameraScore.PoseAt(b);
                    var o = CameraScore.Offset(pose, targetHeight);
                    Assert.That(targetHeight + o.Y, Is.GreaterThanOrEqualTo(CameraScore.MinCameraHeight - 1e-9),
                        $"beat {b} target {targetHeight}");

                    var length = Math.Sqrt(o.X * o.X + o.Y * o.Y + o.Z * o.Z);
                    Assert.That(length, Is.EqualTo(pose.Distance).Within(1e-9),
                        $"補正しても距離＝フレーム高は変えない beat {b} target {targetHeight}");
                }
            }
        }

        [Test]
        public void ClampLimitsPitchDistanceAndFov()
        {
            var low = CameraScore.Clamp(new CameraPose(0.1, 12, -30, 5, CameraTarget.Hips));
            Assert.That(low.Distance, Is.EqualTo(0.45).Within(Eps));
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
            // イントロの寄り: 拍 4〜8 でフレーム高 1.00 → 0.80 m。拍 8 で A のミディアム 0.55 m にカット
            Assert.That(FrameHeight(CameraScore.PoseAt(4.0)), Is.EqualTo(1.00).Within(1e-9));
            Assert.That(FrameHeight(CameraScore.PoseAt(6.0)), Is.EqualTo(0.90).Within(1e-9), "SmoothStep の中点は線形の中点と一致する");
            Assert.That(FrameHeight(CameraScore.PoseAt(7.999)), Is.EqualTo(0.80).Within(1e-3));
            Assert.That(FrameHeight(CameraScore.PoseAt(8.0)), Is.EqualTo(0.55).Within(1e-9));
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

            Assert.That(FrameHeight(CameraScore.PoseAt(-2.0)), Is.EqualTo(1.00).Within(1e-9), "助走中はイントロの引き画");
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
            var impact = CameraScore.Distance(0.30, 12.0);   // 超クローズ
            var medium = CameraScore.Distance(0.55, 22.0);   // 通常のキュー
            Assert.That(CameraScore.PoseAt(96.05).Distance, Is.EqualTo(impact).Within(Eps));
            Assert.That(CameraScore.PoseAt(96.05).Target, Is.EqualTo(CameraTarget.Head));
            Assert.That(CameraScore.PoseAt(96.2).Distance, Is.EqualTo(medium).Within(Eps));
            Assert.That(CameraScore.PoseAt(100.05).Distance, Is.EqualTo(impact).Within(Eps));
            Assert.That(CameraScore.PoseAt(98.05).Distance, Is.EqualTo(medium).Within(Eps), "バーの 3 拍目には入らない");
            Assert.That(CameraScore.PoseAt(84.05).Distance, Is.EqualTo(CameraScore.Distance(0.55, 24.0)).Within(Eps), "衝撃カット無しのキュー");
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
