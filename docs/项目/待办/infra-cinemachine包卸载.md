# Cinemachine 包卸载

> 相机去 Cinemachine 化后代码已零引用、asmdef 已摘除，但 `Packages/manifest.json` 仍装
> `com.unity.cinemachine 2.9.7`——卸包要重析 Library（分钟级），挂起未做。

## 详情

- 背景：相机三件套（`CameraFraming` / `CameraInputReader` / `BattleCameraDriver`）落地后，
  代码与 asmdef 均已零引用；PlayMode 守卫 `AssertBattleCameraDriverWired` 钉住
  「主相机无 Brain、场景无虚机/中转目标」，卸包后若有隐性回归会被它抓住。
- 步骤：
  1. `pirate-crew/Packages/manifest.json` 删 `"com.unity.cinemachine": "2.9.7"` 行；
  2. 开编辑器重析 Library（分钟级），确认 Console 0 编译错误；
  3. `tools/headless/run.sh open` 验证工程可打开（改 manifest 必跑 open 档，见 AGENTS 调试规范）；
  4. 全仓 grep `Cinemachine` 确认无残留引用（注释提及不算）。
- 注意：卸包属改 manifest，按铁律跑 open 档；同工作tree内一次只跑一个 Unity 进程。
