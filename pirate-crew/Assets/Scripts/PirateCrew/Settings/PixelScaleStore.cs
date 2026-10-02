using System.Globalization;
using PirateCrew.Core;

namespace PirateCrew.Settings
{
    /// <summary>
    /// 像素比例档的纯键值层（设置槽 9，与音频/视频设置读改写共存）。
    ///
    /// 【档语义】"1 艺术像素 = N 屏幕像素"：UI 画布（CanvasScaler.scaleFactor）与像素化渲染
    /// rig（RT = 屏幕 ÷ N）同走一档。域 = {自动, 2, 3, 4}——渲染侧相机行为契约钉死滚轮域
    /// 1:2–1:5，本档取其子集 [2,4]，1×（关像素化）不是本档的事。
    ///
    /// 【纯/脏分层】读写只碰 <see cref="SaveData"/> 键值，不碰引擎（同 VideoSettingsStore）；
    /// 应用与落盘时机在 <c>PixelScaleService</c>（运行时）。
    /// </summary>
    public static class PixelScaleStore
    {
        /// <summary>与音频/视频设置共用的设置槽位（读改写，互不覆盖）。</summary>
        public const int SettingsSlot = 9;

        /// <summary>槽位显示名（与视频设置同名——同一份数据的两个写入方）。</summary>
        public const string SettingsDisplayName = "视频设置";

        /// <summary>比例键（值 "auto" / "2" / "3" / "4"）。</summary>
        public const string ScaleKey = "video.pixelScale";

        /// <summary>档位值：自动（按屏幕高 ÷ 参考画布高取最大整数倍）。</summary>
        public const int ScaleAuto = 0;

        /// <summary>档位下界（渲染侧相机契约范围 1:2–1:5 的下端）。</summary>
        public const int ScaleMin = 2;

        /// <summary>档位上界（4K 下 4× 恰好回到 960×540 艺术画布；5 留给战斗滚轮）。</summary>
        public const int ScaleMax = 4;

        /// <summary>默认档：2×（1080p → 960×540 画布，现行口径不变）。</summary>
        public const int ScaleDefault = 2;

        /// <summary>参考画布高（960×540）：自动档换算基准（屏幕高 ÷ 本值 = 整数倍数）。</summary>
        public const int ReferenceCanvasHeight = 540;

        /// <summary>把当前档写入存档数据（纯函数，不落盘）。</summary>
        public static void WriteTo(SaveData data, int scaleIndex)
        {
            if (data == null)
                return;

            data.SetData(ScaleKey, scaleIndex == ScaleAuto
                ? "auto"
                : scaleIndex.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// 从存档数据读档（纯函数）。键缺失时返回 false（旧档/首启保持传入原值）；
        /// 值非法（非 auto 且越界）同样忽略。
        /// </summary>
        public static bool TryReadFrom(SaveData data, ref int scaleIndex)
        {
            if (data == null)
                return false;

            string raw = data.GetData(ScaleKey);
            if (string.IsNullOrEmpty(raw))
                return false;

            if (raw == "auto")
            {
                scaleIndex = ScaleAuto;
                return true;
            }

            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                && parsed >= ScaleMin && parsed <= ScaleMax)
            {
                scaleIndex = parsed;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 自动档换算（纯函数）：屏幕高 ÷ 参考画布高取最大整数，钳 [ScaleMin, ScaleMax]。
        /// 1080p→2（现行观感不变）、1620p→3、2160p→4；非正数（无头/编辑器异常环境）回落默认档。
        /// </summary>
        public static int AutoUnit(int screenHeight)
        {
            if (screenHeight <= 0)
                return ScaleDefault;

            int unit = screenHeight / ReferenceCanvasHeight;
            if (unit < ScaleMin)
                unit = ScaleMin;
            if (unit > ScaleMax)
                unit = ScaleMax;
            return unit;
        }

        /// <summary>把档解析成整数倍（自动档按给定屏幕高换算；固定档原样返回）。纯函数。</summary>
        public static int ResolveUnit(int scaleIndex, int screenHeight)
        {
            return scaleIndex == ScaleAuto ? AutoUnit(screenHeight) : scaleIndex;
        }
    }
}
