# WaterLab 浮游生物 compute 接线缺口

> 一句话现状：`WaterLabPhytoplankton.compute` 在仓但零接线——场景没存引用、代码无赋值方，运行时浮游生物系统不启动（守卫只报错一次）；实验室场景注释自称"恒开"，与现状矛盾。

## 详情

- **发现途径**：2026-11-22 死件审计（像素化死件清仓同轮）发现其 guid 全仓只出现在自己的 `.meta` 里。
- **接线方式**：`WaterLabSimulation.phytoplanktonShader`（`WaterLabSimulation.cs:84`）是公开序列化字段，注释写"装配器不注入（自包含创建）"——但 grep 全仓确认**没有任何赋值方**（场景未序列化该引用，编辑器侧也不注入），只有读方。
- **运行时行为**：`BootstrapPhytoplankton` 判 null → 报错一次 → 浮游生物系统不启动。战斗场景（云端漫步）按设计 `enablePhytoplankton=false` 不受影响；`WaterLab.unity` 实验场景才是受影响方。
- **矛盾点**：字段注释写"实验室场景恒开"，但 compute 没接——需核实实验室场景 Night 档验收画面里浮游生物是否实际出现过。
- **收口动作二选一**：① 在 `WaterLab.unity` 里把 compute 拖进字段（或由装配器注入）；② 若浮游生物已确认不要，删除该 compute 与 `ReplicaPhytoplanktonSystem` 链，并把"恒开"口径改掉。
- 注：像素化死件清仓未动此文件（它是接线缺口而非纯死件）。
