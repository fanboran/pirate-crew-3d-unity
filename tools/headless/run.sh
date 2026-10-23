#!/usr/bin/env bash
# Unity 无头验证分级运行器 —— 档位设计与铁律见 AGENTS.md「调试规范」。
# 统一提供：单飞锁（Library 锁独占）、日志落 $TEMP、失败自动摘 error、
#           open / test 两档按工作区状态缓存 30 分钟（同状态 + 上次真全绿 → 直接回放，不启 Unity）。
#           缓存 key 覆盖未跟踪文件的**内容哈希**；test 档只有 testResults XML 证实「有用例且全过」
#           才写缓存，回放时打印缓存时的 passed/total（防「0 用例命中 / 全 Ignore」的空绿回放成假绿）。
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
                     缓存按「档位+平台+过滤器」分别记，同状态 30 分钟内且上次真全绿 → 直接回放
                     （回放打印缓存时的 passed/total；0 用例命中 / 全 Ignore 的空绿不写缓存）。
                     纯 C# 域测试优先 harness 档（秒级、不占 Library 锁）。
  build <类名.方法名> [透传参数…]
                      无头构建/烘培（如 BuildScript.BuildFromCommandLineArgs）。分钟级，建议后台发起。
  chain <链名|类名.方法名>[;<类名.方法名>…] [透传参数…]
                      单入口编排：一次 Unity 启动按序串跑多件 Editor 装配，省掉每件一次的
                      ≈2.5 分钟冷启动。链名在 HeadlessChain 命名链注册表里定义（full = 字体重建 →
                      Battle 八步管线 → M1 菜单场景；battle = 只重跑 Battle 子链），也可直接给
                      分号分隔的步骤列表。**不带缓存**——装配会改场景/资产，结果不可回放。
  harness <All|Data|Combat|DataEditor|Battle|Runtime> [过滤器…]
                      代理 external/harness/run.sh：不开 Unity，绕开 Library 独占锁。
  bridge <命令>       驱动**常驻编辑器**执行命令（不启动任何 Unity 进程，秒级往返）。
                      编辑器侧桥 = Assets/Editor/CommandBridge.cs（[InitializeOnLoad] 自动装载，
                      只消费本仓库 export/ 文件，不监听网络）。命令：
                        ping / state / refresh / recompile
                        call <类全名.方法名>       反射执行 public static 无参方法
                        chain <链名|步骤列表>      与 chain 档同一张注册表（如 chain full）
                        menu <菜单路径>            执行编辑器菜单项（如 "PirateCrew/Scenes/重跑 Battle 装配链"）
                      编辑器没开本工程 / 桥未装载 → 明确报错退出 4（绝不静默）；命令执行失败 → 退出 5。
  menu <菜单路径>     bridge "menu <路径>" 的糖（整段路径当一个参数传，含中文/空格）。
  unlock              清 Temp/UnityLockfile 僵尸锁兜底：无锁 → 退出 0；有锁且无 Unity.exe 进程
                      → 只删锁文件（Temp 下其余内容不动）并退出 0；有锁且 Unity.exe 在跑
                      → 拒绝并退出 3（与 guard_unity 同口径）。
环境变量: PC3D_UNITY 覆盖 Unity 路径；PC3D_PROJ 覆盖工程目录（自测用）；
          PC3D_NO_CACHE=1 跳过缓存回放（强制真跑一次）；
          PC3D_BRIDGE_PING_TIMEOUT / PC3D_BRIDGE_TIMEOUT 覆盖 bridge 档探活/命令超时秒数
          （默认 10 / 120；chain full 这类长装配可调大）。
EOF
}

case "${1:-}" in
  open|test|build|chain|bridge|menu|harness|unlock) MODE="$1"; shift ;;
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

# 档位缓存：key = HEAD + 工作区状态与内容哈希；30 分钟内同状态且上次真全绿 → 直接回放（不启 Unity）。
# 哈希必须含脏文件**内容**（diff HEAD）：只看"哪些文件脏"时，未提交状态下改码→跑测→再改→再跑
# 的 porcelain 输出不变，会把 30 分钟内的旧"全绿"原样回放（假绿）。
# 未跟踪文件单独纳入：porcelain 对它只输出 `?? 路径`、diff HEAD 完全不含其内容——未 git add 的新
# 测试/运行时代码改内容时上面两项全都不变，同样假绿。故用 ls-files --others 列出后逐个把
# 「路径 + git hash-object 内容哈希」拼进同一条输入流（hash-object 流式过哈希器，大文件不进 bash
# 内存，也不写对象库）；已跟踪文件的改动仍由 porcelain + diff HEAD 覆盖。
# 文件名含工程路径哈希（防 PC3D_PROJ 指向别处时误读别人的缓存）+ 子集标识
# （test 档按 平台/过滤器 分别记，不同子集之间不可互相回放）。
cache_key() {
  echo "$(git -C "$ROOT" rev-parse HEAD 2>/dev/null)-$( {
    git -C "$ROOT" status --porcelain 2>/dev/null
    git -C "$ROOT" diff HEAD 2>/dev/null
    git -C "$ROOT" ls-files --others --exclude-standard 2>/dev/null | while IFS= read -r f; do
      printf '%s\t' "$f"
      git -C "$ROOT" hash-object -- "$f" 2>/dev/null || echo '(读不到)'
    done
  } | md5sum | cut -c1-12)"
}
cache_file_for() {
  echo "$TEMP/pc3d-headless-$1-$(echo -n "$PROJ" | md5sum | cut -c1-8).cache"
}
# 命中则回放并 exit 0；未命中返回非零，调用方继续真跑。
# 缓存文件 4 行：key / 写入时间戳 / 退出码 / passed=…/total=…（open 档无用例数，第 4 行为空；
# 旧版 3 行缓存缺第 4 行同样兼容——只是回放时不打印用例数）。
cache_replay() {
  local cache="$1" key="$2"
  [ "${PC3D_NO_CACHE:-0}" = "1" ] && return 1
  [ -f "$cache" ] || return 1
  local ck cts crc cst
  ck="$(sed -n 1p "$cache")"; cts="$(sed -n 2p "$cache")"; crc="$(sed -n 3p "$cache")"; cst="$(sed -n 4p "$cache")"
  if [ "$ck" = "$key" ] && [ "$crc" = "0" ] && [ $(( $(date +%s) - "${cts:-0}" )) -lt 1800 ]; then
    echo "✅ 缓存命中（同 HEAD + 工作区状态，30 分钟内已跑过且全绿），跳过本次 Unity 启动。"
    [ -n "$cst" ] && echo "   （缓存时结果: $cst）"
    echo "   （要强制重跑：PC3D_NO_CACHE=1 bash tools/headless/run.sh $MODE ...）"
    exit 0
  fi
  return 1
}
cache_store() { { echo "$2"; date +%s; echo "$3"; echo "${4:-}"; } > "$1"; }

# ---- 命令桥（常驻编辑器执行通道；编辑器侧 = pirate-crew/Assets/Editor/CommandBridge.cs）----
# 协议：写一行命令到 <仓库根>/export/unity-command.txt，编辑器每 0.4s 轮询，读到即执行并清空
# 命令文件；结果追加到 export/unity-command-result.txt——">> 命令" 开场、"<<" OK/FAILED 命令 收尾。
# 本档不启动 Unity、不碰 Library 锁（前提恰恰是编辑器开着）。桥文件相对本仓库根——
# 在 worktree 里跑时要求常驻编辑器开的是本 worktree 的工程，两边根目录才会对上。
BRIDGE_DIR="$ROOT/export"
BRIDGE_CMD_FILE="$BRIDGE_DIR/unity-command.txt"
BRIDGE_RES_FILE="$BRIDGE_DIR/unity-command-result.txt"
BRIDGE_PING_TIMEOUT="${PC3D_BRIDGE_PING_TIMEOUT:-10}"
BRIDGE_TIMEOUT="${PC3D_BRIDGE_TIMEOUT:-120}"

bridge_res_lines() { [ -f "$BRIDGE_RES_FILE" ] && wc -l < "$BRIDGE_RES_FILE" 2>/dev/null || echo 0; }

# 等待命令收尾行。$1=命令原文，$2=超时秒，$3=起始行数（写命令**之前**记的基线，防残留误命中）。
# 只看基线之后的新增段；打印新增段作为回传日志。返回 0=<< OK；5=<< FAILED；1=超时。
bridge_wait_finish() {
  local cmd="$1" secs="$2" base="$3" deadline out
  deadline=$(( $(date +%s) + secs ))
  while [ "$(date +%s)" -lt "$deadline" ]; do
    if [ -f "$BRIDGE_RES_FILE" ]; then
      out="$(tail -n +$((base + 1)) "$BRIDGE_RES_FILE" 2>/dev/null)"
      if printf '%s\n' "$out" | grep -qF "<< FAILED $cmd"; then printf '%s\n' "$out"; return 5; fi
      if printf '%s\n' "$out" | grep -qF "<< OK $cmd"; then printf '%s\n' "$out"; return 0; fi
    fi
    sleep 0.2
  done
  [ -f "$BRIDGE_RES_FILE" ] && tail -n +$((base + 1)) "$BRIDGE_RES_FILE" 2>/dev/null
  return 1
}

# 发一条命令给常驻编辑器并等回执。返回 0=OK；4=桥无应答（编辑器不在跑等）；5=命令执行失败。
bridge_run() {
  local cmd="$1" base rc
  mkdir -p "$BRIDGE_DIR"
  # 先 ping 探活（短超时）：把「编辑器不在跑」从「命令执行得久」里拆出来快速失败
  base="$(bridge_res_lines)"
  printf 'ping\n' > "$BRIDGE_CMD_FILE"
  bridge_wait_finish "ping" "$BRIDGE_PING_TIMEOUT" "$base" >/dev/null
  if [ $? -ne 0 ]; then
    echo "❌ 常驻编辑器 ${BRIDGE_PING_TIMEOUT}s 内未应答命令桥——编辑器没开本工程，或桥未装载/版本过旧。" >&2
    echo "   处置：打开 Unity 编辑器（工程 $PROJ）并等它编译完；桥在 Assets/Editor/CommandBridge.cs，" >&2
    echo "   [InitializeOnLoad] 自动装载。若编辑器开着仍无应答：它可能正在编译 / Play / 跑上一条长命令，" >&2
    echo "   或它开的不是本仓库（$ROOT）的工程——桥按仓库根对文件，两边不一致就永远等不到。" >&2
    return 4
  fi
  base="$(bridge_res_lines)"
  printf '%s\n' "$cmd" > "$BRIDGE_CMD_FILE"
  bridge_wait_finish "$cmd" "$BRIDGE_TIMEOUT" "$base"
  rc=$?
  if [ "$rc" -eq 1 ]; then
    echo "❌ 命令 ${BRIDGE_TIMEOUT}s 内未收尾（ping 已通，编辑器在跑）：可能卡在编译/Play/上一条长命令，" >&2
    echo "   或长装配超时——调大 PC3D_BRIDGE_TIMEOUT 重试；结果文件尾部如下：" >&2
    tail -5 "$BRIDGE_RES_FILE" 2>/dev/null >&2
    return 4
  fi
  return "$rc"
}

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
    # 真跑后、写缓存前，解析 testResults XML（NUnit3 根节点 <test-run> 的 total/passed/failed 属性，
    # 文档序上根节点最先出现，取首个匹配即根节点值）：退出码 0 不等于真全绿——0 用例命中
    # （过滤器打错 / 该平台没装用例）或全 Ignore 的跑法退出码同样是 0。failed>0、总数为 0、
    # passed=0（全 Skip/Ignore）或 XML 没产出 → 一律不写缓存，下次照常真跑，宁可重跑不放空绿。
    STATS=""
    if [ -f "$RES" ]; then
      TT="$(sed -n 's/.*[ "]total="\([0-9][0-9]*\)".*/\1/p' "$RES" | head -1)"
      TP="$(sed -n 's/.*[ "]passed="\([0-9][0-9]*\)".*/\1/p' "$RES" | head -1)"
      TF="$(sed -n 's/.*[ "]failed="\([0-9][0-9]*\)".*/\1/p' "$RES" | head -1)"
      TT="${TT:-0}"; TP="${TP:-0}"; TF="${TF:-0}"
      if [ "$RC" -eq 0 ] && [ "$TF" -eq 0 ] && [ "$TT" -gt 0 ] && [ "$TP" -gt 0 ]; then
        STATS="passed=$TP/total=$TT"
        cache_store "$CACHE" "$KEY" "$RC" "$STATS"
      else
        echo "⚠️ 本次结果不进缓存（退出码=$RC total=$TT passed=$TP failed=$TF）：只缓存「退出码 0 且 failed=0 且确有用例跑过且通过」的真全绿。"
      fi
    else
      echo "⚠️ 未产出测试结果 XML，本次不进缓存。"
    fi
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
      echo "chain 档需要链名或步骤列表，例: full / battle（命名链，注册表在 HeadlessChain.cs）" >&2
      echo "  或 <类名.方法名>[;<类名.方法名>…] 显式列表" >&2
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
  bridge)
    if [ $# -lt 1 ]; then
      echo "bridge 档需要命令，例: state / refresh / 'call 类全名.方法名' / 'chain full' / 'menu 菜单路径'" >&2
      exit 2
    fi
    # "$*"：允许 bridge call X.Y 不加引号也能拼成一条命令（含空格路径仍建议整体加引号）
    bridge_run "$*"
    RC=$?
    echo "== run.sh 结果 =="
    echo "档位: bridge  退出码: $RC（0=OK 4=桥无应答/超时 5=命令执行失败）"
    echo "结果文件: $BRIDGE_RES_FILE"
    exit $RC
    ;;
  menu)
    if [ $# -lt 1 ]; then
      echo "menu 档需要菜单路径（一个参数，含空格/中文整体加引号），例: 'PirateCrew/Scenes/重跑 Battle 装配链'" >&2
      exit 2
    fi
    bridge_run "menu $*"
    RC=$?
    echo "== run.sh 结果 =="
    echo "档位: menu  退出码: $RC（0=OK 4=桥无应答/超时 5=菜单路径无效）"
    echo "结果文件: $BRIDGE_RES_FILE"
    exit $RC
    ;;
esac
