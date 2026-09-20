using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.UI
{
    /// <summary>
    /// 手绘涂鸦皮肤的槽位帧加载器（自 game-2/stick-world 的 SketchTextures 机制移植，
    /// 用户 2026-09-20 裁决：隔壁 UI 素材与生成流水线原封不动搬来，只换配色）。
    ///
    /// 【素材来源·双集】主集 = stick-world 原版烘焙 <c>Assets/Resources/UI/StickWorld/sketch/&lt;槽&gt;_f&lt;i&gt;.png</c>
    /// （27 槽×3 帧，隔壁 gen_sketch_ui 同源）；旧改配色集 <c>Assets/Resources/UI/Sketch/</c>
    /// 仅剩 pirate 专属槽与九砖切图继续供给（见 <see cref="UsesLegacySet"/>）。
    /// Editor 装配的场景序列化引用持久资产，播放器 <c>Resources.Load</c> 同路径取帧，一份两用。
    ///
    /// 【槽位语义】对齐隔壁 sketch_style 变体表：<c>panel</c>/<c>btn_normal..</c>/
    /// <c>btn_primary_*</c>/<c>danger_*</c>/<c>progress_bg</c>/<c>progress_fill</c> 上游原样；
    /// <c>pip</c>/<c>ring</c>/<c>fill</c>/<c>cell</c> 是本项目 tint 槽（白实底墨边，
    /// 运行时 <c>Image.color</c> 乘色——白系边乘深色会隐形，彩底件一律走墨边槽）。
    /// </summary>
    public static class SketchSkin
    {
        /// <summary>每个槽位的沸腾帧数（烘焙管线 FRAMES=3）。</summary>
        public const int FrameCount = 3;

        /// <summary>沸腾帧节拍（隔壁 sketch_textures 的 _FrameDriver 7.5fps，像素级复刻对齐口径；
        /// 注意与 sketch_draw 侧 wobble 重掷的 0.12s 是两套时钟，后者在 SketchWobbleGraphic 内）。</summary>
        public const float BoilSeconds = 1f / 7.5f;

        /// <summary>九宫格边带宽（烘焙 MARGIN=10；圆槽 pip/ring 不切片不受此值约束）。</summary>
        public const int NineSliceBorder = 10;

        /// <summary>按钮常规件的状态槽组（normal / hover / pressed / disabled）。</summary>
        public static readonly string[] Btn = { "btn_normal", "btn_hover", "btn_pressed", "btn_disabled" };

        /// <summary>金色主行动按钮的状态槽组（实底金 + 墨边）。</summary>
        public static readonly string[] BtnPrimary = { "btn_primary_normal", "btn_primary_hover", "btn_primary_pressed", "btn_primary_disabled" };

        /// <summary>危险动作按钮的状态槽组（StickWorld 集无 danger_pressed/disabled 档——
        /// 隔壁 sketch_button.gd:79 缺档一律回 btn_normal，accent 同口径）。</summary>
        public static readonly string[] Danger = { "danger_normal", "danger_hover", "btn_normal", "btn_normal" };

        /// <summary>强调按钮的状态槽组（金叠加底+金边——选中态语义，隔壁 ACCENT；
        /// 集无 accent_disabled 档，缺档回 btn_normal 对齐 sketch_button.gd:79）。</summary>
        public static readonly string[] Accent = { "accent_normal", "accent_hover", "accent_pressed", "btn_normal" };

        /// <summary>纸面按钮的状态槽组（奶油纸底+墨边——亮背景形态，隔壁 btn_ink）。</summary>
        public static readonly string[] Ink = { "btn_ink_normal", "btn_ink_hover", "btn_ink_pressed", "btn_ink_disabled" };

        /// <summary>选中态槽（accent hover 档：金底叠加 + 金边）。</summary>
        public const string AccentHover = "accent_hover";

        /// <summary>
        /// 平涂形状枚举 → 手绘槽名（旧调用点的 Shape 参数语义保留：
        /// tint 家族映射到白底墨边 tint 槽，固定色家族映射到成品色槽）。
        /// </summary>
        public static string SlotOfShape(CartoonSpriteFactory.Shape shape)
        {
            switch (shape)
            {
                case CartoonSpriteFactory.Shape.Slot: return "cell";
                case CartoonSpriteFactory.Shape.Pill: return "fill";
                case CartoonSpriteFactory.Shape.Ring: return "ring";
                case CartoonSpriteFactory.Shape.Circle: return "pip";
                case CartoonSpriteFactory.Shape.PanelInk: return "panel";
                case CartoonSpriteFactory.Shape.BarTrack: return "progress_bg";
                default: return "btn_normal";
            }
        }

        /// <summary>取某槽某帧。帧号越界自动回绕；槽缺失（未烘焙）时告警一次并返回 null
        /// （调用方保留已有 sprite，界面不至于白块）。</summary>
        public static Sprite Frame(string slot, int frame)
        {
            if (string.IsNullOrEmpty(slot))
                return null;
            frame = ((frame % FrameCount) + FrameCount) % FrameCount;
            // 双集分流：默认 StickWorld 原版集；pirate 专属槽/九砖切图继续走旧集（UsesLegacySet）。
            string path = (UsesLegacySet(slot) ? "UI/Sketch/" : "UI/StickWorld/sketch/") + slot + "_f" + frame;
            Sprite sprite = Resources.Load<Sprite>(path);
            if (sprite == null)
                WarnOnce(path);
            return sprite;
        }

        /// <summary>pirate 专属 tint 槽（白实底墨边，运行时乘色）——StickWorld 集没有，保留旧集。</summary>
        static readonly HashSet<string> LegacyTintSlots = new HashSet<string> { "pip", "ring", "fill", "cell" };

        /// <summary>九砖切图部位后缀（Sketch9Slice 的 &lt;槽&gt;_&lt;部位&gt;_f&lt;i&gt;）——
        /// 切砖只随旧烘焙管线产出，StickWorld 集只有整图帧，故九砖槽一律走旧集。</summary>
        static readonly string[] NineSlicePartNames = { "tl", "tc", "tr", "ml", "mc", "mr", "bl", "bc", "br" };

        /// <summary>该槽是否仍从旧集 UI/Sketch/ 取图（其余一律走 StickWorld 集）。</summary>
        static bool UsesLegacySet(string slot)
        {
            if (LegacyTintSlots.Contains(slot))
                return true;
            int underscore = slot.LastIndexOf('_');
            if (underscore < 0)
                return false;
            return System.Array.IndexOf(NineSlicePartNames, slot.Substring(underscore + 1)) >= 0;
        }

        static readonly HashSet<string> Warned = new HashSet<string>();

        static void WarnOnce(string path)
        {
            if (Warned.Add(path))
                Debug.LogWarning("[SketchSkin] Resources/" + path + " 缺失（跑 tools/sketch_ui/gen_sketch_ui.py + 导入参数烘焙后可用）");
        }
    }
}
