# マイルストーン 1: BPM 既知の曲に合わせて X Bot が踊る — 設計

日付: 2026-09-03
状態: 承認済み（チャットで合意）

## 目的

「アップロードした音楽に合わせて 3D 赤ちゃんキャラが踊る」プレイヤーの、最大リスクである
**音楽と Animator の同期方式**を最初に証明する。キャラは Mixamo 標準の X Bot、BPM はユーザー入力。
ここが動けば、キャラ差し替え（Meshy）と BPM 自動検出は独立に後付けできる。

## スコープ外

- Meshy 生成キャラ、表情、BPM 自動検出（Spectral Flux）、WebGL、Windows ビルド、シーク／プレイリスト。

## 全体構成

```
[uGUI]  開く / 再生・停止 / BPM スライダー / ダンス切替
   │
   ▼
AudioLoader ──(AudioClip, 開始 dspTime)──▶ BeatClock ──(現在の拍位置)──▶ DanceDriver ──▶ Animator
   ▲                                          ▲
StandaloneFileBrowser                    BPM（ユーザー入力）
UnityWebRequestMultimedia
```

## コンポーネント

### AudioLoader（MonoBehaviour）
- StandaloneFileBrowser の `OpenFilePanelAsync` で MP3 / WAV / OGG を選択（Mac は同期版がフォーカス復帰で例外を出すため常に Async）。
- `file://` + 絶対パスを `UnityWebRequestMultimedia.GetAudioClip` に渡し、拡張子から `AudioType` を決める。
- 読込済みクリップは差し替え前に `Destroy` する。
- 再生は `AudioSource.PlayScheduled(AudioSettings.dspTime + 1.0)` とし、その予定時刻を `StartDspTime` として公開する。停止は `Stop()` で `StartDspTime` を無効化。

### BeatClock（純粋 C# クラス、Unity 非依存）
- 入力: `startDspTime`, `bpm`, 現在の `dspTime`。
- 出力: `CurrentBeat`（実数の拍位置。開始前は負値）。
- BPM 変更: 変更時刻の拍位置を保持したまま傾きだけ変える（`beatAtChange + (dsp - dspAtChange) * bpm / 60`）。これによりスライダー操作で位相が飛ばない。
- EditMode テストで検証する（等速、BPM 変更時の連続性、開始前の負値）。

### DanceDriver（MonoBehaviour）
- `Awake` で `Animator.enabled = false`（`speed = 0` だと `Animator.Update(dt)` の進みも 0 倍になるため、コンポーネント自体を止めて手動 Update だけで進める）。
- 毎フレーム `Δbeat = clock.CurrentBeat - lastBeat` を取り、`Animator.Update(Δbeat * clipSecondsPerBeat)` を呼ぶ。
  `clipSecondsPerBeat = clip.length / beatsPerLoop`。
- `Time.deltaTime` は使わない。dspTime のみが時間源。
- ダンス切替は Animator の State を `CrossFade` で切替える（Manual Ticking でも遷移は動く）。

### DanceClipInfo（ScriptableObject）
- `AnimationClip clip`, `int beatsPerLoop`, `float beatOffset`（拍頭とクリップ先頭のずれ、拍単位）。
- Mixamo ダンスは拍に揃っていないため、この 2 値は人手で合わせる。v0 の唯一の手調整ポイント。

### UI（uGUI）
- 4 要素のみ: 開くボタン、再生／停止トグル、BPM スライダー（60〜200、数値表示）、ダンス切替ドロップダウン。

## エラー処理
- ファイル選択キャンセル → 何もしない。
- 読込失敗（`UnityWebRequest.result != Success`）→ 画面に 1 行メッセージ。ログにも出す。
- 未読込で再生押下 → ボタンを無効化して防ぐ。

## テスト
- EditMode: BeatClock の単体テスト（上記 3 ケース）。壊し方: BPM 変更時に位相リセットするよう実装を壊すと連続性テストが落ちること。
- PlayMode: 無効化した Animator に `Update(0.5f)` を呼ぶと 1 秒クリップの normalizedTime が 0.5 になること。壊し方: `speed = 0` 方式に変えると 0 のままで落ちる。
- 手動: 120 BPM のメトロノーム音源で拍とステップが揃うか目視。5 分再生してドリフトが見えないこと。

## 人手作業（GUI）
1. Mixamo から X Bot（T-pose, With Skin）を `Assets/Characters/XBot.fbx`、Dance 2〜3 本を「Without Skin / FBX for Unity」で同フォルダに DL。
2. Humanoid 化・ループ・Bake Into Pose は `Assets/Editor` の AssetPostprocessor が自動適用する（人手不要）。
3. 各ダンスの beatsPerLoop / beatOffset を耳合わせで DanceClipInfo に入力。

## 判明済みの環境制約
- Unity 6000.5.10f1、URP テンプレート、リポジトリルートがプロジェクトルート。
- Windows Build Support 未インストール。開発は Mac エディタで行う。
