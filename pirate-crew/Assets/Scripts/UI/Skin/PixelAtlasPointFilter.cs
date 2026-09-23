using TMPro;
using UnityEngine;

namespace PirateCrew.UI
{
    /// <summary>
    /// 【已退役】像素字体位图图集的 Point 过滤纠偏件（历史：位图口径时代，TMP 运行时重建
    /// 动态图集会把 filterMode 重置回 Bilinear，本件逐帧钉回 Point 防相邻字形渗色）。
    /// 2026-09-24 文字解除像素栅格、像素字体改 SDF 口径（FusionPixel12-sdf / ArkPixel10-sdf，
    /// Linear 过滤 + SDF shader）后不再有挂载入口（UiKit / PixelShowcasePage 均已移除）。
    /// 类保留：BattleRig.prefab 与 MainMenuScreen.prefab 仍有序列化引用，删除会出
    /// missing script；其 <c>font</c> 引用指向已删除的旧位图资产（null 空转），无害。
    /// 下次 prefab 重建批次可一并摘除本类。
    /// </summary>
    public sealed class PixelAtlasPointFilter : MonoBehaviour
    {
        const int FramesToWatch = 600;

        public TMP_FontAsset font;
        int _frames;

        void LateUpdate()
        {
            if (font == null || font.atlasTextures == null)
                return;
            bool allPoint = true;
            foreach (Texture2D texture in font.atlasTextures)
            {
                if (texture == null)
                    continue;
                if (texture.filterMode != FilterMode.Point)
                {
                    texture.filterMode = FilterMode.Point;
                    allPoint = false;
                }
            }
            _frames++;
            if (allPoint && _frames > FramesToWatch)
                enabled = false;   // 字形已稳定进图集，停止逐帧纠偏
        }
    }
}
