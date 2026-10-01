# 战斗·审计批次遗留

> 四个「审计批次」小尾巴合并：常量收敛批次（已落，待实机走查）、投掷批次 S2–S4（待实施，**含 [投掷行为契约](../../技术/投掷行为契约.md) 文首挂的「PlayMode 契约用例（待补）」**）、待裁决行为/结构类 5 项、同域备案 4 项小债。

## 详情

### 常量收敛批次（2026-09-24 已落 `refactor/industrial-grade`，待实机走查）

5 worktree 并行、harness All 零回归：失败名单与基线 12 条逐条一致、0 编译错；`AiEvaluation` 拆分等提案见⑦。

- ① **战斗域**（`0532722`）：远裁剪 4500×3 → `CameraFraming.OceanFarClipMin`；静止判据 0.01×2 →
  `WeaponTriggerRules.AtRestSqrMagnitudeEpsilon`（无 0.01 出处；静止 = vx==0 且 |vy|<0.2，
  3D 侧 sqrMagnitude 近似，注释已写明）；boulder 碾压 1.5 →
  `WeaponTriggerRules.BoulderCrushDamageScale`（伤害 = |vx|×1.5；暂存缘由见 const 注释）；
  `AiEvaluation` §6.2 打分系数 9 个补名进 const 区（宝箱 40px 与队友 40px 命名分开防混）；
  `AiController.SeedMixPrime`；`UnitOutlineBinder` 默认蓝 → `CrewVisualCatalog.TeamBlue`。
- ② **点击引爆 bug fix**（`983048b`）：`UpdateClickTrigger` 射线 500f → `cam.farClipPlane`——
  原写死 500 与海面档远裁剪 4500 错位，远 zoom 时点击引爆静默失效；语义层偏差见⑦提案。
- ③ **UI 域**（`1f4bb00`+`30b671b`）：`UiStrings.DeadMark` + 血条 `isDead` 判位替代
  `text == "×"` 文案比较；字体 Resources 路径收敛 UiKit 三 const（路径值取 4da5f5b 字源纠偏裁决）。
- ④ **音频域**（`bec4ede`）：路径五形态收敛 `SfxCatalog.AssetFolderName` 单一来源派生
  （AssetFolder/AssetLoadPrefix/WavFileExtension）。
- ⑤ **死代码**（`036f268`+`10d95e3`）：删 `CrewManagementController.DisplayNames` 与
  `AmbientSwayNode._cameraTransform`（grep 全仓仅定义处）；`OutlineRendererFeature` 类头补处置裁决
  注释、[描边Shader调试](../../技术/渲染/描边Shader调试.md) §6.2/§7.3 对齐「未挂载任何 Renderer」现状。
- ⑥ **WorldMaps**（`57a2b10`+`bdc2183`）：kit 件名收敛 `WorldMapKitParts`；新增
  `WorldMapReefProfileGateTests`——目录每图必须有显式 profile，堵静默落兜底档（.meta 手写 GUID，
  Unity 首开确认导入）。**⚠ 随海图 101–108 全删一并作废**（见 [world-海图101-108删除.md](world-海图101-108删除.md)）。
- ⑦ **提案/待定（本批未动代码）**：点击引爆语义契约（旧口径 = 点击任意处引爆；
  3D 版 = 点中弹体。多弹体归属与 UI 点击排除待裁决，裁决后按行为契约流程改，参考核心行为指令#4）；
  `AiEvaluation.cs` 拆分（边界已画：AiRandom/AiBattlefield 快照/弹道评分/四武器规划器/目标选择+期望
  伤害/Session——两份审计明确缓办，关卡内容稳定后解锁）；CrewProfession 13 色 +
  `BattleController.TintFor` 11 组收敛调色板 JSON（[调色板手册](../../技术/资产管线/调色板与量化手册.md)
  §2「代码只引用槽位 id」口径，随 Aseprite 批次收口窗口）；boulder 系数未来独立 Rules 类；
  `AiEvaluation` 残余内联 0.3f/0.5f 补名。

### 投掷批次 S2–S4（待实施）

方案与依据见 [投掷机制修正方案-提案](../../设计/投掷机制修正方案-提案.md)，全部**提案/待定**：
S1（炮台为唯一）已实施并入；下一批：S2 弹体弹跳/摩擦 Flash 口径接线（P0，
`Ballistics.Integrate*` 接进 `WeaponProjectile`，bounce/friction 入 `ProjectileProfile`）→
S3 仰角轴（`ThrowVelocityForWeight` 加默认参数，三端同源不破）→ S4 预览超射程语义。
S1 的实机收口（炮台瞄准手感）随下一次 batchmode 出图一并验收。

### 待裁决·行为/结构类遗留

代码处均已标「提案/待定」，等创始人定：

- ① `MarkUseWeapon` 扣武器失败仍发布 UseWeapon 动作，回合语义（发 EndGo 还是不发）待裁决
  （`AimThrowController.cs` 两处）；
- ② cannon 蓄力 5–29 区间永不开炮（`CannonRules.FireThreshold = MaxFireStrength`，
  逆向原文即有此空档，文件标提案/待定）；
- ③ 平台簇·水线家族复核**已关闭**（2026-09 死码清理分支 `refactor/audit-dead-code`）：
  `ScenePropKind` 整枚举零引用已删；`IslandShellGeometry` 瘦身至现役 `AddDashedBorder`
  （`IslandShellSettings` 及平台/收形锥族已删）；`ScenePropGeometry` 复核为活
  （SceneArtBaker/编辑器工具/确定性测试在消费）；
- ④ 2P 蓝队阵亡不计星（`CampaignApi.OnCrewDied`）；
- ⑤ 音频三个音量魔法数待并入 `SfxRecipe` 配平（现以具名常量过渡）。

### 同域备案·渲染/水/环境四项小尾巴

- `CrewVisualAnimator.ApplyDeath`（陆地死亡）与溺亡同型无终点；
- `PixelartCameraRig` 运行时自建 Cast 相机播放期仍 `DestroyImmediate`；
- `WaterWaveField2D.CurvatureAt` 与 `UploadTexture` 隐含「CellsX==CellsZ 方格」假设；
- `PixelartContentConverter._reportedNoColor` 只写不读。