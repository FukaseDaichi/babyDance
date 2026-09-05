using System;

namespace BabyDance
{
    /// <summary>拍位置をカメラ姿勢に写す純関数。Unity 非依存。</summary>
    public static class CameraScore
    {
        public const double MinPitch = -20.0;
        public const double MinDistance = 0.45;
        public const double MinFov = 10.0;
        public const double MaxFov = 55.0;

        public const double IntroEnd = 8.0;
        public const double CycleBeats = 128.0;

        /// <summary>キャラの全高 [m]。譜面のフレーム高はこの値を基準に決めてある。</summary>
        public const double CharacterHeight = 0.8;

        /// <summary>カメラの床からの下限 [m]。アオリでも地面を突き抜けない。</summary>
        public const double MinCameraHeight = 0.03;

        public static readonly CameraPose FixedPose = P(FullBodyLoose, 0, 8, 30, CameraTarget.Hips);

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

        /// <summary>
        /// ショットは「画面に収める高さ [m]」で指定し、距離は h = 2 d tan(fov/2) から逆算する。
        /// 距離を直接書くと、キャラの体格が変わるたびに全キューを引き直すことになる。
        /// </summary>
        private static CameraPose P(double frameHeight, double yaw, double pitch, double fov, CameraTarget target, double roll = 0.0, double shake = 0.0)
            => new CameraPose(Distance(frameHeight, fov), yaw, pitch, fov, target, roll, shake);

        public static double Distance(double frameHeight, double fov)
            => frameHeight / (2.0 * Math.Tan(fov * Math.PI / 360.0));

        // フレーム高の語彙。全高 0.8 m・頭（耳込み）0.37 m のキャラに合わせてある。
        private const double FullBody = 0.80;
        private const double FullBodyLoose = 1.00;
        private const double Knee = 0.70;
        private const double Waist = 0.60;
        private const double Medium = 0.55;
        private const double Bust = 0.50;
        private const double FaceLoose = 0.42;
        private const double Face = 0.32;
        private const double WideLow = 1.60;
        private const double ExtremeClose = 0.30;
        private const double FeetClose = 0.28;

        // 譜面。拍 8 以降は 128 拍で繰り返す（8〜136）。曲構造は分からないので擬似的なイントロ / A / B / サビ / アウトロを作る。
        private static readonly Cue[] Cues =
        {
            // イントロ（初回のみ）: 引きフィックス → だんだん寄り
            Hold(double.NegativeInfinity, 4, P(FullBodyLoose, 0, 8, 30, CameraTarget.Hips)),
            Move(4, 8, P(FullBodyLoose, 0, 8, 30, CameraTarget.Hips), P(FullBody, 0, 8, 30, CameraTarget.Hips), 4, Ease.Smooth),
            // A: 8 拍カット割り、最後の 2 拍だけ顔クローズアップ
            Hold(8, 16, P(Medium, 35, 4, 24, CameraTarget.Head)),
            Hold(16, 24, P(Waist, -140, 10, 26, CameraTarget.Hips)),
            Hold(24, 30, P(Waist, 0, 6, 26, CameraTarget.Hips)),
            Hold(30, 32, P(Face, 0, 0, 10, CameraTarget.Head)),
            Hold(32, 40, P(Knee, -20, 6, 28, CameraTarget.Hips)),
            // B: オービット → 停止 → サビ前のタメ
            Move(40, 56, P(Knee, -60, 5, 26, CameraTarget.Hips, 0, 0.3), P(Knee, 60, 5, 26, CameraTarget.Hips, 0, 0.3), 16, Ease.Linear),
            Hold(56, 64, P(Knee, 60, 5, 26, CameraTarget.Hips, 0, 0.3)),
            Hold(64, 72, P(FullBody, 0, 8, 28, CameraTarget.Hips)),
            // サビ: 足元 → アオリ広角から顔へ急接近して静止 → パンチイン → 衝撃カット
            Hold(72, 74, P(FeetClose, 0, -20, 18, CameraTarget.Feet, 0, 0.2)),
            Move(74, 80, P(WideLow, 0, -20, 55, CameraTarget.Head), P(FaceLoose, 0, -8, 14, CameraTarget.Head), 0.5, Ease.Smooth),
            Hold(80, 96, P(Medium, 15, 4, 24, CameraTarget.Hips, 0, 0.4), punch: true),
            Hold(96, 104, P(Medium, -45, 2, 22, CameraTarget.Head, 0, 0.4), punch: true, impact: true),
            // アウトロ: アオリ気味のバストアップからゆっくり引いて完全停止
            Move(104, 120, P(Bust, 25, -12, 26, CameraTarget.Hips, 4, 0), P(FullBodyLoose, 25, -12, 30, CameraTarget.Hips, 0, 0), 16, Ease.Smooth),
            Hold(120, 136, P(FullBodyLoose, 25, -12, 30, CameraTarget.Hips)),
        };

        private const double PunchFov = 1.5;
        private const double PunchHalfLifeBeats = 0.15;
        private const double ImpactBeats = 0.125;
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
                pose.Distance = Distance(ExtremeClose, ImpactFov);
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

        /// <summary>
        /// 注視点からカメラ位置への相対ベクトル。床下に潜らない補正込みで、長さは常に Distance に等しい。
        /// 注視点の高さは実行時にしか分からないので引数で受ける。
        /// </summary>
        public static (double X, double Y, double Z) Offset(CameraPose pose, double targetHeight)
        {
            var yaw = pose.Yaw * Math.PI / 180.0;
            var pitch = LiftAboveGround(pose.Pitch, pose.Distance, targetHeight) * Math.PI / 180.0;
            return (Math.Sin(yaw) * Math.Cos(pitch) * pose.Distance,
                Math.Sin(pitch) * pose.Distance,
                Math.Cos(yaw) * Math.Cos(pitch) * pose.Distance);
        }

        /// <summary>カメラが地面より下に降りないよう、距離を保ったままピッチだけ持ち上げる [度]。</summary>
        public static double LiftAboveGround(double pitchDegrees, double distance, double targetHeight)
        {
            if (distance <= 0.0) return pitchDegrees;
            var minSin = (MinCameraHeight - targetHeight) / distance;
            if (minSin <= -1.0) return pitchDegrees;                       // どんなに下げても地面に届かない
            if (minSin >= 1.0) return 90.0;                                // 真上からしか成立しない
            var minPitch = Math.Asin(minSin) * 180.0 / Math.PI;
            return Math.Max(pitchDegrees, minPitch);
        }

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
