// ============================================================================
// Connectivity.hlsl —— 连通域判据本体（位打包 + 平面预测残差）
//
// 【它是什么】v3 `$P/Shaders/Includes/Connectivity.hlsl:42-104` 的翻译改编，Check / Flood /
//   Result 三段 compute 共用（与三个 .compute 同目录，故 `#include "Connectivity.hlsl"` 直接解析）。
//   两个函数构成判据：
//     · `PixelartConnectivityCheckConnectedDepthNormal`（v3 的 `CheckConnectedDepthNormal`）
//       —— 两端法线各自"预测深度"、与实测深度比残差；**两端残差同时超阈值**才判不连通。
//     · `PixelartConnectivityNormalContinuous`（v3 的 `CheckNormalContinuous`）
//       —— 单元内最大法线差（写进 Result 的 .b，供着色那趟的"法线边加成"用）。
//   函数名一律加 `PixelartConnectivity` 前缀：本文件的编译单元里还会出现描边/着色各自 include
//   的东西，不加前缀容易与它们的同名函数撞（AGENTS.md 的"名字错一个字母就是静默失效"反过来用）。
//
// ---------------------------------------------------------------------------
// 【缓冲语义（契约 §1.1 / §1.3；读之前先对齐这三个量）】
//   · `_PixelartNormal0Buffer`（屏幕档 ARGBHalf）：**世界空间**几何法线
//     `normalize(cross(ddy(positionWS), ddx(positionWS)))`。名字里的"屏幕空间"说的是
//     **求导方式**（屏幕空间导数），不是坐标系——值本身是世界空间的。
//     空像素 = (0,0,0)（物体 pass 用 `Color.clear` 清了 G-buffer），判据靠
//     `dot(n,n) < 0.5` 认它（v3 的 SNorm16 清成 0，语义相同）。
//   · `_PixelartDepthBuffer`（屏幕档 depth 24，可采样）：**深度附件**，不是颜色缓冲。
//     D3D 系是 **reversed-Z**（近 = 1、远 = 0）⇒ 取出的原始值必须先"反反转"再线性化，
//     否则 `closer` 的符号与残差的尺度**全都错**（本文件第 ② 步处理）。
//   · 两张都在**屏幕档**（`FineW × FineH` = 艺术画布 × pixelScale），与 v3 的 1600×900 同构。
//
// ---------------------------------------------------------------------------
// 【v3 → 本仓：坐标系一致化，逐条留痕】（这是本移植唯一需要推导的部分）
//
// ① **判据活在视图空间，而缓冲存的是世界空间的量。**
//      v3 `:78`/`:89` 把取到的法线先 `TransformWorldToViewDir(...)` 再拿去预测；本仓照做
//      （`PixelartConnectivityViewNormal`）。视图空间是判据的自然空间：像素阵列就在视图空间的
//      XY 平面上，`normal.z` 就是"朝向相机"那个分量，而深度差也正是沿视图 Z 的差。
//      `TransformWorldToViewDir` 走 `UNITY_MATRIX_V` = URP `UnityInput.hlsl:214` 的 `unity_MatrixV`，
//      URP 每相机下发成全局量，compute 可见（v3 的 compute 也走这条链）。
//
// ② **深度：原始值 → "眼睛米数"。** v3 的 `LINEAR_DEPTH`（`:5`）分两路：正交走
//      `lerp(_ProjectionParams.y, _ProjectionParams.z, rawDepth)`、透视走
//      `LinearEyeDepth(rawDepth, _ZBufferParams)`，两路都先各自处理 reversed-Z。
//      本仓的 Cast 相机**恒为正交**（`PixelartCameraRig.cs:331` 建它时 `orthographic = true`，
//      `:270` 每帧跟主相机同步），所以走正交那一路。
//      ⚠ **不要用 `LinearEyeDepth(z, _ZBufferParams)` 一把梭**：core 的该重载**不支持正交**
//      （`core/Common.hlsl:1153-1157` 原文 "Does NOT work with orthographic projection"），
//      正交下它把线性的深度揉成非线性，"残差超过 0.25 米"这句话的物理意义随即失去。
//      这里用 URP 自带的 `LinearDepthToEyeDepth(z)`（`ShaderVariablesFunctions.hlsl:434`）：
//      它自己带 `UNITY_REVERSED_Z` 分支，正是 URP 里"采样深度附件 → 眼睛米数"的官方口径
//      （`URP/Particles.hlsl:96` 是同一行写法）。透视相机自动落到 `LinearEyeDepth` 分支。
//      `closer` 的语义因此是"中心比邻者更近"（线性化后小 = 近）；不做这一步时 reversed-Z 下
//      它整个反过来——这正是协调者点名的"符号/尺度全错"。
//
// ③ **视图空间 XY 的步长 = 常数 `_PixelartFineUnitSize`**（本移植唯一一处"换算法"，其余逐行照搬）。
//      v3 用 `GetViewPositionWithDepth(uv, rawDepth)`（`$P/.../Transform.hlsl:26`）把两个像素
//      各自反投影回视图位置、取两端位置之差当"像素间距"——那需要它的 `_CameraMatrixInvV` 全局，
//      本仓不发布相机矩阵，也不该为此新增。而对**正交**相机，视图空间 XY 与世界空间 XY 只差
//      一个相机旋转、**没有透视缩放**，于是相邻细像素的视图 XY 间距恒为 ±`_PixelartFineUnitSize`、
//      与深度无关。这与 v3 的式子在正交下**恒等**：v3 取 `unity_OrthoParams.xy * (uv*2 - 1)`，
//      而 `2 * unity_OrthoParams.xy ÷ 屏幕档尺寸 = 一个细像素的世界尺寸 = FineUnitSize`
//      （`unity_OrthoParams.xy` = 正交相机的半宽/半高，URP `ScriptableRenderer.cs:258`；
//      `FineUnitSize` 由 rig 按 `2 × orthographicSize ÷ FineHeight` 下发，`PixelartCameraRig.FineUnitSize`）。
//      ⇒ `pixelSpacing = -(next - center) * _PixelartFineUnitSize`，**轴向与符号沿用 v3 的式子**
//      （+列索引 ⇒ +视图 X、+行索引 ⇒ +视图 Y）。
//      ⚠ **这是整份文件里唯一一处平台相关假设，留着痕**：行方向在 D3D 与 GL 上相反
//      （`SV_Position.y` 向下 / `gl_FragCoord.y` 向上），而 v3 的式子在正交分支里用的是
//      `unity_OrthoParams`（**不含**投影翻转补偿），只在 GL 系严格成立。本仓先按 v3 原式落，
//      理由有三：① 它是被翻译的那份代码的实际行为，改它就是改观感；② 本仓试点场景是 45° 机位
//      的轴对齐体块，一个细像素的深度差约 0.026 m、远小于 0.25 m 的阈值，且判据要求**两端同时**
//      超阈值 ⇒ 符号在这套内容上观察不到差别；③ 真要改只有一处：`PixelartConnectivityViewStep`
//      里给位移的 y 分量乘 −1。**若出图上出现"沿某一轴向的内线伪影或遗漏"，第一件事就是来这里
//      把 y 的符号翻过来。**
//
// ④ **位打包改为直存**（契约 §6 第 7 条 / 蓝图 §2 注意 1）。v3 用 core 的
//      `PackFloatInt8bit / UnpackFloatInt8bit` 往 **SNorm16** 里塞字节；本仓的
//      ConnectivityDetail / ConnectivityResult 是 **ARGB32（UNorm8，sRGB 关）**，
//      一个字节刚好就是一个通道 ⇒ 直接 `byte / 255`，取回 `round(c * 255)`，往返逐位精确
//      （UNorm8 的取值集合就是 k/255，k∈[0,255]）。**下游（描边 / 四趟着色）读 Result.a
//      时也必须按这条直存口径解包**，不要再套 `UnpackFloatInt8bit`。
//
// ⑤ **判据里的三处 v3 细节逐字保留**（看着像 bug，但 v3 的实际画面就出自它们，改掉就是改观感，
//      故原样保留并在此点名，免得后来者当成抄错）：
//        · `prev` 在循环里**从不推进**（恒等于 center）⇒ 每次迭代的预测都锚在中心像素上；
//        · 预测深度一律以 `centerDepth` 为基准（**含"目标那一端"的预测**，v3 `:94`）；
//        · 残差一律与**端点 target 的实测深度**相减（**含中间步**，v3 `:86`/`:96`）。
//      唯一被本仓挪动的是把 `prev` 的两个不变量提到循环外（`prev` 恒为 center ⇒ 值完全一致，
//      只是少 k−1 次对同一坐标的重复取样）。
//
// ⑥ **`CheckNormalContinuous` 的 `z < 0` 门控改在视图空间判**（本仓口径，[提案]）。
//      v3 拿**世界**法线的 `.z` 判正负（`:21`/`:32`），而世界 Z 轴与相机朝向无关 ⇒ 那是
//      "在 v3 的固定机位下凑巧像'朝向相机'"的遗留写法。本仓的竞技场是固定的 XZ 地面 + 45° 机位，
//      世界 Z 对任何面都不是"朝向相机"，照抄会让水平顶面（世界法线 z = 0）**一律拿不到法线差**。
//      本仓改成与 ① 同一份**视图**法线判 `z < 0`（视图空间里 z < 0 = 面朝相机），
//      即"只有朝向相机的两个邻居之间才认出法线转折"。
//      幅度不受影响：两个单位法线之差是旋转不变量（所以着色那趟的 `_NormalEdgeThreshold`
//      标定值不用改）。⇒ 这一条只影响 Result.b，**不影响连通域本身**。
//
// 【不使用 `_ScreenParams`】（契约 §2.2）：尺寸一律读显式全局。
// ============================================================================

#ifndef PIXELART_CONNECTIVITY_INCLUDED
#define PIXELART_CONNECTIVITY_INCLUDED

// URP 的 Core.hlsl 是"只要 shader 数学 + 相机参数"那一个 include：
// 它给出 `TransformWorldToViewDir`（core `SpaceTransforms.hlsl:155`，由 URP `Input.hlsl:217` 引入）、
// `LinearEyeDepth`（core `Common.hlsl`）、`LinearDepthToEyeDepth`（URP `ShaderVariablesFunctions.hlsl:434`）
// 以及 `_ProjectionParams / _ZBufferParams / unity_OrthoParams / unity_MatrixV`
// （URP `UnityInput.hlsl:54/72/78/214`）。v3 的三个 compute 也是这么 include 的。
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// ---------------- 输入（屏幕档）----------------

/// 深度附件（reversed-Z on D3D）。单通道读取：`.r` = 原始深度。
Texture2D<float> _PixelartDepthBuffer;

/// 屏幕空间几何法线（**世界空间**取值）。空像素 = (0,0,0)。
Texture2D<float4> _PixelartNormal0Buffer;

// ---------------- 尺寸全局（契约 §2.2；由 rig / BeforeRender 下发，本文件只读）----------------
// 【为什么必须有它们】判据要在界外提前返回、要跨步走 k 个细像素，还要按 k×k 单元聚合。
// 这几个全局由协调者侧下发（`Shader.SetGlobalFloat`），**本 Feature 不写**；若为 0 则所有
// 线程的守卫都不通过（派发空转），这是"全局没下发"的可见征兆。

float _PixelartFineWidth;    // 屏幕档宽（= 艺术画布宽 × pixelScale）
float _PixelartFineHeight;   // 屏幕档高
float _PixelartRTWidth;      // 艺术画布宽（Result 的目标尺寸）
float _PixelartRTHeight;     // 艺术画布高
float _PixelartSamplingScale; // = pixelScale（一个艺术像素占几个细像素）
float _PixelartFineUnitSize;  // 1 个细像素的世界尺寸（米）

// ---------------- 判据参数（compute 私有 uniform，v3 `ShaderPropertyStorage.cs:22` 同名）----------------

/// 平面预测残差阈值（米）。v3 的 `m_Threshold = 0.25`（蓝图 §3 开头）。
float _Threshold;

// ---------------- ① 视图空间法线 ----------------

/// 世界空间法线 → 视图空间（v3 `:78`/`:89` 的同一步）。空像素 (0,0,0) 变换后仍是 (0,0,0)。
float3 PixelartConnectivityViewNormal(int2 coord)
{
    return TransformWorldToViewDir(_PixelartNormal0Buffer[coord].xyz);
}

// ---------------- ② 深度附件 → 眼睛米数 ----------------

/// 原始深度 → 眼睛米数（"closer"与"残差阈值"都以它为准）。
/// 正交（本仓 Cast 相机）走 URP 的 `LinearDepthToEyeDepth`（自带 reversed-Z 分支）；
/// 透视落到 core 的 `LinearEyeDepth`（`_ZBufferParams` 自带 reversed-Z）。见头注 ②。
float PixelartConnectivityEyeDepth(float rawDepth)
{
    return (unity_OrthoParams.w == 0.0)
        ? LinearEyeDepth(rawDepth, _ZBufferParams)
        : LinearDepthToEyeDepth(rawDepth);
}

// ---------------- ④ 位打包（直存 UNorm8，见头注 ④）----------------

/// 0..255 的位字节 → 可写进 ARGB32 通道的 0..1 值（硬件写入时舍入，取回逐位精确）。
float PixelartConnectivityPackByte(uint bits)
{
    return float(min(bits, 255u)) / 255.0;
}

/// 从 ARGB32 通道取回位字节。
uint PixelartConnectivityUnpackByte(float channel01)
{
    return (uint)round(saturate(channel01) * 255.0);
}

// ---------------- ③ 视图空间步长（见头注 ③）----------------

/// 索引步（细像素）→ 视图空间 XY 位移（米）。正交下与深度无关，就是一个常数乘步数。
/// **平台符号的唯一落点**：若画面出现"沿某一轴向的内线伪影/遗漏"，把 y 的符号翻过来。
float2 PixelartConnectivityViewStep(int2 pixelStep)
{
    return float2(pixelStep) * _PixelartFineUnitSize;
}

// ---------------- 判据本体（v3 `:42-104`）----------------

/// 单元内最大法线差（v3 `CheckNormalContinuous`，`:7-40`）。
/// `coord` 必须在界内（调用方负责），于是四个邻居的边界判断与 v3 一致、不会越界读。
/// 与 v3 的唯一差别：`.z < 0` 用**视图**法线判（头注 ⑥）。
float PixelartConnectivityNormalContinuous(int2 coord, int2 size)
{
    float maxNormalDiff = 0.0;

    int2 up = coord + int2(0, 1);
    int2 down = coord + int2(0, -1);
    int2 left = coord + int2(-1, 0);
    int2 right = coord + int2(1, 0);

    if (up.y < size.y && down.y >= 0)
    {
        float3 upNormal = PixelartConnectivityViewNormal(up);
        float3 downNormal = PixelartConnectivityViewNormal(down);

        if (upNormal.z < 0.0 && downNormal.z < 0.0)
            maxNormalDiff = max(maxNormalDiff, length(downNormal - upNormal));
    }

    if (right.x < size.x && left.x >= 0)
    {
        float3 leftNormal = PixelartConnectivityViewNormal(left);
        float3 rightNormal = PixelartConnectivityViewNormal(right);

        if (leftNormal.z < 0.0 && rightNormal.z < 0.0)
            maxNormalDiff = max(maxNormalDiff, length(rightNormal - leftNormal));
    }

    return maxNormalDiff;
}

/// 「两端法线各自预测深度、比残差」（v3 `CheckConnectedDepthNormal`，`:42-104`）。
///
/// `step` = 起点到目标之间跨几个细像素：Check 段传 1（相邻像素），
/// Result 段传 `pixelScale`（跨到相邻艺术像素的单元中心，中间 k 个细像素逐步走一遍）。
/// 只有 `step` 个中间点**全部**通过才判连通；任一步不通过立即返回（`connected = 0`）。
///
/// `size` 是本判据要用的缓冲尺寸（两者都在屏幕档）；v3 用宏 `BUFFER_WIDTH/BUFFER_HEIGHT`，
/// 本仓改成显式入参，免得同一份头文件被两个不同尺寸域引用时靠宏切换。
void PixelartConnectivityCheckConnectedDepthNormal(int2 center, int2 target, int2 size, int step,
                                                   out uint connected, out uint closer)
{
    // 出界一律当"连通 / 更近"：屏幕边缘不该长出一条假内线（v3 `:44-49`）。
    if (target.x >= size.x || target.y >= size.y || target.x < 0 || target.y < 0)
    {
        connected = 1u;
        closer = 1u;
        return;
    }

    float centerDepth = PixelartConnectivityEyeDepth(_PixelartDepthBuffer[center].r);
    float targetDepth = PixelartConnectivityEyeDepth(_PixelartDepthBuffer[target].r);

    // closer 只看两端（与 v3 相同：循环只细化 connected，不细化 closer）。
    closer = centerDepth <= targetDepth ? 1u : 0u;
    connected = 1u;

    // v3 的 `prev` 恒为 center（循环里从不推进）⇒ 中心那两个量是循环不变量，提到循环外（头注 ⑤）。
    int2 direction = target - center;
    float3 normalCenter = PixelartConnectivityViewNormal(center);

    for (int i = 1; i <= step; i++)
    {
        // 整数步进：direction 只有一个分量非零且是 step 的整数倍（Check 是 ±1，Result 是 ±k），
        // 所以 `direction * i / step` 是精确的中间像素偏移。
        int2 next = direction * i / step + center;

        float3 normalTarget = PixelartConnectivityViewNormal(next);

        // 视图空间像素间距：中心 → 第 i 个中间像素（v3 取 `centerPos.xy - targetPos.xy`，
        // 正交下等于本式，见头注 ③）。
        float2 pixelSpacing = -PixelartConnectivityViewStep(next - center);

        // 用中心法线预测：把中心的切平面外推到 `next` 的视图 XY，看它预测的深度与实测差多少。
        float dz_x = -normalCenter.x * pixelSpacing.x / normalCenter.z;
        float dz_y = -normalCenter.y * pixelSpacing.y / normalCenter.z;
        float predictDepth = centerDepth + dz_x + dz_y;
        float resultCenter = abs(predictDepth - targetDepth);

        // 用 `next` 处的法线预测（v3 `:88-96`：法线取 next 的，基准仍是 centerDepth）。
        dz_x = -normalTarget.x * pixelSpacing.x / normalTarget.z;
        dz_y = -normalTarget.y * pixelSpacing.y / normalTarget.z;
        predictDepth = centerDepth + dz_x + dz_y;
        float resultTarget = abs(predictDepth - targetDepth);

        // 一侧为空（法线长度 ≈ 0 ⇒ `dot(n,n) < 0.5`）判不连通；两端都实且残差同时超阈值也判不连通。
        if (dot(normalTarget, normalTarget) < 0.5 && dot(normalCenter, normalCenter) >= 0.5)
            connected = 0u;
        else if (dot(normalTarget, normalTarget) >= 0.5 && dot(normalCenter, normalCenter) < 0.5)
            connected = 0u;
        else if (resultCenter > _Threshold && resultTarget > _Threshold)
            connected = 0u;

        if (connected == 0u)
            return;
    }
}

#endif // PIXELART_CONNECTIVITY_INCLUDED
