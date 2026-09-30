#!/usr/bin/env bash
# Unity 无头验证分级运行器 —— 档位设计与铁律见 AGENTS.md「调试规范」。
# 统一提供：单飞锁（Library 锁独占）、日志落 $TEMP、失败自动摘 error、
#           open / test 两档按工作区状态缓存 30 分钟（同状态 + 上次全绿 → 直接回放，不启 Unity）。
#
# 【为什么缓存的是这两档】单次运行的成本几乎全在 Unity 冷启动 + 资源导入 + 编译（≈2~3 分钟），
# 用例本身很便宜（实测 EditMode 1300 条 ≈16 秒、PlayMode 8 条 ≈12 秒）。
# 想提速请减少「Unity 启动次数」，不要减少用例数：
#   ① 纯 C# 逻辑改动 → harness 档（秒级，不开 Unity）；
#   ② 同 HEAD + 同工作区重复验证 → 命中缓存直接回放；
#   ③ 只盯一块 → test 档接过滤器（第 2 参数 → -testFilter），跑完同样进缓存。
set -u
: "${TEMP:=${TMP:-/tmp}}"   # WSL/Git Bash 下 $TEMP 可能未导出，兜底（否则 set -u 直接退出）

usage() {
  cat <<'EOF'
用法: bash tools/headless/run.sh <档位> [参数]
  open                整工程可打开验证（分钟级）。仅改了 manifest / Packages / ProjectSettings /
                      程序集定义 / .meta 结构后才需要跑；仅改 C# 或资产内容请用 harness 档。
                      同 HEAD + 工作区状态 30 分钟内且上次成功 → 直接回放缓存，不启 Unity。
  test [EditMode|PlayMode] [过滤器]
                      Unity Test Framework，默认 EditMode。第 2 参数直接透传给 -testFilter
                     （Unity 口径：**测试名子串**，逗号分隔，例："BattleSceneWiringTests"），可只跑一块；
                     缓存按「档位+平台+过滤器」分别记，同状态 30 分钟内且上次全绿 → 直接回放。
                     纯 C# 域测试优先 harness 档（秒级、不占 Library 锁）。
  build <类名.方法名> [透传参数…]
                      无头构建/烘培（如 BuildScript.BuildFromCommandLineArgs）。分钟级，建议后台发起。
  chain <类名.方法名>[;<类名.方法名>…] [透传参数…]
                      单入口编排：一次 Unity 启动按序串跑多件 Editor 装配
                      （如 字体重建;Battle 管线;菜单场景），省掉每件一次的 ≈2.5 分钟冷启动。
                      **不带缓存**——装配方法会改场景/资产，结果不可回放，每次都真跑。
  harness <All|Data|Combat|DataEditor|Battle|Runtime> [过滤器…]
                      代理 external/harness/run.sh：不开 Unity，绕开 Library 独占锁。
  unlock              清 Temp/UnityLockfile 僵尸锁兜底：无锁 → 退出 0；有锁且无 Unity.exe 进程
                      → 只删锁文件（Temp 下其余内容不动）并退出 0；有锁且 Unity.exe 在跑
                      → 拒绝并退出 3（与 guard_unity 同口径）。
环境变量: PC3D_UNITY 覆盖 Unity 路径；PC3D_PROJ 覆盖工程目录（自测用）；
          PC3D_NO_CACHE=1 跳过缓存回放（强制真跑一次）。
EOF
}

case "${1:-}" in
  open|test|build|chain|harness|unlock) MODE="$1"; shift ;;
  *) usage; exit 2 ;;
esac

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
PROJ="${PC3D_PROJ:-$ROOT/pirate-crew}"
UNITY="${PC3D_UNITY:-F:/Unity/2022.3.62f1/Editor/Unity.exe}"
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

# 档位缓存：key = HEAD + 工作区状态与内容哈希；30 分钟内同状态且上次全绿 → 直接回放（不启 Unity）。
# 哈希必须含脏文件**内容**（diff HEAD）：只看"哪些文件脏"时，未提交状态下改码→跑测→再改→再跑
# 的 porcelain 输出不变，会把 30 分钟内的旧"全绿"原样回放（假绿）。
# 文件名含工程路径哈希（防 PC3D_PROJ 指向别处时误读别人的缓存）+ 子集标识
# （test 档按 平台/过滤器 分别记，不同子集之间不可互相回放）。
cache_key() {
  echo "$(git -C "$ROOT" rev-parse HEAD 2>/dev/null)-$( { git -C "$ROOT" status --porcelain 2>/dev/null; git -C "$ROOT" diff HEAD 2>/dev/null; } | md5sum | cut -c1-12)"
}
cache_file_for() {
  echo "$TEMP/pc3d-headless-$1-$(echo -n "$PROJ" | md5sum | cut -c1-8).cache"
}
# 命中则回放并 exit 0；未命中返回非零，调用方继续真跑
cache_replay() {
  local cache="$1" key="$2"
  [ "${PC3D_NO_CACHE:-0}" = "1" ] && return 1
  [ -f "$cache" ] || return 1
  local ck cts crc
  ck="$(sed -n 1p "$cache")"; cts="$(sed -n 2p "$cache")"; crc="$(sed -n 3p "$cache")"
  if [ "$ck" = "$key" ] && [ "$crc" = "0" ] && [ $(( $(date +%s) - "${cts:-0}" )) -lt 1800 ]; then
    echo "✅ 缓存命中（同 HEAD + 工作区状态，30 分钟内已跑过且全绿），跳过本次 Unity 启动。"
    echo "   （要强制重跑：PC3D_NO_CACHE=1 bash tools/headless/run.sh $MODE ...）"
    exit 0
  fi
  return 1
}
cache_store() { { echo "$2"; date +%s; echo "$3"; } > "$1"; }

case "$MODE" in
  open)
    CACHE="$(cache_file_for open)"
    KEY="$(cache_key)"
    cache_replay "$CACHE" "$KEY"
    guard_unity
    "$UNITY" -batchmode -nographics -quit -projectPath "$PROJ" -logFile "$LOG"
    RC=$?
    cache_store "$CACHE" "$KEY" "$RC"
    summarize "$RC"; exit $RC
    ;;
  test)
    PLAT="${1:-EditMode}"
    FILTER="${2:-}"
    if [ "$PLAT" != "EditMode" ] && [ "$PLAT" != "PlayMode" ]; then
      echo "test 档位只接受 EditMode 或 PlayMode，收到: $PLAT" >&2; exit 2
    fi
    # 子集标识：不带过滤器 = 全量（tag 与旧缓存同名，历史缓存继续有效）；带过滤器 = 追加过滤器哈希
    TAG="test-$PLAT"
    [ -n "$FILTER" ] && TAG="$TAG-f$(echo -n "$FILTER" | md5sum | cut -c1-8)"
    CACHE="$(cache_file_for "$TAG")"
    KEY="$(cache_key)"
    cache_replay "$CACHE" "$KEY"
    RES="$TEMP/pc3d-test-results-$PLAT-$TS.xml"
    guard_unity
    if [ -n "$FILTER" ]; then
      echo "（只跑过滤器：$FILTER）"
      "$UNITY" -batchmode -nographics -projectPath "$PROJ" -runTests -testPlatform "$PLAT" \
        -testFilter "$FILTER" -testResults "$RES" -logFile "$LOG"
    else
      "$UNITY" -batchmode -nographics -projectPath "$PROJ" -runTests -testPlatform "$PLAT" \
        -testResults "$RES" -logFile "$LOG"
    fi
    RC=$?
    echo "测试结果 XML: $RES"
    cache_store "$CACHE" "$KEY" "$RC"
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
  chain)
    if [ $# -lt 1 ]; then
      echo "chain 档需要步骤列表 <类名.方法名>[;<类名.方法名>…]，例: FontAssetBuilder.BuildAll;BattleScenePipeline.Build" >&2
      exit 2
    fi
    STEPS="$1"; shift
    # 空列表兜底（空串 / 只有分号）→ 不让 Unity 白启一次
    if [ -z "$(printf '%s' "$STEPS" | tr -d '; ')" ]; then
      echo "chain 档步骤列表为空: \"$STEPS\"" >&2; exit 2
    fi
    # 不带缓存：装配方法会改场景/资产，结果不可回放（口径见 docs/技术/无头验证与启动成本优化.md §5）。
    # 不要 -quit：HeadlessChain.Run 自己调 EditorApplication.Exit 带退出码收口。
    guard_unity
    echo "（chain 串跑: $STEPS）"
    "$UNITY" -batchmode -nographics -projectPath "$PROJ" \
      -executeMethod PirateCrew.EditorTools.HeadlessChain.Run \
      -chainSteps "$STEPS" "$@" -logFile "$LOG"
    RC=$?
    summarize "$RC"; exit $RC
    ;;
  harness)
    if [ $# -lt 1 ]; then
      echo "harness 档需要域参数: All|Data|Combat|DataEditor|Battle|Runtime" >&2; exit 2
    fi
    exec bash "$ROOT/external/harness/run.sh" "$@"
    ;;
  unlock)
    LOCK="$PROJ/Temp/UnityLockfile"
    if [ ! -f "$LOCK" ]; then
      echo "✅ 本来就干净：Temp/UnityLockfile 不存在，无需清理。"
      exit 0
    fi
    if tasklist 2>/dev/null | grep -qi "Unity.exe"; then
      echo "❌ 拒绝清理：检测到 Unity.exe 正在运行，Temp/UnityLockfile 被占用。" >&2
      echo "   先等编辑器/无头实例退出再跑；若是杀不死的僵尸锁，处置见 AGENTS.md batchmode 铁律。" >&2
      exit 3
    fi
    # 只删锁文件本身，Temp 下别的任何东西不碰
    rm -f "$LOCK"
    if [ -f "$LOCK" ]; then
      echo "❌ 删除失败（文件可能仍被占用）: $LOCK" >&2
      exit 1
    fi
    echo "🗑️ 已删除僵尸锁: $LOCK（无 Unity.exe 进程，属陈旧锁）。"
    ;;
esac
