#ifndef PC3D_CLOUDSEA_ANIM_INCLUDED
#define PC3D_CLOUDSEA_ANIM_INCLUDED

// ============================================================================
// CloudSeaAnim.hlsl —— 云海族里「只依赖 Core.hlsl」的那部分：生物蒙皮、哈希 / 值噪声、水面折射偏移。
// 水下 shader 的四个通道都 include 它，所以不能引 Lighting。
//
// 【蒙皮】水下生物的骨架由运行时的运动学控制器（CloudSeaLife → CloudSeaBrains / CloudSeaCrawlers）
//   每帧解出来，每个关节一个物体空间的仿射 [旋转 × 缩放 | 平移]，写进全局缓冲 _CloudSeaJoints
//   （一个关节占三行，行 = (旋转 × 缩放的一行, 平移分量)，v' = R·v + t）。顶点着色器按顶点上的
//   权重自己蒙皮——不走 SkinnedMeshRenderer，也不用 MaterialPropertyBlock（水下材质按颜色共享，
//   MPB 还会让渲染器掉出 SRP Batcher）。
//
// 【蒙皮数据在顶点上】生物的网格带三套 UV（Blender 侧 creature_kit.py 写入）：
//     uv  = (关节号 A, 权重 A)      关节号 = 全场景统一编号（= 缓冲里的关节号）
//     uv1 = (关节号 B, 权重 B)      没有第二个影响时权重 0
//     uv2 = (1, 个体种子)            uv2.x = 1 = 「这个顶点要蒙皮」；
//                                   海草 / 珊瑚 / 岩石 / 贝没有这一层，读到 0，不蒙皮
//   _CloudSeaSkinOn = 0（编辑器预览、没人在驱动）= 静止姿态。前向与投影两个通道调同一个函数——
//   影子和身体同步。
//
// 【海草不在这里动】海草的摆动在 CloudSeaUnderwater.shader 的 CloudSeaSwayOffset（uv.x = 离根高度比）。
// ============================================================================

StructuredBuffer<float4> _CloudSeaJoints;   // 每个关节三行
float _CloudSeaSkinOn;

// 按顶点权重蒙皮：就地改写物体空间的位置与法线。没有蒙皮标记（uv2.x = 0）或没人在驱动时原样返回。
void CloudSeaSkin(inout float3 posOS, inout float3 nOS, float2 uv, float2 uv1, float2 uv2)
{
    if (_CloudSeaSkinOn < 0.5 || uv2.x < 0.5)
        return;

    int a = (int)uv.x * 3;
    float4 a0 = _CloudSeaJoints[a], a1 = _CloudSeaJoints[a + 1], a2 = _CloudSeaJoints[a + 2];
    float3 p = float3(dot(a0.xyz, posOS) + a0.w, dot(a1.xyz, posOS) + a1.w, dot(a2.xyz, posOS) + a2.w);
    float3 n = float3(dot(a0.xyz, nOS), dot(a1.xyz, nOS), dot(a2.xyz, nOS));

    float wb = uv1.y;
    if (wb > 1e-4)
    {
        int b = (int)uv1.x * 3;
        float4 b0 = _CloudSeaJoints[b], b1 = _CloudSeaJoints[b + 1], b2 = _CloudSeaJoints[b + 2];
        p = lerp(p, float3(dot(b0.xyz, posOS) + b0.w, dot(b1.xyz, posOS) + b1.w, dot(b2.xyz, posOS) + b2.w), wb);
        n = lerp(n, float3(dot(b0.xyz, nOS), dot(b1.xyz, nOS), dot(b2.xyz, nOS)), wb);
    }

    posOS = p;
    nOS = n;
}

// ---------------------------------------------------------------------------
// 哈希与值噪声：全部是解析函数，不依赖贴图（程序纹理没有贴图，也就没有导入设置与采样漂移）。
// ---------------------------------------------------------------------------

float CloudSeaHash12(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float2 CloudSeaHash22(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);
}

float CloudSeaNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(CloudSeaHash12(i), CloudSeaHash12(i + float2(1.0, 0.0)), u.x),
                lerp(CloudSeaHash12(i + float2(0.0, 1.0)), CloudSeaHash12(i + float2(1.0, 1.0)), u.x),
                u.y);
}

float2 CloudSeaNoise2(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(CloudSeaHash22(i), CloudSeaHash22(i + float2(1.0, 0.0)), u.x),
                lerp(CloudSeaHash22(i + float2(0.0, 1.0)), CloudSeaHash22(i + float2(1.0, 1.0)), u.x),
                u.y);
}

// ---------------------------------------------------------------------------
// 水面折射偏移：按世界格把水下画面整块挪 1–2 个艺术像素。参数取自海面材质；偏移量 ≤ 0 = 无折射。
// ---------------------------------------------------------------------------

// 该水面世界位置处的折射偏移（艺术像素，整像素）。waterXZ = 水面平面上该像素的世界 XZ。
float2 CloudSeaRefractOffsetEx(float2 waterXZ, float time, float pixels, float cell, float fps)
{
    if (pixels <= 0.0 || fps <= 0.0)
        return 0.0.xx;
    float timeQ = floor(time * fps) / fps;
    float2 c = floor(waterXZ / max(cell, 0.01));
    float2 wobble = CloudSeaNoise2(c * 0.27 + timeQ * float2(0.22, 0.09))
        + CloudSeaNoise2(c * 0.53 - timeQ * float2(0.10, 0.20) + 37.0) - 1.0;
    return round(wobble * 0.5 * pixels);
}

#endif
