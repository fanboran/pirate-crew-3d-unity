namespace PirateCrew.WaterLab
{
    /// <summary>
    /// 实验水体模拟的**外部地形源**（世界 XZ → 世界 Y，米）。云端漫步接入
    /// （2026-11-21）：第 1 关海床是 Blender 高度场（bed_z 公式族），不走 Sandcastle
    /// 沙滩剖面——场景侧组件实现本接口，<see cref="WaterLabSimulation"/> 在 Awake
    /// 把它挂进 <see cref="WaterLabRules.TerrainSampler"/>（序列化引用，不依赖
    /// 组件间 Awake 次序；Height 只吃常量与入参，实现方自身的 Awake 未跑也安全）。
    /// 返回值语义与 <see cref="WaterLabRules.TerrainHeight"/> 一致：**模拟域局部
    /// 世界 Y**（海平面 = <see cref="WaterLabRules.SeaLevel"/> 的同一量纲）。
    /// </summary>
    public interface IWaterLabTerrain
    {
        /// <summary>模拟域局部坐标 (x, z) 处的地形高程（米，域中心在组件 domainCenter）。</summary>
        float GroundHeight(float x, float z);
    }
}
