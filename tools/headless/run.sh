#!/usr/bin/env bash
# Unity 无头验证分级运行器 —— 档位设计与铁律见 AGENTS.md「调试规范」。
# 统一提供：单飞锁（Library 锁独占）、日志落 $TEMP、失败自动摘 error、open 档按工作区状态缓存 30 分钟。
set -u

usage() {
  cat <<'EOF'
用法: bash tools/headless/run.sh <档位> [参数]
  open                整工程可打开验证（分钟级）。仅改了 manifest / Packages / ProjectSettings /
                      程序集定义 / .meta 结构后才需要跑；仅改 C# 或资产内容请用 harness 档。
                      同 HEAD + 工作区状态 30 分钟内直接回放缓存结果。
  test [EditMode|PlayMode]
                      Unity Test Framework，默认 EditMode；纯 C# 域测试优先 harness 档（秒级）。
  build <类名.方法名> [透传参数…]
                      无头构建/烘培（如 BuildScript.BuildFromCommandLineArgs）。分钟级，建议后台发起。
  harness <All|Data|Combat|DataEditor|Battle|Runtime> [过滤器…]
                      代理 external/harness/run.sh：不开 Unity，绕开 Library 独占锁。
环境变量: PC3D_UNITY 覆盖 Unity 路径；PC3D_PROJ 覆盖工程目录（自测用）。
EOF
}

case "${1:-}" in
  open|test|build|harness) MODE="$1"; shift ;;
  *) usage; exit 2 ;;
esac

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
PROJ="${PC3D_PROJ:-$ROOT/pirate-crew}"
UNITY="${PC3D_UNITY:-F:/Unity/2022.3.62f1c1/Editor/Unity.exe}"
TS="$(date +%Y%m%d-%H%M%S)"
LOG="$TEMP/pc3d-headless-$MODE-$TS.log"
mkdir -p "$TEMP"

# 单飞锁：Library 锁是独占资源，一次只许一个 Unity 进程（AGENTS.md batchmode 铁律）
guard_unity() {
  local lock="$PROJ/Temp/UnityLockfile"
  if [ -f "$lock" ]; then
    if tasklist 2>/dev/null | grep -qi "Unity.exe"; then
      echo "❌ 拒绝执行：检测到 Unity.exe 正在运行，Temp/UnityLockfile 被占用。" >&2
      echo "   先等编辑器/无头实例退出再跑；若是杀不死的僵尸锁，处置见 AGENTS.md batchmode 铁律。" >&2
      exit 3
    fi
    echo "⚠️ Temp/UnityLockfile 存在但无 Unity 进程（疑似陈旧锁），继续执行。"
  fi
}

# 统一收口：打退出码与日志路径；失败时自动摘 error，省一轮 grep
summarize() {
  local rc="$1"
  echo "== run.sh 结果 =="
  echo "档位: $MODE  退出码: $rc"
  echo "日志: $LOG"
  if [ "$rc" -ne 0 ]; then
    echo "-- error 摘要（前 20 条）--"
    grep -nE "error CS|Scripts have compiler errors|Aborting batchmode|Exception|Failed|aborted" "$LOG" 2>/dev/null | head -20
  fi
}

# open 档缓存：key = HEAD + 工作区状态哈希；30 分钟内同状态且上次成功 → 直接回放
# 文件名含工程路径哈希，防止 PC3D_PROJ 指向别处时误读别人的缓存
CACHE="$TEMP/pc3d-headless-open-$(echo -n "$PROJ" | md5sum | cut -c1-8).cache"
cache_key() {
  echo "$(git -C "$ROOT" rev-parse HEAD 2>/dev/null)-$(git -C "$ROOT" status --porcelain 2>/dev/null | md5sum | cut -c1-12)"
}

case "$MODE" in
  open)
    KEY="$(cache_key)"
    if [ -f "$CACHE" ]; then
      CK="$(sed -n 1p "$CACHE")"; CTS="$(sed -n 2p "$CACHE")"; CRC="$(sed -n 3p "$CACHE")"
      if [ "$CK" = "$KEY" ] && [ "$CRC" = "0" ] && [ $(( $(date +%s) - CTS )) -lt 1800 ]; then
        echo "✅ 缓存命中（同 HEAD + 工作区状态，30 分钟内已验证工程可打开），跳过本次 Unity 启动。"
        exit 0
      fi
    fi
    guard_unity
    "$UNITY" -batchmode -nographics -quit -projectPath "$PROJ" -logFile "$LOG"
    RC=$?
    { echo "$KEY"; date +%s; echo "$RC"; } > "$CACHE"
    summarize "$RC"; exit $RC
    ;;
  test)
    PLAT="${1:-EditMode}"
    if [ "$PLAT" != "EditMode" ] && [ "$PLAT" != "PlayMode" ]; then
      echo "test 档位只接受 EditMode 或 PlayMode，收到: $PLAT" >&2; exit 2
    fi
    RES="$TEMP/pc3d-test-results-$PLAT-$TS.xml"
    guard_unity
    "$UNITY" -batchmode -nographics -projectPath "$PROJ" -runTests -testPlatform "$PLAT" -testResults "$RES" -logFile "$LOG"
    RC=$?
    echo "测试结果 XML: $RES"
    summarize "$RC"; exit $RC
    ;;
  build)
    if [ $# -lt 1 ]; then
      echo "build 档需要 <类名.方法名>，例如 BuildScript.BuildFromCommandLineArgs" >&2; exit 2
    fi
    METHOD="$1"; shift
    guard_unity
    "$UNITY" -batchmode -nographics -quit -projectPath "$PROJ" -executeMethod "$METHOD" "$@" -logFile "$LOG"
    RC=$?
    summarize "$RC"; exit $RC
    ;;
  harness)
    if [ $# -lt 1 ]; then
      echo "harness 档需要域参数: All|Data|Combat|DataEditor|Battle|Runtime" >&2; exit 2
    fi
    exec bash "$ROOT/external/harness/run.sh" "$@"
    ;;
esac
