using System;
using UnityEngine;

namespace BabyDance
{
    /// <summary>AudioLoader・BeatClock・DanceDriver を束ね、毎フレーム dspTime で拍を進める。</summary>
    public sealed class DancePlayer : MonoBehaviour
    {
        public AudioLoader audio;
        public DanceDriver driver;
        public float initialBpm = 120f;

        private BeatClock _clock;

        public event Action Changed;
        public event Action<string> Message;

        public double Bpm => _clock.Bpm;
        public bool IsPlaying => _clock.IsRunning;
        public bool HasClip => audio.HasClip;
        public int DanceCount => driver.dances.Length;
        public string DanceName(int index) => driver.dances[index].clip.name;

        private void Awake()
        {
            _clock = new BeatClock(initialBpm);
            audio.Loaded += _ => { Message?.Invoke($"Loaded {audio.ClipName}"); Changed?.Invoke(); };
            audio.Error += msg => Message?.Invoke(msg);
        }

        private void Start()
        {
            driver.SetDance(0, 0.0);
        }

        private void Update()
        {
            if (!_clock.IsRunning) return;
            if (!audio.IsPlaying)
            {
                StopPlayback();
                return;
            }
            driver.Tick(_clock.BeatAt(AudioSettings.dspTime));
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
            driver.SetDance(index, _clock.BeatAt(AudioSettings.dspTime));
            Changed?.Invoke();
        }

        private void StartPlayback()
        {
            var start = audio.Play();
            _clock.Start(start);
            driver.SetDance(driver.CurrentIndex, _clock.BeatAt(AudioSettings.dspTime));
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
