// ============================================================================
// PixelartOutline.shader —— 像素化着色路径的**屏幕空间描边**（艺术画布那一档）
//
// 【它是什么】一趟全屏（每个**艺术像素**跑一次片元）：对本像素的 4 邻域各查一次，
//   命中就往 `_PixelartOutlineBuffer.r` 写 1（墨线标记），否则写 0。**不出颜色**——
//   墨色是全局 `_PixelartInkColor`，由四趟着色见到标记后输出（契约 §4.4）。
//   本文件没有 Properties 块：本 pass 一个材质旋钮都不需要（描边开关是**逐物体**的
//   `Palette.a`，由物体材质 `_OutlinePixels` 写进 G-buffer）。
//
// 【为什么不再是反向壳几何描边】旧口径是 `Cull Front` + 沿法线外扩的几何壳。它在箱体的
//   左下/右下两条剪影边（正面墙的底边）上，外扩方向与真实边法线差约 45°，投影到艺术像素
//   只剩 cos45°≈0.7 像素 ⇒ 亚像素 ⇒ 量化后**断续（豁口）**；角部外扩重叠还会**堆块**
//   （r6 实测定位）。创始人 2026-09-22 裁决：改用艺术画布上的 4 邻域膨胀——线宽恒为
//   **1 艺术像素**、不依赖任何法线方向，必然闭合。几何侧因此不再有墨线 pass。
//
// 【判据本体 = v3 `Passes/OutlinePass.hlsl:35-77` 的逐行翻译】四个方向各一次：
//     门控（取自**本像素**的连通域结论）：`connectedToX < 1 && closerThanX < 1`
//     命中：门控通过 **且** 该方向**邻域**的 `Palette.a`（逐物体 applyOutline）不为 0
//   门控两位合起来的确切语义是「**这个方向上有一条真实的不连续边，并且本像素在更远的一侧**」
//   （`closer` 位 = 本像素比邻域近，见 v3 `Connectivity.hlsl:62`）：
//     · 物体 vs 背景：背景像素在更远一侧 ⇒ 墨线落在**背景**上 ⇒ 剪影外一圈（1 艺术像素）；
//     · 物体 vs 更近的邻物（木箱正面墙的底边压在其所在的台面上）：墨线落在**远的那一侧**，
//       也就是墙的底行 ⇒ 这条边照样有墨线（旧几何壳正是在这里断的）。
//   两条合起来才是"片子里每个物体各自闭合的一圈线"。
//
//   ⚠ 推论：**不许把"本像素是背景"当成拦路条件**。背景像素是墨线的合法落点（上面第 1 条），
//     加了它整圈外轮廓会全部消失。这也正是 v3 那趟不做 `clip(albedo.a)` 的原因。
//   ⚠ 更近那一侧的像素自己的门控不通过（`closer`=1），所以上面两圈**不会叠成 2 像素**。
//
// 【每物体独立描边怎么保证】判据只看两件逐像素的数据：邻域的 `Palette.a`（逐物体写）与
//   连通域的不连续位（逐像素算）。与"全场景一圈外轮廓"无关，也与绘制次序无关——
//   物体把材质 `_OutlinePixels` 置 0 ⇒ 它的 `Palette.a` = 0 ⇒ 它自己不描边（契约 §7 的可回退）。
//
// 【邻域偏移必须是 1 艺术像素】契约 §4.4：偏移量 = `1.0/_PixelartRTWidth`、`1.0/_PixelartRTHeight`，
//   **不是** 1 屏幕像素，也**不许**用 `_ScreenParams`（契约 §2.2；v3 是在 BeforeShadingPass 里
//   把 `_ScreenParams` 改写成艺术画布尺寸才使 `_ScreenParams.z - 1.0` 等于 1 艺术像素的）。
//   几何与 G-buffer 在**屏幕档**（艺术画布 × pixelScale），按这个 uv 偏移点采样取到的正是
//   「相邻艺术像素那一块的**块中心**纹素」：艺术坐标 i 的块中心细像素 = i*k + k/2，
//   与连通域结论 compute 取的中心点（`ConnectivityResult.compute:24`）**逐纹素一致**。
//
//   ⚠ 偏移也**不许**改成 1 屏幕像素：那样 4 邻域落在同一艺术像素的块内（k=3 时块内相邻），
//     判据变成"块内自比"，描边会退化成噪声或干脆不动。
//
// 【.a 那个打包字节怎么解】契约 §1.2：`_PixelartConnectivityResultBuffer.a` = 打包的四方向
//   connected/closer，位布局照 v3 `ConnectivityResult.compute:85`：
//     bit7 connectedRight  bit6 connectedLeft  bit5 connectedUp   bit4 connectedDown
//     bit3 closerRight     bit2 closerLeft     bit1 closerUp      bit0 closerDown
//   v3 用 `PackFloatInt8bit(0.0, bits, 256.0)` 把这一字节塞进 SNorm16；展开 core 的实现
//   （`Packing.hlsl:438-446`，maxi = precision = 256）得 t1 = 0、t2 = 1/255 ⇒ 该表达式
//   **化简后就是 bits/255**。本仓"直存"（契约 §6 第 7 条）在 ARGB32 的 .a 上写的正是
//   bits/255，与 v3 数值逐位一致；解码只需 `round(a × 255)`，**不需要** include core 的
//   Packing.hlsl（少一条 include 链 = 少一个静默失效点）。口径与边缘光那条链
//   （`Includes/RimLight.hlsl` 的 `PixelartUnpackConnectivityByte`）保持一致。
// ============================================================================

Shader "PirateCrew/Pixelart/PixelartOutline"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            // pass 名是契约 §2.1 的一行：驱动侧按名字解析索引（`Material.FindPass`），
            // 改名会让描边趟直接报错跳过，不会静默画错 pass。
            Name "PixelartOutline"
            ZWrite Off
            ZTest Always
            Blend Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex   Vert
            #pragma fragment PixelartOutlineFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // 全屏三角形与 uv 口径由官方 Blit.hlsl 提供（Vert / Varyings）。
            // 【静默失效点】它的 Vert 是 `uv * _BlitScaleBias.xy + _BlitScaleBias.zw`、
            // **没有关键字保护**：驱动侧不显式置 `_BlitScaleBias = (1,1,0,0)` 时 uv 恒为 0
            // （全屏读同一个点，画面看着"某处糊了一片"而不报错）。
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // ---- 输入：全部走 shader 全局（前几趟下发）。本 pass 没有任何材质属性。----
            // albedo：**屏幕档**，.a = 覆盖标记（1 = 这里有几何）
            TEXTURE2D(_PixelartAlbedoBuffer);             SAMPLER(sampler_PixelartAlbedoBuffer);
            // palette：**屏幕档**，.a = applyOutline（0/1，逐物体描边开关）
            TEXTURE2D(_PixelartPaletteBuffer);            SAMPLER(sampler_PixelartPaletteBuffer);
            // 连通域结论：**艺术画布**，.a = 打包的四方向 connected/closer（位序见文件头）
            TEXTURE2D(_PixelartConnectivityResultBuffer); SAMPLER(sampler_PixelartConnectivityResultBuffer);

            // 艺术画布尺寸（BeforeRender / rig 每帧下发）。邻域偏移 = 1/它 = **1 艺术像素**。
            float _PixelartRTWidth   = 1.0;
            float _PixelartRTHeight  = 1.0;
            // 契约 §2.2 的调试档全局（0 = 正常出图）。
            // **档位表是全局共享的**（集中登记在契约 §2.2）：0 正常 / 1 albedo / 2 法线 /
            // 3 逐物体参数 / 4 墨线标记 / 5 连通域结论。本 pass 的诊断档因此取 8/9 两个**高位**值，
            // 不与上面那张表撞号（曾经取 5/6，与"5 = 连通域结论"撞车——同一帧里两个 pass 对同一个
            // 全局量各有一套解释，虽然当时无害，但是迟早会咬人的那种隐患）。
            float _PixelartDebugMode = 0.0;

            // 本 pass 自己的诊断档（拆管线用，AGENTS.md 的图形学调试规范）：
            //   8 = 门控诊断：**门控命中就写 1**（不看 applyOutline）。画面全墨 ⇒ 连通域在命中；
            //       画面一点墨都没有 ⇒ 门控没命中（连通域缓冲是空的 / 打包字节与解码口径对不上），
            //       与"命中但 applyOutline=0"一眼分开——这两种故障在最终画面上长得一样。
            //   9 = 覆盖诊断：本像素或 4 邻域有覆盖就写 1（= 物体剪影外扩一圈的地图），与 8 对照即可定位。
            static const float kDebugGate     = 8.0;
            static const float kDebugCoverage = 9.0;

            /// 把 `_PixelartConnectivityResultBuffer.a` 的采样值还原成打包字节（0..255）。
            /// 【为什么是 round 而不是 floor】ARGB32 的 .a 是 8 位 UNorm，写进去的 bits/255
            /// 取回来带一点量化误差；直接乘 255 取整会在 127/255 这类值上差 1，
            /// 而一位之差就是"某个方向的门控翻面"（描边在那个方向整体消失）。
            uint DecodeConnectivityByte(float packedValue)
            {
                return (uint)round(saturate(packedValue) * 255.0);
            }

            /// 该处"要不要描边"：有几何覆盖（albedo.a）**且** 逐物体 applyOutline（palette.a）不为 0。
            /// 覆盖那一条是**对清屏残留/垃圾值的保险**——正常时 applyOutline 只可能由几何写出、
            /// 两者同真（物体 pass 的 palette.a = `_OutlinePixels > 0.5 ? 1 : 0`）。
            ///
            /// 【它对本像素与邻域用的是同一个判据】见 fragment 里那两条（本像素侧 / 邻域侧）的注释。
            bool AppliesOutlineAt(float2 uv)
            {
                float4 albedo = SAMPLE_TEXTURE2D(_PixelartAlbedoBuffer, sampler_PixelartAlbedoBuffer, uv);
                if (albedo.a < 0.5)
                    return false;

                float4 palette = SAMPLE_TEXTURE2D(_PixelartPaletteBuffer, sampler_PixelartPaletteBuffer, uv);
                return palette.a > 0.5;
            }

            /// 邻域有没有几何覆盖（调试档 6 用）。
            bool NeighborCovered(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_PixelartAlbedoBuffer, sampler_PixelartAlbedoBuffer, uv).a > 0.5;
            }

            half4 PixelartOutlineFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uvCenter = input.texcoord;
                float2 texel = float2(1.0 / max(_PixelartRTWidth, 1.0),
                                      1.0 / max(_PixelartRTHeight, 1.0));

                // 门控取自**本像素**：一次采样，八个方向位一起解出来（v3 的 GET_CONNECTIVITY）。
                uint connectCode = DecodeConnectivityByte(
                    SAMPLE_TEXTURE2D(_PixelartConnectivityResultBuffer,
                        sampler_PixelartConnectivityResultBuffer, uvCenter).a);

                // 【本像素自己是不是一个"要描边"的几何像素】
                //
                // 【为什么必须有这一条】照搬 v3 的 OutlinePass 时只检查**邻域**的 applyOutline，
                // 而门控的 `closer` 位把墨线限制在"**更远**那一侧"。两者合起来有个结构性缺口：
                // 物体**近侧**的剪影边（箱体压在更近的大平面上，例如台面下左/下右边缘贴着地面）
                // 两侧都不满足——更远那一侧是物体自己（它的邻域是大平面，applyOutline = 0），
                // 而更近那一侧过不了 closer 门控 ⇒ **整条近侧边一根线都没有**（实测墨线像素的
                // 包围盒只到画面中段：只有上半的远端边缘有线）。
                // v3 那个特征 `m_Active: 0`（从没跑过），所以这是它的代码第一次真跑时暴露的缺口。
                //
                // 补法：门控通过时，**本像素自己开着描边也算**。于是
                //   · 远侧边（外面是背景/更远的面）→ 由**外侧**像素出线，与原来的观感一致；
                //   · 近侧边（外面是更近的面）→ 由**物体自己的边界像素**出线。
                // 两种情形互斥（同一方向只有一个更远侧）⇒ 线宽仍是 **1 艺术像素**，不会叠成两像素。
                bool centerAppliesOutline = AppliesOutlineAt(uvCenter);

                int connectedToRight = (connectCode & 128u) > 0u ? 1 : 0;   // bit7
                int connectedToLeft  = (connectCode &  64u) > 0u ? 1 : 0;   // bit6
                int connectedToUp    = (connectCode &  32u) > 0u ? 1 : 0;   // bit5
                int connectedToDown  = (connectCode &  16u) > 0u ? 1 : 0;   // bit4
                int closerThanRight  = (connectCode &   8u) > 0u ? 1 : 0;   // bit3
                int closerThanLeft   = (connectCode &   4u) > 0u ? 1 : 0;   // bit2
                int closerThanUp     = (connectCode &   2u) > 0u ? 1 : 0;   // bit1
                int closerThanDown   = (connectCode &   1u) > 0u ? 1 : 0;   // bit0

                float marker  = 0.0;    // r = 墨线标记（契约 §1.2）
                float gateHit = 0.0;    // 调试档 5：门控命中的方向数

                // ---- 右（v3 `OutlinePass.hlsl:35-44`）----
                if (connectedToRight < 1 && closerThanRight < 1)
                {
                    gateHit += 1.0;
                    if (centerAppliesOutline || AppliesOutlineAt(uvCenter + float2(texel.x, 0.0)))
                        marker = 1.0;
                }

                // ---- 左（v3 `:46-55`）----
                if (connectedToLeft < 1 && closerThanLeft < 1)
                {
                    gateHit += 1.0;
                    if (centerAppliesOutline || AppliesOutlineAt(uvCenter - float2(texel.x, 0.0)))
                        marker = 1.0;
                }

                // ---- 上（v3 `:57-66`）----
                if (connectedToUp < 1 && closerThanUp < 1)
                {
                    gateHit += 1.0;
                    if (centerAppliesOutline || AppliesOutlineAt(uvCenter + float2(0.0, texel.y)))
                        marker = 1.0;
                }

                // ---- 下（v3 `:68-77`）----
                if (connectedToDown < 1 && closerThanDown < 1)
                {
                    gateHit += 1.0;
                    if (centerAppliesOutline || AppliesOutlineAt(uvCenter - float2(0.0, texel.y)))
                        marker = 1.0;
                }

                // 调试档（0 = 契约语义，别的值都是本 pass 的诊断档；见文件头的取值表）
                if (_PixelartDebugMode > kDebugGate - 0.5 && _PixelartDebugMode < kDebugGate + 0.5)
                {
                    marker = gateHit > 0.0 ? 1.0 : 0.0;
                }
                else if (_PixelartDebugMode > kDebugCoverage - 0.5 && _PixelartDebugMode < kDebugCoverage + 0.5)
                {
                    bool covered = NeighborCovered(uvCenter)
                        || NeighborCovered(uvCenter + float2(texel.x, 0.0))
                        || NeighborCovered(uvCenter - float2(texel.x, 0.0))
                        || NeighborCovered(uvCenter + float2(0.0, texel.y))
                        || NeighborCovered(uvCenter - float2(0.0, texel.y));
                    marker = covered ? 1.0 : 0.0;
                }

                // 契约 §4.4：本 pass 只写标记，其余通道写 0（墨色由着色那几趟取 `_PixelartInkColor`）。
                return half4(marker, 0.0, 0.0, 0.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
