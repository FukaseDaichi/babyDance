# 拍駆動カメラディレクション Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** ダンスの拍位置だけを入力に、MV 風のカット割り・急接近・回り込み・パンチインを Main Camera に適用する。

**Architecture:** `CameraScore.PoseAt(beat)` が Unity 非依存の純関数として 128 拍サイクルの譜面を評価し `CameraPose` を返す。`ShotDirector`（MonoBehaviour）がそれを球面座標で Main Camera に適用し、注視点は Animator の Humanoid ボーン実座標。`DancePlayer.Update` が `driver.Tick` の直後に `director.Tick` を呼ぶ。

**Tech Stack:** Unity 6000.5.10f1 / URP / uGUI / NUnit（Unity Test Framework 1.7）。Cinemachine は使わない。

**Spec:** `docs/superpowers/specs/2026-09-05-beat-camera-direction-design.md`

## Global Constraints

- ランタイムアセンブリ `BabyDance` に `Time.deltaTime` / `Time.time` / `Random` を追加しない。時間源は呼び出し側が渡す拍のみ
- `CameraScore` / `CameraPose` は `System` 名前空間のみ使用（`UnityEngine` を参照しない）
- Unity 操作は必ず `tools/unity.sh` 経由。終了コードだけで成功と判定しない
- 新規 `.cs` を作ったら compile 後に生成される `.meta` を必ず `git add` する
- 現在のテスト件数: EditMode 16 / PlayMode 5。本計画完了後は EditMode 25 / PlayMode 6
- 譜面の数値は spec の表に従う。ただし spec からの変更 2 点: (a) Shake のノイズは Perlin ではなく正弦波の和（Unity 非依存のため）。(b) 拍 72〜74 の足元アップはピッチ −20・Shake 0.2 にし、clamp が実際に効く箇所を作る
- 後方互換・フォールバックを追加しない。ボーンが無ければ例外で止める

---

### Task 1: CameraPose と CameraScore.Clamp

**Files:**
- Create: `Assets/Scripts/CameraPose.cs`
- Create: `Assets/Scripts/CameraScore.cs`
- Test: `Assets/Tests/EditMode/CameraScoreTests.cs`

**Interfaces:**
- Produces: `enum CameraTarget { Hips, Head, Feet }`、`struct CameraPose { double Distance, Yaw, Pitch, Fov, Roll, Shake; CameraTarget Target; }`、`static CameraPose CameraScore.Clamp(CameraPose)`、定数 `MinPitch=-20, MinDistance=1.0, MinFov=10, MaxFov=55`

- [ ] **Step 1: 失敗するテストを書く**

```csharp
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
```

- [ ] **Step 2: 失敗を確認**

Run: `tools/unity.sh compile`
Expected: FAIL with `error CS0246` (CameraScore / CameraPose が無い)

- [ ] **Step 3: 最小実装**

`Assets/Scripts/CameraPose.cs`:

```csharp
namespace BabyDance
{
    public enum CameraTarget { Hips, Head, Feet }

    /// <summary>1 フレームぶんのカメラ姿勢。注視点を中心とした球面座標で表す。Unity 非依存。</summary>
    public struct CameraPose
    {
        public double Distance;
        /// <summary>度。0 がキャラ正面（+Z 側）、正で時計回り。</summary>
        public double Yaw;
        /// <summary>度。正で上から見下ろす。</summary>
        public double Pitch;
        public double Fov;
        public double Roll;
        /// <summary>手ブレの強さ 0〜1。</summary>
        public double Shake;
        public CameraTarget Target;

        public CameraPose(double distance, double yaw, double pitch, double fov, CameraTarget target, double roll = 0.0, double shake = 0.0)
        {
            Distance = distance;
            Yaw = yaw;
            Pitch = pitch;
            Fov = fov;
            Target = target;
            Roll = roll;
            Shake = shake;
        }
    }
}
```

`Assets/Scripts/CameraScore.cs`（この時点では Clamp と定数のみ）:

```csharp
using System;

namespace BabyDance
{
    /// <summary>拍位置をカメラ姿勢に写す純関数。Unity 非依存。</summary>
    public static class CameraScore
    {
        public const double MinPitch = -20.0;
        public const double MinDistance = 1.0;
        public const double MinFov = 10.0;
        public const double MaxFov = 55.0;

        public static CameraPose Clamp(CameraPose p)
        {
            p.Distance = Math.Max(MinDistance, p.Distance);
            p.Pitch = Math.Max(MinPitch, p.Pitch);
            p.Fov = Math.Min(MaxFov, Math.Max(MinFov, p.Fov));
            return p;
        }
    }
}
```

- [ ] **Step 4: 通ることを確認**

Run: `tools/unity.sh compile && tools/unity.sh test EditMode 17`
Expected: `OK EditMode: 17/17 passed`

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/CameraPose.cs Assets/Scripts/CameraPose.cs.meta Assets/Scripts/CameraScore.cs Assets/Scripts/CameraScore.cs.meta Assets/Tests/EditMode/CameraScoreTests.cs Assets/Tests/EditMode/CameraScoreTests.cs.meta
git commit -m "feat: CameraPose と CameraScore.Clamp を追加"
```

---

### Task 2: 譜面のキュー評価（カット・イージング・サイクル）

**Files:**
- Modify: `Assets/Scripts/CameraScore.cs`
- Test: `Assets/Tests/EditMode/CameraScoreTests.cs`

**Interfaces:**
- Produces: `static CameraPose CameraScore.PoseAt(double beat)`、`static readonly CameraPose CameraScore.FixedPose`、`const double IntroEnd = 8, CycleBeats = 128`

- [ ] **Step 1: 失敗するテストを 4 本追加**

```csharp
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
```

- [ ] **Step 2: 失敗を確認**

Run: `tools/unity.sh compile`
Expected: FAIL with `error CS0117` (`PoseAt` / `FixedPose` / `CycleBeats` が無い)

- [ ] **Step 3: 譜面とキュー評価を実装**

`CameraScore` に追記（Clamp はそのまま）:

```csharp
        public const double IntroEnd = 8.0;
        public const double CycleBeats = 128.0;

        public static readonly CameraPose FixedPose = new CameraPose(3.4, 0, 8, 30, CameraTarget.Hips);

        private enum Ease { Cut, Smooth, Linear }

        private readonly struct Cue
        {
            public readonly double Start;
            public readonly double End;
            public readonly CameraPose From;
            public readonly CameraPose To;
            public readonly double MoveBeats;
            public readonly Ease Ease;
            public readonly bool Punch;
            public readonly bool Impact;

            public Cue(double start, double end, CameraPose from, CameraPose to, double moveBeats, Ease ease, bool punch, bool impact)
            {
                Start = start; End = end; From = from; To = to; MoveBeats = moveBeats; Ease = ease; Punch = punch; Impact = impact;
            }

            public CameraPose Evaluate(double beat)
            {
                if (MoveBeats <= 0.0) return To;
                var t = Math.Min(1.0, Math.Max(0.0, (beat - Start) / MoveBeats));
                if (Ease == Ease.Smooth) t = t * t * (3.0 - 2.0 * t);
                return Lerp(From, To, t);
            }
        }

        private static Cue Hold(double start, double end, CameraPose pose, bool punch = false, bool impact = false)
            => new Cue(start, end, pose, pose, 0.0, Ease.Cut, punch, impact);

        private static Cue Move(double start, double end, CameraPose from, CameraPose to, double moveBeats, Ease ease)
            => new Cue(start, end, from, to, moveBeats, ease, false, false);

        private static CameraPose P(double distance, double yaw, double pitch, double fov, CameraTarget target, double roll = 0.0, double shake = 0.0)
            => new CameraPose(distance, yaw, pitch, fov, target, roll, shake);

        // 譜面。拍 8 以降は 128 拍で繰り返す（8〜136）。spec の表と 1 対 1。
        private static readonly Cue[] Cues =
        {
            // イントロ（初回のみ）
            Hold(double.NegativeInfinity, 4, P(3.4, 0, 8, 30, CameraTarget.Hips)),
            Move(4, 8, P(3.4, 0, 8, 30, CameraTarget.Hips), P(2.6, 0, 8, 30, CameraTarget.Hips), 4, Ease.Smooth),
            // A
            Hold(8, 16, P(1.9, 35, 4, 24, CameraTarget.Head)),
            Hold(16, 24, P(2.1, -140, 10, 26, CameraTarget.Hips)),
            Hold(24, 30, P(2.4, 0, 6, 26, CameraTarget.Hips)),
            Hold(30, 32, P(1.6, 0, 0, 10, CameraTarget.Head)),
            Hold(32, 40, P(2.6, -20, 6, 28, CameraTarget.Hips)),
            // B
            Move(40, 56, P(2.8, -60, 5, 26, CameraTarget.Hips, 0, 0.3), P(2.8, 60, 5, 26, CameraTarget.Hips, 0, 0.3), 16, Ease.Linear),
            Hold(56, 64, P(2.8, 60, 5, 26, CameraTarget.Hips, 0, 0.3)),
            Hold(64, 72, P(3.0, 0, 8, 28, CameraTarget.Hips)),
            // サビ
            Hold(72, 74, P(1.8, 0, -20, 18, CameraTarget.Feet, 0, 0.2)),
            Move(74, 80, P(3.2, 0, -20, 55, CameraTarget.Head), P(1.5, 0, -8, 14, CameraTarget.Head), 0.5, Ease.Smooth),
            Hold(80, 96, P(2.2, 15, 4, 24, CameraTarget.Hips, 0, 0.4), punch: true),
            Hold(96, 104, P(2.0, -45, 2, 22, CameraTarget.Head, 0, 0.4), punch: true, impact: true),
            // アウトロ
            Move(104, 120, P(1.8, 25, -12, 26, CameraTarget.Hips, 4, 0), P(3.4, 25, -12, 30, CameraTarget.Hips, 0, 0), 16, Ease.Smooth),
            Hold(120, 136, P(3.4, 25, -12, 30, CameraTarget.Hips)),
        };

        public static CameraPose PoseAt(double beat)
        {
            var b = beat < IntroEnd ? beat : IntroEnd + Mod(beat - IntroEnd, CycleBeats);
            var cue = Find(b);
            var pose = cue.Evaluate(b);
            return Clamp(pose);
        }

        private static Cue Find(double b)
        {
            foreach (var cue in Cues)
                if (b >= cue.Start && b < cue.End) return cue;
            throw new InvalidOperationException($"no cue covers beat {b}");
        }

        private static double Mod(double x, double m) => x - Math.Floor(x / m) * m;

        private static CameraPose Lerp(CameraPose a, CameraPose b, double t) => new CameraPose(
            a.Distance + (b.Distance - a.Distance) * t,
            a.Yaw + (b.Yaw - a.Yaw) * t,
            a.Pitch + (b.Pitch - a.Pitch) * t,
            a.Fov + (b.Fov - a.Fov) * t,
            b.Target,
            a.Roll + (b.Roll - a.Roll) * t,
            a.Shake + (b.Shake - a.Shake) * t);
```

- [ ] **Step 4: 通ることを確認**

Run: `tools/unity.sh compile && tools/unity.sh test EditMode 21`
Expected: `OK EditMode: 21/21 passed`

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/CameraScore.cs Assets/Tests/EditMode/CameraScoreTests.cs
git commit -m "feat: CameraScore に 128 拍サイクルの譜面とキュー評価を実装"
```

---

### Task 3: パンチイン・衝撃カット・手ブレ・clamp 走査

**Files:**
- Modify: `Assets/Scripts/CameraScore.cs`
- Test: `Assets/Tests/EditMode/CameraScoreTests.cs`

**Interfaces:**
- Consumes: Task 2 の `Cue.Punch` / `Cue.Impact` / `CameraPose.Shake`
- Produces: `PoseAt` の最終形（合成順: キュー評価 → 衝撃カット → パンチイン → 手ブレ → Clamp）

- [ ] **Step 1: 失敗するテストを 4 本追加**

```csharp
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
```

ファイル冒頭に `using System;` を追加する（`Math.Abs` 用）。

- [ ] **Step 2: 失敗を確認**

Run: `tools/unity.sh compile && tools/unity.sh test EditMode 25`
Expected: `FAIL: passed 22 of 25`（PunchIn・Impact・Shake が落ちる。AllPosesRespectLimits は clamp 済みなので通る）

- [ ] **Step 3: 合成処理を実装**

`CameraScore` に定数と `PoseAt` の差し替え:

```csharp
        private const double PunchFov = 1.5;
        private const double PunchHalfLifeBeats = 0.15;
        private const double ImpactBeats = 0.125;
        private const double ImpactDistance = 1.2;
        private const double ImpactFov = 12.0;
        private const double ShakeAngle = 0.6;
        private const double ShakeRoll = 0.4;
        private const double ShakeRate = 3.1;

        public static CameraPose PoseAt(double beat)
        {
            var b = beat < IntroEnd ? beat : IntroEnd + Mod(beat - IntroEnd, CycleBeats);
            var cue = Find(b);
            var pose = cue.Evaluate(b);

            if (cue.Impact && Mod(b, 4.0) < ImpactBeats)
            {
                pose.Distance = ImpactDistance;
                pose.Fov = ImpactFov;
                pose.Target = CameraTarget.Head;
            }

            if (cue.Punch)
                pose.Fov -= PunchFov * Math.Pow(0.5, Mod(b, 1.0) / PunchHalfLifeBeats);

            if (pose.Shake > 0.0)
            {
                var x = b * ShakeRate;
                pose.Yaw += Noise(x) * ShakeAngle * pose.Shake;
                pose.Pitch += Noise(x + 100.0) * ShakeAngle * pose.Shake;
                pose.Roll += Noise(x + 200.0) * ShakeRoll * pose.Shake;
            }

            return Clamp(pose);
        }

        /// <summary>[-1, 1] に収まる滑らかな決定的ノイズ。乱数も Unity も使わない。</summary>
        private static double Noise(double x)
            => 0.5 * Math.Sin(2.0 * x) + 0.35 * Math.Sin(5.3 * x + 1.7) + 0.15 * Math.Sin(11.1 * x + 4.2);
```

- [ ] **Step 4: 通ることを確認**

Run: `tools/unity.sh test EditMode 25`
Expected: `OK EditMode: 25/25 passed`

- [ ] **Step 5: clamp が実際に効いていることを否定で示す**

`Clamp` の `p.Pitch = Math.Max(...)` 行を一時的にコメントアウトし `tools/unity.sh test EditMode 25` を走らせ、`AllPosesRespectLimits` が拍 72〜74 で落ちることを確認する。確認後に元へ戻し、再度 25/25 を確認する。

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/CameraScore.cs Assets/Tests/EditMode/CameraScoreTests.cs
git commit -m "feat: CameraScore にパンチイン・衝撃カット・手ブレを合成"
```

---

### Task 4: ShotDirector（姿勢の適用）

**Files:**
- Create: `Assets/Scripts/ShotDirector.cs`
- Test: `Assets/Tests/PlayMode/ShotDirectorTests.cs`

**Interfaces:**
- Consumes: `CameraScore.PoseAt(double)`, `CameraScore.FixedPose`, `CameraPose`, `CameraTarget`
- Produces: `sealed class ShotDirector : MonoBehaviour { public Animator dancer; public bool Fixed; public void Tick(double beat); }`

- [ ] **Step 1: 失敗する PlayMode テストを書く**

```csharp
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
```

- [ ] **Step 2: 失敗を確認**

Run: `tools/unity.sh compile`
Expected: FAIL with `error CS0246` (`ShotDirector` が無い)

- [ ] **Step 3: 実装**

```csharp
using System;
using UnityEngine;

namespace BabyDance
{
    /// <summary>
    /// CameraScore の姿勢を Main Camera に適用する。時間源は呼び出し側が渡す拍のみ。
    /// 注視点は Humanoid ボーンの実座標。唯一の状態は注視点の指数平滑で、拍差分で進める。
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class ShotDirector : MonoBehaviour
    {
        private const double TargetSmoothingBeats = 0.25;
        private const float HeadOffset = 0.1f;

        public Animator dancer;

        /// <summary>真なら譜面を無視し FixedPose に置く（デバッグ・観察用）。</summary>
        public bool Fixed;

        private Camera _camera;
        private Transform _hips;
        private Transform _head;
        private Transform _leftFoot;
        private Transform _rightFoot;
        private Vector3 _smoothedTarget;
        private CameraTarget _lastTarget;
        private bool _hasTarget;
        private double _lastBeat;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        public void Tick(double beat)
        {
            if (_hips == null) ResolveBones();

            var pose = Fixed ? CameraScore.FixedPose : CameraScore.PoseAt(beat);
            var raw = TargetPosition(pose.Target);
            var deltaBeats = beat - _lastBeat;
            _lastBeat = beat;

            if (!_hasTarget || pose.Target != _lastTarget || deltaBeats <= 0.0)
            {
                _smoothedTarget = raw;
                _hasTarget = true;
                _lastTarget = pose.Target;
            }
            else
            {
                var k = 1.0 - Math.Exp(-deltaBeats / TargetSmoothingBeats);
                _smoothedTarget = Vector3.Lerp(_smoothedTarget, raw, (float)k);
            }

            Apply(pose, _smoothedTarget);
        }

        private void ResolveBones()
        {
            if (dancer == null) throw new InvalidOperationException($"{Log.Tag} ShotDirector.dancer is not set");
            _hips = Bone(HumanBodyBones.Hips);
            _head = Bone(HumanBodyBones.Head);
            _leftFoot = Bone(HumanBodyBones.LeftFoot);
            _rightFoot = Bone(HumanBodyBones.RightFoot);
        }

        private Transform Bone(HumanBodyBones bone)
        {
            var t = dancer.GetBoneTransform(bone);
            if (t == null) throw new InvalidOperationException($"{Log.Tag} dancer has no humanoid bone {bone}");
            return t;
        }

        private Vector3 TargetPosition(CameraTarget target)
        {
            switch (target)
            {
                case CameraTarget.Head: return _head.position + Vector3.up * HeadOffset;
                case CameraTarget.Feet: return (_leftFoot.position + _rightFoot.position) * 0.5f;
                default: return _hips.position;
            }
        }

        private void Apply(CameraPose pose, Vector3 target)
        {
            var yaw = (float)(pose.Yaw * Math.PI / 180.0);
            var pitch = (float)(pose.Pitch * Math.PI / 180.0);
            var dir = new Vector3(
                Mathf.Sin(yaw) * Mathf.Cos(pitch),
                Mathf.Sin(pitch),
                Mathf.Cos(yaw) * Mathf.Cos(pitch));
            var position = target + dir * (float)pose.Distance;
            transform.SetPositionAndRotation(
                position,
                Quaternion.LookRotation(target - position) * Quaternion.Euler(0f, 0f, (float)pose.Roll));
            _camera.fieldOfView = (float)pose.Fov;
        }
    }
}
```

- [ ] **Step 4: 通ることを確認**

Run: `tools/unity.sh compile && tools/unity.sh test PlayMode 6`
Expected: `OK PlayMode: 6/6 passed`

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/ShotDirector.cs Assets/Scripts/ShotDirector.cs.meta Assets/Tests/PlayMode/ShotDirectorTests.cs Assets/Tests/PlayMode/ShotDirectorTests.cs.meta
git commit -m "feat: ShotDirector で CameraScore の姿勢を Main Camera に適用"
```

---

### Task 5: 配線（DancePlayer・DanceUi・SceneBuilder）とシーン再生成

**Files:**
- Modify: `Assets/Scripts/DancePlayer.cs`
- Modify: `Assets/Scripts/DanceUi.cs`
- Modify: `Assets/Editor/SceneBuilder.cs`
- Regenerate: `Assets/Scenes/Dance.unity`

**Interfaces:**
- Consumes: `ShotDirector.Tick(double)`, `ShotDirector.Fixed`, `ShotDirector.dancer`
- Produces: `DancePlayer.director`（public フィールド）、`DancePlayer.CameraAuto`（bool）、`DancePlayer.ToggleCamera()`

- [ ] **Step 1: DancePlayer を変更**

フィールド追加と `Update` の差し替え、公開 API 追加:

```csharp
        public AudioLoader audio;
        public DanceDriver driver;
        public ShotDirector director;
```

```csharp
        public bool CameraAuto => !director.Fixed;
```

```csharp
        private void Update()
        {
            if (!_clock.IsRunning)
            {
                director.Tick(0.0);
                return;
            }
            if (!audio.IsPlaying)
            {
                StopPlayback();
                return;
            }
            var beat = _clock.BeatAt(AudioSettings.dspTime);
            driver.Tick(beat);
            director.Tick(beat);
        }
```

```csharp
        public void ToggleCamera()
        {
            director.Fixed = !director.Fixed;
            Changed?.Invoke();
        }
```

- [ ] **Step 2: DanceUi にトグルを追加**

フィールド:

```csharp
        private Text _cameraLabel;
```

`Start` の `_danceDropdown = ...` の直後:

```csharp
            _cameraLabel = AddButton(panel, "", player.ToggleCamera).GetComponentInChildren<Text>();
```

`Refresh` の末尾:

```csharp
            _cameraLabel.text = player.CameraAuto ? "Camera: Auto" : "Camera: Fixed";
```

- [ ] **Step 3: SceneBuilder で配線**

`var driver = character.AddComponent<DanceDriver>(); driver.dances = dances;` の直後:

```csharp
            var director = cameraGo.AddComponent<ShotDirector>();
            director.dancer = character.GetComponent<Animator>();
```

`player.driver = driver;` の直後:

```csharp
            player.director = director;
```

- [ ] **Step 4: コンパイル・全テスト・シーン再生成**

Run:
```bash
tools/unity.sh compile && tools/unity.sh test EditMode 25 && tools/unity.sh test PlayMode 6 && tools/unity.sh exec BabyDance.Editor.SceneBuilder.Build
```
Expected: 各行 `OK`。続けて `grep -c "ShotDirector\|m_Script" Assets/Scenes/Dance.unity` ではなく、`git diff --stat Assets/Scenes/Dance.unity` で差分があること、`grep -n "director:" Assets/Scenes/Dance.unity` が `fileID: 0` 以外を指していることを確認する。

- [ ] **Step 5: WebGL ビルドとブラウザ確認**

Run: `tools/unity.sh build`
Expected: `OK build`

`Builds/WebGL` を `uv run` の簡易 HTTP サーバー（`uv run --with rangehttpserver python -m RangeHTTPServer 8080` など）で配信し、ブラウザで開く。`tools/` のメトロノーム音源生成スクリプトで作った 120 BPM WAV を「Open music」で読み、Play。確認項目:

1. 再生前と助走 1 秒の間は従来と同じ引き画（距離 4.2）
2. 拍 8・16・24 のクリック音と同時にカットが切り替わる
3. 拍 30（開始から約 16 秒）で 1 秒間だけ顔クローズアップ
4. 拍 72〜80（36〜40 秒）で足元 → 広角アオリからの急接近 → 静止
5. 「Camera: Auto」ボタンで「Camera: Fixed」に切り替わり、引き画に戻る。再度押すと譜面に復帰
6. ブラウザコンソールに例外が無い

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/DancePlayer.cs Assets/Scripts/DanceUi.cs Assets/Editor/SceneBuilder.cs Assets/Scenes/Dance.unity
git commit -m "feat: ShotDirector を DancePlayer に配線し Camera Auto/Fixed トグルを追加"
```

---

### Task 6: ドキュメント更新

**Files:**
- Modify: `docs/design.md`
- Modify: `docs/backlog.md`
- Modify: `README.md:35`
- Modify: `docs/superpowers/specs/2026-09-05-beat-camera-direction-design.md`

- [ ] **Step 1: design.md にカメラの節を追加**

「同期方式」の直後に節を追加:

```markdown
## カメラワーク

**カメラの姿勢も拍位置の純関数である。** `CameraScore.PoseAt(beat)` が 128 拍サイクルの譜面（イントロ / A / B / サビ / アウトロの擬似構造）を評価し、距離・ヨー・ピッチ・FOV・ロール・注視点を返す。時間源はダンスと同じ拍で、`ShotDirector` は `driver.Tick` の直後に同じ拍で呼ばれる。同じ拍を渡せば同じ画になるため、譜面は EditMode で決定的にテストできる。

移動はすべて「イージングで一気に動いて着地で止める」。移動拍数を過ぎたキューは終端姿勢で完全静止し、次のカットまで動かない。サビ相当の 32 拍では拍頭ごとに FOV を 1.5 縮めて指数的に戻すパンチイン、バーの 1 拍目には 1/8 拍だけ超アップを挟む衝撃カットを重ねる。手ブレは正弦波の和による決定的ノイズで、乱数は使わない。

注視点は Humanoid ボーン（Hips / Head / 両足の中点）の実座標を毎フレーム読み、拍単位の指数平滑（時定数 0.25 拍）で追う。注視点の種別が変わるカットでは平滑をリセットして跳ねを防ぐ。ピッチは −20° 以上、距離は 1.0 以上、FOV は 10〜55 に clamp する。

Cinemachine は採用しない。キャラは原点に固定されており Follow の価値がなく、Cinemachine のブレンドは `Time.deltaTime` 駆動で時間源を増やすため。

UI の「Camera: Auto / Fixed」で譜面を止めて引きの固定画に戻せる。カメラ演出の過剰に気づくための観察用スイッチである。
```

「構成」の表に 3 行追加:

```markdown
| `CameraPose` | カメラ姿勢の純データ（球面座標 + FOV + ロール + 注視点種別） |
| `CameraScore` | 拍 → カメラ姿勢の純関数。譜面はコード内の配列。Unity 非依存 |
| `ShotDirector` | `CameraScore` の姿勢を Main Camera に適用し、Humanoid ボーンを注視点にする |
```

「テスト方針」の EditMode の列挙に `CameraScore` の譜面評価、PlayMode に「ボーンの無い Animator で `ShotDirector` が例外で止まること」を追記する。

- [ ] **Step 2: README のテスト件数を更新**

`README.md:35` を `tools/unity.sh test EditMode 25 && tools/unity.sh test PlayMode 6` に書き換える。

- [ ] **Step 3: spec の変更点を反映**

spec の「制約」節の Perlin の記述を「正弦波の和による決定的ノイズ」に、譜面表の拍 72〜74 を「ピッチ −20、Shake 0.2」に書き換える。

- [ ] **Step 4: backlog に残課題を追記**

「次の段階の候補」に追加:

```markdown
**曲構造の検出とカメラ譜面の同期** — 現在のカメラ譜面は 128 拍固定サイクルで、実際のサビとは一致しない。音源のエネルギー変化からセクション境界を推定できれば、譜面のサビ区間をそこに合わせられる。`CameraScore.PoseAt` は拍を受けるだけなので、拍のオフセットを外から与える形になる。
```

- [ ] **Step 5: Commit**

```bash
git add docs/design.md docs/backlog.md README.md docs/superpowers/specs/2026-09-05-beat-camera-direction-design.md
git commit -m "docs: カメラワークの設計を design.md に統合し、テスト件数を更新"
```
