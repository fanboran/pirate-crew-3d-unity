using System.Collections.Generic;
using PirateCrew.PirateCrew.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI
{
    /// <summary>
    /// UI 设计系统陈列页（game-2 component_gallery 的 Unity 等价物）：
    /// 把全部控件档位 / 字号 / 色板 / 图标集中摆进一个纯 UI Canvas，截图即得
    /// "设计系统全貌图"。
    ///
    /// 【为什么】战斗 HUD 只能看到组合后的结果，看不出"档位纪律"本身——陈列页让
    /// 改 <see cref="UiSkin"/> Token 的效果一眼全览（视觉回归自检页），也是给用户
    /// 验收"风格是否统一"的最短路径。全部件经 <see cref="UiKit"/> 装配，
    /// 陈列的就是真实控件长相，不是示意图。
    ///
    /// 【出图】播放器 <c>PlayerArtCapture</c> 的 ui-gallery / ui-gallery-icons
    /// 机位：清屏（cullingMask=0）→ 建本页 → 截图 → 整页销毁。不入任何场景。
    ///
    /// 【运行时轨道】皮肤用内存 Sprite、字体走 <see cref="UiKit.RuntimeFont"/>
    /// （Resources 副本）、武器/职业图标走 Resources/UIIcons——本页不依赖 Editor。
    /// </summary>
    public static class UiGalleryPage
    {
        /// <summary>控件档位页（按钮变体 / 血条族 / pips / 字号 / 色板 / 圆角档）。</summary>
        public const string ControlsPage = "ui-gallery";
        /// <summary>图标集页（17 武器 / 7 职业 / 13 符号）。</summary>
        public const string IconsPage = "ui-gallery-icons";

        /// <summary>建陈列页根节点（挂 parent 下，占满 1920×1080）。iconsPage=false 控件页。</summary>
        public static RectTransform Build(Transform parent, bool iconsPage)
        {
            RectTransform root = UiKit.CreateRect("UiGalleryPage", parent);
            UiKit.Stretch(root);
            root.gameObject.AddComponent<CanvasGroup>().blocksRaycasts = false;

            TMP_FontAsset body = UiKit.RuntimeFont(UiKit.RuntimeFontKind.Body);
            TMP_FontAsset secondary = UiKit.RuntimeFont(UiKit.RuntimeFontKind.Secondary);
            TMP_FontAsset title = UiKit.RuntimeFont(UiKit.RuntimeFontKind.Title);

            if (iconsPage)
                BuildIconsPage(root, body, secondary, title);
            else
                BuildControlsPage(root, body, secondary, title);

            return root;
        }

        // ------------------------------------------------------------------
        // 控件档位页
        // ------------------------------------------------------------------

        static void BuildControlsPage(RectTransform root, TMP_FontAsset body,
            TMP_FontAsset secondary, TMP_FontAsset title)
        {
            PageTitle(root, "UI 设计系统 · 控件档位（全部件 = UiKit 真实长相）", title);

            float lx = -560f, cx = 0f, rx = 560f;
            ColumnLabel(root, lx, "按钮变体表（Primary / Dark / Danger）", secondary);
            UiKit.ActionButton("VPrimary", root, UiGlyphs.Glyph.ThrowArc, "主行动 · Primary",
                UiKit.ButtonKind.Primary, At(lx, 330f), new Vector2(300f, 48f), body);
            UiKit.ActionButton("VDark", root, UiGlyphs.Glyph.Flag, "常规件 · Dark",
                UiKit.ButtonKind.Dark, At(lx, 270f), new Vector2(300f, 48f), body);
            UiKit.ActionButton("VDanger", root, UiGlyphs.Glyph.Cross, "危险动作 · Danger",
                UiKit.ButtonKind.Danger, At(lx, 210f), new Vector2(300f, 48f), body);
            UiKit.ActionButton("VSmall", root, UiGlyphs.Glyph.Check, "小尺寸档",
                UiKit.ButtonKind.Dark, At(lx, 156f), new Vector2(200f, 40f), body);

            ColumnLabel(root, lx, "图标钮（46 · 快捷键角标）", secondary, 96f);
            UiKit.IconButton("IconPause", root, UiGlyphs.Glyph.Pause, At(lx - 100f, 42f),
                new Vector2(46f, 46f), UiSkin.InkSoft, UiSkin.TextOnInk);
            UiKit.IconButton("IconPlay", root, UiGlyphs.Glyph.Play, At(lx - 40f, 42f),
                new Vector2(46f, 46f), UiSkin.Gold, UiSkin.InkOnGold);
            UiKit.IconButton("IconEye", root, UiGlyphs.Glyph.Eye, At(lx + 20f, 42f),
                new Vector2(46f, 46f), UiSkin.InkSoft, UiSkin.TextOnInk, hotkey: "3");

            ColumnLabel(root, cx, "血条族（凹槽 + ghost 残影 + 主填充）", secondary);
            BuildTeamBarSample(root, At(cx, 330f));
            BuildUnitBarSample(root, At(cx, 268f));
            BuildPipSample(root, At(cx, 200f));

            ColumnLabel(root, cx, "字号档位（UiSkin.Font 唯一真值）", secondary, 140f);
            FontRow(root, cx - 250f, 92f, "Display " + UiSkin.Font.Display, UiSkin.Font.Display, title);
            FontRow(root, cx - 250f, 52f, "Title " + UiSkin.Font.Title + " 界面标题", UiSkin.Font.Title, title);
            FontRow(root, cx - 250f, 20f, "Section " + UiSkin.Font.Section + " 区块标题", UiSkin.Font.Section, body);
            FontRow(root, cx - 250f, -12f, "Body " + UiSkin.Font.Body + " 按钮与行文本", UiSkin.Font.Body, body);
            FontRow(root, cx - 250f, -42f, "Hint " + UiSkin.Font.Hint + " 辅助提示 / Tiny " + UiSkin.Font.Tiny + " 角标", UiSkin.Font.Hint, secondary);

            ColumnLabel(root, rx, "底色与语义色板", secondary);
            string[] names = { "InkDeep 容器底", "InkSoft 嵌件底", "BarTrack 凹槽", "Gold 强调",
                "TeamRed", "TeamBlue", "Danger", "DeadGray" };
            Color[] colors = { UiSkin.InkDeep, UiSkin.InkSoft, UiSkin.BarTrackInk, UiSkin.Gold,
                UiSkin.TeamRed, UiSkin.TeamBlue, UiSkin.Danger, UiSkin.DeadGray };
            for (int i = 0; i < names.Length; i++)
            {
                float x = rx - 220f + (i % 2) * 230f;
                float y = 330f - (i / 2) * 56f;
                Swatch(root, At(x, y), colors[i], names[i], secondary);
            }

            ColumnLabel(root, rx, "手绘槽位（panel 面板 / btn 按钮 / cell 格 · 0.12s 沸腾）", secondary, 96f);
            SketchImage(root, At(rx - 150f, 40f), "panel", Color.white, "面板", secondary);
            SketchImage(root, At(rx + 30f, 40f), "btn_normal", Color.white, "按钮", secondary);
            SketchImage(root, At(rx + 190f, 40f), "cell", UiSkin.TeamRed, "彩格(tint)", secondary);

            Footer(root, secondary);
        }

        static void BuildTeamBarSample(RectTransform root, Vector2 center)
        {
            // 合成队条：4 段（不同存量）+ 段缝露凹槽——顶部血条的等比样例。
            const float width = 520f, height = 26f, gap = 4f;
            RectTransform bar = UiKit.CreateRect("SampleTeamBar", root);
            bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(0.5f, 0.5f);
            bar.anchoredPosition = center;
            bar.sizeDelta = new Vector2(width, height);
            var track = bar.gameObject.AddComponent<Image>();
            track.sprite = SketchSkin.Frame("progress_bg", 0);
            track.type = Image.Type.Sliced;
            track.raycastTarget = false;
            var trackBoil = bar.gameObject.AddComponent<SketchBoil>();
            trackBoil.Slot = "progress_bg";

            float segW = (width - 4f - 3f * gap) / 4f;
            float[] fills = { 1f, 0.62f, 0.34f, 0.85f };
            for (int i = 0; i < 4; i++)
            {
                RectTransform seg = UiKit.CreateRect("Seg" + i, bar);
                seg.anchorMin = seg.anchorMax = seg.pivot = new Vector2(0f, 0.5f);
                seg.sizeDelta = new Vector2(segW, height - 6f);
                seg.anchoredPosition = new Vector2(2f + i * (segW + gap), 0f);

                Image ghost = UiKit.CreateTinted("Ghost", seg, CartoonSpriteFactory.Shape.Pill,
                    UiSkin.DamageGhost);
                UiKit.Stretch(ghost.rectTransform);
                SetFill(ghost, 1f);
                Image fill = UiKit.CreateTinted("Fill", seg, CartoonSpriteFactory.Shape.Pill,
                    UiSkin.TeamRed);
                UiKit.Stretch(fill.rectTransform);
                SetFill(fill, fills[i]);
            }
        }

        static void BuildUnitBarSample(RectTransform root, Vector2 center)
        {
            // 静态陈列 damage ghost 语义：白残影 0.62 领先、主填充 0.45 落后。
            UiKit.BarView bar = UiKit.CreateBar("SampleUnitBar", root, center,
                new Vector2(280f, 16f), UiSkin.TeamBlue);
            SetFill(bar.Ghost, 0.62f);
            SetFill(bar.Fill, 0.45f);
        }

        static void BuildPipSample(RectTransform root, Vector2 center)
        {
            string[] keys = { "sailor", "gunner", "sniper", "hooker", "arsonist", "captain" };
            float step = 38f, x0 = center.x - (keys.Length * step - 8f) / 2f;
            for (int i = 0; i < keys.Length; i++)
            {
                RectTransform pip = UiKit.CreateRect("Pip" + keys[i], root);
                pip.anchorMin = pip.anchorMax = pip.pivot = new Vector2(0.5f, 0.5f);
                pip.sizeDelta = new Vector2(32f, 32f);
                pip.anchoredPosition = new Vector2(x0 + i * step, center.y);

                var frame = pip.gameObject.AddComponent<Image>();
                frame.sprite = SketchSkin.Frame("cell", 0);
                frame.type = Image.Type.Sliced;
                frame.color = UiSkin.CellBase(UiSkin.CrewColor(keys[i]));
                frame.raycastTarget = false;
                var pipBoil = pip.gameObject.AddComponent<SketchBoil>();
                pipBoil.Slot = "cell";

                Image icon = UiKit.CreateRect("Icon", pip).gameObject.AddComponent<Image>();
                icon.sprite = LoadCrewIcon(keys[i]);
                icon.type = Image.Type.Simple;
                icon.raycastTarget = false;
                UiKit.Stretch(icon.rectTransform, 3f);
            }
            // 死亡态 pip：灰底骷髅。
            RectTransform dead = UiKit.CreateRect("PipDead", root);
            dead.anchorMin = dead.anchorMax = dead.pivot = new Vector2(0.5f, 0.5f);
            dead.sizeDelta = new Vector2(32f, 32f);
            dead.anchoredPosition = new Vector2(x0 + keys.Length * step, center.y);
            var deadFrame = dead.gameObject.AddComponent<Image>();
            deadFrame.sprite = SketchSkin.Frame("cell", 0);
            deadFrame.type = Image.Type.Sliced;
            deadFrame.color = UiSkin.DeadGray;
            deadFrame.raycastTarget = false;
            var deadBoil = dead.gameObject.AddComponent<SketchBoil>();
            deadBoil.Slot = "cell";
            UiKit.CreateGlyph("Skull", dead, UiGlyphs.Glyph.Skull, UiSkin.InkDeep);
        }

        // ------------------------------------------------------------------
        // 图标集页
        // ------------------------------------------------------------------

        static void BuildIconsPage(RectTransform root, TMP_FontAsset body,
            TMP_FontAsset secondary, TMP_FontAsset title)
        {
            PageTitle(root, "UI 图标集 · 武器 17 / 职业 7 / 符号 13", title);

            // ---- 武器：格底 = UiSkin.WeaponColor，静物 = Resources/UIIcons ----
            ColumnLabel(root, 0f, "武器（格底与静物同色系；选中金框在战斗 HUD 内体现）", secondary, 380f);
            WeaponId[] weapons = (WeaponId[])System.Enum.GetValues(typeof(WeaponId));
            float cell = 66f, gap = 8f;
            int columns = 9;
            for (int i = 0; i < weapons.Length; i++)
            {
                int column = i % columns, row = i / columns;
                Vector2 center = new Vector2(
                    -330f + column * (cell + gap),
                    322f - row * (cell + 34f));
                WeaponCell(root, weapons[i], center, cell, secondary);
            }

            // ---- 职业：头像 + 名 ----
            ColumnLabel(root, 0f, "职业（两件式底 + 职业标记）", secondary, 150f);
            string[] crew = { "sailor", "gunner", "sniper", "hooker", "arsonist", "skeleton", "captain" };
            string[] crewNames = { "水手", "炮手", "狙击手", "钩子手", "纵火狂", "骷髅", "船长" };
            float step = 96f, x0 = -(crew.Length - 1) * step * 0.5f;
            for (int i = 0; i < crew.Length; i++)
            {
                Vector2 center = new Vector2(x0 + i * step, 84f);
                RectTransform portrait = UiKit.CreateRect("Crew_" + crew[i], root);
                portrait.anchorMin = portrait.anchorMax = portrait.pivot = new Vector2(0.5f, 0.5f);
                portrait.sizeDelta = new Vector2(52f, 52f);
                portrait.anchoredPosition = center;

                var ring = portrait.gameObject.AddComponent<Image>();
                ring.sprite = SketchSkin.Frame("cell", 0);
                ring.type = Image.Type.Sliced;
                ring.color = UiSkin.CrewColor(crew[i]);
                ring.raycastTarget = false;
                var ringBoil = portrait.gameObject.AddComponent<SketchBoil>();
                ringBoil.Slot = "cell";

                Image face = UiKit.CreateRect("Icon", portrait).gameObject.AddComponent<Image>();
                face.sprite = LoadCrewIcon(crew[i]);
                face.type = Image.Type.Simple;
                face.raycastTarget = false;
                UiKit.Stretch(face.rectTransform, 4f);

                Label(root, At(center.x, center.y - 40f), crewNames[i], UiSkin.Font.Hint,
                    UiSkin.TextOnInk, secondary);
            }

            // ---- 符号：UiGlyphs 平涂 ----
            ColumnLabel(root, 0f, "符号（代码平涂；深底暖白 / 金底深墨两用）", secondary, 6f);
            UiGlyphs.Glyph[] glyphs =
            {
                UiGlyphs.Glyph.Crosshair, UiGlyphs.Glyph.Eye, UiGlyphs.Glyph.MovePad,
                UiGlyphs.Glyph.Pause, UiGlyphs.Glyph.Play, UiGlyphs.Glyph.Check,
                UiGlyphs.Glyph.Cross, UiGlyphs.Glyph.Skull, UiGlyphs.Glyph.Star,
                UiGlyphs.Glyph.Retry, UiGlyphs.Glyph.Helm, UiGlyphs.Glyph.Flag,
                UiGlyphs.Glyph.ThrowArc,
            };
            float gstep = 62f, gx0 = -(glyphs.Length - 1) * gstep * 0.5f;
            for (int i = 0; i < glyphs.Length; i++)
            {
                Image glyph = UiKit.CreateGlyph("Glyph_" + glyphs[i], root, glyphs[i],
                    i % 2 == 0 ? UiSkin.TextOnInk : UiSkin.Gold);
                glyph.rectTransform.sizeDelta = new Vector2(34f, 34f);
                glyph.rectTransform.anchorMin = glyph.rectTransform.anchorMax =
                    glyph.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                glyph.rectTransform.anchoredPosition = new Vector2(gx0 + i * gstep, -58f);
            }

            Footer(root, secondary);
        }

        static void WeaponCell(RectTransform root, WeaponId id, Vector2 center, float cell,
            TMP_FontAsset secondary)
        {
            RectTransform rect = UiKit.CreateRect("Weapon_" + id, root);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(cell, cell);
            rect.anchoredPosition = center;

            var frame = rect.gameObject.AddComponent<Image>();
            frame.sprite = SketchSkin.Frame("cell", 0);
            frame.type = Image.Type.Sliced;
            frame.color = UiSkin.WeaponCellBase(id);
            frame.raycastTarget = false;
            var weaponBoil = rect.gameObject.AddComponent<SketchBoil>();
            weaponBoil.Slot = "cell";

            Image icon = UiKit.CreateRect("Icon", rect).gameObject.AddComponent<Image>();
            icon.sprite = LoadWeaponIcon(id);
            icon.type = Image.Type.Simple;
            icon.raycastTarget = false;
            UiKit.Stretch(icon.rectTransform, 8f);

            Label(root, At(center.x, center.y - cell * 0.5f - 12f), WeaponShortName(id),
                UiSkin.Font.Tiny, UiSkin.TextDim, secondary);
        }

        static string WeaponShortName(WeaponId id)
        {
            string name = id.ToString();
            var map = new Dictionary<string, string>
            {
                { "Cannonball", "铁球" }, { "CherryBomb", "樱桃弹" }, { "Dynamite", "炸药" },
                { "Boulder", "巨石" }, { "Banana", "香蕉" }, { "Mine", "水雷" },
                { "ParachuteBomb", "伞弹" }, { "RumBottle", "朗姆瓶" },
                { "PiecesOfEight", "金币" }, { "GunpowderBarrel", "火药桶" },
                { "WoodenCrate", "木箱" }, { "Anchor", "铁锚" }, { "Seagull", "海鸥" },
                { "TidalWave", "潮浪" }, { "VoodooDoll", "巫毒娃娃" }, { "Cannon", "大炮" },
                { "SweepingFlame", "烈焰" },
            };
            return map.TryGetValue(name, out string zh) ? zh : name;
        }

        // ------------------------------------------------------------------
        // 小工具
        // ------------------------------------------------------------------

        static Vector2 At(float x, float y) => new Vector2(x, y);

        static void PageTitle(RectTransform root, string content, TMP_FontAsset title)
        {
            TextMeshProUGUI text = UiKit.CreateText("PageTitle", root, content, UiSkin.Font.Title,
                TextAlignmentOptions.Center, UiSkin.Gold, title);
            text.enableWordWrapping = false;
            text.rectTransform.anchorMin = text.rectTransform.anchorMax =
                text.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            text.rectTransform.sizeDelta = new Vector2(1200f, 48f);
            text.rectTransform.anchoredPosition = new Vector2(0f, 470f);
        }

        /// <summary>左对齐标签（x = 画布中心系左端点；anchor 0.5,0.5 + pivot 左中）。
        /// 列标题 / 字号样例行 / 色板名共用——r9 出图裁决的教训：anchor(0,·) 左缘锚
        /// 与中心系坐标混用会把内容摆出屏幕（列标题被裁、字号行整列缺失）。</summary>
        static void LeftLabel(RectTransform root, float x, float y, float width, string content,
            int size, Color color, TMP_FontAsset font)
        {
            TextMeshProUGUI text = UiKit.CreateText("Label", root, content, size,
                TextAlignmentOptions.MidlineLeft, color, font);
            text.enableWordWrapping = false;
            text.rectTransform.anchorMin = text.rectTransform.anchorMax =
                new Vector2(0.5f, 0.5f);
            text.rectTransform.pivot = new Vector2(0f, 0.5f);
            text.rectTransform.sizeDelta = new Vector2(width, size + 10f);
            text.rectTransform.anchoredPosition = new Vector2(x, y);
        }

        static void ColumnLabel(RectTransform root, float x, string content,
            TMP_FontAsset secondary, float y = 388f)
        {
            LeftLabel(root, x - 280f, y, 560f, content, UiSkin.Font.Section, UiSkin.TextDim, secondary);
        }

        static void FontRow(RectTransform root, float x, float y, string content, int size,
            TMP_FontAsset font)
        {
            LeftLabel(root, x, y, 560f, content, size, UiSkin.TextOnInk, font);
        }

        static void Swatch(RectTransform root, Vector2 center, Color color, string name,
            TMP_FontAsset secondary)
        {
            // 色板用不透明 cell 槽（btn 槽白 7% 透明底乘色后颜色不可辨）；
            // 直接挂页面根（r12 事故：把本方法自建的空容器当 root 传下去，子块坐标叠算成 2×center）。
            SketchImage(root, center, "cell", color, null, null, new Vector2(104f, 44f));

            // 色板名放色块右侧（r9：居中 160 宽盒左端压进色块内一半）。
            LeftLabel(root, center.x + 58f, center.y, 120f, name, UiSkin.Font.Tiny,
                UiSkin.TextOnInk, secondary);
        }

        /// <summary>建一个手绘槽样块（实底槽乘色 / 固定槽传 white），挂沸腾。</summary>
        static void SketchImage(RectTransform root, Vector2 center, string slot, Color color,
            string name, TMP_FontAsset secondary, Vector2? size = null)
        {
            RectTransform rect = UiKit.CreateRect("Sketch_" + slot, root);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size ?? new Vector2(120f, 64f);
            rect.anchoredPosition = center;
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = SketchSkin.Frame(slot, 0);
            image.type = Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = false;
            var boil = rect.gameObject.AddComponent<SketchBoil>();
            boil.Slot = slot;

            if (name != null)
                Label(root, At(center.x, center.y - 46f), name, UiSkin.Font.Tiny,
                    UiSkin.TextDim, secondary);
        }

        static void Label(RectTransform root, Vector2 position, string content, int size,
            Color color, TMP_FontAsset font)
        {
            TextMeshProUGUI text = UiKit.CreateText("Label", root, content, size,
                TextAlignmentOptions.Center, color, font);
            text.enableWordWrapping = false;
            text.rectTransform.anchorMin = text.rectTransform.anchorMax =
                text.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            text.rectTransform.sizeDelta = new Vector2(160f, size + 8f);
            text.rectTransform.anchoredPosition = position;
        }

        static void Footer(RectTransform root, TMP_FontAsset secondary)
        {
            TextMeshProUGUI text = UiKit.CreateText("Footer", root,
                "颜色 / 字号 / 圆角全部出自 UiSkin Token——改一处常量，本页与全部界面同步变化（视觉回归自检页）",
                UiSkin.Font.Hint, TextAlignmentOptions.Center, UiSkin.TextDim, secondary);
            text.enableWordWrapping = false;
            text.rectTransform.anchorMin = text.rectTransform.anchorMax =
                text.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            text.rectTransform.sizeDelta = new Vector2(1400f, 24f);
            text.rectTransform.anchoredPosition = new Vector2(0f, -496f);
        }

        /// <summary>血条填充比例（anchorMax.x 表达，与 UiMotion 同口径）。</summary>
        static void SetFill(Image fill, float ratio)
        {
            RectTransform rect = fill.rectTransform;
            rect.anchorMax = new Vector2(Mathf.Clamp01(ratio), 1f);
            rect.offsetMax = Vector2.zero;
        }

        static Sprite LoadWeaponIcon(WeaponId id)
        {
            Sprite sprite = Resources.Load<Sprite>("UIIcons/Weapon_" + id);
            return sprite != null ? sprite : UiGlyphs.Get(UiGlyphs.Glyph.ThrowArc);
        }

        static Sprite LoadCrewIcon(string crewKey)
        {
            Sprite sprite = Resources.Load<Sprite>("UIIcons/Crew_" + crewKey);
            return sprite != null ? sprite : UiGlyphs.Get(UiGlyphs.Glyph.Helm);
        }
    }
}
