namespace BabyDance
{
    /// <summary>目と口のアトラスコマ。Unity 非依存の純データ。</summary>
    public struct FacePose
    {
        public int EyeFrame;
        public int MouthFrame;

        public FacePose(int eyeFrame, int mouthFrame)
        {
            EyeFrame = eyeFrame;
            MouthFrame = mouthFrame;
        }
    }
}
