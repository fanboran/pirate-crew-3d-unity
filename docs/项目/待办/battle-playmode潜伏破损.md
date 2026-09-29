# PlayMode 潜伏破损（r13 后从未跑过 PlayMode，本日首跑暴露，均非相机域）

> r13 后首跑 PlayMode 暴露三处破损（站位断言 ±0.001 过严、嵌套类违反类名=文件名、折叠态带覆盖），均非相机域，待裁决/修复。

## 详情

**PlayMode 潜伏破损（r13 后从未跑过 PlayMode，本日首跑暴露，均非相机域）**：
① `BattleScene_IsFullyWired` 的单位站位断言 ±0.001 过严——单位 5 出生后物理滑移
0.19~0.29u（两次运行不同值，非确定性），疑似出生位与场景碰撞体的相互作用，需裁决
（放宽到 ±0.3 或修出生布局）；② `PixelShowcasePage+PixelAtlasPointFilter` 嵌套类违反
「类名=文件名」（像素 UI 线，修法在测试消息里）；③ `折叠态_CrewManagement` 带覆盖
（并行会话 06:13 打开保存所致，重跑 ManagementSceneSetup + 折叠即愈）。