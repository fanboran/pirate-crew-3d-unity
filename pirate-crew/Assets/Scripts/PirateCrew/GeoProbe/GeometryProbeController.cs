using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace PirateCrew.GeoProbe
{
    /// <summary>
    /// **几何标定场**控制器（挂标定场根上，播放期抓帧）。
    ///
    /// 【标定什么】一排几何基元（真实 Crew 预制体 + 球/立方体/圆柱/六棱柱/正八面体，
    /// 色相互异）随转台整体旋转，每 10° 抓一张画布成图。角色探针只看单方位的静态帧，
    /// 这里看**全相位动态**：剪影对称性、逐物体描边是否闭环（悬空物体叠在别的物体前面
    /// 的那段圈）、带界抖动点阵、1px 级特征随相位的闪动——转一圈全部现形。
    ///
    /// 【机位口径】与角色探针同源：俯仰 30°、可见 22.5m、WPP=1/24、k=2
    /// （常量与 <see cref="PirateCrew.CrewProbe.CrewProbeController"/> 对齐，别单边改）。
    /// </summary>
    public sealed class GeometryProbeController : MonoBehaviour
    {
        public const string SceneName = "GeometryProbeRoot";
        public const float PitchDegrees = 30f;
        public const float BaseAzimuthDegrees = 45f;
        public const float CameraDistance = 30f;
        /// <summary>标定场紧密取景口径：可见高度 7.8m → 整排形体撑满画布（非战斗的 22.5m）。</summary>
        public const float BattleVisibleMeters = 22.5f;
        public const int BattlePixelScale = 2;
        /// <summary>转台步进角（度）。36 帧 = 一圈。</summary>
        public const int AzimuthStep = 10;
        /// <summary>自转时的上下浮动幅度（米）。0.06m ≈ 1.4 艺术像素（P=24），轻微浮动。</summary>
        public const float BobAmplitude = 0.06f;

        [Header("接线（烘焙器写入）")]
        public PirateCrew.Rendering.Pixelart.PixelartCameraRig rig;
        public Transform[] spinners;   // 各自原地自转 + 上下浮动的形体（位置固定不公转）

        Vector3[] _basePositions;

        /// <summary>场景资产按「rig 禁用」保存；播放期由此启用（角色探针同款约定）。</summary>
        void Start()
        {
            if (rig != null && !rig.enabled)
                rig.enabled = true;
        }

        /// <summary>桥调用入口：转台 0°→350° 每 10° 抓一张，落 export/geo-probe/。</summary>
        public static void CaptureTurntableInScene()
        {
            var controller = FindFirstObjectByType<GeometryProbeController>();
            if (controller == null || controller.rig == null || !controller.rig.IsReady
                || controller.spinners == null || controller.spinners.Length == 0)
            {
                PirateCrew.Core.Log.Warn("[GeoProbe] 场景里没有 GeometryProbeController（或 rig 未就绪/转台未接线），无法抓帧。");
                return;
            }
            controller.StartCoroutine(controller.CaptureTurntableRoutine());
        }

        IEnumerator CaptureTurntableRoutine()
        {
            string repo = Directory.GetParent(Application.dataPath).Parent.FullName;
            string dir = Path.Combine(repo, "export", "geo-probe",
                "turntable-" + DateTime.Now.ToString("yyyy-MM-dd-HHmmss-fff"));
            Directory.CreateDirectory(dir);

            _basePositions = new Vector3[spinners.Length];
            for (int i = 0; i < spinners.Length; i++)
                _basePositions[i] = spinners[i].localPosition;

            yield return null;
            int frames = 0;
            for (int az = 0; az < 360; az += AzimuthStep)
            {
                float rad = az * Mathf.Deg2Rad;
                for (int i = 0; i < spinners.Length; i++)
                {
                    // 原地自转（各自绕自己的 Y 轴，位置不动）+ 上下轻微浮动
                    spinners[i].localRotation = Quaternion.Euler(0f, az, 0f);
                    Vector3 p = _basePositions[i];
                    p.y += BobAmplitude * Mathf.Sin(rad);
                    spinners[i].localPosition = p;
                }
                yield return null;                          // 让渲染走完一帧再等帧尾读回
                yield return new WaitForEndOfFrame();
                SaveResult(Path.Combine(dir, "az" + az.ToString("000") + ".png"));
                frames++;
                yield return null;
            }
            for (int i = 0; i < spinners.Length; i++)
            {
                spinners[i].localRotation = Quaternion.identity;
                spinners[i].localPosition = _basePositions[i];
            }
            File.WriteAllText(Path.Combine(dir, "meta.txt"),
                "pitch=" + PitchDegrees + "\nvisibleMeters=" + BattleVisibleMeters
                + "\npixelScale=" + BattlePixelScale + "\nworldPerPixel=" + rig.worldPerPixel
                + "\nframes=" + frames + "\nstep=" + AzimuthStep
                + "\nmode=in-place spin + bob " + BobAmplitude + "m\n");
            PirateCrew.Core.Log.Info("[GeoProbe] 自转抓帧完成：" + dir + "（" + frames + " 帧）");
        }

        // ---- 画布成图读回（与 CrewProbeController 同一套约定：线性读回 → gamma → 两次行翻转）----

        void SaveResult(string path)
        {
            RenderTexture rt = rig.ResultBuffer;
            Color32[] px = ReadColor(rt);
            Gamma(px);
            SavePixels(px, rt.width, rt.height, path);
        }

        static Color32[] ReadColor(RenderTexture rt)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, true);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            Color32[] px = tex.GetPixels32();
            Destroy(tex);
            FlipRows(px, rt.width, rt.height);
            return px;
        }

        static void FlipRows(Color32[] px, int width, int height)
        {
            for (int y = 0; y < height / 2; y++)
            {
                int a = y * width;
                int b = (height - 1 - y) * width;
                for (int x = 0; x < width; x++)
                {
                    Color32 tmp = px[a + x];
                    px[a + x] = px[b + x];
                    px[b + x] = tmp;
                }
            }
        }

        static void Gamma(Color32[] px)
        {
            for (int i = 0; i < px.Length; i++)
            {
                px[i] = new Color32(
                    ToGamma(px[i].r), ToGamma(px[i].g), ToGamma(px[i].b), px[i].a);
            }
        }

        static byte ToGamma(byte v)
        {
            return (byte)Mathf.Clamp(Mathf.LinearToGammaSpace(v / 255f) * 255f, 0f, 255f);
        }

        static void SavePixels(Color32[] px, int w, int h, string path)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            var flipped = new Color32[px.Length];
            for (int y = 0; y < h; y++)
                Array.Copy(px, y * w, flipped, (h - 1 - y) * w, w);
            tex.SetPixels32(flipped);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
        }
    }
}
