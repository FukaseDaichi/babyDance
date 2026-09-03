using NUnit.Framework;

namespace BabyDance.Tests
{
    public class BeatClockTests
    {
        [Test]
        public void BeatAdvancesAtConstantBpm()
        {
            var clock = new BeatClock(120);
            clock.Start(10.0);
            Assert.That(clock.BeatAt(10.0), Is.EqualTo(0.0).Within(1e-9));
            Assert.That(clock.BeatAt(11.0), Is.EqualTo(2.0).Within(1e-9));
            Assert.That(clock.BeatAt(40.0), Is.EqualTo(60.0).Within(1e-9));
        }

        [Test]
        public void BeatIsNegativeBeforeStart()
        {
            var clock = new BeatClock(120);
            clock.Start(10.0);
            Assert.That(clock.BeatAt(9.0), Is.EqualTo(-2.0).Within(1e-9));
        }

        [Test]
        public void ChangingBpmKeepsPhaseContinuous()
        {
            var clock = new BeatClock(120);
            clock.Start(0.0);
            // 1 秒後 = 2 拍目で BPM を 60 に落とす
            clock.SetBpm(60, 1.0);
            Assert.That(clock.BeatAt(1.0), Is.EqualTo(2.0).Within(1e-9));
            Assert.That(clock.BeatAt(2.0), Is.EqualTo(3.0).Within(1e-9));
            Assert.That(clock.Bpm, Is.EqualTo(60.0));
        }

        [Test]
        public void StoppedClockReportsZeroAndNotRunning()
        {
            var clock = new BeatClock(120);
            Assert.That(clock.IsRunning, Is.False);
            Assert.That(clock.BeatAt(100.0), Is.EqualTo(0.0));
            clock.Start(0.0);
            Assert.That(clock.IsRunning, Is.True);
            clock.Stop();
            Assert.That(clock.IsRunning, Is.False);
            Assert.That(clock.BeatAt(100.0), Is.EqualTo(0.0));
        }

        [Test]
        public void SetBpmWhileStoppedOnlyChangesRate()
        {
            var clock = new BeatClock(120);
            clock.SetBpm(90, 5.0);
            clock.Start(0.0);
            Assert.That(clock.BeatAt(2.0), Is.EqualTo(3.0).Within(1e-9));
        }
    }
}
