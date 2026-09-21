#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""仓库静态门禁：不启动 Unity 也能复现的那部分判据。

【为什么有这个脚本】CI 的 Unity job 要 license、要几十分钟、还依赖 game-ci 的镜像；
而"版本号是否单一真源""发行场景集是否漏场景""Unity 资产是否漏 .meta"这三类问题
**纯文本就能判**，且判错了代价最高（前两类会静默出错到玩家手里，第三类会让人 clone 下来打不开工程）。
把它们放进一个不需要 Unity 的 job，让反馈在最便宜的地方发生。

用法（在仓库根执行）：
    python3 .github/scripts/repo_gates.py all       # 全部三项
    python3 .github/scripts/repo_gates.py version
    python3 .github/scripts/repo_gates.py scenes
    python3 .github/scripts/repo_gates.py hygiene

退出码：0 = 全过（可能有警告）；1 = 有失败项。
"""

import os
import re
import subprocess
import sys

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
PROJECT = os.path.join(REPO_ROOT, "pirate-crew")

BUILD_VERSION_CS = "pirate-crew/Assets/Editor/Build/BuildVersion.cs"
BUILD_SCENES_CS = "pirate-crew/Assets/Editor/Build/BuildScenes.cs"
SCENE_NAMES_CS = "pirate-crew/Assets/Scripts/Core/SceneNames.cs"
RELEASE_GATE_CS = "pirate-crew/Assets/Editor/ReleaseGate.cs"
PROJECT_SETTINGS = "pirate-crew/ProjectSettings/ProjectSettings.asset"

# ---------------------------------------------------------------------------
# 已知漂移白名单：**每一项都是一条待办**，不是"允许长期存在"。
# 格式：相对路径 -> (说明, 期望值提取函数名)
# 出现白名单里的不匹配时打 WARNING 并继续；出现白名单以外的不匹配才 FAIL。
# 修好之后请把对应条目删掉，让门禁恢复"零容忍"。
# ---------------------------------------------------------------------------
KNOWN_DRIFT = {
    RELEASE_GATE_CS: (
        "ReleaseGate.BuildVersion 常量是旧值 1.0.0，重跑该菜单会把 bundleVersion 回写成旧值。"
        "该文件属「美术向既有脚本」不在构建轨道文件域内，已登记给主控改（改成引用 BuildVersion.Current）。"
    ),
}

FAILURES = []
WARNINGS = []


def repo_path(relative):
    return os.path.join(REPO_ROOT, relative.replace("/", os.sep))


def read(relative):
    with open(repo_path(relative), "r", encoding="utf-8", errors="replace") as handle:
        return handle.read()


def fail(message):
    FAILURES.append(message)


def warn(message):
    WARNINGS.append(message)


def git(*args):
    out = subprocess.run(
        ["git"] + list(args), cwd=REPO_ROOT, capture_output=True, text=True, check=True
    )
    return out.stdout


def git_paths(*args):
    """`git ls-files` 的路径清单。

    用 -z 而不是按行切：路径含非 ASCII（本仓库大量中文名资产）或空格时，按行切的输出会被
    git 加上引号并转义成 \\344 之类的字节序列，拿它去比对 `path + ".meta"` 会全部对不上
    ——门禁会把「有 meta」误判成「缺 meta」（本脚本第一版就踩了这个，报出 186 个假失败）。
    """
    out = subprocess.run(
        ["git"] + list(args) + ["-z"], cwd=REPO_ROOT, capture_output=True, check=True
    )
    raw = out.stdout.decode("utf-8", errors="replace")
    return [item for item in raw.split("\0") if item]


# ---------------------------------------------------------------------------
# 门禁 1：版本号单一真源
# ---------------------------------------------------------------------------
def check_version():
    src = read(BUILD_VERSION_CS)
    match = re.search(r'public\s+const\s+string\s+Current\s*=\s*"([^"]+)"', src)
    if not match:
        fail("无法从 %s 解析出 BuildVersion.Current —— 门禁本身失效了，先修门禁。" % BUILD_VERSION_CS)
        return

    version = match.group(1)
    print("  版本号真源 BuildVersion.Current = %s" % version)

    # (a) ProjectSettings.bundleVersion 必须等于真源
    settings = read(PROJECT_SETTINGS)
    bundle = re.search(r"^\s*bundleVersion:\s*(\S+)\s*$", settings, re.MULTILINE)
    if not bundle:
        fail("无法从 %s 解析 bundleVersion。" % PROJECT_SETTINGS)
    elif bundle.group(1) != version:
        fail(
            "bundleVersion 漂移：ProjectSettings.asset 是 %s，真源是 %s。"
            "手改 YAML 对齐（bundleVersion 是唯一允许手改的 PlayerSettings 字段），"
            "或跑 PirateCrew/Build/写入 PlayerSettings。"
            % (bundle.group(1), version)
        )
    else:
        print("  OK  ProjectSettings.bundleVersion = %s" % bundle.group(1))

    # (b) 其他硬编码版本常量：任何一个不等于真源都要报
    #     （已知漂移走白名单，避免门禁在已登记的问题上永远红着）
    candidates = [
        ("pirate-crew/Assets/Editor/ReleaseGate.cs", r'BundleVersion\s*=\s*"([^"]+)"'),
    ]
    for relative, pattern in candidates:
        found = re.search(pattern, read(relative))
        if not found:
            continue
        value = found.group(1)
        if value == version:
            print("  OK  %s 的版本常量 = %s" % (relative, value))
        elif relative in KNOWN_DRIFT:
            warn(
                "%s 的版本常量是 %s（真源 %s）—— 已知漂移：%s"
                % (relative, value, version, KNOWN_DRIFT[relative])
            )
        else:
            fail("发现第二处版本号真源：%s 里的 \"%s\"（真源是 %s）。版本号只能有一处。" % (relative, value, version))

    # (c) README 与版本号是否脱节。**只警告**：README 的版本号指向「已发行的 release」，
    #     与「下一个要构建的版本」本来就可以不同（两者是不同概念）。
    readme = read("README.md")
    if ("v" + version) not in readme:
        warn(
            "README.md 里找不到 \"v%s\"。若 README 有意只写『已发行的 release 版本』，忽略本条；"
            "否则请同步 README 的版本徽章与下载链。" % version
        )
    else:
        print("  OK  README.md 提到了 v%s" % version)

    # (d) 玩家可见的版本文案（主菜单页脚之类）：**只警告**。它不是构建版本号，
    #     但不该与版本号差得太离谱——差着就是「界面上写着 1.0、包里其实是 0.1.0」。
    ui_strings = read("pirate-crew/Assets/Scripts/UI/UiStrings.cs")
    shown = re.search(r'MainVersion\s*=\s*"([^"]+)"', ui_strings)
    if shown and version.rsplit(".", 1)[0] not in shown.group(1):
        warn(
            "UI/UiStrings.cs 的 MainVersion = \"%s\" 里没有 \"%s\"，与真源版本号 %s 不一致。"
            "该字段是玩家可见文案、属 Scripts/ 文件域，已知且待处置（见 docs/项目/构建与发布手册.md §3.3）。"
            % (shown.group(1), version.rsplit(".", 1)[0], version)
        )
    elif shown:
        print("  OK  UI/UiStrings.MainVersion 与版本号同系（%s）" % shown.group(1))


# ---------------------------------------------------------------------------
# 门禁 2：发行场景集与运行时场景常量的交叉校验
# ---------------------------------------------------------------------------
def _scene_names_consts():
    """SceneNames.cs 里的 public const string 值集合。"""
    src = read(SCENE_NAMES_CS)
    return re.findall(r'public\s+const\s+string\s+\w+\s*=\s*"([^"]+)"', src)


def _release_scene_array():
    """BuildScenes.cs 的 _releaseSceneNames 数组内容。

    数组元素的三种合法写法都要认：
      · `SceneNames.Battle`  —— 常态写法，经 SceneNames 常量表解析
      · `"Battle"`           —— 字面量
      · `ToonPilot`          —— 本文件自己的 const（仅开发场景就是这种写法）
    认不出来的 token 一律返回 None（门禁自报失效），**不猜**——猜错的代价是把「漏了场景」
    静默判成通过，而这是本门禁唯一值钱的断言。
    """
    src = read(BUILD_SCENES_CS)
    match = re.search(
        r"static\s+readonly\s+string\[\]\s+_releaseSceneNames\s*=\s*\{(.*?)\};", src, re.DOTALL
    )
    if not match:
        return None

    # 本文件内的 const string 表（ToonPilot 是这么声明的）
    local_consts = dict(
        re.findall(r'public\s+const\s+string\s+(\w+)\s*=\s*"([^"]+)"', src)
    )

    body = re.sub(r"//[^\n]*", "", match.group(1))
    items = []
    for raw in body.split(","):
        token = raw.strip()
        if not token:
            continue
        if token.startswith('"') and token.endswith('"'):
            items.append(token.strip('"'))
            continue
        ref = re.match(r"SceneNames\.(\w+)$", token)
        if ref:
            items.append(ref.group(1))  # 值是常量名，下面统一映射
            continue
        if re.match(r"\w+$", token):
            items.append(local_consts.get(token, token))
            continue
        return None
    return items


def check_scenes():
    const_names = re.findall(
        r'public\s+const\s+string\s+(\w+)\s*=\s*"([^"]+)"', read(SCENE_NAMES_CS)
    )
    by_const = {name: value for name, value in const_names}
    declared = set(by_const.values())

    items = _release_scene_array()
    if items is None or not items:
        fail("无法从 %s 解析发行场景集 —— 门禁本身失效了，先修门禁。" % BUILD_SCENES_CS)
        return

    release = [by_const.get(item, item) for item in items]
    print("  发行场景集（%d 个）: %s" % (len(release), ", ".join(release)))

    # (a) [0] 必须是 Bootstrapper：播放器启动场景
    if release[0] != "Bootstrapper":
        fail("发行场景集第 0 项是 %s，必须是 Bootstrapper（播放器启动场景）。" % release[0])

    # (b) 运行时声明过的场景必须都在发行集里，否则打包后 LoadScene 失败
    missing = sorted(declared - set(release))
    if missing:
        fail(
            "SceneNames 声明了但不在发行场景集里: %s —— 打包后 SceneLoader.ChangeScene 会失败。"
            % ", ".join(missing)
        )
    else:
        print("  OK  SceneNames 的 %d 个常量全部在发行集内" % len(declared))

    # (c) 发行集不该有 SceneNames 之外的名字（拼错或多打了一个场景）
    extra = sorted(set(release) - declared)
    if extra:
        fail("发行场景集里有 SceneNames 未声明的场景名: %s。" % ", ".join(extra))

    # (d) 仅开发场景不许混进发行集
    if "ToonPilot" in release:
        fail("ToonPilot 是等距像素卡通试点场景（开发/测试专用），不许进发行包。")

    # (e) 与 Build Settings 对账：发行集必须是 Build Settings 的子集
    #     （Build Settings 是超集才正常：它还要含 ToonPilot 供播放器出图）
    build_settings = read("pirate-crew/ProjectSettings/EditorBuildSettings.asset")
    registered = set(re.findall(r"path:\s*Assets/Scenes/(\w+)\.unity", build_settings))
    not_registered = sorted(set(release) - registered)
    if not_registered:
        warn(
            "发行集里的场景不在 Build Settings: %s —— 出包不受影响（构建显式传 scenes），"
            "但编辑器里按 Play / 播放器出图会缺这些场景。" % ", ".join(not_registered)
        )
    else:
        print("  OK  Build Settings 含全部发行场景（另有 %d 个开发场景）" % (len(registered - set(release))))


# ---------------------------------------------------------------------------
# 门禁 3：仓库卫生
# ---------------------------------------------------------------------------
REQUIRED_ATTRIBUTES = ["*.meta", "*.unity", "*.prefab", "*.asset", "*.mat", "*.controller"]


def check_hygiene():
    # (a) .gitattributes 必须为 Unity 序列化资产钉住 LF
    attributes = read(".gitattributes")
    for pattern in REQUIRED_ATTRIBUTES:
        if not re.search(r"^\s*%s\s+text\s+eol=lf\s*$" % re.escape(pattern), attributes, re.MULTILINE):
            fail(".gitattributes 缺少 `%s text eol=lf`（Unity 写的 YAML 必须固定 LF，否则整文件行尾噪音）。" % pattern)
    if not FAILURES:
        print("  OK  .gitattributes 为 %d 类 Unity 资产钉了 LF" % len(REQUIRED_ATTRIBUTES))

    # (b) 索引里不许有 CRLF：eol=lf 属性的意义就在这儿
    eol = git("ls-files", "--eol")
    crlf = [line.split("\t")[-1] for line in eol.splitlines() if line.startswith("i/crlf")]
    if crlf:
        fail(
            "索引里有 %d 个 CRLF 文件（首例 %s）。跑 `git add --renormalize .` 修。"
            % (len(crlf), crlf[0])
        )
    else:
        print("  OK  索引里 0 个 CRLF 文件")

    # (c) Unity 资产必须有 .meta；.meta 必须有对应目标
    tracked = git_paths("ls-files", "pirate-crew/Assets")
    tracked_set = set(tracked)

    missing_meta = []
    for path in tracked:
        if path.endswith(".meta"):
            continue
        if os.path.basename(path).startswith("."):
            continue
        if (path + ".meta") not in tracked_set:
            missing_meta.append(path)
    if missing_meta:
        fail(
            "有 %d 个 Unity 资产缺 .meta（首例 %s）。缺 meta 会让协作者 clone 后引用全断。"
            % (len(missing_meta), missing_meta[0])
        )
    else:
        print("  OK  Assets/ 下 %d 个资产都有配对 .meta" % len([p for p in tracked if not p.endswith(".meta")]))

    orphan_meta = []
    for path in tracked:
        if not path.endswith(".meta"):
            continue
        target = path[: -len(".meta")]
        if target in tracked_set or os.path.isdir(repo_path(target)):
            continue
        orphan_meta.append(path)
    if orphan_meta:
        warn(
            "有 %d 个 .meta 找不到对应资产/目录（首例 %s）—— 多为重命名后漏删的孤儿 meta。"
            % (len(orphan_meta), orphan_meta[0])
        )
    else:
        print("  OK  无孤儿 .meta")

    # (d) IDE 生成文件不许入库（pirate-crew/.gitignore 已忽略，这里是兜底）
    junk = [p for p in git_paths("ls-files") if p.endswith((".csproj", ".sln", ".user"))]
    if junk:
        fail("IDE 生成文件被入库了（首例 %s）。它们应由 pirate-crew/.gitignore 排除。" % junk[0])
    else:
        print("  OK  没有入库的 *.csproj / *.sln / *.user")


GATES = {"version": check_version, "scenes": check_scenes, "hygiene": check_hygiene}


def main(argv):
    if len(argv) < 2 or (argv[1] != "all" and argv[1] not in GATES):
        print(__doc__)
        return 1

    names = list(GATES) if argv[1] == "all" else [argv[1]]
    for name in names:
        print("[repo-gates] %s" % name)
        GATES[name]()

    print("")
    for message in WARNINGS:
        print("  警告: %s" % message)
    for message in FAILURES:
        print("  失败: %s" % message)

    if FAILURES:
        print("\n[repo-gates] 未通过：%d 项失败 / %d 项警告" % (len(FAILURES), len(WARNINGS)))
        return 1

    print("[repo-gates] 全部通过（%d 项警告）" % len(WARNINGS))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
