// ============================================================================
// RimLight.hlsl —— 像素化着色路径的**边缘光门控**（艺术画布域）
//
// 【边缘光是什么，不是什么】边缘光是**光照层的边缘提亮**：在物件的边（前后景交界、
//   物件与物件的相接处）按光的方向提亮一档。它与**墨线（屏幕空间描边）正交**——
//   墨线是"画上去的线"，存在 `_PixelartOutlineBuffer.r`，由描边趟写、由合成趟取；
//   边缘光是加进光照和的一项。本文件里出现 Outline/Ink 字样的**唯一**目的是那条
//   排除规则（墨线像素输出 0），**不要**把两者混成一个概念——它们只是先后关系。
//
// 【门控公式的来源】v3 `$P/Shaders/Includes/RimLight.hlsl:16-20`，逐行翻译：
//     float2 screenSpaceLightDir = normalize(TransformWorldToViewDir(direction).xy);
//     modifier = (1.0 - connectedToRight) * clamp( screenSpaceLightDir.x, 0.0, 1.0)
//              + (1.0 - connectedToLeft ) * clamp(-screenSpaceLightDir.x, 0.0, 1.0)
//              + (1.0 - connectedToUp   ) * clamp( screenSpaceLightDir.y, 0.0, 1.0)
//              + (1.0 - connectedToDown ) * clamp(-screenSpaceLightDir.y, 0.0, 1.0);
//   四个 clamp 项**互斥**（归一化后的 xy 至多一个分量为正），所以 modifier ∈ [0,1]。
//
// 【为什么光向取主光】v3 这一趟的光源是它那 32 个逐物件 rim 光组件
//   （`SloanePixelartRimlight.cs`，方向 = 组件 forward）；本仓的光源契约只有主光
//   （`_PixelartLightDirWS`，指向光源）与环境光（契约 §2.2），没有 rim 光列表这种东西
//   ⇒ 光向取主光，颜色取逐物件边缘光色（`_PixelartRimLightPropertyBuffer.rgb`）。
//
// 【connected 的确切含义】契约 §4.3 的 `(1 - connected)` 就是 v3 那个函数的入参
//   connectedToX；而调用点传的是 **`connectedToX | !closerThanX`**
//   （v3 `ShadingPass.hlsl:270` 与 `:280`，两处一致）。落回本仓即：
//     · connectedX = 1（该像素与该方向的邻像素同面连通）⇒ 这一路彻底关掉
//       ——面内部不该出现边缘光；
//     · closerX = 0（本像素比该方向的邻像素**更远**）⇒ 也关掉。
//       少了这一条，物件与物件相接处**两侧同时亮**（渲染管线-等距像素卡通.md:94
//       记的那条"rim light 与描边叠加会双倍边缘"）；加回来之后只在更近的一侧出。
//   这不是改动 v3，而是把契约那句简写落回 v3 的真实语义。
//
// 【.a 那个打包字节怎么解】契约 §1.2：`_PixelartConnectivityResultBuffer.a` = 打包的
//   四方向 connected/closer，位布局照 v3 `ConnectivityResult.compute:85`：
//     bit7 connectedRight  bit6 connectedLeft  bit5 connectedUp   bit4 connectedDown
//     bit3 closerRight     bit2 closerLeft     bit1 closerUp      bit0 closerDown
//   v3 把这一字节塞进 SNorm16 的高位：`PackFloatInt8bit(0.0, bits, 256.0)`。展开 core 的
//   实现（`Packing.hlsl:438-446`，maxi = precision = 256）得 t1 = 0、t2 = 1/255
//   ⇒ 该表达式**化简后就是 bits/255**。所以本仓"直存"（契约 §6 第 7 条）在 ARGB32 的 .a
//   上写入 bits/255 时，与 v3 的数值**逐位一致**，解码只需 round(a × 255)，
//   **不需要** include core 的 Packing.hlsl（少一条 include 链 = 少一个静默失效点）。
// ============================================================================

#ifndef PIXELART_RIMLIGHT_INCLUDED
#define PIXELART_RIMLIGHT_INCLUDED

// 打包字节的位掩码（与 ConnectivityResult.compute:85 的移位一一对应）。
#define PIXELART_CONNECTIVITY_CONNECTED_RIGHT (128u)   // bit7
#define PIXELART_CONNECTIVITY_CONNECTED_LEFT  ( 64u)   // bit6
#define PIXELART_CONNECTIVITY_CONNECTED_UP    ( 32u)   // bit5
#define PIXELART_CONNECTIVITY_CONNECTED_DOWN  ( 16u)   // bit4
#define PIXELART_CONNECTIVITY_CLOSER_RIGHT    (  8u)   // bit3
#define PIXELART_CONNECTIVITY_CLOSER_LEFT     (  4u)   // bit2
#define PIXELART_CONNECTIVITY_CLOSER_UP       (  2u)   // bit1
#define PIXELART_CONNECTIVITY_CLOSER_DOWN     (  1u)   // bit0

/// 把 `_PixelartConnectivityResultBuffer.a` 的采样值还原成那个打包字节（0..255）。
/// 【为什么 +round】ARGB32 的 .a 是 8 位 UNorm：写进去的 bits/255 取回来带一点量化误差，
/// 直接乘 255 取整会在 127/255 这类值上差 1（一位之差就是"某个方向的门控翻面"）。
uint PixelartUnpackConnectivityByte(float packedValue)
{
    return (uint)round(saturate(packedValue) * 255.0);
}

/// 单方向权重：该方向连通、或本像素比该方向更远 ⇒ 0；否则取光向在该方向的屏幕分量。
float PixelartRimDirectionWeight(uint packed, uint connectedBit, uint closerBit, float lightComponent)
{
    bool connectedThisWay = (packed & connectedBit) != 0u;
    bool closerThisWay = (packed & closerBit) != 0u;

    // v3 调用点的 `connectedToX | !closerThanX`（!= 0 即 1 的整数或）。
    float blocked = (connectedThisWay || !closerThisWay) ? 1.0 : 0.0;
    return (1.0 - blocked) * clamp(lightComponent, 0.0, 1.0);
}

/// 屏幕空间光向门控（= 契约 §4.3 的完整门控，v3 `RimLight.hlsl:16-20`）。
/// <paramref name="packed"/> = PixelartUnpackConnectivityByte 的结果。
float PixelartRimLightModifier(uint packed, float3 lightDirWS)
{
    // 【本仓对 v3 的一处加固】v3 `:16` 是裸的 normalize(...)。光向恰好沿视轴时
    // TransformWorldToViewDir(direction).xy = (0,0)，normalize(0,0) = NaN，而 NaN 会穿过
    // clamp / 比较 / 相加一路污染整个结果（"某些像素颜色不可预测"这类最难的症状）。
    // 本仓取"沿视轴 ⇒ 没有屏幕方向分量 ⇒ 门控为 0"（边缘光消失），是可预期的降级。
    float2 screenLightDir = TransformWorldToViewDir(lightDirWS).xy;
    float lengthOfScreenDir = length(screenLightDir);
    screenLightDir = lengthOfScreenDir > 1e-5 ? screenLightDir / lengthOfScreenDir : float2(0.0, 0.0);

    float modifier = 0.0;
    modifier += PixelartRimDirectionWeight(packed, PIXELART_CONNECTIVITY_CONNECTED_RIGHT, PIXELART_CONNECTIVITY_CLOSER_RIGHT,  screenLightDir.x);
    modifier += PixelartRimDirectionWeight(packed, PIXELART_CONNECTIVITY_CONNECTED_LEFT,  PIXELART_CONNECTIVITY_CLOSER_LEFT,  -screenLightDir.x);
    modifier += PixelartRimDirectionWeight(packed, PIXELART_CONNECTIVITY_CONNECTED_UP,    PIXELART_CONNECTIVITY_CLOSER_UP,     screenLightDir.y);
    modifier += PixelartRimDirectionWeight(packed, PIXELART_CONNECTIVITY_CONNECTED_DOWN,  PIXELART_CONNECTIVITY_CLOSER_DOWN,  -screenLightDir.y);
    return modifier;
}

/// 边缘光着色 = 门控 × 逐物件边缘光色（契约 §4.3 的链条本体）。
///
/// 【v3 在这条链上还有两步，本仓**故意没接**，改动前先读这里】
///   ① `RimLight.hlsl:12-14` 的 `ndotl = saturate(dot(direction, normal)) * factor`：
///      v3 要"表面朝向那盏 rim 光"才亮。本仓的光向是主光，加它就等于"只有朝主光的面
///      才吃边缘光"——那是**背光侧收窄**这个美术裁决（渲染管线-等距像素卡通.md:94），
///      不是契约 §4.3 冻结的链条。
///   ② `RimLight.hlsl:22` 的 `modifier = multiStep(modifier * ndotl, level, 0, 0)`：
///      它把门控量化成 0/1 两态（v3 调用点固定 level = 2.0）。接到本仓需要一个档数
///      **全局名**，而契约 §2.2 没有登记任何 rim 档数全局 ⇒ 现在没有合法的名字可用。
///   要接这两步：先补契约（登记档数全局），再把两行加回本函数——**不要**在别处偷偷乘。
float3 PixelartRimLightShading(uint packed, float3 lightDirWS, float3 rimLightColor)
{
    return rimLightColor * PixelartRimLightModifier(packed, lightDirWS);
}

#endif // PIXELART_RIMLIGHT_INCLUDED
