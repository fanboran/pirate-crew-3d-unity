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
        /// cb0[131] waterColor（A 表 §2.1「锚定」行）：透射染色，σ≡0 下即透射本色。
        /// 值 (0.9603, 0.9858, 0.9995) = Water 材质序列化值（交接档 §3；
        /// A 表 §4 注：运行时来自当前水体 preset，菜单场景以 frida 台账为准——
        /// 抓帧对账点）。
        /// </summary>
        static readonly Vector4 WaterColor = new Vector4(0.9603f, 0.9858f, 0.9995f, 1f);

        /// <summary>
        /// cb0[121] waterScatteringColor（A 表 §2.1「锚定」行）：散射补色，把水体往
        /// 沙色推的暖橙。值 (0.5684, 0.3569, 0) = Water 材质序列化值（交接档 §3；
        /// 运行时对账口径同上）。
        /// </summary>
        static readonly Vector4 ScatterColor = new Vector4(0.5684f, 0.3569f, 0f, 1f);

        /// <summary>
        /// cb0[125] sunColor（A 表 §2.1 槽 125）：镜面染色。
        /// 取值 (1.0, 0.9709, 0.9403) = A 表给的主菜单时刻真值（交接档 §3 灯，
        /// level0 SunLight m_Color 直读）。
        /// TODO(时刻对账)：H 流（交接档 §3 边框/时刻行）给出 DayCycler 在固定时刻
        /// t=1/3 Morning 的运行时 sunColor = (1, 0.9404, 0.8724)——与 A 表的灯序列化值
        /// 差一个色温档。I1 按任务书落 A 表值；I8（固定时刻装配）接线时两值对账闭此。
        /// </summary>
        static readonly Vector4 SunColor = new Vector4(1.0f, 0.9709f, 0.9403f, 1f);

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
            Shader.SetGlobalVector(IdScatterColor, ScatterColor);
            Shader.SetGlobalVector(IdSunColor, SunColor);
            Shader.SetGlobalFloat(IdRefractionFactor, RefractionFactor);
        }
    }
}
