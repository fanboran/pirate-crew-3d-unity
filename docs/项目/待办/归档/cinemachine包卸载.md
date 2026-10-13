# Cinemachine 包卸载

> **已完成**（2026-10-02）：manifest 摘除 `com.unity.cinemachine 2.9.7`，batchmode open 档验证通过——
> 包重解析后 31→30 个、0 编译错误、0 shader 错误；PlayMode 守卫 `AssertBattleCameraDriverWired`
> 继续钉住「主相机无 Brain、场景无虚机/中转目标」，卸包后无隐性回归。

## 详情

- 背景：相机三件套（`CameraFraming` / `CameraInputReader` / `BattleCameraDriver`）落地后，
  代码与 asmdef 均已零引用；PlayMode 守卫 `AssertBattleCameraDriverWired` 钉住
  「主相机无 Brain、场景无虚机/中转目标」，卸包后若有隐性回归会被它抓住。
- 执行记录：
  1. `pirate-crew/Packages/manifest.json` 删 `"com.unity.cinemachine": "2.9.7"` 行；
  2. batchmode open 档（`-batchmode -nographics -quit -projectPath …`）重析 Library，退出码 0，
     日志确认「Lock file was modified → Registered 30 packages」且 0 编译/着色错误
     （`Library/PackageCache` 目录残留属正常缓存，manifest 不再引用即不参与编译）；
  3. 卸包前全仓 grep 复核：仅剩注释与字符串探测（`GetComponent("CinemachineBrain")` 按名查、
     卸包后返回 null，守卫语义不变），零程序集级引用。
- 经验：batchmode `-quit` 的 launcher 退出码不代表 Unity 子进程收尾完成——重析 Library 的
  收尾期间锁仍被持有，紧接的第二次启动会撞 `HandleProjectAlreadyOpenInAnotherInstance`；
  正确姿势是 `Wait-Process` 等进程真正退出（或检查 `Temp/UnityLockfile`）再启动下一个 Unity。
