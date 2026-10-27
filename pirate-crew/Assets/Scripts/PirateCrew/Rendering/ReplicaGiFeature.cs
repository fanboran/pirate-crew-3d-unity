using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PirateCrew.Rendering
{
    /// <summary>
    /// WaterLab GI 复刻（I3a）的 URP 注入件：在主相机渲染序内调
    /// <see cref="WaterLab.ReplicaGiSystem.Render"/>（GenerateMips + Gi/BlurGi 派发 +
    /// SetGlobalTexture("giVoxels")）；sceneVoxels 的顶视采集由
    /// <see cref="WaterLab.ReplicaGiSystem"/> 自建的顶视相机完成（URP 相机序自动渲染）。
    ///
    /// 【为什么在 Rendering 程序集（任务书 pinned 路径为 .../WaterLab/）】
    /// ScriptableRendererFeature 需要 Unity.RenderPipelines.Universal.Runtime 引用；
    /// Assets/Scripts/PirateCrew/WaterLab/ 目录隶属 PirateCrew.Gameplay.asmdef，该
    /// asmdef 与按名引用它的 PirateCrew.Tests.asmdef 均为本任务禁改的现有文件——
    /// 改前者破坏"禁改"，在 WaterLab/ 加新 asmdef 会把现有四文件改挂程序集、直接
    /// 编红 Tests。故本文件落 Rendering（<see cref="WaterLabRefractionFeature"/> 先例
    /// 同处），系统逻辑留在 Gameplay 侧 ReplicaGiSystem；跨程序集经"每帧回灌
    /// Compute 引用"解耦（沿 WaterLabWhitewaterBridge 单向桥先例，Gameplay→Rendering
    /// 单向引用不变）。
    ///
    /// 【注入点依据（E 报告 rt/reports/E-gi-pipeline.md §2.1/§3/§4.4）】
    /// 原作帧序：beach.Draw()（plants→readback→TopView 渲染→gi.Draw 的 Gi+BlurGi
    /// 派发）整体先于 ScriptableRenderPipeline.Render——主相机画第一个像素前 GI 已
    /// 就绪。URP 等价拆两半：
    ///   ① TopView 渲染 → 顶视相机 depth=-100，URP 按相机序先于主相机（等效"管线
    ///      Render 之前"，任务书括号"beach.Draw 之后管线 Render 前"的前半段）；
    ///   ② Gi/BlurGi 派发 → 本 pass 挂 BeforeRenderingOpaques。E §4.4 明确要求
    ///      "BeforeRenderingOpaques 之前完成 Gi+BlurGi 派发"；且 opaque 序的
    ///      Lit/Sand GI 变体是深水亮度真身的载体（E §3），派发必须先于 opaque 绘制。
    /// 任务书括号里"AfterRenderingOpaques 之后、透明水之前"曾评估并否决：晚于
    /// opaque 的挂点会让 opaque 消费者读到上一帧 giVoxels，偏离原作时序。
    /// </summary>
    public sealed class ReplicaGiFeature : ScriptableRendererFeature
    {
        /// <summary>
        /// Gi compute（WaterLabGi.compute）。装配侧注入（子资产序列化，随渲染器
        /// 资产持久——沿白水材质注入手法），每帧回灌给 ReplicaGiSystem.Compute
        /// （运行时静态不持久，域重载后由此恢复）。
        /// </summary>
        [field: SerializeField] public ComputeShader GiCompute { get; set; }

        static readonly int SimEnabledId = Shader.PropertyToID("_WaterLabSimEnabled");

        sealed class GiPass : ScriptableRenderPass
        {
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                var cmd = CommandBufferPool.Get("WaterLabGi");
                WaterLab.ReplicaGiSystem.Render(cmd);
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }

        GiPass _pass;

        public override void Create() => _pass ??= new GiPass();

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (GiCompute == null)
                return;
            if (Shader.GetGlobalFloat(SimEnabledId) < 0.5f)
                return; // 非 WaterLab 场景（模拟未启用）直通零开销——闸门同 WaterLabRefractionFeature
            if (renderingData.cameraData.cameraType != CameraType.Game)
                return; // SceneView/预览相机跳过（同上）

            // 每帧回灌 compute 引用（幂等；防域重载丢失）
            WaterLab.ReplicaGiSystem.Compute = GiCompute;

            // 顶视相机自己渲 sceneVoxels 时不再入队本 pass（防递归/重复派发）
            if (renderingData.cameraData.camera == WaterLab.ReplicaGiSystem.TopViewCamera)
                return;

            renderer.EnqueuePass(_pass);
        }
    }
}
