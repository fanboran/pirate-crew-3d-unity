# 验证环境·Unity 路径引用债

> 全仓 47 处、25 个文件仍写不存在的 `F:/Unity/2022.3.62f1c1/Editor/Unity.exe`；本机真正的可用编辑器在 `F:/Unity/2022.3.62f1`，`tools/headless/run.sh` 透传 `-p:ProjectRoot` 已修，残留路径引用待清理。

## 详情

**【验证环境·Unity 路径引用债】**（2026-09-24 发现，2026-09-30 重查更新）——
① ~~`F:\Unity\2022.3.62f1c1` 安装残缺~~ **已失效**：该目录已不存在；本机可用编辑器在
`F:\Unity\2022.3.62f1`（ProductVersion 实测 = `2022.3.62f1c1`，只是文件夹名少了 `c1`，
`Editor\Data\Managed\` 齐全，33f1 亦完整）。**真正的残留债** = 全仓 **47 处、25 个文件**
仍写不存在的 `F:/Unity/2022.3.62f1c1/Editor/Unity.exe`——活的有 CI `ci.yml`(3)、
[构建与发布手册](../构建与发布手册.md)(5)、[开发者指南](../开发者指南.md)(5)、
README(2)、[UnityAPI陷阱与参照库公约](../../技术/架构/UnityAPI陷阱与参照库公约.md)(9)、4 个 Editor 脚本
（`BuildScript`/`PlayerPreset`/`TextSampleBuilder`/`SceneLookupAudit`）、`Pixelart/README.md`；
已改对的只有 AGENTS / `run.sh` / Scenes README / 架构总览四处。另：`~36K 僵尸 Unity.exe`
现已清零（当前 Unity 进程数 = 0）。
② ~~`tools/headless/run.sh` 不透传 `-p:ProjectRoot`~~ **已修**：`external/harness/run.sh`
第 42–53 行已显式解析并直通 `-p:ProjectRoot`，`tools/headless/run.sh` 的 harness 档原样转发 `"$@"`。
③ 基线既有失败（非本批引入）见 `./infra-harness纪律与基线.md`。