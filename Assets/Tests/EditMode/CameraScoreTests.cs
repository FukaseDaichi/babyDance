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
    }
}
