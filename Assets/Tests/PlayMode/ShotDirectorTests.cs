using System;
using NUnit.Framework;
using UnityEngine;

namespace BabyDance.Tests
{
    /// <summary>Humanoid ボーンが無い Animator を渡したとき、黙って動かず例外で止まることを確認する。</summary>
    public class ShotDirectorTests
    {
        [Test]
        public void TickThrowsWhenDancerHasNoHumanoidBones()
        {
            var cam = new GameObject("cam", typeof(Camera), typeof(ShotDirector));
            var dancer = new GameObject("dancer", typeof(Animator));
            try
            {
                var director = cam.GetComponent<ShotDirector>();
                director.dancer = dancer.GetComponent<Animator>();
                Assert.Throws<InvalidOperationException>(() => director.Tick(0.0));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cam);
                UnityEngine.Object.DestroyImmediate(dancer);
            }
        }
    }
}
