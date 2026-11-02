using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PirateCrew.Rendering
{
    /// <summary>
    /// WaterLab GI 的跨程序集单向桥（模拟/系统在 Gameplay 程序集、Feature 在 Rendering
    /// 程序集；Rendering 不引用 Gameplay——同 WaterLabWhitewaterBridge 先例）。
    /// ReplicaGiSystem 静态构造接线；非实验场景恒 null，pass 跳过。
    /// </summary>
    public static class WaterLabGiBridge
    {
        public static ComputeShader Compute;                   // Feature → System（域重载回灌）
        public static System.Action<CommandBuffer> Render;     // System：dispatch+全局发布
        public static Camera TopViewCamera;                    // System：顶视相机（矩阵/剔除源）
        /// <summary>
        /// sceneVoxels RT（System 建资源后回灌，Shutdown 置 null）。A 流起顶视采集改由
        /// 本程序集手动执行（opaque×"WaterLabVoxelizer" tag 单 draw，见 ReplicaGiFeature），
        /// 本字段是该 draw 的目标。
        /// </summary>
        public static RenderTexture SceneVoxels;
    }

    /// <summary>
    /// WaterLab GI 复刻（I3a；A 流闭合 Voxelizer 采集）的 URP 注入件：在主相机渲染序
    /// 内（BeforeRenderingOpaques 首部）完成 ①顶视 Voxelizer 采集（写 sceneVoxels）→
    /// ②调 <see cref="WaterLab.ReplicaGiSystem.Render"/>（GenerateMips + Gi/BlurGi 派发 +
    /// SetGlobalTexture("giVoxels")）。
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
    /// 就绪。URP 等价：全部收进主相机 BeforeRenderingOpaques 首部的同一 pass——
    ///   ① TopView 渲染（原作 ProcessRenderRequests 的 TopView 分支，E §1.3①）：
    ///     SetRenderTarget(sceneVoxels)→Clear→DrawRenderers(opaque × "WaterLabVoxelizer"
    ///     tag)→…；
    ///   ② Gi/BlurGi 派发：紧随其后，先于本帧 opaque/transparent 绘制——E §4.4 要求
    ///     "BeforeRenderingOpaques 之前完成派发"，且 opaque 序的 Lit/Sand GI 变体是
    ///     深水亮度真身载体（E §3）。先 draw 后 dispatch，与原作 beach.Draw 内
    ///     TopView 先于 gi.Draw 的序一致。
    /// 任务书括号里"AfterRenderingOpaques 之后、透明水之前"曾评估并否决：晚于
    /// opaque 的挂点会让 opaque 消费者读到上一帧 giVoxels，偏离原作时序。
    ///
    /// 【顶视采集方案决策（A 流，任务书"选实现最短者"）】原作=顶视正交相机以
    /// Voxelizer tag 渲 opaque 进 sceneVoxels。候选：a) 让相机进 URP 循环走完整管线
    /// ——需专用 Renderer 资产过滤 LightMode，且 skybox/后处理全套陪跑，重；b)
    /// Camera.RenderWithShader 旧法——SRP 下不生效（管线接管相机渲染），不可用；
    /// c) 本实现：DrawRenderers(ShaderTagId 列表) + RenderStateBlock 交空（状态全由
    /// 各 shader 的 Voxelizer pass 自带），cmd.SetViewProjectionMatrices 覆写为顶视
    /// 正交矩阵。c 是原作语义的最短精确路径（单 draw、无管线陪跑），取 c。
    /// </summary>
    public sealed class ReplicaGiFeature : ScriptableRendererFeature
    {
        /// <summary>
        /// Gi compute（WaterLabGi.compute）。装配侧注入（子资产序列化，随渲染器
        /// 资产持久——沿白水材质注入手法），每帧回灌给 ReplicaGiSystem.Compute
        /// （运行时静态不持久，域重载后由此恢复）。
        /// </summary>
        [field: SerializeField] public ComputeShader GiCompute { get; set; }

        static int _simEnabledId;
        static int SimEnabledId => _simEnabledId != 0
            ? _simEnabledId : _simEnabledId = Shader.PropertyToID("_WaterLabSimEnabled");

        // 顶视 Voxelizer 采集（原作 "Voxelizer" ShaderTag 的 URP 名字面，三消费 shader
        // 的 Voxelizer pass 同名）：opaque 队列过滤 + tag 列表即原作语义。
        // ShaderTagId ctor 同样内部走 TagToID（rt0i 实证）——一并惰性。
        static List<ShaderTagId> _voxelizerShaderTags;
        static List<ShaderTagId> VoxelizerShaderTags => _voxelizerShaderTags ??=
            new List<ShaderTagId> { new ShaderTagId("WaterLabVoxelizer") };

        // cmd.SetViewProjectionMatrices 不写逆矩阵——URP SetCameraMatrices 在该方法之后
        // 单独 SetGlobalMatrix(inverseViewMatrix) 即反证（URP 14 ScriptableRenderer.cs
        // :161-182）。植物 Voxelizer 的 unproject 读 UNITY_MATRIX_I_V（= unity_MatrixInvV，
        // URP Input.hlsl:192 引擎常量），覆写后须显式补、用后须显式还。
        // 【TagToID 禁入 cctor】ScriptableObject 构造/反序列化上下文里 Shader.PropertyToID
        // 抛 UnityException（Install() 的 CreateInstance 首次触发，I3 潜伏 bug）——
        // 改惰性：首次渲染调用时求值（合法上下文）。
        static int _unityMatrixInvVId;
        static int UnityMatrixInvVId => _unityMatrixInvVId != 0
            ? _unityMatrixInvVId : _unityMatrixInvVId = Shader.PropertyToID("unity_MatrixInvV");
        // 顶视视线起点：覆写矩阵不更新 _WorldSpaceCameraPos（它随主相机常量集走），
        // 植物 Voxelizer VS 以此构造 imposter 视向。
        static int _voxelEyeId;
        static int VoxelEyeId => _voxelEyeId != 0
            ? _voxelEyeId : _voxelEyeId = Shader.PropertyToID("g_WaterLabVoxelEye");

        sealed class GiPass : ScriptableRenderPass
        {
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                // ---- ① 顶视 Voxelizer 采集（sceneVoxels 写入；桥就绪才执行）----
                var topView = WaterLabGiBridge.TopViewCamera;
                if (WaterLabGiBridge.SceneVoxels != null && topView != null
                    && topView.TryGetCullingParameters(false, out var cullingParams))
                {
                    // 顶视视锥剔除（主相机渲染序内二次 Cull 合法——URP 阴影 pass 同款）。
                    CullingResults cull = context.Cull(ref cullingParams);

                    var setup = CommandBufferPool.Get("WaterLabVoxelizer");
                    setup.SetRenderTarget(WaterLabGiBridge.SceneVoxels);
                    // 原作 ClearRenderTarget(true, true)（E 报告 §1.3①）；背景色 = System 相机的黑
                    setup.ClearRenderTarget(true, true, topView.backgroundColor);
                    var view = topView.worldToCameraMatrix;
                    var proj = topView.projectionMatrix;
                    setup.SetViewProjectionMatrices(view, proj);
                    setup.SetGlobalMatrix(UnityMatrixInvVId, view.inverse);
                    setup.SetGlobalVector(VoxelEyeId, topView.transform.position);
                    context.ExecuteCommandBuffer(setup);
                    CommandBufferPool.Release(setup);

                    // opaque × "WaterLabVoxelizer" tag 单 draw。RenderStateBlock 交空：
                    // ZWrite/ZTest/Cull 全按各 pass 自带状态（原作 Voxelizer pass 即带状态）。
                    var drawingSettings = CreateDrawingSettings(
                        VoxelizerShaderTags, ref renderingData, SortingCriteria.CommonOpaque);
                    var filteringSettings = new FilteringSettings(
                        RenderQueueRange.opaque, topView.cullingMask);
                    var stateBlock = new RenderStateBlock(RenderStateMask.Nothing);
                    context.DrawRenderers(cull, ref drawingSettings, ref filteringSettings, ref stateBlock);

                    // 恢复主相机矩阵（URP 每相机只在开头设一次，后续 DrawObjectsPass 不重设
                    // ——不还原会把主相机 opaque 画进顶视矩阵）。InvV 同步还原（与 URP
                    // SetCameraMatrices 的 inverseViewMatrix = Inverse(view) 同算式；I_P/I_VP
                    // 未触碰，维持主相机值——本采集路径的 pass 不得依赖 I_P/I_VP）。
                    var camData = renderingData.cameraData;
                    var restore = CommandBufferPool.Get("WaterLabVoxelizerRestore");
                    restore.SetViewProjectionMatrices(
                        camData.GetViewMatrix(), camData.GetProjectionMatrix());
                    restore.SetGlobalMatrix(UnityMatrixInvVId, camData.GetViewMatrix().inverse);
                    context.ExecuteCommandBuffer(restore);
                    CommandBufferPool.Release(restore);
                }

                // ---- ② compute 链（GenerateMips → Gi → BlurGi → SetGlobalTexture）----
                var cmd = CommandBufferPool.Get("WaterLabGi");
                WaterLabGiBridge.Render?.Invoke(cmd);
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
            WaterLabGiBridge.Compute = GiCompute;

            // 顶视相机已 enabled=false 不进相机循环；此守卫留作防递归兜底
            if (renderingData.cameraData.camera == WaterLabGiBridge.TopViewCamera)
                return;

            renderer.EnqueuePass(_pass);
        }
    }
}
