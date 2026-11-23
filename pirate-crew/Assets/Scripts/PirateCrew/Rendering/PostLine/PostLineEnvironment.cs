using UnityEngine;
using UnityEngine.Rendering;

namespace PirateCrew.Rendering.PostLine
{
    /// <summary>
    /// PostLine 线的**环境 / 云影全局下发**（参照库 DayNightCycleManager + CloudShadowManager 的
    /// 本仓等价物）。
    ///
    /// 【为什么必须有这一层】<c>PostLineToonLit</c> 的默认分支读两个**实名全局**：
    /// <c>_GlobalAmbientColor</c>（环境加数）与 <c>_CloudNoise</c> 为首的 11 个云影量。
    /// 它们没有任何材质属性可以兜底——缺省时环境 = 0（未受光面纯黑）、云影 = 1（无影），
    /// 画面会"能跑但完全不是参照观感"，且不报任何错。参照库把这两组量交给两个场景组件
    /// 每帧 SetGlobal*；本仓固定光照不搬昼夜系统，改为在本链每帧推常量 + 场景环境色。
    ///
    /// 【与 CellShade 的分工】这里写的是 <c>_GlobalAmbientColor</c>；CellShade 的 G-buffer
    /// 着色读 <c>_PixelartAmbientColor</c>（<see cref="Pixelart.PixelartPath.AmbientColorId"/>），
    /// 两者是两条链各自的契约。**数值源相同**（都是场景环境色），名字不能合并——合并会让
    /// 一条链的默认值污染另一条。
    ///
    /// 【数值出处】云影常数逐项照抄参照库 <c>CloudShadowManager</c> 的 Inspector 默认值；
    /// <c>_CloudSpeed</c> 用规格 §8.6 的固定光照标定值（0.0119 ≈ 84 s 平移一个噪声周期）。
    /// </summary>
    public static class PostLineEnvironment
    {
        /// <summary>云噪声图在 Resources 下的路径（sRGB + Bilinear + Repeat 是硬要求，见 shader 头注）。</summary>
        const string CloudNoiseResourcePath = "PixelartNoise/CloudNoise";

        // ---- 云影参数（参照 CloudShadowManager 默认值；世界空间语义，与像素比无关）----
        // 【2026-11-09 调值】白雲台上的云影不能照搬默认 0.3 影底：整片云顶会被压到
        // 色带 1/4 档、白面读成灰面（实拍：两整块云台同时发灰）。影底抬到 0.55、
        // 对比度 1.6——仍能看出云影在跑，但白顶保持白。
        const float CloudScale = 50f;          // 噪声世界尺度（米 / 平铺）
        const float CloudWorldY = 20f;         // 云层平面高度（米）
        const float CloudSpeed = 0.0119f;      // 规格 §8.6：固定光照自定标（≈84 s / 周期）
        const float CloudContrast = 1.6f;
        const float CloudThreshold = 0.25f;
        const float CloudShadowMin = 0.55f;
        const float CloudDivergeAngle = 10f;
        const float CloudPower = 1f;

        static readonly Vector2 CloudDirection = new Vector2(1f, 0f);

        /// <summary>投影方向（指向云层；参照 DayNightCycleManager 的 cloudShadowDirection 默认值）。</summary>
        static readonly Vector3 CloudLightDirection = new Vector3(0.2f, -1f, 0.2f).normalized;

        static Texture2D s_CloudNoise;
        static bool s_LoadTried;
        static bool s_WarnedMissing;

        /// <summary>每帧下发（由 <see cref="PostLineRendererFeature"/> 在录制命令缓冲时调用）。</summary>
        public static void Push(CommandBuffer cmd)
        {
            PushAmbient(cmd);
            PushCloudShadows(cmd);
        }

        /// <summary>
        /// 环境光：口径与 <see cref="Pixelart.PixelartCameraRig"/> 一致——用
        /// <c>RenderSettings.ambientLight</c>（AmbientDirector 的档位色就写在这里；
        /// 三色梯度是装配期占位、运行时以档位色为准）乘 <c>ambientIntensity</c>。
        /// 显式 .linear：全局颜色的隐式转换口径不明确，两条链都不赌它。
        /// </summary>
        static void PushAmbient(CommandBuffer cmd)
        {
            Color ambient = RenderSettings.ambientLight * RenderSettings.ambientIntensity;
            Color linear = ambient.linear;
            cmd.SetGlobalVector(PostLinePath.GlobalAmbientColorId,
                new Vector4(linear.r, linear.g, linear.b, 1f));
        }

        static void PushCloudShadows(CommandBuffer cmd)
        {
            EnsureCloudNoise();
            if (s_CloudNoise != null)
                cmd.SetGlobalTexture(PostLinePath.CloudNoiseId, s_CloudNoise);

            cmd.SetGlobalFloat(PostLinePath.CloudScaleId, CloudScale);
            cmd.SetGlobalFloat(PostLinePath.CloudWorldYId, CloudWorldY);
            cmd.SetGlobalFloat(PostLinePath.CloudSpeedId, CloudSpeed);
            cmd.SetGlobalFloat(PostLinePath.CloudContrastId, CloudContrast);
            cmd.SetGlobalFloat(PostLinePath.CloudThresholdId, CloudThreshold);
            cmd.SetGlobalFloat(PostLinePath.CloudShadowMinId, CloudShadowMin);
            cmd.SetGlobalFloat(PostLinePath.CloudDivergeAngleId, CloudDivergeAngle);
            cmd.SetGlobalFloat(PostLinePath.CloudPowerId, CloudPower);
            cmd.SetGlobalVector(PostLinePath.CloudDirectionId,
                new Vector4(CloudDirection.x, CloudDirection.y, 0f, 0f));
            cmd.SetGlobalVector(PostLinePath.CloudLightDirectionId,
                new Vector4(CloudLightDirection.x, CloudLightDirection.y, CloudLightDirection.z, 0f));
        }

        /// <summary>
        /// **CPU 侧立即下发同一组云影常量**（不经 CommandBuffer）。CellShade 链没有
        /// PostLine 的录制趟，而水面 ocean shader 也读这套全局（参照库同式云影）——
        /// 由 <see cref="SceneArt.CloudSea.CloudSeaWeather"/> 每帧补推，保证水面与
        /// PostLine 地面云影同一张噪声图、同一组阈值/尺度，两种渲染风格云影一致。
        /// 数值与 <see cref="PushCloudShadows"/> 共用上面的常量，不另立一套。
        /// </summary>
        public static void PushCloudShadowGlobals()
        {
            EnsureCloudNoise();
            if (s_CloudNoise != null)
                Shader.SetGlobalTexture(PostLinePath.CloudNoiseId, s_CloudNoise);

            Shader.SetGlobalFloat(PostLinePath.CloudScaleId, CloudScale);
            Shader.SetGlobalFloat(PostLinePath.CloudWorldYId, CloudWorldY);
            Shader.SetGlobalFloat(PostLinePath.CloudSpeedId, CloudSpeed);
            Shader.SetGlobalFloat(PostLinePath.CloudContrastId, CloudContrast);
            Shader.SetGlobalFloat(PostLinePath.CloudThresholdId, CloudThreshold);
            Shader.SetGlobalFloat(PostLinePath.CloudShadowMinId, CloudShadowMin);
            Shader.SetGlobalFloat(PostLinePath.CloudDivergeAngleId, CloudDivergeAngle);
            Shader.SetGlobalFloat(PostLinePath.CloudPowerId, CloudPower);
            Shader.SetGlobalVector(PostLinePath.CloudDirectionId,
                new Vector4(CloudDirection.x, CloudDirection.y, 0f, 0f));
            Shader.SetGlobalVector(PostLinePath.CloudLightDirectionId,
                new Vector4(CloudLightDirection.x, CloudLightDirection.y, CloudLightDirection.z, 0f));
        }

        static void EnsureCloudNoise()
        {
            if (s_LoadTried)
                return;
            s_LoadTried = true;
            s_CloudNoise = Resources.Load<Texture2D>(CloudNoiseResourcePath);
            if (s_CloudNoise == null && !s_WarnedMissing)
            {
                s_WarnedMissing = true;
                global::PirateCrew.Core.Log.Warn(
                    "[PostLineEnvironment] 缺 Resources/" + CloudNoiseResourcePath
                    + ".png——云影全局不完整，shader 会按 _CloudScale<=0 护栏退化为无影。");
            }
        }
    }
}
