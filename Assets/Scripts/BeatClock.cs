namespace BabyDance
{
    /// <summary>
    /// AudioSettings.dspTime を拍位置に変換する。Unity 非依存。
    /// BPM 変更時は変更時刻の拍位置を保持し、傾きだけ変える。
    /// </summary>
    public sealed class BeatClock
    {
        private double _bpm;
        private double _anchorDsp;
        private double _anchorBeat;

        public BeatClock(double bpm)
        {
            _bpm = bpm;
        }

        public double Bpm => _bpm;
        public bool IsRunning { get; private set; }

        public void Start(double startDspTime)
        {
            _anchorDsp = startDspTime;
            _anchorBeat = 0.0;
            IsRunning = true;
        }

        public void Stop()
        {
            IsRunning = false;
        }

        public void SetBpm(double bpm, double nowDspTime)
        {
            if (IsRunning)
            {
                _anchorBeat = BeatAt(nowDspTime);
                _anchorDsp = nowDspTime;
            }
            _bpm = bpm;
        }

        public double BeatAt(double dspTime)
        {
            if (!IsRunning) return 0.0;
            return _anchorBeat + (dspTime - _anchorDsp) * _bpm / 60.0;
        }
    }
}
