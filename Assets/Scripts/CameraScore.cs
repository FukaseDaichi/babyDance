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
