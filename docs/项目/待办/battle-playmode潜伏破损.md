# PlayMode 潜伏破损（r13 后从未跑过 PlayMode，本日首跑暴露）

> r13 后首跑 PlayMode 暴露三处破损：① 站位断言 ±0.001 过严（待裁决）；② 嵌套类违反类名=文件名（已修）；
> ③ 折叠态带覆盖（重跑装配即愈）。均非相机域。

## 详情

**PlayMode 潜伏破损（r13 后从未跑过 PlayMode，本日首跑暴露）**：
① `BattleScene_IsFullyWired` 的单位站位断言 ±0.001 过严——单位 5 出生后物理滑移
0.19~0.29u（两次运行不同值，非确定性），疑似出生位与场景碰撞体的相互作用，需裁决
（放宽到 ±0.3 或修出生布局）；② ~~`PixelShowcasePage+PixelAtlasPointFilter` 嵌套类违反
「类名=文件名」~~ **已修**（已提为顶级类、落 `Scripts/UI/Skin/PixelAtlasPointFilter.cs` 同名文件）；
③ `折叠态_CrewManagement` 带覆盖（并行会话 06:13 打开保存所致，重跑 ManagementSceneSetup + 折叠即愈）。