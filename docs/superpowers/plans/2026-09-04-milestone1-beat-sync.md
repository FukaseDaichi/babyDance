# マイルストーン 1: BPM 既知の曲に合わせて X Bot が踊る — 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** ユーザーが選んだ音楽ファイルを再生し、ユーザー入力の BPM に合わせて Mixamo X Bot がドリフトなしで踊る Unity アプリ（Mac 実行ファイル）を作る。

**Architecture:** `AudioLoader`（ファイル選択と DSP スケジュール再生）→ `BeatClock`（dspTime から拍位置を返す純粋 C#）→ `DanceDriver`（無効化した Animator を拍差分で手動 `Update`）の 3 部品を `DancePlayer` が束ね、`DanceUi` が実行時に uGUI を組み立てる。FBX の Humanoid 化、AnimatorController と DanceClipInfo の生成、シーン構築、ビルドはすべて `Assets/Editor` のスクリプトを CLI（`-executeMethod`）から呼んで行う。

**Tech Stack:** Unity 6000.5.10f1（URP、Input System のみ有効）、uGUI 2.5.0、StandaloneFileBrowser 1.3.4（OpenUPM）、Unity Test Framework 1.7.0（NUnit）、Python は `uv` 経由。

**Spec:** `docs/superpowers/specs/2026-09-03-milestone1-beat-sync-design.md`

## Global Constraints

- Unity 操作は CLI 第一。`AGENTS.md` の「Unity の CLI 運用ルール」に従う。**終了コード 0 だけで成功と判定しない。** ログを grep し、テスト結果 XML が存在することを確認する。
- 時間源は `AudioSettings.dspTime` のみ。`Time.deltaTime` を同期に使わない。
- 後方互換・フォールバックを書かない。不要になったコードは削除する。
- Python は `uv run` で実行する。
- 各タスクの最後にコミットする。コミットメッセージ末尾に `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>` を付ける。
- スコープ外: Meshy キャラ、BPM 自動検出、WebGL、Windows ビルド、シーク／プレイリスト。

## 共通コマンド

以下を各タスクで使う。リポジトリルートで実行する。`Logs/` は gitignore 済み。

```bash
UNITY=/Applications/Unity/Hub/Editor/6000.5.10f1/Unity.app/Contents/MacOS/Unity
```

コンパイル確認（`error CS` が 1 行も出なければ OK）:

```bash
$UNITY -batchmode -quit -nographics -projectPath . -logFile Logs/compile.log; echo EXIT=$?; grep -E "error CS" Logs/compile.log || echo COMPILE-OK
```

EditMode テスト（XML が無ければ失敗。`result=` の内訳が `Passed` だけなら OK）:

```bash
rm -f Logs/edit.xml; $UNITY -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults Logs/edit.xml -logFile Logs/edit.log; echo EXIT=$?; test -f Logs/edit.xml || echo NO-XML; grep -o 'result="[A-Za-z]*"' Logs/edit.xml | sort | uniq -c; grep -E "error CS" Logs/edit.log
```

PlayMode テスト（同上、`-testPlatform PlayMode`、XML は `Logs/play.xml`、ログは `Logs/play.log`）。

Editor メソッド実行:

```bash
$UNITY -batchmode -quit -nographics -projectPath . -executeMethod <Namespace.Class.Method> -logFile Logs/exec.log; echo EXIT=$?; grep -E "error CS|Exception:|\[BabyDance\]" Logs/exec.log
```

## ファイル構成

| パス | 責務 |
| --- | --- |
| `Assets/Scripts/BabyDance.asmdef` | ランタイムアセンブリ。UI・InputSystem・StandaloneFileBrowser を参照 |
| `Assets/Scripts/BeatClock.cs` | dspTime → 拍位置。Unity 非依存 |
| `Assets/Scripts/DanceClipInfo.cs` | ダンス 1 本のメタデータ（ScriptableObject） |
| `Assets/Scripts/DanceDriver.cs` | Animator の手動ティック |
| `Assets/Scripts/AudioFileType.cs` | 拡張子 → `AudioType` |
| `Assets/Scripts/AudioLoader.cs` | ファイル選択・読込・スケジュール再生 |
| `Assets/Scripts/DancePlayer.cs` | 3 部品の配線と Update ループ |
| `Assets/Scripts/DanceUi.cs` | 実行時に uGUI を構築 |
| `Assets/Editor/BabyDance.Editor.asmdef` | Editor アセンブリ |
| `Assets/Editor/CharacterImportSettings.cs` | `Assets/Characters/` の FBX を Humanoid・ループ・Bake Into Pose に |
| `Assets/Editor/AssetTools.cs` | 再インポートと状態レポート、AnimatorController と DanceClipInfo 生成 |
| `Assets/Editor/SceneBuilder.cs` | `Assets/Scenes/Dance.unity` を生成 |
| `Assets/Editor/BuildScript.cs` | Mac ビルド |
| `Assets/Tests/EditMode/*.cs` | BeatClock / AudioFileType / DanceDriver 純粋関数のテスト |
| `Assets/Tests/PlayMode/*.cs` | 手動ティックで Animator が進むことのテスト |
| `tools/make_metronome.py` | 検証用 120 BPM メトロノーム WAV 生成 |

---

### Task 1: アセンブリ定義と BeatClock

**Files:**
- Create: `Assets/Scripts/BabyDance.asmdef`
- Create: `Assets/Scripts/BeatClock.cs`
- Create: `Assets/Tests/EditMode/BabyDance.Tests.EditMode.asmdef`
- Create: `Assets/Tests/EditMode/BeatClockTests.cs`

**Interfaces:**
- Produces: `BabyDance.BeatClock` — `BeatClock(double bpm)`, `void Start(double startDspTime)`, `void Stop()`, `void SetBpm(double bpm, double nowDspTime)`, `double BeatAt(double dspTime)`, `double Bpm { get; }`, `bool IsRunning { get; }`。停止中は `BeatAt` が常に `0`。開始前は負値。

- [ ] **Step 1: アセンブリ定義を作る**

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

- [ ] **Step 2: 失敗するテストを書く**

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

- [ ] **Step 3: コンパイルして失敗を確認**

「共通コマンド」のコンパイル確認を実行。
Expected: `error CS0246: The type or namespace name 'BeatClock' could not be found` が出る。

- [ ] **Step 4: BeatClock を実装**

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

- [ ] **Step 5: EditMode テストを実行して成功を確認**

「共通コマンド」の EditMode テストを実行。
Expected: XML が存在し、`result="Passed"` のみ（test-case 5 件 + suite 行）。`error CS` なし。

- [ ] **Step 6: 壊して落ちることを確認**

`SetBpm` の `_anchorBeat = BeatAt(nowDspTime);` を `_anchorBeat = 0.0;` に一時変更してテスト実行。
Expected: `ChangingBpmContinuous` が `result="Failed"` になる。確認後に元へ戻す。

- [ ] **Step 7: コミット**

```bash
git add .gitignore AGENTS.md CLAUDE.md Assets Packages ProjectSettings docs
git commit -m "feat: Unity プロジェクト初期化と BeatClock

- URP テンプレートで 6000.5.10f1 プロジェクトを生成
- StandaloneFileBrowser を OpenUPM から追加
- dspTime から拍位置を返す BeatClock と EditMode テスト

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 2: FBX の Humanoid 自動設定と再インポート

**Files:**
- Create: `Assets/Editor/BabyDance.Editor.asmdef`
- Create: `Assets/Editor/CharacterImportSettings.cs`
- Create: `Assets/Editor/AssetTools.cs`（このタスクでは `ReimportCharacters` のみ）

**Interfaces:**
- Produces: `Assets/Characters/*.fbx` がすべて Humanoid、クリップ名 = ファイル名、ループ有効、Root の回転・Y・XZ を Bake Into Pose。`BabyDance.Editor.AssetTools.ReimportCharacters` を `-executeMethod` で呼べる。

**人手作業（先に依頼する）:** Mixamo から X Bot（T-pose、**With Skin**、FBX for Unity）を `Assets/Characters/XBot.fbx` に置く。無い場合でもダンス 4 本の再インポートまではこのタスクで進められる。

- [ ] **Step 1: Editor アセンブリ定義を作る**

`Assets/Editor/BabyDance.Editor.asmdef`:

```json
{
    "name": "BabyDance.Editor",
    "rootNamespace": "BabyDance.Editor",
    "references": [
        "BabyDance"
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
    /// クリップをファイル名で 1 本に統一し、ループとルート固定を設定する。
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

- [ ] **Step 3: 再インポートとレポートのメソッドを書く**

`Assets/Editor/AssetTools.cs`:

```csharp
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BabyDance.Editor
{
    public static class AssetTools
    {
        private const string Tag = "[BabyDance]";

        public static string[] CharacterFbxPaths() =>
            Directory.GetFiles(CharacterImportSettings.Folder, "*.fbx")
                .Select(p => p.Replace('\\', '/'))
                .OrderBy(p => p)
                .ToArray();

        /// <summary>CLI: -executeMethod BabyDance.Editor.AssetTools.ReimportCharacters</summary>
        public static void ReimportCharacters()
        {
            var paths = CharacterFbxPaths();
            foreach (var p in paths)
                AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.SaveAssets();

            foreach (var p in paths)
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(p);
                var avatar = AssetDatabase.LoadAllAssetsAtPath(p).OfType<Avatar>().FirstOrDefault();
                var clips = AssetDatabase.LoadAllAssetRepresentationsAtPath(p).OfType<AnimationClip>()
                    .Where(c => !c.name.StartsWith("__preview__")).ToArray();
                var clipDesc = string.Join(",", clips.Select(c => $"{c.name}:{c.length:F2}s:loop={c.isLooping}"));
                Debug.Log($"{Tag} {p} type={importer.animationType} avatarValid={(avatar != null && avatar.isValid)} human={(avatar != null && avatar.isHuman)} clips=[{clipDesc}]");
            }
            Debug.Log($"{Tag} ReimportCharacters done: {paths.Length} files");
        }
    }
}
```

- [ ] **Step 4: 再インポートを実行し、レポートを確認**

```bash
$UNITY -batchmode -quit -nographics -projectPath . -executeMethod BabyDance.Editor.AssetTools.ReimportCharacters -logFile Logs/exec.log; echo EXIT=$?; grep -E "error CS|Exception:|\[BabyDance\]" Logs/exec.log
```

Expected: `error CS` なし。各 FBX の行に `type=Human avatarValid=True human=True` と `clips=[<ファイル名>:<秒>s:loop=True]`。末尾に `ReimportCharacters done: N files`（`[BabyDance]` 行が 1 行も無ければメソッドが走っていないので失敗扱い）。

- [ ] **Step 5: `.meta` に設定が入ったことを確認**

```bash
grep -E "animationType|loopTime|lockRootRotation" Assets/Characters/HouseDancing.fbx.meta
```

Expected: `animationType: 3`、`loopTime: 1`、`lockRootRotation: 1`。

- [ ] **Step 6: コミット**

```bash
git add Assets/Editor Assets/Characters
git commit -m "feat: Characters 配下の FBX を Humanoid・ループ・Bake Into Pose に自動設定

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 3: DanceClipInfo と AnimatorController の生成

**Files:**
- Create: `Assets/Scripts/DanceClipInfo.cs`
- Modify: `Assets/Editor/AssetTools.cs`（`BuildDanceAssets` を追加）

**Interfaces:**
- Produces: `BabyDance.DanceClipInfo : ScriptableObject` — `AnimationClip clip`, `string stateName`, `int beatsPerLoop`（既定 8）, `float beatOffset`（既定 0、拍単位）。
- Produces: `Assets/Dance/Dance.controller`（レイヤー 0 に各ダンスの State。State 名 = クリップ名 = FBX ファイル名）と `Assets/Dance/<name>.asset`（DanceClipInfo）。`XBot.fbx` は除外。
- Produces: `BabyDance.Editor.AssetTools.BuildDanceAssets` を `-executeMethod` で呼べる。既存の `.asset` があれば `beatsPerLoop` / `beatOffset` を保持して `clip` と `stateName` だけ更新する。

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

`Assets/Editor/AssetTools.cs` の `using` に `UnityEditor.Animations;` を足し、クラス内に追加:

```csharp
        public const string DanceFolder = "Assets/Dance";
        public const string ControllerPath = DanceFolder + "/Dance.controller";
        public const string CharacterFbx = CharacterImportSettings.Folder + "XBot.fbx";

        public static AnimationClip MainClip(string fbxPath) =>
            AssetDatabase.LoadAllAssetRepresentationsAtPath(fbxPath).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));

        /// <summary>CLI: -executeMethod BabyDance.Editor.AssetTools.BuildDanceAssets</summary>
        public static void BuildDanceAssets()
        {
            if (!AssetDatabase.IsValidFolder(DanceFolder))
                AssetDatabase.CreateFolder("Assets", "Dance");

            var dancePaths = CharacterFbxPaths().Where(p => p != CharacterFbx).ToArray();
            if (dancePaths.Length == 0)
            {
                Debug.LogError($"{Tag} no dance FBX found in {CharacterImportSettings.Folder}");
                return;
            }

            AssetDatabase.DeleteAsset(ControllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var layer = controller.layers[0].stateMachine;

            foreach (var fbx in dancePaths)
            {
                var clip = MainClip(fbx);
                if (clip == null)
                {
                    Debug.LogError($"{Tag} no AnimationClip in {fbx}");
                    continue;
                }
                var state = layer.AddState(clip.name);
                state.motion = clip;

                var infoPath = $"{DanceFolder}/{clip.name}.asset";
                var info = AssetDatabase.LoadAssetAtPath<DanceClipInfo>(infoPath);
                if (info == null)
                {
                    info = ScriptableObject.CreateInstance<DanceClipInfo>();
                    AssetDatabase.CreateAsset(info, infoPath);
                }
                info.clip = clip;
                info.stateName = clip.name;
                EditorUtility.SetDirty(info);
                Debug.Log($"{Tag} dance {clip.name} length={clip.length:F2}s beatsPerLoop={info.beatsPerLoop} beatOffset={info.beatOffset}");
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"{Tag} BuildDanceAssets done: {layer.states.Length} states in {ControllerPath}");
        }
```

- [ ] **Step 3: 実行して確認**

```bash
$UNITY -batchmode -quit -nographics -projectPath . -executeMethod BabyDance.Editor.AssetTools.BuildDanceAssets -logFile Logs/exec.log; echo EXIT=$?; grep -E "error CS|Exception:|\[BabyDance\]" Logs/exec.log; ls Assets/Dance
```

Expected: `dance HouseDancing ...` などダンス数分の行、`BuildDanceAssets done: 4 states`（XBot が無い時点では 4、あれば XBot が除外されて同じく 4）。`Assets/Dance` に `Dance.controller` と 4 つの `.asset`。

- [ ] **Step 4: 再実行しても人手値が保持されることを確認**

```bash
sed -i '' 's/beatsPerLoop: 8/beatsPerLoop: 16/' Assets/Dance/HouseDancing.asset
$UNITY -batchmode -quit -nographics -projectPath . -executeMethod BabyDance.Editor.AssetTools.BuildDanceAssets -logFile Logs/exec.log; grep "dance HouseDancing" Logs/exec.log
sed -i '' 's/beatsPerLoop: 16/beatsPerLoop: 8/' Assets/Dance/HouseDancing.asset
```

Expected: 1 回目の grep に `beatsPerLoop=16`。その後 8 に戻す。

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
- Produces: `BabyDance.DanceDriver : MonoBehaviour`（`[RequireComponent(typeof(Animator))]`）— `DanceClipInfo[] dances`, `int CurrentIndex { get; }`, `void SetDance(int index, double currentBeat)`, `void Tick(double currentBeat)`, `static double SecondsPerBeat(DanceClipInfo)`, `static float NormalizedTime(DanceClipInfo, double beat)`。

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
            var curve = AnimationCurve.Linear(0f, 0f, clipLength, 1f);
            clip.SetCurve("", typeof(Transform), "localPosition.x", curve);
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

コンパイル確認を実行。Expected: `'DanceDriver' could not be found`。

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
        private const float CrossFadeBeats = 1f;

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

        public void SetDance(int index, double currentBeat)
        {
            _current = dances[index];
            CurrentIndex = index;
            var normalized = NormalizedTime(_current, currentBeat);
            if (_animator.GetCurrentAnimatorStateInfo(0).length > 0f)
            {
                var duration = CrossFadeBeats / _current.beatsPerLoop;
                _animator.CrossFade(_current.stateName, duration, 0, normalized);
            }
            else
            {
                _animator.Play(_current.stateName, 0, normalized);
            }
            _animator.Update(0f);
            _lastBeat = currentBeat;
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

Expected: BeatClock 5 件 + DanceDriverMath 3 件がすべて `Passed`。

- [ ] **Step 5: PlayMode テスト用アセンブリを作る**

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

- [ ] **Step 6: 手動ティックの PlayMode テストを書く**

`Assets/Tests/PlayMode/ManualTickTests.cs`:

```csharp
using System.Collections;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;

namespace BabyDance.Tests
{
    public class ManualTickTests
    {
        private const string StateName = "Loop";

        private static (GameObject go, DanceDriver driver, DanceClipInfo info) Build(float clipLength, int beatsPerLoop)
        {
            var clip = new AnimationClip { name = StateName };
            clip.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, clipLength, 1f));
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            var controller = new AnimatorController();
            controller.AddLayer("Base");
            var state = controller.layers[0].stateMachine.AddState(StateName);
            state.motion = clip;

            var info = ScriptableObject.CreateInstance<DanceClipInfo>();
            info.clip = clip;
            info.stateName = StateName;
            info.beatsPerLoop = beatsPerLoop;

            var go = new GameObject("dancer");
            var animator = go.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            var driver = go.AddComponent<DanceDriver>();
            driver.dances = new[] { info };
            return (go, driver, info);
        }

        [UnityTest]
        public IEnumerator TickAdvancesAnimatorByBeatDelta()
        {
            var (go, driver, _) = Build(clipLength: 1f, beatsPerLoop: 4);
            yield return null; // Awake 完了

            driver.SetDance(0, 0.0);
            driver.Tick(2.0); // 2 拍 = 0.5 秒

            var state = go.GetComponent<Animator>().GetCurrentAnimatorStateInfo(0);
            Assert.That(state.normalizedTime, Is.EqualTo(0.5f).Within(0.01f));
            Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator AnimatorDoesNotAdvanceOnItsOwn()
        {
            var (go, driver, _) = Build(clipLength: 1f, beatsPerLoop: 4);
            yield return null;

            driver.SetDance(0, 0.0);
            yield return new WaitForSeconds(0.3f);

            var state = go.GetComponent<Animator>().GetCurrentAnimatorStateInfo(0);
            Assert.That(state.normalizedTime, Is.EqualTo(0f).Within(0.01f));
            Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator SetDanceStartsAtPhaseForBeat()
        {
            var (go, driver, _) = Build(clipLength: 1f, beatsPerLoop: 4);
            yield return null;

            driver.SetDance(0, 3.0); // 3/4 ループ
            var state = go.GetComponent<Animator>().GetCurrentAnimatorStateInfo(0);
            Assert.That(state.normalizedTime, Is.EqualTo(0.75f).Within(0.01f));
            Object.Destroy(go);
        }
    }
}
```

- [ ] **Step 7: PlayMode テストを実行**

```bash
rm -f Logs/play.xml; $UNITY -batchmode -nographics -projectPath . -runTests -testPlatform PlayMode -testResults Logs/play.xml -logFile Logs/play.log; echo EXIT=$?; test -f Logs/play.xml || echo NO-XML; grep -o 'result="[A-Za-z]*"' Logs/play.xml | sort | uniq -c; grep -E "error CS" Logs/play.log
```

Expected: XML あり、`Passed` のみ。

- [ ] **Step 8: 壊して落ちることを確認**

`Awake` の `_animator.enabled = false;` を `_animator.speed = 0f;` に一時変更して PlayMode テストを実行。
Expected: `TickAdvancesAnimatorByBeatDelta` が Failed（normalizedTime が 0 のまま）。確認後に元へ戻し、再度 Passed を確認。

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
- Produces: `BabyDance.AudioLoader : MonoBehaviour`（`[RequireComponent(typeof(AudioSource))]`）— `void OpenFile()`, `double Play()`（開始 dspTime を返す）, `void Stop()`, `bool HasClip`, `bool IsPlaying`, `double StartDspTime`（未再生時 `double.NaN`）, `event Action<AudioClip> Loaded`, `event Action<string> Error`。`const double StartDelay = 1.0`。

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

Expected: `'AudioFileType' could not be found`。

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

Expected: 全件 Passed（BeatClock 5 + DanceDriverMath 3 + AudioFileType 6）。

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

        private IEnumerator Load(string path)
        {
            var type = AudioFileType.FromPath(path);
            if (type == AudioType.UNKNOWN)
            {
                Error?.Invoke($"Unsupported file: {Path.GetFileName(path)}");
                yield break;
            }

            Stop();
            using var request = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, type);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Error?.Invoke($"Load failed: {request.error}");
                yield break;
            }

            var clip = DownloadHandlerAudioClip.GetContent(request);
            clip.name = Path.GetFileName(path);
            if (_source.clip != null) Destroy(_source.clip);
            _source.clip = clip;
            Loaded?.Invoke(clip);
        }
    }
}
```

- [ ] **Step 6: コンパイル確認**

Expected: `error CS` なし（`SFB` 名前空間と `UnityEngine.Networking` が解決される）。

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
- Produces: `BabyDance.DancePlayer : MonoBehaviour` — `AudioLoader audio`, `DanceDriver driver`, `float initialBpm = 120`, `double Bpm`, `bool IsPlaying`, `string ClipName`, `void Open()`, `void TogglePlay()`, `void SetBpm(double)`, `void SelectDance(int)`, `event Action Changed`, `event Action<string> Message`。
- Produces: `BabyDance.DanceUi : MonoBehaviour` — `DancePlayer player`。`Start` で uGUI を構築する。

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
        public string ClipName => audio.HasClip ? audio.GetComponent<AudioSource>().clip.name : "";
        public int DanceCount => driver.dances.Length;
        public string DanceName(int index) => driver.dances[index].stateName;

        private void Awake()
        {
            _clock = new BeatClock(initialBpm);
            audio.Loaded += _ => { Message?.Invoke($"Loaded {ClipName}"); Changed?.Invoke(); };
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
            if (!audio.HasClip)
            {
                Message?.Invoke("Open a music file first");
                return;
            }
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
    /// <summary>実行時に uGUI を組み立てる。開く / 再生・停止 / BPM / ダンス切替 / メッセージの 5 要素。</summary>
    public sealed class DanceUi : MonoBehaviour
    {
        private const float MinBpm = 60f;
        private const float MaxBpm = 200f;

        public DancePlayer player;

        private Font _font;
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
            _playLabel = AddButton(panel, "Play", player.TogglePlay);

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
            _playLabel.text = player.IsPlaying ? "Stop" : "Play";
            _bpmLabel.text = $"BPM {player.Bpm:0}";
            _bpmSlider.SetValueWithoutNotify((float)player.Bpm);
            _danceDropdown.SetValueWithoutNotify(player.driver.CurrentIndex);
        }

        private static void BuildEventSystem()
        {
            if (EventSystem.current != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        private static RectTransform BuildCanvasAndPanel()
        {
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);

            var panelGo = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            panelGo.transform.SetParent(canvasGo.transform, false);
            panelGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);
            var rect = panelGo.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
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
            var element = go.AddComponent<LayoutElement>();
            element.preferredHeight = height;
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

        private Text AddButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = DefaultControls.CreateButton(new DefaultControls.Resources());
            Attach(go, parent, 40f);
            go.GetComponent<Image>().color = new Color(0.2f, 0.45f, 0.9f);
            var text = go.GetComponentInChildren<Text>();
            text.text = label;
            ApplyFont(go);
            go.GetComponent<Button>().onClick.AddListener(onClick);
            return text;
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

Expected: `error CS` なし。`UnityEngine.InputSystem.UI` が解決される（asmdef の `Unity.InputSystem` 参照）。

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

**前提:** `Assets/Characters/XBot.fbx` が存在し、Task 2 の `ReimportCharacters` を再実行して `human=True` になっていること。無ければこのタスクは開始しない。

- [ ] **Step 1: SceneBuilder を書く**

`Assets/Editor/SceneBuilder.cs`:

```csharp
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

        /// <summary>CLI: -executeMethod BabyDance.Editor.SceneBuilder.Build</summary>
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
            {
                Debug.LogError($"{Tag} missing inputs: character={characterPrefab != null} controller={controller != null} dances={dances.Length}");
                return;
            }

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

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";

            var character = (GameObject)PrefabUtility.InstantiatePrefab(characterPrefab);
            character.name = "Dancer";
            var animator = character.GetComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            var driver = character.AddComponent<DanceDriver>();
            driver.dances = dances;

            var playerGo = new GameObject("Player", typeof(AudioSource), typeof(AudioLoader), typeof(DancePlayer), typeof(DanceUi));
            var player = playerGo.GetComponent<DancePlayer>();
            player.audio = playerGo.GetComponent<AudioLoader>();
            player.driver = driver;
            playerGo.GetComponent<DanceUi>().player = player;

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log($"{Tag} SceneBuilder done: {ScenePath} dances={dances.Length}");
        }
    }
}
```

- [ ] **Step 2: BuildScript を書く**

`Assets/Editor/BuildScript.cs`:

```csharp
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BabyDance.Editor
{
    public static class BuildScript
    {
        private const string Tag = "[BabyDance]";

        /// <summary>CLI: -executeMethod BabyDance.Editor.BuildScript.BuildMac</summary>
        public static void BuildMac()
        {
            var options = new BuildPlayerOptions
            {
                scenes = new[] { SceneBuilder.ScenePath },
                locationPathName = "Builds/Mac/BabyDance.app",
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log($"{Tag} BuildMac result={summary.result} size={summary.totalSize} errors={summary.totalErrors} path={summary.outputPath}");
            if (summary.result != BuildResult.Succeeded)
                throw new BuildFailedException($"{Tag} build failed: {summary.result}");
        }
    }
}
```

- [ ] **Step 3: コンパイル確認**

Expected: `error CS` なし。

- [ ] **Step 4: シーンを生成**

```bash
$UNITY -batchmode -quit -nographics -projectPath . -executeMethod BabyDance.Editor.SceneBuilder.Build -logFile Logs/exec.log; echo EXIT=$?; grep -E "error CS|Exception:|\[BabyDance\]" Logs/exec.log; ls Assets/Scenes
```

Expected: `SceneBuilder done: Assets/Scenes/Dance.unity dances=4`。`missing inputs` が出たら XBot.fbx か Task 3 の成果物が無い。

- [ ] **Step 5: 不要なテンプレート資産を削除**

```bash
git rm -rq --cached Assets/Scenes/SampleScene.unity Assets/Scenes/SampleScene.unity.meta Assets/TutorialInfo Assets/TutorialInfo.meta Assets/Readme.asset Assets/Readme.asset.meta 2>/dev/null; rm -rf Assets/Scenes/SampleScene.unity Assets/Scenes/SampleScene.unity.meta Assets/TutorialInfo Assets/TutorialInfo.meta Assets/Readme.asset Assets/Readme.asset.meta
```

その後コンパイル確認を実行し、`error CS` が無いことを確認する（TutorialInfo の Editor スクリプトが消えても他に依存が無いこと）。

- [ ] **Step 6: Mac ビルド**

```bash
$UNITY -batchmode -quit -nographics -projectPath . -executeMethod BabyDance.Editor.BuildScript.BuildMac -logFile Logs/build.log; echo EXIT=$?; grep -E "error CS|BuildFailedException|\[BabyDance\] BuildMac" Logs/build.log; ls Builds/Mac
```

Expected: `BuildMac result=Succeeded errors=0`、`Builds/Mac/BabyDance.app` が存在。

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
- Produces: `tools/metronome_120bpm.wav`（60 秒、120 BPM、4 拍ごとにアクセント）。

- [ ] **Step 1: 生成スクリプトを書く**

`tools/make_metronome.py`:

```python
# /// script
# requires-python = ">=3.11"
# dependencies = ["numpy"]
# ///
"""120 BPM のメトロノーム WAV を生成する。1 拍目は高い音、2〜4 拍目は低い音。"""
import struct
import wave
from pathlib import Path

import numpy as np

BPM = 120
SECONDS = 60
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

Expected: `44100 60.0`。

- [ ] **Step 3: 人手検証を依頼する**

ユーザーに次を依頼し、結果を受け取る:

1. `Builds/Mac/BabyDance.app` を起動する。
2. 「Open music」で `tools/metronome_120bpm.wav` を選ぶ。メッセージに `Loaded metronome_120bpm.wav` と出る。
3. BPM スライダーを 120 にして「Play」。1 秒後にクリックが鳴り始め、X Bot が踊る。
4. 4 ダンスをドロップダウンで切り替え、それぞれで「拍頭とステップの頭が揃うか」を確認する。ずれていれば、そのダンス名と「何拍ずらせば揃うか」を報告する（`beatOffset` に反映）。1 ループが 8 拍でないと感じたら「16 拍っぽい」等を報告する（`beatsPerLoop` に反映）。
5. 60 秒再生し終わったとき、ボタンが自動で「Play」に戻る。
6. 再生中に BPM を 100 に変えると、踊りが位相を飛ばさずゆっくりになる。

- [ ] **Step 4: 報告に基づいて DanceClipInfo を調整**

報告された値を `.asset` に反映する。例（HouseDancing を 16 拍・オフセット 2 拍にする場合）:

```bash
sed -i '' 's/beatsPerLoop: 8/beatsPerLoop: 16/; s/beatOffset: 0/beatOffset: 2/' Assets/Dance/HouseDancing.asset
```

再度 Task 7 Step 6 のビルドを走らせ、Step 3 の 4 を再確認する。

- [ ] **Step 5: コミット**

```bash
git add .gitignore tools/make_metronome.py Assets/Dance
git commit -m "feat: 検証用メトロノーム生成と DanceClipInfo の拍調整

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

## 自己レビュー結果

- **仕様カバレッジ:** AudioLoader → Task 5、BeatClock → Task 1、DanceDriver → Task 4、DanceClipInfo → Task 3、UI 4 要素 → Task 6、エラー処理（キャンセル無視・読込失敗メッセージ・未読込で再生防止）→ Task 5/6、EditMode/PlayMode テスト → Task 1/4/5、手動検証 → Task 8、人手作業（XBot 配置・拍調整）→ Task 2/8。Humanoid 自動化 → Task 2。
- **未読込で再生**: 仕様は「ボタン無効化」だが、実装は押下時にメッセージを出す方式に単純化した（無効化はロード状態の監視が増える）。仕様の意図（誤操作で壊れない）は満たす。
- **型整合:** `DanceClipInfo.stateName` / `beatsPerLoop` / `beatOffset`、`DanceDriver.SetDance(int,double)` / `Tick(double)` / `CurrentIndex`、`AudioLoader.Play()->double` / `IsPlaying` / `HasClip`、`AssetTools.CharacterFbx` / `ControllerPath` / `DanceFolder` を Task 間で同名で使用していることを確認。
