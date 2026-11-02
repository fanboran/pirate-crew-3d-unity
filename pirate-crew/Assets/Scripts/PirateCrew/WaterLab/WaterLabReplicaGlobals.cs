using UnityEngine;

namespace PirateCrew.WaterLab
{
    /// <summary>
    /// Lit/Water 复刻件 uniform 下发（I1）：A 流槽位真值的**单源**。
    ///
    /// 【为什么要独立一个静态类】真值不许写死在 shader 里（shader 只声明 uniform），
    /// 也不能塞进材质属性（.mat 不跟 shader 默认值走 + Linear 工程 Color 属性会被
    /// 再 gamma 一次——物理量一律 Vector 走 <see cref="Shader.SetGlobalVector"/>，
    /// 与 <see cref="WaterLabSimulation.PublishSunDir"/> 的 _WaterLabSunDir 现有链
    /// 同一机制）。太阳**方向**仍由模拟侧现有链下发，本类只补 A 表颜色/折射真值。
    ///
    /// 【发布时序】<see cref="RuntimeInitializeOnLoadMethod"/>（AfterSceneLoad）首发 +
    /// <see cref="Application.onBeforeRender"/> 每帧幂等重发（防域重载丢失/其它写入者
    /// 覆盖；四个全局都是常量，重发零成本）。
    ///
    /// 【槽位对应（A-sigma.md §2）】
    ///   _WaterLabWaterColor      = cb0[131]（PS L114 透射染色）
    ///   _WaterLabScatterColor    = cb0[121]（PS L129 散射补色）
    ///   _WaterLabSunColor        = cb0[125]（PS L155 高光染色）
    ///   _WaterLabRefractionFactor = cb1[0].x（PS L82 折射偏移乘子）
    /// 全部经 WaterLabOcean.shader 消费；抓帧终证（A-sigma §6 路线①）若实测值与本表
    /// 不符，**只改本文件**。
    /// </summary>
    public static class WaterLabReplicaGlobals
    {
        // ------------------------------------------------------------------
        // A 表真值（出处 = external/sandcastle-ref/rt/reports/A-sigma.md §2.1/§2.2，
        // 值源 = 材质序列化 materials-dump.txt + level0 直读；改动须先对账）。
        // ------------------------------------------------------------------

        /// <summary>
        /// cb0[131] waterColor：透射染色。值 (0.7835, 0.9486, 1.0) = t=1/3 DayCycler
        /// 运行时值（R 抓帧实测，L2-run §2：水 PS 全部 draw 逐位同值，跨 Lit_* 同刻同值）。
        /// Water 材质序列化值 (0.9603, 0.9858, 0.9995) 是惰性残留（同 σ 案：序列化 ≠ 运行时）。
        /// </summary>
        static readonly Vector4 WaterColor = new Vector4(0.7835f, 0.9486f, 1.0f, 1f);

        /// <summary>
        /// cb0[121] waterScatteringColor：散射补色。值 (0.8015, 0.5061, 0.5316) =
        /// t=1/3 DayCycler 运行时值（R 抓帧实测，出处同上）；材质序列化 (0.5684, 0.3569, 0) 勿用。
        /// </summary>
        static readonly Vector4 ScatterColor = new Vector4(0.8015f, 0.5061f, 0.5316f, 1f);

        /// <summary>
        /// cb0[125] sunColor：镜面染色。值 (1, 0.940429, 0.872353) = t=1/3 Morning
        /// DayCycler 运行时值（R 抓帧实测 cb0[125]，L2-run §2 终证；灯序列化值
        /// (1, 0.9709, 0.9403) 是待机态，勿用）。
        /// </summary>
        static readonly Vector4 SunColor = new Vector4(1.0f, 0.940429f, 0.872353f, 1f);

        /// <summary>
        /// cb1[0].x（PS L82）折射偏移乘子 = 0.0121（materials-dump.txt，交接档 §3）。
        /// 【双解读 TODO】A 表 §2.2 依 VS 用法断言 CB1=ObjectToWorld、PS 取 m00
        /// （物体世界 X 缩放）；PS 的 CB1 只声明 1 向量（dcl CB1[1]），与材质参数槽
        /// 更吻合。两读法值域一致（同槽同值 0.0121）；本类按材质语义下发 0.0121，
        /// 不读我方 ObjectToWorld.m00（我方物体 scale=1，会改行为）。抓帧终证时读
        /// PS cb1[0].x 实测闭。
        /// </summary>
        const float RefractionFactor = 0.0121f;

        // shader 全局名（WaterLabOcean.shader 声明同名 uniform）
        static readonly int IdWaterColor = Shader.PropertyToID("_WaterLabWaterColor");
        static readonly int IdScatterColor = Shader.PropertyToID("_WaterLabScatterColor");
        static readonly int IdSunColor = Shader.PropertyToID("_WaterLabSunColor");
        static readonly int IdRefractionFactor = Shader.PropertyToID("_WaterLabRefractionFactor");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            Publish();
            Application.onBeforeRender += Publish;
        }

        /// <summary>幂等下发（每帧重发安全）。</summary>
        static void Publish()
        {
            Shader.SetGlobalVector(IdWaterColor, WaterColor);
        // cb0[132] σ 运行时真值（R 流 key_slots.json，2026-10-27）：
        // (0.9930291, 0.1753813, 0.0557871) tint 域；exp2 语义下有效 6σ/m。
        Shader.SetGlobalVector(Shader.PropertyToID("_WaterLabSigma"),
            new Vector4(0.9930291f, 0.1753813f, 0.0557871f, 0f));
            Shader.SetGlobalVector(IdScatterColor, ScatterColor);
            Shader.SetGlobalVector(IdSunColor, SunColor);
            Shader.SetGlobalFloat(IdRefractionFactor, RefractionFactor);
        }
    }
}
