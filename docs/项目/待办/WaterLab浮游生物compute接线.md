# WaterLab 浮游生物 compute 接线缺口

> 一句话现状：**已修复，待实机验收**——`WaterLab.unity` 的 `phytoplanktonShader` 字段已补上 compute 序列化引用（此前 `{fileID: 0}`；同屏已接线的 `replicaSandSnapshot` 对照坐实是手工接线漏项）；运行时取用收口到 `PixelartShaders.Find`。待实机进 Lab 场景确认夜光浮游生物出现、Console 无"未接线"报错后归档。

## 详情

- **发现途径**：2026-11-22 死件审计（像素化死件清仓同轮）发现其 guid 全仓只出现在自己的 `.meta` 里。
- **根因**：`WaterLabSimulation.phytoplanktonShader` 是公开序列化字段、靠场景手工接线；同屏的 `replicaSandSnapshot` 等快照字段都接了，唯独它漏了——"字段等人接、没人接"。
- **修复动作**：
  1. `WaterLab.unity`：`phytoplanktonShader: {fileID: 0}` → `{fileID: 7200000, guid: d2baafabf4fc46a3aca59e330796b4b6, type: 3}`（格式照抄 CloudWalk.prefab 已验证的 compute 引用）；
  2. `WaterLabSimulation.cs`：`BootstrapPhytoplankton` 的裸 `Shader.Find("WaterLab/ReplicaPhytoplankton")` 收口到 `PixelartShaders.Find`（资产缺失时响亮报错，不再静默返回 null）；字段注释改为两场接线口径（实验室 = 场景资产直接接线 / 战斗 = 烘焙器显式 `enablePhytoplankton=false` 不注入不报错）。
- **验证状态**：harness 编译通过；**待创始人实机进 Lab 场景验收**。战斗场景（云端漫步）按设计不带浮游生物，不受本次接线影响。
- **注**：`WaterLabSimulation.cs` 带创始人未提交 WIP，C# 侧修改随其一起提交；场景接线与本档单独成提交。
