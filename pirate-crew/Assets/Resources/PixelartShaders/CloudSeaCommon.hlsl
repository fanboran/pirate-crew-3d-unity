#ifndef PC3D_CLOUDSEA_COMMON_INCLUDED
#define PC3D_CLOUDSEA_COMMON_INCLUDED

// ============================================================================
// CloudSeaCommon.hlsl —— 云端漫步海面族（ClearWater / Seabed / Underwater / Ripple）
// 共用的函数：艺术像素取样板、正交视深、主光方向与岛影二档。
// 哈希 / 值噪声 / 顶点形变 / 折射偏移在 CloudSeaAnim.hlsl（只依赖 Core.hlsl）。
//
// 【为什么要有「艺术像素取样板」】PostLine 的几何与着色跑在全分辨率，事后才降采样到艺术
//   画布；降采样以 2×2 块为单位取色，全分辨率上画的细条纹 / 单像素点会被平均成半透明脏色。
//   做法是程序纹理一律在「本片元所属艺术像素的中心」上求值——同一艺术像素内的片元拿到
//   同一个值，色块边缘落在艺术像素格上，和手绘像素画一样干净。
//
// 【Inspector 分组标题只写英文】ShaderLab 的 [Header(...)] 不接受非 ASCII 字符——写中文会让
//   整支 shader 的属性块解析报错、一个片都画不出（不报在那一行，极难定位）。中文说明一律
//   写在属性的显示名（引号内）里：Inspector 上是「英文分组 + 中文条目」。
// 【只被前向 pass include】本文件不含材质属性（UnityPerMaterial 由各 shader 自己声明），
// 也不定义 Attributes / Varyings（URP 标准 pass 的同名结构体会冲突）。
// ============================================================================

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "CloudSeaAnim.hlsl"

// rig 每帧下发的全局（与 PixelartPath.SamplingScaleId / UnitSizeId / LightDirId 同名）。
// 全局缺失（材质预览等未接线场合）时各处都有兜底。
float  _PixelartSamplingScale;   // 1 个艺术像素 = 几个屏幕像素
float  _PixelartUnitSize;        // 1 个艺术像素 = 多少米（地面平面上）
float4 _PixelartLightDirWS;      // 主光方向（世界空间，指向光源）
float  _PixelartWaterY;          // 海面高度（世界 Y；CloudSeaWater 下发，岛影起伏的入水点用）

// 本片元所属艺术像素的中心（全分辨率纹素坐标）。
float2 CloudSeaArtPixelCentre(float2 positionCS)
{
    float k = max(_PixelartSamplingScale, 1.0);
    return (floor(positionCS / k) + 0.5) * k;
}

// 把片元的世界位置平移到其艺术像素中心。正交相机下「屏幕像素位移 → 世界位移」是常量
// 线性映射，用屏幕导数现取：像素比例 / 相机方位 / 表面坡度怎么变都对。
// 必须在片元着色器的均匀控制流里调用（内部用 ddx / ddy）。
float3 CloudSeaArtPixelWorld(float3 positionWS, float2 positionCS)
{
    float2 dpx = CloudSeaArtPixelCentre(positionCS) - positionCS;
    return positionWS + ddx(positionWS) * dpx.x + ddy(positionWS) * dpx.y;
}

// 正交相机的视深：深度缓冲原始值在近 / 远裁面之间线性变化。
// URP 的 LinearEyeDepth 是透视的 1/z 公式，正交相机下不适用。
float CloudSeaEyeDepth(float rawDepth)
{
#if UNITY_REVERSED_Z
    rawDepth = 1.0 - rawDepth;
#endif
    return lerp(_ProjectionParams.y, _ProjectionParams.z, rawDepth);
}

// 主光方向：优先 rig 下发的全局（与 PostLineToonLit 同源），未接线时回退 URP 主光。
float3 CloudSeaLightDir(float3 fallbackDirection)
{
    return dot(_PixelartLightDirWS.xyz, _PixelartLightDirWS.xyz) > 1e-8
        ? normalize(_PixelartLightDirWS.xyz)
        : fallbackDirection;
}

// ---------------------------------------------------------------------------
// 岛影交界线随波起伏
// 水面的波不直接画（水面画法已定）。光穿过起伏的水面再落到水里的东西上，岛影的边就沿波进退——
// 水面色、海床、水下生物上的影边是**同一条**，所以三处共用下面这一个探点公式：各读各的材质
// 就只有水色那条边在动，海床上的影边不动，会露出一条对不上的接缝。
// 参数是全局（CloudSeaWater 从海面材质读出后每帧下发）；幅度 ≤ 0 = 不起伏。
// ---------------------------------------------------------------------------

float4 _CloudSeaShadowWave;     // x = 波数（弧度/米）, y = 角速度（弧度/秒）, z = 幅度（米）, w = 定帧帧率
float4 _CloudSeaShadowWaveDir;  // xy = 波行进方向（世界 XZ，已归一）

// 岛影查点：把世界位置沿「光来的水平方向」按该处波高推一段，结果拿去查阴影图。
// 波峰处的点更高，影边朝光源退；波谷处前伸。幅度是夸张的（真实波高只有厘米级）。
// 波高取在「照到这一点的光线穿入水面的地方」而不是这一点脚下——海床比水面低两三米，照到
// 海床的光是在上风处入水的；两边取同一点，水面色与海床上的影边才是同一条波。
// ws 必须已是艺术像素中心（取样板），否则影边会碎在亚像素上。
float3 CloudSeaShadowProbe(float3 ws)
{
    if (_CloudSeaShadowWave.z <= 0.0)
        return ws;
    float3 toLight = CloudSeaLightDir(float3(0.0, 1.0, 0.0));
    float2 lightXZ = toLight.xz * rsqrt(dot(toLight.xz, toLight.xz) + 1e-6);
    float2 entry = ws.xz + lightXZ * ((_PixelartWaterY - ws.y) / max(toLight.y, 0.2));
    float t = _CloudSeaShadowWave.w > 0.5
        ? floor(_Time.y * _CloudSeaShadowWave.w) / _CloudSeaShadowWave.w
        : _Time.y;
    float phase = dot(entry, _CloudSeaShadowWaveDir.xy) * _CloudSeaShadowWave.x - t * _CloudSeaShadowWave.y
        + (CloudSeaNoise(entry * 0.15 + 5.3) - 0.5) * 2.4;      // 波峰线被低频噪声扰动，不呆板
    // 按 (x, y, z) 逐个分量拼回去，不要写 float3(ws.xz + 偏移, ws.y)：那样拼出来的是 (x, z, y)，
    // 探点会跑到十几米外，查到的永远是「没被挡」。
    float2 offset = lightXZ * (sin(phase) * _CloudSeaShadowWave.z);
    return float3(ws.x + offset.x, ws.y, ws.z + offset.y);
}

// 岛影二档：主光阴影图硬切（1 = 阳光，0 = 岛影）。主光没开阴影时衰减恒 1，整片算阳光。
// 调用方需要声明 _MAIN_LIGHT_SHADOWS 族关键字（见各 shader 的 multi_compile）。
float CloudSeaSunMask(float3 positionWS, float cut)
{
    Light mainLight = GetMainLight(TransformWorldToShadowCoord(positionWS));
    return step(cut, mainLight.distanceAttenuation * mainLight.shadowAttenuation);
}

#endif
