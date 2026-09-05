| `CameraPose` | カメラ姿勢の純データ（球面座標 + FOV + ロール + 注視点種別） |
| `CameraScore` | 拍 → カメラ姿勢の純関数。譜面はコード内の配列。Unity 非依存 |
| `ShotDirector` | `CameraScore` の姿勢を Main Camera に適用し、Humanoid ボーンを注視点にする |
# babyDance 設計

ユーザーが選んだ音楽ファイルを再生し、入力した BPM に合わせて Mixamo の X Bot が踊る Unity アプリ。

Unity 6000.5.10f1 / URP。入力は Input System のみ。UI は uGUI。ファイル選択はブラウザの `<input type="file">`（自前の jslib）。配布形態は WebGL で、GitHub Pages に置く。

## 同期方式

このアプリの中心は、音楽とアニメーションをどう同期させるかにある。

**時間源は `AudioSettings.dspTime` だけである。** `Time.deltaTime` も `Time.time` もランタイムアセンブリのどこにも現れない。dspTime はオーディオハードウェアのクロックであり、フレームレートの変動にも一時停止にも影響されない。

**Animator はコンポーネントごと無効化し、手で進める。** `Animator.enabled = false` にした上で、毎フレーム `Animator.Update(Δ拍 × クリップの秒/拍)` を呼ぶ。`Animator.speed = 0` では手動 `Update` の進みも 0 倍になるため使えない。無効化した Animator に対して `Play` / `CrossFadeInFixedTime` / `Update` / `GetCurrentAnimatorStateInfo` / `IsInTransition` が期待どおり動くことは Unity の文書にある保証ではなく、PlayMode テストで実証している。

**拍位置は毎フレーム絶対値で再計算し、Animator には差分だけを渡す。** これがドリフトしない理由である。拍位置は `拍 = 変更時の拍 + (dspTime − 変更時の dspTime) × BPM / 60` という dspTime の一次関数で、履歴を持たない。差分は telescoping して総和が絶対位置に一致するため、誤差の累積源がアプリ側に存在しない。BPM 変更時は変更時点の拍位置を保ったまま傾きだけを変えるので、スライダー操作で位相が飛ばない。

**再生は 1 秒先にスケジュールし、その間にダンスを助走させる。** `AudioSource.PlayScheduled(dspTime + 1.0)` で予約し、その予定時刻を拍 0 とする。予約直後に拍位置を問い合わせると負の値（120 BPM なら −2 拍）が返り、ダンスはその位相から始まる。1 秒かけて拍 0 まで進むため、最初の可聴サンプルが鳴る瞬間にダンスはループ先頭にちょうど到達する。この恒等式は BPM とクリップ長によらず厳密に成立する。

**ダンス切替は 1 拍のクロスフェードで行う。** 遷移長は遷移先の「秒/拍」で指定し、Animator も同じレートで進むため、遷移は常に音楽の 1 拍ぶんになる。切替時は遷移先クリップをその瞬間の拍位置に対応する位相へ合わせるので、切り替えても拍から外れない。

## カメラワーク

**カメラの姿勢も拍位置の純関数である。** `CameraScore.PoseAt(beat)` が 128 拍サイクルの譜面（イントロ / A / B / サビ / アウトロの擬似構造）を評価し、距離・ヨー・ピッチ・FOV・ロール・注視点を返す。時間源はダンスと同じ拍で、`ShotDirector` は `driver.Tick` の直後に同じ拍で呼ばれる。同じ拍を渡せば同じ画になるため、譜面は EditMode で決定的にテストできる。

移動はすべて「イージングで一気に動いて着地で止める」。移動拍数を過ぎたキューは終端姿勢で完全静止し、次のカットまで動かない。サビ相当の 32 拍では拍頭ごとに FOV を 1.5 縮めて指数的に戻すパンチイン、バーの 1 拍目には 1/8 拍だけ超アップを挟む衝撃カットを重ねる。手ブレは正弦波の和による決定的ノイズで、乱数は使わない。

注視点は Humanoid ボーン（Hips / Head / 両足の中点）の実座標を毎フレーム読み、拍単位の指数平滑（時定数 0.25 拍）で追う。注視点の種別が変わるカットでは平滑をリセットして跳ねを防ぐ。ピッチは −20° 以上、距離は 1.0 以上、FOV は 10〜55 に clamp する。

Cinemachine は採用しない。キャラは原点に固定されており Follow の価値がなく、Cinemachine のブレンドは `Time.deltaTime` 駆動で時間源を増やすため。

UI の「Camera: Auto / Fixed」で譜面を止めて引きの固定画に戻せる。カメラ演出の過剰に気づくための観察用スイッチである。

## 構成

ランタイム（`Assets/Scripts/`、アセンブリ `BabyDance`）:

| | 責務 |
| --- | --- |
| `BeatClock` | dspTime を実数の拍位置に変換する。Unity 非依存の純粋 C# |
| `DanceDriver` | 無効化した Animator を拍差分で手動ティックする。時間を自分では読まず、拍位置を引数で受け取る |
| `DanceClipInfo` | ダンス 1 本のメタデータ（ScriptableObject）。クリップ、State 名、調律定数 |
| `AudioLoader` | ブラウザのファイル選択（jslib 経由）・blob URL からのデコード・DSP スケジュール再生 |
| `AudioFileType` | 拡張子から `AudioType` を決める。`AudioLoader` から分離してある唯一の理由は、ここだけが単体テスト可能だから |
| `DancePlayer` | 上記を配線し、毎フレーム dspTime から拍を進める |
| `DanceUi` | 実行時に uGUI を組み立てる。開く / 再生・停止 / BPM スライダー（60〜200）/ ダンス切替 / カメラ Auto・Fixed の 5 コントロールと、1 行のメッセージ表示 |

Editor（`Assets/Editor/`、アセンブリ `BabyDance.Editor`）: `BabyDance` と URP Runtime を参照する。逆向きの参照はない。

シーンには Canvas も EventSystem も焼かない。`DanceUi` が実行時に構築するため、シーンに存在すると実行時生成が抑止される。

`DanceUi` が読む状態はすべて `DancePlayer` が公開する。選択中のダンス番号も `DancePlayer` が持つため、UI は `DanceDriver` を直接触らず、両者の `Start` の実行順にも依存しない。

ログ行の接頭辞 `[BabyDance]` はランタイムの `Log.Tag` が唯一の定義で、Editor 側もこれを参照する。`tools/unity.sh` がこの文字列を grep する以上、定義が散ると壊れたときに気づけない。

## アセット生成

FBX の設定・AnimatorController・シーン・ビルドはすべて Editor スクリプトが生成する。GUI 操作は Mixamo から FBX を `Assets/Characters/` に置くところまでで、それ以降に人手は要らない。

`Assets/Characters/` 配下の FBX は `AssetPostprocessor` が自動的に Humanoid にし、クリップ名をファイル名に統一し、ループを有効にし、ルートの回転・Y・XZ を Bake Into Pose にする。`XBot.fbx` がキャラ本体で、それ以外がダンスである。ダンス FBX は「Without Skin」で落としたものでも有効な Humanoid Avatar を生成するので、Humanoid リターゲットの供給側になれる。

`DanceDriver` は Bake Into Pose に加えて `applyRootMotion = false` も設定する。二重の防御であり、キャラの Transform がアニメーションから書かれることはない。原地から流れていくことは構造的に起こらない。

**AnimatorController の GUID は生成を繰り返しても変わらない。** シーンは controller を GUID で参照するため、削除して作り直すと参照が無症状で切れる（例外も出ず、UI も正常に構築され、キャラだけが動かない）。生成は既存アセットを読み込み、中身だけ差し替える形で行う。

State も名前で引き当てて再利用する。消して足し直すと State に新しい fileID が振られ、生成のたびに内容の変わらない差分だけが出る。同じダンス構成で 2 回生成すれば `Dance.controller` に差分は出ない。

`DanceClipInfo` を再生成しても調律定数は保持され、クリップと State 名だけが更新される。対応する FBX が無くなった `.asset` は削除される。クリップ名が衝突するダンスがあれば例外で止まる。

## 調律定数

`beatsPerLoop` は「クリップ 1 ループが音楽の何拍ぶんか」。`秒/拍 = クリップ長 / beatsPerLoop` として Animator の進行レートを決める。新しいダンスを追加すると `クリップ長 × 2`（120 BPM 換算）が初期値として入るため、最初から実速度に近い。

`beatOffset` は拍頭とクリップ先頭のずれを拍単位で表す。現行の 4 本はいずれも 0 で揃っている。

どちらも生成時に上書きされないので、手で調整した値はそのまま残る。

## WebGL 配布

ビルド対象は WebGL のみ。`tools/unity.sh build` が `Builds/WebGL` に出力し、`tools/deploy.sh` がその中身を `gh-pages` ブランチの単一コミットとして force push する。GitHub Pages はこのブランチのルートを配信する。履歴は持たない。

ファイル選択は `Assets/Plugins/WebGL/AudioFilePicker.jslib` が `<input type="file">` を開き、選択後に `SendMessage(<GameObject 名>, "OnFileChosen", "<ファイル名>\n<blob URL>")` で `AudioLoader` に返す。blob URL には拡張子が無いため、ファイル名を一緒に返して `AudioFileType` の拡張子判定を成立させている。キャンセル時は何も返らない。WebGL 以外（Editor 再生）では「開く」はエラーを 1 行表示するだけで、別経路は持たない。

デコードはブラウザの `decodeAudioData` が非同期に行う。`DownloadHandlerAudioClip.GetContent` が返した直後の clip は `length` が 0・`loadState` が `Unloaded` で、Unity 自身が「Trying to get length of sound which is not loaded yet」を出す。`AudioLoader` は `length` が正になるまで 0.1 秒間隔で待ち（上限 15 秒）、その後に `Loaded` を発火する。実測では 60 秒の WAV で 0.2 秒。サンプリングレートはブラウザの `AudioContext` に合わせて変わる（22.05 kHz の入力が 44.1 kHz で返る）ため、`frequency` を前提にした計算は置かない。

圧縮は Brotli のまま Decompression Fallback を有効にしている。GitHub Pages は `.br` に `Content-Encoding` ヘッダを付けないため、ヘッダ無しで動く JS 側の解凍に頼る。

再生開始はユーザーの「開く」操作の後なので、ブラウザの自動再生制限には当たらない。

## CLI 運用

Unity の操作は `tools/unity.sh` を通す。`compile` / `test <EditMode|PlayMode> <期待件数>` / `exec <完全メソッド名>` / `build`（WebGL）を持つ。配布は `tools/deploy.sh`。

このラッパーの存在理由は速記ではなく、**判定不能を成功と読ませないこと**にある。ログが書かれていない、結果 XML が無い、テスト件数が期待と違う、`error CS` がある、Editor メソッドが完了マーカーを出していない — いずれも非 0 終了になる。Unity の終了コード 0 だけを見て成功と判定しない。

終了コードの判定は XML の内容判定より先に置く。プロセスがクラッシュしていながら XML だけ整合しているケースを取り逃さないためで、XML 判定は「終了コード 0 なのに結果が不正」を拾う内側の網として機能する。この順序のため、テスト失敗時のメッセージは失敗テスト名ではなく終了コードを示す。詳細はログと XML にある。

Editor 側のツールは異常時に `return` せず例外を投げる。`-executeMethod` が非 0 で終わるためである。成功時は `[BabyDance] <完全メソッド名> done` をログに出し、ラッパーがこれを要求する。

GUI の Editor が同じプロジェクトを開いていると CLI は失敗する。

## テスト方針

自動テストは決定的な部分に限る。EditMode が `BeatClock`・`DanceDriver` の純粋関数・`CameraScore` の譜面評価・拡張子判定・ピッカー応答の分解、PlayMode が無効化 Animator の手動ティックと、ボーンの無い Animator で `ShotDirector` が例外で止まること。

`AudioLoader`・`DancePlayer`・`DanceUi`、および `ShotDirector` のボーン追従に自動テストは無い。ブラウザのファイル選択、blob URL 経由のデコード、オーディオハードウェアクロック、実行時 uGUI 構築はいずれもバッチモードで動かせない。これらの検証はコンパイルと、ビルド済みページをブラウザで開いたときのコンソール確認と、人手の目視による。

テストは「通るか」ではなく「壊れた実装で落ちるか」で評価する。各ガードには意図的に壊して落ちることを確認した実績がある。

テストのフィクスチャは非対称にする。同じクリップ長・同じ係数・既定オフセットで組むと、単位の取り違えも対象の取り違えも誤差が相殺して不可視になる。

## 既知の限界

Mecanim の `AnimatorStateInfo.normalizedTime` はラップせず増加し続けるため、float 精度が総再生時間とともに劣化する。手動ティックで 5 分駆動すると **約 30 ms** のずれが出る。これはエンジン側の下限であり、この設計の欠陥ではない。明らかにこれより大きいずれや、時間とともに広がるずれは本物の異常を示す。

音声出力レイテンシ（macOS で 10〜30 ms 程度）のぶん、映像は音声より一定量先行する。時間とともに増えないのでドリフトとは区別できる。

ビルド対象は WebGL のみ。Mac 用のビルドスクリプトは持たない。

`DefaultControls` を空の `Resources` で使うため、ドロップダウンの矢印とチェックマークは描かれない。Sprite の無い `Image` は単色矩形になる。
