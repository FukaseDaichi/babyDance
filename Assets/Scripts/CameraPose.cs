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
