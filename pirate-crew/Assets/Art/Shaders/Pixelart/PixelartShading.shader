// ============================================================================
// PixelartShading.shader —— 像素化着色路径的**低分辨率域着色**（艺术画布，四趟 + 合成）
//
// 【四个 pass，名字是契约】（`docs/技术/渲染/像素化着色路径-P4P5接口契约.md` §2.1）：
//   PixelartDiffuse   读屏幕档 G-buffer + 深度 → 主光/附加光漫反射（含连通域降档与法线边加成）
//   PixelartSpecular  主光/附加光高光（`multiStep(pow(NdotH, expSmoothness))`）
//   PixelartGI        本仓简化的环境光项（`环境色 × albedo`）——**暗部色就是它**
//   PixelartCombine   `Diffuse + Specular + GI + RimLight` → Cast 相机的颜色目标（= ResultBuffer）
// 四张缓冲由 rig 分配、由 `PixelartShadingFeature` 按名字找 pass 并驱动（**本 shader 不自己清屏**：
// 三张结果缓冲的清屏在 C# 驱动的 `SetRenderTarget(3 MRT) + ClearRenderTarget` 里做了）。
//
// 【为什么是四趟而不是一趟】分开的唯一理由是**门控要跨趟**：漫反射要读**边缘光缓冲**
//   （`_PixelartRimLightBuffer`）来决定"这个像素要不要关掉连通域降档"（`applyAA`），而边缘光
//   由 `PixelartRimLightFeature` 先跑完；合成又必须等四张都算完。一趟里做不完这件事。
//
// 【着色数学 = v3 的 DiffuseShading】（`$P/Shaders/Includes/Passes/ShadingPass.hlsl:37-63`，逐行翻译）：
//     ndotl = saturate(dot(L, N) * 距离衰减 * 阴影衰减)
//     if (level > 0) {
//         ndotl = pow(ndotl, 1/2.2)                     // 色带边界落在感知均匀的位置
//         ndotl += singleLevel * offset                  // 抖动
//         ndotl = multiStep(ndotl, level, 0, 0)          // 量化成 level 档（"色带"本体）
//         if (ndotl > singleLevel && connect < 阈值) ndotl -= applyAA * singleLevel   // 连通域降档 = 内线
//         if (normalDiff > 阈值)                     ndotl += singleLevel * 边加成
//         ndotl = clamp(ndotl, 0, 1 + singleLevel)
//         ndotl = lerp(transmission, 1, ndotl)
//     }
//     color = ndotl * light.color
//
// 【与 v3 的三处刻意差异】（契约 §6，都写在注释里免得被后来者"改回 v3"）：
//   ① **缓冲直存**：v3 用 `SNorm16` + `PackFloatInt8bit` 把档数/抖动塞进字节，所以它到处
//      `* 255.0`、`* 2.0 - 1.0`、`* 128.0` 地解码。本仓 G-buffer 是 16 位浮点**直存**
//      （`PixelartObject.shader` 写的就是 `_MainLightLevel` / `_NormalEdgeLevel` 的原值），
//      故这里**一个解码都不做**——照抄 v3 的 `* 255.0` 会让档数变成 765 档。
//   ② **applyAA 的三个通道都判**：v3 `:93` 写的是 `r > 0 || g > 0 || g > 0`（**g 判两次、b 一次没判**），
//      于是纯蓝边缘光不会抑制降档（蓝图 §4.4 第 2 条）。本仓判 r/g/b 三通道（见 DiffuseFragment 注）。
//   ③ **不碰 `_ScreenParams`**（契约 §2.2）：一切尺寸走 `_PixelartRTWidth/Height` 与
//      `_PixelartFineWidth/Height`；本文件其实只需要**用 uv 点采样**各缓冲，不直接用尺寸量。
//
// 【positionWS 怎么来】阴影坐标与附加光都要它，而 G-buffer 里只有深度。本仓的深度重建口径
//   **照 URP 自己的屏幕空间阴影 pass**（`Shaders/Utils/ScreenSpaceShadows.shader:23-32`）：
//   深度附件采样 `.r` →（非 reversed-Z 平台再 `*2-1`）→ `ComputeWorldSpacePosition(uv, 深度, UNITY_MATRIX_I_VP)`。
//   这是**正交与透视都精确**的做法（纯矩阵求逆），且不依赖 `_ZBufferParams` / `unity_OrthoParams.xy`
//   的口径。不要改成 `LinearEyeDepth(raw, _ZBufferParams)`：那条是**透视专用**公式（正交下
//   视图深度是线性的，URP 自己给正交用的是 `LinearDepthToEyeDepth`，见 `Particles.hlsl:96`），
//   而且它吐的是**视图空间距离**、不能直接喂 `I_VP`（两者不是同一套空间）。
// ============================================================================

Shader "PirateCrew/Pixelart/PixelartShading"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        // ------------------------------------------------------------------
        // 四趟共用的包含、全局量、辅助函数与着色数学。
        // 【放 HLSLINCLUDE】同文件多 pass 共用的唯一手段；每段 HLSLPROGRAM 都会把它前置。
        // ------------------------------------------------------------------
        HLSLINCLUDE
        #pragma target 4.5

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        // 【这一行不能省：include 链必须自带，不能靠"符号在别处"】URP 的 `Shadows.hlsl` 在软阴影分支里
        // 用了 core 的 `LerpWhiteTo`，而它自己**没有** include 定义它的 `CommonMaterial.hlsl`。
        // 出包时（d3d11 目标）报的是 `undeclared identifier 'LerpWhiteTo' at .../Shadows.hlsl(298)`，
        // 而编辑器里不报——**静态核对"符号存在"证明不了 include 链自洽**，这正是本仓记过的事故类
        // （见 docs/技术/渲染/描边Shader调试.md §八：`GlobalIllumination.hlsl` 用 `BRDFData` 却不 include
        // `BRDF.hlsl`，同一个形状）。所以这里显式补齐，顺序放在 Shadows.hlsl 之前。
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
        // 主光/附加光的 GetMainLight / GetAdditionalPerObjectLight / LIGHT_LOOP_BEGIN 都在这里
        //（RealtimeLights.hlsl 自己会 include Shadows.hlsl、Clustering.hlsl、LightCookie.hlsl）。
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RealtimeLights.hlsl"
        // 全屏三角形与 `input.texcoord` 的口径（驱动只 SetGlobalVector(_BlitScaleBias)，不设 _BlitTexture）。
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        // ---------------- 读的缓冲（全部由 rig 分配、由各自的 Feature 设成 shader 全局）----------------
        // 屏幕档（FineW×FineH；按 uv 点采样取到的是"块中心那个纹素"= 隐式降采，契约 §0）
        TEXTURE2D(_PixelartAlbedoBuffer);       SAMPLER(sampler_PixelartAlbedoBuffer);
        TEXTURE2D(_PixelartNormal1Buffer);      SAMPLER(sampler_PixelartNormal1Buffer);
        TEXTURE2D(_PixelartPhysicalBuffer);     SAMPLER(sampler_PixelartPhysicalBuffer);
        TEXTURE2D(_PixelartShapeBuffer);        SAMPLER(sampler_PixelartShapeBuffer);
        TEXTURE2D(_PixelartPaletteBuffer);      SAMPLER(sampler_PixelartPaletteBuffer);
        TEXTURE2D(_PixelartDepthBuffer);        SAMPLER(sampler_PixelartDepthBuffer);
        // 艺术画布（ArtW×ArtH）
        TEXTURE2D(_PixelartConnectivityResultBuffer); SAMPLER(sampler_PixelartConnectivityResultBuffer);
        TEXTURE2D(_PixelartOutlineBuffer);      SAMPLER(sampler_PixelartOutlineBuffer);
        TEXTURE2D(_PixelartRimLightBuffer);     SAMPLER(sampler_PixelartRimLightBuffer);
        TEXTURE2D(_PixelartDiffuseBuffer);      SAMPLER(sampler_PixelartDiffuseBuffer);
        TEXTURE2D(_PixelartSpecularBuffer);     SAMPLER(sampler_PixelartSpecularBuffer);
        TEXTURE2D(_PixelartGIBuffer);           SAMPLER(sampler_PixelartGIBuffer);

        // ---------------- 全局量 ----------------
        // 连通域降档阈值（= aaScaler ÷ 2，rig.aaScaler 默认 1.5 ⇒ 0.75；契约 §2.2 的 AAThresholdId）。
        float _PixelartAAThreshold;
        // 附加光条数（由 PixelartBeforeRenderFeature 每帧 SetGlobalFloat 下发）。
        // 【为什么声明成 float 而不是 uint】C# 侧走的是 SetGlobalFloat；同名 uniform 用 uint 声明时
        // 常量缓冲的按位解释会让 3.0f 读成 1078530048（静默的巨值），循环上界直接炸。
        float _PixelartAdditionalLightCount;
        // 墨线色（线性；rig 已 .linear 下发）。墨线像素由本趟直接输出它，**不吃光照/色带/环境光**。
        float4 _PixelartInkColor;
        // 环境光色（线性）。本仓的 GI 简化为 `环境色 × albedo`（契约 §4.2、蓝图 §7.2 第 5 条）。
        float4 _PixelartAmbientColor;
        // 主光方向（**指向光源**）与颜色（线性）——**本路径的光以这两个全局量为准**。
        // 【为什么不能让 URP 的 `_MainLightColor`/`_MainLightPosition` 说了算】那两值由场景里那盏主光的
        // 强度与朝向决定，而本路径的口径是"主光由 rig 每帧显式下发"（渲染篇 §4.6；判据脚本算色号期望值、
        // 以及 `PixelartCameraRig.lightIntensity` 这个美术旋钮都依赖它）。Diffuse/Specular 两趟在
        // `GetMainLight()` 之后用它们覆盖（只借 URP 的 shadowAttenuation）。
        float4 _PixelartLightDirWS;
        float4 _PixelartLightColor;
        // 调试档（0 = 正常出图；1..5 见 PixelartDiffuseFragment 里的表）。
        float _PixelartDebugMode;

        // ------------------------------------------------------------------
        // multiStep —— v3 `Math/Step.hlsl` 的逐行翻译（函数本体只有 12 行，不要"优化"它：
        // 档位断点就是靠 floor(value*level + offset) + 第 0 档的最小占比拼出来的）。
        // ------------------------------------------------------------------
        float MultiStep(float value, float level, float minValue, float offset)
        {
            if (level <= 1.0)
                return 1.0;

            float curLevel = value * level;
            curLevel = floor(curLevel + offset);

            float curOffset = curLevel / (level - 1.0);
            curLevel += lerp(minValue, 1.0, curOffset);
            curLevel = curLevel / level;

            return saturate(curLevel);
        }

        // ------------------------------------------------------------------
        // 深度 → 世界坐标。口径照 URP `ScreenSpaceShadows.shader:23-32`（见文件头）。
        // ------------------------------------------------------------------
        float3 PixelartPositionWSFromDepth(float2 uv)
        {
            float deviceDepth = SAMPLE_TEXTURE2D(_PixelartDepthBuffer, sampler_PixelartDepthBuffer, uv).r;
            // 非 reversed-Z 平台（OpenGL/GLES）里深度附件存的是 NDC 的 [0,1] 重映射，
            // `ComputeClipSpacePosition` 要的是 clip z ⇒ 还原到 [-1,1]；D3D 的 reversed-Z 下
            // 深度附件里的值**就是** clip z（近 = 1），直接用。
            #if !UNITY_REVERSED_Z
                deviceDepth = deviceDepth * 2.0 - 1.0;
            #endif
            return ComputeWorldSpacePosition(uv, deviceDepth, UNITY_MATRIX_I_VP);
        }

        // 阴影坐标。`GetShadowCoord` 在非 `_MAIN_LIGHT_SHADOWS_SCREEN` 模式下只用 positionWS，
        // 但屏幕空间阴影那条路要用 positionCS ⇒ 两者都喂（v3 `ShadingPass.hlsl:85-88` 同一手法）。
        float4 PixelartShadowCoord(float3 positionWS)
        {
            VertexPositionInputs vertexInput = (VertexPositionInputs)0;
            vertexInput.positionWS = positionWS;
            vertexInput.positionCS = mul(UNITY_MATRIX_VP, float4(positionWS, 1.0));
            return GetShadowCoord(vertexInput);
        }

        // 指向观察者的方向（Blinn-Phong 半角向量要的是它，不是"视线前方"）。
        // `UNITY_MATRIX_I_V` 的第 3 列 = 视图空间 +Z 轴的世界像 = 从场景指向相机。
        // 【为什么不用 GetWorldSpaceViewDir(positionWS)】本路径的 Cast 相机是**正交**的，
        // 逐像素的 `normalize(camPos - positionWS)` 会收敛到错误方向；正交下观察方向对整幅图
        // 是同一个常量 = 这个轴（v3 `ShadingPass.hlsl:112-115` 同口径）。
        float3 PixelartViewDirWS()
        {
            return float3(UNITY_MATRIX_I_V._m02, UNITY_MATRIX_I_V._m12, UNITY_MATRIX_I_V._m22);
        }

        // 本像素是不是墨线（描边 Feature 写 `_PixelartOutlineBuffer.r = 1`；契约 §1.2/§4.4）。
        float PixelartIsInk(float2 uv)
        {
            return SAMPLE_TEXTURE2D(_PixelartOutlineBuffer, sampler_PixelartOutlineBuffer, uv).r > 0.5 ? 1.0 : 0.0;
        }

        // ------------------------------------------------------------------
        // DiffuseShading —— v3 `ShadingPass.hlsl:37-63` 全文（参数含义逐条对齐）。
        //   connect / normalDiff         连通域结论（`_PixelartConnectivityResultBuffer` 的 .g / .b）
        //   normalEdgeThreshold / Level  逐物体（Shape.b / Palette.b）——本仓直存，不解码
        //   level                        主光档数（Palette.r，1..8；1 = 不量化）
        //   offset                       抖动偏移（Palette.g*2-1）
        //   transmission                 透射项（本仓恒 0.0 ⇒ lerp(0,1,ndotl) == ndotl，保留是为了不改数学）
        //   applyAA                      是否允许"连通域降档"生效（边缘光命中或逐物体缩放到 0 时为 0）
        // ------------------------------------------------------------------
        float3 DiffuseShading(Light light, float3 normal, float connect, float normalDiff,
                              float normalEdgeThreshold, float normalEdgeLevel, float level,
                              float offset, float transmission, float applyAA)
        {
            float ndotl = dot(light.direction, normal);
            ndotl *= light.distanceAttenuation * light.shadowAttenuation;
            ndotl = saturate(ndotl);

            if (level > 0.0)
            {
                float singleLevel = 1.0 / (level - 1.0);

                ndotl = pow(ndotl, 1.0 / 2.2);
                ndotl = saturate(ndotl);
                ndotl += singleLevel * offset;

                ndotl = MultiStep(ndotl, level, 0.0, 0.0);

                // 连通域降档 = **内线**（面转折处降一档，观感上是一条内描边）。
                // v3 `:53` 原样：只降"已经跨过第 0 档"的像素，且该像素不属于连通域（`connect` 小）。
                if (ndotl > singleLevel && connect < _PixelartAAThreshold)
                    ndotl -= applyAA * singleLevel;

                // 法线边加成：法线差超过阈值的像素**提亮**（与外轮廓墨线正交的另一条结构线）。
                if (normalDiff > normalEdgeThreshold)
                    ndotl += singleLevel * normalEdgeLevel;

                ndotl = clamp(ndotl, 0.0, 1.0 + singleLevel);

                ndotl = lerp(transmission, 1.0, ndotl);
            }

            return ndotl * light.color;
        }

        // ------------------------------------------------------------------
        // 调试档输出（全部在 Diffuse 那趟分派；契约与出图脚本共用这张表）。
        //   0 正常 / 1 albedo / 2 Normal1 / 3 Palette 逐物体参数 / 4 墨线标记 / 5 连通域结论
        // 【为什么"没几何的像素一律洋红"】这条路径的故障大多是"没进 G-buffer"或
        // "进了但着色丢了"；把两者画成同一个黑就分不出来，洋红一眼可辨。
        // 【唯一的例外是调试档 4 里的墨线像素】墨线**合法地落在背景像素上**（描边的外圈就画在那儿，
        // 见 `PixelartOutline.shader` 文件头「不许把'本像素是背景'当成拦路条件」）——若先判洋红，
        // 整圈外轮廓会显示成洋红、看着像"没描边"。故档 4 的次序是：**墨线黑 → 没几何洋红 → 其余白**，
        // 三态各自可分（保留"没进 G-buffer"这个诊断力，同时墨线可见）。
        // ------------------------------------------------------------------
        half4 PixelartDebugOutput(float2 uv, float coverage)
        {
            if (_PixelartDebugMode > 3.5 && _PixelartDebugMode < 4.5)
            {
                if (PixelartIsInk(uv) > 0.5)
                    return half4(0.0, 0.0, 0.0, 1.0);
                if (coverage < 0.5)
                    return half4(1.0, 0.0, 1.0, 1.0);
                return half4(1.0, 1.0, 1.0, 1.0);
            }

            if (coverage < 0.5)
                return half4(1.0, 0.0, 1.0, 1.0);

            if (_PixelartDebugMode < 1.5)
                return half4(SAMPLE_TEXTURE2D(_PixelartAlbedoBuffer, sampler_PixelartAlbedoBuffer, uv).rgb, 1.0);

            if (_PixelartDebugMode < 2.5)
            {
                float3 n = SAMPLE_TEXTURE2D(_PixelartNormal1Buffer, sampler_PixelartNormal1Buffer, uv).xyz;
                return half4(n * 0.5 + 0.5, 1.0);
            }

            if (_PixelartDebugMode < 3.5)
            {
                float4 p = SAMPLE_TEXTURE2D(_PixelartPaletteBuffer, sampler_PixelartPaletteBuffer, uv);
                return half4(p.r / 8.0, p.g, p.b, 1.0);
            }

            // 5 = 连通域结论：r = 连通比例、b = 单元内最大法线差。
            float4 connectInfo = SAMPLE_TEXTURE2D(_PixelartConnectivityResultBuffer,
                sampler_PixelartConnectivityResultBuffer, uv);
            return half4(connectInfo.r, 0.0, connectInfo.b, 1.0);
        }
        ENDHLSL

        // ==================================================================
        // 第 1 趟 / PixelartDiffuse：主光 + 附加光漫反射（内线与法线边加成都在这一趟）
        //   输入全局：Albedo / Normal1 / Physical / Shape / Palette / Depth /
        //            ConnectivityResult / Outline / RimLight(+rimLight 缓冲的读法见下)
        //   输出：diffuse = rgb 漫反射，**a 恒 1**（合成那趟用 `clip(a-1)` 区分"没着色"的背景）
        //   墨线像素：直接输出 `_PixelartInkColor`（不吃光照/色带/环境光）
        // ==================================================================
        Pass
        {
            Name "PixelartDiffuse"

            ZTest Always
            ZWrite Off
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment PixelartDiffuseFragment

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            // 【两个附加光关键字都声明】URP 资产上"附加光"是三态（0=关 / 1=逐顶点 / 2=逐像素），
            // 逐顶点那条给的是 `_ADDITIONAL_LIGHTS_VERTEX`。装配器（`PixelartPathInstaller`
            // 的 `EnsureUrpLightingSettings`）会把两档 URP 资产写成 **2 = 逐像素**，但那一步没跑成、
            // 或别人手改回逐顶点时，只声明 `_ADDITIONAL_LIGHTS` 会让本趟**静默丢掉全部附加光**
            // （亮度变化、没有任何报错）。故两个都声明，任一命中都走同一段逐像素循环——
            // 逐顶点模式下 `_AdditionalLightsPosition` 是空的，读到的方向为 0 ⇒ 贡献为 0，
            // 也就是"没有附加光"而不是"脏数据"。
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            half4 PixelartDiffuseFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                half4 albedo = SAMPLE_TEXTURE2D(_PixelartAlbedoBuffer, sampler_PixelartAlbedoBuffer, uv);

                if (_PixelartDebugMode > 0.5)
                    return PixelartDebugOutput(uv, albedo.a);

                // ------------------------------------------------------------------
                // 【次序铁律：墨线判据必须在 `clip(覆盖)` 之前】
                // 墨线**合法地落在背景像素上**：物体 vs 背景那条边的墨线画在更远的背景一侧
                //（`PixelartOutline.shader` 文件头「物体 vs 背景：背景像素在更远一侧 ⇒ 墨线落在背景上」
                // ＋「不许把本像素是背景当成拦路条件」）。若先 `clip(albedo.a - 0.5)`，
                // 整圈外轮廓会被整个丢弃（只剩物体与物体之间的内线），而画面看起来只是"描边没了"。
                // ------------------------------------------------------------------
                if (PixelartIsInk(uv) > 0.5)
                    return half4(_PixelartInkColor.rgb, 1.0);

                // 背景像素丢掉：目标已被清成透明黑（a=0），合成的 `clip(diffuse.a - 1)` 会把
                // 这些像素留给 Cast 相机颜色目标上的场景背景色。
                clip(albedo.a - 0.5);

                float3 normalWS = normalize(SAMPLE_TEXTURE2D(_PixelartNormal1Buffer,
                    sampler_PixelartNormal1Buffer, uv).xyz);
                float4 physicalProp = SAMPLE_TEXTURE2D(_PixelartPhysicalBuffer, sampler_PixelartPhysicalBuffer, uv);
                float4 shapeProp    = SAMPLE_TEXTURE2D(_PixelartShapeBuffer, sampler_PixelartShapeBuffer, uv);
                float4 paletteProp  = SAMPLE_TEXTURE2D(_PixelartPaletteBuffer, sampler_PixelartPaletteBuffer, uv);
                float4 connectInfo  = SAMPLE_TEXTURE2D(_PixelartConnectivityResultBuffer,
                    sampler_PixelartConnectivityResultBuffer, uv);

                float3 positionWS  = PixelartPositionWSFromDepth(uv);
                float4 shadowCoord = PixelartShadowCoord(positionWS);

                // 逐物体参数（**直存，不解码**，见文件头差异 ①）
                float mainLightLevel      = paletteProp.r;              // 主光档数（1 = 不量化）
                float ditherOffset        = paletteProp.g * 2.0 - 1.0;  // 0..1（中心 0.5）→ -1..+1
                float normalEdgeLevel     = paletteProp.b;              // 法线边加成档
                float normalEdgeThreshold = shapeProp.b;                // 法线边阈值
                float metallic            = physicalProp.g;

                // ------------------------------------------------------------------
                // applyAA = 「本像素还没有边缘光」× 逐物体 AA 缩放。
                // 【这里就是 v3 `:93` 那个 bug 的修法】v3 写的是
                //     `if(rimLightInfo.r > 0 || rimLightInfo.g > 0 || rimLightInfo.g > 0) applyAA = 0;`
                // ——**g 判两次、b 一次没判**（蓝图 §4.4 第 2 条），纯蓝边缘光不会抑制降档。
                // 本仓判 r/g/b 三通道：任一非零（= 有边缘光）⇒ applyAA = 0；
                // 三通道全为零（= 没边缘光）才让它等于 1，再乘逐物体的 `shape.a`（`_AAScale`）。
                // 语义：边缘光已经标出"这里是光照层的边缘"，此时再按连通域降一档会双重描边。
                // 【时序前提】边缘光那一趟必须**早于**本趟（契约 §3 第 5 项 < 第 6 项），否则
                // rimLightInfo 恒为（上一帧的）零 ⇒ 降档永远生效，观感上"边缘光压不住内线"。
                // ------------------------------------------------------------------
                float3 rimLightInfo = SAMPLE_TEXTURE2D(_PixelartRimLightBuffer,
                    sampler_PixelartRimLightBuffer, uv).rgb;
                float applyAA = (rimLightInfo.r <= 0.0 && rimLightInfo.g <= 0.0 && rimLightInfo.b <= 0.0) ? 1.0 : 0.0;
                applyAA *= shapeProp.a;

                float3 outputColor = float3(0.0, 0.0, 0.0);

                // ---- 主光（URP 现实的主光，阴影由 shadowCoord 采样）----
                Light mainLight = GetMainLight(shadowCoord);

                // 【为什么把主光的 distanceAttenuation 强制成 1（**故意与 v3 不同的一行**）】
                // URP 的 `GetMainLight()` 把 `distanceAttenuation` 取成 `unity_LightData.z`——那个值在
                // URP 里的语义是"**主光对这个物体**可见吗"（逐物体光源剔除的结果，0 或 1），
                // 而不是真的距离衰减（平行光没有距离衰减）。
                // 本趟是 `DrawProcedural` 的全屏三角形：**没有逐物体光源剔除数据**，`unity_LightData`
                // 在这个绘制里是未定义的（可能是引擎默认值 0，也可能是上一条绘制留下的值）。
                // 若是 0 ⇒ `ndotl *= 0` ⇒ **主光对着色完全没有贡献**，画面只剩环境光与边缘光，
                // 而没有任何报错、也没有编译问题——正是本仓「静默失效点」那一类。
                // 对全屏像素而言主光当然可见，故这里显式置 1（颜色/方向/阴影都不受影响；
                // 主光真被关掉时 `_MainLightColor` 本身是 0，本行不会凭空加亮）。
                // 【要严格照 v3 的话】删掉这一行即可；但那样必须先确认 `unity_LightData.z` 在本路径的
                // blit 上下文里确实是 1（只能靠出图验证）。
                mainLight.distanceAttenuation = 1.0;

                // 【本路径的光以**我们自己的全局量**为准】URP 的 `_MainLightColor`/`_MainLightPosition`
                // 由场景里那盏主光的强度与朝向决定，而本路径的口径是"主光方向/颜色由 rig 每帧显式下发"
                // （渲染篇 §4.6；本文件头的色号期望式也按那组量算）。不覆盖会有两处**静默**损失：
                // `PixelartCameraRig.lightIntensity` 这个美术旋钮失效、以及"下发值"日志不再能用来对色号。
                // 方向两边的口径一致（都**指向光源**），故可直接赋值；为 0 时保留 URP 的值（防 normalize(0)=NaN）。
                if (dot(_PixelartLightDirWS.xyz, _PixelartLightDirWS.xyz) > 1e-8)
                    mainLight.direction = normalize(_PixelartLightDirWS.xyz);
                mainLight.color = _PixelartLightColor.rgb;

                outputColor += DiffuseShading(mainLight, normalWS, connectInfo.g, connectInfo.b,
                    normalEdgeThreshold, normalEdgeLevel, mainLightLevel, ditherOffset, 0.0, applyAA);

                // ---- 附加光（v3 `:101-104`：档数 `level*0.5`、偏移 3.0）----
                // 【写法说明】v3 用 `LIGHT_LOOP_BEGIN(_PixelartAdditionalLightCount)`。URP 14 的这条宏
                // 在 **Forward+** 下展开成簇式遍历（`ClusterInit(inputData...)`），而全屏 blit 没有
                // `InputData` ⇒ 那种写法在 Forward+ 下**编不过**。本仓的 Cast 渲染器是 **Forward**
                //（`PixelartCast_Renderer.asset` 的 `m_RenderingMode: 0`），此时宏就是普通 for 循环，
                // 两条分支结果一致；故这里显式分写成两支：Forward 走宏（与 v3 逐字一致），
                // Forward+ 走"平铺枚举"（试点场景附加光很少，与簇内枚举等价）。
                #if defined(_ADDITIONAL_LIGHTS) || defined(_ADDITIONAL_LIGHTS_VERTEX)
                    #if !USE_FORWARD_PLUS
                        LIGHT_LOOP_BEGIN((uint)_PixelartAdditionalLightCount)
                            Light light = GetAdditionalPerObjectLight(lightIndex, positionWS);
                            outputColor += DiffuseShading(light, normalWS, connectInfo.g, connectInfo.b,
                                normalEdgeThreshold, normalEdgeLevel * 0.5, 3.0, ditherOffset, 0.0, applyAA);
                        LIGHT_LOOP_END
                    #else
                        for (uint lightIndex = 0u; lightIndex < (uint)_PixelartAdditionalLightCount; ++lightIndex)
                        {
                            Light light = GetAdditionalPerObjectLight(lightIndex, positionWS);
                            outputColor += DiffuseShading(light, normalWS, connectInfo.g, connectInfo.b,
                                normalEdgeThreshold, normalEdgeLevel * 0.5, 3.0, ditherOffset, 0.0, applyAA);
                        }
                    #endif
                #endif

                outputColor *= albedo.rgb;
                // 金属的漫反射衰减（v3 `:107` 同式）。本仓逐物体 `_Metallic` 默认 0 ⇒ 恒等。
                outputColor *= (1.0 - metallic);

                return half4(outputColor, 1.0);
            }
            ENDHLSL
        }

        // ==================================================================
        // 第 2 趟 / PixelartSpecular：主光 + 附加光的高光累加
        //   输入全局：Albedo / Normal1 / Physical / Depth
        //   输出：specular = rgb，a 恒 1
        //   墨线像素 / 背景像素：写 0（墨线不吃光照；背景丢给合成那趟）
        // ==================================================================
        Pass
        {
            Name "PixelartSpecular"

            ZTest Always
            ZWrite Off
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment PixelartSpecularFragment

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            // v3 `:117-126` 逐行：半角向量 → `pow(NdotH, expSmoothness)` → 量化（level 恒 2.0）→ 乘光色/高光色/光滑度。
            half3 SpecularShading(Light light, float3 normal, float3 viewDir, float3 specular,
                                  float smoothness, float expSmoothness, float level)
            {
                float3 halfVec = SafeNormalize(float3(light.direction) + float3(viewDir));

                half NdotH = half(saturate(dot(normal, halfVec)));
                float modifier = pow(NdotH, expSmoothness) * light.distanceAttenuation * light.shadowAttenuation;
                modifier = MultiStep(modifier, level, 0.0, 0.0);

                return light.color * specular * modifier * smoothness;
            }

            half4 PixelartSpecularFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                // 调试档下本趟整趟跳过（写 0）：调试图由 Diffuse 那趟出、合成那趟转直通，
                // 三张缓冲又已被清成 0 ⇒ 这里早退省掉两次全屏采样。
                if (_PixelartDebugMode > 0.5)
                    return half4(0.0, 0.0, 0.0, 1.0);

                half4 albedo = SAMPLE_TEXTURE2D(_PixelartAlbedoBuffer, sampler_PixelartAlbedoBuffer, uv);

                // 墨线像素先判（它可能落在背景像素上，见 Diffuse 那趟的"次序铁律"），出 0。
                if (PixelartIsInk(uv) > 0.5)
                    return half4(0.0, 0.0, 0.0, 1.0);

                clip(albedo.a - 0.5);

                float3 normalWS = normalize(SAMPLE_TEXTURE2D(_PixelartNormal1Buffer,
                    sampler_PixelartNormal1Buffer, uv).xyz);
                float4 physicalProp = SAMPLE_TEXTURE2D(_PixelartPhysicalBuffer, sampler_PixelartPhysicalBuffer, uv);

                // 光滑度 / 金属度（直存）。expSmoothness 与 v3 同式：指数越大高光越小越亮。
                float smoothness    = physicalProp.r;
                float expSmoothness = exp2(5.0 * smoothness + 1.0);
                float metallic      = physicalProp.g;
                // 金属体把高光色换成 albedo，非金属体用白（v3 `:144` 同式）。
                float3 specular     = lerp(float3(1.0, 1.0, 1.0), albedo.rgb, metallic);

                float3 viewDir = PixelartViewDirWS();

                float3 positionWS  = PixelartPositionWSFromDepth(uv);
                float4 shadowCoord = PixelartShadowCoord(positionWS);

                float3 outputColor = float3(0.0, 0.0, 0.0);

                Light mainLight = GetMainLight(shadowCoord);
                // 同 Diffuse 那趟：主光的 distanceAttenuation 来自 `unity_LightData.z`，
                // 全屏 DrawProcedural 下未定义 ⇒ 强制 1（理由见 PixelartDiffuseFragment 的长注）。
                mainLight.distanceAttenuation = 1.0;
                // 同 Diffuse：主光的方向/颜色以本路径下发的全局量为准（理由见那边的长注）。
                if (dot(_PixelartLightDirWS.xyz, _PixelartLightDirWS.xyz) > 1e-8)
                    mainLight.direction = normalize(_PixelartLightDirWS.xyz);
                mainLight.color = _PixelartLightColor.rgb;
                outputColor += SpecularShading(mainLight, normalWS, viewDir, specular, smoothness, expSmoothness, 2.0);

                // 【注】附加光这里走 `GetAdditionalPerObjectLight` ⇒ `shadowAttenuation` 恒为 1，
                // 即附加光**不投影**（v3 `:160-163` 同样如此；要投影得换 `GetAdditionalLight(i, pos, shadowMask)`）。
                #if defined(_ADDITIONAL_LIGHTS) || defined(_ADDITIONAL_LIGHTS_VERTEX)
                    #if !USE_FORWARD_PLUS
                        LIGHT_LOOP_BEGIN((uint)_PixelartAdditionalLightCount)
                            Light light = GetAdditionalPerObjectLight(lightIndex, positionWS);
                            outputColor += SpecularShading(light, normalWS, viewDir, specular, smoothness, expSmoothness, 2.0);
                        LIGHT_LOOP_END
                    #else
                        for (uint lightIndex = 0u; lightIndex < (uint)_PixelartAdditionalLightCount; ++lightIndex)
                        {
                            Light light = GetAdditionalPerObjectLight(lightIndex, positionWS);
                            outputColor += SpecularShading(light, normalWS, viewDir, specular, smoothness, expSmoothness, 2.0);
                        }
                    #endif
                #endif

                return half4(outputColor, 1.0);
            }
            ENDHLSL
        }

        // ==================================================================
        // 第 3 趟 / PixelartGI：环境光项（本仓简化）
        //   输入全局：Albedo / AmbientColor / Outline
        //   输出：gi = rgb（= 环境色 × albedo），a 恒 1
        // 【为什么就是它】v3 这趟是 SH/lightmap + 反射探针（`GlobalIlluminationFragment`），
        //   而它的 lightmap 通道实际是空的（蓝图 §2 注意 2）⇒ 观感上近似"单色环境光乘 albedo"。
        //   本仓直接取这一项：**暗部色就是它**（暗面 = albedo × 环境色，渲染篇 §4.2 口径）。
        // ==================================================================
        Pass
        {
            Name "PixelartGI"

            ZTest Always
            ZWrite Off
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment PixelartGIFragment

            half4 PixelartGIFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                if (_PixelartDebugMode > 0.5)
                    return half4(0.0, 0.0, 0.0, 1.0);

                half4 albedo = SAMPLE_TEXTURE2D(_PixelartAlbedoBuffer, sampler_PixelartAlbedoBuffer, uv);

                // 墨线不吃环境光（否则深墨色被环境色染出偏色，就不是墨线了）。
                // 次序同 Diffuse：墨线像素可能落在背景上，先判墨线再 clip。
                if (PixelartIsInk(uv) > 0.5)
                    return half4(0.0, 0.0, 0.0, 1.0);

                clip(albedo.a - 0.5);

                return half4(_PixelartAmbientColor.rgb * albedo.rgb, 1.0);
            }
            ENDHLSL
        }

        // ==================================================================
        // 第 4 趟 / PixelartCombine：四项相加 → Cast 相机的颜色目标（= ResultBuffer）
        //   输入全局：Diffuse / Specular / GI / RimLight（+ 调试档）
        //   输出：目标 = 相机颜色目标（由 C# 驱动 SetRenderTarget 指定）
        //   `clip(diffuse.a - 1)`：Diffuse 那趟没着色的像素（背景）在这里被丢掉，
        //   留下 Cast 相机颜色目标上的场景背景色（v3 `CombineFragment` 同式）。
        // ==================================================================
        Pass
        {
            Name "PixelartCombine"

            ZTest Always
            ZWrite Off
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment PixelartCombineFragment

            half4 PixelartCombineFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                half4 diffuse  = SAMPLE_TEXTURE2D(_PixelartDiffuseBuffer, sampler_PixelartDiffuseBuffer, uv);
                half4 specular = SAMPLE_TEXTURE2D(_PixelartSpecularBuffer, sampler_PixelartSpecularBuffer, uv);
                half4 globalIllumination = SAMPLE_TEXTURE2D(_PixelartGIBuffer, sampler_PixelartGIBuffer, uv);
                half4 rimLight = SAMPLE_TEXTURE2D(_PixelartRimLightBuffer, sampler_PixelartRimLightBuffer, uv);

                // 调试档：**直通 Diffuse 那趟的调试图**（四项相加会把调试图染色；背景像素在那里
                // 已经是洋红且 a=1，所以这条分支要放在 clip 之前）。
                if (_PixelartDebugMode > 0.5)
                    return half4(diffuse.rgb, 1.0);

                clip(diffuse.a - 1.0);

                return half4(diffuse.rgb + specular.rgb + globalIllumination.rgb + rimLight.rgb, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
