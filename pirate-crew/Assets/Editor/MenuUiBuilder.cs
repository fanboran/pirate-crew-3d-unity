using System.Collections.Generic;
using System.IO;
using PirateCrew.UI;
using PirateCrew.UI.Stick;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 菜单类界面（主菜单 / 船员管理 / 选关 / 设置 / 结算弹窗）的共享装配工具。
    ///
    /// 【职责】
    ///   1. 中文字体接入：按规范 §5.1 的三档选型加载 TMP 字体资产；
    ///      资产缺失时**先调用现有的 <see cref="FontAssetBuilder.BuildAll"/> 生成**（幂等，不改该文件），
    ///      仍缺失则回落到 ttf（Dynamic Font）并 <c>Debug.LogWarning</c>，绝不静默出方块字（§5.5）；
    ///   2. 控件装配：设置面板 / 确认弹窗（<see cref="BuildSettingsPanel"/> / <see cref="BuildConfirmDialog"/>），
    ///      件全部走像素皮（<see cref="UiKit.EnsureWindow"/> / <see cref="SketchButton"/> / <see cref="SketchSeparator"/>）；
    ///      字号真值 = <see cref="UiSkin.Font"/>（文本出口统一经 <see cref="UiKit.ResolvePixelFont"/> 解析字体档）。
    ///
    /// 【历史包袱已清退】旧玻璃族工厂（CreatePanel/CreateButton/CreateGlassPanel/GetSprite/
    /// ApplyGlassSkin 等 UiSprites + GlassPanelSpriteBuilder 兼容层）随 UiSprites / GlassPanelSpriteBuilder
    /// 一并退役（2026-09-24 UI 清退批次）——主菜单 / 船员管理 / 选关的底板与按钮全走
    /// <see cref="UiKit.EnsureWindow"/> / <see cref="UiKit.EnsurePanel"/> 直切件皮 / <see cref="SketchButton"/>。
    /// </summary>
    public static class MenuUiBuilder
    {
        // ------------------------------------------------------------------
        // 字号体系（【像素栅格并档】真值源 = UiSkin.Font，本表只是 Editor 侧别名）
        // ------------------------------------------------------------------


        // ------------------------------------------------------------------
        // 字体（【像素字体全局切换】三档统一 Fusion Pixel 12px 位图档；2026-09-23）
        // ------------------------------------------------------------------

        /// <summary>像素字体资产路径（Art 侧；原生档纪律——字号档 = 字体原生设计档，不放大）。
        /// 标题族 16 = 正格点黑16（简体全过）；正文 12 / 次级 10 = 缝合像素。</summary>
        public const string TitleFontAssetPath = "Assets/Art/Fonts/ZhengGeDianHei16.asset";

        /// <summary>正文/按钮（12 原生档，缝合像素）。</summary>
        public const string BodyFontAssetPath = "Assets/Art/Fonts/FusionPixel12.asset";

        /// <summary>次级说明（10 原生档，缝合像素）。</summary>
        public const string SecondaryFontAssetPath = "Assets/Art/Fonts/FusionPixel10.asset";

        /// <summary>缺失位图资产时的 ttf 回落路径（Unity 已导入为 Dynamic Font）。</summary>
        const string TitleFontTtfPath = "Assets/Art/Fonts/ZhengGeDianHei16.ttf";
        const string BodyFontTtfPath = "Assets/Art/Fonts/FusionPixel12-zh_hans.ttf";
        const string SecondaryFontTtfPath = "Assets/Art/Fonts/FusionPixel10-zh_hans.ttf";

        static TMP_FontAsset _title;
        static TMP_FontAsset _body;
        static TMP_FontAsset _secondary;
        static bool _fontEnsured;

        /// <summary>标题字体（StickHand 手写体）。</summary>
        public static TMP_FontAsset TitleFont => _title != null ? _title : (_title = LoadFont(TitleFontAssetPath, TitleFontTtfPath, "标题"));

        /// <summary>正文中文字体（霞鹜文楷 Medium）。</summary>
        public static TMP_FontAsset BodyFont => _body != null ? _body : (_body = LoadFont(BodyFontAssetPath, BodyFontTtfPath, "正文"));

        /// <summary>次级中文字体（霞鹜文楷 Regular）。</summary>
        public static TMP_FontAsset SecondaryFont =>
            _secondary != null ? _secondary : (_secondary = LoadFont(SecondaryFontAssetPath, SecondaryFontTtfPath, "次级"));

        /// <summary>确保字体资产存在（幂等；内部会触发一次 FontAssetBuilder）。</summary>
        public static void EnsureFonts()
        {
            if (_fontEnsured)
                return;

            _fontEnsured = true;

            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BodyFontAssetPath) == null
                || AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TitleFontAssetPath) == null)
            {
                Debug.LogWarning("[MenuUiBuilder] 未找到中文 TMP 字体资产，先调用 FontAssetBuilder.BuildAll 生成。");
                FontAssetBuilder.BuildAll();
            }

            // 触发三档加载（各自缺失时告警并回落 ttf）。
            _ = TitleFont;
            _ = BodyFont;
            _ = SecondaryFont;
        }

        static TMP_FontAsset LoadFont(string assetPath, string ttfPath, string role)
        {
            TMP_FontAsset asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (asset != null)
                return asset;

            Font ttf = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
            if (ttf != null)
            {
                Debug.LogWarning("[MenuUiBuilder] 缺 TMP 字体资产 " + assetPath
                    + "（角色：" + role + "），回落 ttf：" + ttfPath
                    + "。请跑菜单 PirateCrew/Fonts/生成 TMP 中文字体资产（幂等）后重建场景。");
                return TMP_FontAsset.CreateFontAsset(ttf);
            }

            Debug.LogWarning("[MenuUiBuilder] 字体资产与 ttf 都缺失（角色：" + role
                + "）。中文会显示为方块——请先导入 Assets/Art/Fonts/ 下的 ttf，"
                + "再执行 Window/TextMeshPro/Import TMP Essential Resources，"
                + "然后跑 PirateCrew/Fonts/生成 TMP 中文字体资产（幂等）。");
            return null;
        }


        /// <summary>确保资产目录存在（幂等；UiSkinAssetBaker / ReleaseGate 等编辑器工具共用）。</summary>
        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(path);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}