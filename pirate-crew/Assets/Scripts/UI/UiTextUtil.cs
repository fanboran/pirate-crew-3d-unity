using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI
{
    /// <summary>
    /// UI 文本/字体的兼容工具层。
    ///
    /// 【为什么需要它】战斗 HUD 的 <see cref="BattleHud"/> 序列化字段被
    /// <c>Assets/Editor/M2BattleSceneSetup.cs</c> 直接赋值（该文件由并行波次占用、本波次禁改）。
    /// 为了让「旧装配脚本（写 legacy <see cref="Text"/>）」与「本波次新装配（写
    /// <see cref="TextMeshProUGUI"/>）」都能编译且都能运行，字段统一用二者共同基类
    /// <see cref="MaskableGraphic"/>，取值时再由本类分派到 TMP 或 legacy 分支。
    /// 新装配产出的场景里所有文本都是 TMP，legacy 分支只是兼容旧脚本的兜底。
    ///
    /// 【字体规矩】引用不到 TMP 中文字体资产时**必须** <c>Debug.LogWarning</c>，
    /// 不得静默出方块字（规范 §5.5）。
    /// </summary>
    public static class UiTextUtil
    {
        /// <summary>设置文本（TMP 优先；legacy Text 兜底）。</summary>
        public static void SetText(MaskableGraphic graphic, string text)
        {
            switch (graphic)
            {
                case null:
                    return;
                case TextMeshProUGUI tmp:
                    tmp.text = text;
                    return;
                case Text legacy:
                    legacy.text = text;
                    return;
            }
        }

        /// <summary>读取文本（TMP 优先；legacy Text 兜底）。</summary>
        public static string GetText(MaskableGraphic graphic)
        {
            switch (graphic)
            {
                case null:
                    return string.Empty;
                case TextMeshProUGUI tmp:
                    return tmp.text;
                case Text legacy:
                    return legacy.text;
                default:
                    return string.Empty;
            }
        }

        /// <summary>设置颜色（<see cref="Graphic.color"/> 虚分派，TMP/legacy 通用）。</summary>
        public static void SetColor(Graphic graphic, Color color)
        {
            if (graphic != null)
                graphic.color = color;
        }

        /// <summary>设置字体大小（TMP 用 float，legacy 用 int）。</summary>
        public static void SetFontSize(MaskableGraphic graphic, int size)
        {
            switch (graphic)
            {
                case null:
                    return;
                case TextMeshProUGUI tmp:
                    tmp.fontSize = size;
                    return;
                case Text legacy:
                    legacy.fontSize = size;
                    return;
            }
        }

        static readonly System.Collections.Generic.HashSet<string> WarnedRoles =
            new System.Collections.Generic.HashSet<string>();

        /// <summary>
        /// 校验字体资产；为 null 时按角色只警告一次（不静默）。
        /// 返回是否可用。
        /// </summary>
        public static bool WarnIfMissing(TMP_FontAsset font, string role)
        {
            if (font != null)
                return true;

            if (WarnedRoles.Add(role))
            {
                global::PirateCrew.Core.Log.Warn("[UiTextUtil] 字体资产缺失（角色：" + role + "），"
                    + "将回落到 TMP 默认字体——中文可能显示为方块。"
                    + "请在编辑器执行 Window/TextMeshPro/Import TMP Essential Resources，"
                    + "再跑菜单 PirateCrew/Fonts/生成 TMP 中文字体资产（幂等）。");
            }

            return false;
        }
    }
}
