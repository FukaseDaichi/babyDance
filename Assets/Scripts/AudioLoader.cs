using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Networking;

namespace BabyDance
{
    /// <summary>
    /// ブラウザのファイル選択で音楽ファイルを選び、AudioClip 化して DSP クロックでスケジュール再生する。
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class AudioLoader : MonoBehaviour
    {
        public const double StartDelay = 1.0;

        /// <summary>ブラウザのデコード完了を待つ上限と間隔。</summary>
        private const float DecodeTimeoutSeconds = 15f;
        private const float DecodePollSeconds = 0.1f;

#if UNITY_WEBGL && !UNITY_EDITOR
        /// <summary>Assets/Plugins/WebGL/AudioFilePicker.jslib。選択後に SendMessage(gameObjectName, methodName, "ファイル名\nblob URL") が返る。</summary>
        [DllImport("__Internal")]
        private static extern void BabyDance_OpenAudioFile(string gameObjectName, string methodName);
#endif

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
#if UNITY_WEBGL && !UNITY_EDITOR
            BabyDance_OpenAudioFile(gameObject.name, nameof(OnFileChosen));
#else
            Fail("File open works only in the WebGL build");
#endif
        }

        /// <summary>jslib から SendMessage で呼ばれる。</summary>
        public void OnFileChosen(string payload)
        {
            var (name, url) = ParseChosenFile(payload);
            if (string.IsNullOrEmpty(url))
            {
                Fail($"No file URL in picker payload: {payload}");
                return;
            }
            StartCoroutine(Load(name, url));
        }

        /// <summary>"ファイル名\nblob URL" を最初の改行で分ける。改行が無ければ URL は空。</summary>
        public static (string name, string url) ParseChosenFile(string payload)
        {
            var i = payload.IndexOf('\n');
            return i < 0 ? (payload, "") : (payload.Substring(0, i), payload.Substring(i + 1));
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
            Debug.LogError($"{Log.Tag} {message}");
            Error?.Invoke(message);
        }

        private IEnumerator Load(string name, string url)
        {
            var type = AudioFileType.FromPath(name);
            if (type == AudioType.UNKNOWN)
            {
                Fail($"Unsupported file: {name}");
                yield break;
            }

            Stop();
            using var request = UnityWebRequestMultimedia.GetAudioClip(url, type);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Fail($"Load failed: {request.error}");
                yield break;
            }

            var clip = DownloadHandlerAudioClip.GetContent(request);
            if (clip == null)
            {
                Fail($"Decode failed: {name} (clip is null)");
                yield break;
            }

            // WebGL ではブラウザの decodeAudioData が非同期で、取得直後は length が 0・loadState が Unloaded。
            // 完了するまで待ってから使う。
            var waited = 0f;
            while (clip.length <= 0f && clip.loadState != AudioDataLoadState.Failed && waited < DecodeTimeoutSeconds)
            {
                waited += DecodePollSeconds;
                yield return new WaitForSecondsRealtime(DecodePollSeconds);
            }
            if (clip.length <= 0f)
            {
                Fail($"Decode failed: {name} (waited {waited:0.0}s, loadState={clip.loadState})");
                yield break;
            }
            Debug.Log($"{Log.Tag} Decoded {name}: length={clip.length:0.00}s frequency={clip.frequency} loadState={clip.loadState} waited={waited:0.0}s");

            clip.name = name;
            if (_source.clip != null) Destroy(_source.clip);
            _source.clip = clip;
            Loaded?.Invoke(clip);
        }
    }
}
