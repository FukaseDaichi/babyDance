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

        public const double IntroEnd = 8.0;
        public const double CycleBeats = 128.0;

        public static readonly CameraPose FixedPose = new CameraPose(4.2, 0, 8, 30, CameraTarget.Hips);

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

            /// <summary>移動拍数を過ぎたら終端姿勢で完全静止する（移動と停止のメリハリ）。</summary>
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

        // 譜面。拍 8 以降は 128 拍で繰り返す（8〜136）。曲構造は分からないので擬似的なイントロ / A / B / サビ / アウトロを作る。
        private static readonly Cue[] Cues =
        {
            // イントロ（初回のみ）: 引きフィックス → だんだん寄り
            Hold(double.NegativeInfinity, 4, P(4.2, 0, 8, 30, CameraTarget.Hips)),
            Move(4, 8, P(4.2, 0, 8, 30, CameraTarget.Hips), P(3.2, 0, 8, 30, CameraTarget.Hips), 4, Ease.Smooth),
            // A: 8 拍カット割り、最後の 2 拍だけ顔クローズアップ
            Hold(8, 16, P(2.4, 35, 4, 24, CameraTarget.Head)),
            Hold(16, 24, P(2.8, -140, 10, 26, CameraTarget.Hips)),
            Hold(24, 30, P(3.0, 0, 6, 26, CameraTarget.Hips)),
            Hold(30, 32, P(1.6, 0, 0, 10, CameraTarget.Head)),
            Hold(32, 40, P(3.2, -20, 6, 28, CameraTarget.Hips)),
            // B: オービット → 停止 → サビ前のタメ
            Move(40, 56, P(3.4, -60, 5, 26, CameraTarget.Hips, 0, 0.3), P(3.4, 60, 5, 26, CameraTarget.Hips, 0, 0.3), 16, Ease.Linear),
            Hold(56, 64, P(3.4, 60, 5, 26, CameraTarget.Hips, 0, 0.3)),
            Hold(64, 72, P(3.6, 0, 8, 28, CameraTarget.Hips)),
            // サビ: 足元 → アオリ広角から顔へ急接近して静止 → パンチイン → 衝撃カット
            Hold(72, 74, P(2.0, 0, -20, 18, CameraTarget.Feet, 0, 0.2)),
            Move(74, 80, P(3.6, 0, -20, 55, CameraTarget.Head), P(1.8, 0, -8, 14, CameraTarget.Head), 0.5, Ease.Smooth),
            Hold(80, 96, P(2.8, 15, 4, 24, CameraTarget.Hips, 0, 0.4), punch: true),
            Hold(96, 104, P(2.6, -45, 2, 22, CameraTarget.Head, 0, 0.4), punch: true, impact: true),
            // アウトロ: アオリ気味のバストアップからゆっくり引いて完全停止
            Move(104, 120, P(2.4, 25, -12, 26, CameraTarget.Hips, 4, 0), P(4.2, 25, -12, 30, CameraTarget.Hips, 0, 0), 16, Ease.Smooth),
            Hold(120, 136, P(4.2, 25, -12, 30, CameraTarget.Hips)),
        };

        private const double PunchFov = 1.5;
        private const double PunchHalfLifeBeats = 0.15;
        private const double ImpactBeats = 0.125;
        private const double ImpactDistance = 1.4;
        private const double ImpactFov = 12.0;
        private const double ShakeAngle = 0.6;
        private const double ShakeRoll = 0.4;
        private const double ShakeRate = 3.1;

        /// <summary>合成順: キュー評価 → 衝撃カット → パンチイン → 手ブレ → Clamp。</summary>
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

        public static CameraPose Clamp(CameraPose p)
        {
            p.Distance = Math.Max(MinDistance, p.Distance);
            p.Pitch = Math.Max(MinPitch, p.Pitch);
            p.Fov = Math.Min(MaxFov, Math.Max(MinFov, p.Fov));
            return p;
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
    }
}
