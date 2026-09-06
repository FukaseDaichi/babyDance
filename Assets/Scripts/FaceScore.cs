using System;

namespace BabyDance
{
    /// <summary>拍を表情・瞬き・口のコマと UV に写す純関数。Unity 非依存。</summary>
    public static class FaceScore
    {
        public const double Interval = 8.0;
        public const double Jitter = 4.0;
        private const double HalfClosed = 0.1;
        private const double Closed = 0.3;
        private const double BlinkLength = HalfClosed * 2 + Closed;

        public static FacePose PoseAt(double beat, int expression, double switchBeat)
        {
            var n = (long)Math.Floor(beat / Interval);
            var phase = beat - (n * Interval + Hash01(n) * Jitter);
            if (switchBeat <= beat && beat < switchBeat + BlinkLength)
                phase = beat - switchBeat;

            var eye = expression;
            if (phase >= 0 && phase < BlinkLength)
                eye = phase < HalfClosed || phase >= HalfClosed + Closed ? 4 : 5;

            var f = beat - Math.Floor(beat);
            var mouth = f < 0.15 ? 2 : f < 0.35 ? 1 : 0;
            return new FacePose(eye, mouth);
        }

        public static (double X, double Y, double Z, double W) AtlasRect(int frame, int cols, int rows)
        {
            var col = frame % cols;
            var row = frame / cols;
            return (1.0 / cols, 1.0 / rows, (double)col / cols, 1.0 - (double)(row + 1) / rows);
        }

        private static double Hash01(long n)
        {
            unchecked
            {
                ulong x = (ulong)(n * 2654435761L + 1013904223L);
                x ^= x >> 33; x *= 0xff51afd7ed558ccdUL;
                x ^= x >> 33; x *= 0xc4ceb9fe1a85ec53UL;
                x ^= x >> 33;
                return (x >> 11) * (1.0 / 9007199254740992.0);
            }
        }
    }
}
