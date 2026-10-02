# 验证环境·Unity 路径引用债

> **已完成**（2026-10-02）：全仓 47 处不存在的 `F:/Unity/2022.3.62f1c1/Editor/Unity.exe` 路径引用清扫完毕；
> 活文档与 Editor 脚本零残留。归档/图档 README 与版本号语义处按规范保留原貌。

## 详情

**【验证环境·Unity 路径引用债】**（2026-09-24 发现，2026-10-02 清扫完毕）——

- **改了什么**（两类区分是本次的关键口径）：
  - **文件路径**（`F:/Unity/2022.3.62f1c1/…` 或 `F:\Unity\2022.3.62f1c1\…`，目录不存在）→ 全部改为
    `2022.3.62f1`，共 10 个活文件：CI `ci.yml`、[UnityAPI陷阱与参照库公约](../../技术/架构/UnityAPI陷阱与参照库公约.md)、
    [重构迁移报告](../重构迁移报告.md)、[构建与发布手册](../构建与发布手册.md)、
    [运行期查找清退报告](../../审计/专项/运行期查找清退报告.md)、`Assets/Pixelart/README.md`、
    Editor 脚本 `BuildScript` / `TextSampleBuilder` / `SceneLookupAudit`、[海面.md](../../技术/渲染/管线/海面.md)。
  - **版本号语义**（Unity 官方版本字符串本身确实以 `c1` 结尾）→ **保留不改**：
    `ProjectSettings/ProjectVersion.txt`（Unity 真源）、CI `UNITY_VERSION`、Hub 安装指引文案等。
- **有意不改**（按文档规范保留）：交接归档 5 处、`docs/images` 图档 README 2 处、
  `docs/审计/场景接线审计报告.md`（快照类）。
- ① ~~`F:\Unity\2022.3.62f1c1` 安装残缺~~ 该目录已不存在；本机可用编辑器在 `F:\Unity\2022.3.62f1`
  （ProductVersion 实测 = `2022.3.62f1c1`，只是文件夹名少了 `c1`）。
- ② ~~`tools/headless/run.sh` 不透传 `-p:ProjectRoot`~~ **已修**（2026-09-30 前完成）。
- ③ 基线既有失败见 `./infra-harness纪律与基线.md`。
