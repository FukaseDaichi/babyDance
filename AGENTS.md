# コーディング原則

- **後方互換性は維持しない。** 互換レイヤー、フォールバック、マイグレーションを追加するのではなく、不要になった実装やコードパスは削除する。
- **現在の要件を完全に満たす、最もシンプルな実装を選ぶ。** 将来を見越した過剰な抽象化、設定、間接化は避ける。
- **全体の複雑さを減らしたり、信頼性を高めたりできる場合は、実績があり継続的にメンテナンスされているライブラリを優先する。** 明確な理由がない限り、一般的な機能を自前で再実装しない。

# 検証の原則

- **「判定不能」を「否定の答え」として扱わない。** プローブが答えられなかった／出力が空だった／沈黙していた、は第3の状態として扱い、安全側へ倒す。本リポジトリで実際に見つかった不具合の大半はこの一つの誤りだった（コンパイル失敗時の沈黙をPASS、空の `instances[]` を「Editorなし」、応答不能なプローブを「異常なし」、文書に書かれた前提を実行時の保証、と読んだ）。
- **テストは「通るか」ではなく「壊れた実装で落ちるか」で評価する。** 各ルールについて「意図的に壊したら、どのテストが落ちるか」を答えられないなら、そのルールは無防備。
- **ガードを入れたら、バグ条件を自分で作って否定する。** 「直したから大丈夫」で終わらせず、バグが起こすはずの数値を測って、それが起きないことを示す。

# Python の実行ルール

**Python は必ず `uv` で実行する。`python` / `python3` / `pip` / `venv` を直接使わない。**

- 依存関係はスクリプト冒頭の PEP 723 インラインメタデータ（`# /// script` ブロック）に書く。
- スキルやドキュメントに Python の実行例を書くときも、必ず `uv run` 形式で書く。

# Agent Skill の管理ルール

Claude Code と Codex の両方から同じスキルを参照・実行できるようにする。スキルの作成・インストール・更新・削除は必ずこの方式に従う。

## 正本と公開方法

- スキルの唯一の編集元（Single Source of Truth）は `.agents/skills/<name>/SKILL.md`。1スキル = 1ディレクトリで、`SKILL.md` を必須とする。frontmatter には少なくとも `name` と `description` を置く。**正本以外を直接編集しない。**
- Codex は `.agents/skills/` を直接読む（`$<name>` で呼び出し）。
- Claude Code への公開は `.claude/skills/<name>/SKILL.md` に**通常ファイルの参照スタブ**を置く（`/<name>` で呼び出し）。スタブの内容は次の形式のみとし、手順本体は書かない：

  ```markdown
  ---
  name: <name>
  description: <実体と同じ description>
  ---

  このファイルは Claude Code 用の参照スタブ。スキルの実体は `.agents/skills/<name>/SKILL.md`。
  実体を読み、その手順に従って実行せよ。編集は実体側だけに行う。
  ```

- 実体の `description` を変更したら、スタブの `description` も一致するように更新する。
- 実体 `SKILL.md` の中からスクリプトや参照ファイルを指すパスは、**リポジトリルート相対**で書く（例: `.agents/skills/<name>/scripts/foo.py`）。スタブ経由で起動するとスキルのベースディレクトリが `.claude/skills/<name>` になるため、スキルディレクトリ相対のパスは解決できない。

# Unity の CLI 運用ルール

Unity 6000.5.10f1 のプロジェクト。**Unity 操作は CLI（バッチモード）を第一手段とし、GUI が必須な作業だけ人手に依頼する。**

- `UNITY=/Applications/Unity/Hub/Editor/6000.5.10f1/Unity.app/Contents/MacOS/Unity`
- 常に `-batchmode -nographics -projectPath . -logFile <log>` を付け、終了後にログを `grep -E "error CS|Failed"` で確認する。終了コード 0 だけでは成功と判定しない。
- コンパイル確認: 上記に `-quit` を付けて起動するだけ。
- テスト: `-runTests -testPlatform EditMode -testResults <xml>`（`-quit` は付けない）。XML の `result="Passed"` を確認する。
- パッケージ追加: `Packages/manifest.json` を直接編集し、コンパイル確認を 1 回走らせる。
- ビルド: `Assets/Editor/` のビルドスクリプトを `-executeMethod` で呼ぶ。Windows Build Support は未インストール（Mac / WebGL のみ）。
- GUI の Editor が同プロジェクトを開いていると CLI は失敗する。

# LEARNINGS.md ループ

各セッションの開始時に、リポジトリ直下の LEARNINGS.md を読め。
読んだ内容を1〜3行で要約して提示し、読み込みが行われたことを可視化せよ。
実質的なリポジトリ作業を完了して最終回答を返す前に、 `update-learnings` スキルを1回だけ実行せよ。 雑談、単純な質問、変更や再利用可能な学びがない作業では実行不要とする。
