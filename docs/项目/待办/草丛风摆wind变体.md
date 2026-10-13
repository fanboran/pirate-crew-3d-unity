# 草丛风摆的像素路径 `_WIND` 变体（t3ssel8r 口径收尾）

> 草丛几何已改 t3ssel8r 口径（`20d52768`），但风摆仍缺——需给 `PixelartObject.shader` 加 keyword 门控的 `_WIND` 顶点位移变体，动冻结渲染契约须经评审后单独做。

## 详情

**草丛风摆的像素路径 `_WIND` 变体（t3ssel8r 口径收尾）**：草丛几何已改 t3ssel8r 口径
（2026-09-30，提交 `20d52768`：世界噪声三档斑块 + 草叶法线强制朝上），但**风摆**还缺——
旧 `PirateAmbientWind` shader 已随 PBR 根除删除（b55bf7c6），`AmbientWindBinder` 现换上的
像素材质无风属性、摆动空转（AmbientLibrary.cs:209 自注）。要复刻原版「双八度滚动噪声乘积
→ 阈值 → 绕根部旋转 ±60°、阵风散发」（GrassBlade.shader:251-275，external/ref/
unity-isometric-pixel-pipeline），需给 `PixelartObject.shader` 加 keyword 门控的 `_WIND`
顶点位移变体（先例：`_NORMALMAP`），同步材质工厂 / 内容转换器透传 / 接口契约 §2.3 登记——
动冻结渲染契约，须经评审后单独做。