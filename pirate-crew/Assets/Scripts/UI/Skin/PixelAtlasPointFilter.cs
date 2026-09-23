using TMPro;
using UnityEngine;

namespace PirateCrew.UI
{
    /// <summary>
    /// 像素字体动态图集的 Point 过滤纠偏件：TMP 运行时重建图集后 filterMode 会被重置成
    /// Bilinear——本件每帧把像素字体图集钉回 Point，字形按需进图集的整个过程里不出半格渗色
    /// （第一张实机截图"重影"的病根，见 PixelShowcasePage 类头）。
    ///
    /// 【为什么是顶级类】Unity 的 MonoScript 解析要求「类名 = 文件名、不嵌套」——嵌套类挂进
    /// 场景/Prefab 会还原不回来（ScriptAssetSerializabilityTests 契约）。挂载入口：
    /// <see cref="UiKit.EnsureAtlasPointFilter"/>（运行时 UI）与
    /// <see cref="PixelShowcasePage.PixelFont"/>（展示页），同字体只挂一件。
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
