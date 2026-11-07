using UnityEngine;

namespace PirateCrew.WaterLab
{
    /// <summary>
    /// 夜光浮游生物粒子系统（D 流件4）：Sandcastle Phytoplankton（cs-full，Culling 基类）
    /// 的运行时移植——32768 池、每帧 Update（水场驱动漂移/亮度）+ Culling（寿命 alpha 门
    /// + 主/阴影双视锥）、DrawProceduralIndirect 绘制。
    ///
    /// 【原作调度】Init 派发 Initialization（清池）→ Run 每帧 Update → Draw 在
    /// dayCycler.Night ≥ 0.5 才画（强度 sat(Night−0.5)×2 下发材质 intensity）。主菜单固定
    /// Morning（Night=0）→ <b>系统在跑、画面恒无</b>；本系统随 <see cref="WaterLabSimulation"/>
    /// 自包含创建（禁改 Setup，无场景接线），Night 常量同值（<see cref="WaterLabRules.PhytoNightAmount"/>）。
    ///
    /// 【渲染】原作 Lit_Phytoplankton blob 在 D 流解出时 D3DDisassemble 乱码（D-sand.md
    /// §4.3 遗留，"深色小方片+径向/距离淡出"），PS 逐指令未完成；本系统配
    /// ReplicaPhytoplankton.shader 的简化直译（扁四边形贴水面 + 寿命 alpha 曲线
    /// 1−exp(−4.328·life) + 距离淡出），Night 门控不亮时零绘制开销。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ReplicaPhytoplanktonSystem : MonoBehaviour
    {
        public ComputeShader computeShader;
        public Material material;
        public Camera renderCamera;

        // 缓冲（Culling 基类同构：pool/drawList/drawShadowList/args/argsShadow + 计数）
        ComputeBuffer _pool, _drawList, _shadowList, _count, _args;
        int _kBegin, _kInit, _kUpdate, _kCulling, _kEnd;
        float _simTime;

        static readonly int PoolId = Shader.PropertyToID("_PhytoPool");
        static readonly int WaterId = Shader.PropertyToID("_PhytoWater");
        static readonly int DrawListId = Shader.PropertyToID("_PhytoDrawList");
        static readonly int ShadowListId = Shader.PropertyToID("_PhytoShadowList");
        static readonly int CountId = Shader.PropertyToID("_PhytoCount");
        static readonly int ArgsId = Shader.PropertyToID("_PhytoArgs");
        static readonly int CapacityId = Shader.PropertyToID("_PhytoCapacity");
        static readonly int DtId = Shader.PropertyToID("_PhytoDt");
        static readonly int TimeId = Shader.PropertyToID("_PhytoTime");
        static readonly int HalfDomainId = Shader.PropertyToID("_PhytoHalfDomain");
        static readonly int DriftId = Shader.PropertyToID("_PhytoDrift");
        static readonly int CellsId = Shader.PropertyToID("_PhytoWaterCells");
        static readonly int FrustumId = Shader.PropertyToID("_PhytoFrustum");
        static readonly int ShadowFrustumId = Shader.PropertyToID("_PhytoShadowFrustum");
        static readonly int IntensityId = Shader.PropertyToID("intensity");

        public void Bootstrap(ComputeShader shader, ComputeBuffer waterBuffer, Camera camera)
        {
            computeShader = shader;
            renderCamera = camera;
            _kBegin = shader.FindKernel("PhytoBegin");
            _kInit = shader.FindKernel("PhytoInitialization");
            _kUpdate = shader.FindKernel("PhytoUpdate");
            _kCulling = shader.FindKernel("PhytoCulling");
            _kEnd = shader.FindKernel("PhytoEnd");

            int cap = WaterLabRules.PhytoCapacity;
            _pool = new ComputeBuffer(cap, 16);
            _drawList = new ComputeBuffer(cap, 4);
            _shadowList = new ComputeBuffer(cap, 4);
            _count = new ComputeBuffer(2, 4);
            _args = new ComputeBuffer(4, 4, ComputeBufferType.IndirectArguments);

            foreach (int k in new[] { _kBegin, _kInit, _kUpdate, _kCulling, _kEnd })
            {
                shader.SetBuffer(k, PoolId, _pool);
                shader.SetBuffer(k, WaterId, waterBuffer);
                shader.SetBuffer(k, DrawListId, _drawList);
                shader.SetBuffer(k, ShadowListId, _shadowList);
                shader.SetBuffer(k, CountId, _count);
                shader.SetBuffer(k, ArgsId, _args);
            }
            // 本工程 ComputeShader 的 SetInt/SetFloat 只收全局形式（全 kernel 生效）
            shader.SetInt(CapacityId, cap);
            shader.SetFloat(HalfDomainId, WaterLabRules.DomainSize * 0.5f);
            shader.SetFloat(DriftId, WaterLabRules.PhytoDrift);
            shader.SetInt(CellsId, WaterLabRules.WaterCells);
            shader.Dispatch(_kInit, cap / 64, 1, 1);
        }

        void OnDestroy()
        {
            _pool?.Dispose();
            _drawList?.Dispose();
            _shadowList?.Dispose();
            _count?.Dispose();
            _args?.Dispose();
        }

        /// <summary>水场缓冲重绑（宿主 ping-pong 换位后最新场对象会变，每帧由宿主调用）。</summary>
        public void RebindWater(ComputeBuffer waterBuffer)
        {
            if (computeShader == null)
                return;
            computeShader.SetBuffer(_kUpdate, WaterId, waterBuffer);
            computeShader.SetBuffer(_kCulling, WaterId, waterBuffer);
        }

        void Update()
        {
            if (computeShader == null)
                return;
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            _simTime += dt;

            computeShader.SetFloat(DtId, dt);
            computeShader.SetFloat(TimeId, _simTime);
            PushFrustum();

            int groups = WaterLabRules.PhytoCapacity / 64;
            computeShader.Dispatch(_kBegin, 1, 1, 1);
            computeShader.Dispatch(_kUpdate, groups, 1, 1);
            computeShader.Dispatch(_kCulling, groups, 1, 1);
            computeShader.Dispatch(_kEnd, 1, 1, 1);

            // 绘制门控（原作 Draw：Night < 0.5 直接 return；强度 sat(Night−0.5)×2）
            if (!WaterLabRules.PhytoVisible || material == null || renderCamera == null)
                return;
            material.SetFloat(IntensityId, WaterLabRules.PhytoIntensity);
            var bounds = new Bounds(Vector3.zero, Vector3.one * WaterLabRules.DomainSize);
            Graphics.DrawMeshInstancedIndirect(
                QuadMesh(), 0, material, bounds, _args, 0,
                null, UnityEngine.Rendering.ShadowCastingMode.Off, false, 0, null,
                UnityEngine.Rendering.LightProbeUsage.Off);
        }

        void PushFrustum()
        {
            var cam = renderCamera != null ? renderCamera : Camera.main;
            var planes = new Plane[6];
            var v4 = new Vector4[6];
            if (cam == null)
            {
                for (int i = 0; i < 6; i++) v4[i] = new Vector4(0f, 0f, 0f, 1f);
            }
            else
            {
                GeometryUtility.CalculateFrustumPlanes(cam, planes);
                for (int i = 0; i < 6; i++)
                    v4[i] = new Vector4(planes[i].normal.x, planes[i].normal.y, planes[i].normal.z, planes[i].distance);
            }
            computeShader.SetVectorArray(FrustumId, v4);
            // 阴影视锥那一路：我方粒子不投影，恒退化平面（全部内侧）
            var degenerate = new Vector4[6];
            for (int i = 0; i < 6; i++) degenerate[i] = new Vector4(0f, 0f, 0f, 1f);
            computeShader.SetVectorArray(ShadowFrustumId, degenerate);
        }

        Mesh _quad;
        Mesh QuadMesh()
        {
            if (_quad != null)
                return _quad;
            _quad = new Mesh { name = "WaterLabPhytoQuad" };
            // 扁四边形（原作白水同构：贴水皮水平小方片；顶点流 position.xy 供 VS 展开）
            _quad.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f),
                                     new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f) };
            _quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            _quad.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            _quad.UploadMeshData(false);
            return _quad;
        }
    }
}
