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
