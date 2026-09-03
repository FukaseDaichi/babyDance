#if UNITY_EDITOR
using System;
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;

namespace BabyDance.Tests
{
    /// <summary>
    /// 無効化した Animator が手動 Update でだけ進み、位相指定と 1 拍クロスフェードが効くことを確認する。
    /// State A: 1 秒で localPosition.x 0→1。State B: 1 秒で localPosition.y 0→1。どちらも 4 拍/ループ。
    /// </summary>
    public class ManualTickTests
    {
        private const string Layer = "Base Layer";
        private GameObject _go;
        private AnimatorController _controller;
        private DanceDriver _driver;
        private DanceClipInfo _a;
        private DanceClipInfo _b;

        private static AnimationClip Clip(string name, string property)
        {
            var clip = new AnimationClip { name = name };
            clip.SetCurve("", typeof(Transform), property, AnimationCurve.Linear(0f, 0f, 1f, 1f));
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            return clip;
        }

        private static DanceClipInfo Info(AnimationClip clip)
        {
            var info = ScriptableObject.CreateInstance<DanceClipInfo>();
            info.clip = clip;
            info.stateName = $"{Layer}.{clip.name}";
            info.beatsPerLoop = 4;
            return info;
        }

        [SetUp]
        public void SetUp()
        {
            _a = Info(Clip("A", "localPosition.x"));
            _b = Info(Clip("B", "localPosition.y"));

            _controller = new AnimatorController();
            _controller.AddLayer(Layer);
            var sm = _controller.layers[0].stateMachine;
            sm.AddState("A").motion = _a.clip;
            sm.AddState("B").motion = _b.clip;

            _go = new GameObject("dancer");
            _go.AddComponent<Animator>().runtimeAnimatorController = _controller;
            _driver = _go.AddComponent<DanceDriver>();
            _driver.dances = new[] { _a, _b };
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.Destroy(_go);
            UnityEngine.Object.Destroy(_controller);
            UnityEngine.Object.Destroy(_a.clip);
            UnityEngine.Object.Destroy(_b.clip);
            UnityEngine.Object.Destroy(_a);
            UnityEngine.Object.Destroy(_b);
        }

        private AnimatorStateInfo State() => _go.GetComponent<Animator>().GetCurrentAnimatorStateInfo(0);

        [UnityTest]
        public IEnumerator TickAdvancesStateAndTransformByBeatDelta()
        {
            yield return null;
            _driver.SetDance(0, 0.0);
            _driver.Tick(2.0); // 2 拍 = 0.5 秒

            Assert.That(State().normalizedTime, Is.EqualTo(0.5f).Within(0.01f));
            Assert.That(_go.transform.localPosition.x, Is.EqualTo(0.5f).Within(0.01f));
        }

        [UnityTest]
        public IEnumerator AnimatorDoesNotAdvanceOnItsOwn()
        {
            yield return null;
            _driver.SetDance(0, 0.0);
            yield return new WaitForSeconds(0.3f);

            Assert.That(State().normalizedTime, Is.EqualTo(0f).Within(0.01f));
            Assert.That(_go.transform.localPosition.x, Is.EqualTo(0f).Within(0.01f));
        }

        [UnityTest]
        public IEnumerator SetDanceStartsAtPhaseForBeat()
        {
            yield return null;
            _driver.SetDance(0, 3.0); // 3/4 ループ

            Assert.That(State().normalizedTime, Is.EqualTo(0.75f).Within(0.01f));
            Assert.That(_go.transform.localPosition.x, Is.EqualTo(0.75f).Within(0.01f));
        }

        [UnityTest]
        public IEnumerator SwitchingDanceCrossFadesWithinOneBeat()
        {
            yield return null;
            var animator = _go.GetComponent<Animator>();
            _driver.SetDance(0, 0.0);
            _driver.Tick(1.0);
            _driver.SetDance(1, 1.0);

            _driver.Tick(1.5); // 0.5 拍: 遷移中
            Assert.That(animator.IsInTransition(0), Is.True);

            _driver.Tick(2.5); // 合計 1.5 拍: 遷移完了、B が現在 State
            Assert.That(animator.IsInTransition(0), Is.False);
            Assert.That(State().IsName(_b.stateName), Is.True);
            Assert.That(_go.transform.localPosition.y, Is.GreaterThan(0.1f));
        }

        [UnityTest]
        public IEnumerator SetDanceThrowsForMissingState()
        {
            yield return null;
            var missing = Info(Clip("Missing", "localPosition.z"));
            _driver.dances = new[] { missing };

            Assert.Throws<InvalidOperationException>(() => _driver.SetDance(0, 0.0));
            UnityEngine.Object.Destroy(missing.clip);
            UnityEngine.Object.Destroy(missing);
        }
    }
}
#endif
