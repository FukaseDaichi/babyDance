using System;
using UnityEngine;

namespace BabyDance
{
    /// <summary>AudioLoader・BeatClock・DanceDriver を束ね、毎フレーム dspTime で拍を進める。</summary>
    public sealed class DancePlayer : MonoBehaviour
    {
        public AudioLoader audio;
        public DanceDriver driver;
        public ShotDirector director;
        [Range(60f, 200f)] public float initialBpm = 120f;

        private BeatClock _clock;

        public event Action Changed;
        public event Action<string> Message;

        public double Bpm => _clock.Bpm;
        public bool IsPlaying => _clock.IsRunning;
        public bool HasClip => audio.HasClip;
        public bool CameraAuto => !director.Fixed;
        public int DanceCount => driver.dances.Length;
        public string DanceName(int index) => driver.dances[index].clip.name;

        /// <summary>選択中のダンス番号。driver の初期化順に依存させないため、ここが唯一の窓口になる。</summary>
        public int CurrentDance { get; private set; }

        private void Awake()
        {
            _clock = new BeatClock(initialBpm);
            audio.Loaded += _ => { Message?.Invoke($"Loaded {audio.ClipName}"); Changed?.Invoke(); };
            audio.Error += msg => Message?.Invoke(msg);
        }

        private void Start()
        {
            SelectDance(0);
        }

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
            // Animator を進めた後にカメラがボーンを読む順序。
            var beat = _clock.BeatAt(AudioSettings.dspTime);
            driver.Tick(beat);
            director.Tick(beat);
        }

        public void Open() => audio.OpenFile();

        public void TogglePlay()
        {
            if (_clock.IsRunning) StopPlayback();
            else StartPlayback();
        }

        public void SetBpm(double bpm)
        {
            _clock.SetBpm(bpm, AudioSettings.dspTime);
            Changed?.Invoke();
        }

        public void SelectDance(int index)
        {
            CurrentDance = index;
            driver.SetDance(index, _clock.BeatAt(AudioSettings.dspTime));
            Changed?.Invoke();
        }

        public void ToggleCamera()
        {
            director.Fixed = !director.Fixed;
            Changed?.Invoke();
        }

        private void StartPlayback()
        {
            var start = audio.Play();
            _clock.Start(start);
            driver.SetDance(CurrentDance, _clock.BeatAt(AudioSettings.dspTime));
            Changed?.Invoke();
        }

        private void StopPlayback()
        {
            audio.Stop();
            _clock.Stop();
            Changed?.Invoke();
        }
    }
}
