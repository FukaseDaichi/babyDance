# 今後やること

## 次の段階の候補

同期方式が実証されたので、以下は互いに独立に着手できる。

**キャラクター差し替え（Meshy）** — X Bot を生成キャラに置き換える。Humanoid リグであれば `DanceDriver` も AnimatorController もそのまま使える。`Assets/Characters/` に FBX を置けば `AssetPostprocessor` が Humanoid 化するので、必要なのは `SceneBuilder` が参照するキャラ本体のパスを変えることだけ。表情は別課題。

**BPM 自動検出** — 現在は BPM をユーザーが入力する。Spectral Flux 等で音源から推定できれば入力欄が不要になる。`BeatClock` は BPM を外から受け取るだけなので、検出側を足して `SetBpm` を呼ぶ形になる。検出結果が揺れる場合に位相を飛ばさない扱いが設計課題。

**ダンスの追加** — `Assets/Characters/` に FBX を置き、`BabyDance.Editor.AssetTools.BuildDanceAssets` を実行し、`SceneBuilder.Build` でシーンを作り直す。`beatsPerLoop` はクリップ長から初期値が入る。

**WebGL 配布** — ビルド対象としては可能。`StandaloneFileBrowser` は WebGL をサポートするが挙動が異なる。オーディオの DSP クロックがブラウザでどう振る舞うかは未調査。

**シーク / プレイリスト** — 再生位置の移動は `BeatClock` の再アンカーで表現できるが、UI とスケジュール再生の扱いを設計し直す必要がある。

**Windows ビルド** — Build Support のインストールが前提。

## 判明している負債

いずれも動作に影響しないと確認済み。着手の優先度は低い。

- `BuildDanceAssets` を再実行すると `Dance.controller` の State に新しい fileID が振られ、毎回 60 行ほど差分ノイズが出る。State は名前で参照するため動作には影響しない。
- Animator 側のエラーはログにしか出ない。音声の読込エラーは画面に 1 行表示されるので、扱いが非対称になっている。
- `DanceUi` が `player.driver.CurrentIndex` を直接読んでおり、ここだけ `DancePlayer` の窓口を迂回している。これが `Start` の実行順に依存する唯一の箇所でもある。`DancePlayer` 側に現在のダンス番号を公開すれば両方が解消する。
- `DancePlayer` の初期ダンス設定が UI 更新イベントを発火しない。初期番号が 0 で uGUI 側がクランプするため実害が出ていない。
- `ProjectSettings` の `templateDefaultScene` が削除済みの SampleScene を指している。Editor 専用で無害。
- `Assets/music/` の MP3 4 本（12MB）はアプリから参照されていない。ファイルダイアログで読む設計なので `Assets/` の外に置いても動く。
- ログ用のタグ文字列 `[BabyDance]` が 4 箇所で個別に定義されている。`tools/unity.sh` がこの文字列を grep するため、実質的には 4 つの複製を持つ契約になっている。

## 未解決の問い

**5 分ドリフトを自動回帰テストで守れるか。** 現状は守れていない。位相が絶対拍位置の純関数である限り、長時間テストは Mecanim 内部の累積を再測定するだけになり、このリポジトリのソース変更で壊せる対象が見つかっていない。累積を持ち込む変更（拍位置を差分で持つ設計への退行など）を検出する形なら書ける可能性がある。
