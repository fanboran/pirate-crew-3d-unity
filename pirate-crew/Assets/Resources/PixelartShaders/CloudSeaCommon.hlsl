#ifndef PC3D_CLOUDSEA_COMMON_INCLUDED
#define PC3D_CLOUDSEA_COMMON_INCLUDED

// ============================================================================
// CloudSeaCommon.hlsl —— 云端漫步海面族（ClearWater / Seabed / Underwater / Ripple）
// 共用的函数：艺术像素取样板、正交视深、值噪声、主光方向与岛影二档。
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

// rig 每帧下发的全局（与 PixelartPath.SamplingScaleId / UnitSizeId / LightDirId 同名）。
// 全局缺失（材质预览等未接线场合）时各处都有兜底。
float  _PixelartSamplingScale;   // 1 个艺术像素 = 几个屏幕像素
float  _PixelartUnitSize;        // 1 个艺术像素 = 多少米（地面平面上）
float4 _PixelartLightDirWS;      // 主光方向（世界空间，指向光源）

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

// 哈希与值噪声：全部是解析函数，不依赖贴图（海面族没有贴图，也就没有导入设置与采样漂移）。
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

// 主光方向：优先 rig 下发的全局（与 PostLineToonLit 同源），未接线时回退 URP 主光。
float3 CloudSeaLightDir(float3 fallbackDirection)
{
    return dot(_PixelartLightDirWS.xyz, _PixelartLightDirWS.xyz) > 1e-8
        ? normalize(_PixelartLightDirWS.xyz)
        : fallbackDirection;
}

// 岛影二档：主光阴影图硬切（1 = 阳光，0 = 岛影）。主光没开阴影时衰减恒 1，整片算阳光。
// 调用方需要声明 _MAIN_LIGHT_SHADOWS 族关键字（见各 shader 的 multi_compile）。
float CloudSeaSunMask(float3 positionWS, float cut)
{
    Light mainLight = GetMainLight(TransformWorldToShadowCoord(positionWS));
    return step(cut, mainLight.distanceAttenuation * mainLight.shadowAttenuation);
}

#endif
