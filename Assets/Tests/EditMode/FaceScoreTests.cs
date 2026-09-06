using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace BabyDance.Tests
{
    public class FaceScoreTests
    {
        private const int Expression = 1; // 1, cols=3, rows=2, T=8, J=4 はすべて異なる。
        private const double Step = 0.001;
        private static FacePose Pose(double beat, int expression = Expression, double switchBeat = double.NegativeInfinity)
            => FaceScore.PoseAt(beat, expression, switchBeat);

        private static List<double> Starts()
        {
            var starts = new List<double>();
            var wasOpen = true;
            for (var i = -2000; i <= 1000000; i++)
            {
                var b = i * Step;
                var open = Pose(b).EyeFrame == Expression;
                if (wasOpen && !open) starts.Add(b);
                wasOpen = open;
            }
            return starts;
        }

        [Test]
        public void BlinkIntervalsHaveNonzeroVariance()
        {
            var starts = Starts();
            Assert.That(starts.Count, Is.GreaterThan(2));
            double min = double.MaxValue, max = 0;
            for (var i = 1; i < starts.Count; i++)
            {
                var interval = starts[i] - starts[i - 1];
                min = Math.Min(min, interval); max = Math.Max(max, interval);
            }
            Assert.That(max - min, Is.GreaterThan(0.1), "発火間隔の分散が必要");
            TestContext.WriteLine($"blink interval min={min:R} max={max:R}");
        }

        [Test]
        public void BlinkFiresOncePerPeriodAcrossThousandBeats()
        {
            Assert.That(FaceScore.Interval, Is.EqualTo(8));
            Assert.That(FaceScore.Jitter, Is.EqualTo(4));
            Assert.That(FaceScore.Jitter + 0.5, Is.LessThan(FaceScore.Interval));
            var starts = Starts();
            Assert.That(starts.Count, Is.EqualTo(125));
            TestContext.WriteLine($"blink starts [-2,1000]={starts.Count}");
        }

        [Test]
        public void EveryBlinkHasPointThreeBeatsOfClosedEyes()
        {
            var starts = Starts();
            Assert.That(starts.Count, Is.EqualTo(125));
            foreach (var start in starts)
            {
                var closed = 0;
                for (var i = 0; i < 600; i++)
                    if (Pose(start + i * Step).EyeFrame == 5) closed++;
                Assert.That(closed * Step, Is.EqualTo(0.3).Within(0.002), $"start={start:R}");
            }
        }

        [Test]
        public void NegativeBeatsUseFloorAndKeepMouthPhase()
        {
            // -2 から必ず走査。負の小数拍は正の同位相と一致する。
            for (var i = -2000; i < 0; i++)
            {
                var b = i * Step;
                var f = b - Math.Floor(b);
                var expected = f < 0.15 ? 2 : f < 0.35 ? 1 : 0;
                Assert.That(Pose(b).MouthFrame, Is.EqualTo(expected), $"beat={b:R}");
                Assert.That(Pose(b).EyeFrame, Is.EqualTo(Expression));
            }
            // -2..0 では通常瞬きが無い。負の周期の発火自体も固定ベクトルで検査する。
            Assert.That(Pose(-4.801403835022011).EyeFrame, Is.EqualTo(5));
        }

        [Test]
        public void AtlasRowsAreTopToBottomForEveryFrame()
        {
            foreach (var size in new[] { (3, 2), (4, 2), (3, 1) })
                for (var row = 0; row < size.Item2; row++)
                    for (var col = 0; col < size.Item1; col++)
                    {
                        var r = FaceScore.AtlasRect(row * size.Item1 + col, size.Item1, size.Item2);
                        Assert.That(r.X, Is.EqualTo(1.0 / size.Item1).Within(1e-12));
                        Assert.That(r.Y, Is.EqualTo(1.0 / size.Item2).Within(1e-12));
                        Assert.That(r.Z, Is.EqualTo((double)col / size.Item1).Within(1e-12));
                        Assert.That(r.W, Is.EqualTo((double)(size.Item2 - row - 1) / size.Item2).Within(1e-12));
                    }
        }

        [Test]
        public void SwitchingForcesSharedBlinkImmediately()
        {
            const double change = 5.25;
            Assert.That(Pose(change).EyeFrame, Is.EqualTo(Expression), "通常瞬きが無い入力");
            Assert.That(Pose(change, 3, change).EyeFrame, Is.EqualTo(4));
            Assert.That(Pose(change + 0.05, 3, change).EyeFrame, Is.EqualTo(4));
            Assert.That(Pose(change + 0.2, 3, change).EyeFrame, Is.EqualTo(5));
            Assert.That(Pose(change + 0.45, 3, change).EyeFrame, Is.EqualTo(4));
            Assert.That(Pose(change + 0.5, 3, change).EyeFrame, Is.EqualTo(3));
        }

        [Test]
        public void ExpressionsChangeOnlyOpenEyes()
        {
            var shared = 0;
            var open = 0;
            for (var i = -2000; i <= 32000; i++)
            {
                var b = i * Step;
                var a = Pose(b, 1); var z = Pose(b, 3);
                if (a.EyeFrame == 1) { Assert.That(z.EyeFrame, Is.EqualTo(3)); open++; }
                else { Assert.That(a.EyeFrame, Is.EqualTo(4).Or.EqualTo(5)); Assert.That(z.EyeFrame, Is.EqualTo(a.EyeFrame)); shared++; }
                Assert.That(z.MouthFrame, Is.EqualTo(a.MouthFrame));
            }
            Assert.That(shared, Is.GreaterThan(0)); Assert.That(open, Is.GreaterThan(0));
        }

        [Test]
        public void MouthAlwaysStaysInItsThreeFrames()
        {
            var counts = new int[3];
            for (var i = -2000; i <= 1000000; i++)
            {
                var frame = Pose(i * Step, 3).MouthFrame;
                Assert.That(frame, Is.InRange(0, 2), $"beat={i * Step:R}");
                counts[frame]++;
            }
            foreach (var count in counts) Assert.That(count, Is.GreaterThan(0));
            TestContext.WriteLine($"mouth frame counts={string.Join(",", counts)}");
        }

        [Test]
        public void MouthUsesExactBeatThresholds()
        {
            Assert.That(Pose(0).MouthFrame, Is.EqualTo(2));
            Assert.That(Pose(0.149999).MouthFrame, Is.EqualTo(2));
            Assert.That(Pose(0.15).MouthFrame, Is.EqualTo(1));
            Assert.That(Pose(0.349999).MouthFrame, Is.EqualTo(1));
            Assert.That(Pose(0.35).MouthFrame, Is.EqualTo(0));
            Assert.That(Pose(0.999999).MouthFrame, Is.EqualTo(0));
        }

        [Test]
        public void NaturalBlinkCanDelayReopeningAfterSwitch()
        {
            const double start = 2.182435421309691;
            var change = start - 0.3;
            Assert.That(Pose(change + 0.5, 3, change).EyeFrame, Is.EqualTo(5));
            Assert.That(Pose(start + 0.501, 3, change).EyeFrame, Is.EqualTo(3));
            for (var i = 0; i < 500; i++)
                Assert.That(Pose(change + i * Step, 3, change).EyeFrame, Is.EqualTo(4).Or.EqualTo(5));
        }
    }
}
