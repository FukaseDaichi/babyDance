# babyDance 設計

ユーザーが選んだ音楽ファイルを再生し、入力した BPM に合わせて Mixamo の X Bot が踊る Unity アプリ。

Unity 6000.5.10f1 / URP。入力は Input System のみ。UI は uGUI。ファイル選択は StandaloneFileBrowser（OpenUPM）。配布形態は Mac の実行ファイル。

## 同期方式

このアプリの中心は、音楽とアニメーションをどう同期させるかにある。

**時間源は `AudioSettings.dspTime` だけである。** `Time.deltaTime` も `Time.time` もランタイムアセンブリのどこにも現れない。dspTime はオーディオハードウェアのクロックであり、フレームレートの変動にも一時停止にも影響されない。

**Animator はコンポーネントごと無効化し、手で進める。** `Animator.enabled = false` にした上で、毎フレーム `Animator.Update(Δ拍 × クリップの秒/拍)` を呼ぶ。`Animator.speed = 0` では手動 `Update` の進みも 0 倍になるため使えない。無効化した Animator に対して `Play` / `CrossFadeInFixedTime` / `Update` / `GetCurrentAnimatorStateInfo` / `IsInTransition` が期待どおり動くことは Unity の文書にある保証ではなく、PlayMode テストで実証している。

**拍位置は毎フレーム絶対値で再計算し、Animator には差分だけを渡す。** これがドリフトしない理由である。拍位置は `拍 = 変更時の拍 + (dspTime − 変更時の dspTime) × BPM / 60` という dspTime の一次関数で、履歴を持たない。差分は telescoping して総和が絶対位置に一致するため、誤差の累積源がアプリ側に存在しない。BPM 変更時は変更時点の拍位置を保ったまま傾きだけを変えるので、スライダー操作で位相が飛ばない。

**再生は 1 秒先にスケジュールし、その間にダンスを助走させる。** `AudioSource.PlayScheduled(dspTime + 1.0)` で予約し、その予定時刻を拍 0 とする。予約直後に拍位置を問い合わせると負の値（120 BPM なら −2 拍）が返り、ダンスはその位相から始まる。1 秒かけて拍 0 まで進むため、最初の可聴サンプルが鳴る瞬間にダンスはループ先頭にちょうど到達する。この恒等式は BPM とクリップ長によらず厳密に成立する。

**ダンス切替は 1 拍のクロスフェードで行う。** 遷移長は遷移先の「秒/拍」で指定し、Animator も同じレートで進むため、遷移は常に音楽の 1 拍ぶんになる。切替時は遷移先クリップをその瞬間の拍位置に対応する位相へ合わせるので、切り替えても拍から外れない。

## 構成

ランタイム（`Assets/Scripts/`、アセンブリ `BabyDance`）:

| | 責務 |
| --- | --- |
| `BeatClock` | dspTime を実数の拍位置に変換する。Unity 非依存の純粋 C# |
| `DanceDriver` | 無効化した Animator を拍差分で手動ティックする。時間を自分では読まず、拍位置を引数で受け取る |
| `DanceClipInfo` | ダンス 1 本のメタデータ（ScriptableObject）。クリップ、State 名、調律定数 |
| `AudioLoader` | ファイル選択・デコード・DSP スケジュール再生 |
| `AudioFileType` | 拡張子から `AudioType` を決める。`AudioLoader` から分離してある唯一の理由は、ここだけが単体テスト可能だから |
| `DancePlayer` | 上記を配線し、毎フレーム dspTime から拍を進める |
| `DanceUi` | 実行時に uGUI を組み立てる。開く / 再生・停止 / BPM スライダー（60〜200）/ ダンス切替の 4 コントロールと、1 行のメッセージ表示 |

Editor（`Assets/Editor/`、アセンブリ `BabyDance.Editor`）: `BabyDance` と URP Runtime を参照する。逆向きの参照はない。

シーンには Canvas も EventSystem も焼かない。`DanceUi` が実行時に構築するため、シーンに存在すると実行時生成が抑止される。

## アセット生成

FBX の設定・AnimatorController・シーン・ビルドはすべて Editor スクリプトが生成する。GUI 操作は Mixamo から FBX を `Assets/Characters/` に置くところまでで、それ以降に人手は要らない。

`Assets/Characters/` 配下の FBX は `AssetPostprocessor` が自動的に Humanoid にし、クリップ名をファイル名に統一し、ループを有効にし、ルートの回転・Y・XZ を Bake Into Pose にする。`XBot.fbx` がキャラ本体で、それ以外がダンスである。ダンス FBX は「Without Skin」で落としたものでも有効な Humanoid Avatar を生成するので、Humanoid リターゲットの供給側になれる。

`DanceDriver` は Bake Into Pose に加えて `applyRootMotion = false` も設定する。二重の防御であり、キャラの Transform がアニメーションから書かれることはない。原地から流れていくことは構造的に起こらない。

**AnimatorController の GUID は生成を繰り返しても変わらない。** シーンは controller を GUID で参照するため、削除して作り直すと参照が無症状で切れる（例外も出ず、UI も正常に構築され、キャラだけが動かない）。生成は既存アセットを読み込んで State を入れ替える形で行う。

`DanceClipInfo` を再生成しても調律定数は保持され、クリップと State 名だけが更新される。対応する FBX が無くなった `.asset` は削除される。クリップ名が衝突するダンスがあれば例外で止まる。

## 調律定数

`beatsPerLoop` は「クリップ 1 ループが音楽の何拍ぶんか」。`秒/拍 = クリップ長 / beatsPerLoop` として Animator の進行レートを決める。新しいダンスを追加すると `クリップ長 × 2`（120 BPM 換算）が初期値として入るため、最初から実速度に近い。

`beatOffset` は拍頭とクリップ先頭のずれを拍単位で表す。現行の 4 本はいずれも 0 で揃っている。

どちらも生成時に上書きされないので、手で調整した値はそのまま残る。

## CLI 運用

Unity の操作は `tools/unity.sh` を通す。`compile` / `test <EditMode|PlayMode> <期待件数>` / `exec <完全メソッド名>` / `build` を持つ。

このラッパーの存在理由は速記ではなく、**判定不能を成功と読ませないこと**にある。ログが書かれていない、結果 XML が無い、テスト件数が期待と違う、`error CS` がある、Editor メソッドが完了マーカーを出していない — いずれも非 0 終了になる。Unity の終了コード 0 だけを見て成功と判定しない。

終了コードの判定は XML の内容判定より先に置く。プロセスがクラッシュしていながら XML だけ整合しているケースを取り逃さないためで、XML 判定は「終了コード 0 なのに結果が不正」を拾う内側の網として機能する。この順序のため、テスト失敗時のメッセージは失敗テスト名ではなく終了コードを示す。詳細はログと XML にある。

Editor 側のツールは異常時に `return` せず例外を投げる。`-executeMethod` が非 0 で終わるためである。成功時は `[BabyDance] <完全メソッド名> done` をログに出し、ラッパーがこれを要求する。

GUI の Editor が同じプロジェクトを開いていると CLI は失敗する。

## テスト方針

自動テストは決定的な部分に限る。EditMode が `BeatClock`・`DanceDriver` の純粋関数・拡張子判定、PlayMode が無効化 Animator の手動ティック。

`AudioLoader`・`DancePlayer`・`DanceUi` に自動テストは無い。ネイティブファイルダイアログ、`file://` 経由のデコード、オーディオハードウェアクロック、実行時 uGUI 構築はいずれもバッチモードで動かせない。これらの検証はコンパイルと、ビルド済みアプリの起動ログ確認と、人手の目視による。

テストは「通るか」ではなく「壊れた実装で落ちるか」で評価する。各ガードには意図的に壊して落ちることを確認した実績がある。

テストのフィクスチャは非対称にする。同じクリップ長・同じ係数・既定オフセットで組むと、単位の取り違えも対象の取り違えも誤差が相殺して不可視になる。

## 既知の限界

Mecanim の `AnimatorStateInfo.normalizedTime` はラップせず増加し続けるため、float 精度が総再生時間とともに劣化する。手動ティックで 5 分駆動すると **約 30 ms** のずれが出る。これはエンジン側の下限であり、この設計の欠陥ではない。明らかにこれより大きいずれや、時間とともに広がるずれは本物の異常を示す。

音声出力レイテンシ（macOS で 10〜30 ms 程度）のぶん、映像は音声より一定量先行する。時間とともに増えないのでドリフトとは区別できる。

Windows Build Support は未インストール。ビルド対象は Mac と WebGL のみ。

`DefaultControls` を空の `Resources` で使うため、ドロップダウンの矢印とチェックマークは描かれない。Sprite の無い `Image` は単色矩形になる。
