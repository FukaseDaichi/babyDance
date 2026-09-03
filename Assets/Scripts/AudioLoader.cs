using System;
using System.Collections;
using System.IO;
using SFB;
using UnityEngine;
using UnityEngine.Networking;

namespace BabyDance
{
    /// <summary>
    /// ネイティブダイアログで音楽ファイルを選び、AudioClip 化して DSP クロックでスケジュール再生する。
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class AudioLoader : MonoBehaviour
    {
        public const double StartDelay = 1.0;

        private static readonly ExtensionFilter[] Filters =
        {
            new ExtensionFilter("Audio", "mp3", "wav", "ogg"),
        };

        private AudioSource _source;

        public event Action<AudioClip> Loaded;
        public event Action<string> Error;

        public bool HasClip => _source.clip != null;
        public string ClipName => HasClip ? _source.clip.name : "";
        public double StartDspTime { get; private set; } = double.NaN;
        public bool IsPlaying => !double.IsNaN(StartDspTime) && (AudioSettings.dspTime < StartDspTime || _source.isPlaying);

        private void Awake()
        {
            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = false;
        }

        public void OpenFile()
        {
            StandaloneFileBrowser.OpenFilePanelAsync("Open music", "", Filters, false, paths =>
            {
                if (paths == null || paths.Length == 0 || string.IsNullOrEmpty(paths[0])) return;
                StartCoroutine(Load(paths[0]));
            });
        }

        public double Play()
        {
            var start = AudioSettings.dspTime + StartDelay;
            _source.PlayScheduled(start);
            StartDspTime = start;
            return start;
        }

        public void Stop()
        {
            _source.Stop();
            StartDspTime = double.NaN;
        }

        private void Fail(string message)
        {
            Debug.LogError($"[BabyDance] {message}");
            Error?.Invoke(message);
        }

        private IEnumerator Load(string path)
        {
            var type = AudioFileType.FromPath(path);
            if (type == AudioType.UNKNOWN)
            {
                Fail($"Unsupported file: {Path.GetFileName(path)}");
                yield break;
            }

            Stop();
            using var request = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, type);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Fail($"Load failed: {request.error}");
                yield break;
            }

            var clip = DownloadHandlerAudioClip.GetContent(request);
            if (clip == null || clip.length <= 0f)
            {
                Fail($"Decode failed: {Path.GetFileName(path)}");
                yield break;
            }

            clip.name = Path.GetFileName(path);
            if (_source.clip != null) Destroy(_source.clip);
            _source.clip = clip;
            Loaded?.Invoke(clip);
        }
    }
}
