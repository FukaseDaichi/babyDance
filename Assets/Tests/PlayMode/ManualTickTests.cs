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
    /// State A: 1 秒で localPosition.x 0→1、beatOffset 0、4 拍/ループ（SecondsPerBeat 0.25）。
    /// State B: 2 秒で localPosition.y 0→1、beatOffset 1 拍、4 拍/ループ（SecondsPerBeat 0.5）。
    /// 長さ・offset をあえて非対称にし、秒と正規化時間の取り違え／どちらの Dance の
    /// SecondsPerBeat を使ったかの取り違えを検出できるようにしている。
    /// </summary>
    public class ManualTickTests
    {
        private const string Layer = "Base Layer";
        private GameObject _go;
        private AnimatorController _controller;
        private DanceDriver _driver;
        private DanceClipInfo _a;
        private DanceClipInfo _b;

        private static AnimationClip Clip(string name, string property, float length)
        {
            var clip = new AnimationClip { name = name };
            clip.SetCurve("", typeof(Transform), property, AnimationCurve.Linear(0f, 0f, length, 1f));
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            return clip;
        }

        private static DanceClipInfo Info(AnimationClip clip, float beatOffset)
        {
            var info = ScriptableObject.CreateInstance<DanceClipInfo>();
            info.clip = clip;
            info.stateName = $"{Layer}.{clip.name}";
            info.beatsPerLoop = 4;
            info.beatOffset = beatOffset;
            return info;
        }

        [SetUp]
        public void SetUp()
        {
            _a = Info(Clip("A", "localPosition.x", 1f), 0f);
            _b = Info(Clip("B", "localPosition.y", 2f), 1f);

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
            _driver.Tick(2.0); // 2 拍 × SecondsPerBeat(A) 0.25 = 0.5 秒

            Assert.That(State().normalizedTime, Is.EqualTo(0.5f).Within(0.01f));
            Assert.That(_go.transform.localPosition.x, Is.EqualTo(0.5f).Within(0.01f));
        }

        [UnityTest]
        public IEnumerator AnimatorDoesNotAdvanceOnItsOwn()
        {
            yield return null;
            _driver.SetDance(0, 3.0); // NormalizedTime(A, 3.0) = 3/4 = 0.75 で凍結
            yield return new WaitForSeconds(0.3f);

            Assert.That(State().normalizedTime, Is.EqualTo(0.75f).Within(0.01f));
            Assert.That(_go.transform.localPosition.x, Is.EqualTo(0.75f).Within(0.01f));
        }

        [UnityTest]
        public IEnumerator SetDanceStartsAtPhaseForBeat()
        {
            yield return null;
            _driver.SetDance(1, 3.0); // NormalizedTime(B, 3.0) = (3-1)/4 = 0.5

            Assert.That(State().normalizedTime, Is.EqualTo(0.5f).Within(0.01f));
            Assert.That(_go.transform.localPosition.y, Is.EqualTo(0.5f).Within(0.01f));
        }

        [UnityTest]
        public IEnumerator SwitchingDanceCrossFadesWithinOneBeat()
        {
            yield return null;
            var animator = _go.GetComponent<Animator>();
            _driver.SetDance(0, 0.0);
            _driver.Tick(2.0); // 2 拍 × SecondsPerBeat(A) 0.25 = 0.5 秒: A の 0.5 秒地点
            _driver.SetDance(1, 2.0); // NormalizedTime(B, 2.0) = (2-1)/4 = 0.25 → offset 0.5 秒、遷移時間 SecondsPerBeat(B) 0.5 秒

            _driver.Tick(2.5); // 0.5 拍 × 0.5 秒 = 0.25 秒経過: 遷移中（全体 0.5 秒）
            Assert.That(animator.IsInTransition(0), Is.True);

            _driver.Tick(3.5); // 追加 1.0 拍 × 0.5 秒 = 0.5 秒、遷移経過合計 0.75 秒 > 0.5 秒で完了
            Assert.That(animator.IsInTransition(0), Is.False);
            Assert.That(State().IsName(_b.stateName), Is.True);
            // B のローカル時刻 = offset 0.5 + 0.25 + 0.5 = 1.25 秒 → y = 1.25 / 2.0 = 0.625
            Assert.That(_go.transform.localPosition.y, Is.EqualTo(0.625f).Within(0.02f));
        }

        [UnityTest]
        public IEnumerator SetDanceThrowsForMissingState()
        {
            yield return null;
            var missing = Info(Clip("Missing", "localPosition.z", 1f), 0f);
            _driver.dances = new[] { missing };

            Assert.Throws<InvalidOperationException>(() => _driver.SetDance(0, 0.0));
            UnityEngine.Object.Destroy(missing.clip);
            UnityEngine.Object.Destroy(missing);
        }
    }
}
#endif
