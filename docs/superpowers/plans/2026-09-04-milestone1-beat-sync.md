# マイルストーン 1: BPM 既知の曲に合わせて X Bot が踊る — 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** ユーザーが選んだ音楽ファイルを再生し、ユーザー入力の BPM に合わせて Mixamo X Bot がドリフトなしで踊る Unity アプリ（Mac 実行ファイル）を作る。

**Architecture:** `AudioLoader`（ファイル選択と DSP スケジュール再生）→ `BeatClock`（dspTime から拍位置を返す純粋 C#）→ `DanceDriver`（無効化した Animator を拍差分で手動 `Update`）の 3 部品を `DancePlayer` が束ね、`DanceUi` が実行時に uGUI を組み立てる。FBX の Humanoid 化、AnimatorController と DanceClipInfo の生成、シーン構築、ビルドはすべて `Assets/Editor` のスクリプトを CLI（`-executeMethod`）から呼んで行う。Unity CLI は `tools/unity.sh` 経由で叩き、未実行・判定不能をすべて失敗にする。

**Tech Stack:** Unity 6000.5.10f1（URP、Input System のみ有効）、uGUI 2.5.0、StandaloneFileBrowser 1.3.4（OpenUPM）、Unity Test Framework 1.7.0（NUnit）、Python は `uv` 経由。

**Spec:** `docs/superpowers/specs/2026-09-03-milestone1-beat-sync-design.md`

**Review:** 2026-09-04 に Codex レビューを受け、PlayMode アセンブリのプラットフォーム指定、CLI 判定の厳格化、State 名の完全パス化、URP 参照、`CrossFadeInFixedTime`、資産の掃除、エラー表示、5 分ドリフト検証を反映済み。

## Global Constraints

- Unity 操作は CLI 第一。`AGENTS.md` の「Unity の CLI 運用ルール」に従う。**終了コード 0 だけで成功と判定しない。** 判定は必ず `tools/unity.sh` を通す（ログ不在・XML 不在・件数不一致・`error CS`・完了マーカー不在をすべて失敗にする）。
- Editor 側のツールは異常時に `return` せず**例外を投げる**（`-executeMethod` が非 0 で終わる）。
- 時間源は `AudioSettings.dspTime` のみ。`Time.deltaTime` を同期に使わない。
- Animator の State 名は `Base Layer.<name>` の完全パスで扱う。
- 後方互換・フォールバックを書かない。不要になったコードは削除する。
- Python は `uv run` で実行する。
- 各タスクの最後にコミットする。コミットメッセージ末尾に `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>` を付ける。
- スコープ外: Meshy キャラ、BPM 自動検出、WebGL、Windows ビルド、シーク／プレイリスト。

## ファイル構成

| パス | 責務 |
| --- | --- |
| `tools/unity.sh` | Unity CLI ラッパー。`compile` / `test <platform> <期待件数>` / `exec <Method>` / `build` |
| `Assets/Scripts/BabyDance.asmdef` | ランタイムアセンブリ。UI・InputSystem・StandaloneFileBrowser を参照 |
| `Assets/Scripts/BeatClock.cs` | dspTime → 拍位置。Unity 非依存 |
| `Assets/Scripts/DanceClipInfo.cs` | ダンス 1 本のメタデータ（ScriptableObject） |
| `Assets/Scripts/DanceDriver.cs` | Animator の手動ティック |
| `Assets/Scripts/AudioFileType.cs` | 拡張子 → `AudioType` |
| `Assets/Scripts/AudioLoader.cs` | ファイル選択・読込・スケジュール再生 |
| `Assets/Scripts/DancePlayer.cs` | 3 部品の配線と Update ループ |
| `Assets/Scripts/DanceUi.cs` | 実行時に uGUI を構築 |
| `Assets/Editor/BabyDance.Editor.asmdef` | Editor アセンブリ（URP Runtime を参照） |
| `Assets/Editor/CharacterImportSettings.cs` | `Assets/Characters/` の FBX を Humanoid・ループ・Bake Into Pose に |
| `Assets/Editor/AssetTools.cs` | 再インポートと検証、AnimatorController と DanceClipInfo 生成・掃除 |
| `Assets/Editor/SceneBuilder.cs` | `Assets/Scenes/Dance.unity` を生成 |
| `Assets/Editor/BuildScript.cs` | Mac ビルド |
| `Assets/Tests/EditMode/*.cs` | BeatClock / AudioFileType / DanceDriver 純粋関数のテスト |
| `Assets/Tests/PlayMode/*.cs` | 無効化 Animator の手動ティック・位相・CrossFade・不正 State のテスト |
| `tools/make_metronome.py` | 検証用 120 BPM メトロノーム WAV（300 秒）生成 |

---

### Task 1: CLI ラッパー、アセンブリ定義、BeatClock

**Files:**
- Create: `tools/unity.sh`
- Create: `Assets/Scripts/BabyDance.asmdef`
- Create: `Assets/Scripts/BeatClock.cs`
- Create: `Assets/Tests/EditMode/BabyDance.Tests.EditMode.asmdef`
- Create: `Assets/Tests/EditMode/BeatClockTests.cs`

**Interfaces:**
- Produces: `tools/unity.sh compile` / `tools/unity.sh test EditMode <n>` / `tools/unity.sh test PlayMode <n>` / `tools/unity.sh exec <Namespace.Class.Method>` / `tools/unity.sh build`。成功時のみ終了コード 0 で `OK ...` を出力。`exec` は対象メソッドがログに `[BabyDance] <Namespace.Class.Method> done` を出すことを要求する。
- Produces: `BabyDance.BeatClock` — `BeatClock(double bpm)`, `void Start(double startDspTime)`, `void Stop()`, `void SetBpm(double bpm, double nowDspTime)`, `double BeatAt(double dspTime)`, `double Bpm { get; }`, `bool IsRunning { get; }`。停止中は `BeatAt` が常に `0`。開始前は負値。

- [ ] **Step 1: CLI ラッパーを書く**

`tools/unity.sh`:

```bash
#!/usr/bin/env bash
# Unity CLI ラッパー。失敗・未実行・判定不能をすべて非 0 終了にする。
set -u
UNITY=/Applications/Unity/Hub/Editor/6000.5.10f1/Unity.app/Contents/MacOS/Unity
cd "$(dirname "$0")/.." || exit 1
mkdir -p Logs

fail() { echo "FAIL: $*" >&2; exit 1; }

# run_unity <log> <unity args...>
run_unity() {
  local log=$1; shift
  rm -f "$log"
  "$UNITY" -batchmode -nographics -projectPath . -logFile "$log" "$@"
  local code=$?
  [ -f "$log" ] || fail "log not written: $log"
  if grep -E "error CS[0-9]+" "$log"; then fail "compile errors (see $log)"; fi
  [ "$code" -eq 0 ] || fail "unity exit code $code (see $log)"
}

case "${1:-}" in
  compile)
    run_unity Logs/compile.log -quit
    echo "OK compile"
    ;;
  test)
    platform=${2:?EditMode|PlayMode}; expected=${3:?expected test count}
    xml=Logs/$platform.xml; rm -f "$xml"
    run_unity "Logs/$platform.log" -runTests -testPlatform "$platform" -testResults "$xml"
    [ -f "$xml" ] || fail "no result xml: $xml"
    summary=$(grep -o '<test-run [^>]*>' "$xml" | head -1)
    total=$(echo "$summary" | grep -o ' total="[0-9]*"' | grep -o '[0-9]*')
    passed=$(echo "$summary" | grep -o ' passed="[0-9]*"' | grep -o '[0-9]*')
    [ "${total:-x}" = "$expected" ] || fail "expected $expected tests, ran '${total:-none}'"
    [ "${passed:-x}" = "$expected" ] || fail "passed ${passed:-0} of $total (see $xml)"
    echo "OK $platform: $passed/$total passed"
    ;;
  exec)
    method=${2:?Namespace.Class.Method}
    run_unity Logs/exec.log -quit -executeMethod "$method"
    grep -E "\[BabyDance\]" Logs/exec.log
    grep -q "\[BabyDance\] $method done" Logs/exec.log || fail "no done marker for $method"
    echo "OK exec $method"
    ;;
  build)
    run_unity Logs/build.log -quit -buildTarget StandaloneOSX -executeMethod BabyDance.Editor.BuildScript.BuildMac
    grep -E "\[BabyDance\]" Logs/build.log
    grep -q "\[BabyDance\] BabyDance.Editor.BuildScript.BuildMac done" Logs/build.log || fail "no done marker for BuildMac"
    [ -d Builds/Mac/BabyDance.app ] || fail "Builds/Mac/BabyDance.app missing"
    echo "OK build"
    ;;
  *)
    fail "usage: tools/unity.sh compile | test <EditMode|PlayMode> <n> | exec <Method> | build"
    ;;
esac
```

```bash
chmod +x tools/unity.sh
```

- [ ] **Step 2: アセンブリ定義を作る**

`Assets/Scripts/BabyDance.asmdef`:

```json
{
    "name": "BabyDance",
    "rootNamespace": "BabyDance",
    "references": [
        "UnityEngine.UI",
        "Unity.InputSystem",
        "StandaloneFileBrowser"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

`Assets/Tests/EditMode/BabyDance.Tests.EditMode.asmdef`:

```json
{
    "name": "BabyDance.Tests.EditMode",
    "rootNamespace": "BabyDance.Tests",
    "references": [
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner",
        "BabyDance"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 3: 失敗するテストを書く**

`Assets/Tests/EditMode/BeatClockTests.cs`:

```csharp
using NUnit.Framework;

namespace BabyDance.Tests
{
    public class BeatClockTests
    {
        [Test]
        public void BeatAdvancesAtConstantBpm()
        {
            var clock = new BeatClock(120);
            clock.Start(10.0);
            Assert.That(clock.BeatAt(10.0), Is.EqualTo(0.0).Within(1e-9));
            Assert.That(clock.BeatAt(11.0), Is.EqualTo(2.0).Within(1e-9));
            Assert.That(clock.BeatAt(40.0), Is.EqualTo(60.0).Within(1e-9));
        }

        [Test]
        public void BeatIsNegativeBeforeStart()
        {
            var clock = new BeatClock(120);
            clock.Start(10.0);
            Assert.That(clock.BeatAt(9.0), Is.EqualTo(-2.0).Within(1e-9));
        }

        [Test]
        public void ChangingBpmKeepsPhaseContinuous()
        {
            var clock = new BeatClock(120);
            clock.Start(0.0);
            // 1 秒後 = 2 拍目で BPM を 60 に落とす
            clock.SetBpm(60, 1.0);
            Assert.That(clock.BeatAt(1.0), Is.EqualTo(2.0).Within(1e-9));
            Assert.That(clock.BeatAt(2.0), Is.EqualTo(3.0).Within(1e-9));
            Assert.That(clock.Bpm, Is.EqualTo(60.0));
        }

        [Test]
        public void StoppedClockReportsZeroAndNotRunning()
        {
            var clock = new BeatClock(120);
            Assert.That(clock.IsRunning, Is.False);
            Assert.That(clock.BeatAt(100.0), Is.EqualTo(0.0));
            clock.Start(0.0);
            Assert.That(clock.IsRunning, Is.True);
            clock.Stop();
            Assert.That(clock.IsRunning, Is.False);
            Assert.That(clock.BeatAt(100.0), Is.EqualTo(0.0));
        }

        [Test]
        public void SetBpmWhileStoppedOnlyChangesRate()
        {
            var clock = new BeatClock(120);
            clock.SetBpm(90, 5.0);
            clock.Start(0.0);
            Assert.That(clock.BeatAt(2.0), Is.EqualTo(3.0).Within(1e-9));
        }
    }
}
```

- [ ] **Step 4: コンパイルして失敗を確認**

```bash
tools/unity.sh compile
```

Expected: `error CS0246 ... 'BeatClock' could not be found` が表示され、`FAIL: compile errors` で終了コード 1。

- [ ] **Step 5: BeatClock を実装**

`Assets/Scripts/BeatClock.cs`:

```csharp
namespace BabyDance
{
    /// <summary>
    /// AudioSettings.dspTime を拍位置に変換する。Unity 非依存。
    /// BPM 変更時は変更時刻の拍位置を保持し、傾きだけ変える。
    /// </summary>
    public sealed class BeatClock
    {
        private double _bpm;
        private double _anchorDsp;
        private double _anchorBeat;

        public BeatClock(double bpm)
        {
            _bpm = bpm;
        }

        public double Bpm => _bpm;
        public bool IsRunning { get; private set; }

        public void Start(double startDspTime)
        {
            _anchorDsp = startDspTime;
            _anchorBeat = 0.0;
            IsRunning = true;
        }

        public void Stop()
        {
            IsRunning = false;
        }

        public void SetBpm(double bpm, double nowDspTime)
        {
            if (IsRunning)
            {
                _anchorBeat = BeatAt(nowDspTime);
                _anchorDsp = nowDspTime;
            }
            _bpm = bpm;
        }

        public double BeatAt(double dspTime)
        {
            if (!IsRunning) return 0.0;
            return _anchorBeat + (dspTime - _anchorDsp) * _bpm / 60.0;
        }
    }
}
```

- [ ] **Step 6: EditMode テストを実行して成功を確認**

```bash
tools/unity.sh test EditMode 5
```

Expected: `OK EditMode: 5/5 passed`。

- [ ] **Step 7: ラッパーの件数ガードが働くことを確認**

```bash
tools/unity.sh test EditMode 99; echo "exit=$?"
```

Expected: `FAIL: expected 99 tests, ran '5'`、`exit=1`。

- [ ] **Step 8: 壊して落ちることを確認**

`SetBpm` の `_anchorBeat = BeatAt(nowDspTime);` を `_anchorBeat = 0.0;` に一時変更して `tools/unity.sh test EditMode 5` を実行。
Expected: `FAIL: passed 4 of 5`。確認後に元へ戻し、`OK EditMode: 5/5 passed` を再確認。

- [ ] **Step 9: コミット**

```bash
git add .gitignore AGENTS.md CLAUDE.md Assets Packages ProjectSettings docs tools
git commit -m "feat: Unity プロジェクト初期化、CLI ラッパー、BeatClock

- URP テンプレートで 6000.5.10f1 プロジェクトを生成
- StandaloneFileBrowser を OpenUPM から追加
- 未実行・判定不能を失敗にする tools/unity.sh
- dspTime から拍位置を返す BeatClock と EditMode テスト

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 2: FBX の Humanoid 自動設定と再インポート

**Files:**
- Create: `Assets/Editor/BabyDance.Editor.asmdef`
- Create: `Assets/Editor/CharacterImportSettings.cs`
- Create: `Assets/Editor/AssetTools.cs`（このタスクでは `ReimportCharacters` まで）

**Interfaces:**
- Produces: `Assets/Characters/*.fbx` がすべて Humanoid、クリップ名 = ファイル名、ループ有効、Root の回転・Y・XZ を Bake Into Pose。
- Produces: `BabyDance.Editor.AssetTools.ReimportCharacters` — 全 FBX を強制再インポートし、Human な Avatar が無い FBX、またはクリップが無いダンス FBX があれば例外を投げる。成功時にログ `[BabyDance] BabyDance.Editor.AssetTools.ReimportCharacters done: N files`。
- Produces: 定数 `AssetTools.CharacterFbx = "Assets/Characters/XBot.fbx"`（キャラ本体。ダンス判定はこのパス以外）。

**人手作業（先に依頼する）:** Mixamo から X Bot（T-pose、**With Skin**、FBX for Unity）を `Assets/Characters/XBot.fbx` に置く。無い場合でもこのタスクは通る（XBot はクリップ必須ではない）。

- [ ] **Step 1: Editor アセンブリ定義を作る**

`Assets/Editor/BabyDance.Editor.asmdef`:

```json
{
    "name": "BabyDance.Editor",
    "rootNamespace": "BabyDance.Editor",
    "references": [
        "BabyDance",
        "Unity.RenderPipelines.Universal.Runtime"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 2: AssetPostprocessor を書く**

`Assets/Editor/CharacterImportSettings.cs`:

```csharp
using System.IO;
using UnityEditor;

namespace BabyDance.Editor
{
    /// <summary>
    /// Assets/Characters/ 配下の FBX を Humanoid にし、
    /// クリップ名をファイル名に統一し、ループとルート固定を設定する。
    /// </summary>
    public sealed class CharacterImportSettings : AssetPostprocessor
    {
        public const string Folder = "Assets/Characters/";

        private bool IsTarget => assetPath.StartsWith(Folder) && assetPath.EndsWith(".fbx");

        private void OnPreprocessModel()
        {
            if (!IsTarget) return;
            var importer = (ModelImporter)assetImporter;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
        }

        private void OnPreprocessAnimation()
        {
            if (!IsTarget) return;
            var importer = (ModelImporter)assetImporter;
            var clips = importer.defaultClipAnimations;
            if (clips.Length == 0) return;
            var name = Path.GetFileNameWithoutExtension(assetPath);
            for (var i = 0; i < clips.Length; i++)
            {
                var c = clips[i];
                c.name = clips.Length == 1 ? name : $"{name}_{i}";
                c.loopTime = true;
                c.lockRootRotation = true;
                c.keepOriginalOrientation = true;
                c.lockRootHeightY = true;
                c.keepOriginalPositionY = true;
                c.lockRootPositionXZ = true;
                c.keepOriginalPositionXZ = true;
            }
            importer.clipAnimations = clips;
        }
    }
}
```

- [ ] **Step 3: 再インポートと検証のメソッドを書く**

`Assets/Editor/AssetTools.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BabyDance.Editor
{
    public static class AssetTools
    {
        private const string Tag = "[BabyDance]";
        public const string CharacterFbx = CharacterImportSettings.Folder + "XBot.fbx";

        public static string[] CharacterFbxPaths() =>
            Directory.GetFiles(CharacterImportSettings.Folder, "*.fbx")
                .Select(p => p.Replace('\\', '/'))
                .OrderBy(p => p)
                .ToArray();

        public static string[] DanceFbxPaths() => CharacterFbxPaths().Where(p => p != CharacterFbx).ToArray();

        public static AnimationClip MainClip(string fbxPath) =>
            AssetDatabase.LoadAllAssetRepresentationsAtPath(fbxPath).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));

        /// <summary>CLI: tools/unity.sh exec BabyDance.Editor.AssetTools.ReimportCharacters</summary>
        public static void ReimportCharacters()
        {
            var paths = CharacterFbxPaths();
            if (paths.Length == 0) throw new InvalidOperationException($"{Tag} no FBX in {CharacterImportSettings.Folder}");

            foreach (var p in paths)
                AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.SaveAssets();

            foreach (var p in paths)
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(p);
                var avatar = AssetDatabase.LoadAllAssetsAtPath(p).OfType<Avatar>().FirstOrDefault();
                var clip = MainClip(p);
                var clipDesc = clip == null ? "none" : $"{clip.name}:{clip.length:F2}s:loop={clip.isLooping}";
                Debug.Log($"{Tag} {p} type={importer.animationType} avatarValid={avatar != null && avatar.isValid} human={avatar != null && avatar.isHuman} clip={clipDesc}");

                if (avatar == null || !avatar.isValid || !avatar.isHuman)
                    throw new InvalidOperationException($"{Tag} {p} has no valid Humanoid avatar");
                if (p != CharacterFbx && (clip == null || !clip.isLooping))
                    throw new InvalidOperationException($"{Tag} {p} has no looping AnimationClip");
            }
            Debug.Log($"{Tag} BabyDance.Editor.AssetTools.ReimportCharacters done: {paths.Length} files");
        }
    }
}
```

- [ ] **Step 4: 再インポートを実行し、レポートを確認**

```bash
tools/unity.sh exec BabyDance.Editor.AssetTools.ReimportCharacters
```

Expected: 各 FBX の行に `type=Human avatarValid=True human=True clip=<ファイル名>:<秒>s:loop=True`、最後に `OK exec ...`。

- [ ] **Step 5: `.meta` に設定が入ったことを確認**

```bash
grep -E "animationType|loopTime|lockRootRotation" Assets/Characters/HouseDancing.fbx.meta
```

Expected: `animationType: 3`、`loopTime: 1`、`lockRootRotation: 1`。

- [ ] **Step 6: ガードが働くことを確認**

```bash
cp Assets/Characters/HouseDancing.fbx Assets/Characters/Broken.fbx && printf 'x' > Assets/Characters/Broken.fbx
tools/unity.sh exec BabyDance.Editor.AssetTools.ReimportCharacters; echo "exit=$?"
rm -f Assets/Characters/Broken.fbx Assets/Characters/Broken.fbx.meta
```

Expected: `exit=1`（`has no valid Humanoid avatar` か `no done marker`）。削除後に Step 4 を再実行して `OK` を確認。

- [ ] **Step 7: コミット**

```bash
git add Assets/Editor Assets/Editor.meta Assets/Characters Assets/Characters.meta
git commit -m "feat: Characters 配下の FBX を Humanoid・ループ・Bake Into Pose に自動設定

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 3: DanceClipInfo と AnimatorController の生成

**Files:**
- Create: `Assets/Scripts/DanceClipInfo.cs`
- Modify: `Assets/Editor/AssetTools.cs`（`BuildDanceAssets` を追加）

**Interfaces:**
- Produces: `BabyDance.DanceClipInfo : ScriptableObject` — `AnimationClip clip`, `string stateName`（完全パス `Base Layer.<name>`）, `int beatsPerLoop`（既定 8）, `float beatOffset`（既定 0、拍単位）。
- Produces: `Assets/Dance/Dance.controller`（`Base Layer` に各ダンスの State。State 名 = クリップ名 = FBX ファイル名）と `Assets/Dance/<name>.asset`。対応する FBX が無い `.asset` は削除される。
- Produces: `BabyDance.Editor.AssetTools.BuildDanceAssets`。既存 `.asset` の `beatsPerLoop` / `beatOffset` は保持し、`clip` と `stateName` だけ更新。ダンス FBX が 0 本、またはクリップが取れない FBX があれば例外。成功ログ `[BabyDance] BabyDance.Editor.AssetTools.BuildDanceAssets done: N states`。
- Produces: 定数 `AssetTools.DanceFolder = "Assets/Dance"`, `AssetTools.ControllerPath = "Assets/Dance/Dance.controller"`。

- [ ] **Step 1: DanceClipInfo を書く**

`Assets/Scripts/DanceClipInfo.cs`:

```csharp
using UnityEngine;

namespace BabyDance
{
    /// <summary>ダンス 1 本のメタデータ。beatsPerLoop と beatOffset は耳合わせで人手調整する。</summary>
    [CreateAssetMenu(menuName = "BabyDance/Dance Clip Info")]
    public sealed class DanceClipInfo : ScriptableObject
    {
        public AnimationClip clip;
        public string stateName;
        [Min(1)] public int beatsPerLoop = 8;
        public float beatOffset;
    }
}
```

- [ ] **Step 2: BuildDanceAssets を AssetTools に追加**

`Assets/Editor/AssetTools.cs` の `using` に `using UnityEditor.Animations;` を足し、クラス内に追加:

```csharp
        public const string DanceFolder = "Assets/Dance";
        public const string ControllerPath = DanceFolder + "/Dance.controller";
        public const string LayerName = "Base Layer";

        /// <summary>CLI: tools/unity.sh exec BabyDance.Editor.AssetTools.BuildDanceAssets</summary>
        public static void BuildDanceAssets()
        {
            var dancePaths = DanceFbxPaths();
            if (dancePaths.Length == 0)
                throw new InvalidOperationException($"{Tag} no dance FBX found in {CharacterImportSettings.Folder}");

            if (!AssetDatabase.IsValidFolder(DanceFolder))
                AssetDatabase.CreateFolder("Assets", "Dance");

            AssetDatabase.DeleteAsset(ControllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var stateMachine = controller.layers[0].stateMachine;
            var keep = new System.Collections.Generic.HashSet<string>();

            foreach (var fbx in dancePaths)
            {
                var clip = MainClip(fbx) ?? throw new InvalidOperationException($"{Tag} no AnimationClip in {fbx}");
                var state = stateMachine.AddState(clip.name);
                state.motion = clip;

                var infoPath = $"{DanceFolder}/{clip.name}.asset";
                keep.Add(infoPath);
                var info = AssetDatabase.LoadAssetAtPath<DanceClipInfo>(infoPath);
                if (info == null)
                {
                    info = ScriptableObject.CreateInstance<DanceClipInfo>();
                    AssetDatabase.CreateAsset(info, infoPath);
                }
                info.clip = clip;
                info.stateName = $"{LayerName}.{clip.name}";
                EditorUtility.SetDirty(info);
                Debug.Log($"{Tag} dance {clip.name} length={clip.length:F2}s beatsPerLoop={info.beatsPerLoop} beatOffset={info.beatOffset}");
            }

            var stale = AssetDatabase.FindAssets("t:DanceClipInfo", new[] { DanceFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !keep.Contains(p))
                .ToArray();
            foreach (var p in stale)
            {
                AssetDatabase.DeleteAsset(p);
                Debug.Log($"{Tag} deleted stale {p}");
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"{Tag} BabyDance.Editor.AssetTools.BuildDanceAssets done: {stateMachine.states.Length} states");
        }
```

- [ ] **Step 3: 実行して確認**

```bash
tools/unity.sh exec BabyDance.Editor.AssetTools.BuildDanceAssets; ls Assets/Dance
```

Expected: `dance HouseDancing ...` などダンス数分の行、`done: 4 states`、`OK exec`。`Assets/Dance` に `Dance.controller` と 4 つの `.asset`。

- [ ] **Step 4: 再実行しても人手値が保持され、孤児が消えることを確認**

```bash
sed -i '' 's/beatsPerLoop: 8/beatsPerLoop: 16/' Assets/Dance/HouseDancing.asset
cp Assets/Dance/HouseDancing.asset Assets/Dance/Orphan.asset
tools/unity.sh exec BabyDance.Editor.AssetTools.BuildDanceAssets | grep -E "dance HouseDancing|deleted stale"
sed -i '' 's/beatsPerLoop: 16/beatsPerLoop: 8/' Assets/Dance/HouseDancing.asset
ls Assets/Dance
```

Expected: `beatsPerLoop=16` の行と `deleted stale Assets/Dance/Orphan.asset`。`ls` に `Orphan.asset` が無い。その後 8 に戻す。

- [ ] **Step 5: コミット**

```bash
git add Assets/Scripts/DanceClipInfo.cs Assets/Scripts/DanceClipInfo.cs.meta Assets/Editor Assets/Dance Assets/Dance.meta
git commit -m "feat: DanceClipInfo と AnimatorController を CLI 生成

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 4: DanceDriver（Animator 手動ティック）

**Files:**
- Create: `Assets/Scripts/DanceDriver.cs`
- Create: `Assets/Tests/EditMode/DanceDriverMathTests.cs`
- Create: `Assets/Tests/PlayMode/BabyDance.Tests.PlayMode.asmdef`
- Create: `Assets/Tests/PlayMode/ManualTickTests.cs`

**Interfaces:**
- Consumes: `DanceClipInfo`（Task 3）。
- Produces: `BabyDance.DanceDriver : MonoBehaviour`（`[RequireComponent(typeof(Animator))]`）— `DanceClipInfo[] dances`, `int CurrentIndex { get; }`, `void SetDance(int index, double currentBeat)`（State が無ければ `InvalidOperationException`）, `void Tick(double currentBeat)`, `static double SecondsPerBeat(DanceClipInfo)`, `static float NormalizedTime(DanceClipInfo, double beat)`。

**この計画の核心リスク:** 無効化した Animator に対する `Play` / `CrossFadeInFixedTime` / `Update` / `GetCurrentAnimatorStateInfo` の組合せは公式に明記された保証が無い。Step 7 の PlayMode テストが全件通ることで初めて確定する。通らなければ**このタスクで止まり、結果をユーザーに報告する**（別方式への変更は再設計）。

- [ ] **Step 1: 純粋関数の失敗テストを書く**

`Assets/Tests/EditMode/DanceDriverMathTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;

namespace BabyDance.Tests
{
    public class DanceDriverMathTests
    {
        private static DanceClipInfo Info(float clipLength, int beatsPerLoop, float beatOffset)
        {
            var clip = new AnimationClip();
            clip.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, clipLength, 1f));
            var info = ScriptableObject.CreateInstance<DanceClipInfo>();
            info.clip = clip;
            info.beatsPerLoop = beatsPerLoop;
            info.beatOffset = beatOffset;
            return info;
        }

        [Test]
        public void SecondsPerBeatIsClipLengthOverBeats()
        {
            Assert.That(DanceDriver.SecondsPerBeat(Info(4f, 8, 0f)), Is.EqualTo(0.5).Within(1e-6));
        }

        [Test]
        public void NormalizedTimeWrapsEveryLoop()
        {
            var info = Info(4f, 8, 0f);
            Assert.That(DanceDriver.NormalizedTime(info, 0.0), Is.EqualTo(0f).Within(1e-6));
            Assert.That(DanceDriver.NormalizedTime(info, 2.0), Is.EqualTo(0.25f).Within(1e-6));
            Assert.That(DanceDriver.NormalizedTime(info, 10.0), Is.EqualTo(0.25f).Within(1e-6));
        }

        [Test]
        public void NormalizedTimeAppliesOffsetAndHandlesNegativeBeats()
        {
            var info = Info(4f, 8, 2f);
            Assert.That(DanceDriver.NormalizedTime(info, 2.0), Is.EqualTo(0f).Within(1e-6));
            Assert.That(DanceDriver.NormalizedTime(info, -1.0), Is.EqualTo(0.625f).Within(1e-6));
        }
    }
}
```

- [ ] **Step 2: コンパイルして失敗を確認**

```bash
tools/unity.sh compile
```

Expected: `'DanceDriver' could not be found` で `FAIL`。

- [ ] **Step 3: DanceDriver を実装**

`Assets/Scripts/DanceDriver.cs`:

```csharp
using System;
using UnityEngine;

namespace BabyDance
{
    /// <summary>
    /// Animator を無効化し、拍の差分だけ手動で Update する。
    /// 時間源は呼び出し側が渡す拍位置のみ。
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public sealed class DanceDriver : MonoBehaviour
    {
        private const int Layer = 0;

        public DanceClipInfo[] dances = Array.Empty<DanceClipInfo>();

        private Animator _animator;
        private DanceClipInfo _current;
        private double _lastBeat;

        public int CurrentIndex { get; private set; } = -1;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            _animator.enabled = false;
            _animator.applyRootMotion = false;
        }

        /// <summary>切替は 1 拍かけてクロスフェードする。初回は即時。</summary>
        public void SetDance(int index, double currentBeat)
        {
            var next = dances[index];
            if (!_animator.HasState(Layer, Animator.StringToHash(next.stateName)))
                throw new InvalidOperationException($"Animator has no state '{next.stateName}'");

            var offsetSeconds = NormalizedTime(next, currentBeat) * next.clip.length;
            if (_current == null)
                _animator.Play(next.stateName, Layer, offsetSeconds / next.clip.length);
            else
                _animator.CrossFadeInFixedTime(next.stateName, (float)SecondsPerBeat(next), Layer, offsetSeconds);

            _current = next;
            CurrentIndex = index;
            _lastBeat = currentBeat;
            _animator.Update(0f);
        }

        public void Tick(double currentBeat)
        {
            if (_current == null) return;
            var deltaBeats = currentBeat - _lastBeat;
            _lastBeat = currentBeat;
            if (deltaBeats <= 0.0) return;
            _animator.Update((float)(deltaBeats * SecondsPerBeat(_current)));
        }

        public static double SecondsPerBeat(DanceClipInfo info) => info.clip.length / info.beatsPerLoop;

        public static float NormalizedTime(DanceClipInfo info, double beat)
        {
            var loops = (beat - info.beatOffset) / info.beatsPerLoop;
            return (float)(loops - Math.Floor(loops));
        }
    }
}
```

- [ ] **Step 4: EditMode テストを実行**

```bash
tools/unity.sh test EditMode 8
```

Expected: `OK EditMode: 8/8 passed`。

- [ ] **Step 5: PlayMode テスト用アセンブリを作る**

`includePlatforms` は空にする（`Editor` を入れると Test Framework が EditMode 扱いにして PlayMode で走らない）。Editor 専用 API はテスト本体で `#if UNITY_EDITOR` に閉じる。

`Assets/Tests/PlayMode/BabyDance.Tests.PlayMode.asmdef`:

```json
{
    "name": "BabyDance.Tests.PlayMode",
    "rootNamespace": "BabyDance.Tests",
    "references": [
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner",
        "BabyDance"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 6: 手動ティックの PlayMode テストを書く**

`Assets/Tests/PlayMode/ManualTickTests.cs`:

```csharp
#if UNITY_EDITOR
using System;
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;

namespace BabyDance.Tests
{
    /// <summary>
    /// 無効化した Animator が手動 Update でだけ進み、位相指定と 1 拍クロスフェードが効くことを確認する。
    /// State A: 1 秒で localPosition.x 0→1。State B: 1 秒で localPosition.y 0→1。どちらも 4 拍/ループ。
    /// </summary>
    public class ManualTickTests
    {
        private const string Layer = "Base Layer";
        private GameObject _go;
        private AnimatorController _controller;
        private DanceDriver _driver;
        private DanceClipInfo _a;
        private DanceClipInfo _b;

        private static AnimationClip Clip(string name, string property)
        {
            var clip = new AnimationClip { name = name };
            clip.SetCurve("", typeof(Transform), property, AnimationCurve.Linear(0f, 0f, 1f, 1f));
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            return clip;
        }

        private static DanceClipInfo Info(AnimationClip clip)
        {
            var info = ScriptableObject.CreateInstance<DanceClipInfo>();
            info.clip = clip;
            info.stateName = $"{Layer}.{clip.name}";
            info.beatsPerLoop = 4;
            return info;
        }

        [SetUp]
        public void SetUp()
        {
            _a = Info(Clip("A", "localPosition.x"));
            _b = Info(Clip("B", "localPosition.y"));

            _controller = new AnimatorController();
            _controller.AddLayer(Layer);
            var sm = _controller.layers[0].stateMachine;
            sm.AddState("A").motion = _a.clip;
            sm.AddState("B").motion = _b.clip;

            _go = new GameObject("dancer");
            _go.AddComponent<Animator>().runtimeAnimatorController = _controller;
            _driver = _go.AddComponent<DanceDriver>();
            _driver.dances = new[] { _a, _b };
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.Destroy(_go);
            UnityEngine.Object.Destroy(_controller);
            UnityEngine.Object.Destroy(_a.clip);
            UnityEngine.Object.Destroy(_b.clip);
            UnityEngine.Object.Destroy(_a);
            UnityEngine.Object.Destroy(_b);
        }

        private AnimatorStateInfo State() => _go.GetComponent<Animator>().GetCurrentAnimatorStateInfo(0);

        [UnityTest]
        public IEnumerator TickAdvancesStateAndTransformByBeatDelta()
        {
            yield return null;
            _driver.SetDance(0, 0.0);
            _driver.Tick(2.0); // 2 拍 = 0.5 秒

            Assert.That(State().normalizedTime, Is.EqualTo(0.5f).Within(0.01f));
            Assert.That(_go.transform.localPosition.x, Is.EqualTo(0.5f).Within(0.01f));
        }

        [UnityTest]
        public IEnumerator AnimatorDoesNotAdvanceOnItsOwn()
        {
            yield return null;
            _driver.SetDance(0, 0.0);
            yield return new WaitForSeconds(0.3f);

            Assert.That(State().normalizedTime, Is.EqualTo(0f).Within(0.01f));
            Assert.That(_go.transform.localPosition.x, Is.EqualTo(0f).Within(0.01f));
        }

        [UnityTest]
        public IEnumerator SetDanceStartsAtPhaseForBeat()
        {
            yield return null;
            _driver.SetDance(0, 3.0); // 3/4 ループ

            Assert.That(State().normalizedTime, Is.EqualTo(0.75f).Within(0.01f));
            Assert.That(_go.transform.localPosition.x, Is.EqualTo(0.75f).Within(0.01f));
        }

        [UnityTest]
        public IEnumerator SwitchingDanceCrossFadesWithinOneBeat()
        {
            yield return null;
            var animator = _go.GetComponent<Animator>();
            _driver.SetDance(0, 0.0);
            _driver.Tick(1.0);
            _driver.SetDance(1, 1.0);

            _driver.Tick(1.5); // 0.5 拍: 遷移中
            Assert.That(animator.IsInTransition(0), Is.True);

            _driver.Tick(2.5); // 合計 1.5 拍: 遷移完了、B が現在 State
            Assert.That(animator.IsInTransition(0), Is.False);
            Assert.That(State().IsName(_b.stateName), Is.True);
            Assert.That(_go.transform.localPosition.y, Is.GreaterThan(0.1f));
        }

        [UnityTest]
        public IEnumerator SetDanceThrowsForMissingState()
        {
            yield return null;
            var missing = Info(Clip("Missing", "localPosition.z"));
            _driver.dances = new[] { missing };

            Assert.Throws<InvalidOperationException>(() => _driver.SetDance(0, 0.0));
            UnityEngine.Object.Destroy(missing.clip);
            UnityEngine.Object.Destroy(missing);
        }
    }
}
#endif
```

- [ ] **Step 7: PlayMode テストを実行**

```bash
tools/unity.sh test PlayMode 5
```

Expected: `OK PlayMode: 5/5 passed`。`FAIL: expected 5 tests, ran 'none'` や `ran '0'` が出た場合はアセンブリが PlayMode 対象になっていない（asmdef を見直す）。テストが Failed なら核心リスクの顕在化なので、ここで止めて報告する。

- [ ] **Step 8: 壊して落ちることを確認**

`Awake` の `_animator.enabled = false;` を `_animator.speed = 0f;` に一時変更して `tools/unity.sh test PlayMode 5` を実行。
Expected: `TickAdvancesStateAndTransformByBeatDelta` が落ちる（normalizedTime が 0 のまま）か `AnimatorDoesNotAdvanceOnItsOwn` が落ちる。確認後に元へ戻し、`OK PlayMode: 5/5 passed` を再確認。

- [ ] **Step 9: コミット**

```bash
git add Assets/Scripts/DanceDriver.cs Assets/Scripts/DanceDriver.cs.meta Assets/Tests
git commit -m "feat: DanceDriver で Animator を拍差分の手動 Update で駆動

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 5: AudioLoader（ファイル選択・読込・スケジュール再生）

**Files:**
- Create: `Assets/Scripts/AudioFileType.cs`
- Create: `Assets/Scripts/AudioLoader.cs`
- Create: `Assets/Tests/EditMode/AudioFileTypeTests.cs`

**Interfaces:**
- Produces: `static AudioType BabyDance.AudioFileType.FromPath(string path)` — `.mp3`→`MPEG`, `.wav`→`WAV`, `.ogg`→`OGGVORBIS`, それ以外→`UNKNOWN`。大文字小文字を区別しない。
- Produces: `BabyDance.AudioLoader : MonoBehaviour`（`[RequireComponent(typeof(AudioSource))]`）— `void OpenFile()`, `double Play()`（開始 dspTime を返す）, `void Stop()`, `bool HasClip`, `bool IsPlaying`, `string ClipName`, `double StartDspTime`（未再生時 `double.NaN`）, `event Action<AudioClip> Loaded`, `event Action<string> Error`。失敗は `Error` 通知と `Debug.LogError` の両方。`const double StartDelay = 1.0`。

- [ ] **Step 1: 失敗テストを書く**

`Assets/Tests/EditMode/AudioFileTypeTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;

namespace BabyDance.Tests
{
    public class AudioFileTypeTests
    {
        [TestCase("/a/b/song.mp3", AudioType.MPEG)]
        [TestCase("/a/b/SONG.MP3", AudioType.MPEG)]
        [TestCase("C:\\music\\loop.wav", AudioType.WAV)]
        [TestCase("/x/y.ogg", AudioType.OGGVORBIS)]
        [TestCase("/x/y.flac", AudioType.UNKNOWN)]
        [TestCase("/x/noext", AudioType.UNKNOWN)]
        public void MapsExtensionToAudioType(string path, AudioType expected)
        {
            Assert.That(AudioFileType.FromPath(path), Is.EqualTo(expected));
        }
    }
}
```

- [ ] **Step 2: コンパイルして失敗を確認**

```bash
tools/unity.sh compile
```

Expected: `'AudioFileType' could not be found` で `FAIL`。

- [ ] **Step 3: AudioFileType を実装**

`Assets/Scripts/AudioFileType.cs`:

```csharp
using System.IO;
using UnityEngine;

namespace BabyDance
{
    public static class AudioFileType
    {
        public static AudioType FromPath(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".mp3": return AudioType.MPEG;
                case ".wav": return AudioType.WAV;
                case ".ogg": return AudioType.OGGVORBIS;
                default: return AudioType.UNKNOWN;
            }
        }
    }
}
```

- [ ] **Step 4: EditMode テストを実行**

```bash
tools/unity.sh test EditMode 14
```

Expected: `OK EditMode: 14/14 passed`（BeatClock 5 + DanceDriverMath 3 + AudioFileType 6）。

- [ ] **Step 5: AudioLoader を実装**

`Assets/Scripts/AudioLoader.cs`:

```csharp
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
```

- [ ] **Step 6: コンパイル確認**

```bash
tools/unity.sh compile
```

Expected: `OK compile`。

- [ ] **Step 7: コミット**

```bash
git add Assets/Scripts/AudioFileType.cs* Assets/Scripts/AudioLoader.cs* Assets/Tests/EditMode/AudioFileTypeTests.cs*
git commit -m "feat: AudioLoader で音楽ファイルを選択・読込・DSP スケジュール再生

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 6: DancePlayer と DanceUi

**Files:**
- Create: `Assets/Scripts/DancePlayer.cs`
- Create: `Assets/Scripts/DanceUi.cs`

**Interfaces:**
- Consumes: `BeatClock`（Task 1）, `DanceDriver`（Task 4）, `AudioLoader`（Task 5）。
- Produces: `BabyDance.DancePlayer : MonoBehaviour` — `AudioLoader audio`, `DanceDriver driver`, `float initialBpm = 120`, `double Bpm`, `bool IsPlaying`, `bool HasClip`, `int DanceCount`, `string DanceName(int)`, `void Open()`, `void TogglePlay()`, `void SetBpm(double)`, `void SelectDance(int)`, `event Action Changed`, `event Action<string> Message`。
- Produces: `BabyDance.DanceUi : MonoBehaviour` — `DancePlayer player`。`Start` で uGUI を構築する。再生ボタンはクリップ未読込のとき `interactable = false`。

**UI の見た目について:** `DefaultControls` を空の `Resources` で使うため、ドロップダウンの矢印・チェックマークは描かれない（Sprite が無い Image は単色矩形）。ボタン・スライダーのノブ・パネルは色を明示して視認性を確保する。MVP ではこれで足りる。

- [ ] **Step 1: DancePlayer を書く**

`Assets/Scripts/DancePlayer.cs`:

```csharp
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
```

- [ ] **Step 2: DanceUi を書く**

`Assets/Scripts/DanceUi.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace BabyDance
{
    /// <summary>実行時に uGUI を組み立てる。開く / 再生・停止 / BPM / ダンス切替 / メッセージ。</summary>
    public sealed class DanceUi : MonoBehaviour
    {
        private const float MinBpm = 60f;
        private const float MaxBpm = 200f;

        public DancePlayer player;

        private Font _font;
        private Button _playButton;
        private Text _playLabel;
        private Text _bpmLabel;
        private Text _message;
        private Slider _bpmSlider;
        private Dropdown _danceDropdown;

        private void Start()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildEventSystem();
            var panel = BuildCanvasAndPanel();

            AddButton(panel, "Open music", player.Open);
            _playButton = AddButton(panel, "Play", player.TogglePlay);
            _playLabel = _playButton.GetComponentInChildren<Text>();

            _bpmLabel = AddText(panel, "");
            _bpmSlider = AddSlider(panel, MinBpm, MaxBpm, (float)player.Bpm, v => player.SetBpm(v));

            var names = new List<string>();
            for (var i = 0; i < player.DanceCount; i++) names.Add(player.DanceName(i));
            _danceDropdown = AddDropdown(panel, names, player.SelectDance);

            _message = AddText(panel, "Open a music file to start");

            player.Changed += Refresh;
            player.Message += msg => _message.text = msg;
            Refresh();
        }

        private void Refresh()
        {
            _playButton.interactable = player.HasClip;
            _playLabel.text = player.IsPlaying ? "Stop" : "Play";
            _bpmLabel.text = $"BPM {player.Bpm:0}";
            _bpmSlider.SetValueWithoutNotify((float)player.Bpm);
            _danceDropdown.SetValueWithoutNotify(player.driver.CurrentIndex);
        }

        private static void BuildEventSystem()
        {
            if (EventSystem.current != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        private static RectTransform BuildCanvasAndPanel()
        {
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);

            var panelGo = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            panelGo.transform.SetParent(canvasGo.transform, false);
            panelGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
            var rect = panelGo.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(16f, 16f);
            rect.sizeDelta = new Vector2(320f, 0f);
            var layout = panelGo.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.spacing = 8f;
            layout.childForceExpandHeight = false;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            panelGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rect;
        }

        private static void Attach(GameObject go, Transform parent, float height)
        {
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredHeight = height;
        }

        private void ApplyFont(GameObject root)
        {
            foreach (var text in root.GetComponentsInChildren<Text>(true))
            {
                text.font = _font;
                text.color = Color.white;
                text.fontSize = 18;
            }
        }

        private Button AddButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = DefaultControls.CreateButton(new DefaultControls.Resources());
            Attach(go, parent, 40f);
            go.GetComponent<Image>().color = new Color(0.2f, 0.45f, 0.9f);
            go.GetComponentInChildren<Text>().text = label;
            ApplyFont(go);
            var button = go.GetComponent<Button>();
            button.onClick.AddListener(onClick);
            return button;
        }

        private Text AddText(Transform parent, string content)
        {
            var go = DefaultControls.CreateText(new DefaultControls.Resources());
            Attach(go, parent, 24f);
            var text = go.GetComponent<Text>();
            text.text = content;
            ApplyFont(go);
            return text;
        }

        private Slider AddSlider(Transform parent, float min, float max, float value, UnityEngine.Events.UnityAction<float> onChanged)
        {
            var go = DefaultControls.CreateSlider(new DefaultControls.Resources());
            Attach(go, parent, 24f);
            var slider = go.GetComponent<Slider>();
            slider.fillRect.GetComponent<Image>().color = new Color(0.2f, 0.45f, 0.9f);
            slider.handleRect.GetComponent<Image>().color = Color.white;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = true;
            slider.SetValueWithoutNotify(value);
            slider.onValueChanged.AddListener(onChanged);
            return slider;
        }

        private Dropdown AddDropdown(Transform parent, List<string> options, UnityEngine.Events.UnityAction<int> onChanged)
        {
            var go = DefaultControls.CreateDropdown(new DefaultControls.Resources());
            Attach(go, parent, 40f);
            go.GetComponent<Image>().color = new Color(0.25f, 0.25f, 0.25f);
            var dropdown = go.GetComponent<Dropdown>();
            dropdown.ClearOptions();
            dropdown.AddOptions(options);
            ApplyFont(go);
            dropdown.onValueChanged.AddListener(onChanged);
            return dropdown;
        }
    }
}
```

- [ ] **Step 3: コンパイル確認**

```bash
tools/unity.sh compile
```

Expected: `OK compile`。

- [ ] **Step 4: コミット**

```bash
git add Assets/Scripts/DancePlayer.cs* Assets/Scripts/DanceUi.cs*
git commit -m "feat: DancePlayer で部品を配線し DanceUi で uGUI を実行時構築

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 7: シーン生成と Mac ビルド

**Files:**
- Create: `Assets/Editor/SceneBuilder.cs`
- Create: `Assets/Editor/BuildScript.cs`

**Interfaces:**
- Consumes: `Assets/Characters/XBot.fbx`（人手）, `Assets/Dance/Dance.controller` と `Assets/Dance/*.asset`（Task 3）, `DanceDriver` / `AudioLoader` / `DancePlayer` / `DanceUi`。
- Produces: `Assets/Scenes/Dance.unity`（Build Settings の唯一のシーン）、`Builds/Mac/BabyDance.app`。
- Produces: `BabyDance.Editor.SceneBuilder.Build`（入力欠落で例外。成功ログ `[BabyDance] BabyDance.Editor.SceneBuilder.Build done`）、`BabyDance.Editor.BuildScript.BuildMac`（失敗で例外。成功ログ `[BabyDance] BabyDance.Editor.BuildScript.BuildMac done`）。

**前提:** `Assets/Characters/XBot.fbx` が存在し、`tools/unity.sh exec BabyDance.Editor.AssetTools.ReimportCharacters` が `OK` であること。無ければこのタスクは開始しない。

- [ ] **Step 1: SceneBuilder を書く**

`Assets/Editor/SceneBuilder.cs`:

```csharp
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BabyDance.Editor
{
    public static class SceneBuilder
    {
        private const string Tag = "[BabyDance]";
        public const string ScenePath = "Assets/Scenes/Dance.unity";

        /// <summary>CLI: tools/unity.sh exec BabyDance.Editor.SceneBuilder.Build</summary>
        public static void Build()
        {
            var characterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetTools.CharacterFbx);
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(AssetTools.ControllerPath);
            var dances = AssetDatabase.FindAssets("t:DanceClipInfo", new[] { AssetTools.DanceFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p)
                .Select(AssetDatabase.LoadAssetAtPath<DanceClipInfo>)
                .ToArray();
            if (characterPrefab == null || controller == null || dances.Length == 0)
                throw new InvalidOperationException($"{Tag} missing inputs: character={characterPrefab != null} controller={controller != null} dances={dances.Length}");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraGo.tag = "MainCamera";
            cameraGo.transform.position = new Vector3(0f, 1.3f, 3.2f);
            cameraGo.transform.LookAt(new Vector3(0f, 0.9f, 0f));
            var camera = cameraGo.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.15f, 0.16f, 0.2f);
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;

            var lightGo = new GameObject("Directional Light", typeof(Light));
            var light = lightGo.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            GameObject.CreatePrimitive(PrimitiveType.Plane).name = "Ground";

            var character = (GameObject)PrefabUtility.InstantiatePrefab(characterPrefab);
            character.name = "Dancer";
            character.GetComponent<Animator>().runtimeAnimatorController = controller;
            var driver = character.AddComponent<DanceDriver>();
            driver.dances = dances;

            var playerGo = new GameObject("Player", typeof(AudioSource), typeof(AudioLoader), typeof(DancePlayer), typeof(DanceUi));
            var player = playerGo.GetComponent<DancePlayer>();
            player.audio = playerGo.GetComponent<AudioLoader>();
            player.driver = driver;
            playerGo.GetComponent<DanceUi>().player = player;

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException($"{Tag} failed to save {ScenePath}");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log($"{Tag} BabyDance.Editor.SceneBuilder.Build done: {ScenePath} dances={dances.Length}");
        }
    }
}
```

- [ ] **Step 2: BuildScript を書く**

`Assets/Editor/BuildScript.cs`:

```csharp
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BabyDance.Editor
{
    public static class BuildScript
    {
        private const string Tag = "[BabyDance]";

        /// <summary>CLI: tools/unity.sh build</summary>
        public static void BuildMac()
        {
            var options = new BuildPlayerOptions
            {
                scenes = new[] { SceneBuilder.ScenePath },
                locationPathName = "Builds/Mac/BabyDance.app",
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.None,
            };
            var summary = BuildPipeline.BuildPlayer(options).summary;
            Debug.Log($"{Tag} BuildMac result={summary.result} size={summary.totalSize} errors={summary.totalErrors} path={summary.outputPath}");
            if (summary.result != BuildResult.Succeeded)
                throw new BuildFailedException($"{Tag} build failed: {summary.result}");
            Debug.Log($"{Tag} BabyDance.Editor.BuildScript.BuildMac done");
        }
    }
}
```

- [ ] **Step 3: コンパイル確認**

```bash
tools/unity.sh compile
```

Expected: `OK compile`。

- [ ] **Step 4: シーンを生成**

```bash
tools/unity.sh exec BabyDance.Editor.SceneBuilder.Build; ls Assets/Scenes
```

Expected: `Build done: Assets/Scenes/Dance.unity dances=4`、`OK exec`。`missing inputs` なら XBot.fbx か Task 3 の成果物が無い。

- [ ] **Step 5: 不要なテンプレート資産を削除**

```bash
rm -rf Assets/Scenes/SampleScene.unity Assets/Scenes/SampleScene.unity.meta Assets/TutorialInfo Assets/TutorialInfo.meta Assets/Readme.asset Assets/Readme.asset.meta
tools/unity.sh compile
```

Expected: `OK compile`。

- [ ] **Step 6: Mac ビルド**

```bash
tools/unity.sh build
```

Expected: `BuildMac result=Succeeded ... errors=0`、`OK build`。

- [ ] **Step 7: コミット**

```bash
git add -A Assets
git commit -m "feat: Dance シーンの CLI 生成と Mac ビルドスクリプト

- テンプレートの SampleScene / TutorialInfo / Readme を削除

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 8: メトロノーム音源と手動検証

**Files:**
- Create: `tools/make_metronome.py`
- Modify: `.gitignore`（`tools/*.wav` を追加）

**Interfaces:**
- Produces: `tools/metronome_120bpm.wav`（300 秒、120 BPM、4 拍ごとにアクセント）。仕様の「5 分再生してドリフトが見えない」を確認する音源。

- [ ] **Step 1: 生成スクリプトを書く**

`tools/make_metronome.py`:

```python
# /// script
# requires-python = ">=3.11"
# dependencies = ["numpy"]
# ///
"""120 BPM のメトロノーム WAV（300 秒）を生成する。1 拍目は高い音、2〜4 拍目は低い音。"""
import wave
from pathlib import Path

import numpy as np

BPM = 120
SECONDS = 300
RATE = 44100
CLICK_SECONDS = 0.04


def click(freq: float) -> np.ndarray:
    t = np.arange(int(RATE * CLICK_SECONDS)) / RATE
    envelope = np.exp(-t * 80)
    return (np.sin(2 * np.pi * freq * t) * envelope).astype(np.float32)


def main() -> None:
    samples = np.zeros(RATE * SECONDS, dtype=np.float32)
    beat_len = 60.0 / BPM
    beat = 0
    while beat * beat_len < SECONDS:
        start = int(beat * beat_len * RATE)
        c = click(1760.0 if beat % 4 == 0 else 880.0)
        end = min(start + len(c), len(samples))
        samples[start:end] += c[: end - start]
        beat += 1
    pcm = (np.clip(samples, -1, 1) * 32767).astype("<i2")
    out = Path(__file__).with_name(f"metronome_{BPM}bpm.wav")
    with wave.open(str(out), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(pcm.tobytes())
    print(out, len(pcm) / RATE, "sec")


if __name__ == "__main__":
    main()
```

- [ ] **Step 2: 生成して確認**

```bash
echo 'tools/*.wav' >> .gitignore
uv run tools/make_metronome.py
uv run --with numpy python -c "import wave;w=wave.open('tools/metronome_120bpm.wav');print(w.getframerate(),w.getnframes()/w.getframerate())"
```

Expected: `44100 300.0`。

- [ ] **Step 3: 人手検証を依頼する**

ユーザーに次を依頼し、結果を受け取る:

1. `Builds/Mac/BabyDance.app` を起動する。「Play」ボタンは灰色（無効）になっている。
2. 「Open music」で `tools/metronome_120bpm.wav` を選ぶ。メッセージに `Loaded metronome_120bpm.wav` と出て「Play」が押せるようになる。
3. BPM スライダーを 120 にして「Play」。1 秒後にクリックが鳴り始め、X Bot が踊る。
4. 4 ダンスをドロップダウンで切り替え、それぞれで「アクセント（高い音）とステップの頭が揃うか」を確認する。ずれていれば、ダンス名と「何拍ずらせば揃うか」を報告する（`beatOffset` に反映）。1 ループが 8 拍でないと感じたら「16 拍っぽい」等を報告する（`beatsPerLoop` に反映）。
5. **5 分間そのまま再生し**、開始直後・2 分半・終了直前の 3 点で、アクセントとステップの頭のずれが広がっていないことを確認する（ドリフト検証）。
6. 300 秒再生し終わったとき、ボタンが自動で「Play」に戻る。
7. 再生中に BPM を 100 に変えると、踊りが位相を飛ばさずゆっくりになる。

- [ ] **Step 4: 報告に基づいて DanceClipInfo を調整**

報告された値を `.asset` に反映する。例（HouseDancing を 16 拍・オフセット 2 拍にする場合）:

```bash
sed -i '' 's/beatsPerLoop: 8/beatsPerLoop: 16/; s/beatOffset: 0/beatOffset: 2/' Assets/Dance/HouseDancing.asset
tools/unity.sh build
```

Step 3 の 4 を再確認する。

- [ ] **Step 5: コミット**

```bash
git add .gitignore tools/make_metronome.py Assets/Dance
git commit -m "feat: 検証用メトロノーム生成と DanceClipInfo の拍調整

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

## 自己レビュー結果

- **仕様カバレッジ:** AudioLoader → Task 5、BeatClock → Task 1、DanceDriver → Task 4、DanceClipInfo → Task 3、UI 4 要素 → Task 6、エラー処理（キャンセル無視・読込失敗をログと画面に表示・未読込で再生ボタン無効）→ Task 5/6、EditMode/PlayMode テスト → Task 1/4/5、5 分ドリフト手動検証 → Task 8、人手作業（XBot 配置・拍調整）→ Task 2/8、Humanoid 自動化 → Task 2。
- **Codex レビューへの対応:** 重大 1〜5、中 6〜12、軽微 14 を反映。軽微 13（壊れた WAV の PlayMode テスト）はテスト用音源フィクスチャの追加コストに対して得るものが小さいため見送り、`clip.length <= 0` のガードと手動検証で代替する。
- **型整合:** `DanceClipInfo.stateName`（完全パス）/ `beatsPerLoop` / `beatOffset`、`DanceDriver.SetDance(int,double)` / `Tick(double)` / `CurrentIndex`、`AudioLoader.Play()->double` / `IsPlaying` / `HasClip` / `ClipName`、`DancePlayer.HasClip` / `DanceName(int)`、`AssetTools.CharacterFbx` / `ControllerPath` / `DanceFolder` / `LayerName` / `DanceFbxPaths()` を Task 間で同名で使用していることを確認。完了マーカーは `[BabyDance] <完全メソッド名> done` で `tools/unity.sh exec` の要求と一致。
