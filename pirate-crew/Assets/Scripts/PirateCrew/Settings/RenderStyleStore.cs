using System.Globalization;
using PirateCrew.Core;

namespace PirateCrew.Settings
{
    /// <summary>
    /// 渲染风格的纯键值层（设置槽 9，与音频/视频/像素比例读改写共存）。
    ///
    /// 【纯/脏分层】读写只碰 <see cref="SaveData"/> 键值，不碰引擎（同 <see cref="PixelScaleStore"/>）；
    /// 应用（写 <see cref="RenderStyleState"/>）与重载场景的时机在设置面板服务层。
    /// 值域 = "cellshade" / "postline"；键缺失或非法时保持传入原值（旧档/首启）。
    /// </summary>
    public static class RenderStyleStore
    {
        /// <summary>与音频/视频/像素比例共用的设置槽位（读改写，互不覆盖）。</summary>
        public const int SettingsSlot = 9;

        /// <summary>槽位显示名（同一份设置数据的多个写入方，显示名一致）。</summary>
        public const string SettingsDisplayName = "视频设置";

        /// <summary>风格键。</summary>
        public const string StyleKey = "render.style";

        const string ValueCellShade = "cellshade";
        const string ValuePostLine = "postline";

        /// <summary>把当前风格写入存档数据（纯函数，不落盘）。</summary>
        public static void WriteTo(SaveData data, RenderStyle style)
        {
            if (data == null)
                return;

            data.SetData(StyleKey, style == RenderStyle.PostLine ? ValuePostLine : ValueCellShade);
        }

        /// <summary>
        /// 从存档数据读档（纯函数）。键缺失或值非法时返回 false，调用方保持原值。
        /// </summary>
        public static bool TryReadFrom(SaveData data, ref RenderStyle style)
        {
            if (data == null)
                return false;

            string raw = data.GetData(StyleKey);
            if (string.IsNullOrEmpty(raw))
                return false;

            switch (raw.Trim().ToLowerInvariant())
            {
                case ValueCellShade:
                    style = RenderStyle.CellShade;
                    return true;
                case ValuePostLine:
                    style = RenderStyle.PostLine;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>风格代号 → 存档值（落盘/日志用）。</summary>
        public static string ToStorageValue(RenderStyle style)
        {
            return style == RenderStyle.PostLine ? ValuePostLine : ValueCellShade;
        }
    }
}
