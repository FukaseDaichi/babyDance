# babyDance

選んだ音楽ファイルを再生し、入力した BPM に合わせて Mixamo の X Bot が踊る Unity アプリ。ブラウザで動く。

**公開 URL: https://fukasedaichi.github.io/babyDance/**

## 使い方

1. 公開 URL を PC のブラウザで開く（動作確認済みは macOS の Chrome。スマートフォンは未確認）。
2. 「Open music」で手元の音楽ファイル（mp3 / wav / ogg）を選ぶ。ファイルはブラウザ内で読むだけで、どこにも送信されない。
3. 「Play」を押す。1 秒の助走のあと、最初の音と同時にダンスがループ先頭から始まる。
4. スライダーで BPM（60〜200）を曲に合わせる。位相は飛ばずに速さだけ変わる。
5. ドロップダウンでダンスを切り替える。切替は 1 拍のクロスフェード。

動作確認用に 120 BPM のメトロノーム音源がある: `tools/metronome_120bpm.wav`（無ければ `uv run tools/make_metronome.py` で生成）。

## 開発環境

- Unity 6000.5.10f1（WebGL Build Support 必須。`/Applications/Unity/Hub/Editor/6000.5.10f1/` に置く）
- `gh`（GitHub CLI、ログイン済み）
- `uv`（Python スクリプト用）
- Unity の操作は GUI ではなく `tools/unity.sh` から行う。GUI の Editor が同じプロジェクトを開いていると CLI は失敗する。

設計は [docs/design.md](docs/design.md)、今後の候補は [docs/backlog.md](docs/backlog.md)、作業ルールは [AGENTS.md](AGENTS.md)。

## 公開を更新する

コードや資材を変えたら、次の 3 つを順に実行する。

```bash
tools/unity.sh compile
```

```bash
tools/unity.sh test EditMode 25 && tools/unity.sh test PlayMode 6
```

```bash
tools/unity.sh build
```

```bash
tools/deploy.sh
```

- `build` は `Builds/WebGL` に WebGL 版を出力する。ビルド対象を Standalone から切り替えた直後は再インポートが入り、数分かかる。
- `deploy.sh` は `Builds/WebGL` の中身を `gh-pages` ブランチの単一コミットとして force push し、GitHub Pages の状態を確認して公開 URL を表示する。`gh-pages` に履歴は残らない。反映まで 1〜2 分かかることがある。
- テストの件数（16 / 5）はテストを増減したら合わせて変える。件数が合わないと失敗扱いになる。

## 資材を更新する

### ダンスを追加・差し替える

1. Mixamo からダンスの FBX（「Without Skin」で可）を `Assets/Characters/` に置く。ファイル名がダンス名になる。
2. `tools/unity.sh exec BabyDance.Editor.AssetTools.BuildDanceAssets` を実行する。FBX が Humanoid・ループ再生に設定され、`Assets/Dance/` に `<ダンス名>.asset` と `Dance.controller` の State が作られる。
3. `tools/unity.sh exec BabyDance.Editor.SceneBuilder.Build` でシーンを作り直す。
4. `Assets/Dance/<ダンス名>.asset` の `beatsPerLoop`（1 ループが何拍か）を必要なら手で調整する。初期値はクリップ長 × 2（120 BPM 換算）。再生成しても手で入れた値は残る。
5. 上の「公開を更新する」を実行する。

FBX を消して手順 2 を再実行すると、対応する `.asset` も消える。

### キャラクターを差し替える

`Assets/Characters/XBot.fbx` がキャラ本体。Humanoid リグの FBX なら同名で置き換えるだけでよい。別名にするなら `Assets/Editor/SceneBuilder.cs` が参照するパスを変える。

### 音楽ファイル

アプリは実行時にブラウザのファイル選択で音楽を読むため、リポジトリに音源を入れる必要はない。`Assets/music/` の MP3 はアプリから参照されていない。

## トラブルシュート

- `tools/unity.sh` が `compile errors` で止まる: `Logs/compile.log` を `grep "error CS"` で見る。
- `deploy.sh` が `Builds/WebGL/index.html missing`: 先に `tools/unity.sh build` を実行する。
- 公開ページで音楽を開いても「Decode failed」になる: ブラウザがそのコーデックをデコードできない。mp3 / wav / ogg の別ファイルで試す。
- ページが真っ黒のまま: ブラウザのコンソールを開く。`.unityweb` の 404 なら `gh-pages` の中身が古いか欠けているので `deploy.sh` を再実行する。
