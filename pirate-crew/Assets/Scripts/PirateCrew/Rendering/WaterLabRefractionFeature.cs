using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PirateCrew.Rendering
{
    /// <summary>
    /// WaterLab 白水粒子（W4）的 GPU 缓冲桥：WaterLabSimulation 每帧刷新，渲染 pass 读。
    /// 跨程序集单向桥（模拟在 Gameplay 程序集、渲染在 Rendering 程序集）；非实验场景
    /// 恒 null，pass 跳过。Args 为 DrawProceduralIndirect 参数 (6, count, 0, 0)。
    /// </summary>
    public static class WaterLabWhitewaterBridge
    {
        public static ComputeBuffer Pos;  // xyz=世界坐标 w=寿命 [0,1]
        public static ComputeBuffer Vel;  // xyz=速度 w=基准尺寸 (m)
        public static ComputeBuffer Draw; // 活粒子槽位 draw list（实例号→池槽）
        public static ComputeBuffer Args; // (6, count, 0, 0)

        public static void Publish(ComputeBuffer pos, ComputeBuffer vel, ComputeBuffer draw, ComputeBuffer args)
        {
            Pos = pos; Vel = vel; Draw = draw; Args = args;
        }

        public static void Clear() => Publish(null, null, null, null);
    }

    /// <summary>
    /// WaterLab 白水粒子（W4，外包债③，对标 Sandcastle Whitewater）的出画 pass：
    /// GPU indirect 点精灵（模拟侧 Compact kernel 产出 drawList、End kernel 产出
    /// drawArgs），DrawProceduralIndirect 画在透明队列之后（AfterRenderingTransparents，
    /// 水体 Transparent+10 已写深度——浪背面的白水被正确遮挡）。
    /// 折射的「沙渲→拷贝→水渲」三段序不在此处：水体排透明调度位后由 URP 内建
    /// CopyColorPass（_CameraOpaqueTexture）天然供帧色。
    /// 【闸门】<c>_WaterLabSimEnabled</c> 全局由 WaterLabSimulation 发布：非 WaterLab
    /// 场景恒 0 直通零开销；SceneView/预览相机也跳过。Feature 挂在现役渲染器资产上
    /// （WaterLabSetup.EnsureRefractionFeature 幂等注入，白水材质由装配器注入）。
    /// </summary>
    public sealed class WaterLabRefractionFeature : ScriptableRendererFeature
    {
        static readonly int SimEnabledId = Shader.PropertyToID("_WaterLabSimEnabled");
        static readonly int WWPosId = Shader.PropertyToID("_WaterLabWWPos");
        static readonly int WWVelId = Shader.PropertyToID("_WaterLabWWVel");
        static readonly int WWDrawId = Shader.PropertyToID("_WaterLabWWDraw");

        /// <summary>白水粒子材质（装配器注入，随 Feature 子资产序列化；null = 不画白水）。</summary>
        [field: SerializeField] public Material WhitewaterMaterial { get; set; }

        sealed class WhitewaterPass : ScriptableRenderPass
        {
            readonly WaterLabRefractionFeature _owner;

            public WhitewaterPass(WaterLabRefractionFeature owner)
            {
                _owner = owner;
                renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                var mat = _owner.WhitewaterMaterial;
                if (mat == null || WaterLabWhitewaterBridge.Args == null)
                    return;

                var cmd = CommandBufferPool.Get("WaterLabWhitewater");
                cmd.SetGlobalBuffer(WWPosId, WaterLabWhitewaterBridge.Pos);
                cmd.SetGlobalBuffer(WWVelId, WaterLabWhitewaterBridge.Vel);
                cmd.SetGlobalBuffer(WWDrawId, WaterLabWhitewaterBridge.Draw);
                cmd.DrawProceduralIndirect(Matrix4x4.identity, mat, 0,
                    MeshTopology.Triangles, WaterLabWhitewaterBridge.Args, 0);
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }

        WhitewaterPass _pass;

        public override void Create() => _pass ??= new WhitewaterPass(this);

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (WhitewaterMaterial == null || WaterLabWhitewaterBridge.Args == null)
                return;
            if (renderingData.cameraData.cameraType != CameraType.Game)
                return;
            if (Shader.GetGlobalFloat(SimEnabledId) < 0.5f)
                return;
            renderer.EnqueuePass(_pass);
        }
    }
}
